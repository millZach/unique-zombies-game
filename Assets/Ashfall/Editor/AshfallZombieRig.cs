using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.Nav;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Adopts a *rigged* Meshcaster zombie: import settings, Animator
    /// Controller, and the runtime bridge that drives it.
    ///
    /// **This code never spends credits and never talks to a provider.** Like
    /// <see cref="AshfallMeshcasterImport"/> it has no network calls, no keys
    /// and no endpoints. It reads an FBX that <c>Tools/Blender/rig_zombie.py</c>
    /// produced from a mesh a human already generated, approved and copied in.
    ///
    /// The contract with the Blender script is one JSON file. A slot counts as
    /// rig-verified only when the manifest is present and parses, the FBX
    /// imports with a skinned renderer and an avatar, and every one of the five
    /// clips is actually in the file. Anything less falls back -- first to the
    /// static Meshcaster mesh, then to the procedural body -- rather than
    /// shipping a T-pose.
    ///
    /// See <c>Docs/MeshcasterArtPass.md</c> for the whole workflow.
    /// </summary>
    public static class AshfallZombieRig
    {
        /// <summary>Sub-folder of a staging slot that holds the rigged output.</summary>
        public const string RiggedSubfolder = "Rigged";

        public const string ControllerFolder = "Assets/Ashfall/Art/Generated/Animation";

        /// <summary>Where <c>rig_zombie.py</c> reads a slot's source mesh from.</summary>
        public const string BlenderInputFolder = "Tools/Blender/Input";

        /// <summary>Marks an importer this tool has already given its material defaults.</summary>
        private const string ImporterStamp = "AshfallZombieRig/v1";

        /// <summary>The clips the rig pipeline authors, in state-machine order.</summary>
        public static readonly string[] RequiredClips = ZombieAnimator.ClipNames;

        /// <summary>The three enemy slots. Weapons are static meshes and are not rigged here.</summary>
        public static IEnumerable<AshfallMeshcasterImport.Slot> EnemySlots()
        {
            foreach (AshfallMeshcasterImport.Slot slot in AshfallMeshcasterImport.Slots)
            {
                if (slot.Key.StartsWith("Enemy_", StringComparison.Ordinal))
                {
                    yield return slot;
                }
            }
        }

        // ------------------------------------------------------------------
        // Manifest
        // ------------------------------------------------------------------

        [Serializable]
        public class ClipEntry
        {
            public string name;
            public int start;
            public int end;
            public bool loop;
        }

        /// <summary>
        /// What <c>rig_zombie.py</c> wrote next to the FBX.
        ///
        /// Field names match the JSON exactly because <see cref="JsonUtility"/>
        /// maps by name; unknown fields in the file are ignored, so the Blender
        /// side can add to the manifest without breaking this.
        /// </summary>
        [Serializable]
        public class RigManifest
        {
            public int schemaVersion;
            public string slot;
            public string source;
            public bool selfTest;
            public string sourceFile;
            public string fbx;
            public float heightMeters;
            public int fps;
            public int vertexCount;
            public int triangleCount;
            public int boneCount;
            public int deformBoneCount;
            public string weighting;
            public string blender;
            public ClipEntry[] clips;

            public bool Loops(string clipName)
            {
                if (clips == null)
                {
                    return false;
                }

                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null && ZombieAnimator.MatchesClip(clipName, clips[i].name))
                    {
                        return clips[i].loop;
                    }
                }

                return false;
            }
        }

        public static string RiggedFolder(string slotKey) =>
            $"{AshfallMeshcasterImport.StagingFolder}/{slotKey}/{RiggedSubfolder}";

        public static string ManifestPath(string slotKey) =>
            $"{RiggedFolder(slotKey)}/{slotKey}_Rigged.rigmanifest.json";

        /// <summary>Reads the manifest for a slot. Returns null when absent or malformed.</summary>
        public static RigManifest LoadManifest(string slotKey)
        {
            string path = ManifestPath(slotKey);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var manifest = JsonUtility.FromJson<RigManifest>(File.ReadAllText(path));
                return manifest != null && !string.IsNullOrEmpty(manifest.slot) ? manifest : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Ashfall] Rig manifest at {path} could not be read: {exception.Message}");
                return null;
            }
        }

        /// <summary>The rigged model asset for a slot, or null.</summary>
        public static GameObject FindRiggedModel(string slotKey)
        {
            string folder = RiggedFolder(slotKey);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return null;
            }

            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
            if (guids.Length == 0)
            {
                return null;
            }

            // Deterministic across machines: sort by path, not by search order.
            Array.Sort(guids, (a, b) => string.CompareOrdinal(
                AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

            return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // ------------------------------------------------------------------
        // Verification
        // ------------------------------------------------------------------

        /// <summary>
        /// Whether a slot's rigged art is fit to ship, and why not when it is not.
        ///
        /// Deliberately strict. Every one of these checks corresponds to a way a
        /// rigged import can look fine in the Project view and be broken in the
        /// game: no skin, no avatar, a clip that did not survive the FBX, or a
        /// self-test proxy someone copied into a real slot.
        /// </summary>
        public static bool IsRigVerified(string slotKey, out string reason)
        {
            RigManifest manifest = LoadManifest(slotKey);
            if (!IsManifestShippable(manifest, out reason))
            {
                return false;
            }

            GameObject model = FindRiggedModel(slotKey);
            if (model == null)
            {
                reason = "manifest present but no model imported next to it";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(model);

            if (model.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
            {
                reason = "imported model has no SkinnedMeshRenderer";
                return false;
            }

            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isValid)
            {
                reason = "imported model has no valid avatar";
                return false;
            }

            List<AnimationClip> clips = LoadClips(path);
            foreach (string wanted in RequiredClips)
            {
                if (FindClip(clips, wanted) == null)
                {
                    reason = $"clip '{wanted}' is missing from {Path.GetFileName(path)}";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        public static bool IsRigVerified(string slotKey) => IsRigVerified(slotKey, out _);

        /// <summary>
        /// The half of verification that needs only the manifest.
        ///
        /// Split out because it is the half worth testing without an FBX on
        /// disk -- and because the self-test rejection is the rule that keeps a
        /// proxy from ever reaching a player.
        /// </summary>
        public static bool IsManifestShippable(RigManifest manifest, out string reason)
        {
            if (manifest == null)
            {
                reason = "no rig manifest";
                return false;
            }

            if (manifest.selfTest || manifest.source == "self-test-proxy")
            {
                reason = "manifest is a self-test proxy, not approved art";
                return false;
            }

            if (manifest.schemaVersion != 1)
            {
                reason = $"manifest schema {manifest.schemaVersion} is not supported";
                return false;
            }

            if (manifest.clips == null || manifest.clips.Length != RequiredClips.Length)
            {
                reason = $"manifest lists {manifest.clips?.Length ?? 0} clips, expected {RequiredClips.Length}";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>Every AnimationClip sub-asset of an imported model.</summary>
        public static List<AnimationClip> LoadClips(string assetPath)
        {
            var clips = new List<AnimationClip>();
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                // The importer's own preview clip is hidden and is not a take.
                if (asset is AnimationClip clip && (clip.hideFlags & HideFlags.HideInHierarchy) == 0)
                {
                    clips.Add(clip);
                }
            }

            clips.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return clips;
        }

        public static AnimationClip FindClip(List<AnimationClip> clips, string wanted)
        {
            for (int i = 0; i < clips.Count; i++)
            {
                if (ZombieAnimator.MatchesClip(clips[i].name, wanted))
                {
                    return clips[i];
                }
            }

            return null;
        }

        // ------------------------------------------------------------------
        // Import settings
        // ------------------------------------------------------------------

        /// <summary>
        /// Configures a rigged FBX as a Generic rig with looping set per the
        /// manifest. Returns true when something changed and the asset was
        /// reimported.
        ///
        /// Generic, not Humanoid: Humanoid needs a bone mapping Unity infers
        /// from names and proportions, and a generated zombie with digitigrade
        /// legs or three-metre shoulders is exactly the case where that
        /// inference silently produces a bad avatar. Generic plays the clips
        /// this rig was built with, on the rig they were built for. Retargeting
        /// between the three enemies is not needed -- each has its own clips
        /// from its own proportions.
        /// </summary>
        public static bool ConfigureImporter(string assetPath, RigManifest manifest)
        {
            if (AssetImporter.GetAtPath(assetPath) is not ModelImporter importer)
            {
                return false;
            }

            bool firstTime = importer.userData != ImporterStamp;

            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.resampleCurves = true;
            importer.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            importer.skinWeights = ModelImporterSkinWeights.Standard;
            importer.optimizeGameObjects = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importBlendShapes = false;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;

            // Materials belong to whoever approved them. Meshcaster may already
            // have built a URP material a human looked at and kept, and an
            // extraction or a remap done by hand must survive a re-run of this
            // tool -- so the material settings are touched exactly once.
            if (firstTime)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.userData = ImporterStamp;
            }

            ApplyClipSettings(importer, manifest);

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            return true;
        }

        private static void ApplyClipSettings(ModelImporter importer, RigManifest manifest)
        {
            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            if (defaults == null || defaults.Length == 0)
            {
                return;
            }

            var clips = new ModelImporterClipAnimation[defaults.Length];
            for (int i = 0; i < defaults.Length; i++)
            {
                ModelImporterClipAnimation clip = defaults[i];
                bool loop = manifest != null && manifest.Loops(clip.name);

                clip.loopTime = loop;
                clip.loopPose = loop;

                // Root motion is baked out on purpose: the CharacterController
                // owns position, and a clip that also moved the body would
                // double every step. See ZombieAnimator.
                clip.lockRootRotation = true;
                clip.lockRootHeightY = true;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalOrientation = true;
                clip.keepOriginalPositionY = true;
                clip.keepOriginalPositionXZ = true;

                clips[i] = clip;
            }

            importer.clipAnimations = clips;
        }

        // ------------------------------------------------------------------
        // Animator Controller
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds a five-state controller with no transitions.
        ///
        /// <see cref="ZombieAnimator"/> cross-fades to states by name, so the
        /// controller needs states and nothing else. A transition graph would
        /// add a second place for the same rules to live and a second place for
        /// them to disagree with the brain.
        /// </summary>
        public static AnimatorController BuildController(string slotKey, List<AnimationClip> clips)
        {
            AshfallAssetUtility.EnsureFolder(ControllerFolder);
            string path = $"{ControllerFolder}/AC_{slotKey}.controller";

            AssetDatabase.DeleteAsset(path);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            machine.anyStatePosition = new Vector3(-220f, 0f, 0f);
            machine.entryPosition = new Vector3(-220f, -80f, 0f);
            machine.exitPosition = new Vector3(-220f, 80f, 0f);

            for (int i = 0; i < RequiredClips.Length; i++)
            {
                string name = RequiredClips[i];
                AnimationClip clip = FindClip(clips, name);
                if (clip == null)
                {
                    Debug.LogWarning($"[Ashfall] {slotKey}: no '{name}' clip; " +
                                     "ZombieAnimator will fall back to Idle for it.");
                    continue;
                }

                AnimatorState state = machine.AddState(name, new Vector3(60f, i * 70f - 140f, 0f));
                state.motion = clip;
                state.writeDefaultValues = false;

                if (name == "Idle")
                {
                    machine.defaultState = state;
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        // ------------------------------------------------------------------
        // Attachment
        // ------------------------------------------------------------------

        /// <summary>
        /// Instantiates the rig-verified model under <paramref name="parent"/>,
        /// fitted to the slot's target size, with its controller assigned.
        ///
        /// Returns null when the slot is not rig-verified, which is the signal
        /// to the caller to try the static Meshcaster mesh and then the
        /// procedural body.
        /// </summary>
        public static GameObject TryAttachRigged(
            Transform parent, string slotKey, List<Renderer> renderers, out Animator animator)
        {
            animator = null;

            if (parent == null || !AshfallMeshcasterImport.TryGetSlot(slotKey, out AshfallMeshcasterImport.Slot slot))
            {
                return null;
            }

            if (!IsRigVerified(slotKey, out string reason))
            {
                if (LoadManifest(slotKey) != null)
                {
                    Debug.LogWarning($"[Ashfall] {slotKey} has rigged art that is not usable: {reason}. " +
                                     "Falling back.");
                }

                return null;
            }

            GameObject source = FindRiggedModel(slotKey);
            string assetPath = AssetDatabase.GetAssetPath(source);
            List<AnimationClip> clips = LoadClips(assetPath);
            AnimatorController controller = BuildController(slotKey, clips);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            if (instance == null)
            {
                return null;
            }

            instance.name = "MeshcasterRiggedBody";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(slot.Euler);
            instance.transform.localScale = Vector3.one;

            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            AshfallMeshcasterImport.FitToSlot(instance, slot);

            animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            var found = new List<Renderer>();
            instance.GetComponentsInChildren(true, found);
            renderers?.AddRange(found);

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            Debug.Log($"[Ashfall] Rigged Meshcaster art adopted for {slot.DisplayName}: {assetPath} " +
                      $"({found.Count} renderer(s), {clips.Count} clip(s)).");
            return instance;
        }

        /// <summary>Adds and wires the runtime bridge on an enemy root.</summary>
        public static ZombieAnimator AttachBridge(GameObject enemyRoot, Animator animator)
        {
            if (enemyRoot == null || animator == null)
            {
                return null;
            }

            var bridge = enemyRoot.GetComponent<ZombieAnimator>() ?? enemyRoot.AddComponent<ZombieAnimator>();
            bridge.Configure(
                animator,
                enemyRoot.GetComponent<EnemyBrain>(),
                enemyRoot.GetComponent<EnemyHealth>(),
                enemyRoot.GetComponent<SteeringAgent>());

            return bridge;
        }

        // ------------------------------------------------------------------
        // Slot source export (the input side of the Blender pipeline)
        // ------------------------------------------------------------------

        [MenuItem("Ashfall/Meshcaster: Export Slot Source for Blender", priority = 21)]
        public static void ExportSlotSourcesMenu()
        {
            Debug.Log(ExportSlotSources());
        }

        public static void ExportSlotSourcesFromCommandLine()
        {
            Debug.Log(ExportSlotSources());
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Writes each staged enemy slot's static mesh to
        /// <c>Tools/Blender/Input/&lt;slot&gt;/&lt;slot&gt;.obj</c>.
        ///
        /// Meshcaster delivers a Unity prefab with Unity mesh assets, which
        /// Blender cannot open. Rather than ask a human to dig the original GLB
        /// out of the plugin's working folder, the geometry already sitting in
        /// the Project is written back out in the one interchange format that
        /// needs no importer and no package. Read-only with respect to the
        /// staged art: nothing under Assets is modified.
        /// </summary>
        public static string ExportSlotSources()
        {
            var report = new StringBuilder();
            report.AppendLine("[Ashfall] MESHCASTER_SLOT_SOURCE_EXPORT");

            int written = 0;
            foreach (AshfallMeshcasterImport.Slot slot in EnemySlots())
            {
                GameObject model = AshfallMeshcasterImport.FindApprovedModel(slot.Key);
                if (model == null)
                {
                    report.AppendLine($"  [pending] {slot.Key,-18} nothing staged");
                    continue;
                }

                string directory = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath) ?? ".",
                    BlenderInputFolder, slot.Key);
                Directory.CreateDirectory(directory);

                string objPath = Path.Combine(directory, $"{slot.Key}.obj");
                int triangles = WriteObj(model, objPath);

                written++;
                report.AppendLine($"  [written] {slot.Key,-18} {triangles} triangles -> " +
                                  $"{BlenderInputFolder}/{slot.Key}/{slot.Key}.obj");
            }

            report.AppendLine($"  {written}/3 enemy slots exported.");
            report.Append("  Next: blender --background --python Tools/Blender/rig_zombie.py -- --all");
            return report.ToString();
        }

        /// <summary>Writes a prefab's baked geometry as a Wavefront OBJ. Returns the triangle count.</summary>
        public static int WriteObj(GameObject prefab, string path)
        {
            var culture = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine($"# {prefab.name} exported by AshfallZombieRig for Tools/Blender/rig_zombie.py");
            sb.AppendLine("# Geometry only. Textures stay in the Unity slot folder.");

            int vertexOffset = 1;
            int triangleCount = 0;
            var filters = new List<MeshFilter>();
            prefab.GetComponentsInChildren(true, filters);
            var skins = new List<SkinnedMeshRenderer>();
            prefab.GetComponentsInChildren(true, skins);

            var sources = new List<(string name, Mesh mesh, Transform transform)>();
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh != null)
                {
                    sources.Add((filter.name, filter.sharedMesh, filter.transform));
                }
            }

            foreach (SkinnedMeshRenderer skin in skins)
            {
                if (skin.sharedMesh != null)
                {
                    sources.Add((skin.name, skin.sharedMesh, skin.transform));
                }
            }

            // Stable order so two exports of the same prefab are byte-identical.
            sources.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            foreach ((string name, Mesh mesh, Transform transform) in sources)
            {
                Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix * transform.localToWorldMatrix;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;

                sb.AppendLine($"g {name}");

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 v = toRoot.MultiplyPoint3x4(vertices[i]);

                    // Unity is left-handed with +X right; OBJ readers (and
                    // Blender) are right-handed. Flipping X here and letting
                    // the OBJ importer treat the file as Y-up keeps the model
                    // facing the same way it did in Unity.
                    sb.Append("v ").Append((-v.x).ToString("0.######", culture))
                      .Append(' ').Append(v.y.ToString("0.######", culture))
                      .Append(' ').Append(v.z.ToString("0.######", culture)).AppendLine();
                }

                for (int i = 0; i < normals.Length; i++)
                {
                    Vector3 n = toRoot.MultiplyVector(normals[i]).normalized;
                    sb.Append("vn ").Append((-n.x).ToString("0.####", culture))
                      .Append(' ').Append(n.y.ToString("0.####", culture))
                      .Append(' ').Append(n.z.ToString("0.####", culture)).AppendLine();
                }

                for (int i = 0; i < uvs.Length; i++)
                {
                    sb.Append("vt ").Append(uvs[i].x.ToString("0.#####", culture))
                      .Append(' ').Append(uvs[i].y.ToString("0.#####", culture)).AppendLine();
                }

                bool hasNormals = normals.Length == vertices.Length;
                bool hasUvs = uvs.Length == vertices.Length;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] indices = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        // Winding is reversed along with the X flip, or every
                        // face would arrive in Blender inside out.
                        sb.Append("f ")
                          .Append(Face(indices[i + 2] + vertexOffset, hasUvs, hasNormals)).Append(' ')
                          .Append(Face(indices[i + 1] + vertexOffset, hasUvs, hasNormals)).Append(' ')
                          .Append(Face(indices[i] + vertexOffset, hasUvs, hasNormals)).AppendLine();
                        triangleCount++;
                    }
                }

                vertexOffset += vertices.Length;
            }

            File.WriteAllText(path, sb.ToString());
            return triangleCount;
        }

        private static string Face(int index, bool hasUvs, bool hasNormals)
        {
            string text = index.ToString(CultureInfo.InvariantCulture);
            if (hasUvs && hasNormals)
            {
                return $"{text}/{text}/{text}";
            }

            if (hasNormals)
            {
                return $"{text}//{text}";
            }

            return hasUvs ? $"{text}/{text}" : text;
        }

        // ------------------------------------------------------------------
        // Status
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies import settings to every rigged FBX that has a manifest, then
        /// reports. Safe to run with nothing staged.
        /// </summary>
        [MenuItem("Ashfall/Meshcaster: Adopt Rigged Zombies", priority = 22)]
        public static void AdoptRiggedMenu()
        {
            Debug.Log(AdoptRigged());
        }

        public static void AdoptRiggedFromCommandLine()
        {
            Debug.Log(AdoptRigged());
            EditorApplication.Exit(0);
        }

        public static string AdoptRigged()
        {
            var report = new StringBuilder();
            report.AppendLine("[Ashfall] MESHCASTER_RIG_ADOPT");

            int adopted = 0;
            foreach (AshfallMeshcasterImport.Slot slot in EnemySlots())
            {
                RigManifest manifest = LoadManifest(slot.Key);
                if (manifest == null)
                {
                    report.AppendLine($"  [pending] {slot.Key,-18} no rig manifest");
                    continue;
                }

                GameObject model = FindRiggedModel(slot.Key);
                if (model == null)
                {
                    report.AppendLine($"  [broken]  {slot.Key,-18} manifest with no model beside it");
                    continue;
                }

                ConfigureImporter(AssetDatabase.GetAssetPath(model), manifest);

                if (IsRigVerified(slot.Key, out string reason))
                {
                    adopted++;
                    report.AppendLine($"  [rigged]  {slot.Key,-18} {manifest.boneCount} bones, " +
                                      $"{manifest.clips?.Length ?? 0} clips, {manifest.weighting}");
                }
                else
                {
                    report.AppendLine($"  [broken]  {slot.Key,-18} {reason}");
                }
            }

            report.Append($"  {adopted}/3 enemy slots rig-verified. " +
                          (adopted == 0
                              ? "Enemies keep the procedural or static body."
                              : "Run Ashfall > Build Playable Scene to adopt them."));
            return report.ToString();
        }

        /// <summary>One line per enemy slot, for the art-pass status report.</summary>
        public static void AppendStatus(StringBuilder sb)
        {
            sb.AppendLine("  rigging (Tools/Blender/rig_zombie.py):");

            foreach (AshfallMeshcasterImport.Slot slot in EnemySlots())
            {
                if (IsRigVerified(slot.Key, out string reason))
                {
                    RigManifest manifest = LoadManifest(slot.Key);
                    sb.AppendLine($"    [rigged]  {slot.Key,-18} {manifest.boneCount} bones, " +
                                  $"{string.Join("/", RequiredClips)}, weighting={manifest.weighting}");
                }
                else
                {
                    sb.AppendLine($"    [static]  {slot.Key,-18} {reason}");
                }
            }
        }
    }
}
