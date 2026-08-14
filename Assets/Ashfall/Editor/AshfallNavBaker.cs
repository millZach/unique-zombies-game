using System.Collections.Generic;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Nav;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Bakes the waypoint graph by probing the finished geometry.
    ///
    /// The station is sampled on a grid; every floor surface a body could stand on
    /// becomes a node, and neighbours are linked when a capsule can actually sweep
    /// between them. Links whose midpoint falls inside a door's gate volume are tagged
    /// with that gate, so closing the door closes the route for the AI too.
    ///
    /// Sampling the real colliders rather than trusting the layout constants means the
    /// graph cannot silently disagree with the level.
    /// </summary>
    public static class AshfallNavBaker
    {
        public struct GateVolume
        {
            public string Name;
            public Bounds Bounds;
        }

        private const float Spacing = 2.0f;
        private const float AgentRadius = 0.45f;
        private const float AgentHeight = 1.8f;
        private const float MaxStepUp = 0.55f;
        private const float ProbeTop = 22f;
        private const float MaxLinkSlope = 0.85f;

        public struct BakeReport
        {
            public int NodeCount;
            public int LinkCount;
            public int GateCount;
            public int GatedLinkCount;
            public int IsolatedNodeCount;
        }

        public static BakeReport Bake(NavGraph graph, Bounds bounds, IReadOnlyList<GateVolume> gateVolumes)
        {
            Physics.SyncTransforms();

            var nodes = new List<NavNode>(2048);
            var nodeZones = new List<int>(2048);
            LayerMask blocking = AshfallLayers.BlockingMask;

            int stepsX = Mathf.CeilToInt(bounds.size.x / Spacing);
            int stepsZ = Mathf.CeilToInt(bounds.size.z / Spacing);

            var hits = new RaycastHit[16];

            for (int ix = 0; ix <= stepsX; ix++)
            {
                for (int iz = 0; iz <= stepsZ; iz++)
                {
                    float x = bounds.min.x + ix * Spacing;
                    float z = bounds.min.z + iz * Spacing;

                    var origin = new Vector3(x, bounds.max.y + 1f, z);
                    int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, ProbeTop + 4f, blocking, QueryTriggerInteraction.Ignore);

                    for (int h = 0; h < count; h++)
                    {
                        RaycastHit hit = hits[h];

                        // Reject steep faces: a wall's side is not a floor.
                        if (hit.normal.y < 0.7f)
                        {
                            continue;
                        }

                        Vector3 foot = hit.point + Vector3.up * 0.05f;

                        // Reject anywhere a body would not physically fit.
                        //
                        // The clearance numbers here are deliberately generous. On a
                        // sloped surface the perpendicular distance from the floor is the
                        // vertical distance times cos(slope), so a capsule sized to just
                        // fit on flat ground clips straight into a ramp and every stair
                        // flight bakes as a wall. The extra lift plus the smaller probe
                        // radius keep ramps up to roughly 45 degrees walkable.
                        const float footLift = 0.14f;
                        const float probeRadius = AgentRadius * 0.85f;

                        if (Physics.CheckCapsule(
                                foot + Vector3.up * (AgentRadius + footLift),
                                foot + Vector3.up * (AgentHeight - AgentRadius),
                                probeRadius,
                                blocking,
                                QueryTriggerInteraction.Ignore))
                        {
                            continue;
                        }

                        nodes.Add(new NavNode { Position = foot, LinkStart = 0, LinkCount = 0, Zone = 0 });
                        nodeZones.Add((int)ClassifyZone(foot));
                    }
                }
            }

            // --- link ------------------------------------------------------------
            var neighbours = new List<List<NavLink>>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++)
            {
                neighbours.Add(new List<NavLink>(8));
            }

            // Bucket by grid cell so linking is not O(n^2).
            var buckets = new Dictionary<long, List<int>>(nodes.Count);
            float cell = Spacing * 1.6f;
            for (int i = 0; i < nodes.Count; i++)
            {
                long key = CellKey(nodes[i].Position, cell);
                if (!buckets.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>(8);
                    buckets[key] = list;
                }

                list.Add(i);
            }

            float maxLinkDistance = Spacing * 1.55f;
            var candidates = new List<int>(32);

            for (int i = 0; i < nodes.Count; i++)
            {
                Vector3 a = nodes[i].Position;
                candidates.Clear();
                GatherNeighbourhood(buckets, a, cell, candidates);

                for (int c = 0; c < candidates.Count; c++)
                {
                    int j = candidates[c];
                    if (j <= i)
                    {
                        continue;
                    }

                    Vector3 b = nodes[j].Position;
                    Vector3 delta = b - a;
                    float planar = new Vector2(delta.x, delta.z).magnitude;

                    if (planar > maxLinkDistance || planar < 0.01f)
                    {
                        continue;
                    }

                    float rise = Mathf.Abs(delta.y);
                    if (rise > MaxStepUp && rise / Mathf.Max(0.01f, planar) > MaxLinkSlope)
                    {
                        continue;
                    }

                    if (!CanTraverse(a, b, blocking))
                    {
                        continue;
                    }

                    float cost = delta.magnitude + rise * 0.6f;
                    int gate = GateForMidpoint((a + b) * 0.5f, gateVolumes);

                    neighbours[i].Add(new NavLink { Target = j, Cost = cost, GateId = gate });
                    neighbours[j].Add(new NavLink { Target = i, Cost = cost, GateId = gate });
                }
            }

            // --- flatten ------------------------------------------------------------
            var flatNodes = new List<NavNode>(nodes.Count);
            var flatLinks = new List<NavLink>(nodes.Count * 6);
            int isolated = 0;
            int gated = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                NavNode node = nodes[i];
                node.Zone = nodeZones[i];
                node.LinkStart = flatLinks.Count;
                node.LinkCount = neighbours[i].Count;

                if (node.LinkCount == 0)
                {
                    isolated++;
                }

                for (int l = 0; l < neighbours[i].Count; l++)
                {
                    if (neighbours[i][l].GateId >= 0)
                    {
                        gated++;
                    }

                    flatLinks.Add(neighbours[i][l]);
                }

                flatNodes.Add(node);
            }

            var gateNames = new string[gateVolumes.Count];
            for (int i = 0; i < gateVolumes.Count; i++)
            {
                gateNames[i] = gateVolumes[i].Name;
            }

            graph.SetBakedData(flatNodes, flatLinks, gateNames, Spacing, bounds);

            return new BakeReport
            {
                NodeCount = flatNodes.Count,
                LinkCount = flatLinks.Count,
                GateCount = gateNames.Length,
                GatedLinkCount = gated,
                IsolatedNodeCount = isolated
            };
        }

        private static void GatherNeighbourhood(Dictionary<long, List<int>> buckets, Vector3 position, float cell, List<int> result)
        {
            int cx = Mathf.FloorToInt(position.x / cell);
            int cz = Mathf.FloorToInt(position.z / cell);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    long key = ((long)(cx + dx + 32768) << 20) | (uint)(cz + dz + 32768);
                    if (buckets.TryGetValue(key, out List<int> list))
                    {
                        result.AddRange(list);
                    }
                }
            }
        }

        private static long CellKey(Vector3 p, float cell)
        {
            int cx = Mathf.FloorToInt(p.x / cell);
            int cz = Mathf.FloorToInt(p.z / cell);
            return ((long)(cx + 32768) << 20) | (uint)(cz + 32768);
        }

        /// <summary>Can a body-sized capsule actually get from a to b?</summary>
        private static bool CanTraverse(Vector3 a, Vector3 b, LayerMask blocking)
        {
            // Lift the sweep to chest height and step the capsule bottom up by the
            // allowed step, so a kerb links but a wall does not.
            Vector3 from = a + Vector3.up * (MaxStepUp + AgentRadius);
            Vector3 to = b + Vector3.up * (MaxStepUp + AgentRadius);
            Vector3 direction = to - from;
            float distance = direction.magnitude;

            if (distance < 0.001f)
            {
                return true;
            }

            direction /= distance;

            Vector3 point1 = from;
            Vector3 point2 = from + Vector3.up * (AgentHeight - MaxStepUp - AgentRadius * 2f);

            if (Physics.CapsuleCast(point1, point2, AgentRadius * 0.85f, direction, out _, distance,
                    blocking, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            // Reject links that fly over a hole: the midpoint must have floor under it.
            Vector3 mid = (a + b) * 0.5f;
            return Physics.Raycast(mid + Vector3.up * 0.6f, Vector3.down, out RaycastHit floor, 1.6f,
                       blocking, QueryTriggerInteraction.Ignore)
                   && Mathf.Abs(floor.point.y - mid.y) < 0.7f;
        }

        private static int GateForMidpoint(Vector3 midpoint, IReadOnlyList<GateVolume> volumes)
        {
            for (int i = 0; i < volumes.Count; i++)
            {
                if (volumes[i].Bounds.Contains(midpoint))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Assigns a node to a named region. Kept as ranges rather than trigger volumes
        /// so the classification cannot be broken by a mis-sized collider.
        /// </summary>
        private static StationZone ClassifyZone(Vector3 p)
        {
            if (p.y > 4.6f)
            {
                return StationZone.Rooftop;
            }

            if (p.y > 2.4f)
            {
                return StationZone.Catwalk;
            }

            if (p.z >= 16.5f)
            {
                return StationZone.LabWing;
            }

            if (p.x >= 17.5f)
            {
                return StationZone.GeneratorRoom;
            }

            return StationZone.Courtyard;
        }
    }
}
