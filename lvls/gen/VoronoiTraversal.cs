using System;
using System.Collections.Generic;
using Godot;

namespace PCGTogether.lvls.gen;

/// Builds a traversal graph over a Voronoi diagram by first creating a biased spanning tree (Kruskal-style),
/// then sampling additional edges (weighted by length) until a neighbour coverage ratio is reached.
public static class VoronoiTraversal {
    /// Constructs a traversal graph from a Voronoi diagram.
    /// <param name="diagram">Voronoi diagram to traverse.</param>
    /// <param name="neighborRatio">Desired neighbour coverage ratio (0..1).</param>
    /// <param name="rngSeed">Seed for deterministic sampling.</param>
    /// <param name="includeBorderEdges">If false, skips edges on the border.</param>
    /// <param name="connectionDistributionScaling">Bias for connector placement along edges.</param>
    /// <returns>Built traversal graph.</returns>
    public static VoronoiTraversalGraph Build(VoronoiDiagram diagram, float neighborRatio = 0.75f, int rngSeed = 12345,
                                              bool includeBorderEdges = true, float connectionDistributionScaling = 1f) {
        neighborRatio = Mathf.Clamp(neighborRatio, 0f, 1f);
        connectionDistributionScaling = Mathf.Clamp(connectionDistributionScaling, 0f, 1f);
        var rng = new DeterministicRng(rngSeed);

        var neighborPairs = CollectNeighborPairs(diagram);
        var candidates = new List<EdgeCandidate>();
        for (var i = 0; i < diagram.Edges.Count; i++) {
            var edge = diagram.Edges[i];
            if (!includeBorderEdges && edge.IsBorder) {
                continue;
            }

            var length = edge.From.DistanceTo(edge.To);
            if (length <= 0f) {
                continue;
            }

            // Use length as weight; Kruskal phase sorts descending to favor longer edges.
            candidates.Add(new EdgeCandidate(i, length));
        }

        var connections = new List<VoronoiConnection>();
        var connectedPairs = new HashSet<PairKey>();
        var unionFind = new UnionFind(diagram.Seeds.Count);

        // Phase 1: spanning tree with Kruskal (biased toward longer edges).
        candidates.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        foreach (var cand in candidates) {
            if (unionFind.ComponentCount == 1) {
                break;
            }

            var edge = diagram.Edges[cand.EdgeIndex];
            var pair = new PairKey(edge.SeedA, edge.SeedB);
            if (connectedPairs.Contains(pair)) {
                continue;
            }

            if (!unionFind.Union(edge.SeedA, edge.SeedB)) {
                continue;
            }

            var point = SamplePointOnEdge(edge, ref rng, connectionDistributionScaling);
            connections.Add(new VoronoiConnection(edge.SeedA, edge.SeedB, cand.EdgeIndex, point, cand.Weight));
            connectedPairs.Add(pair);
        }

        // Phase 2: add edges until neighbour coverage ratio reached (still biasing longer edges).
        var coverageTarget = Mathf.CeilToInt(neighborPairs.Count * neighborRatio);
        var targetConnections = Math.Max(connections.Count, coverageTarget);

        var remainingEdges = new List<int>();
        var remainingWeights = new List<float>();
        foreach (var cand in candidates) {
            var edge = diagram.Edges[cand.EdgeIndex];
            var pair = new PairKey(edge.SeedA, edge.SeedB);
            if (connectedPairs.Contains(pair)) {
                continue;
            }

            remainingEdges.Add(cand.EdgeIndex);
            remainingWeights.Add(cand.Weight);
        }

        var cumulative = BuildCumulative(remainingWeights);
        var attempts = 0;
        var maxAttempts = Math.Max(1, remainingEdges.Count * 5);

        while (connections.Count < targetConnections && remainingEdges.Count > 0 && attempts < maxAttempts) {
            attempts++;
            var pick = rng.NextFloat() * cumulative[cumulative.Count - 1];
            var idx = BinarySearchCumulative(cumulative, pick);
            var edgeIndex = remainingEdges[idx];
            var edge = diagram.Edges[edgeIndex];
            var pair = new PairKey(edge.SeedA, edge.SeedB);

            if (connectedPairs.Contains(pair)) {
                RemoveCandidate(remainingEdges, remainingWeights, idx, out cumulative);
                continue;
            }

            var point = SamplePointOnEdge(edge, ref rng, connectionDistributionScaling);
            connections.Add(new VoronoiConnection(edge.SeedA, edge.SeedB, edgeIndex, point, edge.From.DistanceTo(edge.To)));
            connectedPairs.Add(pair);

            RemoveCandidate(remainingEdges, remainingWeights, idx, out cumulative);
        }

        return new VoronoiTraversalGraph(diagram, neighborPairs.Count, targetConnections, connections, connectedPairs);
    }

