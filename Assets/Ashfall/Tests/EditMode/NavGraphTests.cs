using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Ashfall.Nav;

namespace Ashfall.Tests
{
    /// <summary>
    /// Pathfinding and gating, on a hand-built graph small enough to reason about.
    ///
    /// The layout below is two 3x3 rooms joined by a single corridor node, and that
    /// corridor's links are gated. It is the smallest thing that reproduces the shape of
    /// the real station: buying a door has to be the only way through.
    ///
    ///   room A            gate           room B
    ///   0 1 2                             9 10 11
    ///   3 4 5  --  6  ==[gate 0]==  7  -- 12 13 14
    /// </summary>
    public class NavGraphTests
    {
        private GameObject _host;
        private NavGraph _graph;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
        }

        /// <summary>Builds a straight chain of nodes 1m apart, gating one chosen link.</summary>
        private NavGraph BuildChain(int count, int gatedLinkIndex)
        {
            _host = new GameObject("NavGraph Test");
            _graph = _host.AddComponent<NavGraph>();

            var nodes = new List<NavNode>(count);
            var links = new List<NavLink>(count * 2);

            // Build adjacency first so LinkStart offsets can be computed.
            var adjacency = new List<List<NavLink>>(count);
            for (int i = 0; i < count; i++)
            {
                adjacency.Add(new List<NavLink>(2));
            }

            for (int i = 0; i < count - 1; i++)
            {
                int gate = i == gatedLinkIndex ? 0 : -1;
                adjacency[i].Add(new NavLink { Target = i + 1, Cost = 1f, GateId = gate });
                adjacency[i + 1].Add(new NavLink { Target = i, Cost = 1f, GateId = gate });
            }

            for (int i = 0; i < count; i++)
            {
                nodes.Add(new NavNode
                {
                    Position = new Vector3(i, 0f, 0f),
                    LinkStart = links.Count,
                    LinkCount = adjacency[i].Count,
                    Zone = 0
                });

                links.AddRange(adjacency[i]);
            }

            string[] gates = gatedLinkIndex >= 0 ? new[] { "TestGate" } : new string[0];
            _graph.SetBakedData(nodes, links, gates, 1f, new Bounds(new Vector3(count * 0.5f, 0f, 0f), new Vector3(count + 2, 4f, 4f)));
            _graph.PrimeForQueries();
            return _graph;
        }

        // ------------------------------------------------------------------

