"""Convert an approved Meshcaster GLB into a Unity-importable FBX.

This repository has no ``com.unity.cloud.gltfast``, so a ``.glb`` sitting in
``Assets/`` is an inert file: Unity will not build a mesh from it and
``AshfallMeshcasterImport`` will not find a model for the slot. This script is
the free, offline, deterministic bridge between the two -- import the GLB,
orient it the way the slot expects, export an FBX beside it.

It does not decide what is approved and it never generates anything. It reads
a file a human (or an authorised run) already paid for and writes another file
next to it.

Run it with Blender, never with a bare Python:

    /snap/bin/blender --background --python Tools/Blender/glb_to_fbx.py -- \\
        --slot Weapon_Arc9Rifle --input /path/to/model.glb

With no ``--output`` the FBX lands at the slot's documented staging location:

    Assets/Ashfall/Art/Meshcaster/<slot>/Source/<slot>.fbx

which is where both ``AshfallMeshcasterImport`` (static weapons) and
``rig_zombie.py`` (enemies) already look. The slot's ``Rigged/`` sub-folder is
owned by ``rig_zombie.py`` and is never written here.

Orientation
-----------
Meshy returns a model in whatever pose it felt like. Enemies come back upright
often enough that the default is to leave them alone; weapons are measured
along Unity's Z (``AshfallMeshcasterImport.Slots``), so a barrel lying along X
would be auto-fitted on the wrong dimension and arrive the wrong size.

``--align length`` therefore rotates a weapon so its longest bounding-box axis
runs along Unity Z, its second-longest along Unity Y, and the thinnest along
Unity X -- which for a gun is length, then grip height, then plate thickness.
Which end is the muzzle cannot be read off a bounding box, so it is guessed
from the volume centroid (a gun is heavier at the receiver than at the muzzle)
and the guess is printed. ``--yaw 180`` overrides it.

Scale is deliberately *not* corrected here. ``AshfallMeshcasterImport.FitToSlot``
measures the model in Unity and fits it to the slot's target size; doing it in
two places would let the two disagree.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

import bpy
import mathutils

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
STAGING_DIR = os.path.join(REPO_ROOT, "Assets", "Ashfall", "Art", "Meshcaster")

#: Owned by rig_zombie.py. Never written by this script, never read as input.
RIGGED_SUBFOLDER = "Rigged"

#: Slot -> which Unity axis the slot's target size is measured on. Mirrors
#: AshfallMeshcasterImport.Slots; the two must agree or the auto-fit measures
#: one dimension while this script aligned another.
SIZE_AXIS = {
    "Enemy_Shambler": "Y",
    "Enemy_Sprinter": "Y",
    "Enemy_StormBrute": "Y",
    "Weapon_MeridianSidearm": "Z",
    "Weapon_BreakwaterShotgun": "Z",
    "Weapon_Arc9Rifle": "Z",
}


class ConvertError(Exception):
    """A condition that must stop the run rather than write a bad FBX."""


def log(message: str) -> None:
    print(f"[glb2fbx] {message}", flush=True)


# ---------------------------------------------------------------------------
# Scene
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


def import_glb(path: str) -> list:
    before = set(bpy.data.objects)
    extension = os.path.splitext(path)[1].lower()

    if extension in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif extension == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif extension == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    else:
        raise ConvertError(f"unsupported input format '{extension}' ({path})")

    created = [obj for obj in bpy.data.objects if obj not in before]
    meshes = [obj for obj in created if obj.type == "MESH"]
    if not meshes:
        raise ConvertError(f"'{path}' imported without producing any mesh object")

    # A camera or lamp riding along in the GLB is not part of the asset.
    for obj in created:
        if obj.type not in ("MESH", "EMPTY"):
            bpy.data.objects.remove(obj, do_unlink=True)

    return [obj for obj in meshes if obj.name in bpy.data.objects]


def flatten(meshes: list) -> None:
    """Drop parents and bake transforms, so measurements are in world space."""
    for obj in meshes:
        matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = matrix

    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj in meshes)

    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


# ---------------------------------------------------------------------------
# Measurement
# ---------------------------------------------------------------------------

def world_bounds(meshes: list):
    lo = mathutils.Vector((float("inf"),) * 3)
    hi = mathutils.Vector((float("-inf"),) * 3)

    for obj in meshes:
        for corner in obj.bound_box:
            point = obj.matrix_world @ mathutils.Vector(corner)
            for axis in range(3):
                lo[axis] = min(lo[axis], point[axis])
                hi[axis] = max(hi[axis], point[axis])

    if lo.x == float("inf"):
        raise ConvertError("model has no measurable geometry")

    return lo, hi


def vertex_centroid(meshes: list) -> mathutils.Vector:
    """Unweighted mean vertex position -- the cheap stand-in for mass."""
    total = mathutils.Vector((0.0, 0.0, 0.0))
    count = 0

    for obj in meshes:
        matrix = obj.matrix_world
        for vertex in obj.data.vertices:
            total += matrix @ vertex.co
            count += 1

    if count == 0:
        raise ConvertError("model has no vertices")

    return total / count


def rotate_all(meshes: list, matrix: mathutils.Matrix) -> None:
    for obj in meshes:
        obj.matrix_world = matrix @ obj.matrix_world

    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj in meshes)

    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


# ---------------------------------------------------------------------------
# Orientation
# ---------------------------------------------------------------------------

# Blender is Z-up / -Y-forward; the FBX export below writes axis_up="Y",
# axis_forward="-Z", which is the mapping Unity reads back as
#   Blender +X -> Unity +X,  Blender +Z -> Unity +Y,  Blender -Y -> Unity +Z.
# So "put the length on Unity Z" means "put the length on Blender Y".
UNITY_AXIS_TO_BLENDER = {"X": 0, "Y": 2, "Z": 1}


def align_longest(meshes: list, unity_axis: str) -> dict:
    """Rotate so the longest extent runs along ``unity_axis`` in Unity space.

    Only whole 90-degree rotations are used: the model is re-axised, never
    sheared or skewed, so a run is reproducible and reversible.
    """
    lo, hi = world_bounds(meshes)
    size = hi - lo

    order = sorted(range(3), key=lambda axis: size[axis], reverse=True)
    longest, middle, shortest = order

    # Target: longest -> the requested Unity axis in Blender terms,
    # remaining two -> up (Blender Z) then side (Blender X), biggest first.
    target_long = UNITY_AXIS_TO_BLENDER[unity_axis]
    remaining = [2, 1, 0]  # Blender Z (up), Y (depth), X (side)
    remaining.remove(target_long)
    # Prefer putting the second-largest extent on Blender Z (Unity up).
    target_middle = 2 if 2 in remaining else remaining[0]
    remaining.remove(target_middle)
    target_short = remaining[0]

    basis = mathutils.Matrix.Identity(3)
    source_to_target = {longest: target_long, middle: target_middle, shortest: target_short}
    for source_axis, target_axis in source_to_target.items():
        row = [0.0, 0.0, 0.0]
        row[source_axis] = 1.0
        basis[target_axis] = row

    if round(basis.determinant()) < 0:
        # Keep it a rotation, not a reflection: a mirrored gun is a left-handed
        # gun and its ejection port comes out the wrong side.
        basis[target_short] = [-value for value in basis[target_short]]

    rotate_all(meshes, basis.to_4x4())

    return {
        "sourceExtents": [round(value, 5) for value in size],
        "longestSourceAxis": "XYZ"[longest],
        "alignedToUnityAxis": unity_axis,
    }


def face_muzzle_forward(meshes: list, unity_axis: str) -> dict:
    """Flip 180 degrees when the bulk sits at the far end of the length axis.

    A gun carries its mass in the receiver, grip and stock, so the vertex
    centroid should be *behind* the bounding-box centre along the barrel. When
    it is in front, the model came in pointing backwards.
    """
    axis = UNITY_AXIS_TO_BLENDER[unity_axis]
    lo, hi = world_bounds(meshes)
    centre = (lo[axis] + hi[axis]) * 0.5
    centroid = vertex_centroid(meshes)[axis]
    span = hi[axis] - lo[axis]
    bias = (centroid - centre) / span if span > 1e-9 else 0.0

    # Along Blender Y the muzzle should point -Y (Unity +Z), so the bulk
    # belongs at +Y: a negative bias means the model is reversed.
    flipped = bias < 0.0
    if flipped:
        rotate_all(meshes, mathutils.Matrix.Rotation(3.141592653589793, 4, "Z"))

    return {"centroidBias": round(bias, 4), "flipped180": flipped}


def ground_and_centre(meshes: list) -> None:
    """Sit the model on the Blender floor, centred on X and Y.

    Unity re-grounds too, but a source file whose origin is a kilometre from
    its geometry imports with unusable bounds in the inspector.
    """
    lo, hi = world_bounds(meshes)
    offset = mathutils.Vector((
        -(lo.x + hi.x) * 0.5,
        -(lo.y + hi.y) * 0.5,
        -lo.z,
    ))
    rotate_all(meshes, mathutils.Matrix.Translation(offset))


# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------

def downscale_textures(limit: int) -> list:
    """Cap every embedded map at ``limit`` pixels on its longest side.

    Meshy delivers 4096-square maps. This game shows a weapon at 40 cm and an
    enemy through fog at 20 m, so 4K is storage the player never sees -- and
    re-encoding it losslessly turns a 3 MB JPEG into a 25 MB PNG, which is how
    six slots became 400 MB of git history on the first attempt.

    Returns one record per image so the manifest can show what was resized.
    """
    records = []

    for image in bpy.data.images:
        width, height = image.size
        if width == 0 or height == 0:
            continue

        record = {"name": image.name, "source": [width, height]}
        longest = max(width, height)

        if limit and longest > limit:
            scale = limit / float(longest)
            image.scale(max(1, int(round(width * scale))), max(1, int(round(height * scale))))
            record["scaledTo"] = list(image.size)

        records.append(record)

    return records


def unpack_textures(target_dir: str, slot: str) -> list:
    """Write the (already downscaled) maps out beside the FBX, as JPEG.

    The maps are *not* embedded in the FBX. Embedding put Meshy's original 4K
    bytes back into a 23 MB file no matter what the in-memory buffer had been
    scaled to, which is 140 MB of git across six slots for pixels the player
    never sees at this game's viewing distances.

    Instead each map becomes its own Unity asset and the FBX refers to it by
    bare filename (``path_mode="STRIP"``), which is what Unity's model importer
    resolves textures by. Every name is prefixed with the slot key because
    Meshy calls all of them ``Image_0``..``Image_3`` -- unprefixed, Unity would
    be free to bind the shambler's base colour to the sidearm.
    """
    if not bpy.data.images:
        return []

    os.makedirs(target_dir, exist_ok=True)
    written = []

    for index, image in enumerate(list(bpy.data.images)):
        if image.size[0] == 0 or image.size[1] == 0:
            continue

        stem = "".join(ch if ch.isalnum() or ch in "-_" else "_" for ch in image.name) or f"image_{index}"
        name = f"{slot}_{stem}"
        path = os.path.join(target_dir, f"{name}.jpg")
        try:
            image.filepath_raw = path
            image.file_format = "JPEG"
            image.save()

            # The datablock name is what the FBX writes as the texture's name,
            # and the filepath basename is what Unity searches the project for.
            # Both have to carry the slot prefix or the collision above is live.
            image.name = name
            image.source = "FILE"
            image.filepath = path
            if image.packed_file is not None:
                image.unpack(method="REMOVE")
            image.reload()

            written.append(os.path.basename(path))
        except RuntimeError as exc:
            log(f"could not write {path}: {exc}")

    return sorted(written)


def export_fbx(path: str, meshes: list, bake_axes: bool) -> None:
    """Write the FBX.

    ``bake_axes`` decides where the Blender-Z-up to Unity-Y-up conversion
    lives. Blender's default is to leave the geometry alone and put the
    conversion in a -90 degrees X rotation on the FBX root -- which is fine
    until something zeroes that rotation. ``AshfallMeshcasterImport.TryAttach``
    does exactly that (``localRotation = Quaternion.Euler(slot.Euler)``), so a
    weapon exported the default way arrives in Blender's axis order and
    ``FitToSlot`` measures its grip height as its length.

    So weapons bake the conversion into the vertex data and arrive upright with
    an identity root. Enemies do not: their FBX is read back by
    ``rig_zombie.py`` in Blender, which wants Blender's own Z-up space, and
    their Unity-facing asset is the rigged FBX that script exports.
    """
    os.makedirs(os.path.dirname(path), exist_ok=True)

    for obj in bpy.context.view_layer.objects:
        obj.select_set(obj in meshes)
    bpy.context.view_layer.objects.active = meshes[0]

    attempts = [
        dict(
            filepath=path,
            use_selection=True,
            apply_unit_scale=True,
            global_scale=1.0,
            axis_forward="-Z",
            axis_up="Y",
            bake_space_transform=bake_axes,
            object_types={"MESH"},
            use_mesh_modifiers=True,
            mesh_smooth_type="FACE",
            bake_anim=False,
            path_mode="STRIP",
            embed_textures=False,
        ),
        dict(
            filepath=path,
            use_selection=True,
            axis_forward="-Z",
            axis_up="Y",
            object_types={"MESH"},
            bake_anim=False,
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
        except (TypeError, RuntimeError) as exc:
            errors.append(str(exc))

    raise ConvertError(f"FBX export failed for '{path}': " + "; ".join(errors))


# ---------------------------------------------------------------------------
# Driver
# ---------------------------------------------------------------------------

def convert(args) -> dict:
    if not os.path.isfile(args.input):
        raise ConvertError(f"no such input file: {args.input}")

    output = args.output or os.path.join(STAGING_DIR, args.slot, "Source", f"{args.slot}.fbx")
    if os.path.basename(os.path.dirname(output)) == RIGGED_SUBFOLDER:
        raise ConvertError(f"'{RIGGED_SUBFOLDER}/' belongs to rig_zombie.py; refusing to write {output}")

    reset_scene()
    meshes = import_glb(args.input)
    flatten(meshes)

    report = {
        "slot": args.slot,
        "source": os.path.abspath(args.input),
        "output": os.path.abspath(output),
        "meshObjects": len(meshes),
        "materialSlots": sorted({slot.material.name for obj in meshes
                                 for slot in obj.material_slots if slot.material}),
        "triangles": sum(len(obj.data.loop_triangles) for obj in meshes
                         if obj.data.loop_triangles or obj.data.calc_loop_triangles() is None),
    }

    if args.align == "length":
        report["align"] = align_longest(meshes, SIZE_AXIS.get(args.slot, "Z"))
        report["align"].update(face_muzzle_forward(meshes, SIZE_AXIS.get(args.slot, "Z")))
    else:
        report["align"] = {"alignedToUnityAxis": None}

    if args.yaw:
        rotate_all(meshes, mathutils.Matrix.Rotation(args.yaw * 3.141592653589793 / 180.0, 4, "Z"))
        report["align"]["extraYawDegrees"] = args.yaw

    ground_and_centre(meshes)

    lo, hi = world_bounds(meshes)
    size = hi - lo
    # Reported in Unity terms so it can be read against AshfallMeshcasterImport.
    report["blenderExtents"] = [round(value, 5) for value in size]
    report["unityExtents"] = {
        "x": round(size.x, 5),
        "y": round(size.z, 5),
        "z": round(size.y, 5),
    }

    # Meshy returns one unnamed material. Give it the slot's name so the
    # material Unity generates is identifiable in the project window.
    for obj in meshes:
        for material_slot in obj.material_slots:
            if material_slot.material is not None:
                material_slot.material.name = f"{args.slot}_Mat"

    report["textureMaps"] = downscale_textures(args.texture_size)
    report["textures"] = unpack_textures(
        os.path.join(os.path.dirname(output), "Textures"), args.slot)
    report["materialSlots"] = sorted({s.material.name for obj in meshes
                                      for s in obj.material_slots if s.material})
    export_fbx(output, meshes, args.bake_axes)
    report["bakeAxes"] = bool(args.bake_axes)
    report["outputBytes"] = os.path.getsize(output)

    manifest = os.path.splitext(output)[0] + "_convert.json"
    with open(manifest, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2, sort_keys=True)
        handle.write("\n")
    report["manifest"] = manifest

    return report


def parse_args(argv: list):
    parser = argparse.ArgumentParser(
        prog="glb_to_fbx.py",
        description="Convert an approved Meshcaster GLB into a Unity-importable FBX.")
    parser.add_argument("--slot", required=True, choices=sorted(SIZE_AXIS),
                        help="Ashfall art slot the file belongs to")
    parser.add_argument("--input", required=True,
                        help="the approved .glb/.gltf/.fbx/.obj to convert")
    parser.add_argument("--output",
                        help="FBX to write (default: the slot's Source/ folder)")
    parser.add_argument("--align", choices=("length", "none"), default="none",
                        help="'length' re-axises the model so its longest extent "
                             "runs along the slot's measured Unity axis")
    parser.add_argument("--yaw", type=float, default=0.0,
                        help="extra rotation about up, in degrees, applied after --align")
    parser.add_argument("--bake-axes", action="store_true",
                        help="bake the Z-up to Y-up conversion into the vertex "
                             "data instead of a root rotation. Use for weapons, "
                             "whose root rotation Unity's importer overwrites; "
                             "leave off for enemies, whose FBX is read back by "
                             "rig_zombie.py in Blender's own Z-up space")
    parser.add_argument("--texture-size", type=int, default=2048,
                        help="cap each map's longest side at this many pixels "
                             "(0 keeps Meshy's 4K originals)")
    return parser.parse_args(argv)


def main(argv: list) -> int:
    try:
        args = parse_args(argv)
    except SystemExit as exit_request:
        return int(exit_request.code or 0)

    try:
        report = convert(args)
    except ConvertError as exc:
        log(f"FAILED {args.slot if 'args' in dir() else ''}: {exc}")
        return 1

    resized = [m for m in report.get("textureMaps", []) if "scaledTo" in m]
    log(f"{report['slot']}: {report['meshObjects']} mesh object(s), "
        f"{len(report['materialSlots'])} material(s), "
        f"{len(report['textures'])} texture file(s), "
        f"{len(resized)} downscaled")
    log(f"{report['slot']}: Unity extents "
        f"x={report['unityExtents']['x']} y={report['unityExtents']['y']} "
        f"z={report['unityExtents']['z']}")
    log(f"{report['slot']}: wrote {report['output']} ({report['outputBytes']} bytes)")
    return 0


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    sys.exit(main(argv))
