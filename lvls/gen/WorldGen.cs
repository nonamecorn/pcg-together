#nullable enable
using System.Collections.Generic;
using Godot;
using PCGTogether.test;

namespace PCGTogether.lvls.gen;

/// High-level world generator that runs Voronoi + traversal + CA and paints into a TileMapLayer.
public partial class WorldGen : Node2D {
    /// NodePath to the Voronoi node.
    [Export] public NodePath VoronoiNodePath = "";
    /// NodePath to the TileMapLayer to paint.
    [Export] public NodePath TileMapPath = "";
    /// Optional debug view to refresh after generation.
    [Export] public NodePath DebugViewPath = "";
    /// If true, generation runs on _Ready.
    [Export] public bool GenerateOnReady = true;

    [ExportCategory("Voronoi")]
    /// Ratio of neighbours to keep connected.
    [Export] public float NeighborCoverage = 0.5f;
    /// Traversal seed override (0 => derived).
    [Export] public int TraversalSeed = 0;
    /// Bias for connector sampling along edges.
    [Export(PropertyHint.Range, "0,1,0.01")] public float ConnectionDistributionScaling = 0.75f;
    /// Padding around each cell mask for CA.
    [Export] public int CellPadding = 4;

    [ExportCategory("CA")]
    /// CA kernel size (odd).
    [Export] public int KernelSize = 5;
    /// Birth threshold.
    [Export] public int BirthLimit = 4;
    /// Survival threshold.
    [Export] public int SurvivalLimit = 3;
    /// Number of CA iterations.
    [Export] public int Iterations = 4;
    /// Initial wall probability for CA seeds.
    [Export(PropertyHint.Range, "0,1,0.01")] public float InitialWallProbability = 0.45f;

    [ExportCategory("Tilemap")]
    /// Thickness applied when painting floors.
    [Export] public int PathThickness = 2;
    /// TileMap terrain set id.
    [Export] public int TerrainSet = 0;
    /// TileMap terrain id.
    [Export] public int TerrainId = 0;

    private Voronoi? _voronoi;
    private TileMapLayer? _tileMap;
    private VoronoiDebugView? _debugView;
    private VoronoiTraversalGraph? _traversalGraph;

    /// Godot lifecycle: fetches required nodes and optionally triggers generation.
    public override void _Ready() {
        _voronoi = GetNodeOrNull<Voronoi>(VoronoiNodePath);
        _tileMap = GetNodeOrNull<TileMapLayer>(TileMapPath);
        _debugView = GetNodeOrNull<VoronoiDebugView>(DebugViewPath);
        if (_voronoi == null || _tileMap == null) {
            GD.PushWarning($"{nameof(WorldGen)}: missing Voronoi or TileMap.");
            return;
        }

        if (GenerateOnReady) {
            GenerateAndPaint();
        }

        if (_debugView != null) {
            _debugView.Refresh(_traversalGraph);
        }
    }

    /// Runs Voronoi, traversal, and CA; merges and paints the final map into the TileMapLayer.
    public void GenerateAndPaint() {
        // Build seeds and diagrams.
        var traversalSeedOverride = TraversalSeed == 0 ? (int?)null : TraversalSeed;
        var seeds = new VoronoiSeedChain(_voronoi!.Seed, traversalSeedOverride: traversalSeedOverride);
        _voronoi.Generate(seeds);
        if (_voronoi.Diagram == null) {
            return;
        }

        var traversal = VoronoiTraversal.Build(_voronoi.Diagram, NeighborCoverage, seeds.TraversalSeed, true, ConnectionDistributionScaling);
        var caMediator = VoronoiCA.Build(_voronoi.Diagram, traversal, seeds, CellPadding);
        var caConfig = new CaConfig(KernelSize, BirthLimit, SurvivalLimit, Iterations, InitialWallProbability);
        var caResult = caMediator.RunAll(caConfig);

        PaintTilemap(caResult);
        _traversalGraph = traversal;
    }

    /// Paints merged CA output into the TileMapLayer.
    /// <param name="result">Merged CA result.</param>
    private void PaintTilemap(CaRunResult result) {
        if (_tileMap == null) {
            return;
        }

        // _tileMap.Clear();
        var cells = new List<Vector2I>();
        var merged = result.Merged;
        for (var x = 0; x < result.CanvasSize.X; x++) {
            for (var y = 0; y < result.CanvasSize.Y; y++) {
                if (merged[x, y] == 0) {
                    // floor: carve path thickness
                    AddThickCell(new Vector2I(x, y), PathThickness, cells);
                }
            }
        }

        if (cells.Count > 0) {
            _tileMap.SetCellsTerrainConnect(new Godot.Collections.Array<Vector2I>(cells), TerrainSet, TerrainId);
        }

        foreach (Vector2 seed in _voronoi!.Diagram!.Seeds) {
            _tileMap.SetCell(new(Mathf.RoundToInt(seed.X), Mathf.RoundToInt(seed.Y)), 0, Vector2I.Zero);
        }
    }

    /// Adds a square footprint around a tile coordinate.
    /// <param name="center">Center tile coordinate.</param>
    /// <param name="thickness">Thickness radius.</param>
    /// <param name="cells">Collection to append painted tiles into.</param>
    private static void AddThickCell(Vector2I center, int thickness, List<Vector2I> cells) {
        var r = Mathf.Max(0, thickness - 1);
        for (var dy = -r; dy <= r; dy++) {
            for (var dx = -r; dx <= r; dx++) {
                cells.Add(new Vector2I(center.X + dx, center.Y + dy));
            }
        }
    }
}
