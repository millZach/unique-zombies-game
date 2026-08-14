# Meshcaster art pass — guns and enemies

Everything needed to run the six generation jobs by hand and drop the results
into the game: the exact prompts, the settings for each job, the target
dimensions, where the files go, and what it costs.

> **Nothing in this repository spends credits.**
> `AshfallMeshcasterImport` has no network calls, no API key handling and no
> knowledge that Meshy exists. It reads files that were already generated,
> approved and copied in. Every credit is spent outside this repository, by the
> Meshcaster window or the Meshcaster harness, against a price printed first.
>
> **2026-08-14 — how this pass was actually run.** Zach, who owns both
> repositories, authorised an agent to run the generation through the
> Meshcaster harness (`harness/batch_test.py submit` / `refine`) under a
> 500-credit cap, explicitly overriding the "a human clicks the priced button"
> rule below. That authorisation was for this pass. It is recorded here rather
> than quietly applied, because the rule it overrides is stated three times in
> this document and a reader who found the ledger filled in without this note
> would be right to distrust it.

**Status right now: 6 of 6 slots staged, 3 of 3 zombies rigged.** The pass was
run on 2026-08-14 and cost **180 credits** (see the ledger). All six slots hold
a refined, textured Meshy asset converted to FBX; all three enemies carry a
22-bone rig with five original clips. The procedural bodies built by
`AshfallPrefabFactory` are still present and still the fallback — the importer
disables them rather than deleting them, so removing a slot folder restores the
old look with no code change.

Check it yourself, at any time, without spending anything:

```bash
UNITY=/home/zach/Unity/Hub/Editor/6000.5.7f1/Editor/Unity
$UNITY -batchmode -nographics -projectPath "$PWD" \
  -executeMethod Ashfall.EditorTools.AshfallMeshcasterImport.ReportFromCommandLine \
  -logFile /tmp/ashfall-meshcaster.log
grep -A 14 MESHCASTER_ART_PASS_STATUS /tmp/ashfall-meshcaster.log
```

---

## The whole route, end to end

There are two halves. The first spends money and only a human may run it. The
second is free, scriptable, and already built.

```
  OUTSIDE THIS REPOSITORY                          THE REPOSITORY
  ──────────────────────────                       ──────────────
  1  prompt ──► Preview   (20 cr, priced)
  2  look at the preview  (discarding is free)
  3  Refine               (10 cr, priced)
  4  convert the refined GLB into ──────────────►  Assets/Ashfall/Art/Meshcaster/<slot>/Source/
       blender --background --python                        │
         Tools/Blender/glb_to_fbx.py                        │
                                                            ▼
                                    5  blender --background --python Tools/Blender/rig_zombie.py -- --all
                                                          │   writes <slot>/Rigged/<slot>_Rigged.fbx + manifest
                                                          ▼
                                    6  Ashfall ▸ Meshcaster: Adopt Rigged Zombies
                                                          │   Generic rig, loop flags, Animator Controller
                                                          ▼
                                    7  Ashfall ▸ Build Playable Scene
                                                              rigged body in, procedural body off
```

Steps 4–7 spend nothing and can be re-run as often as you like. Weapons stop at
step 4: they are static meshes and are not rigged.

Step 1–3 can be driven either by the Meshcaster Unity window (which prices each
click) or by the Meshcaster harness (`harness/batch_test.py submit` / `refine`,
which prices each batch and needs `--yes`). The 2026-08-14 pass used the
harness; see the note at the top of this document about who authorised that.

Step 4 exists because Meshy returns a `.glb` and this project cannot import one
(no `com.unity.cloud.gltfast`). `Tools/Blender/glb_to_fbx.py` is the bridge:

```bash
# weapons -- --align length puts the barrel on Unity Z, --bake-axes puts the
# up-axis conversion in the vertex data (see below for why that matters)
/snap/bin/blender --background --python Tools/Blender/glb_to_fbx.py -- \
  --slot Weapon_Arc9Rifle --align length --bake-axes \
  --input /path/to/refined/model_smoothed.glb

# enemies -- leave the axes alone; rig_zombie.py reads this file back into
# Blender and wants Blender's own Z-up space
/snap/bin/blender --background --python Tools/Blender/glb_to_fbx.py -- \
  --slot Enemy_Shambler --input /path/to/refined/model_smoothed.glb
```

It writes `<slot>/Source/<slot>.fbx`, the maps beside it in `Source/Textures/`
capped at 2048 px and prefixed with the slot key, and a `_convert.json` recording
what it measured and what it rotated. It never writes `Rigged/`.

