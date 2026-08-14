using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Ashfall.Nav;
using Ashfall.Player;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Explains the baked nav graph: how many disconnected islands it has, which zones
    /// each island covers, and where the boundary between two islands actually is.
    ///
    /// "The roof is unreachable" is a useless bug report. "Island A ends at
    /// (41, 5.5, 16) and island B starts at (41, 5.5, 18)" points straight at the
    /// collider in between.
    /// </summary>
    public static class AshfallNavDiagnostics
    {
        [MenuItem("Ashfall/Diagnose Navigation", priority = 21)]
        public static void DiagnoseMenu()
        {
            Debug.Log(Diagnose());
        }

        public static void DiagnoseFromCommandLine()
        {
            EditorSceneManager.OpenScene(AshfallProjectBuilder.ScenePath, OpenSceneMode.Single);
            Debug.Log(Diagnose());
            EditorApplication.Exit(0);
        }

        private static string Diagnose()
        {
            var nav = Object.FindFirstObjectByType<NavGraph>();
            if (nav == null)
            {
                return "[Ashfall] No NavGraph in the scene.";
            }

            nav.PrimeForQueries();
            for (int i = 0; i < nav.GateCount; i++)
            {
                nav.SetGateOpen(i, true);
            }

            int count = nav.NodeCount;
            var component = new int[count];
            for (int i = 0; i < count; i++)
            {
                component[i] = -1;
            }

            var sizes = new List<int>();
            var frontier = new Stack<int>();
            var path = new List<int>();

            // Flood fill using the graph's own path query so the traversal sees exactly
            // the same links the AI would.
            for (int seed = 0; seed < count; seed++)
            {
                if (component[seed] >= 0)
                {
                    continue;
                }

                int id = sizes.Count;
                int size = 0;
                frontier.Push(seed);
                component[seed] = id;

                while (frontier.Count > 0)
                {
                    int current = frontier.Pop();
                    size++;

                    for (int other = 0; other < count; other++)
                    {
                        if (component[other] >= 0)
                        {
                            continue;
                        }

                        // Cheap spatial reject before the full path query.
                        if ((nav.NodePosition(other) - nav.NodePosition(current)).sqrMagnitude >
                            nav.NodeSpacing * nav.NodeSpacing * 2.5f)
                        {
                            continue;
                        }

                        if (nav.FindPath(current, other, path) && path.Count <= 2)
                        {
                            component[other] = id;
                            frontier.Push(other);
                        }
                    }
                }

                sizes.Add(size);
            }

            var player = Object.FindFirstObjectByType<PlayerRig>();
            int playerNode = player != null ? nav.NearestNode(player.transform.position, 8f) : -1;
            int playerComponent = playerNode >= 0 ? component[playerNode] : -1;

            var sb = new StringBuilder();
            sb.AppendLine("===== Ashfall navigation diagnosis =====");
            sb.AppendLine($"Nodes {count}, links {nav.LinkCount}, gates {nav.GateCount} (all opened for this report).");
            sb.AppendLine($"Islands: {sizes.Count}. Player is on island {playerComponent}.");

            // Describe the biggest islands by their bounding box and zone mix.
            var order = new List<int>();
            for (int i = 0; i < sizes.Count; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) => sizes[b].CompareTo(sizes[a]));

            int described = Mathf.Min(order.Count, 8);
            for (int i = 0; i < described; i++)
            {
                int id = order[i];
                var bounds = new Bounds();
                bool first = true;
                var zones = new Dictionary<StationZone, int>();

                for (int n = 0; n < count; n++)
                {
                    if (component[n] != id)
                    {
                        continue;
                    }

                    Vector3 p = nav.NodePosition(n);
                    if (first)
                    {
                        bounds = new Bounds(p, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(p);
                    }

                    StationZone zone = nav.NodeZone(n);
                    zones.TryGetValue(zone, out int z);
                    zones[zone] = z + 1;
                }

                var zoneText = new StringBuilder();
                foreach (KeyValuePair<StationZone, int> pair in zones)
                {
                    zoneText.Append($"{pair.Key}={pair.Value} ");
                }

                sb.AppendLine(
                    $"  island {id}{(id == playerComponent ? " (player)" : string.Empty)}: " +
                    $"{sizes[id]} nodes, min {Fmt(bounds.min)} max {Fmt(bounds.max)}, {zoneText.ToString().TrimEnd()}");
            }

            if (order.Count > described)
            {
                sb.AppendLine($"  ... and {order.Count - described} smaller island(s) not listed.");
            }

            // Probe points that matter for gameplay.
            (string label, Vector3 point)[] probes =
            {
                ("player spawn", player != null ? player.transform.position : Vector3.zero),
                ("courtyard centre", new Vector3(0f, 0.2f, 0f)),
                ("lab wing", new Vector3(0f, 0.2f, 30f)),
                ("generator floor", new Vector3(31f, 0.2f, -4f)),
                ("generator catwalk", new Vector3(31f, 3.7f, 10f)),
                ("roof stair foot", new Vector3(40.5f, 3.7f, 9.5f)),
                ("roof bridge", new Vector3(40.5f, 5.6f, 15f)),
                ("roof deck east", new Vector3(39f, 5.6f, 21.5f)),
                ("roof deck west", new Vector3(-1f, 5.6f, 21.5f))
            };

            sb.AppendLine("Probes:");
            for (int i = 0; i < probes.Length; i++)
            {
                int node = nav.NearestNode(probes[i].point, 6f);
                if (node < 0)
                {
                    sb.AppendLine($"  {probes[i].label,-20} {Fmt(probes[i].point)} -> NO NODE WITHIN 6m");
                    continue;
                }

                sb.AppendLine(
                    $"  {probes[i].label,-20} {Fmt(probes[i].point)} -> node {node} at {Fmt(nav.NodePosition(node))}, " +
                    $"island {component[node]}, zone {nav.NodeZone(node)}");
            }

            nav.CloseAllGates();
            sb.AppendLine("========================================");
            return sb.ToString();
        }

        private static string Fmt(Vector3 v) => $"({v.x:0.0},{v.y:0.0},{v.z:0.0})";
    }
}
