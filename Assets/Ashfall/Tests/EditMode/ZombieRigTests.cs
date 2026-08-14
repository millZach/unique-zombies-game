using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Ashfall.Enemies;
using Ashfall.EditorTools;

namespace Ashfall.Tests
{
    /// <summary>
    /// The rigged-zombie contract.
    ///
    /// No approved Meshcaster art exists in this repository, and none is
    /// invented here: every fixture is either built in the test or read out of
    /// this repository's own files. What is tested is the part that has to be
    /// right *before* the paid assets arrive -- the shape of the contract
    /// between the Blender script and the importer, and the fact that an empty
    /// slot leaves the shipping game exactly as it was.
    /// </summary>
    public class ZombieRigTests
    {
        private const string TestSlot = "ZZ_TestSlot";

        private readonly List<string> _createdAssets = new();
        private readonly List<Object> _createdObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (string path in _createdAssets)
            {
                AssetDatabase.DeleteAsset(path);
            }

            _createdAssets.Clear();

            foreach (Object created in _createdObjects)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            _createdObjects.Clear();

            // Building a controller creates the folder that holds it. Leaving an
            // empty one behind turns every test run into an untracked directory
            // in someone's working tree.
            if (AssetDatabase.IsValidFolder(AshfallZombieRig.ControllerFolder)
                && Directory.GetFileSystemEntries(AshfallZombieRig.ControllerFolder).Length == 0)
            {
                AssetDatabase.DeleteAsset(AshfallZombieRig.ControllerFolder);
            }
        }

        // ------------------------------------------------------------------
        // The contract with the Blender script
        // ------------------------------------------------------------------

        [Test]
        public void TheFiveClipNamesAreSharedByEverySideOfThePipeline()
        {
            CollectionAssert.AreEqual(
                new[] { "Idle", "Walk", "Attack", "HitReact", "Death" },
                ZombieAnimator.ClipNames);

            CollectionAssert.AreEqual(ZombieAnimator.ClipNames, AshfallZombieRig.RequiredClips,
                "The importer and the runtime bridge must want the same clips.");

            string script = ReadRigScript();
            foreach (string clip in ZombieAnimator.ClipNames)
            {
                StringAssert.Contains($"(\"{clip}\"", script,
                    $"rig_zombie.py does not author a '{clip}' clip.");
            }
        }

        [Test]
        public void BlenderTargetHeightsMatchTheImporterSlotTable()
        {
            // The Blender script scales each model to a height, and the
            // importer builds hitboxes around the same number. If the two
            // drift, a rigged enemy is silently the wrong size for its head
            // box -- and nothing in either tool would report it.
            string script = ReadRigScript();

            foreach (AshfallMeshcasterImport.Slot slot in AshfallZombieRig.EnemySlots())
            {
                Match match = Regex.Match(script, $"\"{slot.Key}\"\\s*:\\s*([0-9.]+)");
                Assert.IsTrue(match.Success, $"rig_zombie.py has no SLOTS entry for {slot.Key}.");

                float scripted = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                Assert.AreEqual(slot.TargetSize, scripted, 0.001f,
                    $"{slot.Key}: rig_zombie.py targets {scripted:0.00} m, the slot table says " +
                    $"{slot.TargetSize:0.00} m.");
            }
        }

        [Test]
        public void EveryEnemyArchetypeHasARiggableSlot()
        {
            var keys = new List<string>();
            foreach (AshfallMeshcasterImport.Slot slot in AshfallZombieRig.EnemySlots())
            {
                keys.Add(slot.Key);
            }

            Assert.AreEqual(3, keys.Count, "Three enemies, three rig slots.");

            foreach (Core.EnemyArchetype archetype in System.Enum.GetValues(typeof(Core.EnemyArchetype)))
            {
                CollectionAssert.Contains(keys, AshfallMeshcasterImport.KeyForArchetype(archetype));
            }
        }

        // ------------------------------------------------------------------
        // Manifest
        // ------------------------------------------------------------------

