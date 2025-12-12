#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace PCGTogether.lvls.gen;

/// Mediates between Voronoi generation, traversal, and the per-cell CA stage.
/// Collects immutable inputs (masks, connectors, deterministic seeds) that can be
/// passed to worker threads without touching Godot objects.
public sealed class VoronoiCA {
    private const int CaSalt = unchecked((int)0xCA11FACEu);

    public VoronoiSeedChain SeedChain { get; }
    public Vector2I CanvasSize { get; }
    public int[,] OwnershipGrid { get; }
    public IReadOnlyList<CellTask> CellTasks => _cellTasks;

    private readonly List<CellTask> _cellTasks;

    private VoronoiCA(VoronoiSeedChain seedChain, Vector2I canvasSize, int[,] ownershipGrid, List<CellTask> tasks) {
        SeedChain = seedChain;
        CanvasSize = canvasSize;
        OwnershipGrid = ownershipGrid;
        _cellTasks = tasks;
    }

    /// Builds CA inputs for each Voronoi cell using the provided diagram and traversal.
    /// The traversal may be null; connectors will then be empty.
    /// <param name="diagram">Voronoi diagram with ownership grid.</param>
    /// <param name="traversal">Optional traversal graph for inter-cell connectors.</param>
    /// <param name="seeds">Seed chain used to derive per-cell CA seeds.</param>
    /// <param name="padding">Extra pixels around each cell mask (clamped to canvas).</param>
    public static VoronoiCA Build(VoronoiDiagram diagram, VoronoiTraversalGraph? traversal, VoronoiSeedChain seeds, int padding = 1) {
        if (diagram == null) {
            throw new ArgumentNullException(nameof(diagram));
        }

        var ownership = diagram.OwnershipGrid;
        var connectorMap = BuildConnectorMap(traversal, diagram);
        var tasks = new List<CellTask>(diagram.Cells.Count);
        var caBaseSeed = SeedUtils.DeriveSeed(seeds.BaseSeed, CaSalt);

        for (var i = 0; i < diagram.Cells.Count; i++) {
            var cell = diagram.Cells[i];
            var region = PadBounds(cell.BoundingBox, diagram.Size, padding);
            var mask = BuildMask(region, ownership, i);

            IReadOnlyList<CellConnector> connectors;
            if (!connectorMap.TryGetValue(i, out var rawConnectors) || rawConnectors.Count == 0) {
                connectors = Array.Empty<CellConnector>();
            } else {
                var list = new List<CellConnector>(rawConnectors.Count);
                foreach (var raw in rawConnectors) {
                    var localPoint = ToLocal(raw.WorldPoint, region);
                    var inward = InwardDirection(diagram.Seeds[i], raw.WorldPoint);
                    list.Add(new CellConnector(raw.OtherCell, raw.EdgeIndex, raw.WorldPoint, localPoint, inward));
                }

                connectors = list;
            }

            var caSeed = SeedUtils.DeriveSeed(caBaseSeed, i);
            tasks.Add(new CellTask(i, region, mask, connectors, caSeed, cell.Seed));
        }

        return new VoronoiCA(seeds, diagram.Size, ownership, tasks);
    }

    /// Runs cellular automata for all cells using a limited degree of parallelism and merges the outputs.
    /// <param name="config">CA parameters.</param>
    /// <param name="maxDegreeOfParallelism">Cap on concurrent workers; 0 or less uses Environment.ProcessorCount.</param>
    /// <returns>Merged CA map and per-cell results.</returns>
    public CaRunResult RunAll(CaConfig config, int maxDegreeOfParallelism = 0) {
        var results = new CaResult[_cellTasks.Count];
        if (_cellTasks.Count == 0) {
            return new CaRunResult(CanvasSize, OwnershipGrid, Array.Empty<CaResult>(), new byte[CanvasSize.X, CanvasSize.Y]);
        }

        var dop = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : System.Environment.ProcessorCount;
        var options = new ParallelOptions { MaxDegreeOfParallelism = dop };

        Parallel.For(0, _cellTasks.Count, options, i => {
            results[i] = CellularAutomata.Run(_cellTasks[i], config);
        });

        var merged = MergeResults(results, CanvasSize, OwnershipGrid);
        return new CaRunResult(CanvasSize, OwnershipGrid, results, merged);
    }

    private static byte[,] MergeResults(IReadOnlyList<CaResult> results, Vector2I size, int[,] ownership) {
        var final = new byte[size.X, size.Y];
        for (var i = 0; i < results.Count; i++) {
            var res = results[i];
            var region = res.Region;
            for (var x = 0; x < region.Size.X; x++) {
                var worldX = region.Position.X + x;
                if (worldX < 0 || worldX >= size.X) continue;
                for (var y = 0; y < region.Size.Y; y++) {
                    var worldY = region.Position.Y + y;
                    if (worldY < 0 || worldY >= size.Y) continue;
                    if (ownership[worldX, worldY] != res.CellIndex) continue;
                    final[worldX, worldY] = res.Tiles[x, y];
                }
            }
        }

        return final;
    }

