using Godot;
using PCGTogether.lvls.gen;

namespace PCGTogether.test;

/// Node2D helper that renders Voronoi debug output (with optional traversal overlay) to the canvas.
public partial class VoronoiDebugView : Node2D {
    public enum DebugMode {
        Voronoi,
        Traversal
    }


    /// Path to the Voronoi generator node.
    [Export] public NodePath VoronoiNodePath;
    /// Path to the Sprite2D that displays the image.
    [Export] public NodePath SpriteNodePath;
    [Export(PropertyHint.Enum, "Voronoi,Traversal")]
    public DebugMode Mode = DebugMode.Voronoi;
    /// If true, regenerate on _Ready.
    [Export] public bool GenerateOnReady = false;
    /// Target neighbour coverage for traversal display.
    [Export] public float NeighborCoverage = 0.5f;
    /// Traversal seed override; 0 means derived.
    [Export] public int TraversalSeed = 0;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ConnectionDistributionScaling = 0.75f;
    /// Stroke width for connection lines.
    [Export] public int ConnectionStroke = 2;
    /// Radius for connection points.
    [Export] public int ConnectionPointRadius = 3;

    private Voronoi _voronoi;
    private VoronoiTraversalGraph _traversal;
    private ImageTexture _texture;
    private Sprite2D _sprite;

    /// Godot lifecycle: resolves node references and optionally refreshes debug output.
    public override void _Ready() {
        _voronoi = GetNodeOrNull<Voronoi>(VoronoiNodePath);
        _sprite = GetNodeOrNull<Sprite2D>(SpriteNodePath);
        if (_voronoi == null || _sprite == null) {
            GD.PushWarning("VoronoiDebugView._Ready(): didn't find nodes.");
            return;
        }
        _sprite.Texture = _texture;
        if (GenerateOnReady) {
            Refresh();
        }
    }

    /// Regenerates the Voronoi diagram and (optionally) traversal graph, then rebuilds the texture.
    /// <param name="traversal">Optional traversal graph to render.</param>
    public void Refresh(VoronoiTraversalGraph traversal = null) {
        if (_voronoi == null) {
            return;
        }

        // var traversalSeedOverride = TraversalSeed == 0 ? (int?)null : TraversalSeed;
        // var seeds = new VoronoiSeedChain(_voronoi.Seed, traversalSeedOverride: traversalSeedOverride);

        // _voronoi.Generate(seeds);
        if (_voronoi.Diagram == null) {
            return;
        }

        switch (Mode) {
            case DebugMode.Voronoi:
                _traversal = null;
                _texture = ImageTexture.CreateFromImage(
                    _voronoi.Diagram.DrawDebugImage(_voronoi.CanvasSize, _voronoi.EdgeColor, _voronoi.SeedColor,
                        _voronoi.StrokeWidth)
                );
                break;
            case DebugMode.Traversal:
                // _traversal = VoronoiTraversal.Build(_voronoi.Diagram, NeighborCoverage, seeds.TraversalSeed, true, ConnectionDistributionScaling);
                _traversal = traversal;
                _texture = ImageTexture.CreateFromImage(
                    _traversal.DrawDebugImageWithConnections(_voronoi.EdgeColor, _voronoi.SeedColor, null,
                        _voronoi.StrokeWidth, ConnectionStroke, ConnectionPointRadius)
                );
                break;
        }
        _sprite.Texture = _texture;
        QueueRedraw();
    }

    /// Draws the current texture to the canvas.
    public override void _Draw() {
        if (_texture != null) {
            DrawTexture(_texture, Vector2.Zero);
        }
    }
}
