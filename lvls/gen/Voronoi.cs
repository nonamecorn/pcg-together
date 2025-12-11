using System;
using System.Collections.Generic;
using Godot;

namespace PCGTogether.lvls.gen;
#nullable enable

/// Node that generates a Poisson-sampled Voronoi diagram and an optional debug texture.
public partial class Voronoi : Node {
    /// Canvas size used for sampling and clipping.
    [Export] public Vector2I CanvasSize = new(512, 512);
    /// Poisson disk radius.
    [Export] public float PoissonRadius = 32f;
    /// Attempts per active Poisson sample before removal.
    [Export] public int PoissonAttempts = 30;
    /// Seed for deterministic generation.
    [Export] public int Seed = 1;
    /// Pixels of padding to keep seeds away from the canvas border.
    [Export] public int SeedPadding = 8;
    /// If true, calls Generate during _Ready.
    [Export] public bool GenerateOnReady = true;
    /// Color used when drawing Voronoi edges.
    [Export] public Color EdgeColor = new Color(0.9f, 0.9f, 0.9f);
    /// Color used when drawing seed points.
    [Export] public Color SeedColor = new Color(0.9f, 0.25f, 0.25f);
    /// Stroke width for debug edge drawing.
    [Export] public int StrokeWidth = 1;
    /// Generated Voronoi graph data.
    public VoronoiDiagram? Diagram { get; private set; }
    /// Raw debug image containing edges and seed dots.
    public Image? DebugImage { get; private set; }
    /// Texture created from the debug image for UI display.
    public ImageTexture? DebugTexture { get; private set; }

    public override void _Ready() {
        if (GenerateOnReady) {
            Generate();
        }
    }

    /// Runs Poisson sampling, builds Voronoi topology, and renders the debug image.
    public void Generate() {
        var padding = Mathf.Max(0, SeedPadding);
        var innerSize = new Vector2I(
            Mathf.Max(1, CanvasSize.X - padding * 2),
            Mathf.Max(1, CanvasSize.Y - padding * 2)
        );

        var samples = PoissonDS.Generate(innerSize, PoissonRadius, PoissonAttempts, Seed);
        var shiftedSamples = new List<Vector2>(samples.Count);
        var offset = new Vector2(padding, padding);
        foreach (var s in samples) {
            shiftedSamples.Add(s + offset);
        }

        Diagram = VoronoiDiagram.Build(shiftedSamples, CanvasSize);
        DebugImage = Diagram.DrawDebugImage(CanvasSize, EdgeColor, SeedColor, StrokeWidth);
        DebugTexture = ImageTexture.CreateFromImage(DebugImage);
        var DebugSprite = GetNode<Sprite2D>("Sprite2D");
        if (DebugSprite != null) {
            DebugSprite.Texture = DebugTexture;
        }
    }
}

/// Voronoi graph consisting of seeds, cells, and edges.
public class VoronoiDiagram {
    /// Canvas size used for clipping.
    public readonly Vector2I Size;
    /// Seed coordinates.
    public readonly List<Vector2> Seeds;
    /// Cells keyed by seed index.
    public readonly List<VoronoiCell> Cells;
    /// Undirected Voronoi edges between seeds.
    public readonly List<VoronoiEdge> Edges;

    private VoronoiDiagram(Vector2I size, List<Vector2> seeds, List<VoronoiCell> cells, List<VoronoiEdge> edges) {
        Size = size;
        Seeds = seeds;
        Cells = cells;
        Edges = edges;
    }

    /// Builds a Voronoi diagram from the given seeds, clipped to the provided size.
    public static VoronoiDiagram Build(List<Vector2> seeds, Vector2I size) {
        var cells = new List<VoronoiCell>(seeds.Count);
        for (var i = 0; i < seeds.Count; i++) {
            cells.Add(new VoronoiCell(i, seeds[i]));
        }

        if (seeds.Count < 2) {
            return new VoronoiDiagram(size, seeds, cells, new List<VoronoiEdge>());
        }

        if (seeds.Count == 2) {
            cells[0].Neighbors.Add(1);
            cells[1].Neighbors.Add(0);
            var mid = (seeds[0] + seeds[1]) * 0.5f;
            var dir = seeds[1] - seeds[0];
            var perp = new Vector2(-dir.Y, dir.X).Normalized();
            var bounds = new Rect2(Vector2.Zero, size);
            var from = mid - perp * 10000f;
            var to = mid + perp * 10000f;
            TryClipSegmentToBounds(ref from, ref to, bounds);
            var edge = new VoronoiEdge {
                From = from,
                To = to,
                SeedA = 0,
                SeedB = 1,
                IsBorder = true
            };
            var edgesList = new List<VoronoiEdge> { edge };
            cells[0].EdgeIndices.Add(0);
            cells[1].EdgeIndices.Add(0);
            return new VoronoiDiagram(size, seeds, cells, edgesList);
        }

        var edges = BuildEdgesFromDelaunay(seeds, size, cells);
        return new VoronoiDiagram(size, seeds, cells, edges);
    }

