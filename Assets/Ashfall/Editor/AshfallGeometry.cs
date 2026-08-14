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

        // ------------------------------------------------------------------
        // Organic shapes: lofts, ellipsoids and chamfered boxes
        //
        // A box is the wrong primitive for a body. Ten boxes stacked into a
        // torso read as ten boxes no matter how carefully they are placed,
        // because every silhouette edge is a straight line and every surface
        // normal is one of six constants -- so the lighting never curves and
        // the eye gets no information about volume.
        //
        // A loft fixes both at once: the outline follows a spine the caller
        // draws, and the normals are computed from the surface itself, so a
        // shoulder actually catches light like a shoulder. The cost is about
        // 100 triangles per limb, which at twenty-four concurrent enemies is
        // still under thirty thousand triangles for the whole field.
        // ------------------------------------------------------------------

        /// <summary>One cross-section of a lofted shape: an ellipse at a point on the spine.</summary>
        public readonly struct LoftRing
        {
            public readonly Vector3 Center;
            public readonly float RadiusX;
            public readonly float RadiusZ;

            public LoftRing(Vector3 center, float radiusX, float radiusZ)
            {
                Center = center;
                RadiusX = radiusX;
                RadiusZ = radiusZ;
            }

            public LoftRing(Vector3 center, float radius) : this(center, radius, radius)
            {
            }
        }

        /// <summary>
        /// Skins a series of elliptical cross-sections into one smooth surface.
        ///
        /// Normals are derived from the surface, not from
        /// <c>RecalculateNormals</c>: the UV seam duplicates its vertices, and
        /// averaging would leave a visible crease straight down the front of
        /// every body. Computing them from the ring and spine tangents gives
        /// the seam the same normal on both sides for free.
        /// </summary>
        public static Mesh Loft(
            string name,
            IReadOnlyList<LoftRing> rings,
            int segments = 10,
            bool capStart = true,
            bool capEnd = true,
            float tileSize = 1f)
        {
            if (rings == null || rings.Count < 2)
            {
                throw new System.ArgumentException($"Loft '{name}' needs at least two rings.", nameof(rings));
            }

            segments = Mathf.Max(3, segments);
            string key = $"Loft_{name}_{segments}_{(capStart ? 1 : 0)}{(capEnd ? 1 : 0)}_{HashRings(rings, tileSize):x8}";
            if (Cache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            int ringCount = rings.Count;
            int perRing = segments + 1;

            var vertices = new List<Vector3>(ringCount * perRing + 2 * (segments + 2));
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(ringCount * segments * 6 + segments * 6);

            // v runs with real distance along the spine so a long limb and a
            // short one land at the same texel density.
            var spineV = new float[ringCount];
            for (int i = 1; i < ringCount; i++)
            {
                spineV[i] = spineV[i - 1] + Vector3.Distance(rings[i - 1].Center, rings[i].Center);
            }

            for (int i = 0; i < ringCount; i++)
            {
                LoftRing ring = rings[i];

                // Central difference along the spine, one-sided at the ends.
                Vector3 spineTangent = rings[Mathf.Min(i + 1, ringCount - 1)].Center
                                       - rings[Mathf.Max(i - 1, 0)].Center;
                if (spineTangent.sqrMagnitude < 1e-10f)
                {
                    spineTangent = Vector3.up;
                }

                float radiusSlopeX = (rings[Mathf.Min(i + 1, ringCount - 1)].RadiusX
                                      - rings[Mathf.Max(i - 1, 0)].RadiusX);
                float radiusSlopeZ = (rings[Mathf.Min(i + 1, ringCount - 1)].RadiusZ
                                      - rings[Mathf.Max(i - 1, 0)].RadiusZ);

                float circumference = Mathf.PI * (ring.RadiusX + ring.RadiusZ);

                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    float angle = t * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    var offset = new Vector3(cos * ring.RadiusX, 0f, sin * ring.RadiusZ);
                    vertices.Add(ring.Center + offset);

                    // The exact surface normal, from the two surface tangents.
                    //
                    // The meridian tangent has to include the radius change,
                    // not just the spine direction. Leaving that term out is
                    // the difference between a cone that shades like a cone and
                    // one that shades like a cylinder -- and at an ellipsoid's
                    // poles, where the radius collapses to nothing, it is the
                    // difference between a sphere and a lantern.
                    var radial = new Vector3(
                        cos * Mathf.Max(ring.RadiusZ, 1e-5f),
                        0f,
                        sin * Mathf.Max(ring.RadiusX, 1e-5f)).normalized;

                    Vector3 alongRing = new Vector3(-sin * ring.RadiusX, 0f, cos * ring.RadiusZ).normalized;
                    Vector3 alongSpine = spineTangent + new Vector3(cos * radiusSlopeX, 0f, sin * radiusSlopeZ);

                    Vector3 normal = Vector3.Cross(alongSpine, alongRing);
                    if (normal.sqrMagnitude < 1e-12f)
                    {
                        normal = radial;
                    }

                    normal = normal.normalized;
                    if (Vector3.Dot(normal, radial) < 0f)
                    {
                        normal = -normal;
                    }

                    normals.Add(normal);

                    uvs.Add(new Vector2(t * circumference / tileSize, spineV[i] / tileSize));
                }
            }

            for (int i = 0; i < ringCount - 1; i++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = i * perRing + s;
                    int b = a + 1;
                    int c = a + perRing;
                    int d = c + 1;

                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            if (capStart)
            {
                AddLoftCap(vertices, normals, uvs, triangles, rings[0], segments, -1, tileSize);
            }

            if (capEnd)
            {
                AddLoftCap(vertices, normals, uvs, triangles, rings[ringCount - 1], segments, 1, tileSize);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            return Register(key, mesh);
        }

        private static void AddLoftCap(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
            LoftRing ring, int segments, int direction, float tileSize)
        {
            Vector3 normal = direction > 0 ? Vector3.up : Vector3.down;

            int center = vertices.Count;
            vertices.Add(ring.Center);
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int s = 0; s <= segments; s++)
            {
                float angle = s / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices.Add(ring.Center + new Vector3(cos * ring.RadiusX, 0f, sin * ring.RadiusZ));
                normals.Add(normal);
                uvs.Add(new Vector2(cos * ring.RadiusX / tileSize + 0.5f, sin * ring.RadiusZ / tileSize + 0.5f));
            }

            for (int s = 0; s < segments; s++)
            {
                if (direction > 0)
                {
                    triangles.Add(center);
                    triangles.Add(center + s + 2);
                    triangles.Add(center + s + 1);
                }
                else
                {
                    triangles.Add(center);
                    triangles.Add(center + s + 1);
                    triangles.Add(center + s + 2);
                }
            }
        }

        /// <summary>
        /// A tapered, optionally curved limb: the workhorse behind every arm and leg.
        /// </summary>
        /// <param name="bend">Sideways displacement of the midpoint, in local +Z.</param>
        public static Mesh Limb(
            string name, float length, float rootRadius, float midRadius, float tipRadius,
            float bend = 0f, int segments = 8, bool round = true)
        {
            var rings = new List<LoftRing>(5);
            for (int i = 0; i < 5; i++)
            {
                float t = i / 4f;
                // Sine bulge puts the bend at the middle and leaves the ends where
                // the caller put them, so joints still line up after the curve.
                float z = Mathf.Sin(t * Mathf.PI) * bend;
                float radius = t < 0.5f
                    ? Mathf.Lerp(rootRadius, midRadius, t * 2f)
                    : Mathf.Lerp(midRadius, tipRadius, (t - 0.5f) * 2f);

                // Rounding the ends costs two rings and removes the flat disc
                // that otherwise reads as a cut-off pipe.
                if (round && (i == 0 || i == 4))
                {
                    radius *= 0.62f;
                }

                rings.Add(new LoftRing(new Vector3(0f, -length * 0.5f + length * t, z), radius));
            }

            return Loft(name, rings, segments, capStart: true, capEnd: true, tileSize: 0.5f);
        }

        /// <summary>A smooth ellipsoid. Heads, hands, feet, canister cores.</summary>
        public static Mesh Ellipsoid(string name, Vector3 radii, int segments = 12, int stacks = 8)
        {
            var rings = new List<LoftRing>(stacks + 1);
            for (int i = 0; i <= stacks; i++)
            {
                float t = i / (float)stacks;
                float phi = t * Mathf.PI;
                float y = -Mathf.Cos(phi) * radii.y;
                float r = Mathf.Sin(phi);
                rings.Add(new LoftRing(new Vector3(0f, y, 0f),
                    Mathf.Max(r * radii.x, 1e-4f),
                    Mathf.Max(r * radii.z, 1e-4f)));
            }

            return Loft(name, rings, segments, capStart: false, capEnd: false, tileSize: 0.5f);
        }

        /// <summary>
        /// A box with its edges knocked off.
        ///
        /// On weapons this matters more than any amount of extra detail: a
        /// hard 90-degree edge produces a single hard lighting break, while a
        /// two-millimetre bevel catches a highlight along the whole length of
        /// the receiver and is what makes a viewmodel read as machined metal.
        /// </summary>
        public static Mesh Chamfer(Vector3 size, float bevel, float tileSize = 0.25f)
        {
            string key = $"Cham_{size.x:0.####}x{size.y:0.####}x{size.z:0.####}_b{bevel:0.####}_t{tileSize:0.##}";
            if (Cache.TryGetValue(key, out Mesh cached))
            {
                return cached;
            }

            Vector3 h = size * 0.5f;
            float maxBevel = Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.48f;
            bevel = Mathf.Clamp(bevel, 0.0004f, Mathf.Max(0.0004f, maxBevel));

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // Four horizontal rings: the two bevelled ends pull in by the bevel
            // amount, the two middle ones sit at full width.
            var profile = new[]
            {
                (y: -h.y, inset: bevel),
                (y: -h.y + bevel, inset: 0f),
                (y: h.y - bevel, inset: 0f),
                (y: h.y, inset: bevel)
            };

            const int corner = 2;               // subdivisions per rounded corner
            int perRing = 4 * (corner + 1);
            var quadrants = new[] { (sx: 1f, sz: 1f), (sx: -1f, sz: 1f), (sx: -1f, sz: -1f), (sx: 1f, sz: -1f) };

            for (int p = 0; p < profile.Length; p++)
            {
                float y = profile[p].y;
                float cx = Mathf.Max(h.x - profile[p].inset - bevel, 0f);
                float cz = Mathf.Max(h.z - profile[p].inset - bevel, 0f);
                float perimeter = 0f;

                for (int c = 0; c < 4; c++)
                {
                    (float sx, float sz) = quadrants[c];
                    for (int k = 0; k <= corner; k++)
                    {
                        // Sweep each corner from the +X face round to the +Z
                        // face, alternating direction so the ring stays convex.
                        float angle = k / (float)corner * Mathf.PI * 0.5f;
                        float ax = c % 2 == 0 ? Mathf.Cos(angle) : Mathf.Sin(angle);
                        float az = c % 2 == 0 ? Mathf.Sin(angle) : Mathf.Cos(angle);

                        var v = new Vector3(sx * (cx + ax * bevel), y, sz * (cz + az * bevel));
                        if (vertices.Count > p * perRing)
                        {
                            perimeter += Vector3.Distance(vertices[vertices.Count - 1], v);
                        }

                        vertices.Add(v);
                        uvs.Add(new Vector2(perimeter / tileSize, (y + h.y) / tileSize));
                    }
                }
            }

            for (int p = 0; p < profile.Length - 1; p++)
            {
                for (int s = 0; s < perRing; s++)
                {
                    int a = p * perRing + s;
                    int b = p * perRing + (s + 1) % perRing;
                    int c = a + perRing;
                    int d = p * perRing + perRing + (s + 1) % perRing;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            AddChamferCap(vertices, uvs, triangles, 0, perRing, false, tileSize);
            AddChamferCap(vertices, uvs, triangles, (profile.Length - 1) * perRing, perRing, true, tileSize);

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);

            // The caps re-emit their own vertices, so averaging gives the side
            // wall a smooth bevel highlight and leaves the caps flat -- exactly
            // the split a chamfered part wants.
            mesh.RecalculateNormals();
            return Register(key, mesh);
        }

        private static void AddChamferCap(
            List<Vector3> vertices, List<Vector2> uvs, List<int> triangles,
            int ringStart, int perRing, bool facingUp, float tileSize)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < perRing; i++)
            {
                sum += vertices[ringStart + i];
            }

            int center = vertices.Count;
            vertices.Add(sum / perRing);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int rim = vertices.Count;
            for (int i = 0; i < perRing; i++)
            {
                Vector3 v = vertices[ringStart + i];
                vertices.Add(v);
                uvs.Add(new Vector2(v.x / tileSize + 0.5f, v.z / tileSize + 0.5f));
            }

            for (int i = 0; i < perRing; i++)
            {
                int a = rim + i;
                int b = rim + (i + 1) % perRing;
                if (facingUp)
                {
                    triangles.Add(center); triangles.Add(b); triangles.Add(a);
                }
                else
                {
                    triangles.Add(center); triangles.Add(a); triangles.Add(b);
                }
            }
        }

        private static uint HashRings(IReadOnlyList<LoftRing> rings, float tileSize)
        {
            // FNV-1a over quantised values: two callers that ask for the same
            // shape share one mesh, and a shape that changes gets a new key
            // rather than silently reusing a stale asset.
            unchecked
            {
                uint hash = 2166136261u;

                void Feed(float value)
                {
                    int quantised = Mathf.RoundToInt(value * 10000f);
                    for (int i = 0; i < 4; i++)
                    {
                        hash = (hash ^ (byte)(quantised >> (i * 8))) * 16777619u;
                    }
                }

                for (int i = 0; i < rings.Count; i++)
                {
                    Feed(rings[i].Center.x);
                    Feed(rings[i].Center.y);
                    Feed(rings[i].Center.z);
                    Feed(rings[i].RadiusX);
                    Feed(rings[i].RadiusZ);
                }

                Feed(tileSize);
                return hash;
            }
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