**Feed it `model_smoothed.glb`, not `model_decimated.glb`.** On a refined asset
the harness's decimated copy comes out with zero materials and zero images —
measured on this pass, 504 KB against 21 MB — because quadric collapse has no
notion of a UV seam. The harness README says textured assets are skipped for
decimation; on this run they were not. The smoothed copy is the one that keeps
the four PBR maps the refine was paid for.

**`--bake-axes` is not cosmetic.** Blender's FBX exporter normally puts the
Z-up-to-Y-up conversion in a -90° X rotation on the FBX root.
`AshfallMeshcasterImport.TryAttach` sets `localRotation = Quaternion.Euler(slot.Euler)`,
which throws that rotation away — so an unbaked weapon arrives in Blender's axis
order and `FitToSlot` measures its grip height as its length. Measured on this
pass: the sidearm read 0.014 m on Z and was scaled ×17.1; with `--bake-axes` it
reads 1.906 m and is scaled ×0.126 to hit its 0.24 m target.

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

### Price checkpoints

Three moments in the pass are worth stopping at. They are placed where the next
click is the expensive one, not after it.

| Checkpoint | When | Spent so far | What to confirm before continuing |
| --- | --- | --- | --- |
| **A — after the first preview** | One Generate, one look | 20 | The style clause landed. If the first preview is not recognisably this game's palette and material language, fix the prompt now; the other five inherit the same clause. |
| **B — after the first full asset** | First Approve completes | 31 | It imported, fitted its target size, and looks right in `Build Playable Scene`. This is the cheapest possible proof that the whole route works. |
| **C — after all six previews** | Six Generates, none approved | 120 | Six previews you would pay to refine. Any you would not, discard and re-roll here — before you have spent a single refine credit. |
| **Cap** | Any time | 500 | Hard stop. |

Checkpoint C is the one that saves money: **Discard is free**, and discarding at
120 credits costs nothing beyond the previews you already have.

### Ledger

Fill this in as you go. The numbers are the truth of what was spent; do not
reconstruct them from memory later. The plan rows are pre-filled; add a row per
actual click and keep the running total honest.

Opening balance **1069**. Closing balance **889**. Measured spend **180**, which
is the balance delta and matches the estimate exactly — no task was charged for
that did not deliver, and nothing else touched the account meanwhile.

Batches, not individual clicks: the harness submits three at a time and reads
the account balance either side, so the delta is per batch and the per-slot
credits are the price list.

| # | Date | Slot | Step | Credits | Running total | Balance after | Outcome |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 2026-08-14 | Meridian Sidearm | Preview | 20 | 20 | | Succeeded, 8,648 tri, 1 body |
| 2 | 2026-08-14 | Breakwater Shotgun | Preview | 20 | 40 | | Succeeded, 15,097 tri, 1 body |
| 3 | 2026-08-14 | Arc-9 Rifle | Preview | 20 | 60 | 1009 | Succeeded, 11,798 tri, 18 bodies |
| 4 | 2026-08-14 | Shambler | Preview | 20 | 80 | | Succeeded, 17,019 tri |
| 5 | 2026-08-14 | Sprinter | Preview | 20 | 100 | | Succeeded, 17,734 tri |
| 6 | 2026-08-14 | Storm Brute | Preview | 20 | 120 | 949 | Succeeded, 10,730 tri ← checkpoint C |
| 7 | 2026-08-14 | Meridian Sidearm | Refine | 10 | 130 | | Succeeded, 4 PBR maps |
| 8 | 2026-08-14 | Breakwater Shotgun | Refine | 10 | 140 | | Succeeded, 4 PBR maps |
| 9 | 2026-08-14 | Arc-9 Rifle | Refine | 10 | 150 | 919 | Succeeded, 4 PBR maps |
| 10 | 2026-08-14 | Shambler | Refine | 10 | 160 | | Succeeded, 4 PBR maps |
| 11 | 2026-08-14 | Sprinter | Refine | 10 | 170 | | Succeeded, 4 PBR maps |
| 12 | 2026-08-14 | Storm Brute | Refine | 10 | **180** | 889 | Succeeded, 4 PBR maps |
| | | | *no re-rolls were run* | 0 | **180** | | 320 of the cap unspent |

**Planned spend: 180. Actual: 180. Cap: 500. Unspent headroom: 320.**

No **Resize** step appears above and none was run: resize is a Unity-plugin
operation and this harness has no equivalent. Its job — landing the model at
game scale — is done for free instead, by `rig_zombie.py` normalising each
enemy to its target height in Blender and by `AshfallMeshcasterImport.FitToSlot`
measuring and fitting every slot in Unity. That saves the 6 credits the original
plan allotted to it, which is why the actual is 180 rather than 186.