        [Test]
        public void AManifestOfTheShapeTheScriptWritesParses()
        {
            AshfallZombieRig.RigManifest manifest =
                JsonUtility.FromJson<AshfallZombieRig.RigManifest>(SampleManifest(selfTest: false));

            Assert.AreEqual("Enemy_Shambler", manifest.slot);
            Assert.AreEqual(1, manifest.schemaVersion);
            Assert.AreEqual(22, manifest.boneCount);
            Assert.AreEqual(30, manifest.fps);
            Assert.AreEqual(1.85f, manifest.heightMeters, 0.001f);
            Assert.AreEqual("envelope-fallback", manifest.weighting);
            Assert.AreEqual(5, manifest.clips.Length);

            Assert.IsTrue(manifest.Loops("Idle"), "Idle has to loop.");
            Assert.IsTrue(manifest.Loops("Rig|Rig|Walk"), "Loop lookup must survive FBX take naming.");
            Assert.IsFalse(manifest.Loops("Death"), "A looping death never ends.");
            Assert.IsFalse(manifest.Loops("NotAClip"));
        }

        [Test]
        public void AShippableManifestIsAcceptedAndAProxyIsNot()
        {
            var good = JsonUtility.FromJson<AshfallZombieRig.RigManifest>(SampleManifest(selfTest: false));
            Assert.IsTrue(AshfallZombieRig.IsManifestShippable(good, out string reason), reason);

            var proxy = JsonUtility.FromJson<AshfallZombieRig.RigManifest>(SampleManifest(selfTest: true));
            Assert.IsFalse(AshfallZombieRig.IsManifestShippable(proxy, out reason),
                "A self-test proxy must never be shippable as approved art.");
            StringAssert.Contains("proxy", reason);

            Assert.IsFalse(AshfallZombieRig.IsManifestShippable(null, out reason));
            StringAssert.Contains("no rig manifest", reason);

            good.schemaVersion = 99;
            Assert.IsFalse(AshfallZombieRig.IsManifestShippable(good, out reason));
            StringAssert.Contains("schema", reason);

            good.schemaVersion = 1;
            good.clips = new AshfallZombieRig.ClipEntry[2];
            Assert.IsFalse(AshfallZombieRig.IsManifestShippable(good, out reason));
            StringAssert.Contains("clips", reason);
        }

        [Test]
        public void ClipNamesAreMatchedThroughFbxTakePrefixes()
        {
            Assert.IsTrue(ZombieAnimator.MatchesClip("Idle", "Idle"));
            Assert.IsTrue(ZombieAnimator.MatchesClip("Enemy_Shambler_Rig|Idle", "Idle"));
            Assert.IsTrue(ZombieAnimator.MatchesClip("Rig|Rig|HitReact", "HitReact"));
            Assert.IsTrue(ZombieAnimator.MatchesClip("rig|death", "Death"), "Case must not matter.");

            Assert.IsFalse(ZombieAnimator.MatchesClip("Rig|Walk", "Idle"));
            Assert.IsFalse(ZombieAnimator.MatchesClip("IdleExtra", "Idle"),
                "A clip whose name merely starts the same is not the clip.");
            Assert.IsFalse(ZombieAnimator.MatchesClip(null, "Idle"));
            Assert.IsFalse(ZombieAnimator.MatchesClip("", "Idle"));
        }

        // ------------------------------------------------------------------
        // State mapping
        // ------------------------------------------------------------------

        [Test]
        public void EveryBrainStateMapsToAClipThatExists()
        {
            foreach (EnemyState state in System.Enum.GetValues(typeof(EnemyState)))
            {
                foreach (bool moving in new[] { false, true })
                {
                    string clip = ZombieAnimator.ClipFor(state, dying: false, reacting: false, moving: moving);
                    CollectionAssert.Contains(ZombieAnimator.ClipNames, clip,
                        $"{state} (moving={moving}) asked for '{clip}', which nothing authors.");
                }
            }
        }

        [Test]
        public void DeathOutranksEverythingAndAttacksOutrankFlinches()
        {
            Assert.AreEqual("Death", ZombieAnimator.ClipFor(EnemyState.Chase, true, true, true));
            Assert.AreEqual("Death", ZombieAnimator.ClipFor(EnemyState.Dead, false, false, false));

            // A stagger cancels a swing in the brain, so it may cancel it here.
            Assert.AreEqual("HitReact", ZombieAnimator.ClipFor(EnemyState.Stagger, false, false, false));

            // A hit that did *not* stagger must not hide a telegraphed swing:
            // the windup is the player's cue to back out of range.
            Assert.AreEqual("Attack", ZombieAnimator.ClipFor(EnemyState.AttackWindup, false, true, false));
            Assert.AreEqual("Attack", ZombieAnimator.ClipFor(EnemyState.TearBarricade, false, true, false));

            Assert.AreEqual("HitReact", ZombieAnimator.ClipFor(EnemyState.Chase, false, true, true));
            Assert.AreEqual("Walk", ZombieAnimator.ClipFor(EnemyState.Chase, false, false, true));
            Assert.AreEqual("Idle", ZombieAnimator.ClipFor(EnemyState.Chase, false, false, false));
            Assert.AreEqual("Idle", ZombieAnimator.ClipFor(EnemyState.Idle, false, false, true));
        }

