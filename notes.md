# Ashfall: Black Meridian — build notes

## 2026-08-14 — Two tests that only fail once the art shows up

Built the zombie rigging pipeline: Blender takes an approved mesh, fits a
22-bone humanoid to its own measured proportions, skins it, writes five clips,
exports a Unity FBX. Wired the Unity end and it all worked first try — 100
edit-mode and 28 play-mode tests green. Then I actually staged a rigged model
and re-ran the suite, and two *pre-existing* tests failed. `MeasureLocal` in
`GeneratedArtTests` only walked `MeshFilter`s, so a `SkinnedMeshRenderer` body
measured 0.00 m tall against a 1.85 m definition. And the "procedural body is
visible" test asked `HasApprovedModel`, which only knows about static meshes, so
a slot with *only* a rigged model looked unstaged while its procedural body was
correctly hidden. Neither would have fired until Zach staged his first paid
asset, which is the worst possible time to find them. Lesson: a test that guards
"feature X is absent" needs to be run once with X present, or it is only testing
the empty case.

Also learned that Blender 5.x removed `action.fcurves` — slotted actions moved
the curves into `action.layers[].strips[].channelbags[].fcurves`. One helper
that reaches through both, and the script runs on either.

## 2026-08-13 — A MonoBehaviour in the wrong file cost every head shot

Spotted `The referenced script on this Behaviour (Game Object 'Hitbox_Body') is
missing!` scrolling past in a play-mode test log. `DamageRelay` — the component
that scales damage per hitbox and flags head shots as critical — was declared
inside `Damage.cs` alongside the `IDamageable` interface. Unity will only
deserialize a MonoBehaviour from a file named after its class, so every hitbox
on every enemy prefab loaded with a null component. The game still dealt damage,
because `GetComponentInParent<IDamageable>()` fell through to `EnemyHealth`, so
nothing crashed and nothing looked wrong — head shots just quietly did body
damage on all three enemy types. Fix was moving one class into `DamageRelay.cs`.
Then I added a validation pass that walks the scene and all 13 prefabs looking
for null components, plus one that asserts every enemy prefab has a critical
hitbox on the right layer, because a bug that only shows up as an editor warning
will happen again.

## 2026-08-13 — Stairs that bake as walls

Spent about two hours on "the roof is unreachable" in the nav bake. Wrote a
diagnostic that flood-fills the graph and prints each disconnected island's
bounding box and zone mix, and it pointed straight at the problem in one run:
the catwalk stairs at 34.6° produced zero nodes. Cause was geometric. The node
validity check places a capsule 0.50 m above the sampled floor point, but on an
inclined surface the *perpendicular* clearance is 0.50 × cos(slope) — at 34.6°
that is 0.412 m against a 0.414 m probe radius. Failed by two millimetres. The
28.6° roof ramp passed by an equally thin margin, which is why half the route
worked. Fixed both ends: lifted the probe and shrank its radius so ramps up to
~45° are safe, and shallowed the flight to 27°. Also swapped per-step colliders
for one sloped box — stacked 0.25 m tread slabs are what created the problem in
the first place.

Second, smaller version of the same lesson: a 3.2 m-wide catwalk only catches
one row of a 2 m sample grid, and the roof ramp happened to overhang exactly
that row. Widened the deck to 4.4 m.

## 2026-08-13 — Blender exported 24 assets at the wrong size, silently

`generate_assets.py` ran clean, wrote 35 files, reported OK. The shambler was
0.86 m tall instead of 1.94 m. In `--background` nothing evaluates the depsgraph
between creating an object and reading `matrix_world`, so it was still identity
and every part of every merged asset had collapsed to the origin. The union of
centred boxes happens to look like a plausible mesh, so nothing downstream
complained. Fix was building the matrix from `location`/`rotation_euler`/`scale`
directly instead of trusting `matrix_world`. Added an `EXPECTED_SIZE` table that
bounds-checks eleven representative assets and fails the run — an asset pipeline
that reports success while producing the wrong shape is worse than one that
crashes.

## 2026-08-13 — Two wall openings that never lined up

Validation caught that the generator room was unreachable with every door open.
The wall-gap helper takes offsets relative to each wall's own centre, and I had
passed world coordinates: the generator's west opening landed at z = 13 while
the courtyard's east opening was at z = 0, so the two rooms shared no doorway at
all. Same mistake on the lab's west wall (offset 28 on a wall whose half-length
is 14, so the gap fell entirely outside the wall and produced no opening).
Separately, each room's floor stops at its own wall line, which left a 1 m strip
of empty air in every doorway — enough to drop the player through and to sever
the nav graph. Added explicit threshold slabs. Writing the reachability
assertions before the level was finished is the only reason any of this was
found before playtesting.
