using System;
using System.Collections.Generic;
using Godot;

namespace PCGTogether.lvls.gen;

/// Builds a traversal graph over a Voronoi diagram by randomly selecting edges (weighted by length)
/// until the graph is connected and a neighbour coverage ratio is reached.
public static class VoronoiTraversal {
    public static VoronoiTraversalGraph Build(VoronoiDiagram diagram, float neighborRatio = 0.75f, int rngSeed = 12345,
                                              bool includeBorderEdges = true) {
        neighborRatio = Mathf.Clamp(neighborRatio, 0f, 1f);
        var rng = new RandomNumberGenerator { Seed = (ulong)rngSeed };

        var neighborPairs = CollectNeighborPairs(diagram);
        var targetConnections = Math.Max(diagram.Seeds.Count - 1, Mathf.CeilToInt(neighborPairs.Count * neighborRatio));

        var candidateEdges = new List<int>();
        var candidateWeights = new List<float>();
        for (var i = 0; i < diagram.Edges.Count; i++) {
            var edge = diagram.Edges[i];
            if (!includeBorderEdges && edge.IsBorder) {
                continue;
            }

            var length = edge.From.DistanceTo(edge.To);
            if (length <= 0f) {
                continue;
            }

            candidateEdges.Add(i);
            candidateWeights.Add(length);
        }

        var connections = new List<VoronoiConnection>();
        var connectedPairs = new HashSet<PairKey>();
        var unionFind = new UnionFind(diagram.Seeds.Count);

        var cumulative = BuildCumulative(candidateWeights);

        int iterations = 0;
        var maxIterations = Math.Max(1, candidateEdges.Count * 10);

        while ((unionFind.ComponentCount > 1 || connections.Count < targetConnections) &&
               candidateEdges.Count > 0 && iterations < maxIterations) {
            iterations++;

            var pick = rng.Randf() * cumulative[cumulative.Count - 1];
            var idx = BinarySearchCumulative(cumulative, pick);
            var edgeIndex = candidateEdges[idx];
            var edge = diagram.Edges[edgeIndex];
            var pair = new PairKey(edge.SeedA, edge.SeedB);

            if (connectedPairs.Contains(pair)) {
                RemoveCandidate(candidateEdges, candidateWeights, idx, out cumulative);
                continue;
            }

            var t = rng.Randf();
            var smooth = 3f * t * t - 2f * t * t * t; // cubic smoothstep
            var point = edge.From.Lerp(edge.To, smooth);

            connections.Add(new VoronoiConnection(edge.SeedA, edge.SeedB, edgeIndex, point,
                edge.From.DistanceTo(edge.To)));
            connectedPairs.Add(pair);
            unionFind.Union(edge.SeedA, edge.SeedB);

            RemoveCandidate(candidateEdges, candidateWeights, idx, out cumulative);
        }

        return new VoronoiTraversalGraph(diagram, neighborPairs.Count, targetConnections, connections, connectedPairs);
    }

    private static HashSet<PairKey> CollectNeighborPairs(VoronoiDiagram diagram) {
        var pairs = new HashSet<PairKey>();
        for (var i = 0; i < diagram.Cells.Count; i++) {
            foreach (var neighbor in diagram.Cells[i].Neighbors) {
                pairs.Add(new PairKey(i, neighbor));
            }
        }

        return pairs;
    }

    private static List<float> BuildCumulative(List<float> weights) {
        var cumulative = new List<float>(weights.Count);
        float total = 0f;
        foreach (var w in weights) {
            total += w;
            cumulative.Add(total);
        }

        return cumulative;
    }

    private static int BinarySearchCumulative(List<float> cumulative, float value) {
        var low = 0;
        var high = cumulative.Count - 1;
        while (low < high) {
            var mid = (low + high) / 2;
            if (value <= cumulative[mid]) {
                high = mid;
            }
            else {
                low = mid + 1;
            }
        }

        return low;
    }

    private static void RemoveCandidate(List<int> edges, List<float> weights, int index, out List<float> cumulative) {
        edges.RemoveAt(index);
        weights.RemoveAt(index);
        cumulative = BuildCumulative(weights);
    }
}

/// Traversal graph built from Voronoi connectivity.
public class VoronoiTraversalGraph {
    public VoronoiDiagram Diagram { get; }
    public int TotalNeighborPairs { get; }
    public int TargetConnections { get; }
    public IReadOnlyList<VoronoiConnection> Connections => _connections;
    public IReadOnlyCollection<PairKey> ConnectedPairs => _connectedPairs;

    private readonly List<VoronoiConnection> _connections;
    private readonly HashSet<PairKey> _connectedPairs;

    public VoronoiTraversalGraph(VoronoiDiagram diagram, int totalNeighborPairs, int targetConnections,
                                 List<VoronoiConnection> connections, HashSet<PairKey> connectedPairs) {
        Diagram = diagram;
        TotalNeighborPairs = totalNeighborPairs;
        TargetConnections = targetConnections;
        _connections = connections;
        _connectedPairs = connectedPairs;
    }
}

/// A single connection along a Voronoi edge, including the sampled point.
public class VoronoiConnection {
    public int CellA { get; }
    public int CellB { get; }
    public int EdgeIndex { get; }
    public Vector2 PointOnEdge { get; }
    public float EdgeLength { get; }

    public VoronoiConnection(int cellA, int cellB, int edgeIndex, Vector2 pointOnEdge, float edgeLength) {
        CellA = cellA;
        CellB = cellB;
        EdgeIndex = edgeIndex;
        PointOnEdge = pointOnEdge;
        EdgeLength = edgeLength;
    }
}

public readonly struct PairKey : IEquatable<PairKey> {
    public readonly int A;
    public readonly int B;

    public PairKey(int a, int b) {
        if (a < b) {
            A = a;
            B = b;
        }
        else {
            A = b;
            B = a;
        }
    }

    public bool Equals(PairKey other) {
        return A == other.A && B == other.B;
    }

    public override bool Equals(object obj) {
        return obj is PairKey other && Equals(other);
    }

    public override int GetHashCode() {
        return HashCode.Combine(A, B);
    }
}

internal sealed class UnionFind {
    private readonly int[] _parent;
    private readonly int[] _rank;
    public int ComponentCount { get; private set; }

    public UnionFind(int size) {
        _parent = new int[size];
        _rank = new int[size];
        ComponentCount = size;
        for (var i = 0; i < size; i++) {
            _parent[i] = i;
            _rank[i] = 0;
        }
    }

    public int Find(int x) {
        if (_parent[x] != x) {
            _parent[x] = Find(_parent[x]);
        }

        return _parent[x];
    }

    public bool Union(int a, int b) {
        var rootA = Find(a);
        var rootB = Find(b);
        if (rootA == rootB) {
            return false;
        }

        if (_rank[rootA] < _rank[rootB]) {
            _parent[rootA] = rootB;
        }
        else if (_rank[rootA] > _rank[rootB]) {
            _parent[rootB] = rootA;
        }
        else {
            _parent[rootB] = rootA;
            _rank[rootA]++;
        }

        ComponentCount--;
        return true;
    }
}