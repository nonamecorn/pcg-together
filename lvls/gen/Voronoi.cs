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
    /// Seed chain used for the last generation pass.
    public VoronoiSeedChain SeedChain { get; private set; }

    /// Godot lifecycle: optionally triggers generation.
    public override void _Ready() {
        if (GenerateOnReady) {
            Generate();
        }
    }

    /// Runs Poisson sampling and builds the Voronoi topology (no drawing).
    /// <returns>Generated diagram assigned to Diagram.</returns>
    public void Generate() {
        var seeds = new VoronoiSeedChain(SeedUtils.RandomizeIfZero(Seed));
        Generate(seeds);
    }

    /// Runs Poisson sampling using a supplied deterministic seed chain.
    /// <param name="seeds">Seed chain to use for generation.</param>
    public void Generate(VoronoiSeedChain seeds) {
        SeedChain = seeds;
        var padding = Mathf.Max(0, SeedPadding);
        var innerSize = new Vector2I(
            Mathf.Max(1, CanvasSize.X - padding * 2),
            Mathf.Max(1, CanvasSize.Y - padding * 2)
        );

        var samples = PoissonDS.Generate(innerSize, PoissonRadius, PoissonAttempts, SeedChain.PoissonSeed);
        var shiftedSamples = new List<Vector2>(samples.Count);
        var offset = new Vector2(padding, padding);
        foreach (var s in samples) {
            shiftedSamples.Add(s + offset);
        }

        Diagram = VoronoiDiagram.Build(shiftedSamples, CanvasSize);
    }

    /// Finds the Voronoi cell whose seed is closest to the given point. Returns null if no diagram is available or the point is outside the canvas.
    /// <param name="point">Point in world/canvas space.</param>
    /// <returns>Voronoi cell containing the point, or null.</returns>
    public VoronoiCell? FindCell(Vector2 point) {
        if (Diagram == null || Diagram.Seeds.Count == 0) {
            return null;
        }

        if (!IsInsideCanvas(point) || Diagram.OwnershipGrid == null) {
            return null;
        }

        if (!TryGetOwnershipIndex(point, Diagram.OwnershipGrid, Diagram.Size, out var cellIndex)) {
            return null;
        }

        if (cellIndex < 0 || cellIndex >= Diagram.Cells.Count) {
            return null;
        }

        return Diagram.Cells[cellIndex];
    }

    /// Checks whether the provided point belongs to the specified cell (i.e., that cell's seed is the closest and the point lies within the canvas).
    /// <param name="cellIndex">Cell index to test.</param>
    /// <param name="point">Point in canvas space.</param>
    /// <returns>True if the point lies in the cell.</returns>
    public bool IsPointInCell(int cellIndex, Vector2 point) {
        if (Diagram == null || cellIndex < 0 || cellIndex >= Diagram.Cells.Count) {
            return false;
        }

        if (!IsInsideCanvas(point) || Diagram.OwnershipGrid == null) {
            return false;
        }

        return TryGetOwnershipIndex(point, Diagram.OwnershipGrid, Diagram.Size, out var owner) && owner == cellIndex;
    }

    /// Finds the nearest seed index to a point by brute force.
    /// <param name="point">Point in canvas space.</param>
    /// <returns>Index of closest seed.</returns>
    public int FindNearestSeedIndex(Vector2 point) {
        var seeds = Diagram!.Seeds;
        var bestIndex = 0;
        var bestDist = point.DistanceSquaredTo(seeds[0]);
        for (var i = 1; i < seeds.Count; i++) {
            var dist = point.DistanceSquaredTo(seeds[i]);
            if (dist < bestDist) {
                bestDist = dist;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// Checks if a point lies within the canvas bounds.
    /// <param name="point">Point to test.</param>
    /// <returns>True if inside.</returns>
    private bool IsInsideCanvas(Vector2 point) {
        var bounds = new Rect2(Vector2.Zero, CanvasSize);
        return bounds.HasPoint(point);
    }

    /// Maps a point to an ownership index.
    /// <param name="point">Point in canvas space.</param>
    /// <param name="ownership">Ownership grid.</param>
    /// <param name="size">Canvas size.</param>
    /// <param name="index">Output owner index.</param>
    /// <returns>True if point is in bounds and owned.</returns>
    private static bool TryGetOwnershipIndex(Vector2 point, int[,] ownership, Vector2I size, out int index) {
        var x = Mathf.FloorToInt(point.X);
        var y = Mathf.FloorToInt(point.Y);
        if (x < 0 || y < 0 || x >= size.X || y >= size.Y) {
            index = -1;
            return false;
        }

        index = ownership[x, y];
        return index >= 0;
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
    /// Delaunay triangles backing the diagram.
    public readonly List<DelaunayTriangle> Triangles;
    /// Ownership grid mapping each pixel to the nearest seed index; -1 when empty.
    public readonly int[,] OwnershipGrid;

    /// Creates a Voronoi diagram instance.
    /// <param name="size">Canvas size.</param>
    /// <param name="seeds">Seed list.</param>
    /// <param name="cells">Cell list.</param>
    /// <param name="edges">Edge list.</param>
    /// <param name="triangles">Delaunay triangles.</param>
    /// <param name="ownershipGrid">Ownership grid.</param>
    private VoronoiDiagram(Vector2I size, List<Vector2> seeds, List<VoronoiCell> cells, List<VoronoiEdge> edges,
                           List<DelaunayTriangle> triangles, int[,] ownershipGrid) {
        Size = size;
        Seeds = seeds;
        Cells = cells;
        Edges = edges;
        Triangles = triangles;
        OwnershipGrid = ownershipGrid;
    }

    /// Builds a Voronoi diagram from the given seeds, clipped to the provided size.
    /// <param name="seeds">Seed positions.</param>
    /// <param name="size">Canvas size.</param>
    /// <returns>Constructed Voronoi diagram.</returns>
    public static VoronoiDiagram Build(List<Vector2> seeds, Vector2I size) {
        var cells = new List<VoronoiCell>(seeds.Count);
        for (var i = 0; i < seeds.Count; i++) {
            cells.Add(new VoronoiCell(i, seeds[i]));
        }

        if (seeds.Count < 2) {
            var ownershipSingle = BuildOwnershipGrid(seeds, size);
            return new VoronoiDiagram(size, seeds, cells, new List<VoronoiEdge>(), new List<DelaunayTriangle>(), ownershipSingle);
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
            var ownershipTwo = BuildOwnershipGrid(seeds, size);
            return new VoronoiDiagram(size, seeds, cells, edgesList, new List<DelaunayTriangle>(), ownershipTwo);
        }

        var triangles = BuildDelaunayTriangles(seeds);
        var edges = BuildEdgesFromDelaunay(seeds, size, cells, triangles);
        ComputeBoundingBoxes(cells, edges);
        var ownership = BuildOwnershipGrid(seeds, size);
        return new VoronoiDiagram(size, seeds, cells, edges, triangles, ownership);
    }

    /// Builds Delaunay triangles from seed points.
    /// <param name="seeds">Seed positions.</param>
    /// <returns>List of Delaunay triangles.</returns>
    private static List<DelaunayTriangle> BuildDelaunayTriangles(List<Vector2> seeds) {
        var points = seeds.ToArray();
        var triangulation = Geometry2D.TriangulateDelaunay(points);
        var triangleCount = triangulation.Length / 3;
        var triangles = new List<DelaunayTriangle>(triangleCount);
        for (var i = 0; i < triangulation.Length; i += 3) {
            var a = triangulation[i];
            var b = triangulation[i + 1];
            var c = triangulation[i + 2];
            var circum = ComputeCircumcenter(points[a], points[b], points[c]);
            triangles.Add(new DelaunayTriangle(a, b, c, circum));
        }

        return triangles;
    }

    /// Builds an ownership grid mapping each pixel to the nearest seed.
    /// <param name="seeds">Seed positions.</param>
    /// <param name="size">Canvas dimensions.</param>
    /// <returns>Ownership grid of size.X by size.Y.</returns>
    private static int[,] BuildOwnershipGrid(List<Vector2> seeds, Vector2I size) {
        var width = size.X;
        var height = size.Y;
        var grid = new int[width, height];
        if (seeds.Count == 0) {
            for (var x = 0; x < width; x++) {
                for (var y = 0; y < height; y++) {
                    grid[x, y] = -1;
                }
            }
            return grid;
        }

        for (var x = 0; x < width; x++) {
            for (var y = 0; y < height; y++) {
                var point = new Vector2(x + 0.5f, y + 0.5f);
                var bestIndex = 0;
                var bestDist = point.DistanceSquaredTo(seeds[0]);
                for (var i = 1; i < seeds.Count; i++) {
                    var dist = point.DistanceSquaredTo(seeds[i]);
                    if (dist < bestDist) {
                        bestDist = dist;
                        bestIndex = i;
                    }
                }
                grid[x, y] = bestIndex;
            }
        }

        return grid;
    }

    /// Converts Delaunay triangulation into Voronoi edges.
    /// <param name="seeds">Seed positions.</param>
    /// <param name="size">Canvas size for clipping.</param>
    /// <param name="cells">Cell collection to populate neighbours/edges.</param>
    /// <param name="triangles">Delaunay triangles.</param>
    /// <returns>List of Voronoi edges.</returns>
    private static List<VoronoiEdge> BuildEdgesFromDelaunay(List<Vector2> seeds, Vector2I size, List<VoronoiCell> cells, List<DelaunayTriangle> triangles) {
        if (triangles.Count == 0) {
            return new List<VoronoiEdge>();
        }

        var points = seeds;

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

    /// Computes AABBs for each cell based on edges.
    /// <param name="cells">Cells to populate.</param>
    /// <param name="edges">Edge list.</param>
    private static void ComputeBoundingBoxes(List<VoronoiCell> cells, List<VoronoiEdge> edges) {
        for (var i = 0; i < cells.Count; i++) {
            var cell = cells[i];
            var minX = Mathf.FloorToInt(cell.Seed.X);
            var minY = Mathf.FloorToInt(cell.Seed.Y);
            var maxX = Mathf.CeilToInt(cell.Seed.X);
            var maxY = Mathf.CeilToInt(cell.Seed.Y);

            foreach (var edgeIndex in cell.EdgeIndices) {
                if (edgeIndex < 0 || edgeIndex >= edges.Count) {
                    continue;
                }

                var e = edges[edgeIndex];
                ExpandBounds(e.From, ref minX, ref minY, ref maxX, ref maxY);
                ExpandBounds(e.To, ref minX, ref minY, ref maxX, ref maxY);
            }

            var sizeX = Math.Max(1, maxX - minX + 1);
            var sizeY = Math.Max(1, maxY - minY + 1);
            cell.BoundingBox = new Rect2I(new Vector2I(minX, minY), new Vector2I(sizeX, sizeY));
        }
    }

    /// Expands integer bounds to include a point.
    /// <param name="point">Point to include.</param>
    /// <param name="minX">Min X bound (mutable).</param>
    /// <param name="minY">Min Y bound (mutable).</param>
    /// <param name="maxX">Max X bound (mutable).</param>
    /// <param name="maxY">Max Y bound (mutable).</param>
    private static void ExpandBounds(Vector2 point, ref int minX, ref int minY, ref int maxX, ref int maxY) {
        minX = Math.Min(minX, Mathf.FloorToInt(point.X));
        minY = Math.Min(minY, Mathf.FloorToInt(point.Y));
        maxX = Math.Max(maxX, Mathf.CeilToInt(point.X));
        maxY = Math.Max(maxY, Mathf.CeilToInt(point.Y));
    }

    /// Renders the Voronoi edges and seeds into an Image for quick inspection.
    /// <param name="size">Image dimensions.</param>
    /// <param name="edgeColor">Color for edges.</param>
    /// <param name="seedColor">Color for seeds.</param>
    /// <param name="strokeWidth">Stroke width for edges.</param>
    /// <returns>Generated debug image.</returns>
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

    /// Draws a Bresenham line into an image.
    /// <param name="image">Target image.</param>
    /// <param name="start">Line start.</param>
    /// <param name="end">Line end.</param>
    /// <param name="color">Color to draw.</param>
    /// <param name="thickness">Stroke thickness.</param>
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

    /// Draws a filled circle into an image.
    /// <param name="image">Target image.</param>
    /// <param name="center">Circle center.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="color">Color to draw.</param>
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

    /// Safely sets pixels with thickness inside image bounds.
    /// <param name="image">Target image.</param>
    /// <param name="x">Pixel X.</param>
    /// <param name="y">Pixel Y.</param>
    /// <param name="color">Color to apply.</param>
    /// <param name="thickness">Stroke thickness.</param>
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

    /// Clamps a point to a rectangle.
    /// <param name="point">Point to clamp.</param>
    /// <param name="bounds">Rectangle bounds.</param>
    private static void ClipPointToBounds(ref Vector2 point, Rect2 bounds) {
        var maxX = bounds.Position.X + bounds.Size.X - 1f;
        var maxY = bounds.Position.Y + bounds.Size.Y - 1f;
        var x = Mathf.Clamp(point.X, bounds.Position.X, maxX);
        var y = Mathf.Clamp(point.Y, bounds.Position.Y, maxY);
        point = new Vector2(x, y);
    }

    /// Clips a line segment to rectangle bounds.
    /// <param name="p0">Line start (mutated).</param>
    /// <param name="p1">Line end (mutated).</param>
    /// <param name="bounds">Clipping rectangle.</param>
    /// <returns>True if segment intersects the bounds.</returns>
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

    /// Computes a normalized perpendicular direction to segment AB.
    /// <param name="a">Segment start.</param>
    /// <param name="b">Segment end.</param>
    /// <returns>Perpendicular unit vector.</returns>
    private static Vector2 ComputePerpendicularDirection(Vector2 a, Vector2 b) {
        var dir = b - a;
        if (dir == Vector2.Zero) {
            return Vector2.Right;
        }

        return new Vector2(-dir.Y, dir.X).Normalized();
    }

    /// Computes the circumcenter of a triangle.
    /// <param name="a">Vertex A.</param>
    /// <param name="b">Vertex B.</param>
    /// <param name="c">Vertex C.</param>
    /// <returns>Circumcenter point.</returns>
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

    /// Delaunay triangle representation.
    public readonly struct DelaunayTriangle {
        public readonly int[] Vertices;
        public readonly Vector2 Circumcenter;

        /// Constructs a triangle record.
        /// <param name="a">First vertex index.</param>
        /// <param name="b">Second vertex index.</param>
        /// <param name="c">Third vertex index.</param>
        /// <param name="circumcenter">Circumcenter position.</param>
        public DelaunayTriangle(int a, int b, int c, Vector2 circumcenter) {
            Vertices = new[] { a, b, c };
            Circumcenter = circumcenter;
        }
    }

    /// Hashable undirected edge key.
    private readonly struct EdgeKey : IEquatable<EdgeKey> {
        public readonly int A;
        public readonly int B;

        /// Initializes an undirected edge key (ordered internally).
        /// <param name="a">First vertex index.</param>
        /// <param name="b">Second vertex index.</param>
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

    /// Triangle membership data for an edge.
    private struct EdgeData {
        public int FirstTriangle;
        public int SecondTriangle;
        public int OppositeA;
        public int OppositeB;

        /// Creates a new edge data container.
        /// <returns>Initialized edge data.</returns>
        public static EdgeData Create() {
            return new EdgeData {
                FirstTriangle = -1,
                SecondTriangle = -1,
                OppositeA = -1,
                OppositeB = -1
            };
        }

        /// Registers a triangle incident to this edge.
        /// <param name="triangleIndex">Triangle index.</param>
        /// <param name="opposite">Opposite vertex index.</param>
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

    /// Axis-aligned bounding box fully covering the cell; populated during diagram construction.
    public Rect2I BoundingBox { get; internal set; }

    /// Constructs a Voronoi cell.
    /// <param name="seedIndex">Seed index.</param>
    /// <param name="seed">Seed position.</param>
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