    /// Collects all unique neighbour pairs in the diagram.
    /// <param name="diagram">Source diagram.</param>
    /// <returns>Set of neighbour pairs.</returns>
    private static HashSet<PairKey> CollectNeighborPairs(VoronoiDiagram diagram) {
        var pairs = new HashSet<PairKey>();
        for (var i = 0; i < diagram.Cells.Count; i++) {
            foreach (var neighbor in diagram.Cells[i].Neighbors) {
                pairs.Add(new PairKey(i, neighbor));
            }
        }

        return pairs;
    }

    /// Builds a cumulative distribution from weights.
    /// <param name="weights">Weights to accumulate.</param>
    /// <returns>Cumulative sum list.</returns>
    private static List<float> BuildCumulative(List<float> weights) {
        var cumulative = new List<float>(weights.Count);
        float total = 0f;
        foreach (var w in weights) {
            total += w;
            cumulative.Add(total);
        }

        return cumulative;
    }

    /// Binary searches a cumulative array for a value.
    /// <param name="cumulative">Cumulative list.</param>
    /// <param name="value">Value to locate.</param>
    /// <returns>Index of selected item.</returns>
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

    /// Removes a candidate edge and rebuilds cumulative weights.
    /// <param name="edges">Edge list.</param>
    /// <param name="weights">Weight list.</param>
    /// <param name="index">Index to remove.</param>
    /// <param name="cumulative">Rebuilt cumulative weights.</param>
    private static void RemoveCandidate(List<int> edges, List<float> weights, int index, out List<float> cumulative) {
        edges.RemoveAt(index);
        weights.RemoveAt(index);
        cumulative = BuildCumulative(weights);
    }

    /// Samples a point along an edge using a smooth distribution.
    /// <param name="edge">Edge to sample.</param>
    /// <param name="rng">Deterministic RNG.</param>
    /// <param name="distributionScale">Bias factor around midpoint.</param>
    /// <returns>Point on the edge.</returns>
    private static Vector2 SamplePointOnEdge(VoronoiEdge edge, ref DeterministicRng rng, float distributionScale) {
        var t = rng.NextFloat();
        var smooth = 3f * t * t - 2f * t * t * t; // cubic smoothstep
        var scaled = ((smooth - 0.5f) * distributionScale) + 0.5f;
        return edge.From.Lerp(edge.To, scaled);
    }

    private readonly struct EdgeCandidate {
        public readonly int EdgeIndex;
        public readonly float Weight;

        public EdgeCandidate(int edgeIndex, float weight) {
            EdgeIndex = edgeIndex;
            Weight = weight;
        }
    }
}

/// Traversal graph built from Voronoi connectivity.
public class VoronoiTraversalGraph {
    /// Source Voronoi diagram.
    public VoronoiDiagram Diagram { get; }
    /// Number of neighbour pairs in the diagram.
    public int TotalNeighborPairs { get; }
    /// Target number of connections.
    public int TargetConnections { get; }
    /// Actual connections chosen.
    public IReadOnlyList<VoronoiConnection> Connections => _connections;
    /// Connected pair set for quick lookup.
    public IReadOnlyCollection<PairKey> ConnectedPairs => _connectedPairs;

    private readonly List<VoronoiConnection> _connections;
    private readonly HashSet<PairKey> _connectedPairs;

    /// Constructs a traversal graph container.
    /// <param name="diagram">Source diagram.</param>
    /// <param name="totalNeighborPairs">Total possible neighbour pairs.</param>
    /// <param name="targetConnections">Target connections generated.</param>
    /// <param name="connections">Connection list.</param>
    /// <param name="connectedPairs">Pair lookup set.</param>
    public VoronoiTraversalGraph(VoronoiDiagram diagram, int totalNeighborPairs, int targetConnections,
                                 List<VoronoiConnection> connections, HashSet<PairKey> connectedPairs) {
        Diagram = diagram;
        TotalNeighborPairs = totalNeighborPairs;
        TargetConnections = targetConnections;
        _connections = connections;
        _connectedPairs = connectedPairs;
    }

