using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    /// <summary>Small shared helpers used across the build steps.</summary>
    public static class AshfallAssetUtility
    {
        public const string RootFolder = "Assets/Ashfall";
        public const string GeneratedFolder = "Assets/Ashfall/Art/Generated";
        public const string MaterialFolder = "Assets/Ashfall/Art/Generated/Materials";
        public const string MeshFolder = "Assets/Ashfall/Art/Generated/Meshes";
        public const string PrefabFolder = "Assets/Ashfall/Prefabs";
        public const string DataFolder = "Assets/Ashfall/Data";
        public const string SettingsFolder = "Assets/Ashfall/Settings";
        public const string SceneFolder = "Assets/Ashfall/Scenes";

        /// <summary>Creates every missing folder along an "Assets/a/b/c" path.</summary>
        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }

            Directory.CreateDirectory(path);
        }

        /// <summary>Creates or overwrites a ScriptableObject asset and returns the live instance.</summary>
        public static T CreateOrReplace<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            var instance = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(instance, path);
            return instance;
        }

        public static GameObject NewChild(Transform parent, string name, Vector3 localPosition = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go;
        }

        /// <summary>Applies a layer to an object and everything beneath it.</summary>
        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0)
            {
                return;
            }

            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>Saves a prefab, replacing any existing one at the path.</summary>
        public static GameObject SavePrefab(GameObject source, string path)
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, path);
            Object.DestroyImmediate(source);
            return prefab;
        }
    }
}
