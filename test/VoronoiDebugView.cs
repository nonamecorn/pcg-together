using Godot;
using PCGTogether.lvls.gen;

namespace PCGTogether.test;

/// Node2D helper that renders Voronoi debug output (with optional traversal overlay) to the canvas.
public partial class VoronoiDebugView : Node2D {
    public enum DebugMode {
        Voronoi,
        Traversal
    }


    [Export] public NodePath VoronoiNodePath;
    [Export] public NodePath SpriteNodePath;
    [Export(PropertyHint.Enum, "Voronoi,Traversal")]
    public DebugMode Mode = DebugMode.Voronoi;
    [Export] public bool GenerateOnReady = true;
    [Export] public float NeighborCoverage = 0.75f;
    [Export] public int TraversalSeed = 42;
    [Export(PropertyHint.Range, "0,1,0.01")] public float ConnectionDistributionScaling = 1f;
    [Export] public int ConnectionStroke = 2;
    [Export] public int ConnectionPointRadius = 3;

    private Voronoi _voronoi;
    private VoronoiTraversalGraph _traversal;
    private ImageTexture _texture;
    private Sprite2D _sprite;

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
    public void Refresh() {
        if (_voronoi == null) {
            return;
        }

        _voronoi.Generate();
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
                _traversal = VoronoiTraversal.Build(_voronoi.Diagram, NeighborCoverage, TraversalSeed, true, ConnectionDistributionScaling);
                _texture = ImageTexture.CreateFromImage(
                    _traversal.DrawDebugImageWithConnections(_voronoi.EdgeColor, _voronoi.SeedColor, null,
                        _voronoi.StrokeWidth, ConnectionStroke, ConnectionPointRadius)
                );
                break;
        }
        _sprite.Texture = _texture;
        QueueRedraw();
    }

    public override void _Draw() {
        if (_texture != null) {
            DrawTexture(_texture, Vector2.Zero);
        }
    }
}
