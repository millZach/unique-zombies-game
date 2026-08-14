# Ashfall: Black Meridian — Blender source assets

Two scripts, both headless, both deterministic:

| Script | What it does |
| --- | --- |
| `generate_assets.py` | Builds the game's original source-art library from scratch and exports it for Unity. |
| `rig_zombie.py` | Rigs, skins and animates an **approved Meshcaster** zombie mesh, and exports a Unity-ready FBX. |

Everything in `generate_assets.py` is modelled procedurally from primitives with
procedural materials. Nothing is downloaded, scanned, traced, or derived from
another game's content. `rig_zombie.py` authors its five clips from bone
keyframes written in the script; no motion data is copied from anywhere.

Neither script talks to a network, and neither can spend a credit.

---

## Run it

```bash
/snap/bin/blender --background --python Tools/Blender/generate_assets.py
```

From any directory — the script resolves paths relative to itself. It exits `0`
on success and `1` if any asset failed to build, failed a dimension check, or
failed to export. Tested against **Blender 5.2.0 LTS**; the exporter calls fall
back through several argument signatures, so 3.6+ should also work.

Typical run takes a couple of seconds and prints a summary:

```
[materials] 17 procedural materials
[build]     StationKit    8 assets,    908 triangles
[build]     Props        10 assets,   1772 triangles
[build]     Weapons       3 assets,    732 triangles
[build]     Enemies       3 assets,    620 triangles
[build]     24 assets, 4032 triangles total
[verify]    11 assets dimension-checked
[export]    35/35 files written, 1387.9 KiB total
RESULT: OK
```

---

## Output directory

Everything is written to `Tools/Blender/Output/`, which is **git-ignored** — it
is generated, not source. Re-run the script to recreate it.

```
Tools/Blender/Output/
├── FBX/
│   ├── Ashfall_StationKit.fbx      8 modular architecture pieces
│   ├── Ashfall_Props.fbx          10 set-dressing props
│   ├── Ashfall_Weapons.fbx         3 weapon silhouettes
│   ├── Ashfall_Enemies.fbx         3 enemy silhouettes
│   ├── Ashfall_Complete.fbx        all 24 in one file
│   └── Individual/                 one .fbx per asset
├── GLB/                            same groups as glTF 2.0 binary
└── Blend/
    └── AshfallSourceAssets.blend   editable source scene
```

FBX is the Unity-native path and needs no extra packages. GLB is included for
any other engine or for previewing in a browser; **Unity cannot import GLB
without an add-on** (glTFast or similar), so use the FBX files unless you have
already installed one.

---

## What gets built

**Station kit** (4 m modules, origin-centred so they tile on a 4 m grid)

| Asset | Notes |
| --- | --- |
| `Kit_WallPanel_4x4` | Solid wall, recessed centre, scuffed kick plate |
| `Kit_WallPanel_4x4_Breach` | Window opening with torn boards across it |
| `Kit_FloorTile_4x4` | Slab with a drainage channel |
| `Kit_CatwalkSection_4m` | Tread plate deck with handrails |
| `Kit_Pillar_4m` | Column with hazard banding |
| `Kit_BlastDoor_4x4` | Frame, rolling shutter, chevrons, status lamp |
| `Kit_StairFlight_4m` | 12 risers over a 4 m run, with a skirt |
| `Kit_RoofPanel_4x4` | Deck with a raised skylight kerb |

**Props** — `Prop_Crate_Small`, `Prop_Crate_Large`, `Prop_Drum`,
`Prop_PipeRun_4m`, `Prop_Generator`, `Prop_LampHousing`,
`Prop_BarricadePlank`, `Prop_AntennaMast`, `Prop_ControlConsole`,
`Prop_SalvageRack`

**Weapons** — `Weapon_MeridianSidearm`, `Weapon_Breakwater`, `Weapon_Arc9`.
Modelled at 1:1 scale with the muzzle pointing along **+Y** so they align with
the in-engine viewmodel sockets without hand-rotation.