### Provider task ids

Kept so a result can be traced back to the job that produced it. Meshy deletes
outputs after three days, so these are a record, not a download link.

| Slot | Preview task id | Refine task id |
| --- | --- | --- |
| `Weapon_MeridianSidearm` | `01a00173-143d-7c0f-a594-ee9de04d2ae2` | `01a0017a-9520-7df9-995d-a974d8693796` |
| `Weapon_BreakwaterShotgun` | `01a00173-18ef-7514-96bb-b42d9ab143a6` | `01a0017a-955f-7dfa-9883-a228939b2a4b` |
| `Weapon_Arc9Rifle` | `01a00173-1ddf-74ca-b014-301ab9ea4b69` | `01a0017a-959d-765d-90de-c14d3bed9b65` |
| `Enemy_Shambler` | `01a00175-a899-73fb-b7f4-74e792670b73` | `01a0017c-fc5a-7582-8f24-1ea2a2716b21` |
| `Enemy_Sprinter` | `01a00175-ad9c-75ce-99ea-594a4ab53cc2` | `01a0017c-fc98-76e4-a3f5-cceb0c1e980b` |
| `Enemy_StormBrute` | `01a00175-b4dd-73fd-b2f5-b2e3eabe7fd4` | `01a0017c-fcd3-7e8d-a281-b85684fdf53a` |

Settings used: model `meshy-6`, `model_type=lowpoly`, `topology=quad`,
`should_remesh=true`, target polycount 6,000 weapons / 8,000 enemies.

### Outcome per slot

| Slot | Geometry | On brief? |
| --- | --- | --- |
| `Weapon_MeridianSidearm` | good, one body, clean muzzle | yes — slate/gunmetal, amber index line |
| `Weapon_BreakwaterShotgun` | good, one body, clean muzzle | **partly** — palette drifted: mint-green receiver and bright orange timber rather than blued steel and oil-darkened wood. No vented heat shield. |
| `Weapon_Arc9Rifle` | good; one body plus 26 decimation shards, all under 2.8% of the diagonal | mostly — teal reads as one roundel on the receiver, not three coil rings on the barrel |
| `Enemy_Shambler` | good, hunched, limbs separated | yes — grey skin, rotted layers, teal spine seam |
| `Enemy_Sprinter` | good, crouched and pitched forward | **partly** — none of the teal rib/spine/eye seams appeared; it is monochrome grey-blue |
| `Enemy_StormBrute` | good, reads as a wall | yes — slate plate, orange rust, teal reactor |

The two "partly" rows are texture-pass misses, not geometry misses, so a re-roll
would be re-rolling the mesh to fix the paint. A cheaper fix for both is an
emission map on the generated material, which is the limitation already noted at
the foot of this document. Re-rolling both slots end to end would cost 2 × 30 =
**60 credits**, taking the pass to 240 of 500.

Re-roll accounting, so a bad run cannot creep past the cap without you noticing:

| Running total | You have room for |
| --- | --- |
| ≤ 186 | everything below |
| ≤ 300 | 5 more previews *and* 3 more approves |
| ≤ 400 | 4 more previews, or 2 previews + 2 approves |
| ≤ 460 | 2 more previews |
| ≤ 489 | 1 more preview — and nothing after it |
| > 489 | **stop.** A preview you cannot afford to approve is 20 credits of nothing. |

If a slot needs more than three previews, stop and keep the procedural body —
it is a working fallback, not a placeholder.

---

## Before you start

1. Open the Meshcaster project at `../meshcaster/unity` in Unity **6000.5.7f1**.
2. **Tools → Meshcaster**. Confirm the API-key status line is green.
3. Check the credit balance shown in the window against your cap. Write the
   opening balance at the top of the ledger; the ledger's running total and the
   window's balance should move together, and if they do not, stop.
4. Use the **Single** tab. Batch auto-approval is explicitly *not* wanted here:
   auto-approve refines every successful preview the moment it finishes, which
   is the one setting that can spend past a budget without a second click.

Leave **approval policy on Manual** for this entire pass.

### The clicks, exactly

Per slot, in the Single tab:

| # | Control | What to set | Price shown |
| --- | --- | --- | --- |
| 1 | Input | **Text prompt** (or Image; see below) | — |
| 2 | Prompt field | paste the slot's prompt from this document, whole | — |
| 3 | Delivered polycount | **8000** enemies, **6000** weapons | — |
| 4 | Height (m) | the slot's number from the table below | — |
| 5 | Collider | **Off** | — |
| 6 | Approval policy | **Manual** | — |
| 7 | **Generate** | read the price, then click | **20** |
| 8 | wait | Preview lands at *Awaiting Approval* | — |
| 9 | judge it | the five checks further down | — |
| 10a | **Discard** | if it fails any check | **0** |
| 10b | **Approve** | read the price, then click | **11** |
| 11 | wait | Refine → Resize → download → prefab | — |

