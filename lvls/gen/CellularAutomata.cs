#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace PCGTogether.lvls.gen;

/// Configuration for a cave-style cellular automata run.
public readonly struct CaConfig {
    /// Square kernel size (odd, >= 3).
    public int KernelSize { get; }
    /// Neighbour count required for a dead cell to become a wall.
    public int BirthLimit { get; }
    /// Neighbour count required for a wall to survive.
    public int SurvivalLimit { get; }
    /// Iterations to run.
    public int Iterations { get; }
    /// Initial probability that an unmasked cell starts as a wall.
    public float InitialWallProbability { get; }
    /// Depth (in cells) to carve from each connector into the mask to keep portals open.
    public int ConnectorDepth { get; }

    /// Creates a CA configuration with clamped/normalized parameters.
    /// <param name="kernelSize">Neighbourhood size (square, odd, >= 3).</param>
    /// <param name="birthLimit">Neighbours required for a dead cell to become a wall.</param>
    /// <param name="survivalLimit">Neighbours required for a wall to remain.</param>
    /// <param name="iterations">Number of simulation steps.</param>
    /// <param name="initialWallProbability">Chance (0..1) for an unmasked cell to start as wall.</param>
    /// <param name="connectorDepth">Cells to carve inward from each connector every step.</param>
    public CaConfig(int kernelSize, int birthLimit, int survivalLimit, int iterations, float initialWallProbability = 0.45f, int connectorDepth = 2) {
        KernelSize = MakeOdd(Math.Max(3, kernelSize));
        var maxNeighbours = KernelSize * KernelSize - 1;
        BirthLimit = ClampNeighbour(birthLimit, maxNeighbours);
        SurvivalLimit = ClampNeighbour(survivalLimit, maxNeighbours);
        Iterations = Math.Max(0, iterations);
        InitialWallProbability = Mathf.Clamp(initialWallProbability, 0f, 1f);
        ConnectorDepth = Math.Max(0, connectorDepth);
    }

    private static int MakeOdd(int value) {
        return (value & 1) == 1 ? value : value + 1;
    }

    private static int ClampNeighbour(int value, int max) {
        return Math.Max(0, Math.Min(max, value));
    }
}

/// Result of a CA pass for a single cell.
public sealed class CaResult {
    /// Cell index in the diagram.
    public int CellIndex { get; }
    /// Region in world coordinates covered by this mask.
    public Rect2I Region { get; }
    /// Final wall map: 1 = wall, 0 = empty.
    public byte[,] Tiles { get; }
    /// Connectors used for post-merge stitching.
    public IReadOnlyList<CellConnector> Connectors { get; }

    /// Builds a CA result container.
    /// <param name="cellIndex">Voronoi cell index.</param>
    /// <param name="region">World-space region covered by the tile grid.</param>
    /// <param name="tiles">Final wall grid (1=wall, 0=floor).</param>
    /// <param name="connectors">Connectors used to keep portals open.</param>
    public CaResult(int cellIndex, Rect2I region, byte[,] tiles, IReadOnlyList<CellConnector> connectors) {
        CellIndex = cellIndex;
        Region = region;
        Tiles = tiles;
        Connectors = connectors;
    }
}

/// Simple cellular automata runner that respects a mask (mask==0 => treated as wall).
public static class CellularAutomata {
    /// Runs the CA for a single Voronoi cell.
    /// <param name="task">Prepared cell task containing mask and connectors.</param>
    /// <param name="config">CA parameters.</param>
    /// <returns>Resulting tile grid for the cell.</returns>
    public static CaResult Run(CellTask task, CaConfig config) {
        if (task == null) {
            throw new ArgumentNullException(nameof(task));
        }

        var mask = task.Mask;
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var current = new byte[width, height];
        var next = new byte[width, height];
        var offsets = BuildNeighbourOffsets(config.KernelSize);
        var carveMask = BuildConnectorCarveMask(task.Connectors, mask, config.ConnectorDepth);

        var rng = new DeterministicRng(task.CaSeed);

        // Initial fill with deterministic noise; masked-out cells stay walls.
        for (var x = 0; x < width; x++) {
            for (var y = 0; y < height; y++) {
                if (mask[x, y] == 0 && carveMask[x, y] == 0) {
                    current[x, y] = 1;
                    continue;
                }

                if (carveMask[x, y] == 1) {
                    current[x, y] = 0;
                } else {
                    current[x, y] = rng.NextFloat() < config.InitialWallProbability ? (byte)1 : (byte)0;
                }
            }
        }

        for (var iter = 0; iter < config.Iterations; iter++) {
            Step(mask, carveMask, current, next, offsets, config.BirthLimit, config.SurvivalLimit);
            Swap(ref current, ref next);
        }

        return new CaResult(task.CellIndex, task.Region, current, task.Connectors);
    }