        [Test]
        public void FindsAStraightPathThroughAnUngatedChain()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: -1);
            var path = new List<int>();

            Assert.IsTrue(graph.FindPath(0, 5, path));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, path);
        }

        [Test]
        public void PathToSelfIsASingleNode()
        {
            NavGraph graph = BuildChain(4, gatedLinkIndex: -1);
            var path = new List<int>();

            Assert.IsTrue(graph.FindPath(2, 2, path));
            CollectionAssert.AreEqual(new[] { 2 }, path);
        }

        [Test]
        public void ClosedGateBlocksTheOnlyRoute()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: 2);
            var path = new List<int>();

            Assert.AreEqual(1, graph.GateCount);
            Assert.IsFalse(graph.IsGateOpen(0), "Gates must start closed.");
            Assert.IsFalse(graph.FindPath(0, 5, path), "A closed gate must sever the chain.");
            Assert.IsEmpty(path);
        }

        [Test]
        public void OpeningTheGateRestoresTheRoute()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: 2);
            var path = new List<int>();

            Assert.IsFalse(graph.FindPath(0, 5, path));

            graph.SetGateOpen(0, true);
            Assert.IsTrue(graph.IsGateOpen(0));
            Assert.IsTrue(graph.FindPath(0, 5, path));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, path);
        }

        [Test]
        public void GatesCanBeAddressedByName()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: 2);

            Assert.AreEqual(0, graph.GateIdByName("TestGate"));
            Assert.AreEqual(0, graph.GateIdByName("testgate"), "Gate lookup should be case-insensitive.");
            Assert.AreEqual(-1, graph.GateIdByName("NoSuchGate"));

            graph.SetGateOpen("TestGate", true);
            Assert.IsTrue(graph.IsGateOpen(0));

            graph.CloseAllGates();
            Assert.IsFalse(graph.IsGateOpen(0));
        }

        [Test]
        public void UngatedLinksAreAlwaysPassable()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: 2);
            var path = new List<int>();

            // Either side of the gate still connects internally.
            Assert.IsTrue(graph.FindPath(0, 2, path));
            Assert.IsTrue(graph.FindPath(3, 5, path));
        }

        [Test]
        public void NearestNodeSnapsToTheClosestPoint()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: -1);

            Assert.AreEqual(0, graph.NearestNode(new Vector3(-0.4f, 0f, 0f)));
            Assert.AreEqual(3, graph.NearestNode(new Vector3(3.1f, 0f, 0.2f)));
            Assert.AreEqual(5, graph.NearestNode(new Vector3(5.4f, 0f, 0f)));
        }

        [Test]
        public void NearestNodeReturnsMinusOneWhenNothingIsInRange()
        {
            NavGraph graph = BuildChain(6, gatedLinkIndex: -1);
            Assert.AreEqual(-1, graph.NearestNode(new Vector3(0f, 0f, 500f), maxDistance: 5f));
        }

        [Test]
        public void InvalidNodeIndicesAreRejectedRatherThanThrowing()
        {
            NavGraph graph = BuildChain(4, gatedLinkIndex: -1);
            var path = new List<int>();

            Assert.IsFalse(graph.FindPath(-1, 2, path));
            Assert.IsFalse(graph.FindPath(0, 99, path));
            Assert.IsFalse(graph.FindPath(99, 99, path));
        }

        [Test]
        public void RepeatedQueriesDoNotCorruptTheSearchState()
        {
            NavGraph graph = BuildChain(8, gatedLinkIndex: -1);
            var path = new List<int>();

            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(graph.FindPath(0, 7, path));
                Assert.AreEqual(8, path.Count, $"Path degraded on iteration {i}.");
                Assert.IsTrue(graph.FindPath(7, 0, path));
                Assert.AreEqual(8, path.Count);
            }
        }

        [Test]
        public void AStarPicksTheCheaperOfTwoRoutes()
        {
            _host = new GameObject("NavGraph Diamond");
            _graph = _host.AddComponent<NavGraph>();

            // 0 -> 1 -> 3 costs 2; 0 -> 2 -> 3 costs 10. Both are valid; A* must take
            // the cheap one even though the expensive one is discovered first.
            var adjacency = new List<List<NavLink>>
            {
                new() { new NavLink { Target = 2, Cost = 5f, GateId = -1 }, new NavLink { Target = 1, Cost = 1f, GateId = -1 } },
                new() { new NavLink { Target = 0, Cost = 1f, GateId = -1 }, new NavLink { Target = 3, Cost = 1f, GateId = -1 } },
                new() { new NavLink { Target = 0, Cost = 5f, GateId = -1 }, new NavLink { Target = 3, Cost = 5f, GateId = -1 } },
                new() { new NavLink { Target = 1, Cost = 1f, GateId = -1 }, new NavLink { Target = 2, Cost = 5f, GateId = -1 } }
            };

            var positions = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 1f),
                new Vector3(1f, 0f, -1f),
                new Vector3(2f, 0f, 0f)
            };

            var nodes = new List<NavNode>();
            var links = new List<NavLink>();
            for (int i = 0; i < 4; i++)
            {
                nodes.Add(new NavNode
                {
                    Position = positions[i],
                    LinkStart = links.Count,
                    LinkCount = adjacency[i].Count,
                    Zone = 0
                });
                links.AddRange(adjacency[i]);
            }

            _graph.SetBakedData(nodes, links, new string[0], 1f, new Bounds(Vector3.zero, Vector3.one * 8f));
            _graph.PrimeForQueries();

            var path = new List<int>();
            Assert.IsTrue(_graph.FindPath(0, 3, path));
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, path, "A* should take the cheap route.");
        }

        [Test]
        public void EmptyGraphIsHandledGracefully()
        {
            _host = new GameObject("NavGraph Empty");
            _graph = _host.AddComponent<NavGraph>();
            _graph.SetBakedData(new List<NavNode>(), new List<NavLink>(), new string[0], 1f, new Bounds());
            _graph.PrimeForQueries();

            var path = new List<int>();
            Assert.AreEqual(0, _graph.NodeCount);
            Assert.AreEqual(-1, _graph.NearestNode(Vector3.zero));
            Assert.IsFalse(_graph.FindPath(Vector3.zero, Vector3.one, path));
        }
    }
}