Then copy the finished folder into this repository and carry on with steps 5–8
of the route at the top of this document.

> **Steps 7 and 10b are the only steps that spend money.** The design of both
> projects is that the price is seen before either happens — by a person in this
> window, or printed by the harness before a batch it will not run without
> `--yes`. Nothing automates its way past that. Everything downstream of step 4
> is free. See the note at the top of this document for how the 2026-08-14 pass
> was authorised to run the harness instead of this window.

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
| `.glb` | **No** | This project does not have `com.unity.cloud.gltfast`, so a `.glb` under `Assets/` is an inert file Unity builds no mesh from. Convert it first with `Tools/Blender/glb_to_fbx.py`; that step is free. Do not commit the `.glb` itself. |

### Import settings applied automatically

| Setting | Value | Reason |
| --- | --- | --- |
| Scale | Auto-fitted to the target size | Logged when the correction exceeds 2%. Meshcaster's resize gets it close; this makes it exact. |
| Origin | Bottom-centre, on the local floor | Enemies stand on their feet, not in them. |
| Colliders | Stripped | The game builds its own layered hitboxes. |
| Renderers | Registered with `EnemyHealth` | Hit flash and death dissolve drive the imported mesh. |
| Procedural body | Disabled, not deleted | It stays a one-line fallback and keeps the animated transforms alive. |

---

## Rigging and animating the three zombies

Meshy returns a static mesh with no bones. This repository builds the rig
itself, in Blender, from the approved mesh — free, repeatable, and offline.

```bash
# 5. Unity: get the approved geometry out where Blender can read it.
#    Menu:  Ashfall ▸ Meshcaster: Export Slot Source for Blender
#    Headless:
$UNITY -batchmode -nographics -projectPath "$PWD" \
  -executeMethod Ashfall.EditorTools.AshfallZombieRig.ExportSlotSourcesFromCommandLine \
  -logFile /tmp/ashfall-slot-source.log

# 6. Blender: rig, skin, animate, export. Add --check first to see what it found.
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- --check
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- --all

# 7. Unity: import settings, loop flags, Animator Controller.
#    Menu:  Ashfall ▸ Meshcaster: Adopt Rigged Zombies
$UNITY -batchmode -nographics -projectPath "$PWD" \
  -executeMethod Ashfall.EditorTools.AshfallZombieRig.AdoptRiggedFromCommandLine \
  -logFile /tmp/ashfall-adopt.log

# 8. Unity: rebuild the prefabs and the scene.
$UNITY -batchmode -nographics -projectPath "$PWD" \
  -executeMethod Ashfall.EditorTools.AshfallProjectBuilder.BuildFromCommandLine \
  -logFile /tmp/ashfall-build.log
```

If a slot has no approved mesh, step 6 says so and exits non-zero for that slot.
It never invents a rig.

### What the rig is

22 bones, fitted to each model's own measured proportions:

```
Root ─ Pelvis ─ Spine ─ Chest ─ Neck ─ Head
                  │        └─ Shoulder.L/R ─ UpperArm ─ LowerArm ─ Hand
                  └─ UpperLeg.L/R ─ LowerLeg ─ Foot ─ Toe
```

Skinning tries Blender's automatic (bone heat) weights first and checks the
result. Generated meshes are frequently not manifold — loose parts, interior
shells, zero-area faces — and heat weighting fails on exactly those. When more
than 2% of vertices come back unbound, a deterministic inverse-distance envelope
takes over: four influences per vertex, weighted by distance to the bone
segment. Which one ran is recorded in the manifest, so you never have to guess.

### The five clips

Authored from bone keyframes in `rig_zombie.py`, from this project's own curves.
No motion data is copied from anywhere.

| Clip | Frames @ 30 fps | Loops | Reads as |
| --- | --- | --- | --- |
| `Idle` | 60 (2.0 s) | yes | breathing, slow weight shift |
| `Walk` | 40 (1.33 s) | yes | one full gait cycle, arms counter-swinging |
| `Attack` | 30 (1.0 s) | no | windup to frame 11, swing lands frame 17 |
| `HitReact` | 18 (0.6 s) | no | spine-and-head snap back, then settle |
| `Death` | 45 (1.5 s) | no | knees buckle, fold forward, land low |