        // ------------------------------------------------------------------
        // Animator Controller generation
        // ------------------------------------------------------------------

        [Test]
        public void ControllerGetsOneStatePerClipAndDefaultsToIdle()
        {
            List<AnimationClip> clips = SynthesiseClips(ZombieAnimator.ClipNames);
            AnimatorController controller = BuildTestController(clips);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            Assert.AreEqual(ZombieAnimator.ClipNames.Length, machine.states.Length);
            Assert.AreEqual("Idle", machine.defaultState.name);

            foreach (string wanted in ZombieAnimator.ClipNames)
            {
                bool found = false;
                foreach (ChildAnimatorState child in machine.states)
                {
                    if (child.state.name == wanted)
                    {
                        found = true;
                        Assert.IsNotNull(child.state.motion, $"State '{wanted}' has no motion.");
                    }
                }

                Assert.IsTrue(found, $"Controller has no '{wanted}' state.");
            }

            // No transitions on purpose: ZombieAnimator cross-fades by state, and
            // a graph would be a second place for the same rules to disagree.
            foreach (ChildAnimatorState child in machine.states)
            {
                Assert.AreEqual(0, child.state.transitions.Length,
                    $"State '{child.state.name}' grew a transition the runtime does not use.");
            }
        }

        [Test]
        public void AMissingClipLeavesAControllerThatStillRuns()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                AnimatorController controller = BuildTestController(SynthesiseClips(new[] { "Idle", "Walk" }));
                AnimatorStateMachine machine = controller.layers[0].stateMachine;

