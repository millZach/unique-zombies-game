# Ashfall: Black Meridian — Blender source assets

`generate_assets.py` builds the game's original source-art library from scratch
inside Blender and exports it in formats Unity can import directly.

Everything is modelled procedurally from primitives with procedural materials.
Nothing is downloaded, scanned, traced, or derived from another game's content.

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
- **No rigs or animation.** The enemies are static silhouettes. All enemy motion
  in the game is procedural (gait bob, lean, attack windup) and driven from code.
- **No LODs or lightmap UVs.** At ~4,000 triangles for the entire library this
  has not been necessary.
- **Materials do not survive FBX.** Only slot names transfer. This is a format
  limitation, not a bug.