Per-archetype character comes from one number, `LEAN` — the resting forward
hunch, 26° shambler, 34° sprinter, 8° brute — which every clip is written
against. That is what makes a shambler's walk read differently from a
sprinter's without three separate sets of curves.

**No root motion.** The `CharacterController` owns position; a clip that also
moved the body would double every step and slide the feet. Root translation is
locked at export and again at import.

### How it reaches the game

`ZombieAnimator` on the enemy root cross-fades to states from `EnemyBrain.State`
and `EnemyHealth`:

| Brain state | Clip |
| --- | --- |
| dead or dying | `Death` |
| `AttackWindup`, `AttackRecover`, `TearBarricade` | `Attack` |
| `Stagger`, or a recent hit | `HitReact` |
| `Chase` while moving | `Walk` |
| anything else | `Idle` |

An attack outranks a flinch, matching the brain: a hit past the halfway point of
a windup does not cancel the swing, so it must not cancel the animation either.

When the bridge takes over it turns off `EnemyBrain.ProceduralGaitEnabled` and
`EnemyHealth.ProceduralDeathCollapse`, and retimes the death collapse to the
death clip's own length so the pool does not recycle a body mid-fall. Everything
else — hit flash, hitboxes, salvage, the alive count, pooling — is untouched.

If anything is missing it degrades one step at a time: no `Walk` → `Idle`, no
`Idle` → the procedural gait comes back. There is no state in which an enemy
stands in a T-pose.

### When it will not verify

A slot is adopted only when *all* of this holds. `Adopt Rigged Zombies` prints
the reason when it does not:

| Reason | Fix |
| --- | --- |
| `no rig manifest` | Step 6 has not run for this slot. |
| `manifest is a self-test proxy` | `--self-test` output was copied into a real slot. Delete it. |
| `imported model has no SkinnedMeshRenderer` | The FBX exported without the skin; check step 6's log for the weighting line. |
| `imported model has no valid avatar` | Unity could not build a Generic avatar; the FBX has no armature. |
| `clip '<name>' is missing` | An action did not survive the FBX. Re-run step 6. |

### If the model comes in wrong

| Symptom | Flag |
| --- | --- |
| script refuses: "not standing up in Blender's Z-up space" | re-export upright, or `--force` to rig it lying down anyway |
| enemy walks backwards | `--yaw 180` |
| enemy faces sideways | `--yaw 90` or `--yaw -90` |
| want to see the rig by hand | `--blend`, then open the `.blend` beside the FBX |

Exercise the whole pipeline with no paid asset at all:

```bash
/snap/bin/blender --background --python Tools/Blender/rig_zombie.py -- \
  --self-test --all --output /tmp/ashfall-rig-selftest
```

That builds a blocky proxy humanoid, rigs it, animates it and exports it. Its
manifest is stamped `"selfTest": true` and `AshfallZombieRig` refuses to ship
it, so it can never be mistaken for approved art. It also refuses to write into
the staging folders at all without an explicit `--output`.

---

## Known limitations of this integration

Stated plainly, because the alternative is discovering them at round 6.

- **The rig is generic, not authored.** 22 bones placed from a proportion table
  and the model's measured widths. It will look right on a humanoid and wrong on
  something that is not one — a sprinter with true digitigrade legs gets a
  human's knee placement, because that is what the table knows. Judge the walk
  cycle before you judge the mesh.
- **Envelope weights are the likely path, not the exception.** Meshy geometry
  usually fails bone-heat weighting. Envelope weights are smoother and less
  anatomically sharp; at the distance and fog this game shows enemies at, that
  has not been the readable difference. Up close it will be.
- **Weapons are not rigged.** Three static meshes, by design.
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
  Approve, or read an API key. This stayed true through the 2026-08-14 pass:
  the spending ran in the Meshcaster harness, in the other repository.
- `UserSettings/MeshcasterSettings.json` and `harness/meshy_config.json` hold
  credentials. Never read, copy, print or commit them. `.gitignore` in this
  repository blocks both filenames defensively.
- No generation without the price being read first.
- No agent, script, cron job or CI step may spend credits **on its own
  initiative**. A budget is not a mandate. The 2026-08-14 pass was run by an
  agent because Zach, who owns both repositories, asked for exactly that and
  named the cap; absent that instruction the correct outcome is prepared jobs
  and an unspent balance, not a workaround. An instruction to spend covers the
  pass it was given for and does not roll forward.
- Do not exceed **500 credits** for this pass. 180 of it is now spent.
- Never describe a slot as generated, rigged or animated unless the file is on
  disk and a command in this document printed it. `Meshcaster Art Pass Status`
  and `Adopt Rigged Zombies` are the two things that count as evidence.