    /// Draws the base Voronoi debug image and overlays traversal connections with blue strokes and dots.
    public Image DrawDebugImageWithConnections(Color? edgeColor = null, Color? seedColor = null, Color? connectionColor = null,
                                               int strokeWidth = 1, int connectionStroke = 2, int connectionPointRadius = 3) {
        edgeColor ??= new Color(0.9f, 0.9f, 0.9f);
        seedColor ??= new Color(0.9f, 0.25f, 0.25f);
        connectionColor ??= new Color(0.2f, 0.65f, 1f);

        var image = Diagram.DrawDebugImage(Diagram.Size, edgeColor.Value, seedColor.Value, strokeWidth);

        foreach (var connection in _connections) {
            var seedA = Diagram.Seeds[connection.CellA];
            var seedB = Diagram.Seeds[connection.CellB];
            DrawLine(image, seedA, connection.PointOnEdge, connectionColor.Value, connectionStroke);
            DrawLine(image, seedB, connection.PointOnEdge, connectionColor.Value, connectionStroke);
            DrawCircle(image, connection.PointOnEdge, connectionPointRadius, connectionColor.Value);
        }

        return image;
    }

    private static void DrawLine(Image image, Vector2 start, Vector2 end, Color color, int thickness) {
        var a = new Vector2I(Mathf.RoundToInt(start.X), Mathf.RoundToInt(start.Y));
        var b = new Vector2I(Mathf.RoundToInt(end.X), Mathf.RoundToInt(end.Y));

        var dx = Mathf.Abs(b.X - a.X);
        var sx = a.X < b.X ? 1 : -1;
        var dy = -Mathf.Abs(b.Y - a.Y);
        var sy = a.Y < b.Y ? 1 : -1;
        var err = dx + dy;

        var x = a.X;
        var y = a.Y;
        while (true) {
            SetPixelSafe(image, x, y, color, thickness);
            if (x == b.X && y == b.Y) {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy) {
                err += dy;
                x += sx;
            }

            if (e2 <= dx) {
                err += dx;
                y += sy;
            }
        }
    }

    private static void DrawCircle(Image image, Vector2 center, int radius, Color color) {
        var c = new Vector2I(Mathf.RoundToInt(center.X), Mathf.RoundToInt(center.Y));
        for (var y = -radius; y <= radius; y++) {
            for (var x = -radius; x <= radius; x++) {
                if (x * x + y * y <= radius * radius) {
                    SetPixelSafe(image, c.X + x, c.Y + y, color, 1);
                }
            }
        }
    }

    private static void SetPixelSafe(Image image, int x, int y, Color color, int thickness) {
        for (var dy = -thickness + 1; dy < thickness; dy++) {
            for (var dx = -thickness + 1; dx < thickness; dx++) {
                var px = x + dx;
                var py = y + dy;
                if (px >= 0 && py >= 0 && px < image.GetWidth() && py < image.GetHeight()) {
                    image.SetPixel(px, py, color);
                }
            }
        }
    }
}

/// A single connection along a Voronoi edge, including the sampled point.
public class VoronoiConnection {
    /// First cell id.
    public int CellA { get; }
    /// Second cell id.
    public int CellB { get; }
    /// Index of the underlying edge.
    public int EdgeIndex { get; }
    /// Sampled point along the edge.
    public Vector2 PointOnEdge { get; }
    /// Edge length.
    public float EdgeLength { get; }

    /// Constructs a connection entry.
    /// <param name="cellA">First cell id.</param>
    /// <param name="cellB">Second cell id.</param>
    /// <param name="edgeIndex">Edge index.</param>
    /// <param name="pointOnEdge">Sampled point on edge.</param>
    /// <param name="edgeLength">Length of the edge.</param>
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

    /// Builds an ordered pair key.
    /// <param name="a">First index.</param>
    /// <param name="b">Second index.</param>
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

    /// Initializes a disjoint-set forest.
    /// <param name="size">Number of elements.</param>
    public UnionFind(int size) {
        _parent = new int[size];
        _rank = new int[size];
        ComponentCount = size;
        for (var i = 0; i < size; i++) {
            _parent[i] = i;
            _rank[i] = 0;
        }
    }

    /// Finds the root of an element with path compression.
    /// <param name="x">Element index.</param>
    /// <returns>Root representative.</returns>
    public int Find(int x) {
        if (_parent[x] != x) {
            _parent[x] = Find(_parent[x]);
        }

        return _parent[x];
    }

    /// Unions two sets.
    /// <param name="a">First element.</param>
    /// <param name="b">Second element.</param>
    /// <returns>True if a merge happened.</returns>
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
