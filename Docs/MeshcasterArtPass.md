# Meshcaster art pass — guns and enemies

Everything needed to run the six generation jobs by hand and drop the results
into the game: the exact prompts, the settings for each job, the target
dimensions, where the files go, and what it costs.

> **Nothing in this repository spends credits.**
> `AshfallMeshcasterImport` has no network calls, no API key handling and no
> knowledge that Meshy exists. It reads files that a human has already
> generated, approved and copied in. Every credit is spent by a person clicking
> a button in the Meshcaster window that shows the price first.

**Status right now: 0 of 6 slots staged.** No generation has been run. The game
currently ships the procedural bodies built by `AshfallPrefabFactory`, which
were rebuilt from scratch in this pass (lofted, curved-spine bodies rather than
stacked boxes). The Meshcaster slots are wired, tested and empty.

---

## The hard cap

**500 credits maximum for this pass.** Meshcaster's prices, from
`MeshyAdapter` (`PreviewCredits`, `RefineCredits`, `ResizeCredits`):

| Step | Credits | What it is |
| --- | --- | --- |
| Preview | 20 | Untextured geometry. This is the one you judge. |
| Refine | 10 | Textures the approved preview. |
| Resize | 1 | Sets real-world height. Runs behind Approve. |
| **Full chain** | **31** | Per finished asset. |

### Budget

| Line | Jobs | Credits | Running total |
| --- | --- | --- | --- |
| Six assets, full chain | 6 × 31 | 186 | 186 |
| Re-roll allowance — previews only, discarded free | up to 10 × 20 | 200 | 386 |
| Refine + resize for re-rolled assets | up to 3 × 11 | 33 | 419 |
| **Reserve left under the cap** | | **81** | **500** |

Read that as: the planned spend is **186**. Everything above it is headroom for
previews you look at and throw away, which is the only cheap iteration loop
available — **Discard costs nothing**, and it is the correct response to a
preview whose proportions are wrong.

### Ledger

Fill this in as you go. The numbers are the truth of what was spent; do not
reconstruct them from memory later.

| Date | Slot | Step | Credits | Running total | Outcome |
| --- | --- | --- | --- | --- | --- |
| | | | | | |

Running total must never exceed **500**. If a slot needs more than three
previews, stop and keep the procedural body — it is a working fallback, not a
placeholder.

---

## Before you start

1. Open the Meshcaster project at `../meshcaster/unity` in Unity **6000.5.7f1**.
2. **Tools → Meshcaster**. Confirm the API-key status line is green.
3. Check the credit balance shown in the window against your cap.
4. Use the **Single** tab. Batch auto-approval is explicitly *not* wanted here:
   auto-approve refines every preview the moment it finishes, which is the one
   setting that can spend past a budget without a second click.

Leave **approval policy on Manual** for this entire pass.

---

## Shared style prompt

Every prompt below ends with the same style clause. Keep it identical across
all six so the set looks like one game:

> weathered coastal atmospheric research station salvage-tech, storm-corrupted
> industrial materials, muted slate and rust palette with cold teal energy
> accents, readable silhouette, physically plausible proportions, game-ready
> low-poly topology, clean non-overlapping UVs, PBR materials, neutral A-pose,
> no text, no logos, no insignia, no branding, original design

**Do not** name any other game, studio, franchise, or character in a prompt.
The design language above is the whole brief; it is deliberately specific
enough that nothing else is needed.

Negative guidance to include if the model drifts:

> no text, no logos, no watermark, no floating parts, no base, no pedestal,
> no ground plane, no weapon in hand

---

## Job settings (identical for all six)

| Setting | Value | Why |
| --- | --- | --- |
| Input | Text prompt | Image mode is available on the Single tab if you want to feed it a reference view; see below. |
| Delivered polycount | **8,000** for enemies, **6,000** for weapons | Twenty-four concurrent enemies at 8k is ~192k triangles, which this scene carries comfortably. Weapons are viewed at 40 cm, so the budget goes to the silhouette, not the receiver. |
| Height | Per slot, from the table below | Meshcaster's resize step takes a real-world height, so the model arrives at game scale. |
| Collider | **Off** | The game builds its own hitboxes. An imported collider would shadow them, and `AshfallMeshcasterImport` strips any it finds. |
| Approval | **Manual** | The price gate. |

