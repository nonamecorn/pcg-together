using System.Collections.Generic;
using Godot;
using Godot.Collections;
using PCGTogether.lvls.gen;

namespace PCGTogether.test;

/// Paints Voronoi traversal paths onto a TileMap by clearing terrain along the connections.
public partial class VoronoiTilePainter : Node2D {
    /// Path to the Voronoi generator node.
    [Export] public NodePath VoronoiNodePath;
    /// Path to the TileMap to paint.
    [Export] public NodePath TileMapPath;
    /// If true, paint on _Ready.
    [Export] public bool GenerateOnReady = true;
    /// Target neighbour coverage for traversal generation.
    [Export] public float NeighborCoverage = 0.5f;
    /// Seed override for traversal generation.
    [Export] public int TraversalSeed = 42;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ConnectionDistributionScaling = 0.75f;
    /// Thickness of carved paths.
    [Export] public int PathThickness = 2;
    /// Terrain set to modify.
    [Export] public int TerrainSet = 0;
    /// Terrain id to apply.
    [Export] public int TerrainId = 0;

    private Voronoi _voronoi;
    private TileMapLayer _tileMap;

    /// Godot lifecycle: resolves nodes and optionally paints paths.
    public override void _Ready() {
        _voronoi = GetNodeOrNull<Voronoi>(VoronoiNodePath);
        _tileMap = GetNodeOrNull<TileMapLayer>(TileMapPath);
        if (_voronoi == null || _tileMap == null) {
            GD.PushWarning($"{nameof(VoronoiTilePainter)}: missing Voronoi or TileMap.");
            return;
        }
        // _tileMap.Clear();
        PaintPaths();
    }

    /// Generates a traversal graph and carves its paths into the TileMap.
    public void PaintPaths() {
        if (_voronoi == null || _tileMap == null) {
            return;
        }

        _voronoi.Generate();
        if (_voronoi.Diagram == null) {
            return;
        }
        // Fill(Vector2I.Zero, _voronoi.CanvasSize);
        var traversal = VoronoiTraversal.Build(_voronoi.Diagram, NeighborCoverage, TraversalSeed, true, ConnectionDistributionScaling);
        var cells = new HashSet<Vector2I>();
        foreach (var connection in traversal.Connections) {
            var seedA = _voronoi.Diagram.Seeds[connection.CellA];
            var seedB = _voronoi.Diagram.Seeds[connection.CellB];
            AddLineCells(seedA, connection.PointOnEdge, cells);
            AddLineCells(seedB, connection.PointOnEdge, cells);
        }

        if (cells.Count > 0) {
            _tileMap.SetCellsTerrainConnect(new Array<Vector2I>(new List<Vector2I>(cells)), TerrainSet, TerrainId);
        }
    }

    /// Rasterizes a line in world space and adds thickened tile coordinates.
    /// <param name="from">World-space start.</param>
    /// <param name="to">World-space end.</param>
    /// <param name="cells">Set collecting carved tiles.</param>
    private void AddLineCells(Vector2 from, Vector2 to, HashSet<Vector2I> cells) {
        // Seeds are already in tilemap-local space; just round to tile coords.
        var start = new Vector2I(Mathf.RoundToInt(from.X), Mathf.RoundToInt(from.Y));
        var end = new Vector2I(Mathf.RoundToInt(to.X), Mathf.RoundToInt(to.Y));
        foreach (var cell in RasterLine(start, end)) {
            AddThickCell(cell, cells);
        }
    }

    /// Adds a square footprint around a coordinate.
    /// <param name="center">Center tile.</param>
    /// <param name="cells">Set to append into.</param>
    private void AddThickCell(Vector2I center, HashSet<Vector2I> cells) {
        var r = Mathf.Max(0, PathThickness - 1);
        for (var dy = -r; dy <= r; dy++) {
            for (var dx = -r; dx <= r; dx++) {
                cells.Add(new Vector2I(center.X + dx, center.Y + dy));
            }
        }
    }

    /// Bresenham raster of a line between two tile coordinates.
    /// <param name="a">Start tile.</param>
    /// <param name="b">End tile.</param>
    /// <returns>Enumerable of tiles along the line.</returns>
    private static IEnumerable<Vector2I> RasterLine(Vector2I a, Vector2I b) {
        var x0 = a.X;
        var y0 = a.Y;
        var x1 = b.X;
        var y1 = b.Y;
        var dx = Mathf.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Mathf.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;

        while (true) {
            yield return new Vector2I(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            var e2 = 2 * err;
            if (e2 >= dy) {
                err += dy;
                x0 += sx;
            }
            if (e2 <= dx) {
                err += dx;
                y0 += sy;
            }
        }
    }

    /// Fills a rectangle with a debug tile.
    /// <param name="from">Top-left inclusive.</param>
    /// <param name="to">Bottom-right exclusive.</param>
    public void Fill(Vector2I from, Vector2I to) {
        for (int y = from.Y; y < to.Y; ++y)
        for (int x = from.X; x < to.X; ++x)
            _tileMap.SetCell(new(x, y), 2, Vector2I.One);
    }
}
