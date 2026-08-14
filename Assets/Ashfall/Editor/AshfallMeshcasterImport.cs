using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Adopts approved Meshcaster output into the game's prefabs.
    ///
    /// **This code never spends credits and never talks to a provider.** It has
    /// no network calls, no API keys, no endpoints and no knowledge that Meshy
    /// exists. Generation happens in the Meshcaster editor window, where a
    /// human clicks a button that shows the price first; this class only reads
    /// files that a human has already paid for, approved and copied into
    /// <see cref="StagingFolder"/>.
    ///
    /// The staging contract is deliberately dumb: one folder per slot, named
    /// for the slot, containing a prefab or an FBX. Whatever is in there is
    /// what the scene builder uses. Nothing in there is required -- with the
    /// folder empty, every slot falls back to the procedural body that ships in
    /// this repository, and the game is unchanged.
    ///
    /// See <c>Docs/MeshcasterArtPass.md</c> for the prompts, the target
    /// dimensions, the expected filenames and the credit ledger.
    /// </summary>
    public static class AshfallMeshcasterImport
    {
        public const string StagingFolder = "Assets/Ashfall/Art/Meshcaster";

        /// <summary>
        /// One art slot: where the approved model goes, and what it has to
        /// measure once it gets there.
        /// </summary>
        public readonly struct Slot
        {
            /// <summary>Folder name under <see cref="StagingFolder"/>, and the Meshcaster asset name.</summary>
            public readonly string Key;

            public readonly string DisplayName;

            /// <summary>Metres, tip to tail along the axis that matters. Drives the auto-fit.</summary>
            public readonly float TargetSize;

            /// <summary>Which axis <see cref="TargetSize"/> measures.</summary>
            public readonly Axis SizeAxis;

            /// <summary>Local placement applied after fitting.</summary>
            public readonly Vector3 PositionOffset;

            public readonly Vector3 Euler;

            public Slot(string key, string displayName, float targetSize, Axis sizeAxis,
                Vector3 positionOffset, Vector3 euler)
            {
                Key = key;
                DisplayName = displayName;
                TargetSize = targetSize;
                SizeAxis = sizeAxis;
                PositionOffset = positionOffset;
                Euler = euler;
            }
        }

        public enum Axis
        {
            X,
            Y,
            Z
        }

        /// <summary>
        /// The six slots this art pass covers.
        ///
        /// Target sizes are the dimensions the procedural bodies already
        /// occupy, so an imported model drops into the same hitboxes, the same
        /// nav radius and the same camera framing. They are also the numbers to
        /// type into Meshcaster's height field, which is why the document and
        /// this table have to agree -- so they are stated once, here.
        /// </summary>
        public static readonly Slot[] Slots =
        {
            new("Enemy_Shambler", "Shambler", 1.85f, Axis.Y, Vector3.zero, Vector3.zero),
            new("Enemy_Sprinter", "Sprinter", 1.68f, Axis.Y, Vector3.zero, Vector3.zero),
            new("Enemy_StormBrute", "Storm Brute", 2.85f, Axis.Y, Vector3.zero, Vector3.zero),

            // Weapons are measured along Z because a viewmodel's length is what
            // has to match: the muzzle transform is fixed, and a barrel that
            // ends in the wrong place puts the flash inside the receiver.
            new("Weapon_MeridianSidearm", "Meridian Sidearm", 0.24f, Axis.Z,
                new Vector3(0f, 0.02f, 0.02f), Vector3.zero),
            // The Meshcaster shotgun's source axis remains reversed after the
            // generic centroid pass, so keep this explicit viewmodel correction
            // at the canonical attachment point rather than hiding it in gameplay.
            new("Weapon_BreakwaterShotgun", "Breakwater Shotgun", 1.02f, Axis.Z,
                new Vector3(0f, 0.01f, 0.15f), new Vector3(0f, 180f, 0f)),
            new("Weapon_Arc9Rifle", "Arc-9 Rifle", 1.13f, Axis.Z,
                new Vector3(0f, 0.01f, 0.19f), Vector3.zero)
        };

        public static string KeyForArchetype(EnemyArchetype archetype)
        {
            switch (archetype)
            {
                case EnemyArchetype.Sprinter: return "Enemy_Sprinter";
                case EnemyArchetype.StormBrute: return "Enemy_StormBrute";
                default: return "Enemy_Shambler";
            }
        }

        public static bool TryGetSlot(string key, out Slot slot)
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (Slots[i].Key == key)
                {
                    slot = Slots[i];
                    return true;
                }
            }

            slot = default;
            return false;
        }

        // ------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------

        /// <summary>
        /// The approved *static* model for a slot, or null.
        ///
        /// A prefab wins over a raw model: Meshcaster's own output is a prefab
        /// with its URP material already built, and re-deriving that from the
        /// FBX would throw away the textures it paid for.
        ///
        /// The slot's <c>Rigged/</c> sub-folder is skipped. What lands there is
        /// this repository's own Blender output, not the approved delivery, and
        /// letting it answer here would make the rigged FBX compete with the
        /// mesh it was built from. <see cref="AshfallZombieRig"/> owns that
        /// folder.
        /// </summary>
        public static GameObject FindApprovedModel(string key)
        {
            string folder = $"{StagingFolder}/{key}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                return null;
            }

            string riggedPrefix = $"{folder}/{AshfallZombieRig.RiggedSubfolder}/";

            foreach (string filter in new[] { "t:Prefab", "t:Model" })
            {
                string[] guids = AssetDatabase.FindAssets(filter, new[] { folder });
                if (guids.Length == 0)
                {
                    continue;
                }

                // Deterministic: two candidates must not resolve differently on
                // two machines, so pick by path rather than by search order.
                System.Array.Sort(guids, (a, b) => string.CompareOrdinal(
                    AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));

                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (path.StartsWith(riggedPrefix, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset != null)
                    {
                        return asset;
                    }
                }
            }

            return null;
        }

        public static bool HasApprovedModel(string key) => FindApprovedModel(key) != null;

        /// <summary>True when at least one slot has an approved model waiting.</summary>
        public static bool AnyApproved()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                if (HasApprovedModel(Slots[i].Key))
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Attachment
        // ------------------------------------------------------------------

        /// <summary>
        /// Instantiates the approved model under <paramref name="parent"/>,
        /// scaled and placed to the slot's target dimensions.
        ///
        /// Returns null when there is nothing staged, which is the signal to
        /// the caller that the procedural body stays visible. Any renderers it
        /// creates are appended to <paramref name="renderers"/> so the hit
        /// flash and death dissolve keep working on the new mesh.
        /// </summary>
        public static GameObject TryAttach(Transform parent, string key, List<Renderer> renderers)
        {
            if (parent == null || !TryGetSlot(key, out Slot slot))
            {
                return null;
            }

            GameObject source = FindApprovedModel(key);
            if (source == null)
            {
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            if (instance == null)
            {
                return null;
            }

            instance.name = "MeshcasterBody";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(slot.Euler);
            instance.transform.localScale = Vector3.one;

            // Unpack so the prefab is not a nested instance the scene builder
            // would have to keep in sync; the staged asset stays the source of
            // truth on disk, but the game prefab owns its own copy.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            FitToSlot(instance, slot);

            var found = new List<Renderer>();
            instance.GetComponentsInChildren(true, found);
            renderers?.AddRange(found);

            // Colliders in a generated model are not the game's hitboxes and
            // would shadow them; the real ones are built by the prefab factory.
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }

            Debug.Log($"[Ashfall] Meshcaster art adopted for {slot.DisplayName}: " +
                      $"{AssetDatabase.GetAssetPath(source)} ({found.Count} renderer(s)).");
            return instance;
        }

        /// <summary>
        /// Rescales and grounds the model so it occupies the slot's target size.
        ///
        /// Meshcaster's resize step already asks for a real-world height, so in
        /// the normal case this is close to a no-op. It exists because "close
        /// to" is not "exactly", and a brute that arrives 8% short is a brute
        /// whose head is inside its own head hitbox.
        /// </summary>
        public static void FitToSlot(GameObject instance, Slot slot)
        {
            Bounds bounds = LocalBounds(instance);
            if (bounds.size.sqrMagnitude < 1e-8f)
            {
                Debug.LogWarning($"[Ashfall] Meshcaster model for {slot.Key} has no renderer bounds; left unscaled.");
                return;
            }

            float measured = slot.SizeAxis switch
            {
                Axis.X => bounds.size.x,
                Axis.Z => bounds.size.z,
                _ => bounds.size.y
            };

            if (measured > 1e-5f)
            {
                float scale = slot.TargetSize / measured;
                instance.transform.localScale = Vector3.one * scale;

                if (Mathf.Abs(scale - 1f) > 0.02f)
                {
                    Debug.Log($"[Ashfall] Meshcaster model for {slot.Key} measured {measured:0.###} m " +
                              $"on {slot.SizeAxis}; scaled by {scale:0.###} to hit {slot.TargetSize:0.###} m.");
                }
            }

            // Re-measure after scaling and sit the model on the local origin,
            // which for an enemy is the floor under its feet.
            Bounds scaled = LocalBounds(instance);
            Vector3 offset = slot.PositionOffset;
            offset.y -= scaled.min.y;
            offset.x -= scaled.center.x;
            instance.transform.localPosition += offset;
        }

        private static Bounds LocalBounds(GameObject instance)
        {
            var renderers = new List<Renderer>();
            instance.GetComponentsInChildren(true, renderers);

            Transform reference = instance.transform.parent != null ? instance.transform.parent : instance.transform;
            var bounds = new Bounds();
            bool started = false;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] is not MeshRenderer && renderers[i] is not SkinnedMeshRenderer)
                {
                    continue;
                }

                var filter = renderers[i].GetComponent<MeshFilter>();
                Mesh mesh = filter != null
                    ? filter.sharedMesh
                    : (renderers[i] as SkinnedMeshRenderer)?.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Bounds meshBounds = mesh.bounds;
                Matrix4x4 toReference = reference.worldToLocalMatrix * renderers[i].transform.localToWorldMatrix;

                // Transform all eight corners: a rotated model's axis-aligned
                // bounds are not the rotation of its axis-aligned bounds.
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                        (c & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                        (c & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);

                    Vector3 point = toReference.MultiplyPoint3x4(corner);
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

        // ------------------------------------------------------------------
        // Status reporting
        // ------------------------------------------------------------------

        [MenuItem("Ashfall/Meshcaster Art Pass Status", priority = 20)]
        public static void ReportStatusMenu()
        {
            Debug.Log(BuildStatusReport());
        }

        /// <summary>
        /// Batch entry point. Prints which slots are staged and exits 0 either
        /// way -- an empty staging folder is the expected state until someone
        /// has run the generation jobs, not a failure.
        /// </summary>
        public static void ReportFromCommandLine()
        {
            Debug.Log(BuildStatusReport());
            EditorApplication.Exit(0);
        }

        public static string BuildStatusReport()
        {
            EnsureStagingFolders();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Ashfall] MESHCASTER_ART_PASS_STATUS");
            sb.AppendLine($"  staging folder: {StagingFolder}");

            int staged = 0;
            for (int i = 0; i < Slots.Length; i++)
            {
                Slot slot = Slots[i];
                GameObject model = FindApprovedModel(slot.Key);
                if (model != null)
                {
                    staged++;
                }

                sb.AppendLine(model != null
                    ? $"  [staged]  {slot.Key,-24} {AssetDatabase.GetAssetPath(model)}"
                    : $"  [pending] {slot.Key,-24} target {slot.TargetSize:0.00} m on {slot.SizeAxis}");
            }

            sb.AppendLine($"  {staged}/{Slots.Length} slots staged. " +
                          (staged == 0
                              ? "All slots fall back to the procedural bodies in this repository."
                              : "Run Ashfall > Build Playable Scene to adopt them."));

            AshfallZombieRig.AppendStatus(sb);

            sb.Append("  No credits are spent by this tool. Generation happens in the Meshcaster window, " +
                      "behind its own priced approval click.");
            return sb.ToString();
        }

        /// <summary>Creates the six empty slot folders so the layout is discoverable.</summary>
        public static void EnsureStagingFolders()
        {
            AshfallAssetUtility.EnsureFolder(StagingFolder);
            for (int i = 0; i < Slots.Length; i++)
            {
                AshfallAssetUtility.EnsureFolder($"{StagingFolder}/{Slots[i].Key}");
            }

            string readme = Path.Combine(StagingFolder, "README.md");
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme, StagingReadme());
                AssetDatabase.ImportAsset(readme);
            }
        }

        private static string StagingReadme()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Meshcaster staging");
            sb.AppendLine();
            sb.AppendLine("Drop **approved** Meshcaster output into the matching folder, then run");
            sb.AppendLine("`Ashfall > Build Playable Scene`. Each folder takes a Unity prefab (what");
            sb.AppendLine("Meshcaster produces) or an FBX. Raw `.glb` is not imported by Unity in this");
            sb.AppendLine("project -- let Meshcaster convert it, and copy the generated folder here.");
            sb.AppendLine();
            sb.AppendLine("| Folder | Asset | Target size |");
            sb.AppendLine("| --- | --- | --- |");
            for (int i = 0; i < Slots.Length; i++)
            {
                sb.AppendLine($"| `{Slots[i].Key}` | {Slots[i].DisplayName} | {Slots[i].TargetSize:0.00} m on {Slots[i].SizeAxis} |");
            }

            sb.AppendLine();
            sb.AppendLine("Empty folders are fine: every slot falls back to the procedural body.");
            sb.AppendLine();
            sb.AppendLine("## Rigging the three enemies");
            sb.AppendLine();
            sb.AppendLine("Enemy slots take one more step, which spends nothing:");
            sb.AppendLine();
            sb.AppendLine("1. `Ashfall > Meshcaster: Export Slot Source for Blender`");
            sb.AppendLine("2. `blender --background --python Tools/Blender/rig_zombie.py -- --all`");
            sb.AppendLine("3. `Ashfall > Meshcaster: Adopt Rigged Zombies`");
            sb.AppendLine("4. `Ashfall > Build Playable Scene`");
            sb.AppendLine();
            sb.AppendLine("Step 2 writes `<slot>/Rigged/`. Nothing else may be put in that folder --");
            sb.AppendLine("it is generated, and the static-mesh importer deliberately ignores it.");
            sb.AppendLine();
            sb.AppendLine("Prompts, import settings and the credit ledger are in `Docs/MeshcasterArtPass.md`.");
            return sb.ToString();
        }
    }
}