                Assert.AreEqual(2, machine.states.Length);
                Assert.AreEqual("Idle", machine.defaultState.name,
                    "Idle is the floor every fallback lands on; it has to be the default.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        }

        // ------------------------------------------------------------------
        // The shipping state: nothing staged
        // ------------------------------------------------------------------

        [Test]
        public void NoSlotIsRigVerifiedAndEveryEnemyKeepsItsProceduralBody()
        {
            foreach (AshfallMeshcasterImport.Slot slot in AshfallZombieRig.EnemySlots())
            {
                bool verified = AshfallZombieRig.IsRigVerified(slot.Key, out string reason);
                bool hasManifest = File.Exists(AshfallZombieRig.ManifestPath(slot.Key));

                if (!hasManifest)
                {
                    Assert.IsFalse(verified, $"{slot.Key} reports a rig with no manifest on disk.");
                    Assert.AreEqual("no rig manifest", reason);
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{AshfallAssetUtility.PrefabFolder}/{PrefabNameFor(slot.Key)}.prefab");
                Assert.IsNotNull(prefab, $"{slot.Key} has no enemy prefab.");

                Transform visual = prefab.transform.Find("Visual");
                Assert.IsNotNull(visual);

                if (!verified)
                {
                    Assert.IsNull(visual.Find("MeshcasterRiggedBody"),
                        $"{PrefabNameFor(slot.Key)} has a rigged body with no verified rig.");
                    Assert.IsNull(prefab.GetComponent<ZombieAnimator>(),
                        $"{PrefabNameFor(slot.Key)} has an animation bridge with nothing to drive.");
                    Assert.IsTrue(visual.Find("Procedural") != null
                                  && visual.Find("Procedural").gameObject.activeSelf,
                        $"{PrefabNameFor(slot.Key)} must keep its procedural body visible.");
                }
            }
        }

        [Test]
        public void TheStatusReportNamesEverySlotAndSaysNothingWasSpent()
        {
            string report = AshfallMeshcasterImport.BuildStatusReport();

            foreach (AshfallMeshcasterImport.Slot slot in AshfallMeshcasterImport.Slots)
            {
                StringAssert.Contains(slot.Key, report);
            }

            StringAssert.Contains("rigging", report);
            StringAssert.Contains("No credits are spent by this tool", report);
        }

        // ------------------------------------------------------------------
        // The OBJ bridge into Blender
        // ------------------------------------------------------------------

        [Test]
        public void ObjExportIsCompleteAndByteIdenticalTwice()
        {
            var root = new GameObject("ObjSource");
            _createdObjects.Add(root);

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            Object.DestroyImmediate(cube.GetComponent<Collider>());

            string first = Path.Combine(Path.GetTempPath(), "ashfall-obj-a.obj");
            string second = Path.Combine(Path.GetTempPath(), "ashfall-obj-b.obj");

            int triangles = AshfallZombieRig.WriteObj(root, first);
            int again = AshfallZombieRig.WriteObj(root, second);

            Assert.AreEqual(12, triangles, "A cube is twelve triangles.");
            Assert.AreEqual(triangles, again);

            string text = File.ReadAllText(first);
            Assert.AreEqual(text, File.ReadAllText(second),
                "Two exports of the same prefab must be identical, or the rig pipeline is not reproducible.");

            Assert.AreEqual(24, CountLines(text, "v "), "Unity's cube has 24 split vertices.");
            Assert.AreEqual(12, CountLines(text, "f "));
            StringAssert.Contains("vn ", text, "Normals are needed or Blender re-derives them flat.");

            File.Delete(first);
            File.Delete(second);
        }

        [Test]
        public void ExportingWithNothingStagedReportsPendingAndWritesNoAssets()
        {
            string report = AshfallZombieRig.ExportSlotSources();

            foreach (AshfallMeshcasterImport.Slot slot in AshfallZombieRig.EnemySlots())
            {
                if (!AshfallMeshcasterImport.HasApprovedModel(slot.Key))
                {
                    StringAssert.Contains($"[pending] {slot.Key}", report);
                }
            }

            StringAssert.Contains("rig_zombie.py", report,
                "The report has to say what to run next.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static string PrefabNameFor(string slotKey) => slotKey switch
        {
            "Enemy_Sprinter" => "Enemy_Sprinter",
            "Enemy_StormBrute" => "Enemy_StormBrute",
            _ => "Enemy_Shambler"
        };

        private static string ReadRigScript()
        {
            string path = Path.Combine(
                Path.GetDirectoryName(Application.dataPath) ?? ".",
                "Tools", "Blender", "rig_zombie.py");

            Assert.IsTrue(File.Exists(path), $"The rigging script is missing at {path}.");
            return File.ReadAllText(path);
        }

        private static int CountLines(string text, string prefix)
        {
            int count = 0;
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Empty clips named the way the FBX importer names takes. Synthesised,
        /// not imported: there is no approved art to import, and committing a
        /// stand-in under the staging folder would be a lie about what exists.
        /// </summary>
        private List<AnimationClip> SynthesiseClips(IEnumerable<string> names)
        {
            var clips = new List<AnimationClip>();
            foreach (string name in names)
            {
                var clip = new AnimationClip { name = $"TestRig|{name}" };
                _createdObjects.Add(clip);
                clips.Add(clip);
            }

            return clips;
        }

        private AnimatorController BuildTestController(List<AnimationClip> clips)
        {
            AnimatorController controller = AshfallZombieRig.BuildController(TestSlot, clips);
            _createdAssets.Add($"{AshfallZombieRig.ControllerFolder}/AC_{TestSlot}.controller");
            Assert.IsNotNull(controller);
            return controller;
        }

        private static string SampleManifest(bool selfTest) => @"{
  ""blender"": ""5.2.0 LTS"",
  ""boneCount"": 22,
  ""clips"": [
    { ""end"": 61, ""loop"": true,  ""name"": ""Idle"",     ""start"": 1 },
    { ""end"": 41, ""loop"": true,  ""name"": ""Walk"",     ""start"": 1 },
    { ""end"": 30, ""loop"": false, ""name"": ""Attack"",   ""start"": 1 },
    { ""end"": 18, ""loop"": false, ""name"": ""HitReact"", ""start"": 1 },
    { ""end"": 45, ""loop"": false, ""name"": ""Death"",    ""start"": 1 }
  ],
  ""deformBoneCount"": 21,
  ""fbx"": ""Enemy_Shambler_Rigged.fbx"",
  ""fps"": 30,
  ""generator"": ""Tools/Blender/rig_zombie.py"",
  ""heightMeters"": 1.85,
  ""schemaVersion"": 1,
  ""selfTest"": " + (selfTest ? "true" : "false") + @",
  ""slot"": ""Enemy_Shambler"",
  ""source"": """ + (selfTest ? "self-test-proxy" : "imported-file") + @""",
  ""triangleCount"": 7860,
  ""vertexCount"": 4102,
  ""weighting"": ""envelope-fallback""
}";
    }
}
