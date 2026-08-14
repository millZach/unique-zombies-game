using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.EditorTools;
using Ashfall.Weapons;

namespace Ashfall.Tests
{
    /// <summary>
    /// What the scene builder actually produced, checked against what the rest
    /// of the game assumes.
    ///
    /// These are the tests that would have caught the failure mode this project
    /// is most exposed to: everything is generated, so a prefab can end up
    /// structurally valid and completely wrong -- a mesh filter with no mesh, a
    /// weapon pointing at the wrong sound, an enemy a head taller than its own
    /// hitbox. None of that throws. All of it is visible only if something
    /// measures it.
    /// </summary>
    public class GeneratedArtTests
    {
        private const string PrefabFolder = "Assets/Ashfall/Prefabs";
        private const string DataFolder = "Assets/Ashfall/Data";

        private static readonly (string prefab, string definition)[] Enemies =
        {
            ("Enemy_Shambler", "Enemy_Shambler"),
            ("Enemy_Sprinter", "Enemy_Sprinter"),
            ("Enemy_StormBrute", "Enemy_StormBrute")
        };

        private static readonly (string prefab, string definition, AudioCue fireCue)[] Weapons =
        {
            ("VM_MeridianSidearm", "Weapon_MeridianSidearm", AudioCue.WeaponFireSidearm),
            ("VM_Breakwater", "Weapon_Breakwater", AudioCue.WeaponFireShotgun),
            ("VM_Arc9", "Weapon_Arc9", AudioCue.WeaponFireRifle)
        };

        private static T Load<T>(string folder, string name) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>($"{folder}/{name}.{(typeof(T) == typeof(GameObject) ? "prefab" : "asset")}");
            Assert.IsNotNull(asset, $"{folder}/{name} is missing. Run Ashfall > Build Playable Scene.");
            return asset;
        }

        // ------------------------------------------------------------------
        // Geometry
        // ------------------------------------------------------------------

        [Test]
        public void EveryEnemyAndWeaponMeshFilterHasAMesh()
        {
            var names = new List<string>();
            foreach ((string prefab, _) in Enemies)
            {
                names.Add(prefab);
            }

            foreach ((string prefab, _, _) in Weapons)
            {
                names.Add(prefab);
            }

            foreach (string name in names)
            {
                var go = Load<GameObject>(PrefabFolder, name);
                MeshFilter[] filters = go.GetComponentsInChildren<MeshFilter>(true);
                Assert.Greater(filters.Length, 0, $"{name} has no mesh filters at all.");

                foreach (MeshFilter filter in filters)
                {
                    // A mesh created but never written into the shared asset
                    // serialises as null, and the prefab looks fine until it is
                    // reopened. This is the check for that.
                    Assert.IsNotNull(filter.sharedMesh,
                        $"{name}/{filter.name} has a null mesh -- it was probably not persisted into the mesh asset.");
                    Assert.Greater(filter.sharedMesh.vertexCount, 0,
                        $"{name}/{filter.name} has an empty mesh.");
                }
            }
        }

