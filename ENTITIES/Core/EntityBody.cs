// Shared actor foundation used by robots and wildlife during migration.
// Owns cached health, motor and layer-specific navigation references.
// Behaviour components remain coordinated by the existing actor scripts.
using Godot;

public abstract partial class EntityBody : CharacterBody2D
{
    #region Shared Components
    public Health Health { get; protected set; }
    public EnemyMotor Motor { get; protected set; }

    public virtual double NavigationPathInterval => 0.45;

    private WorldLayerMember _layerMember;
    private WorldNavigation _cachedNavigation;
    private WorldLayer _navigationLayer;
    private bool _navigationResolved;
    #endregion

    #region Navigation
    // =========================================================
    // Reuse navigation while reading layer changes from the cached member.
    public WorldNavigation Navigation
    {
        get
        {
            if (!IsInsideTree() || IsQueuedForDeletion())
                return null;

            if (!GodotObject.IsInstanceValid(_layerMember))
            {
                _layerMember = WorldLayerMember.Attach(
                    this, WorldLayerMember.For(this));
                _navigationResolved = false;
            }

            WorldLayer layer = _layerMember.Layer;

            if (!_navigationResolved ||
                layer != _navigationLayer ||
                !GodotObject.IsInstanceValid(_cachedNavigation) ||
                _cachedNavigation.IsQueuedForDeletion())
            {
                _cachedNavigation = WorldNavigation.For(this);
                _navigationLayer = layer;
                _navigationResolved =
                    GodotObject.IsInstanceValid(_cachedNavigation);
            }

            return _cachedNavigation;
        }
    }
    #endregion

    #region Initialization
    // =========================================================
    // Cache shared components once after the actor's children are ready.
    protected void BindSharedComponents()
    {
        Health = GetNode<Health>("Systems/Health");
        Motor = GetNode<EnemyMotor>("Systems/Motor");
        _layerMember = WorldLayerMember.Attach(
            this, WorldLayerMember.For(this));

        _cachedNavigation = null;
        _navigationResolved = false;
    }
    #endregion
}