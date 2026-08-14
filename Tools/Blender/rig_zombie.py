#!/usr/bin/env python3
"""Rig and animate an approved Meshcaster zombie mesh for Unity.

    blender --background --python Tools/Blender/rig_zombie.py -- --slot Enemy_Shambler
    blender --background --python Tools/Blender/rig_zombie.py -- --all
    blender --background --python Tools/Blender/rig_zombie.py -- --self-test --output /tmp/rig

What it does, per slot:

  import -> normalise -> armature -> skin -> five original clips -> FBX + manifest

Nothing here spends credits, touches a network, or knows what Meshy is. It
reads a mesh file a human already generated, approved and copied in. With no
input file present it fails loudly and writes nothing -- an empty slot must
never look like a successful rig.

The five clips are authored here from bone keyframes, not copied from any
motion library: each one is a handful of sine and ease curves over the rig this
script just built, scaled by the model's own measured proportions.

Run with --self-test to exercise the whole pipeline on a proxy humanoid this
script builds from primitives. That output is stamped
``"source": "self-test-proxy"`` in its manifest and the Unity importer refuses
to ship it as approved art.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import sys
import traceback

import bpy
import bmesh
from mathutils import Vector

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------

PIPELINE_VERSION = 1

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))

STAGING_DIR = os.path.join(REPO_ROOT, "Assets", "Ashfall", "Art", "Meshcaster")

#: Where ``AshfallZombieRig.ExportSlotSource`` drops a slot's mesh for Blender.
SOURCE_DIR = os.path.join(SCRIPT_DIR, "Input")

#: Sub-folder of a staging slot that holds this script's output.
RIGGED_SUBFOLDER = "Rigged"

#: Target height in metres per slot. These are the numbers ``EnemyDefinition``
#: builds hitboxes from, so the rig has to land on them exactly.
SLOTS = {
    "Enemy_Shambler": 1.85,
    "Enemy_Sprinter": 1.68,
    "Enemy_StormBrute": 2.85,
}

#: Preference order when a slot folder holds more than one candidate. FBX first
#: because it survives a round trip with materials; OBJ last because it is
#: geometry only.
EXTENSION_PRIORITY = (".fbx", ".glb", ".gltf", ".obj")

FPS = 30

#: (name, first frame, last frame, loops). Unity reads these back out of the
#: manifest, so the names are the contract with ``ZombieAnimator``.
CLIPS = (
    ("Idle", 1, 61, True),
    ("Walk", 1, 41, True),
    ("Attack", 1, 30, False),
    ("HitReact", 1, 18, False),
    ("Death", 1, 45, False),
)

DEFORM_BONES = (
    "Pelvis", "Spine", "Chest", "Neck", "Head",
    "Shoulder.L", "UpperArm.L", "LowerArm.L", "Hand.L",
    "Shoulder.R", "UpperArm.R", "LowerArm.R", "Hand.R",
    "UpperLeg.L", "LowerLeg.L", "Foot.L", "Toe.L",
    "UpperLeg.R", "LowerLeg.R", "Foot.R", "Toe.R",
)

ALL_BONES = ("Root",) + DEFORM_BONES


class RigError(Exception):
    """A condition that must stop the run rather than produce a bad rig."""


def log(message: str) -> None:
    print(f"[rig] {message}", flush=True)


# ---------------------------------------------------------------------------
# Scene helpers
# ---------------------------------------------------------------------------

def reset_scene() -> None:
    """Empty .blend state, so two runs of this script cannot differ."""
    bpy.ops.wm.read_factory_settings(use_empty=True)

    for collection in (
        bpy.data.objects, bpy.data.meshes, bpy.data.armatures,
        bpy.data.actions, bpy.data.materials, bpy.data.images,
    ):
        for item in list(collection):
            collection.remove(item, do_unlink=True)

    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.render.fps_base = 1.0
    scene.frame_start = 1
    scene.frame_end = max(end for _, _, end, _ in CLIPS)


def activate(obj: bpy.types.Object) -> None:
    for other in bpy.context.view_layer.objects:
        other.select_set(False)

    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


# ---------------------------------------------------------------------------
# Input resolution
# ---------------------------------------------------------------------------

def find_source(slot: str, input_path: str | None) -> str:
    """The mesh file for a slot, or raise.

    Accepts a direct file, a slot folder, or a parent folder holding one folder
    per slot. Never guesses across slots: an ``Enemy_Sprinter`` run will not
    silently rig the shambler that happens to be next to it.
    """
    if input_path and os.path.isfile(input_path):
        return input_path

    roots = []
    if input_path:
        roots.extend([os.path.join(input_path, slot), input_path])
    else:
        roots.extend([
            os.path.join(SOURCE_DIR, slot),
            os.path.join(STAGING_DIR, slot, "Source"),
        ])

    searched = []
    for root in roots:
        if not os.path.isdir(root):
            searched.append(f"{root} (no such directory)")
            continue

        searched.append(root)
        candidates = []
        for directory, _, filenames in os.walk(root):
            # This script's own output must never become its own input.
            if os.path.basename(directory) == RIGGED_SUBFOLDER:
                continue

            for filename in filenames:
                extension = os.path.splitext(filename)[1].lower()
                if extension in EXTENSION_PRIORITY:
                    candidates.append((
                        EXTENSION_PRIORITY.index(extension),
                        os.path.join(directory, filename),
                    ))

        if candidates:
            # Sorted, not "first found": os.walk order must not decide which of
            # two approved files gets rigged.
            candidates.sort(key=lambda pair: (pair[0], pair[1]))
            return candidates[0][1]

    raise RigError(
        f"no importable mesh for slot '{slot}'.\n"
        f"        looked in: {', '.join(searched)}\n"
        f"        accepted:  {', '.join(EXTENSION_PRIORITY)}\n"
        f"        This slot has no approved Meshcaster output yet. Generate and\n"
        f"        approve it in the Meshcaster window (a human clicks the priced\n"
        f"        button), copy the result into the slot, then run\n"
        f"        'Ashfall > Meshcaster: Export Slot Source for Blender'."
    )


def sha256(path: str) -> str:
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)

    return digest.hexdigest()


# ---------------------------------------------------------------------------
# Import
# ---------------------------------------------------------------------------

def import_mesh(path: str) -> list:
    """Import one file and return the mesh objects it produced."""
    before = set(bpy.data.objects)
    extension = os.path.splitext(path)[1].lower()

    if extension == ".fbx":
        _try_ops([
            (bpy.ops.import_scene.fbx, dict(filepath=path, automatic_bone_orientation=True)),
            (bpy.ops.import_scene.fbx, dict(filepath=path)),
            (bpy.ops.wm.fbx_import, dict(filepath=path)),
        ], path)
    elif extension in (".glb", ".gltf"):
        _try_ops([
            (bpy.ops.import_scene.gltf, dict(filepath=path)),
        ], path)
    elif extension == ".obj":
        _try_ops([
            (bpy.ops.wm.obj_import, dict(filepath=path, forward_axis="NEGATIVE_Z", up_axis="Y")),
            (bpy.ops.wm.obj_import, dict(filepath=path)),
            (bpy.ops.import_scene.obj, dict(filepath=path)),
        ], path)
    else:
        raise RigError(f"unsupported input format '{extension}' ({path})")

    created = [obj for obj in bpy.data.objects if obj not in before]
    meshes = [obj for obj in created if obj.type == "MESH"]

    if not meshes:
        raise RigError(f"'{path}' imported without producing any mesh object")

    # Meshy output is a static mesh; an armature riding along would fight the
    # one this script is about to build.
    for obj in created:
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    return [obj for obj in meshes if obj.name in bpy.data.objects]


def _try_ops(attempts: list, path: str) -> None:
    """Run the first import operator whose signature this Blender accepts."""
    errors = []
    for operator, kwargs in attempts:
        if operator is None:
            continue

        try:
            operator(**kwargs)
            return
        except TypeError as exc:
            errors.append(f"{exc}")
            continue
        except RuntimeError as exc:
            errors.append(f"{exc}")
            continue

    raise RigError(f"could not import '{path}': " + "; ".join(errors))


def consolidate(meshes: list, name: str) -> bpy.types.Object:
    """One object, one mesh. Keeps every material slot the import created."""
    for obj in meshes:
        activate(obj)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    if len(meshes) > 1:
        activate(meshes[0])
        for obj in meshes[1:]:
            obj.select_set(True)

        bpy.ops.object.join()

    body = bpy.context.view_layer.objects.active
    body.name = name
    body.data.name = f"{name}_Mesh"
    return body


# ---------------------------------------------------------------------------
# Normalisation
# ---------------------------------------------------------------------------

def measure(body: bpy.types.Object) -> tuple:
    coords = [v.co for v in body.data.vertices]
    if not coords:
        raise RigError(f"'{body.name}' has no vertices")

    low = Vector((min(c.x for c in coords), min(c.y for c in coords), min(c.z for c in coords)))
    high = Vector((max(c.x for c in coords), max(c.y for c in coords), max(c.z for c in coords)))
    return low, high


def normalise(body: bpy.types.Object, target_height: float, yaw_degrees: float, force: bool) -> None:
    """Upright, centred, sitting on Z=0, and exactly ``target_height`` tall.

    Blender is Z-up; every importer this script uses converts a Y-up source
    into that. A model whose tallest axis is not Z arrived lying down, and a
    vertical armature dropped into it produces a rig that looks plausible in
    the log and garbage in the game -- so that stops the run unless the caller
    insists.
    """
    if abs(yaw_degrees) > 1e-6:
        angle = math.radians(yaw_degrees)
        cos_a, sin_a = math.cos(angle), math.sin(angle)
        for vertex in body.data.vertices:
            x, y = vertex.co.x, vertex.co.y
            vertex.co.x = x * cos_a - y * sin_a
            vertex.co.y = x * sin_a + y * cos_a

    low, high = measure(body)
    size = high - low

    if size.z < max(size.x, size.y) and not force:
        raise RigError(
            f"'{body.name}' is {size.x:.2f} x {size.y:.2f} x {size.z:.2f} m -- it is not "
            f"standing up in Blender's Z-up space.\n"
            f"        Re-export the source upright, or re-run with --force to rig it as-is."
        )

    if size.z < 1e-5:
        raise RigError(f"'{body.name}' has no height to scale")

    scale = target_height / size.z
    for vertex in body.data.vertices:
        vertex.co *= scale

    low, high = measure(body)
    offset = Vector((-(low.x + high.x) * 0.5, -(low.y + high.y) * 0.5, -low.z))
    for vertex in body.data.vertices:
        vertex.co += offset

    body.location = (0.0, 0.0, 0.0)
    body.rotation_euler = (0.0, 0.0, 0.0)
    body.scale = (1.0, 1.0, 1.0)

    log(f"normalised: scaled by {scale:.4f} to {target_height:.3f} m, origin at floor centre")


def clamp(value: float, low: float, high: float) -> float:
    return max(low, min(high, value))


def half_width(body: bpy.types.Object, low_fraction: float, high_fraction: float, height: float) -> float:
    """Robust |x| half-width of the band between two height fractions.

    A 95th percentile rather than a max: one stray vertex on a rotted sleeve
    should not decide where the shoulder joint goes.
    """
    low_z = low_fraction * height
    high_z = high_fraction * height
    widths = sorted(abs(v.co.x) for v in body.data.vertices if low_z <= v.co.z <= high_z)

    if not widths:
        return 0.0

    return widths[min(len(widths) - 1, int(len(widths) * 0.95))]


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

def build_armature(body: bpy.types.Object, height: float, name: str) -> bpy.types.Object:
    """A 22-bone generic humanoid fitted to this mesh's own measurements.

    The height fractions are a standing-humanoid proportion table; the widths
    are measured off the mesh, so a brute gets brute shoulders and a sprinter
    does not get its arms rigged inside its ribs.
    """
    # Measured, then clamped to humanoid plausibility. The clamp is not
    # paranoia: a shambler's arms hang past its knees, so the hip band is full
    # of forearm, and an unclamped measurement would put the hip joints out
    # where the hands are.
    shoulder_hw = clamp(half_width(body, 0.74, 0.84, height), 0.10 * height, 0.30 * height)
    hip_hw = clamp(half_width(body, 0.46, 0.56, height) * 0.5,
                   0.05 * height, min(0.14 * height, shoulder_hw * 0.8))
    arm_hw = shoulder_hw * 1.05

    def point(x, y, z):
        return Vector((x, y * height, z * height))

    # name: (head, tail, parent, connected)
    layout = {
        "Root":        (point(0, 0, 0.0),   point(0, 0, 0.06),  None,      False),
        "Pelvis":      (point(0, 0, 0.53),  point(0, 0, 0.60),  "Root",    False),
        "Spine":       (point(0, 0, 0.60),  point(0, 0, 0.70),  "Pelvis",  True),
        "Chest":       (point(0, 0, 0.70),  point(0, 0, 0.81),  "Spine",   True),
        "Neck":        (point(0, 0, 0.81),  point(0, 0, 0.87),  "Chest",   True),
        "Head":        (point(0, 0, 0.87),  point(0, 0, 0.98),  "Neck",    True),
    }

    for side, sign in (("L", 1.0), ("R", -1.0)):
        shoulder_x = sign * shoulder_hw
        arm_x = sign * arm_hw
        leg_x = sign * hip_hw

        layout[f"Shoulder.{side}"] = (
            Vector((sign * shoulder_hw * 0.18, 0.0, 0.805 * height)),
            Vector((shoulder_x * 0.80, 0.0, 0.790 * height)),
            "Chest", False)
        layout[f"UpperArm.{side}"] = (
            Vector((shoulder_x * 0.80, 0.0, 0.790 * height)),
            Vector((arm_x * 0.95, 0.0, 0.620 * height)),
            f"Shoulder.{side}", True)
        layout[f"LowerArm.{side}"] = (
            Vector((arm_x * 0.95, 0.0, 0.620 * height)),
            Vector((arm_x, 0.0, 0.460 * height)),
            f"UpperArm.{side}", True)
        layout[f"Hand.{side}"] = (
            Vector((arm_x, 0.0, 0.460 * height)),
            Vector((arm_x, 0.0, 0.395 * height)),
            f"LowerArm.{side}", True)

        layout[f"UpperLeg.{side}"] = (
            Vector((leg_x, 0.0, 0.520 * height)),
            Vector((leg_x, 0.0, 0.280 * height)),
            "Pelvis", False)
        layout[f"LowerLeg.{side}"] = (
            Vector((leg_x, 0.0, 0.280 * height)),
            Vector((leg_x, 0.0, 0.055 * height)),
            f"UpperLeg.{side}", True)
        layout[f"Foot.{side}"] = (
            Vector((leg_x, 0.0, 0.055 * height)),
            Vector((leg_x, -0.085 * height, 0.012 * height)),
            f"LowerLeg.{side}", True)
        layout[f"Toe.{side}"] = (
            Vector((leg_x, -0.085 * height, 0.012 * height)),
            Vector((leg_x, -0.145 * height, 0.010 * height)),
            f"Foot.{side}", True)

    armature_data = bpy.data.armatures.new(f"{name}_Armature")
    rig = bpy.data.objects.new(f"{name}_Rig", armature_data)
    bpy.context.collection.objects.link(rig)

    activate(rig)
    bpy.ops.object.mode_set(mode="EDIT")

    for bone_name in ALL_BONES:
        head, tail, _, _ = layout[bone_name]
        bone = armature_data.edit_bones.new(bone_name)
        bone.head = head
        bone.tail = tail
        bone.roll = 0.0

    for bone_name in ALL_BONES:
        _, _, parent, connected = layout[bone_name]
        if parent is None:
            continue

        bone = armature_data.edit_bones[bone_name]
        bone.parent = armature_data.edit_bones[parent]
        bone.use_connect = connected

    bpy.ops.object.mode_set(mode="OBJECT")

    # Root is a handle for the exporter and the Unity transform, not a deform
    # bone: weighting to it would drag the whole mesh with any root motion.
    for bone in rig.data.bones:
        bone.use_deform = bone.name != "Root"

    for pose_bone in rig.pose.bones:
        pose_bone.rotation_mode = "XYZ"

    log(f"armature: {len(ALL_BONES)} bones, shoulder half-width {shoulder_hw:.3f} m, "
        f"hip half-width {hip_hw:.3f} m")
    return rig


# ---------------------------------------------------------------------------
# Skinning
# ---------------------------------------------------------------------------

def skin(body: bpy.types.Object, rig: bpy.types.Object) -> str:
    """Bind mesh to rig. Returns the method that actually worked.

    Automatic weights (bone heat) is the good answer and is tried first. It
    needs manifold, watertight-ish geometry, which generated meshes frequently
    are not: heat fails on loose parts, interior shells and zero-area faces.
    Rather than let a half-weighted mesh through, the result is checked and a
    deterministic envelope fallback takes over when it is not good enough.
    """
    for modifier in [m for m in body.modifiers if m.type == "ARMATURE"]:
        body.modifiers.remove(modifier)

    body.vertex_groups.clear()
    body.parent = None

    activate(body)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
        weighted = weighted_fraction(body)
        if weighted >= 0.98:
            log(f"skinning: automatic weights, {weighted * 100:.1f}% of vertices bound")
            return "automatic"

        log(f"skinning: automatic weights left {(1 - weighted) * 100:.1f}% of vertices "
            f"unbound; falling back to envelopes")
    except RuntimeError as exc:
        log(f"skinning: automatic weights failed ({exc}); falling back to envelopes")

    return envelope_skin(body, rig)


def weighted_fraction(body: bpy.types.Object) -> float:
    deform = {group.index for group in body.vertex_groups if group.name in DEFORM_BONES}
    if not deform:
        return 0.0

    bound = sum(
        1 for vertex in body.data.vertices
        if any(g.group in deform and g.weight > 1e-4 for g in vertex.groups)
    )
    return bound / max(1, len(body.data.vertices))


def envelope_skin(body: bpy.types.Object, rig: bpy.types.Object) -> str:
    """Deterministic inverse-distance envelope weighting.

    For every vertex: distance to each deform bone's segment, keep the four
    nearest, weight by 1/(d^3 + eps), normalise. It cannot fail on bad topology
    because it never looks at topology -- and at the distances this game shows
    enemies at, the difference from heat weighting is not readable. Recorded in
    the manifest so nobody has to guess which path ran.
    """
    for modifier in [m for m in body.modifiers if m.type == "ARMATURE"]:
        body.modifiers.remove(modifier)

    body.vertex_groups.clear()

    segments = []
    for bone in rig.data.bones:
        if bone.use_deform:
            segments.append((bone.name, bone.head_local.copy(), bone.tail_local.copy()))

    groups = {name: body.vertex_groups.new(name=name) for name, _, _ in segments}

    influences = 4
    epsilon = 1e-6
    for vertex in body.data.vertices:
        distances = []
        for name, head, tail in segments:
            distances.append((point_segment_distance(vertex.co, head, tail), name))

        distances.sort(key=lambda pair: (pair[0], pair[1]))
        nearest = distances[:influences]

        weights = [(name, 1.0 / (distance ** 3 + epsilon)) for distance, name in nearest]
        total = sum(weight for _, weight in weights)
        if total <= 0.0:
            weights = [(nearest[0][1], 1.0)]
            total = 1.0

        for name, weight in weights:
            groups[name].add([vertex.index], weight / total, "REPLACE")

    body.parent = rig
    body.matrix_parent_inverse = rig.matrix_world.inverted()
    modifier = body.modifiers.new(name="Armature", type="ARMATURE")
    modifier.object = rig

    log(f"skinning: envelope fallback, {len(segments)} deform bones, "
        f"{influences} influences per vertex")
    return "envelope-fallback"


def point_segment_distance(point: Vector, head: Vector, tail: Vector) -> float:
    axis = tail - head
    length_squared = axis.length_squared
    if length_squared < 1e-12:
        return (point - head).length

    t = max(0.0, min(1.0, (point - head).dot(axis) / length_squared))
    return (point - (head + axis * t)).length


# ---------------------------------------------------------------------------
# Animation
# ---------------------------------------------------------------------------

def ease(t: float) -> float:
    """Smoothstep. Keeps a pose change from starting and stopping with a jolt."""
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


class Poser:
    """Collects bone poses per frame into one action."""

    def __init__(self, rig: bpy.types.Object, name: str):
        self.rig = rig
        self.action = bpy.data.actions.new(name=name)
        self.action.use_fake_user = True

        if rig.animation_data is None:
            rig.animation_data_create()

        rig.animation_data.action = self.action

    def key(self, frame: int, poses: dict) -> None:
        """``poses``: bone name -> (rx, ry, rz) degrees, or a 6-tuple with a
        translation in metres appended for the two bones allowed to move."""
        for bone in self.rig.pose.bones:
            bone.rotation_euler = (0.0, 0.0, 0.0)
            bone.location = (0.0, 0.0, 0.0)

        for bone_name, values in poses.items():
            bone = self.rig.pose.bones[bone_name]
            bone.rotation_euler = tuple(math.radians(v) for v in values[:3])
            if len(values) > 3:
                bone.location = tuple(values[3:6])

        for bone in self.rig.pose.bones:
            bone.keyframe_insert(data_path="rotation_euler", frame=frame)
            if bone.name in ("Root", "Pelvis"):
                bone.keyframe_insert(data_path="location", frame=frame)

    def finish(self) -> bpy.types.Action:
        for curve in iter_fcurves(self.action):
            for keyframe in curve.keyframe_points:
                keyframe.interpolation = "BEZIER"
                keyframe.handle_left_type = "AUTO_CLAMPED"
                keyframe.handle_right_type = "AUTO_CLAMPED"

        self.rig.animation_data.action = None
        return self.action


def iter_fcurves(action: bpy.types.Action):
    """Every f-curve in an action, on old and slotted-action Blenders.

    Blender 4.4 moved curves from ``action.fcurves`` into layer/strip
    channelbags and 5.x removed the old attribute. Reaching through both keeps
    this script running on whatever the artist has installed.
    """
    legacy = getattr(action, "fcurves", None)
    if legacy is not None:
        yield from legacy
        return

    for layer in action.layers:
        for strip in layer.strips:
            for channelbag in getattr(strip, "channelbags", ()):
                yield from channelbag.fcurves


def build_clips(rig: bpy.types.Object, height: float, lean: float) -> list:
    """The five clips, authored from this rig's own proportions.

    ``lean`` is the archetype's resting forward hunch in degrees, which is what
    separates a shambler's walk from a sprinter's without needing five sets of
    curves per enemy.
    """
    unit = height  # translations are written as a fraction of body height

    actions = []
    actions.append(build_idle(rig, lean, unit))
    actions.append(build_walk(rig, lean, unit))
    actions.append(build_attack(rig, lean, unit))
    actions.append(build_hit_react(rig, lean, unit))
    actions.append(build_death(rig, lean, unit))
    return actions


def build_idle(rig, lean, unit) -> bpy.types.Action:
    """Breathing and a slow weight shift. First and last frame match."""
    poser = Poser(rig, "Idle")
    frames = 60

    for step in range(frames + 1):
        frame = 1 + step
        phase = (step / frames) * math.tau
        breath = math.sin(phase)
        sway = math.sin(phase * 0.5) * 0.5 + math.sin(phase) * 0.5

        poser.key(frame, {
            "Pelvis": (lean * 0.15, 0.0, sway * 1.2, 0.0, 0.0, breath * 0.004 * unit),
            "Spine": (lean * 0.30 + breath * 1.6, 0.0, sway * 0.8),
            "Chest": (lean * 0.35 - breath * 2.2, 0.0, -sway * 0.6),
            "Neck": (-lean * 0.25, 0.0, sway * 0.9),
            "Head": (-lean * 0.35 + breath * 1.4, sway * 2.0, 0.0),
            "Shoulder.L": (0.0, 0.0, -breath * 1.5),
            "Shoulder.R": (0.0, 0.0, breath * 1.5),
            "UpperArm.L": (breath * 2.0, 0.0, -6.0 - sway * 1.5),
            "UpperArm.R": (breath * 2.0, 0.0, 6.0 + sway * 1.5),
            "LowerArm.L": (-14.0 - breath * 2.5, 0.0, 0.0),
            "LowerArm.R": (-14.0 - breath * 2.5, 0.0, 0.0),
            "Hand.L": (-8.0, 0.0, 0.0),
            "Hand.R": (-8.0, 0.0, 0.0),
            "UpperLeg.L": (-lean * 0.20, 0.0, 0.0),
            "UpperLeg.R": (-lean * 0.20, 0.0, 0.0),
            "LowerLeg.L": (lean * 0.10, 0.0, 0.0),
            "LowerLeg.R": (lean * 0.10, 0.0, 0.0),
        })

    return poser.finish()


def build_walk(rig, lean, unit) -> bpy.types.Action:
    """One full gait cycle: two steps, arms counter-swinging, pelvis bobbing.

    No root translation. The CharacterController moves the body in Unity, and a
    clip that also moved it would double the speed and slide the feet.
    """
    poser = Poser(rig, "Walk")
    frames = 40
    stride = 26.0 + lean * 0.6
    swing = 18.0

    for step in range(frames + 1):
        frame = 1 + step
        phase = (step / frames) * math.tau
        left = math.sin(phase)
        right = math.sin(phase + math.pi)
        bob = abs(math.cos(phase)) * 0.018 * unit
        roll = math.sin(phase) * 2.5

        poser.key(frame, {
            "Pelvis": (lean * 0.20, roll * 0.6, -math.sin(phase) * 4.0,
                       0.0, 0.0, bob),
            "Spine": (lean * 0.35, roll * 0.4, math.sin(phase) * 2.5),
            "Chest": (lean * 0.30, -roll * 0.5, math.sin(phase) * 3.5),
            "Neck": (-lean * 0.30, 0.0, -math.sin(phase) * 1.5),
            "Head": (-lean * 0.40, math.sin(phase * 2.0) * 1.5, 0.0),

            "Shoulder.L": (0.0, 0.0, -2.0),
            "Shoulder.R": (0.0, 0.0, 2.0),
            "UpperArm.L": (right * swing, 0.0, -7.0),
            "UpperArm.R": (left * swing, 0.0, 7.0),
            "LowerArm.L": (-18.0 - max(0.0, right) * 12.0, 0.0, 0.0),
            "LowerArm.R": (-18.0 - max(0.0, left) * 12.0, 0.0, 0.0),
            "Hand.L": (-6.0, 0.0, 0.0),
            "Hand.R": (-6.0, 0.0, 0.0),

            "UpperLeg.L": (left * stride - lean * 0.25, 0.0, 0.0),
            "UpperLeg.R": (right * stride - lean * 0.25, 0.0, 0.0),
            "LowerLeg.L": (max(0.0, -left) * 34.0 + 4.0, 0.0, 0.0),
            "LowerLeg.R": (max(0.0, -right) * 34.0 + 4.0, 0.0, 0.0),
            "Foot.L": (-left * 12.0 - 4.0, 0.0, 0.0),
            "Foot.R": (-right * 12.0 - 4.0, 0.0, 0.0),
            "Toe.L": (max(0.0, left) * 10.0, 0.0, 0.0),
            "Toe.R": (max(0.0, right) * 10.0, 0.0, 0.0),
        })

    return poser.finish()


def build_attack(rig, lean, unit) -> bpy.types.Action:
    """Wind up, overhead double swing, recover. Reads at silhouette size.

    Frames 1-11 are the windup, which is what the player has to see in time to
    back out of range; the swing lands on frame 17.
    """
    poser = Poser(rig, "Attack")

    def pose(wind: float, swing: float, settle: float) -> dict:
        reach = wind * 55.0 - swing * 95.0
        return {
            "Pelvis": (lean * 0.15 - wind * 5.0 + swing * 9.0, 0.0, 0.0,
                       0.0, 0.0, (-wind * 0.02 + swing * 0.01) * unit),
            "Spine": (lean * 0.25 - wind * 9.0 + swing * 16.0, 0.0, 0.0),
            "Chest": (lean * 0.20 - wind * 14.0 + swing * 26.0, 0.0, 0.0),
            "Neck": (-lean * 0.20 + wind * 6.0 - swing * 8.0, 0.0, 0.0),
            "Head": (-lean * 0.30 + wind * 10.0 - swing * 16.0, 0.0, 0.0),

            "Shoulder.L": (0.0, 0.0, -wind * 12.0 + swing * 6.0),
            "Shoulder.R": (0.0, 0.0, wind * 12.0 - swing * 6.0),
            "UpperArm.L": (reach, 0.0, -10.0 - wind * 14.0 + swing * 20.0),
            "UpperArm.R": (reach, 0.0, 10.0 + wind * 14.0 - swing * 20.0),
            "LowerArm.L": (-20.0 - wind * 40.0 + swing * 46.0, 0.0, 0.0),
            "LowerArm.R": (-20.0 - wind * 40.0 + swing * 46.0, 0.0, 0.0),
            "Hand.L": (-10.0 - wind * 18.0 + swing * 30.0, 0.0, 0.0),
            "Hand.R": (-10.0 - wind * 18.0 + swing * 30.0, 0.0, 0.0),

            "UpperLeg.L": (-lean * 0.20 + wind * 8.0 - swing * 14.0, 0.0, 0.0),
            "UpperLeg.R": (-lean * 0.20 - wind * 4.0 + swing * 10.0, 0.0, 0.0),
            "LowerLeg.L": (lean * 0.10 + wind * 6.0, 0.0, 0.0),
            "LowerLeg.R": (lean * 0.10 + wind * 10.0 - swing * 6.0, 0.0, 0.0),
            "Foot.L": (-wind * 4.0, 0.0, 0.0),
            "Foot.R": (-wind * 6.0 + swing * 4.0, 0.0, 0.0),
        }

    keys = {
        1: pose(0.0, 0.0, 0.0),
        6: pose(ease(0.55), 0.0, 0.0),
        11: pose(1.0, 0.0, 0.0),
        14: pose(1.0, ease(0.35), 0.0),
        17: pose(0.85, 1.0, 0.0),
        22: pose(0.35, 0.72, ease(0.5)),
        30: pose(0.0, 0.0, 1.0),
    }
    for frame, values in keys.items():
        poser.key(frame, values)

    return poser.finish()


def build_hit_react(rig, lean, unit) -> bpy.types.Action:
    """A short spine-and-head snap backwards, then a settle. 0.6 s.

    Deliberately shorter than the shortest stagger in ``EnemyDefinition`` so it
    never outlives the state that triggered it.
    """
    poser = Poser(rig, "HitReact")

    def pose(hit: float) -> dict:
        return {
            "Pelvis": (lean * 0.15 - hit * 4.0, 0.0, hit * 3.0,
                       0.0, 0.0, -hit * 0.012 * unit),
            "Spine": (lean * 0.30 - hit * 11.0, 0.0, hit * 5.0),
            "Chest": (lean * 0.30 - hit * 17.0, 0.0, hit * 7.0),
            "Neck": (-lean * 0.25 - hit * 9.0, 0.0, -hit * 4.0),
            "Head": (-lean * 0.35 - hit * 15.0, hit * 6.0, 0.0),
            "Shoulder.L": (0.0, 0.0, -hit * 14.0),
            "Shoulder.R": (0.0, 0.0, hit * 10.0),
            "UpperArm.L": (-hit * 22.0, 0.0, -6.0 - hit * 16.0),
            "UpperArm.R": (-hit * 16.0, 0.0, 6.0 + hit * 12.0),
            "LowerArm.L": (-14.0 - hit * 26.0, 0.0, 0.0),
            "LowerArm.R": (-14.0 - hit * 20.0, 0.0, 0.0),
            "UpperLeg.L": (-lean * 0.20 + hit * 9.0, 0.0, 0.0),
            "UpperLeg.R": (-lean * 0.20 + hit * 5.0, 0.0, 0.0),
            "LowerLeg.L": (lean * 0.10 + hit * 12.0, 0.0, 0.0),
            "LowerLeg.R": (lean * 0.10 + hit * 7.0, 0.0, 0.0),
        }

    for frame, hit in ((1, 0.0), (3, 1.0), (7, 0.45), (12, 0.18), (18, 0.0)):
        poser.key(frame, pose(hit))

    return poser.finish()


def build_death(rig, lean, unit) -> bpy.types.Action:
    """Buckle at the knees, fold forward, land face down. 1.5 s.

    Ends with the pelvis at ankle height and the spine flat, so the corpse
    silhouette is a low shape on the floor rather than a kneeling one -- the
    player has to be able to read a cleared lane at a glance.
    """
    poser = Poser(rig, "Death")

    def pose(t: float) -> dict:
        fall = ease(t)
        fold = ease(max(0.0, (t - 0.18) / 0.82))
        return {
            "Pelvis": (lean * 0.15 + fold * 62.0, 0.0, fall * 6.0,
                       0.0, -fold * 0.10 * unit, -fall * 0.42 * unit),
            "Spine": (lean * 0.30 + fold * 34.0, fall * 4.0, fall * 5.0),
            "Chest": (lean * 0.30 + fold * 30.0, -fall * 5.0, -fall * 4.0),
            "Neck": (-lean * 0.20 + fold * 16.0, 0.0, fall * 8.0),
            "Head": (-lean * 0.30 + fold * 26.0, fall * 10.0, 0.0),

            "Shoulder.L": (0.0, 0.0, -fold * 16.0),
            "Shoulder.R": (0.0, 0.0, fold * 16.0),
            "UpperArm.L": (-fold * 34.0, 0.0, -8.0 - fold * 26.0),
            "UpperArm.R": (-fold * 28.0, 0.0, 8.0 + fold * 30.0),
            "LowerArm.L": (-16.0 - fold * 34.0, 0.0, 0.0),
            "LowerArm.R": (-16.0 - fold * 28.0, 0.0, 0.0),
            "Hand.L": (-8.0 - fold * 14.0, 0.0, 0.0),
            "Hand.R": (-8.0 - fold * 14.0, 0.0, 0.0),

            "UpperLeg.L": (-lean * 0.20 - fall * 66.0, 0.0, fall * 6.0),
            "UpperLeg.R": (-lean * 0.20 - fall * 58.0, 0.0, -fall * 8.0),
            "LowerLeg.L": (lean * 0.10 + fall * 96.0, 0.0, 0.0),
            "LowerLeg.R": (lean * 0.10 + fall * 88.0, 0.0, 0.0),
            "Foot.L": (-fall * 26.0, 0.0, 0.0),
            "Foot.R": (-fall * 22.0, 0.0, 0.0),
            "Toe.L": (fall * 14.0, 0.0, 0.0),
            "Toe.R": (fall * 12.0, 0.0, 0.0),
        }

    for frame, t in ((1, 0.0), (8, 0.22), (18, 0.55), (30, 0.86), (38, 0.98), (45, 1.0)):
        poser.key(frame, pose(t))

    return poser.finish()


# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------

def export_fbx(body: bpy.types.Object, rig: bpy.types.Object, path: str) -> None:
    """Unity-compatible FBX: Y-up, -Z forward, one take per action."""
    os.makedirs(os.path.dirname(path), exist_ok=True)

    for obj in bpy.context.view_layer.objects:
        obj.select_set(False)

    body.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig

    attempts = [
        dict(
            filepath=path,
            use_selection=True,
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_NONE",
            axis_forward="-Z",
            axis_up="Y",
            object_types={"MESH", "ARMATURE"},
            mesh_smooth_type="FACE",
            use_mesh_modifiers=False,
            add_leaf_bones=False,
            primary_bone_axis="Y",
            secondary_bone_axis="X",
            armature_nodetype="NULL",
            bake_anim=True,
            bake_anim_use_all_actions=True,
            bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=False,
            bake_anim_force_startend_keying=True,
            bake_anim_step=1.0,
            bake_anim_simplify_factor=0.0,
            path_mode="COPY",
            embed_textures=False,
        ),
        dict(
            filepath=path,
            use_selection=True,
            axis_forward="-Z",
            axis_up="Y",
            object_types={"MESH", "ARMATURE"},
            add_leaf_bones=False,
            bake_anim=True,
            bake_anim_use_all_actions=True,
        ),
        dict(filepath=path, use_selection=True, axis_forward="-Z", axis_up="Y"),
    ]

    errors = []
    for kwargs in attempts:
        try:
            bpy.ops.export_scene.fbx(**kwargs)
            if os.path.isfile(path):
                return

            errors.append("exporter reported success but wrote no file")
        except TypeError as exc:
            errors.append(str(exc))
            continue
        except RuntimeError as exc:
            errors.append(str(exc))
            continue

    raise RigError(f"FBX export failed for '{path}': " + "; ".join(errors))


def write_manifest(path: str, payload: dict) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


# ---------------------------------------------------------------------------
# Self-test proxy
# ---------------------------------------------------------------------------

def build_proxy(height: float) -> bpy.types.Object:
    """A deterministic blocky humanoid, so the pipeline can be exercised with
    no paid asset present.

    This is a test fixture and says so: its manifest is stamped
    ``self-test-proxy`` and the Unity importer will not ship it.
    """
    mesh = bpy.data.meshes.new("Proxy_Mesh")
    body = bpy.data.objects.new("Proxy", mesh)
    bpy.context.collection.objects.link(body)

    bm = bmesh.new()

    def block(cx, cy, cz, sx, sy, sz):
        verts = []
        for ix in (-1, 1):
            for iy in (-1, 1):
                for iz in (-1, 1):
                    verts.append(bm.verts.new((
                        cx + ix * sx * 0.5,
                        cy + iy * sy * 0.5,
                        cz + iz * sz * 0.5,
                    )))

        bm.verts.ensure_lookup_table()
        # (index into verts) faces of an axis-aligned box
        quads = (
            (0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1),
            (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3),
        )
        for quad in quads:
            bm.faces.new([verts[i] for i in quad])

    h = height
    block(0.0, 0.0, 0.60 * h, 0.30 * h, 0.17 * h, 0.24 * h)   # pelvis
    block(0.0, 0.0, 0.78 * h, 0.34 * h, 0.19 * h, 0.22 * h)   # chest
    block(0.0, 0.0, 0.92 * h, 0.15 * h, 0.15 * h, 0.16 * h)   # head
    for sign in (-1, 1):
        block(sign * 0.22 * h, 0.0, 0.68 * h, 0.09 * h, 0.09 * h, 0.32 * h)   # arm
        block(sign * 0.22 * h, 0.0, 0.46 * h, 0.08 * h, 0.08 * h, 0.16 * h)   # forearm
        block(sign * 0.09 * h, 0.0, 0.38 * h, 0.12 * h, 0.12 * h, 0.30 * h)   # thigh
        block(sign * 0.09 * h, 0.0, 0.14 * h, 0.10 * h, 0.10 * h, 0.28 * h)   # shin
        block(sign * 0.09 * h, -0.04 * h, 0.02 * h, 0.10 * h, 0.20 * h, 0.05 * h)  # foot

    bm.to_mesh(mesh)
    bm.free()

    material = bpy.data.materials.new("Proxy_Flesh")
    material.use_nodes = True
    mesh.materials.append(material)

    return body


# ---------------------------------------------------------------------------
# Per-slot pipeline
# ---------------------------------------------------------------------------

#: Resting forward hunch per archetype, in degrees. Matches the silhouette the
#: procedural bodies and the generation prompts already describe.
LEAN = {
    "Enemy_Shambler": 26.0,
    "Enemy_Sprinter": 34.0,
    "Enemy_StormBrute": 8.0,
}


def rig_slot(slot: str, args) -> dict:
    height = args.height if args.height else SLOTS[slot]
    lean = LEAN.get(slot, 18.0)

    log("=" * 62)
    log(f"slot {slot}: target {height:.3f} m, resting lean {lean:.0f} deg")

    reset_scene()

    if args.self_test:
        source_path = None
        source_kind = "self-test-proxy"
        body = build_proxy(height * 1.4)   # deliberately off-size; normalise fixes it
        activate(body)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        body.name = slot
    else:
        source_path = find_source(slot, args.input)
        # "imported-file", not "approved": this script cannot tell where a mesh
        # came from, and a manifest that claimed otherwise would be the exact
        # kind of unearned confidence this pipeline exists to avoid. Approval is
        # a human copying the file into the staging slot.
        source_kind = "imported-file"
        log(f"source: {source_path}")
        body = consolidate(import_mesh(source_path), slot)

    normalise(body, height, args.yaw, args.force)

    body.data.calc_loop_triangles()
    vertex_count = len(body.data.vertices)
    triangle_count = len(body.data.loop_triangles)
    log(f"mesh: {vertex_count} vertices, {triangle_count} triangles, "
        f"{len(body.data.materials)} material slot(s)")

    rig = build_armature(body, height, slot)
    weighting = skin(body, rig)
    actions = build_clips(rig, height, lean)
    log(f"clips: {', '.join(action.name for action in actions)}")

    output_dir = args.output or os.path.join(STAGING_DIR, slot, RIGGED_SUBFOLDER)
    os.makedirs(output_dir, exist_ok=True)

    fbx_path = os.path.join(output_dir, f"{slot}_Rigged.fbx")
    export_fbx(body, rig, fbx_path)

    manifest = {
        "schemaVersion": PIPELINE_VERSION,
        "slot": slot,
        "source": source_kind,
        "selfTest": bool(args.self_test),
        "sourceFile": os.path.relpath(source_path, REPO_ROOT) if source_path else None,
        "sourceSha256": sha256(source_path) if source_path else None,
        "fbx": os.path.basename(fbx_path),
        "heightMeters": round(height, 4),
        "fps": FPS,
        "vertexCount": vertex_count,
        "triangleCount": triangle_count,
        "materialSlots": len(body.data.materials),
        "boneCount": len(ALL_BONES),
        "boneNames": list(ALL_BONES),
        "deformBoneCount": len(DEFORM_BONES),
        "weighting": weighting,
        "leanDegrees": lean,
        "clips": [
            {"name": name, "start": start, "end": end, "loop": loop}
            for name, start, end, loop in CLIPS
        ],
        "blender": bpy.app.version_string,
        "generator": "Tools/Blender/rig_zombie.py",
    }

    manifest_path = os.path.join(output_dir, f"{slot}_Rigged.rigmanifest.json")
    write_manifest(manifest_path, manifest)

    size_kib = os.path.getsize(fbx_path) / 1024.0
    log(f"wrote {fbx_path} ({size_kib:.1f} KiB)")
    log(f"wrote {manifest_path}")

    if args.blend:
        blend_path = os.path.join(output_dir, f"{slot}_Rigged.blend")
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
        log(f"wrote {blend_path}")

    return manifest


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def parse_args(argv: list) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        prog="rig_zombie.py",
        description="Rig, animate and export an approved Meshcaster zombie for Unity.")

    parser.add_argument("--slot", action="append", choices=sorted(SLOTS),
                        help="slot to rig; repeatable")
    parser.add_argument("--all", action="store_true",
                        help="rig every enemy slot that has an approved mesh")
    parser.add_argument("--input",
                        help="file, slot folder, or parent of slot folders "
                             f"(default: {os.path.relpath(SOURCE_DIR, REPO_ROOT)}/<slot>)")
    parser.add_argument("--output",
                        help="output directory (default: the slot's Rigged/ folder)")
    parser.add_argument("--height", type=float,
                        help="override the slot's target height, in metres")
    parser.add_argument("--yaw", type=float, default=0.0,
                        help="degrees to rotate about Z before rigging, if the source "
                             "does not face Blender -Y")
    parser.add_argument("--force", action="store_true",
                        help="rig even if the source is not standing upright")
    parser.add_argument("--blend", action="store_true",
                        help="also save the .blend next to the FBX")
    parser.add_argument("--self-test", action="store_true",
                        help="run the whole pipeline on a generated proxy humanoid; "
                             "writes output stamped as a proxy, not as approved art")
    parser.add_argument("--check", action="store_true",
                        help="report which slots have an importable source and exit; "
                             "writes nothing")

    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    return parser.parse_args(argv)


def run_check(args) -> int:
    log("=" * 62)
    log("source check -- nothing is written, nothing is spent")
    ready = 0

    for slot in sorted(SLOTS):
        try:
            path = find_source(slot, args.input)
        except RigError:
            log(f"  [pending] {slot:<18} no approved mesh staged")
            continue

        ready += 1
        log(f"  [ready]   {slot:<18} {os.path.relpath(path, REPO_ROOT)}")

    log(f"{ready}/{len(SLOTS)} enemy slots have a source to rig")
    return 0


def main(argv: list) -> int:
    args = parse_args(argv)

    print("=" * 72)
    print("Ashfall: Black Meridian - Meshcaster zombie rigging")
    print(f"Blender {bpy.app.version_string}, pipeline v{PIPELINE_VERSION}")
    print("=" * 72)

    if args.check:
        return run_check(args)

    if args.self_test and not args.output:
        # A proxy written into the staging folder would look exactly like
        # approved art in the Project view. Make the caller name a scratch path.
        print("[FAIL]      --self-test requires --output; it must not write into "
              "the staging slots.")
        print("RESULT: FAILED (refused to write a proxy into Assets/)")
        return 1

    slots = list(dict.fromkeys(args.slot or []))
    if args.all or not slots:
        slots = sorted(SLOTS)

    if args.output and len(slots) > 1 and not args.self_test:
        # One --output for three slots would have each overwrite the last.
        log("note: --output is shared by every slot; files are named per slot")

    results = []
    failures = []

    for slot in slots:
        try:
            results.append(rig_slot(slot, args))
        except RigError as exc:
            failures.append(f"{slot}: {exc}")
        except Exception as exc:  # noqa: BLE001 - surface the real traceback in batch
            traceback.print_exc()
            failures.append(f"{slot}: unexpected {type(exc).__name__}: {exc}")

    print("-" * 72)
    for manifest in results:
        print(f"[ok]        {manifest['slot']:<18} {manifest['boneCount']} bones, "
              f"{len(manifest['clips'])} clips, {manifest['weighting']}, "
              f"{manifest['triangleCount']} tris, source={manifest['source']}")

    if failures:
        for failure in failures:
            print(f"[FAIL]      {failure}")

        print(f"RESULT: FAILED ({len(failures)} of {len(slots)} slot(s))")
        return 1

    if not results:
        print("RESULT: FAILED (nothing was rigged)")
        return 1

    print(f"RESULT: OK ({len(results)} slot(s) rigged)")
    return 0


if __name__ == "__main__":
    try:
        code = main(sys.argv)
    except SystemExit as exit_request:      # argparse --help / bad flags
        code = int(exit_request.code or 0)
    except Exception:                        # noqa: BLE001
        traceback.print_exc()
        code = 1

    # Blender ignores a plain return value in --background, so exit explicitly.
    sys.exit(code)