### Optional reference views

The Single tab accepts one to four local PNG/JPEG files as reference. If you
want the generated model to match what is already in the game, use the model
sheets this repository can render for itself:

```bash
xvfb-run -a "$UNITY" -batchmode -projectPath "$PWD" \
  -executeMethod Ashfall.EditorTools.AshfallCapture.CaptureFromCommandLine \
  -captureOut /tmp/ashfall-capture -logFile /tmp/ashfall-capture.log
```

That writes three views per enemy and two per weapon:

| Slot | Reference images to feed |
| --- | --- |
| Shambler | `Enemy_Shambler_205.png` (front 3/4), `_270.png` (side), `_330.png` (back 3/4) |
| Sprinter | `Enemy_Sprinter_205.png`, `_270.png`, `_330.png` |
| Storm Brute | `Enemy_StormBrute_205.png`, `_270.png`, `_330.png` |
| Meridian Sidearm | `VM_MeridianSidearm_270.png` (side), `_215.png` (3/4) |
| Breakwater | `VM_Breakwater_270.png`, `_215.png` |
| Arc-9 | `VM_Arc9_270.png`, `_215.png` |

Image mode costs the same as text mode. Use the side view as the primary/front
image — it is the one that carries the silhouette.

---

## The six jobs

### 1. Meridian Sidearm — `Weapon_MeridianSidearm`

Target length **0.24 m** along Z. Height field: `0.24`.

> A compact semi-automatic service sidearm for a storm research station. Boxy
> slide with shallow rear serrations, squared trigger guard, straight
> single-stack magazine, short accessory rail under a stubby barrel, small
> luminous front and rear sight blades. Dark gunmetal frame with amber painted
> sight dots and a thin amber index stripe along the slide. Salt corrosion
> around the muzzle and grip screws. Weathered coastal atmospheric research
> station salvage-tech, storm-corrupted industrial materials, muted slate and
> rust palette with cold teal energy accents, readable silhouette, physically
> plausible proportions, game-ready low-poly topology, clean non-overlapping
> UVs, PBR materials, neutral A-pose, no text, no logos, no insignia, no
> branding, original design.

### 2. Breakwater Shotgun — `Weapon_BreakwaterShotgun`

Target length **1.02 m** along Z. Height field: `1.02`.

> A heavy pump-action deck shotgun repurposed from ice-clearing duty. Thick
> ribbed wooden fore-end, wide steel receiver with a large ejection port,
> vented heat shield running the length of the barrel, tube magazine below,
> straight wooden stock with a rubber butt pad, elevated brass bead front
> sight, side shell carrier holding loose shells. Deep orange rust bloom over
> blued steel, oil-darkened timber. Weathered coastal atmospheric research
> station salvage-tech, storm-corrupted industrial materials, muted slate and
> rust palette with cold teal energy accents, readable silhouette, physically
> plausible proportions, game-ready low-poly topology, clean non-overlapping
> UVs, PBR materials, neutral A-pose, no text, no logos, no insignia, no
> branding, original design.

### 3. Arc-9 Rifle — `Weapon_Arc9Rifle`

Target length **1.13 m** along Z. Height field: `1.13`.

> A prototype electromagnetic rail carbine built from station spares. Slim
> squared receiver, top accessory rail with a compact boxy optic, tubular
> handguard, three glowing induction coil rings spaced along the exposed
> barrel, curved box magazine, skeletal fixed stock with a raised cheek rest, a
> cylindrical power cell clamped to the left side of the receiver, thick
> insulated cable from cell to coils. Matte dark steel with cold teal glowing
> coil rings and teal optic glass. Weathered coastal atmospheric research
> station salvage-tech, storm-corrupted industrial materials, muted slate and
> rust palette with cold teal energy accents, readable silhouette, physically
> plausible proportions, game-ready low-poly topology, clean non-overlapping
> UVs, PBR materials, neutral A-pose, no text, no logos, no insignia, no
> branding, original design.

### 4. Shambler — `Enemy_Shambler`