    private static List<VoronoiEdge> BuildEdgesFromDelaunay(List<Vector2> seeds, Vector2I size, List<VoronoiCell> cells) {
        var points = seeds.ToArray();
        var triangulation = Geometry2D.TriangulateDelaunay(points);
        var triangleCount = triangulation.Length / 3;
        if (triangleCount == 0) {
            return new List<VoronoiEdge>();
        }

        var triangles = new List<Triangle>(triangleCount);
        for (var i = 0; i < triangulation.Length; i += 3) {
            var a = triangulation[i];
            var b = triangulation[i + 1];
            var c = triangulation[i + 2];
            var circum = ComputeCircumcenter(points[a], points[b], points[c]);
            triangles.Add(new Triangle(a, b, c, circum));
        }

        var edgeMap = new Dictionary<EdgeKey, EdgeData>();
        for (var t = 0; t < triangles.Count; t++) {
            var tri = triangles[t];
            var verts = tri.Vertices;
            for (var i = 0; i < 3; i++) {
                var start = verts[i];
                var end = verts[(i + 1) % 3];
                var opposite = verts[(i + 2) % 3];
                var key = new EdgeKey(start, end);
                if (!edgeMap.TryGetValue(key, out var data)) {
                    data = EdgeData.Create();
                }

                data.AddTriangle(t, opposite);
                edgeMap[key] = data;
                cells[start].Neighbors.Add(end);
                cells[end].Neighbors.Add(start);
            }
        }

        var bounds = new Rect2(Vector2.Zero, size);
        var voronoiEdges = new List<VoronoiEdge>(edgeMap.Count);

        foreach (var kvp in edgeMap) {
            var key = kvp.Key;
            var data = kvp.Value;
            Vector2 from;
            Vector2 to;
            var isBorder = data.SecondTriangle < 0;
            if (data.SecondTriangle >= 0) {
                from = triangles[data.FirstTriangle].Circumcenter;
                to = triangles[data.SecondTriangle].Circumcenter;
            } else {
                from = triangles[data.FirstTriangle].Circumcenter;
                var dir = ComputePerpendicularDirection(points[key.A], points[key.B]);
                var opposite = points[data.OppositeA];
                if (dir.Dot(opposite - from) > 0) {
                    dir = -dir;
                }

                var extent = Mathf.Max(size.X, size.Y) * 2f;
                var lineStart = from;
                var lineEnd = from + dir * extent;
                if (!TryClipSegmentToBounds(ref lineStart, ref lineEnd, bounds)) {
                    continue;
                }

                from = lineStart;
                to = lineEnd;
            }

            if (!TryClipSegmentToBounds(ref from, ref to, bounds)) {
                continue;
            }

            if (from.DistanceSquaredTo(to) < 0.25f) {
                continue;
            }

            var edge = new VoronoiEdge {
                From = from,
                To = to,
                SeedA = key.A,
                SeedB = key.B,
                IsBorder = isBorder
            };

            var edgeIndex = voronoiEdges.Count;
            voronoiEdges.Add(edge);
            cells[key.A].EdgeIndices.Add(edgeIndex);
            cells[key.B].EdgeIndices.Add(edgeIndex);
        }

        return voronoiEdges;
    }

