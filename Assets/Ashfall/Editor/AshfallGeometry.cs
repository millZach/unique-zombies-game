using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Procedural mesh factory for the station kit.
    ///
    /// Unity's primitive cube maps every face to 0..1, which stretches a texture across
    /// a twenty-metre wall and makes the whole map read as untextured grey. These boxes
    /// carry world-scale UVs instead: a 20x4 wall and a 2x2 crate get the same texel
    /// density, which is most of the difference between "programmer art" and
    /// "deliberately low-fi".
    ///
    /// Meshes are cached by their parameters and written into one shared asset so the
    /// scene references survive a reimport.
    /// </summary>
    public static class AshfallGeometry
    {
        private static readonly Dictionary<string, Mesh> Cache = new();
        private static readonly List<Mesh> Created = new();

        private static string _assetPath;
        private static bool _hasMainAsset;

        public static IReadOnlyList<Mesh> CreatedMeshes => Created;

        public static void ResetCache()
        {
            Cache.Clear();
            Created.Clear();
            _assetPath = null;
            _hasMainAsset = false;
        }

        /// <summary>
        /// Starts writing every generated mesh into one shared asset immediately.
        ///
        /// Persisting at creation time rather than at the end is not optional: prefabs
        /// and the scene are saved while generation is still running, and a MeshFilter
        /// pointing at a mesh that is not yet an asset serialises as a null reference.
        /// Getting this wrong produces a project that looks fine until it is reopened.
        /// </summary>
        public static void BeginPersist(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }

            _assetPath = assetPath;
            _hasMainAsset = false;
        }

        public static void EndPersist()
        {
            if (_assetPath == null)
            {
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static Mesh Register(string key, Mesh mesh)
        {
            mesh.name = key;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            Cache[key] = mesh;
            Created.Add(mesh);

            if (_assetPath != null)
            {
                if (!_hasMainAsset)
                {
                    AssetDatabase.CreateAsset(mesh, _assetPath);
                    _hasMainAsset = true;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(mesh, _assetPath);
                }
            }

            return mesh;
        }

        // ------------------------------------------------------------------
        // Box with per-face, world-scale UVs
        // ------------------------------------------------------------------

        public static Mesh Box(Vector3 size, float tileSize = 2f)
        {
            string key = $"Box_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_t{tileSize:0.##}";
            if (Cache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            Vector3 h = size * 0.5f;
            var vertices = new List<Vector3>(24);
            var normals = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var triangles = new List<int>(36);

            void Face(Vector3 origin, Vector3 right, Vector3 up, float uSpan, float vSpan)
            {
                int baseIndex = vertices.Count;
                Vector3 normal = Vector3.Cross(right, up).normalized;

                vertices.Add(origin);
                vertices.Add(origin + right);
                vertices.Add(origin + right + up);
                vertices.Add(origin + up);

                for (int i = 0; i < 4; i++)
                {
                    normals.Add(normal);
                }

                float u = Mathf.Max(0.01f, uSpan / tileSize);
                float v = Mathf.Max(0.01f, vSpan / tileSize);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, v));
                uvs.Add(new Vector2(0f, v));

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }

            // +X / -X
            Face(new Vector3(h.x, -h.y, h.z), new Vector3(0, 0, -size.z), new Vector3(0, size.y, 0), size.z, size.y);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(0, 0, size.z), new Vector3(0, size.y, 0), size.z, size.y);
            // +Y / -Y
            Face(new Vector3(-h.x, h.y, h.z), new Vector3(size.x, 0, 0), new Vector3(0, 0, -size.z), size.x, size.z);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(size.x, 0, 0), new Vector3(0, 0, size.z), size.x, size.z);
            // +Z / -Z
            Face(new Vector3(-h.x, -h.y, h.z), new Vector3(size.x, 0, 0), new Vector3(0, size.y, 0), size.x, size.y);
            Face(new Vector3(h.x, -h.y, -h.z), new Vector3(-size.x, 0, 0), new Vector3(0, size.y, 0), size.x, size.y);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return Register(key, mesh);
        }

        // ------------------------------------------------------------------
        // Cylinder / pipe
        // ------------------------------------------------------------------

        public static Mesh Cylinder(float radius, float height, int segments = 16, float tileSize = 2f, bool capped = true)
        {
            string key = $"Cyl_{radius:0.###}_{height:0.###}_{segments}_t{tileSize:0.##}_{(capped ? "c" : "o")}";
            if (Cache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            float circumference = 2f * Mathf.PI * radius;
            float halfHeight = height * 0.5f;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = t * Mathf.PI * 2f;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                var normal = new Vector3(x, 0f, z);

                vertices.Add(new Vector3(x * radius, -halfHeight, z * radius));
                vertices.Add(new Vector3(x * radius, halfHeight, z * radius));
                normals.Add(normal);
                normals.Add(normal);
                uvs.Add(new Vector2(t * circumference / tileSize, 0f));
                uvs.Add(new Vector2(t * circumference / tileSize, height / tileSize));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                triangles.Add(a);
                triangles.Add(a + 1);
                triangles.Add(a + 2);
                triangles.Add(a + 1);
                triangles.Add(a + 3);
                triangles.Add(a + 2);
            }

            if (capped)
            {
                AddCap(vertices, normals, uvs, triangles, radius, halfHeight, segments, Vector3.up, tileSize);
                AddCap(vertices, normals, uvs, triangles, radius, -halfHeight, segments, Vector3.down, tileSize);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return Register(key, mesh);
        }

        private static void AddCap(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            float radius, float y, int segments, Vector3 normal, float tileSize)
        {
            int center = vertices.Count;
            vertices.Add(new Vector3(0f, y, 0f));
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                vertices.Add(new Vector3(x * radius, y, z * radius));
                normals.Add(normal);
                uvs.Add(new Vector2(x * radius / tileSize + 0.5f, z * radius / tileSize + 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                if (normal.y > 0f)
                {
                    triangles.Add(center);
                    triangles.Add(center + i + 2);
                    triangles.Add(center + i + 1);
                }
                else
                {
                    triangles.Add(center);
                    triangles.Add(center + i + 1);
                    triangles.Add(center + i + 2);
                }
            }
        }

        // ------------------------------------------------------------------
        // Wedge / ramp
        // ------------------------------------------------------------------

        /// <summary>A ramp rising along +Z, used for stairs and loading docks.</summary>
        public static Mesh Ramp(Vector3 size, float tileSize = 2f)
        {
            string key = $"Ramp_{size.x:0.###}x{size.y:0.###}x{size.z:0.###}_t{tileSize:0.##}";
            if (Cache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            Vector3 h = size * 0.5f;

            Vector3 a = new(-h.x, -h.y, -h.z);
            Vector3 b = new(h.x, -h.y, -h.z);
            Vector3 c = new(h.x, -h.y, h.z);
            Vector3 d = new(-h.x, -h.y, h.z);
            Vector3 e = new(-h.x, h.y, h.z);
            Vector3 f = new(h.x, h.y, h.z);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float uSpan, float vSpan)
            {
                int baseIndex = vertices.Count;
                Vector3 normal = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                vertices.Add(p0); vertices.Add(p1); vertices.Add(p2); vertices.Add(p3);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                float u = uSpan / tileSize;
                float v = vSpan / tileSize;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(u, 0));
                uvs.Add(new Vector2(u, v)); uvs.Add(new Vector2(0, v));
                triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
            }

            void Tri(Vector3 p0, Vector3 p1, Vector3 p2, float scale)
            {
                int baseIndex = vertices.Count;
                Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                vertices.Add(p0); vertices.Add(p1); vertices.Add(p2);
                for (int i = 0; i < 3; i++) normals.Add(normal);
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(scale, 0));
                uvs.Add(new Vector2(scale, scale));
                triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
            }

            float slope = Mathf.Sqrt(size.y * size.y + size.z * size.z);
            Quad(a, b, c, d, size.x, size.z);          // bottom
            Quad(d, c, f, e, size.x, slope);           // slope
            Quad(c, b, f, f, size.x, size.y);          // back (degenerate-safe)
            Quad(b, a, d, c, size.x, 0.001f);          // seam filler
            Tri(a, d, e, size.z / tileSize);           // left side
            Tri(b, f, c, size.z / tileSize);           // right side
            Quad(e, f, c, d, size.x, 0.001f);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return Register(key, mesh);
        }

        /// <summary>Flat quad facing +Z, for signage and decals.</summary>
        public static Mesh Quad(Vector2 size, float tileSize = 1f)
        {
            string key = $"Quad_{size.x:0.###}x{size.y:0.###}_t{tileSize:0.##}";
            if (Cache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            Vector2 h = size * 0.5f;
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-h.x, -h.y, 0f),
                    new Vector3(h.x, -h.y, 0f),
                    new Vector3(h.x, h.y, 0f),
                    new Vector3(-h.x, h.y, 0f)
                },
                normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(size.x / tileSize, 0f),
                    new Vector2(size.x / tileSize, size.y / tileSize),
                    new Vector2(0f, size.y / tileSize)
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };

            return Register(key, mesh);
        }
    }
}