Target height **1.85 m** along Y. Height field: `1.85`.

> A slow humanoid figure, hunched heavily forward with a curved spine, broad
> rounded shoulders, head hanging low and pushed ahead of the chest, long heavy
> arms reaching past the knees, thick legs, bare feet. Sodden layered work
> clothing rotted to strips over grey desaturated skin. A single narrow seam of
> cold teal light runs up the spine and a small teal core sits in the sternum;
> everything else is unlit. Standing idle, arms hanging. Weathered coastal
> atmospheric research station salvage-tech, storm-corrupted industrial
> materials, muted slate and rust palette with cold teal energy accents,
> readable silhouette, physically plausible proportions, game-ready low-poly
> topology, clean non-overlapping UVs, PBR materials, neutral A-pose, no text,
> no logos, no insignia, no branding, original design.

### 5. Sprinter — `Enemy_Sprinter`

Target height **1.68 m** along Y. Height field: `1.68`.

> A lean humanoid figure crouched low and pitched steeply forward, shoulders
> ahead of the hips, long narrow skull thrust out in front, folded
> digitigrade legs with long shins and high ankles, thin whipcord arms, small
> hooked claws. Taut grey skin over an exposed ribcage, remnants of a torn
> technician's coverall. Three cold teal light seams glow between the ribs on
> each side, a thin teal line up the spine, and a narrow teal band across the
> eyes. Standing in a ready crouch. Weathered coastal atmospheric research
> station salvage-tech, storm-corrupted industrial materials, muted slate and
> rust palette with cold teal energy accents, readable silhouette, physically
> plausible proportions, game-ready low-poly topology, clean non-overlapping
> UVs, PBR materials, neutral A-pose, no text, no logos, no insignia, no
> branding, original design.

### 6. Storm Brute — `Enemy_StormBrute`

Target height **2.85 m** along Y. Height field: `2.85`.

> A massive armoured humanoid nearly three metres tall, built from bolted
> industrial salvage plate. Huge chamfered pauldrons, a tapering armoured
> barrel chest, a small blocky helmeted head with a narrow horizontal visor
> slit, enormous plated arms ending in blunt slab fists, thick columnar legs,
> wide flat feet. A circular glowing reactor is set into the chest plate behind
> a heavy rust-caked bezel, with a lit conduit rod running down the spine and
> vent slots on the outer shoulders. Scuffed slate-grey plate over deep orange
> rust, cold teal light from the reactor, visor and vents. Standing upright,
> arms at its sides. Weathered coastal atmospheric research station
> salvage-tech, storm-corrupted industrial materials, muted slate and rust
> palette with cold teal energy accents, readable silhouette, physically
> plausible proportions, game-ready low-poly topology, clean non-overlapping
> UVs, PBR materials, neutral A-pose, no text, no logos, no insignia, no
> branding, original design.

---

## Judging a preview before you pay to refine

Refine costs 10 credits and cannot fix geometry. Check, in this order:

1. **Silhouette at thumbnail size.** Squint at the preview thumbnail. The
   shambler must read as bent over, the sprinter as leaning into a run, the
   brute as a wall. If it does not read at 64 px it will not read at 20 m in
   fog.
2. **Proportions against the target size.** A two-metre shambler is a wrong
   shambler even if it looks good, because the hitboxes are built around 1.85.
3. **Base and pedestal.** Meshy likes adding a plinth. A model on a plinth
   floats in game. Discard and re-prompt with the negative guidance.
4. **Limb separation.** Arms fused to the torso cannot be told apart at
   distance and defeat the whole point of the pass.
5. **Weapons only: muzzle end.** The barrel has to terminate cleanly at the
   front; the muzzle flash is parented to a fixed transform.

Discard is free. Use it.

---

## Getting the result into the game

1. In Meshcaster, the finished asset lands in
   `unity/Assets/Meshcaster/Generated/<asset-name>/` — a prefab, a mesh asset
   and its texture maps.