    private static Rect2I PadBounds(Rect2I bounds, Vector2I canvasSize, int padding) {
        var minX = Math.Max(0, bounds.Position.X - padding);
        var minY = Math.Max(0, bounds.Position.Y - padding);
        var maxX = Math.Min(canvasSize.X - 1, bounds.Position.X + bounds.Size.X + padding - 1);
        var maxY = Math.Min(canvasSize.Y - 1, bounds.Position.Y + bounds.Size.Y + padding - 1);
        var width = Math.Max(1, maxX - minX + 1);
        var height = Math.Max(1, maxY - minY + 1);
        return new Rect2I(new Vector2I(minX, minY), new Vector2I(width, height));
    }

    private static byte[,] BuildMask(Rect2I region, int[,] ownership, int cellIndex) {
        var mask = new byte[region.Size.X, region.Size.Y];
        var startX = region.Position.X;
        var startY = region.Position.Y;
        for (var x = 0; x < region.Size.X; x++) {
            var worldX = startX + x;
            for (var y = 0; y < region.Size.Y; y++) {
                var worldY = startY + y;
                if (ownership[worldX, worldY] == cellIndex) {
                    mask[x, y] = 1;
                }
            }
        }

        return mask;
    }

    private static Vector2I ToLocal(Vector2 worldPoint, Rect2I region) {
        var lx = Mathf.Clamp(Mathf.FloorToInt(worldPoint.X) - region.Position.X, 0, region.Size.X - 1);
        var ly = Mathf.Clamp(Mathf.FloorToInt(worldPoint.Y) - region.Position.Y, 0, region.Size.Y - 1);
        return new Vector2I(lx, ly);
    }

    private static Vector2 InwardDirection(Vector2 seed, Vector2 point) {
        var dir = seed - point;
        if (dir.LengthSquared() < 1e-6f) {
            return Vector2.Right;
        }

        return dir.Normalized();
    }

    private static Dictionary<int, List<RawConnector>> BuildConnectorMap(VoronoiTraversalGraph? traversal, VoronoiDiagram diagram) {
        var map = new Dictionary<int, List<RawConnector>>(diagram.Cells.Count);
        if (traversal == null) {
            return map;
        }

        foreach (var conn in traversal.Connections) {
            Add(conn.CellA, conn.CellB, conn);
            Add(conn.CellB, conn.CellA, conn);
        }

        return map;

        void Add(int cell, int other, VoronoiConnection connection) {
            if (!map.TryGetValue(cell, out var list)) {
                list = new List<RawConnector>();
                map[cell] = list;
            }
            list.Add(new RawConnector(other, connection.PointOnEdge, connection.EdgeIndex));
        }
    }

    private readonly struct RawConnector {
        public readonly int OtherCell;
        public readonly Vector2 WorldPoint;
        public readonly int EdgeIndex;

        public RawConnector(int otherCell, Vector2 worldPoint, int edgeIndex) {
            OtherCell = otherCell;
            WorldPoint = worldPoint;
            EdgeIndex = edgeIndex;
        }
    }
}

/// Immutable CA job payload for a single Voronoi cell.
public sealed class CellTask {
    public int CellIndex { get; }
    public Rect2I Region { get; }
    public byte[,] Mask { get; }
    public IReadOnlyList<CellConnector> Connectors { get; }
    public int CaSeed { get; }
    public Vector2 SeedPosition { get; }

    public CellTask(int cellIndex, Rect2I region, byte[,] mask, IReadOnlyList<CellConnector> connectors, int caSeed, Vector2 seedPosition) {
        CellIndex = cellIndex;
        Region = region;
        Mask = mask;
        Connectors = connectors;
        CaSeed = caSeed;
        SeedPosition = seedPosition;
    }
}

/// Connection constraint for carving portals between adjacent CA masks.
public readonly struct CellConnector {
    public int OtherCell { get; }
    public int EdgeIndex { get; }
    public Vector2 WorldPoint { get; }
    public Vector2I LocalPoint { get; }
    public Vector2 DirectionIntoCell { get; }

    public CellConnector(int otherCell, int edgeIndex, Vector2 worldPoint, Vector2I localPoint, Vector2 directionIntoCell) {
        OtherCell = otherCell;
        EdgeIndex = edgeIndex;
        WorldPoint = worldPoint;
        LocalPoint = localPoint;
        DirectionIntoCell = directionIntoCell;
    }
}

/// Aggregated CA output for all Voronoi cells.
public sealed class CaRunResult {
    public Vector2I CanvasSize { get; }
    public int[,] Ownership { get; }
    public IReadOnlyList<CaResult> CellResults { get; }
    /// Final merged map (1=wall, 0=floor) in canvas space.
    public byte[,] Merged { get; }

    public CaRunResult(Vector2I canvasSize, int[,] ownership, IReadOnlyList<CaResult> cellResults, byte[,] merged) {
        CanvasSize = canvasSize;
        Ownership = ownership;
        CellResults = cellResults;
        Merged = merged;
    }
}
