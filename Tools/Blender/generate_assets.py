#!/usr/bin/env python3
"""Generate the original Ashfall: Black Meridian source-art library in Blender.

Run headlessly:

    /snap/bin/blender --background --python Tools/Blender/generate_assets.py

Everything in here is authored from scratch out of primitives and procedural
materials. Nothing is downloaded, traced, or derived from another game's
content; the shapes deliberately mirror the silhouettes the Unity scene builder
already produces from code, so the exports are a drop-in visual upgrade rather
than a second, unrelated art style.

Output lands in ``Tools/Blender/Output`` (git-ignored). See the README next to
this file for the Unity import step.

Exit code is 0 on success and 1 if any export failed.
"""

from __future__ import annotations

import math
import os
import sys
import traceback

import bpy
import mathutils

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "Output")
FBX_DIR = os.path.join(OUTPUT_DIR, "FBX")
GLB_DIR = os.path.join(OUTPUT_DIR, "GLB")
BLEND_DIR = os.path.join(OUTPUT_DIR, "Blend")

# ---------------------------------------------------------------------------
# Palette. Mirrors Assets/Ashfall/Scripts/Core/AshfallPalette.cs so the Blender
# exports and the procedural in-engine kit read as one art direction.
# ---------------------------------------------------------------------------

PALETTE = {
    "concrete_dark":    (0.106, 0.118, 0.133),
    "concrete_mid":     (0.180, 0.196, 0.216),
    "concrete_light":   (0.278, 0.298, 0.318),
    "wet_floor":        (0.078, 0.090, 0.106),
    "metal_oxidised":   (0.243, 0.196, 0.161),
    "metal_painted":    (0.153, 0.204, 0.216),
    "rust_deep":        (0.310, 0.161, 0.090),
    "storm_teal":       (0.239, 0.878, 0.855),
    "emergency_amber":  (1.000, 0.627, 0.204),
    "hazard_yellow":    (0.898, 0.749, 0.153),
    "hazard_stripe":    (0.098, 0.098, 0.110),
    "warning_red":      (0.902, 0.243, 0.216),
    "enemy_flesh":      (0.318, 0.298, 0.290),
    "enemy_corrupt":    (0.180, 0.639, 0.612),
    "brute_armour":     (0.184, 0.169, 0.176),
    "timber":           (0.290, 0.212, 0.145),
    "gun_body":         (0.106, 0.114, 0.122),
}

EXPORTED: list[str] = []
FAILURES: list[str] = []


# ---------------------------------------------------------------------------
# Scene setup
# ---------------------------------------------------------------------------

def reset_scene() -> None:
    """Empty the file completely, including orphaned data blocks."""
    bpy.ops.wm.read_factory_settings(use_empty=True)

    for collection in (
        bpy.data.meshes, bpy.data.materials, bpy.data.objects,
        bpy.data.collections, bpy.data.images, bpy.data.node_groups,
    ):
        for block in list(collection):
            try:
                collection.remove(block)
            except (RuntimeError, ReferenceError):
                pass

    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0


def ensure_collection(name: str) -> bpy.types.Collection:
    if name in bpy.data.collections:
        return bpy.data.collections[name]

    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


# ---------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------

def _set_socket(node: bpy.types.Node, names, value) -> bool:
    """Set the first socket that exists from a list of candidate names.

    Principled BSDF socket names moved around between Blender 3.x and 4.x
    (``Emission`` became ``Emission Color``, ``Specular`` became
    ``Specular IOR Level``). Trying a list keeps this script working across
    versions instead of exploding on a KeyError.
    """
    if isinstance(names, str):
        names = [names]

    for name in names:
        socket = node.inputs.get(name)
        if socket is not None:
            socket.default_value = value
            return True

    return False