        [Test]
        public void EveryRendererHasAMaterial()
        {
            foreach ((string prefab, _) in Enemies)
            {
                var go = Load<GameObject>(PrefabFolder, prefab);
                foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.IsNotNull(renderer.sharedMaterial,
                        $"{prefab}/{renderer.name} would render magenta.");
                }
            }
        }

        [Test]
        public void EnemyBodiesFitTheHeightTheirDefinitionAdvertises()
        {
            foreach ((string prefabName, string definitionName) in Enemies)
            {
                var go = Load<GameObject>(PrefabFolder, prefabName);
                var definition = Load<EnemyDefinition>(DataFolder, definitionName);

                Bounds bounds = MeasureLocal(go);

                // The hitboxes, the nav radius and the camera framing are all
                // derived from bodyHeight; a mesh that disagrees leaves a head
                // sticking out of its own head hitbox.
                Assert.That(bounds.max.y, Is.InRange(definition.bodyHeight * 0.80f, definition.bodyHeight * 1.05f),
                    $"{prefabName} stands {bounds.max.y:0.00} m but its definition says {definition.bodyHeight:0.00} m.");

                Assert.Less(bounds.min.y, 0.12f,
                    $"{prefabName} floats: its lowest geometry is at {bounds.min.y:0.00} m.");

                float halfWidth = Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x));
                Assert.Less(halfWidth, definition.bodyRadius * 2.6f,
                    $"{prefabName} is {halfWidth:0.00} m wide against a body radius of {definition.bodyRadius:0.00} m.");
            }
        }

        [Test]
        public void EnemiesUseCurvedGeometryRatherThanPlainBoxes()
        {
            // The point of the art pass: a body made of boxes has 24 vertices
            // per part and six distinct normals. Lofted limbs and ellipsoid
            // heads have neither, and this is the cheapest way to assert that
            // the organic geometry is what actually shipped.
            foreach ((string prefabName, _) in Enemies)
            {
                var go = Load<GameObject>(PrefabFolder, prefabName);

                int curved = 0;
                int total = 0;
                foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                    {
                        continue;
                    }

                    total++;
                    if (DistinctNormalCount(filter.sharedMesh) > 12)
                    {
                        curved++;
                    }
                }

                Assert.Greater(total, 0, $"{prefabName} has no meshes.");
                Assert.GreaterOrEqual(curved, total / 2,
                    $"Only {curved} of {total} parts on {prefabName} are curved; the body is still mostly boxes.");
            }
        }

        [Test]
        public void EnemyTriangleCountsStayInsideTheFieldBudget()
        {
            // Twenty-four concurrent enemies is the director's hard cap.
            const int budgetPerEnemy = 12000;

            foreach ((string prefabName, _) in Enemies)
            {
                var go = Load<GameObject>(PrefabFolder, prefabName);
                int triangles = 0;

                foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh != null)
                    {
                        triangles += filter.sharedMesh.triangles.Length / 3;
                    }
                }

                Assert.Greater(triangles, 200, $"{prefabName} is suspiciously simple at {triangles} triangles.");
                Assert.Less(triangles, budgetPerEnemy,
                    $"{prefabName} is {triangles} triangles; 24 of them would not fit the frame budget.");
            }
        }

        [Test]
        public void EveryEnemyKeepsACriticalHitboxAndAnAttackOrigin()
        {
            foreach ((string prefabName, _) in Enemies)
            {
                var go = Load<GameObject>(PrefabFolder, prefabName);

                bool critical = false;
                foreach (DamageRelay relay in go.GetComponentsInChildren<DamageRelay>(true))
                {
                    critical |= relay.CountsAsCritical;
                }

                Assert.IsTrue(critical, $"{prefabName} has no critical hitbox: head shots would do body damage.");
                Assert.IsNotNull(go.transform.Find("AttackOrigin"), $"{prefabName} lost its attack origin.");
            }
        }

        [Test]
        public void WeaponViewModelsKeepTheirAnimatedRig()
        {
            foreach ((string prefabName, _, _) in Weapons)
            {
                var go = Load<GameObject>(PrefabFolder, prefabName);
                var view = go.GetComponent<WeaponViewModel>();

                Assert.IsNotNull(view, $"{prefabName} has no WeaponViewModel.");
                Assert.IsNotNull(view.Muzzle, $"{prefabName} has no muzzle transform.");
                Assert.AreNotSame(go.transform, view.Muzzle,
                    $"{prefabName} fell back to its root as a muzzle; the flash would sit inside the receiver.");
                Assert.Greater(view.Muzzle.localPosition.z, 0.1f,
                    $"{prefabName}'s muzzle is not out in front of the weapon.");
            }
        }

        // ------------------------------------------------------------------
        // Audio assignment
        // ------------------------------------------------------------------

        [Test]
        public void EachWeaponPointsAtItsOwnShotSound()
        {
            var seen = new HashSet<AudioCue>();

            foreach ((_, string definitionName, AudioCue expected) in Weapons)
            {
                var definition = Load<WeaponDefinition>(DataFolder, definitionName);
                Assert.AreEqual(expected, definition.fireCue,
                    $"{definition.displayName} is wired to the wrong shot sound.");
                Assert.IsTrue(seen.Add(definition.fireCue),
                    $"{definition.displayName} shares a shot sound with another weapon.");
            }
        }

        [Test]
        public void ShellReloadingWeaponsGetTheShellInsertSound()
        {
            foreach ((_, string definitionName, _) in Weapons)
            {
                var definition = Load<WeaponDefinition>(DataFolder, definitionName);
                AudioCue expected = definition.incrementalReload
                    ? AudioCue.WeaponReloadShell
                    : AudioCue.WeaponReloadMagazine;

                Assert.AreEqual(expected, definition.ReloadCue,
                    $"{definition.displayName} reloads {(definition.incrementalReload ? "a shell at a time" : "by magazine")} " +
                    "but plays the other sound.");
            }
        }

        [Test]
        public void EveryCueInTheMixHasAClipOnDisk()
        {
            List<AudioDirector.CueEntry> table = AshfallAudioLibrary.BuildCueTable();

            Assert.IsEmpty(AshfallAudioLibrary.MissingClips,
                "Missing audio: " + string.Join(", ", AshfallAudioLibrary.MissingClips) +
                ". Run /usr/bin/python3 Tools/Audio/generate_audio.py");

            foreach (AudioDirector.CueEntry entry in table)
            {
                Assert.IsNotNull(entry.clip, $"{entry.cue} has no clip.");
                Assert.Greater(entry.clip.length, 0.02f, $"{entry.cue} is effectively silent.");
            }

            Assert.IsNotNull(AshfallAudioLibrary.StormAmbience(), "There is no storm ambience bed.");
        }

        [Test]
        public void EveryCueExceptNoneIsInTheMix()
        {
            List<AudioDirector.CueEntry> table = AshfallAudioLibrary.BuildCueTable();
            var covered = new HashSet<AudioCue>();
            foreach (AudioDirector.CueEntry entry in table)
            {
                Assert.IsTrue(covered.Add(entry.cue), $"{entry.cue} appears twice in the mix table.");
            }

            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue)))
            {
                if (cue == AudioCue.None)
                {
                    continue;
                }

                Assert.IsTrue(covered.Contains(cue), $"{cue} exists but nothing in the mix table plays it.");
            }
        }

        [Test]
        public void ShotIntervalsAreShorterThanTheWeaponsCyclicRate()
        {
            // An interval longer than the gap between shots turns full-auto
            // into a stutter, which is the one way the anti-spam guard can make
            // the game worse rather than better.
            List<AudioDirector.CueEntry> table = AshfallAudioLibrary.BuildCueTable();

            foreach ((_, string definitionName, AudioCue fireCue) in Weapons)
            {
                var definition = Load<WeaponDefinition>(DataFolder, definitionName);
                AudioDirector.CueEntry entry = table.Find(e => e.cue == fireCue);

                Assert.IsNotNull(entry, $"{fireCue} is not in the mix table.");
                Assert.Less(entry.minInterval, definition.ShotInterval,
                    $"{definition.displayName} fires every {definition.ShotInterval:0.000}s but its sound is " +
                    $"guarded for {entry.minInterval:0.000}s, so shots would be silently dropped.");
            }
        }

        // ------------------------------------------------------------------
        // Meshcaster staging
        // ------------------------------------------------------------------

        [Test]
        public void MeshcasterSlotsCoverEveryWeaponAndEnemy()
        {
            Assert.AreEqual(6, AshfallMeshcasterImport.Slots.Length,
                "The art pass is three weapons and three enemies.");

            foreach (EnemyArchetype archetype in System.Enum.GetValues(typeof(EnemyArchetype)))
            {
                string key = AshfallMeshcasterImport.KeyForArchetype(archetype);
                Assert.IsTrue(AshfallMeshcasterImport.TryGetSlot(key, out _),
                    $"{archetype} maps to '{key}', which is not a staging slot.");
            }
        }

        [Test]
        public void MeshcasterEnemyTargetsMatchTheEnemyDefinitions()
        {
            // The slot table's target height is what the importer scales an
            // approved model to, and it is also the number the document tells a
            // human to type into Meshcaster. If it drifts from the definition,
            // an imported enemy silently stops matching its hitboxes.
            foreach ((_, string definitionName) in Enemies)
            {
                var definition = Load<EnemyDefinition>(DataFolder, definitionName);
                string key = AshfallMeshcasterImport.KeyForArchetype(definition.archetype);

                Assert.IsTrue(AshfallMeshcasterImport.TryGetSlot(key, out AshfallMeshcasterImport.Slot slot));
                Assert.AreEqual(definition.bodyHeight, slot.TargetSize, 0.001f,
                    $"{key} targets {slot.TargetSize:0.00} m but {definition.displayName} is {definition.bodyHeight:0.00} m.");
                Assert.AreEqual(AshfallMeshcasterImport.Axis.Y, slot.SizeAxis,
                    $"{key} should be measured by height.");
            }
        }

        [Test]
        public void AbsentMeshcasterArtLeavesTheProceduralBodyVisible()
        {
            // The fallback is the shipping state until someone runs the paid
            // jobs, so it has to be the thing that is actually tested.
            foreach ((string prefabName, string definitionName) in Enemies)
            {
                var go = Load<GameObject>(PrefabFolder, prefabName);
                Transform visual = go.transform.Find("Visual");
                Assert.IsNotNull(visual, $"{prefabName} has no Visual root.");

                Transform procedural = visual.Find("Procedural");
                Assert.IsNotNull(procedural, $"{prefabName} lost its procedural body root.");

                string key = AshfallMeshcasterImport.KeyForArchetype(
                    Load<EnemyDefinition>(DataFolder, definitionName).archetype);
                bool staged = AshfallMeshcasterImport.HasApprovedModel(key);

                Assert.AreEqual(!staged, procedural.gameObject.activeSelf,
                    staged
                        ? $"{prefabName} has staged Meshcaster art but still shows the procedural body."
                        : $"{prefabName} has no staged art, so the procedural body must stay visible.");

                if (!staged)
                {
                    Assert.IsNull(visual.Find("MeshcasterBody"),
                        $"{prefabName} has a Meshcaster body with nothing staged.");
                }
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Bounds MeasureLocal(GameObject prefab)
        {
            var bounds = new Bounds();
            bool started = false;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                // activeInHierarchy is always false on a prefab asset, so the
                // disabled-branch check has to walk the chain by hand.
                if (filter.sharedMesh == null || !IsActiveUnder(filter.transform, prefab.transform))
                {
                    continue;
                }

                Bounds local = filter.sharedMesh.bounds;
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? local.min.x : local.max.x,
                        (c & 2) == 0 ? local.min.y : local.max.y,
                        (c & 4) == 0 ? local.min.z : local.max.z);

                    Vector3 point = toRoot.MultiplyPoint3x4(corner);
                    if (!started)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        started = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return bounds;
        }

        private static bool IsActiveUnder(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root.parent; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    return false;
                }
            }

            return true;
        }

        private static int DistinctNormalCount(Mesh mesh)
        {
            Vector3[] normals = mesh.normals;
            var distinct = new HashSet<Vector3Int>();

            for (int i = 0; i < normals.Length && distinct.Count <= 32; i++)
            {
                // Quantised so floating-point noise does not count as variety.
                distinct.Add(new Vector3Int(
                    Mathf.RoundToInt(normals[i].x * 12f),
                    Mathf.RoundToInt(normals[i].y * 12f),
                    Mathf.RoundToInt(normals[i].z * 12f)));
            }

            return distinct.Count;
        }
    }
}
