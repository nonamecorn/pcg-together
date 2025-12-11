using System;
using System.Collections.Generic;
using Godot;

namespace PCGTogether.lvls.gen;

/// Poisson disk sampler utility for generating blue-noise seed points.
public partial class PoissonDS : Node {
    /// Region to fill with samples, in pixels.
    [Export] public Vector2I RegionSize = new(512, 512);
    /// Minimum distance between samples.
    [Export] public float Radius = 32f;
    /// Number of attempts per active sample before it is retired.
    [Export] public int RejectionSamples = 30;
    /// Seed for deterministic sampling.
    [Export] public int Seed = 1337;
    
    /// Generates Poisson disk samples for the configured region.
    public List<Vector2> Generate() {
        return Generate(RegionSize, Radius, RejectionSamples, Seed);
    }

    /// Generates Poisson disk samples for the provided settings; optionally caps the number of points.
    public static List<Vector2> Generate(Vector2I regionSize,
                                         float radius,
                                         int rejectionSamples,
                                         int seed,
                                         int? maxPoints = null) {
        if (radius <= 0) {
            throw new ArgumentException("Radius must be > 0", nameof(radius));
        }

        var rng = seed == 0 ? new RandomNumberGenerator() : new RandomNumberGenerator { Seed = (ulong)seed };
        var result = new List<Vector2>();
        var active = new List<Vector2>();

        var cellSize = radius / Mathf.Sqrt(2f);
        var gridWidth = Mathf.CeilToInt(regionSize.X / cellSize);
        var gridHeight = Mathf.CeilToInt(regionSize.Y / cellSize);
        var grid = new int[gridWidth, gridHeight];
        for (var x = 0; x < gridWidth; x++) {
            for (var y = 0; y < gridHeight; y++) {
                grid[x, y] = -1;
            }
        }

        Vector2 FirstSample() {
            return new Vector2(rng.Randf() * regionSize.X, rng.Randf() * regionSize.Y);
        }

        void StoreSample(Vector2 sample) {
            result.Add(sample);
            active.Add(sample);
            var cell = ToCell(sample, cellSize);
            grid[cell.X, cell.Y] = result.Count - 1;
        }

        StoreSample(FirstSample());

        var radiusSquared = radius * radius;

        while (active.Count > 0) {
            var activeIndex = rng.RandiRange(0, active.Count - 1);
            var center = active[activeIndex];
            var found = false;

            for (var i = 0; i < rejectionSamples; i++) {
                var candidate = GenerateCandidate(center, radius, rng);
                if (!IsInside(candidate, regionSize)) {
                    continue;
                }

                if (IsValid(candidate, cellSize, radiusSquared, grid, result, regionSize)) {
                    StoreSample(candidate);
                    found = true;
                    if (maxPoints.HasValue && result.Count >= maxPoints.Value) {
                        return result;
                    }

                    break;
                }
            }

            if (!found) {
                active.RemoveAt(activeIndex);
            }
        }

        return result;
    }

    private static Vector2I ToCell(Vector2 point, float cellSize) {
        return new Vector2I(Mathf.FloorToInt(point.X / cellSize), Mathf.FloorToInt(point.Y / cellSize));
    }

    private static bool IsInside(Vector2 point, Vector2I regionSize) {
        return point.X >= 0 && point.X < regionSize.X && point.Y >= 0 && point.Y < regionSize.Y;
    }

    private static Vector2 GenerateCandidate(Vector2 center, float radius, RandomNumberGenerator rng) {
        // Sample radius uniformly over the annulus to avoid clustering.
        var distance = radius * (1f + Mathf.Sqrt(rng.Randf()));
        var angle = rng.Randf() * Mathf.Tau;
        var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        return center + offset;
    }

    private static bool IsValid(Vector2 candidate, float cellSize, float radiusSquared, int[,] grid,
                                List<Vector2> points, Vector2I regionSize) {
        var cell = ToCell(candidate, cellSize);
        var searchRadius = 2;

        for (var dx = -searchRadius; dx <= searchRadius; dx++) {
            for (var dy = -searchRadius; dy <= searchRadius; dy++) {
                var cx = cell.X + dx;
                var cy = cell.Y + dy;
                if (cx < 0 || cy < 0 || cx >= grid.GetLength(0) || cy >= grid.GetLength(1)) {
                    continue;
                }

                var sampleIndex = grid[cx, cy];
                if (sampleIndex == -1) {
                    continue;
                }

                var diff = points[sampleIndex] - candidate;
                if (diff.LengthSquared() < radiusSquared) {
                    return false;
                }
            }
        }

        return true;
    }
}