    private static void Step(byte[,] mask, byte[,] carveMask, byte[,] src, byte[,] dst, List<Vector2I> offsets, int birthLimit, int survivalLimit) {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        for (var x = 0; x < width; x++) {
            for (var y = 0; y < height; y++) {
                if (carveMask[x, y] == 1) {
                    dst[x, y] = 0;
                    continue;
                }

                if (mask[x, y] == 0) {
                    dst[x, y] = 1;
                    continue;
                }

                var neighbours = CountNeighbours(mask, carveMask, src, x, y, offsets);
                var alive = src[x, y] == 1;
                if (alive) {
                    dst[x, y] = (byte)(neighbours >= survivalLimit ? 1 : 0);
                }
                else {
                    dst[x, y] = (byte)(neighbours >= birthLimit ? 1 : 0);
                }
            }
        }
    }

    private static int CountNeighbours(byte[,] mask, byte[,] carveMask, byte[,] grid, int x, int y, List<Vector2I> offsets) {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var count = 0;
        for (var i = 0; i < offsets.Count; i++) {
            var off = offsets[i];
            var nx = x + off.X;
            var ny = y + off.Y;
            if (nx < 0 || ny < 0 || nx >= width || ny >= height) {
                count++; // treat outside region as wall
                continue;
            }

            if (mask[nx, ny] == 0 && carveMask[nx, ny] == 0) {
                count++; // masked-out tiles behave like walls
                continue;
            }

            if (carveMask[nx, ny] == 1) {
                continue;
            }

            count += grid[nx, ny];
        }

        return count;
    }

    private static List<Vector2I> BuildNeighbourOffsets(int kernelSize) {
        var half = kernelSize / 2;
        var offsets = new List<Vector2I>((kernelSize * kernelSize) - 1);
        for (var dx = -half; dx <= half; dx++) {
            for (var dy = -half; dy <= half; dy++) {
                if (dx == 0 && dy == 0) {
                    continue;
                }

                offsets.Add(new Vector2I(dx, dy));
            }
        }

        return offsets;
    }

    private static byte[,] BuildConnectorCarveMask(IReadOnlyList<CellConnector> connectors, byte[,] mask, int depth) {
        var width = mask.GetLength(0);
        var height = mask.GetLength(1);
        var carve = new byte[width, height];
        if (depth <= 0 || connectors.Count == 0) {
            return carve;
        }

        foreach (var connector in connectors) {
            CarveLine(connector.LocalPoint, connector.DirectionIntoCell, depth, mask, carve);
        }

        return carve;
    }

    private static void CarveLine(Vector2I start, Vector2 direction, int depth, byte[,] mask, byte[,] carve) {
        var dirNorm = direction;
        if (dirNorm.LengthSquared() < 1e-6f) {
            dirNorm = Vector2.Right;
        }
        dirNorm = dirNorm.Normalized();

        var endOffset = new Vector2I(
            Mathf.FloorToInt(dirNorm.X * depth),
            Mathf.FloorToInt(dirNorm.Y * depth)
        );
        var end = start + endOffset;
        foreach (var cell in RasterLine(start, end)) {
            if (cell.X < 0 || cell.Y < 0 || cell.X >= carve.GetLength(0) || cell.Y >= carve.GetLength(1)) {
                continue;
            }
            if (mask[cell.X, cell.Y] == 0) {
                continue;
            }
            carve[cell.X, cell.Y] = 1;
        }
    }

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

    private static void Swap(ref byte[,] a, ref byte[,] b) {
        var tmp = a;
        a = b;
        b = tmp;
    }
}