**Enemies** — `Enemy_Shambler` (1.94 m), `Enemy_Sprinter` (1.62 m),
`Enemy_StormBrute` (2.71 m). Feet on the origin, facing **+Y**, matching the
`bodyHeight` values in the corresponding `EnemyDefinition` assets.

Materials use the same palette as `Assets/Ashfall/Scripts/Core/AshfallPalette.cs`,
so the Blender exports and the procedural in-engine kit read as one art
direction rather than two.

---

## Importing into Unity

The game **does not need these files**. The Unity scene is built entirely from
code-generated primitives (`Ashfall ▸ Build Playable Scene`), so the project is
playable from a clean clone without ever running Blender. These exports are an
optional visual upgrade.

To use them:

1. Run the script.
2. Copy or symlink the FBX you want into the project, e.g.
   ```bash
   mkdir -p Assets/Ashfall/Art/Imported
   cp Tools/Blender/Output/FBX/Ashfall_StationKit.fbx Assets/Ashfall/Art/Imported/
   ```
3. In Unity, select the imported asset and set:
   - **Scale Factor** `1`
   - **Convert Units** on
   - **Import Materials** → `Standard` (or extract and re-assign the generated
     URP materials from `Assets/Ashfall/Art/Generated/Materials`)
   - **Generate Colliders** off for weapons and enemies, on for kit pieces
4. Drag the mesh onto the matching object, or swap the `MeshFilter.sharedMesh`
   on a prefab under `Assets/Ashfall/Prefabs/`.

The exports carry material **slots and names** but not textures — the materials
are procedural node graphs, which FBX cannot represent. Assign the URP materials
the project already generates instead of relying on the FBX import.

### Axis convention

FBX is written with `axis_up=Y`, `axis_forward=-Z` and no unit rescaling, which
is exactly what Unity expects. A re-import round-trip through Blender confirms
the sizes survive:

```
Enemy_Shambler:   1.20 x 0.65 x 1.94 m
Enemy_StormBrute: 2.46 x 1.19 x 2.71 m
Weapon_Arc9:      0.10 x 1.10 x 0.30 m
```

---

## Editing the assets

Open `Tools/Blender/Output/Blend/AshfallSourceAssets.blend` to inspect or hand-
edit the result — but treat it as **generated output**. Changes made there are
overwritten on the next run. Edit `generate_assets.py` instead; that file is the
source of truth and is the thing under version control.

Each asset is one merged mesh with per-face material indices. The builder
functions (`box`, `cylinder`, `wedge`, `frame`, `join`) take metres and degrees.

### Dimension checks

`EXPECTED_SIZE` near the bottom of the script asserts a bounding box for eleven
representative assets. This exists because of a real bug caught during
development: reading `matrix_world` on a freshly created object in `--background`
returns a stale identity matrix, so every part merged at the origin. The kit
exported without a single error and was completely the wrong shape. If you add
assets, add a bound — a silent geometry collapse is otherwise invisible.

---

## Known limitations

- **No UV unwrapping.** Meshes ship without UV maps; the in-engine kit generates
  world-scale UVs in C# instead. Add a `smart_project` pass if you need to
  texture these outside Unity.
- **No rigs or animation.** These enemies are static silhouettes and motion for
  them is procedural (gait bob, lean, attack windup), driven from code. Rigging
  is a separate concern handled by `rig_zombie.py` below, and it operates on
  approved Meshcaster meshes, not on these.
- **No LODs or lightmap UVs.** At ~4,000 triangles for the entire library this
  has not been necessary.
- **Materials do not survive FBX.** Only slot names transfer. This is a format
  limitation, not a bug.

---

## `rig_zombie.py` — rigging an approved zombie

Meshy returns a static mesh. This turns one into a skinned, animated,
Unity-ready character.

```bash
# what is there to rig? writes nothing.
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- --check

# rig every enemy slot that has an approved mesh
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- --all

# one slot, from an arbitrary file
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- \
  --slot Enemy_StormBrute --input /path/to/brute.glb

# exercise the whole pipeline with no paid asset at all
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- \
  --self-test --all --output /tmp/ashfall-rig-selftest
```