def make_material(
    name: str,
    base_color,
    metallic: float = 0.0,
    roughness: float = 0.75,
    emission=None,
    emission_strength: float = 0.0,
    noise_amount: float = 0.0,
    noise_scale: float = 12.0,
) -> bpy.types.Material:
    """A Principled material, optionally broken up by a procedural noise mix."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]

    material = bpy.data.materials.new(name)
    material.use_nodes = True
    tree = material.node_tree
    nodes = tree.nodes
    links = tree.links

    bsdf = nodes.get("Principled BSDF")
    if bsdf is None:
        bsdf = nodes.new("ShaderNodeBsdfPrincipled")
        output = nodes.get("Material Output") or nodes.new("ShaderNodeOutputMaterial")
        links.new(bsdf.outputs["BSDF"], output.inputs["Surface"])

    rgba = (base_color[0], base_color[1], base_color[2], 1.0)
    _set_socket(bsdf, "Base Color", rgba)
    _set_socket(bsdf, "Metallic", metallic)
    _set_socket(bsdf, "Roughness", roughness)

    if emission is not None and emission_strength > 0.0:
        _set_socket(bsdf, ["Emission Color", "Emission"], (emission[0], emission[1], emission[2], 1.0))
        _set_socket(bsdf, "Emission Strength", emission_strength)

    if noise_amount > 0.0:
        # Noise -> ColorRamp -> Mix into base colour. Enough grain that a flat
        # surface does not read as a solid block of colour under a hard light.
        tex_coord = nodes.new("ShaderNodeTexCoord")
        tex_coord.location = (-900, 200)

        noise = nodes.new("ShaderNodeTexNoise")
        noise.location = (-700, 200)
        noise.inputs["Scale"].default_value = noise_scale
        if "Detail" in noise.inputs:
            noise.inputs["Detail"].default_value = 6.0
        links.new(tex_coord.outputs["Object"], noise.inputs["Vector"])

        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.location = (-500, 200)
        ramp.color_ramp.elements[0].position = 0.35
        ramp.color_ramp.elements[1].position = 0.72
        links.new(noise.outputs["Fac"], ramp.inputs["Fac"])

        mix = nodes.new("ShaderNodeMixRGB") if "ShaderNodeMixRGB" in dir(bpy.types) else nodes.new("ShaderNodeMix")
        mix.location = (-250, 120)

        darker = (
            max(0.0, base_color[0] * (1.0 - noise_amount)),
            max(0.0, base_color[1] * (1.0 - noise_amount)),
            max(0.0, base_color[2] * (1.0 - noise_amount)),
            1.0,
        )

        if mix.bl_idname == "ShaderNodeMix":
            mix.data_type = "RGBA"
            mix.inputs["Factor"].default_value = 1.0
            mix.inputs[6].default_value = rgba
            mix.inputs[7].default_value = darker
            links.new(ramp.outputs["Color"], mix.inputs["Factor"])
            links.new(mix.outputs[2], bsdf.inputs["Base Color"])
        else:
            mix.blend_type = "MIX"
            mix.inputs["Color1"].default_value = rgba
            mix.inputs["Color2"].default_value = darker
            links.new(ramp.outputs["Color"], mix.inputs["Fac"])
            links.new(mix.outputs["Color"], bsdf.inputs["Base Color"])

    return material


def build_material_library() -> dict:
    return {
        "concrete": make_material("M_Concrete", PALETTE["concrete_mid"], 0.0, 0.88, noise_amount=0.35, noise_scale=9.0),
        "concrete_dark": make_material("M_ConcreteDark", PALETTE["concrete_dark"], 0.0, 0.90, noise_amount=0.30),
        "wet_floor": make_material("M_WetFloor", PALETTE["wet_floor"], 0.05, 0.28, noise_amount=0.45, noise_scale=6.0),
        "steel": make_material("M_SteelPanel", PALETTE["metal_painted"], 0.85, 0.42, noise_amount=0.22, noise_scale=16.0),
        "steel_dark": make_material("M_SteelDark", PALETTE["concrete_dark"], 0.90, 0.34),
        "rust": make_material("M_Rust", PALETTE["rust_deep"], 0.35, 0.78, noise_amount=0.40, noise_scale=11.0),
        "oxidised": make_material("M_MetalOxidised", PALETTE["metal_oxidised"], 0.55, 0.62, noise_amount=0.30),
        "hazard": make_material("M_HazardPaint", PALETTE["hazard_yellow"], 0.10, 0.55),
        "hazard_dark": make_material("M_HazardStripe", PALETTE["hazard_stripe"], 0.10, 0.60),
        "timber": make_material("M_Timber", PALETTE["timber"], 0.0, 0.85, noise_amount=0.35, noise_scale=22.0),
        "gun_body": make_material("M_GunBody", PALETTE["gun_body"], 0.80, 0.38),
        "flesh": make_material("M_EnemyFlesh", PALETTE["enemy_flesh"], 0.05, 0.82, noise_amount=0.30),
        "brute": make_material("M_BruteArmour", PALETTE["brute_armour"], 0.60, 0.45, noise_amount=0.20),
        "teal": make_material("M_StormTeal", PALETTE["storm_teal"], 0.0, 0.35,
                              emission=PALETTE["storm_teal"], emission_strength=6.0),
        "corrupt": make_material("M_EnemyCorrupt", PALETTE["enemy_corrupt"], 0.0, 0.40,
                                 emission=PALETTE["enemy_corrupt"], emission_strength=4.0),
        "amber": make_material("M_EmergencyAmber", PALETTE["emergency_amber"], 0.0, 0.35,
                               emission=PALETTE["emergency_amber"], emission_strength=5.0),
        "red": make_material("M_WarningRed", PALETTE["warning_red"], 0.0, 0.40,
                             emission=PALETTE["warning_red"], emission_strength=4.0),
    }


# ---------------------------------------------------------------------------
# Primitive builders (raw mesh data, no bpy.ops -- deterministic and fast)
# ---------------------------------------------------------------------------

def _new_object(name: str, verts, faces, material, collection) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_mesh")
    mesh.from_pydata(verts, [], faces)
    mesh.validate(verbose=False)
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    if material is not None:
        obj.data.materials.append(material)

    collection.objects.link(obj)
    return obj


def _transform(obj: bpy.types.Object, location=(0, 0, 0), rotation=(0, 0, 0), scale=(1, 1, 1)) -> None:
    obj.location = location
    obj.rotation_euler = tuple(math.radians(a) for a in rotation)
    obj.scale = scale


def box(name, size, material, collection, location=(0, 0, 0), rotation=(0, 0, 0)) -> bpy.types.Object:
    """Axis-aligned box centred on its own origin. Size is (x, y, z) in metres."""
    hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
    verts = [
        (-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
        (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz),
    ]
    faces = [
        (0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4),
        (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7),
    ]
    obj = _new_object(name, verts, faces, material, collection)
    _transform(obj, location, rotation)
    return obj


def cylinder(name, radius, height, material, collection, segments=16,
             location=(0, 0, 0), rotation=(0, 0, 0)) -> bpy.types.Object:
    """Capped cylinder along +Z."""
    verts = []
    faces = []
    half = height / 2.0

    for i in range(segments):
        angle = (i / segments) * math.tau
        x, y = math.cos(angle) * radius, math.sin(angle) * radius
        verts.append((x, y, -half))
        verts.append((x, y, half))

    for i in range(segments):
        a = i * 2
        b = ((i + 1) % segments) * 2
        faces.append((a, b, b + 1, a + 1))

    bottom_centre = len(verts)
    verts.append((0.0, 0.0, -half))
    top_centre = len(verts)
    verts.append((0.0, 0.0, half))

    for i in range(segments):
        a = i * 2
        b = ((i + 1) % segments) * 2
        faces.append((bottom_centre, b, a))
        faces.append((top_centre, a + 1, b + 1))

    obj = _new_object(name, verts, faces, material, collection)
    _transform(obj, location, rotation)
    return obj


def wedge(name, size, material, collection, location=(0, 0, 0), rotation=(0, 0, 0)) -> bpy.types.Object:
    """A ramp rising along +Y, used for stair skirts and loading docks."""
    hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
    verts = [
        (-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
        (-hx, hy, hz), (hx, hy, hz),
    ]
    faces = [
        (0, 3, 2, 1),   # bottom
        (0, 1, 5, 4),   # slope
        (2, 3, 4, 5),   # back
        (0, 4, 3),      # left
        (1, 2, 5),      # right
    ]
    obj = _new_object(name, verts, faces, material, collection)
    _transform(obj, location, rotation)
    return obj


def frame(name, outer, thickness, depth, material, collection, location=(0, 0, 0)) -> list:
    """Four boxes forming a rectangular frame in the XZ plane."""
    w, h = outer
    parts = [
        box(f"{name}_Top", (w, depth, thickness), material, collection,
            (location[0], location[1], location[2] + h / 2.0 - thickness / 2.0)),
        box(f"{name}_Bottom", (w, depth, thickness), material, collection,
            (location[0], location[1], location[2] - h / 2.0 + thickness / 2.0)),
        box(f"{name}_Left", (thickness, depth, h - thickness * 2), material, collection,
            (location[0] - w / 2.0 + thickness / 2.0, location[1], location[2])),
        box(f"{name}_Right", (thickness, depth, h - thickness * 2), material, collection,
            (location[0] + w / 2.0 - thickness / 2.0, location[1], location[2])),
    ]
    return parts


def join(name: str, parts: list, collection: bpy.types.Collection) -> bpy.types.Object:
    """Merge parts into one object whose origin sits at the world origin."""
    parts = [p for p in parts if p is not None]
    if not parts:
        raise ValueError(f"{name} has no parts to join")

    if len(parts) == 1:
        parts[0].name = name
        return parts[0]

    # Apply each part's transform into its mesh, then merge the vertex data by
    # hand. Doing it this way avoids bpy.ops.object.join, which needs a view
    # layer context that does not reliably exist in --background.
    all_verts = []
    all_faces = []
    material_slots = []
    face_materials = []

    for part in parts:
        # Build the matrix from the part's own loc/rot/scale rather than reading
        # matrix_world. In --background nothing evaluates the depsgraph between
        # creating an object and joining it, so matrix_world is still identity
        # and every part silently merges at the origin -- which produces a kit
        # that exports cleanly and is completely the wrong shape.
        matrix = mathutils.Matrix.LocRotScale(part.location, part.rotation_euler, part.scale)
        offset = len(all_verts)
        mesh = part.data

        part_material = mesh.materials[0] if mesh.materials else None
        if part_material is not None and part_material not in material_slots:
            material_slots.append(part_material)
        material_index = material_slots.index(part_material) if part_material is not None else 0

        for vertex in mesh.vertices:
            all_verts.append(tuple(matrix @ vertex.co))

        for polygon in mesh.polygons:
            all_faces.append(tuple(index + offset for index in polygon.vertices))
            face_materials.append(material_index)

    merged = bpy.data.meshes.new(f"{name}_mesh")
    merged.from_pydata(all_verts, [], all_faces)
    merged.validate(verbose=False)

    for material in material_slots:
        merged.materials.append(material)

    for polygon, material_index in zip(merged.polygons, face_materials):
        polygon.material_index = material_index

    merged.update()

    obj = bpy.data.objects.new(name, merged)
    collection.objects.link(obj)

    for part in parts:
        collection.objects.unlink(part)
        bpy.data.objects.remove(part, do_unlink=True)

    return obj


def shade_smooth_edges(obj: bpy.types.Object, angle_degrees: float = 35.0) -> None:
    """Auto-smooth by angle so cylinders round off but box edges stay crisp."""
    mesh = obj.data
    for polygon in mesh.polygons:
        polygon.use_smooth = True

    # Blender 4.1 removed mesh.use_auto_smooth in favour of a modifier; support
    # both so the script is not pinned to one release.
    if hasattr(mesh, "use_auto_smooth"):
        mesh.use_auto_smooth = True
        mesh.auto_smooth_angle = math.radians(angle_degrees)
    else:
        modifier = obj.modifiers.new("Smooth by Angle", "NODES")
        try:
            node_group = bpy.data.node_groups.get("Smooth by Angle")
            if node_group is not None:
                modifier.node_group = node_group
            else:
                obj.modifiers.remove(modifier)
                for polygon in mesh.polygons:
                    polygon.use_smooth = False
        except Exception:
            obj.modifiers.remove(modifier)


# ---------------------------------------------------------------------------
# Modular station kit
# ---------------------------------------------------------------------------

def build_station_kit(mats: dict) -> list:
    """A 4m modular kit: walls, floors, pillars, doors, stairs, catwalks."""
    collection = ensure_collection("Ashfall_Kit")
    built = []

    # --- solid wall panel with a recessed centre and a scuffed kick plate ----
    parts = [box("Shell", (4.0, 0.30, 4.0), mats["concrete"], collection)]
    parts.append(box("Recess", (3.3, 0.10, 3.3), mats["concrete_dark"], collection, (0, -0.12, 0)))
    parts.append(box("KickPlate", (3.8, 0.06, 0.45), mats["oxidised"], collection, (0, -0.17, -1.72)))
    for i, x in enumerate((-1.75, 1.75)):
        parts.append(box(f"Stud{i}", (0.16, 0.36, 4.0), mats["steel"], collection, (x, 0, 0)))
    built.append(join("Kit_WallPanel_4x4", parts, collection))

    # --- wall panel with a boarded window opening ----------------------------
    parts = []
    parts.append(box("Below", (4.0, 0.30, 1.1), mats["concrete"], collection, (0, 0, -1.45)))
    parts.append(box("Above", (4.0, 0.30, 1.3), mats["concrete"], collection, (0, 0, 1.35)))
    parts.append(box("JambL", (0.9, 0.30, 1.6), mats["concrete"], collection, (-1.55, 0, 0.1)))
    parts.append(box("JambR", (0.9, 0.30, 1.6), mats["concrete"], collection, (1.55, 0, 0.1)))
    parts += frame("Sill", (2.3, 1.8), 0.12, 0.38, mats["oxidised"], collection, (0, 0, 0.1))
    for i, (z, tilt) in enumerate(((-0.45, 4.0), (0.10, -6.0), (0.62, 3.0))):
        parts.append(box(f"Board{i}", (2.5, 0.12, 0.24), mats["timber"], collection, (0, -0.05, z), (0, tilt, 0)))
    built.append(join("Kit_WallPanel_4x4_Breach", parts, collection))

    # --- floor tile with a drainage channel ----------------------------------
    parts = [box("Slab", (4.0, 4.0, 0.30), mats["wet_floor"], collection)]
    parts.append(box("Channel", (0.30, 4.0, 0.08), mats["concrete_dark"], collection, (0, 0, 0.12)))
    for i, (x, y) in enumerate(((-1.85, -1.85), (1.85, -1.85), (-1.85, 1.85), (1.85, 1.85))):
        parts.append(box(f"Edge{i}", (0.28, 0.28, 0.34), mats["concrete_dark"], collection, (x, y, 0.02)))
    built.append(join("Kit_FloorTile_4x4", parts, collection))

    # --- tread-plate catwalk section -----------------------------------------
    parts = [box("Deck", (4.0, 1.6, 0.10), mats["oxidised"], collection)]
    for i in range(8):
        parts.append(box(f"Tread{i}", (0.34, 1.4, 0.04), mats["steel_dark"], collection,
                         (-1.75 + i * 0.5, 0, 0.06), (0, 0, 30)))
    for side, y in enumerate((-0.78, 0.78)):
        for i in range(3):
            parts.append(box(f"Post{side}_{i}", (0.07, 0.07, 1.05), mats["steel_dark"], collection,
                             (-1.9 + i * 1.9, y, 0.55)))
        parts.append(box(f"Rail{side}_top", (4.0, 0.06, 0.06), mats["steel_dark"], collection, (0, y, 1.05)))
        parts.append(box(f"Rail{side}_mid", (4.0, 0.05, 0.05), mats["steel_dark"], collection, (0, y, 0.58)))
    built.append(join("Kit_CatwalkSection_4m", parts, collection))

    # --- pillar ---------------------------------------------------------------
    parts = [box("Shaft", (0.5, 0.5, 4.0), mats["concrete"], collection)]
    parts.append(box("CapTop", (0.75, 0.75, 0.22), mats["concrete_dark"], collection, (0, 0, 1.9)))
    parts.append(box("CapBottom", (0.75, 0.75, 0.22), mats["concrete_dark"], collection, (0, 0, -1.9)))
    parts.append(box("HazardBand", (0.54, 0.54, 0.40), mats["hazard"], collection, (0, 0, -1.4)))
    built.append(join("Kit_Pillar_4m", parts, collection))

    # --- blast door frame with a rolling shutter -----------------------------
    parts = []
    parts.append(box("JambL", (0.45, 0.7, 4.0), mats["steel_dark"], collection, (-2.0, 0, 0)))
    parts.append(box("JambR", (0.45, 0.7, 4.0), mats["steel_dark"], collection, (2.0, 0, 0)))
    parts.append(box("Header", (4.45, 0.7, 0.6), mats["steel_dark"], collection, (0, 0, 1.7)))
    parts.append(box("Shutter", (3.55, 0.22, 3.4), mats["steel"], collection, (0, 0, -0.3)))
    for i in range(6):
        parts.append(box(f"Slat{i}", (3.55, 0.06, 0.10), mats["steel_dark"], collection,
                         (0, -0.13, -1.85 + i * 0.56)))
    parts.append(box("Chevron", (3.3, 0.06, 0.55), mats["hazard"], collection, (0, -0.15, -1.75)))
    parts.append(box("StatusLamp", (0.28, 0.20, 0.28), mats["red"], collection, (2.25, -0.3, 2.1)))
    built.append(join("Kit_BlastDoor_4x4", parts, collection))

    # --- stair flight (0.25m risers over 4m of run) ---------------------------
    parts = []
    steps = 12
    for i in range(steps):
        parts.append(box(f"Step{i:02d}", (2.4, 4.0 / steps, 0.22), mats["oxidised"], collection,
                         (0, -2.0 + (i + 0.5) * (4.0 / steps), i * 0.25)))
    parts.append(wedge("Skirt", (2.3, 4.0, steps * 0.25), mats["concrete_dark"], collection,
                       (0, 0, (steps * 0.25) / 2.0 - 0.14)))
    built.append(join("Kit_StairFlight_4m", parts, collection))

    # --- roof panel with a raised skylight kerb -------------------------------
    parts = [box("Deck", (4.0, 4.0, 0.24), mats["oxidised"], collection)]
    parts += frame("Kerb", (2.6, 2.6), 0.18, 0.30, mats["steel"], collection, (0, 0, 0))
    for part in parts[1:]:
        part.rotation_euler = (math.radians(90), 0, 0)
        part.location = (part.location[0], part.location[2], 0.24)
    parts.append(box("Glazing", (2.2, 2.2, 0.05), mats["teal"], collection, (0, 0, 0.20)))
    built.append(join("Kit_RoofPanel_4x4", parts, collection))

    return built


# ---------------------------------------------------------------------------
# Prop set
# ---------------------------------------------------------------------------

def build_props(mats: dict) -> list:
    collection = ensure_collection("Ashfall_Props")
    built = []

    # --- supply crates ---------------------------------------------------------
    for label, side in (("Small", 0.9), ("Large", 1.5)):
        parts = [box("Body", (side, side, side), mats["steel"], collection)]
        edge = side * 0.08
        for i, (x, y) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
            parts.append(box(f"Corner{i}", (edge, edge, side), mats["steel_dark"], collection,
                             (x * (side / 2 - edge / 2), y * (side / 2 - edge / 2), 0)))
        parts.append(box("Band", (side * 1.02, side * 1.02, side * 0.12), mats["hazard"], collection,
                         (0, 0, -side * 0.22)))
        parts.append(box("Latch", (side * 0.22, 0.05, side * 0.16), mats["oxidised"], collection,
                         (0, -side / 2 - 0.02, side * 0.12)))
        built.append(join(f"Prop_Crate_{label}", parts, collection))

    # --- fuel drum -------------------------------------------------------------
    parts = [cylinder("Body", 0.42, 1.1, mats["rust"], collection, 20)]
    for i, z in enumerate((-0.30, 0.0, 0.30)):
        parts.append(cylinder(f"Rib{i}", 0.45, 0.07, mats["oxidised"], collection, 20, (0, 0, z)))
    parts.append(cylinder("Cap", 0.14, 0.06, mats["steel_dark"], collection, 12, (0.18, 0, 0.56)))
    parts.append(box("HazardLabel", (0.34, 0.02, 0.30), mats["hazard"], collection, (0, -0.42, 0.10)))
    drum = join("Prop_Drum", parts, collection)
    shade_smooth_edges(drum)
    built.append(drum)

    # --- overhead pipe run -----------------------------------------------------
    parts = [cylinder("PipeA", 0.22, 4.0, mats["oxidised"], collection, 16, (0, 0, 0), (0, 90, 0))]
    parts.append(cylinder("PipeB", 0.15, 4.0, mats["rust"], collection, 14, (0, 0.42, -0.10), (0, 90, 0)))
    for i, x in enumerate((-1.6, 0.0, 1.6)):
        parts.append(box(f"Bracket{i}", (0.10, 0.75, 0.55), mats["steel_dark"], collection, (x, 0.20, 0.30)))
        parts.append(cylinder(f"Flange{i}", 0.27, 0.09, mats["steel"], collection, 16, (x, 0, 0), (0, 90, 0)))
    built.append(join("Prop_PipeRun_4m", parts, collection))

    # --- storm generator --------------------------------------------------------
    parts = [box("Plinth", (2.6, 2.6, 0.5), mats["concrete"], collection, (0, 0, 0.25))]
    parts.append(box("PlinthStripe", (2.7, 2.7, 0.04), mats["hazard"], collection, (0, 0, 0.52)))
    parts.append(cylinder("Housing", 0.95, 1.9, mats["steel"], collection, 20, (0, 0, 1.45)))
    parts.append(cylinder("Cap", 1.05, 0.26, mats["oxidised"], collection, 20, (0, 0, 2.45)))
    parts.append(cylinder("CoilRing", 1.0, 0.16, mats["teal"], collection, 20, (0, 0, 1.85)))
    parts.append(cylinder("Exhaust", 0.16, 1.6, mats["rust"], collection, 12, (0.62, 0.62, 3.2)))
    for i in range(4):
        angle = i * math.pi / 2.0
        parts.append(box(f"Fin{i}", (0.10, 0.55, 1.3), mats["steel_dark"], collection,
                         (math.cos(angle) * 0.95, math.sin(angle) * 0.95, 1.45),
                         (0, 0, math.degrees(angle))))
    generator = join("Prop_Generator", parts, collection)
    shade_smooth_edges(generator)
    built.append(generator)

    # --- emergency lamp housing --------------------------------------------------
    parts = [box("Housing", (0.55, 0.42, 0.22), mats["steel_dark"], collection)]
    parts.append(box("Lens", (0.42, 0.32, 0.06), mats["amber"], collection, (0, 0, -0.13)))
    parts.append(box("Hood", (0.60, 0.14, 0.16), mats["oxidised"], collection, (0, -0.22, 0.06)))
    parts.append(box("Bracket", (0.14, 0.14, 0.34), mats["steel_dark"], collection, (0, 0, 0.26)))
    for i, x in enumerate((-0.22, 0.22)):
        parts.append(box(f"Cage{i}", (0.03, 0.34, 0.20), mats["steel_dark"], collection, (x, 0, -0.13)))
    built.append(join("Prop_LampHousing", parts, collection))

    # --- barricade plank ----------------------------------------------------------
    parts = [box("Plank", (2.4, 0.16, 0.28), mats["timber"], collection)]
    for i, x in enumerate((-0.95, 0.95)):
        parts.append(cylinder(f"Nail{i}", 0.035, 0.22, mats["steel_dark"], collection, 8, (x, 0, 0), (90, 0, 0)))
    parts.append(box("SplinterA", (0.30, 0.14, 0.10), mats["timber"], collection, (1.28, 0, 0.06), (0, 12, 0)))
    built.append(join("Prop_BarricadePlank", parts, collection))

    # --- collapsed antenna mast ------------------------------------------------
    parts = [box("Base", (2.2, 2.2, 0.9), mats["concrete"], collection, (0, 0, 0.45))]
    parts.append(box("BaseStripe", (2.3, 2.3, 0.05), mats["hazard"], collection, (0, 0, 0.92)))
    parts.append(cylinder("MastLower", 0.22, 3.2, mats["rust"], collection, 12, (0, 0, 2.5)))
    parts.append(cylinder("MastUpper", 0.17, 3.4, mats["rust"], collection, 12, (1.1, 0.4, 5.2), (34, 0, 20)))
    parts.append(box("DishFrame", (1.9, 1.9, 0.14), mats["steel"], collection, (2.1, 0.8, 6.6), (52, 0, 20)))
    parts.append(cylinder("DishStrut", 0.07, 1.1, mats["steel_dark"], collection, 8, (1.8, 0.7, 6.2), (52, 0, 20)))
    built.append(join("Prop_AntennaMast", parts, collection))

    # --- control console -------------------------------------------------------
    parts = [box("Cabinet", (1.6, 0.75, 1.0), mats["steel"], collection, (0, 0, 0.5))]
    parts.append(box("Desk", (1.7, 0.95, 0.09), mats["steel_dark"], collection, (0, -0.10, 1.02)))
    parts.append(box("Screen", (1.15, 0.10, 0.62), mats["teal"], collection, (0, 0.28, 1.42), (-18, 0, 0)))
    parts.append(box("ScreenBezel", (1.28, 0.14, 0.74), mats["steel_dark"], collection, (0, 0.31, 1.42), (-18, 0, 0)))
    for i in range(5):
        parts.append(box(f"Key{i}", (0.16, 0.16, 0.04), mats["amber"] if i % 2 == 0 else mats["steel_dark"],
                         collection, (-0.5 + i * 0.25, -0.22, 1.08)))
    parts.append(box("Vent", (1.3, 0.05, 0.30), mats["steel_dark"], collection, (0, -0.39, 0.35)))
    built.append(join("Prop_ControlConsole", parts, collection))

    # --- salvage weapon rack -----------------------------------------------------
    parts = [box("Backboard", (1.9, 0.16, 1.4), mats["steel"], collection)]
    parts.append(box("Shelf", (1.9, 0.55, 0.12), mats["oxidised"], collection, (0, 0.30, -0.72)))
    parts.append(box("Sign", (1.5, 0.05, 0.42), mats["teal"], collection, (0, -0.10, 0.34)))
    parts.append(box("Stripe", (1.9, 0.04, 0.20), mats["hazard"], collection, (0, -0.09, 0.62)))
    for i, x in enumerate((-0.7, 0.7)):
        parts.append(box(f"Hook{i}", (0.07, 0.30, 0.07), mats["steel_dark"], collection, (x, -0.20, -0.20)))
    built.append(join("Prop_SalvageRack", parts, collection))

    return built


# ---------------------------------------------------------------------------
# Weapon silhouettes
# ---------------------------------------------------------------------------

def build_weapons(mats: dict) -> list:
    """Three viewmodel silhouettes matching the in-engine weapon set.

    Modelled at 1:1 scale with the muzzle pointing down +Y so a Unity import
    lines up with the procedural viewmodels without hand-rotation.
    """
    collection = ensure_collection("Ashfall_Weapons")
    built = []

    # --- Meridian sidearm ------------------------------------------------------
    parts = [box("Frame", (0.052, 0.20, 0.075), mats["gun_body"], collection, (0, 0.03, 0))]
    parts.append(box("Slide", (0.056, 0.215, 0.052), mats["gun_body"], collection, (0, 0.045, 0.055)))
    parts.append(box("Grip", (0.048, 0.062, 0.135), mats["gun_body"], collection, (0, -0.035, -0.088), (13, 0, 0)))
    parts.append(box("Magazine", (0.036, 0.042, 0.115), mats["steel_dark"], collection, (0, -0.034, -0.092), (13, 0, 0)))
    parts.append(box("TriggerGuard", (0.030, 0.010, 0.045), mats["gun_body"], collection, (0, 0.012, -0.035)))
    parts.append(box("Rail", (0.030, 0.130, 0.010), mats["steel_dark"], collection, (0, 0.088, -0.028)))
    parts.append(box("SightRear", (0.040, 0.014, 0.014), mats["amber"], collection, (0, -0.040, 0.088)))
    parts.append(box("SightFront", (0.010, 0.012, 0.016), mats["amber"], collection, (0, 0.140, 0.090)))
    parts.append(box("AccentStripe", (0.058, 0.090, 0.008), mats["amber"], collection, (0, 0.060, 0.030)))
    parts.append(cylinder("Bore", 0.011, 0.03, mats["steel_dark"], collection, 10, (0, 0.163, 0.055), (90, 0, 0)))
    built.append(join("Weapon_MeridianSidearm", parts, collection))

    # --- Breakwater shotgun -----------------------------------------------------
    parts = [box("Receiver", (0.075, 0.30, 0.098), mats["gun_body"], collection, (0, 0.02, 0))]
    parts.append(cylinder("Barrel", 0.030, 0.52, mats["rust"], collection, 14, (0, 0.40, 0.030), (90, 0, 0)))
    parts.append(cylinder("MagTube", 0.024, 0.46, mats["gun_body"], collection, 12, (0, 0.36, -0.032), (90, 0, 0)))
    parts.append(box("Pump", (0.070, 0.13, 0.062), mats["timber"], collection, (0, 0.30, -0.030)))
    parts.append(box("Stock", (0.055, 0.24, 0.090), mats["timber"], collection, (0, -0.24, -0.052), (-6, 0, 0)))
    parts.append(box("Grip", (0.050, 0.060, 0.120), mats["gun_body"], collection, (0, -0.075, -0.098), (16, 0, 0)))
    parts.append(box("ShellCarrier", (0.090, 0.14, 0.030), mats["rust"], collection, (0, -0.045, -0.062)))
    parts.append(box("HeatShield", (0.058, 0.34, 0.014), mats["rust"], collection, (0, 0.34, 0.062)))
    for i in range(4):
        parts.append(box(f"ShieldSlot{i}", (0.062, 0.045, 0.020), mats["steel_dark"], collection,
                         (0, 0.22 + i * 0.08, 0.062)))
    parts.append(box("Bead", (0.012, 0.012, 0.016), mats["amber"], collection, (0, 0.62, 0.070)))
    shotgun = join("Weapon_Breakwater", parts, collection)
    shade_smooth_edges(shotgun)
    built.append(shotgun)

    # --- Arc-9 rail carbine ------------------------------------------------------
    parts = [box("Receiver", (0.062, 0.34, 0.088), mats["gun_body"], collection, (0, 0.05, 0))]
    parts.append(box("Handguard", (0.056, 0.26, 0.062), mats["steel_dark"], collection, (0, 0.34, -0.006)))
    parts.append(cylinder("Barrel", 0.017, 0.30, mats["steel_dark"], collection, 12, (0, 0.58, 0.010), (90, 0, 0)))
    parts.append(box("RailTop", (0.032, 0.50, 0.012), mats["steel_dark"], collection, (0, 0.22, 0.052)))
    parts.append(box("ChargingHandle", (0.070, 0.075, 0.024), mats["gun_body"], collection, (0, -0.075, 0.042)))
    parts.append(box("Stock", (0.048, 0.22, 0.080), mats["gun_body"], collection, (0, -0.26, -0.014)))
    parts.append(box("Cheek", (0.040, 0.16, 0.026), mats["steel_dark"], collection, (0, -0.24, 0.038)))
    parts.append(box("Grip", (0.046, 0.058, 0.115), mats["gun_body"], collection, (0, -0.070, -0.092), (18, 0, 0)))
    parts.append(box("Magazine", (0.040, 0.075, 0.155), mats["steel_dark"], collection, (0, 0.055, -0.110), (-7, 0, 0)))
    for i in range(3):
        parts.append(cylinder(f"Coil{i}", 0.030, 0.022, mats["teal"], collection, 14,
                              (0, 0.48 + i * 0.075, 0.010), (90, 0, 0)))
    parts.append(box("Optic", (0.036, 0.10, 0.046), mats["gun_body"], collection, (0, 0.055, 0.085)))
    parts.append(box("OpticGlass", (0.028, 0.006, 0.030), mats["teal"], collection, (0, 0.108, 0.085)))
    parts.append(box("PowerCell", (0.030, 0.11, 0.030), mats["teal"], collection, (-0.048, 0.02, 0.006)))
    rifle = join("Weapon_Arc9", parts, collection)
    shade_smooth_edges(rifle)
    built.append(rifle)

    return built


# ---------------------------------------------------------------------------
# Enemy silhouettes
# ---------------------------------------------------------------------------

def build_enemies(mats: dict) -> list:
    """Three enemy silhouettes, built feet-on-origin and facing +Y.

    Readability at distance is the whole brief: the shambler is hunched and
    wide, the sprinter is low and pitched forward, and the brute is a wall.
    """
    collection = ensure_collection("Ashfall_Enemies")
    built = []

    # --- Shambler ---------------------------------------------------------------
    parts = [box("Pelvis", (0.46, 0.30, 0.30), mats["flesh"], collection, (0, 0, 0.92))]
    parts.append(box("Torso", (0.62, 0.36, 0.66), mats["flesh"], collection, (0, 0.06, 1.30), (-14, 0, 0)))
    parts.append(box("Shoulders", (0.82, 0.34, 0.20), mats["flesh"], collection, (0, 0.10, 1.55), (-10, 0, 0)))
    parts.append(box("Head", (0.30, 0.30, 0.34), mats["flesh"], collection, (0, 0.16, 1.72), (-22, 0, 0)))
    parts.append(box("Jaw", (0.22, 0.20, 0.10), mats["corrupt"], collection, (0, 0.28, 1.62), (-22, 0, 0)))
    parts.append(box("ArmL", (0.17, 0.17, 0.78), mats["flesh"], collection, (-0.44, 0.12, 1.16), (16, 0, 6)))
    parts.append(box("ArmR", (0.17, 0.17, 0.78), mats["flesh"], collection, (0.44, 0.12, 1.16), (16, 0, -6)))
    parts.append(box("HandL", (0.20, 0.22, 0.20), mats["corrupt"], collection, (-0.50, 0.26, 0.78)))
    parts.append(box("HandR", (0.20, 0.22, 0.20), mats["corrupt"], collection, (0.50, 0.26, 0.78)))
    parts.append(box("LegL", (0.20, 0.22, 0.86), mats["flesh"], collection, (-0.16, 0, 0.42)))
    parts.append(box("LegR", (0.20, 0.22, 0.86), mats["flesh"], collection, (0.16, 0, 0.42)))
    parts.append(box("FootL", (0.24, 0.34, 0.12), mats["flesh"], collection, (-0.16, 0.06, 0.06)))
    parts.append(box("FootR", (0.24, 0.34, 0.12), mats["flesh"], collection, (0.16, 0.06, 0.06)))
    parts.append(box("SpineVein", (0.09, 0.06, 0.60), mats["corrupt"], collection, (0, -0.16, 1.32), (-14, 0, 0)))
    parts.append(box("ChestCore", (0.16, 0.08, 0.16), mats["corrupt"], collection, (0, 0.25, 1.38), (-14, 0, 0)))
    built.append(join("Enemy_Shambler", parts, collection))

    # --- Sprinter -----------------------------------------------------------------
    parts = [box("Pelvis", (0.34, 0.26, 0.24), mats["flesh"], collection, (0, -0.06, 0.86))]
    parts.append(box("Torso", (0.44, 0.28, 0.56), mats["flesh"], collection, (0, 0.10, 1.16), (-34, 0, 0)))
    parts.append(box("Neck", (0.14, 0.14, 0.20), mats["corrupt"], collection, (0, 0.28, 1.38), (-34, 0, 0)))
    parts.append(box("Head", (0.24, 0.32, 0.22), mats["flesh"], collection, (0, 0.42, 1.44), (-34, 0, 0)))
    parts.append(box("Eyes", (0.20, 0.05, 0.06), mats["corrupt"], collection, (0, 0.57, 1.47), (-34, 0, 0)))
    parts.append(box("ArmL", (0.12, 0.12, 0.62), mats["flesh"], collection, (-0.30, 0.14, 1.10), (42, 0, 8)))
    parts.append(box("ArmR", (0.12, 0.12, 0.62), mats["flesh"], collection, (0.30, 0.14, 1.10), (42, 0, -8)))
    parts.append(box("ThighL", (0.16, 0.18, 0.48), mats["flesh"], collection, (-0.13, -0.02, 0.62), (14, 0, 0)))
    parts.append(box("ThighR", (0.16, 0.18, 0.48), mats["flesh"], collection, (0.13, -0.02, 0.62), (14, 0, 0)))
    parts.append(box("ShinL", (0.12, 0.14, 0.46), mats["flesh"], collection, (-0.13, 0.06, 0.24), (-16, 0, 0)))
    parts.append(box("ShinR", (0.12, 0.14, 0.46), mats["flesh"], collection, (0.13, 0.06, 0.24), (-16, 0, 0)))
    parts.append(box("SpineVein", (0.07, 0.05, 0.52), mats["corrupt"], collection, (0, -0.06, 1.18), (-34, 0, 0)))
    parts.append(box("RibGlowL", (0.05, 0.05, 0.28), mats["corrupt"], collection, (-0.19, 0.14, 1.16), (-34, 0, 0)))
    parts.append(box("RibGlowR", (0.05, 0.05, 0.28), mats["corrupt"], collection, (0.19, 0.14, 1.16), (-34, 0, 0)))
    built.append(join("Enemy_Sprinter", parts, collection))

    # --- Storm Brute ----------------------------------------------------------------
    parts = [box("Pelvis", (1.02, 0.68, 0.52), mats["brute"], collection, (0, 0, 1.18))]
    parts.append(box("Torso", (1.28, 0.82, 1.02), mats["brute"], collection, (0, 0.04, 1.86), (-8, 0, 0)))
    parts.append(box("ChestPlate", (1.06, 0.18, 0.62), mats["rust"], collection, (0, 0.44, 1.98), (-8, 0, 0)))
    parts.append(cylinder("Reactor", 0.24, 0.20, mats["teal"], collection, 14, (0, 0.54, 1.98), (90, 0, 0)))
    parts.append(box("ShoulderL", (0.52, 0.62, 0.56), mats["brute"], collection, (-0.84, 0.02, 2.24), (0, -14, 0)))
    parts.append(box("ShoulderR", (0.52, 0.62, 0.56), mats["brute"], collection, (0.84, 0.02, 2.24), (0, 14, 0)))
    parts.append(box("VentL", (0.10, 0.44, 0.36), mats["corrupt"], collection, (-1.02, 0.02, 2.30)))
    parts.append(box("VentR", (0.10, 0.44, 0.36), mats["corrupt"], collection, (1.02, 0.02, 2.30)))
    parts.append(box("Head", (0.44, 0.46, 0.38), mats["brute"], collection, (0, 0.12, 2.52)))
    parts.append(box("Visor", (0.34, 0.06, 0.09), mats["corrupt"], collection, (0, 0.36, 2.54)))
    parts.append(box("ArmL", (0.34, 0.34, 1.06), mats["brute"], collection, (-0.92, 0.06, 1.60), (8, 0, 5)))
    parts.append(box("ArmR", (0.34, 0.34, 1.06), mats["brute"], collection, (0.92, 0.06, 1.60), (8, 0, -5)))
    parts.append(box("FistL", (0.50, 0.52, 0.44), mats["rust"], collection, (-0.98, 0.20, 1.04)))
    parts.append(box("FistR", (0.50, 0.52, 0.44), mats["rust"], collection, (0.98, 0.20, 1.04)))
    parts.append(box("LegL", (0.42, 0.46, 1.02), mats["brute"], collection, (-0.32, 0, 0.52)))
    parts.append(box("LegR", (0.42, 0.46, 1.02), mats["brute"], collection, (0.32, 0, 0.52)))
    parts.append(box("FootL", (0.50, 0.68, 0.16), mats["rust"], collection, (-0.32, 0.10, 0.08)))
    parts.append(box("FootR", (0.50, 0.68, 0.16), mats["rust"], collection, (0.32, 0.10, 0.08)))
    parts.append(box("SpineRod", (0.14, 0.10, 0.90), mats["corrupt"], collection, (0, -0.44, 1.92), (-8, 0, 0)))
    brute = join("Enemy_StormBrute", parts, collection)
    shade_smooth_edges(brute)
    built.append(brute)

    return built


# ---------------------------------------------------------------------------
# Export
# ---------------------------------------------------------------------------

def _select_only(objects) -> None:
    for obj in bpy.data.objects:
        obj.select_set(False)

    for obj in objects:
        obj.select_set(True)

    if objects:
        bpy.context.view_layer.objects.active = objects[0]


def export_fbx(objects, filepath: str) -> bool:
    """Unity-friendly FBX: Y-up, -Z forward, no unit rescaling."""
    _select_only(objects)

    attempts = [
        dict(
            filepath=filepath,
            use_selection=True,
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_NONE",
            axis_forward="-Z",
            axis_up="Y",
            object_types={"MESH"},
            mesh_smooth_type="FACE",
            use_mesh_modifiers=True,
            bake_anim=False,
            path_mode="COPY",
            embed_textures=False,
        ),
        # Older/newer exporters occasionally drop a keyword; fall back to the
        # minimum that still produces a correctly oriented file.
        dict(filepath=filepath, use_selection=True, axis_forward="-Z", axis_up="Y"),
        dict(filepath=filepath, use_selection=True),
    ]

    for kwargs in attempts:
        try:
            bpy.ops.export_scene.fbx(**kwargs)
            return True
        except TypeError:
            continue
        except Exception as exc:  # noqa: BLE001 - report and keep going
            FAILURES.append(f"FBX {os.path.basename(filepath)}: {exc}")
            return False

    FAILURES.append(f"FBX {os.path.basename(filepath)}: no compatible exporter signature")
    return False


def export_glb(objects, filepath: str) -> bool:
    _select_only(objects)

    attempts = [
        dict(filepath=filepath, export_format="GLB", use_selection=True, export_apply=True),
        dict(filepath=filepath, export_format="GLB", use_selection=True),
        dict(filepath=filepath, export_format="GLB"),
    ]

    for kwargs in attempts:
        try:
            bpy.ops.export_scene.gltf(**kwargs)
            return True
        except TypeError:
            continue
        except Exception as exc:  # noqa: BLE001
            FAILURES.append(f"GLB {os.path.basename(filepath)}: {exc}")
            return False

    FAILURES.append(f"GLB {os.path.basename(filepath)}: no compatible exporter signature")
    return False


def export_group(name: str, objects: list) -> None:
    """Export one logical set as both a combined FBX and a combined GLB."""
    if not objects:
        FAILURES.append(f"{name}: nothing was built")
        return

    fbx_path = os.path.join(FBX_DIR, f"Ashfall_{name}.fbx")
    glb_path = os.path.join(GLB_DIR, f"Ashfall_{name}.glb")

    if export_fbx(objects, fbx_path):
        EXPORTED.append(fbx_path)

    if export_glb(objects, glb_path):
        EXPORTED.append(glb_path)


def export_individually(objects: list) -> None:
    """One FBX per asset, so a single prop can be imported without the rest."""
    per_asset_dir = os.path.join(FBX_DIR, "Individual")
    os.makedirs(per_asset_dir, exist_ok=True)

    for obj in objects:
        path = os.path.join(per_asset_dir, f"{obj.name}.fbx")
        if export_fbx([obj], path):
            EXPORTED.append(path)


# ---------------------------------------------------------------------------
# Sanity check
# ---------------------------------------------------------------------------

# Expected bounding boxes in metres, as (min, max) per axis. Loose enough to
# allow art changes, tight enough to catch a transform that silently collapsed.
EXPECTED_SIZE = {
    "Kit_WallPanel_4x4":    ((3.9, 4.2), (0.2, 0.5), (3.9, 4.2)),
    "Kit_FloorTile_4x4":    ((3.9, 4.2), (3.9, 4.2), (0.2, 0.6)),
    "Kit_StairFlight_4m":   ((2.2, 2.6), (3.8, 4.3), (2.7, 3.3)),
    "Prop_Drum":            ((0.8, 1.0), (0.8, 1.0), (1.0, 1.3)),
    "Prop_AntennaMast":     ((2.0, 5.0), (2.0, 5.0), (6.0, 8.5)),
    "Weapon_MeridianSidearm": ((0.05, 0.10), (0.20, 0.32), (0.20, 0.32)),
    "Weapon_Breakwater":    ((0.06, 0.12), (0.95, 1.20), (0.20, 0.30)),
    "Weapon_Arc9":          ((0.05, 0.12), (1.00, 1.25), (0.24, 0.36)),
    "Enemy_Shambler":       ((0.9, 1.4), (0.5, 0.9), (1.7, 2.1)),
    "Enemy_Sprinter":       ((0.5, 0.9), (0.6, 1.1), (1.4, 1.8)),
    "Enemy_StormBrute":     ((2.0, 2.6), (0.9, 1.5), (2.6, 3.1)),
}


def _bounds(obj: bpy.types.Object) -> tuple:
    """Local-space bounding box size. Objects are pre-baked, so local == world."""
    coords = [v.co for v in obj.data.vertices]
    if not coords:
        return (0.0, 0.0, 0.0)

    return (
        max(c.x for c in coords) - min(c.x for c in coords),
        max(c.y for c in coords) - min(c.y for c in coords),
        max(c.z for c in coords) - min(c.z for c in coords),
    )


def check_dimensions(groups: dict) -> None:
    """Fail loudly when an asset is the wrong physical size.

    A mesh that exports without error but is a third of its intended height is
    the worst kind of bug in an asset pipeline: everything downstream reports
    success. This turns it into a build failure.
    """
    by_name = {obj.name: obj for objects in groups.values() for obj in objects}
    checked = 0

    for name, expected in EXPECTED_SIZE.items():
        obj = by_name.get(name)
        if obj is None:
            FAILURES.append(f"dimension check: '{name}' was not built")
            continue

        size = _bounds(obj)
        checked += 1

        for axis, (value, (low, high)) in enumerate(zip(size, expected)):
            if not (low <= value <= high):
                FAILURES.append(
                    f"dimension check: {name} {'XYZ'[axis]} is {value:.2f}m, "
                    f"expected {low:.2f}-{high:.2f}m "
                    f"(full size {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f})")

    print(f"[verify]    {checked} assets dimension-checked")


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> int:
    print("=" * 72)
    print("Ashfall: Black Meridian - Blender source asset generation")
    print(f"Blender {bpy.app.version_string}")
    print("=" * 72)

    for directory in (OUTPUT_DIR, FBX_DIR, GLB_DIR, BLEND_DIR):
        os.makedirs(directory, exist_ok=True)

    reset_scene()
    mats = build_material_library()
    print(f"[materials] {len(mats)} procedural materials")

    groups = {
        "StationKit": build_station_kit(mats),
        "Props": build_props(mats),
        "Weapons": build_weapons(mats),
        "Enemies": build_enemies(mats),
    }

    total_objects = 0
    total_tris = 0
    for name, objects in groups.items():
        tris = 0
        for obj in objects:
            # Every face is a tri or a quad, so this is exact for this kit.
            tris += sum(len(p.vertices) - 2 for p in obj.data.polygons)

        total_objects += len(objects)
        total_tris += tris
        print(f"[build]     {name:<12} {len(objects):>2} assets, {tris:>6} triangles")
        for obj in objects:
            print(f"              - {obj.name}")

    print(f"[build]     {total_objects} assets, {total_tris} triangles total")

    check_dimensions(groups)

    for name, objects in groups.items():
        export_group(name, objects)

    every_object = [obj for objects in groups.values() for obj in objects]
    export_group("Complete", every_object)
    export_individually(every_object)

    blend_path = os.path.join(BLEND_DIR, "AshfallSourceAssets.blend")
    try:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
        EXPORTED.append(blend_path)
    except Exception as exc:  # noqa: BLE001
        FAILURES.append(f"BLEND: {exc}")

    print("-" * 72)
    on_disk = 0
    total_bytes = 0
    for path in EXPORTED:
        if os.path.isfile(path):
            on_disk += 1
            total_bytes += os.path.getsize(path)
        else:
            FAILURES.append(f"reported but missing on disk: {path}")

    print(f"[export]    {on_disk}/{len(EXPORTED)} files written, {total_bytes / 1024.0:.1f} KiB total")
    print(f"[export]    output directory: {OUTPUT_DIR}")

    if FAILURES:
        print("-" * 72)
        for failure in FAILURES:
            print(f"[FAIL]      {failure}")
        print(f"RESULT: FAILED ({len(FAILURES)} problem(s))")
        return 1

    print("RESULT: OK")
    return 0


if __name__ == "__main__":
    try:
        code = main()
    except Exception:  # noqa: BLE001 - always surface a real traceback in batch
        traceback.print_exc()
        code = 1

    # Blender ignores a plain return value in --background, so exit explicitly.
    sys.exit(code)