2. Copy **the whole folder's contents** into this repository at:

   ```
   Assets/Ashfall/Art/Meshcaster/<Slot key>/
   ```

   The six slot keys are exactly:

   | Slot key | Asset | Target |
   | --- | --- | --- |
   | `Weapon_MeridianSidearm` | Meridian Sidearm | 0.24 m on Z |
   | `Weapon_BreakwaterShotgun` | Breakwater Shotgun | 1.02 m on Z |
   | `Weapon_Arc9Rifle` | Arc-9 Rifle | 1.13 m on Z |
   | `Enemy_Shambler` | Shambler | 1.85 m on Y |
   | `Enemy_Sprinter` | Sprinter | 1.68 m on Y |
   | `Enemy_StormBrute` | Storm Brute | 2.85 m on Y |

   The folders already exist and are empty. Copy the files, not the folder, so
   the slot folder name stays exactly as above.

3. Let Unity import. Then run **`Ashfall ▸ Meshcaster Art Pass Status`** to
   confirm the slot flipped from `[pending]` to `[staged]`.
4. Run **`Ashfall ▸ Build Playable Scene`** (`Ctrl`/`Cmd`+`Shift`+`B`). The
   prefab factory picks up every staged model, fits it to its target size,
   grounds it, strips any imported colliders, and disables the procedural body
   underneath.
5. Render a model sheet and look at it:

   ```bash
   xvfb-run -a "$UNITY" -batchmode -projectPath "$PWD" \
     -executeMethod Ashfall.EditorTools.AshfallCapture.CaptureFromCommandLine \
     -captureOut /tmp/ashfall-capture -logFile /tmp/ashfall-capture.log
   ```

6. Run validation and the test suites (commands in `README.md`).

### File formats

| Format | Works? | Note |
| --- | --- | --- |
| `.prefab` | **Preferred** | What Meshcaster produces. Keeps the URP material it already built. |
| `.fbx` | Yes | Unity imports it natively. Materials will need reassigning. |
| `.obj` | Yes | Geometry only. |
| `.glb` | **No** | This project does not have `com.unity.cloud.gltfast`. Let Meshcaster do the GLB → prefab conversion; that step is free. |

### Import settings applied automatically

| Setting | Value | Reason |
| --- | --- | --- |
| Scale | Auto-fitted to the target size | Logged when the correction exceeds 2%. Meshcaster's resize gets it close; this makes it exact. |
| Origin | Bottom-centre, on the local floor | Enemies stand on their feet, not in them. |
| Colliders | Stripped | The game builds its own layered hitboxes. |
| Renderers | Registered with `EnemyHealth` | Hit flash and death dissolve drive the imported mesh. |
| Procedural body | Disabled, not deleted | It stays a one-line fallback and keeps the animated transforms alive. |

---

## Known limitations of this integration

Stated plainly, because the alternative is discovering them at round 6.

- **No skinning, no animation.** Meshy returns a static mesh. Enemy motion in
  this game is procedural — gait bob, lean, attack windup, death collapse — and
  it drives the visual root, so an imported model gets all of it. What it does
  not get is limb articulation, because there are no bones to articulate.
- **Weapon sub-part animation is lost.** The slide-cycle and magazine-drop
  animate named child transforms. An imported model is one mesh, so those
  transforms stay alive but hidden and drive nothing visible. The reload tilt
  and the equip slide animate the viewmodel root, so those still apply to the
  imported mesh. Splitting an imported weapon into slide and magazine
  sub-objects is manual work in Blender, not something the importer can infer.
- **Emissive accents may need a material tweak.** Meshy bakes glow into the
  base colour rather than into an emission map. If the coils or the reactor
  read as flat paint, add an emission map to the generated material.
- **One material per model.** Meshy returns a single PBR set. The procedural
  bodies use two (dull flesh, emissive corruption), which is what makes the
  corruption read in fog. Expect the imported version to read slightly flatter
  at distance.

---

## What must never happen

- No script in this repository may call a Meshy endpoint, submit a job, click
  Approve, or read an API key.
- `UserSettings/MeshcasterSettings.json` and `harness/meshy_config.json` hold
  credentials. Never read, copy, print or commit them. `.gitignore` in this
  repository blocks both filenames defensively.
- No generation without a human looking at the price first.
- Do not exceed **500 credits** for this pass.
