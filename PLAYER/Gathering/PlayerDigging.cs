// Resolves short-range shovel targeting against the visible terrain surface.
// Runs only on weapon actions and reuses its obstruction query.
using Godot;

public partial class PlayerDigging : Node
{
    #region State
    private Player _player;
    private TerrainElevation _elevation;
    private PhysicsRayQueryParameters2D _query;
    #endregion

    #region Lifecycle
    // =========================================================
    // Cache the owning actor and a reusable solid-obstruction query.
    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        _query = new PhysicsRayQueryParameters2D
        {
            CollisionMask = 9,
            HitFromInside = true
        };
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release the native query when the player is removed.
    public override void _ExitTree()
    {
        _query?.Dispose();
    }
    #endregion

    #region Digging
    // =========================================================
    // Convert the cursor to logical ground and dig only within shovel reach.
    public bool Dig(DiggingAttack attack)
    {
        GroundResourceWorld resources = GroundResourceWorld.Find(this);
        if (resources == null) return false;

        _elevation ??= GetTree().GetFirstNodeInGroup("terrain_elevation")
            as TerrainElevation;
        Vector2 cursor = _player.GetGlobalMousePosition();
        Vector2 target = cursor;

        if (_elevation != null)
        {
            for (int i = 0; i < 16; i++)
            {
                Vector2 next = cursor + Vector2.Down *
                    _elevation.SampleWorldHeight(target);
                if (next.DistanceSquaredTo(target) < 0.0001f)
                {
                    target = next;
                    break;
                }
                target = next;
            }
        }

        float reach = Mathf.Max(8f, attack.Reach);
        if (_player.GlobalPosition.DistanceSquaredTo(target) > reach * reach)
            return false;

        _query.From = _player.GlobalPosition;
        _query.To = target;
        if (_player.GetWorld2D().DirectSpaceState.IntersectRay(_query).Count > 0)
            return false;

        return resources.Dig(
            target, attack.ShovelStrength, attack.DiggingPower);
    }
    #endregion
}