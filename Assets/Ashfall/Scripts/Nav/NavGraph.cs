using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Nav
{
    /// <summary>Named regions of the station. Drives spawn selection and phase gating.</summary>
    public enum StationZone
    {
        Courtyard = 0,
        LabWing = 1,
        GeneratorRoom = 2,
        Rooftop = 3,
        Catwalk = 4
    }

    [Serializable]
    public struct NavNode
    {
        public Vector3 Position;
        public int LinkStart;
        public int LinkCount;
        public int Zone;
    }

    [Serializable]
    public struct NavLink
    {
        public int Target;
        public float Cost;

        /// <summary>-1 for an always-open link, otherwise an index into the gate array.</summary>
        public int GateId;
    }

    /// <summary>
    /// A baked waypoint graph with gated links.
    ///
    /// This replaces NavMesh for the slice on purpose. The AI Navigation package is not
    /// available offline for this editor install, and a hand-rolled graph buys something
    /// NavMesh would have made awkward anyway: routes that are *physically* closed until
    /// the player buys a door or the station changes phase. Closing a gate re-routes every
    /// enemy on the next path request, so the map evolving is something the AI feels.
    ///
    /// Nodes and links are baked by the editor scene builder and serialised straight into
    /// the scene, so there is nothing to bake at runtime and nothing to wire by hand.
    /// </summary>
    [DisallowMultipleComponent]
    public class NavGraph : MonoBehaviour
    {
        [SerializeField] private List<NavNode> nodes = new();
        [SerializeField] private List<NavLink> links = new();
        [SerializeField] private float nodeSpacing = 2.0f;
        [SerializeField] private Vector3 boundsMin;
        [SerializeField] private Vector3 boundsMax;

        /// <summary>Gate 0 is reserved as "always open" so an unset gate never blocks.</summary>
        [SerializeField] private bool[] gateOpen = Array.Empty<bool>();

        [SerializeField] private string[] gateNames = Array.Empty<string>();

        private Dictionary<long, List<int>> _spatial;
        private float _cellSize;

        // Reusable A* scratch. Sized to the node count on first use; enemies path on
        // staggered timers so a single shared buffer set is enough and allocates nothing
        // per request.
        private float[] _gScore;
        private float[] _fScore;
        private int[] _cameFrom;
        private int[] _openHeap;
        private int[] _heapIndex;
        private bool[] _closed;
        private int _heapCount;
        private int _searchStamp;
        private int[] _visitStamp;

        public int NodeCount => nodes.Count;
        public int LinkCount => links.Count;
        public int GateCount => gateOpen.Length;
        public float NodeSpacing => nodeSpacing;
        public IReadOnlyList<NavNode> Nodes => nodes;

        public static NavGraph Active { get; private set; }

        private void Awake()
        {
            PrimeForQueries();
        }

        /// <summary>
        /// Makes the graph queryable and registers it as the active one.
        ///
        /// Normally Awake does this. Editor tooling and tests need the same setup
        /// without entering play mode, and calling Awake by hand through SendMessage
        /// trips Unity's own "should this behaviour run" assertion -- so it is a real
        /// method instead.
        /// </summary>
        public void PrimeForQueries()
        {
            Active = this;
            EnsureScratch();
            BuildSpatialIndex();
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        // ------------------------------------------------------------------
        // Bake-time API (called by the editor scene builder)
        // ------------------------------------------------------------------

        public void SetBakedData(List<NavNode> bakedNodes, List<NavLink> bakedLinks, string[] gates, float spacing, Bounds bounds)
        {
            nodes = bakedNodes;
            links = bakedLinks;
            gateNames = gates ?? Array.Empty<string>();
            gateOpen = new bool[gateNames.Length];
            for (int i = 0; i < gateOpen.Length; i++)
            {
                gateOpen[i] = false;
            }

            nodeSpacing = spacing;
            boundsMin = bounds.min;
            boundsMax = bounds.max;
            _spatial = null;
            _gScore = null;
        }

        // ------------------------------------------------------------------
        // Gates
        // ------------------------------------------------------------------

        public int GateIdByName(string gateName)
        {
            for (int i = 0; i < gateNames.Length; i++)
            {
                if (string.Equals(gateNames[i], gateName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        public string GateName(int gateId)
        {
            return gateId >= 0 && gateId < gateNames.Length ? gateNames[gateId] : "<none>";
        }

        public bool IsGateOpen(int gateId)
        {
            if (gateId < 0)
            {
                return true;
            }

            return gateId < gateOpen.Length && gateOpen[gateId];
        }

        public void SetGateOpen(string gateName, bool open)
        {
            int id = GateIdByName(gateName);
            if (id >= 0)
            {
                SetGateOpen(id, open);
            }
        }

        public void SetGateOpen(int gateId, bool open)
        {
            if (gateId >= 0 && gateId < gateOpen.Length)
            {
                gateOpen[gateId] = open;
            }
        }

        public void CloseAllGates()
        {
            for (int i = 0; i < gateOpen.Length; i++)
            {
                gateOpen[i] = false;
            }
        }

        // ------------------------------------------------------------------
        // Queries
        // ------------------------------------------------------------------

        private void EnsureScratch()
        {
            int n = nodes.Count;
            if (_gScore != null && _gScore.Length >= n && n > 0)
            {
                return;
            }

            if (n == 0)
            {
                return;
            }

            _gScore = new float[n];
            _fScore = new float[n];
            _cameFrom = new int[n];
            _openHeap = new int[n + 1];
            _heapIndex = new int[n];
            _closed = new bool[n];
            _visitStamp = new int[n];
        }

        private void BuildSpatialIndex()
        {
            _cellSize = Mathf.Max(1f, nodeSpacing * 2f);
            _spatial = new Dictionary<long, List<int>>(Mathf.Max(16, nodes.Count / 2));
            for (int i = 0; i < nodes.Count; i++)
            {
                long key = CellKey(nodes[i].Position);
                if (!_spatial.TryGetValue(key, out var bucket))
                {
                    bucket = new List<int>(8);
                    _spatial[key] = bucket;
                }

                bucket.Add(i);
            }
        }

        private long CellKey(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / _cellSize);
            int cz = Mathf.FloorToInt(p.z / _cellSize);
            return ((long)(cx + 32768) << 20) | (uint)(cz + 32768);
        }

        /// <summary>Nearest baked node to a world position, or -1 when the graph is empty.</summary>
        public int NearestNode(Vector3 position, float maxDistance = 40f)
        {
            if (nodes.Count == 0)
            {
                return -1;
            }

            if (_spatial == null)
            {
                BuildSpatialIndex();
            }

            int best = -1;
            float bestSqr = maxDistance * maxDistance;

            int cx = Mathf.FloorToInt(position.x / _cellSize);
            int cz = Mathf.FloorToInt(position.z / _cellSize);
            int ring = 0;
            int maxRing = Mathf.CeilToInt(maxDistance / _cellSize) + 1;

            while (ring <= maxRing)
            {
                for (int dx = -ring; dx <= ring; dx++)
                {
                    for (int dz = -ring; dz <= ring; dz++)
                    {
                        // Only walk the perimeter of each ring; inner cells were covered already.
                        if (ring > 0 && Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring)
                        {
                            continue;
                        }

                        long key = ((long)(cx + dx + 32768) << 20) | (uint)(cz + dz + 32768);
                        if (!_spatial.TryGetValue(key, out var bucket))
                        {
                            continue;
                        }

                        for (int b = 0; b < bucket.Count; b++)
                        {
                            int idx = bucket[b];
                            float sqr = (nodes[idx].Position - position).sqrMagnitude;
                            if (sqr < bestSqr)
                            {
                                bestSqr = sqr;
                                best = idx;
                            }
                        }
                    }
                }

                // One extra ring past the first hit guards against a diagonal neighbour
                // in the next cell actually being closer.
                if (best >= 0 && ring > 0)
                {
                    break;
                }

                ring++;
            }

            return best;
        }

        public Vector3 NodePosition(int index)
        {
            return index >= 0 && index < nodes.Count ? nodes[index].Position : Vector3.zero;
        }

        public StationZone NodeZone(int index)
        {
            return index >= 0 && index < nodes.Count ? (StationZone)nodes[index].Zone : StationZone.Courtyard;
        }

        /// <summary>
        /// A* from <paramref name="startNode"/> to <paramref name="goalNode"/>, honouring
        /// gates. Results are appended to <paramref name="result"/> start-to-goal.
        /// Returns false when no open route exists.
        /// </summary>
        public bool FindPath(int startNode, int goalNode, List<int> result)
        {
            result.Clear();

            if (nodes.Count == 0 || startNode < 0 || goalNode < 0
                || startNode >= nodes.Count || goalNode >= nodes.Count)
            {
                return false;
            }

            EnsureScratch();

            if (startNode == goalNode)
            {
                result.Add(startNode);
                return true;
            }

            _searchStamp++;
            _heapCount = 0;

            Vector3 goalPos = nodes[goalNode].Position;

            TouchNode(startNode);
            _gScore[startNode] = 0f;
            _fScore[startNode] = Heuristic(nodes[startNode].Position, goalPos);
            _cameFrom[startNode] = -1;
            HeapPush(startNode);

            while (_heapCount > 0)
            {
                int current = HeapPop();
                if (current == goalNode)
                {
                    ReconstructPath(current, result);
                    return true;
                }

                _closed[current] = true;

                NavNode node = nodes[current];
                int end = node.LinkStart + node.LinkCount;
                for (int li = node.LinkStart; li < end; li++)
                {
                    NavLink link = links[li];
                    if (!IsGateOpen(link.GateId))
                    {
                        continue;
                    }

                    int next = link.Target;
                    TouchNode(next);
                    if (_closed[next])
                    {
                        continue;
                    }

                    float tentative = _gScore[current] + link.Cost;
                    if (tentative >= _gScore[next])
                    {
                        continue;
                    }

                    _cameFrom[next] = current;
                    _gScore[next] = tentative;
                    _fScore[next] = tentative + Heuristic(nodes[next].Position, goalPos);

                    if (_heapIndex[next] > 0)
                    {
                        HeapSiftUp(_heapIndex[next]);
                    }
                    else
                    {
                        HeapPush(next);
                    }
                }
            }

            return false;
        }

        /// <summary>Convenience overload that snaps world positions onto the graph.</summary>
        public bool FindPath(Vector3 from, Vector3 to, List<int> result)
        {
            return FindPath(NearestNode(from), NearestNode(to), result);
        }

        /// <summary>True when a gated route currently exists between two positions.</summary>
        public bool IsReachable(Vector3 from, Vector3 to, List<int> scratch = null)
        {
            scratch ??= new List<int>();
            return FindPath(from, to, scratch);
        }

        private void TouchNode(int index)
        {
            if (_visitStamp[index] == _searchStamp)
            {
                return;
            }

            _visitStamp[index] = _searchStamp;
            _gScore[index] = float.PositiveInfinity;
            _fScore[index] = float.PositiveInfinity;
            _cameFrom[index] = -1;
            _closed[index] = false;
            _heapIndex[index] = 0;
        }

        private static float Heuristic(Vector3 a, Vector3 b)
        {
            // Flat-plane distance with a small vertical penalty: the station is mostly
            // horizontal, and this keeps stairs from looking artificially cheap.
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            float dy = (a.y - b.y) * 1.4f;
            return Mathf.Sqrt(dx * dx + dz * dz + dy * dy);
        }

        private void ReconstructPath(int goal, List<int> result)
        {
            int cursor = goal;
            int guard = 0;
            while (cursor >= 0 && guard++ <= nodes.Count)
            {
                result.Add(cursor);
                cursor = _cameFrom[cursor];
            }

            result.Reverse();
        }

        // --- Binary min-heap keyed on _fScore. _heapIndex is 1-based; 0 means "not queued".

        private void HeapPush(int node)
        {
            _heapCount++;
            _openHeap[_heapCount] = node;
            _heapIndex[node] = _heapCount;
            HeapSiftUp(_heapCount);
        }

        private int HeapPop()
        {
            int top = _openHeap[1];
            _heapIndex[top] = 0;

            int last = _openHeap[_heapCount];
            _heapCount--;

            if (_heapCount > 0)
            {
                _openHeap[1] = last;
                _heapIndex[last] = 1;
                HeapSiftDown(1);
            }

            return top;
        }

        private void HeapSiftUp(int at)
        {
            int node = _openHeap[at];
            float score = _fScore[node];

            while (at > 1)
            {
                int parent = at >> 1;
                int parentNode = _openHeap[parent];
                if (_fScore[parentNode] <= score)
                {
                    break;
                }

                _openHeap[at] = parentNode;
                _heapIndex[parentNode] = at;
                at = parent;
            }

            _openHeap[at] = node;
            _heapIndex[node] = at;
        }

        private void HeapSiftDown(int at)
        {
            int node = _openHeap[at];
            float score = _fScore[node];

            while (true)
            {
                int child = at << 1;
                if (child > _heapCount)
                {
                    break;
                }

                if (child + 1 <= _heapCount && _fScore[_openHeap[child + 1]] < _fScore[_openHeap[child]])
                {
                    child++;
                }

                if (_fScore[_openHeap[child]] >= score)
                {
                    break;
                }

                _openHeap[at] = _openHeap[child];
                _heapIndex[_openHeap[at]] = at;
                at = child;
            }

            _openHeap[at] = node;
            _heapIndex[node] = at;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (nodes == null || nodes.Count == 0)
            {
                return;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                NavNode n = nodes[i];
                Gizmos.color = new Color(0.24f, 0.88f, 0.86f, 0.5f);
                Gizmos.DrawWireCube(n.Position + Vector3.up * 0.05f, new Vector3(0.25f, 0.05f, 0.25f));

                int end = n.LinkStart + n.LinkCount;
                for (int li = n.LinkStart; li < end && li < links.Count; li++)
                {
                    NavLink link = links[li];
                    if (link.Target <= i)
                    {
                        continue;
                    }

                    Gizmos.color = link.GateId < 0
                        ? new Color(0.24f, 0.88f, 0.86f, 0.22f)
                        : new Color(1f, 0.63f, 0.2f, 0.65f);
                    Gizmos.DrawLine(n.Position + Vector3.up * 0.05f, nodes[link.Target].Position + Vector3.up * 0.05f);
                }
            }

            Gizmos.color = new Color(1f, 1f, 1f, 0.12f);
            Gizmos.DrawWireCube((boundsMin + boundsMax) * 0.5f, boundsMax - boundsMin);
        }
#endif
    }
}