Exits `0` only when every requested slot was rigged and exported. A slot with no
importable mesh is a failure, printed with the paths it looked in — the script
will not invent a rig for a slot that has no art.

### Where files come from and go

| | Path |
| --- | --- |
| input, default | `Tools/Blender/Input/<slot>/` (git-ignored; written by `Ashfall ▸ Meshcaster: Export Slot Source for Blender`) |
| input, alternate | `Assets/Ashfall/Art/Meshcaster/<slot>/Source/` |
| output, default | `Assets/Ashfall/Art/Meshcaster/<slot>/Rigged/` |

Accepted formats, in preference order: `.fbx`, `.glb`, `.gltf`, `.obj`. Two
files in one slot resolve by sorted path, never by directory order, so two
machines produce the same result.

### What it does, in order

1. **Import** — whichever operator this Blender build accepts, falling back
   through signatures.
2. **Consolidate** — apply transforms, join to one mesh object, keep every
   material slot.
3. **Normalise** — scale to the slot's exact target height, centre on X and Y,
   sit the origin on the floor. Refuses to continue if the model is not standing
   up in Z, unless `--force`.
4. **Armature** — 22 bones: `Root, Pelvis, Spine, Chest, Neck, Head`, and per
   side `Shoulder, UpperArm, LowerArm, Hand` and `UpperLeg, LowerLeg, Foot,
   Toe`. Heights from a proportion table; widths measured off this mesh at the
   95th percentile, then clamped to humanoid plausibility so a shambler's
   knee-length arms cannot drag the hip joints out with them.
5. **Skin** — automatic (bone heat) weights, then *verified*: if more than 2% of
   vertices come back unbound, a deterministic inverse-distance envelope takes
   over — four influences per vertex, weighted by `1/d³` to the bone segment.
   It cannot fail on bad topology because it never looks at topology. Which path
   ran is recorded in the manifest.
6. **Animate** — `Idle`, `Walk`, `Attack`, `HitReact`, `Death` at 30 fps, all
   from bone keyframes. No root translation: the `CharacterController` owns
   position in Unity.
7. **Export** — FBX, Y-up, `-Z` forward, one take per action, leaf bones off,
   textures copied beside the file.
8. **Manifest** — `<slot>_Rigged.rigmanifest.json`: bone list, clip ranges and
   loop flags, vertex and triangle counts, weighting method, source hash,
   Blender version. Unity reads this and refuses to adopt a rig it does not
   fully describe.

### Flags

| Flag | Effect |
| --- | --- |
| `--slot <key>` | `Enemy_Shambler`, `Enemy_Sprinter`, `Enemy_StormBrute`. Repeatable. |
| `--all` | Every enemy slot. Also the default with no `--slot`. |
| `--input <path>` | A file, a slot folder, or a parent of slot folders. |
| `--output <dir>` | Where to write. Required with `--self-test`. |
| `--height <m>` | Override the slot's target height. |
| `--yaw <deg>` | Rotate about Z before rigging. `180` if the enemy walks backwards. |
| `--force` | Rig a model that is not standing upright. |
| `--blend` | Save the `.blend` beside the FBX, to inspect the rig by hand. |
| `--check` | Report which slots have a source. Writes nothing. |
| `--self-test` | Build a proxy humanoid and run the full pipeline on it. |

### Determinism and honesty

Nothing in the script is random or time-dependent, so two runs on the same input
produce the same rig and the same clips.

`--self-test` output is stamped `"source": "self-test-proxy"`,
`"selfTest": true`, and `AshfallZombieRig.IsManifestShippable` rejects it. The
script also refuses to run `--self-test` without an explicit `--output`, so a
proxy cannot be written into a staging slot where it would look like approved
art in the Unity Project view.

A rig produced from any imported file is stamped `"source": "imported-file"` —
never "approved". The script cannot know where a mesh came from, and approval is
a human copying it into the staging slot.

Full workflow, including the Unity half: `Docs/MeshcasterArtPass.md`.
