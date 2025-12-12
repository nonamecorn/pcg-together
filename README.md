# PCG Together – Procedural Level Generation Overview

## What This Report Covers
- High-level explanation of the generation algorithms (no code embedded).
- How determinism, threading, and masks are handled.
- Where to plug in parameters and how to export results.
- Pointers for screenshots and PDF export (target: ≤2 pages per team member).

## Pipeline at a Glance
1) **Poisson Disk Sampling**  
   - Blue-noise seeds inside the canvas with configurable radius/attempts.  
   - Seeds are padded from the border to avoid degenerate cells.
2) **Voronoi + Ownership Grid**  
   - Delaunay triangulation → Voronoi edges/cells (clipped to canvas).  
   - Each pixel is labeled with its nearest seed (`OwnershipGrid`), used for conflict-free merging.  
   - Bounding boxes are computed per cell to localize work.
3) **Traversal Graph**  
   - Kruskal-like spanning tree biased toward longer edges, then extra edges until neighbour coverage is met.  
   - Each connection stores a sampled point along its edge.
4) **CA Prep (VoronoiCA)**  
   - Builds per-cell masks from `OwnershipGrid` with optional padding.  
   - Connectors are converted to local coordinates and carved to the seed (precomputed carve mask).  
   - Deterministic per-cell seeds are derived from the world seed chain.
5) **Cellular Automata**  
   - Masked cave CA (square kernel, birth/survival rules, iterations, initial wall probability).  
   - Mask==0 behaves as wall; carve mask forces corridors open.  
   - Runs per cell on a thread pool; no Godot objects touched off the main thread.
6) **Merge & Paint**  
   - CA outputs merge into a canvas-wide grid using `OwnershipGrid` to resolve overlaps.  
   - `WorldGen` paints floors into a `TileMapLayer`, optionally thickening footprints.

## Determinism & Seeds
- Root seed → `VoronoiSeedChain` → per-stage seeds (Poisson, traversal, CA).  
- Custom xorshift* RNG (`DeterministicRng`) is thread-safe per instance.  
- Setting a seed yields repeatable output across runs; traversal seed can be overridden independently.

## Key Parameters (edit in Godot inspector)
- **Voronoi**: canvas size, Poisson radius/attempts, padding.  
- **Traversal**: neighbour coverage (0..1), traversal seed, edge bias.  
- **CA**: kernel size (odd ≥3), birth/survival thresholds, iterations, initial wall probability, cell padding.  
- **Painting**: path thickness, terrain set/id for the TileMap.

## Usage
- Attach `WorldGen` to a Node2D, wire `VoronoiNodePath` and `TileMapPath`, tweak exports, and enable `GenerateOnReady` or call `GenerateAndPaint()`.  
- For debugging, `VoronoiDebugView` can render Voronoi or traversal layers; `VoronoiTilePainter` shows traversal-only carving.

## Threading Notes
- CA runs via `VoronoiCA.RunAll` using `Parallel.For` with a configurable degree of parallelism.  
- Only plain data crosses threads; TileMap drawing happens on the main thread after merging.

## Screenshots (recommended)
- Voronoi edges + seeds: `docs/screens/voronoi_debug.png`  
- Traversal overlay: `docs/screens/traversal_overlay.png`  
- CA result before painting: `docs/screens/ca_masks.png`  
- Final painted TileMap: `docs/screens/world_painted.png`  
*(Capture in-editor, save to the suggested paths, and update links if needed.)*

## Exporting to PDF
- Keep the writeup to ≤2 pages per team member; avoid embedding code.  
- Convert this README to PDF with `pandoc README.md -o report.pdf` (or print to PDF from your editor).  
- Ensure screenshots are present and referenced correctly before exporting.
