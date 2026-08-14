# Meshcaster staging

Drop **approved** Meshcaster output into the matching folder, then run
`Ashfall > Build Playable Scene`. Each folder takes a Unity prefab (what
Meshcaster produces) or an FBX. Raw `.glb` is not imported by Unity in this
project -- let Meshcaster convert it, and copy the generated folder here.

| Folder | Asset | Target size |
| --- | --- | --- |
| `Enemy_Shambler` | Shambler | 1.85 m on Y |
| `Enemy_Sprinter` | Sprinter | 1.68 m on Y |
| `Enemy_StormBrute` | Storm Brute | 2.85 m on Y |
| `Weapon_MeridianSidearm` | Meridian Sidearm | 0.24 m on Z |
| `Weapon_BreakwaterShotgun` | Breakwater Shotgun | 1.02 m on Z |
| `Weapon_Arc9Rifle` | Arc-9 Rifle | 1.13 m on Z |

Empty folders are fine: every slot falls back to the procedural body.

## Rigging the three enemies

Enemy slots take one more step, which spends nothing:

1. `Ashfall > Meshcaster: Export Slot Source for Blender`
2. `blender --background --python Tools/Blender/rig_zombie.py -- --all`
3. `Ashfall > Meshcaster: Adopt Rigged Zombies`
4. `Ashfall > Build Playable Scene`

Step 2 writes `<slot>/Rigged/`. Nothing else may be put in that folder --
it is generated, and the static-mesh importer deliberately ignores it.

Prompts, import settings and the credit ledger are in `Docs/MeshcasterArtPass.md`.