    /// Renders the Voronoi edges and seeds into an Image for quick inspection.
    public Image DrawDebugImage(Vector2I size, Color edgeColor, Color seedColor, int strokeWidth) {
        var image = Image.CreateEmpty(size.X, size.Y, false, Image.Format.Rgba8);
        image.Fill(new Color(0.06f, 0.06f, 0.06f, 1f));

        foreach (var edge in Edges) {
            DrawLine(image, edge.From, edge.To, edgeColor, strokeWidth);
        }

        foreach (var seed in Seeds) {
            DrawCircle(image, seed, 3, seedColor);
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

    private static void ClipPointToBounds(ref Vector2 point, Rect2 bounds) {
        var maxX = bounds.Position.X + bounds.Size.X - 1f;
        var maxY = bounds.Position.Y + bounds.Size.Y - 1f;
        var x = Mathf.Clamp(point.X, bounds.Position.X, maxX);
        var y = Mathf.Clamp(point.Y, bounds.Position.Y, maxY);
        point = new Vector2(x, y);
    }

    private static bool TryClipSegmentToBounds(ref Vector2 p0, ref Vector2 p1, Rect2 bounds) {
        var x0 = p0.X;
        var y0 = p0.Y;
        var x1 = p1.X;
        var y1 = p1.Y;
        var dx = x1 - x0;
        var dy = y1 - y0;

        var t0 = 0f;
        var t1 = 1f;

        bool Clip(float p, float q) {
            if (Mathf.IsZeroApprox(p)) {
                return q >= 0;
            }

            var r = q / p;
            if (p < 0) {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }

            return true;
        }

        var xmin = bounds.Position.X;
        var xmax = bounds.Position.X + bounds.Size.X;
        var ymin = bounds.Position.Y;
        var ymax = bounds.Position.Y + bounds.Size.Y;

        if (!Clip(-dx, x0 - xmin)) return false;
        if (!Clip( dx, xmax - x0)) return false;
        if (!Clip(-dy, y0 - ymin)) return false;
        if (!Clip( dy, ymax - y0)) return false;

        var nx0 = x0 + t0 * dx;
        var ny0 = y0 + t0 * dy;
        var nx1 = x0 + t1 * dx;
        var ny1 = y0 + t1 * dy;
        p0 = new Vector2(nx0, ny0);
        p1 = new Vector2(nx1, ny1);
        return true;
    }

    private static Vector2 ComputePerpendicularDirection(Vector2 a, Vector2 b) {
        var dir = b - a;
        if (dir == Vector2.Zero) {
            return Vector2.Right;
        }

        return new Vector2(-dir.Y, dir.X).Normalized();
    }

    private static Vector2 ComputeCircumcenter(Vector2 a, Vector2 b, Vector2 c) {
        var d = 2f * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
        if (Mathf.IsZeroApprox(d)) {
            return (a + b + c) / 3f;
        }

        var aSq = a.LengthSquared();
        var bSq = b.LengthSquared();
        var cSq = c.LengthSquared();
        var ux = (aSq * (b.Y - c.Y) + bSq * (c.Y - a.Y) + cSq * (a.Y - b.Y)) / d;
        var uy = (aSq * (c.X - b.X) + bSq * (a.X - c.X) + cSq * (b.X - a.X)) / d;
        return new Vector2(ux, uy);
    }

    private readonly struct Triangle {
        public readonly int[] Vertices;
        public readonly Vector2 Circumcenter;

        public Triangle(int a, int b, int c, Vector2 circumcenter) {
            Vertices = new[] { a, b, c };
            Circumcenter = circumcenter;
        }
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey> {
        public readonly int A;
        public readonly int B;

        public EdgeKey(int a, int b) {
            if (a < b) {
                A = a;
                B = b;
            }
            else {
                A = b;
                B = a;
            }
        }

        public bool Equals(EdgeKey other) {
            return A == other.A && B == other.B;
        }

        public override bool Equals(object? obj) {
            return obj is EdgeKey other && Equals(other);
        }

        public override int GetHashCode() {
            return HashCode.Combine(A, B);
        }
    }

    private struct EdgeData {
        public int FirstTriangle;
        public int SecondTriangle;
        public int OppositeA;
        public int OppositeB;

        public static EdgeData Create() {
            return new EdgeData {
                FirstTriangle = -1,
                SecondTriangle = -1,
                OppositeA = -1,
                OppositeB = -1
            };
        }

        public void AddTriangle(int triangleIndex, int opposite) {
            if (FirstTriangle == -1) {
                FirstTriangle = triangleIndex;
                OppositeA = opposite;
            }
            else {
                SecondTriangle = triangleIndex;
                OppositeB = opposite;
            }
        }
    }
}

/// A single Voronoi region and its adjacency metadata.
public class VoronoiCell {
    /// Index of the seed in the Seeds array.
    public readonly int SeedIndex;

    /// Seed coordinate.
    public readonly Vector2 Seed;

    /// Adjacent seed indices.
    public readonly HashSet<int> Neighbors = new();

    /// Indices into the VoronoiEdge list.
    public readonly List<int> EdgeIndices = new();

    public VoronoiCell(int seedIndex, Vector2 seed) {
        SeedIndex = seedIndex;
        Seed = seed;
    }
}

/// Undirected edge shared by two seeds in the Voronoi diagram.
public class VoronoiEdge {
    /// Edge start point.
    public Vector2 From { get; set; }

    /// Edge end point.
    public Vector2 To { get; set; }

    /// Seed index on one side.
    public int SeedA { get; set; }

    /// Seed index on the other side.
    public int SeedB { get; set; }

    /// True when the edge touches the boundary.
    public bool IsBorder { get; set; }
}
