// Represents a solid ore deposit with independent remaining quantity and mining work.
// Requests collection before consuming ore, so rejected rewards remain in the deposit.
using Godot;
using System;
using System.Threading.Tasks;

public partial class OreDeposit : Obstacle, IMiningTarget
{
    #region Configuration
    [ExportGroup("Deposit")]
    [Export] public OreDefinition Definition { get; set; }
    [Export] public float InstanceSize { get; set; } = 1f;
    #endregion

    #region State
    public int RemainingUnits { get; private set; }
    private float _work;
    #endregion

    #region Configuration
    // =========================================================
    // Configure footprint, shadow dimensions, and instance extraction state.
    protected override void ConfigureInstance()
    {
        if (Definition == null || Definition.YieldItem == null)
            throw new InvalidOperationException(
                "OreDeposit requires an OreDefinition with a yield item.");

        InstanceSize = Mathf.Max(0.1f, InstanceSize);
        Footprint = Definition.Footprint * InstanceSize;
        Height = Definition.Height * InstanceSize;
        VisualOverride = Definition.Visual;
        RemainingUnits = Mathf.Max(1, Definition.TotalUnits);
    }
    #endregion

    #region Extraction
    // =========================================================
    // Build mining work and consume a batch only after successful collection.
    public bool Mine(float power, Func<ItemDefinition, int, bool> collect)
    {
        if (IsQueuedForDeletion() || RemainingUnits <= 0 ||
            power <= 0f || collect == null)
            return false;

        float required = Mathf.Max(0.1f, Definition.WorkPerBatch);
        _work = Mathf.Min(required, _work + power);
        if (_work < required) return true;

        int units = Math.Min(
            RemainingUnits, Mathf.Max(1, Definition.UnitsPerBatch));
        if (!collect(Definition.YieldItem, units)) return false;

        _work = 0f;
        RemainingUnits -= units;

        if (RemainingUnits == 0)
        {
            CollisionLayer = 0;
            QueueFree();
        }

        return true;
    }
    #endregion

    #region Artwork
    // =========================================================
    // Attach an imported visual or the shared disk-cached fallback texture.
    protected override async Task AttachArtworkAsync()
    {
        ImageTexture texture = await OreArtwork.Get(this, Definition);
        if (!IsInsideTree() || IsQueuedForDeletion()) return;

        TerrainVisual visual = TerrainVisual.Attach(
            this,
            new Rect2(Vector2.Zero, new Vector2(
                Definition.BakeSize.X, Definition.BakeSize.Y)),
            -Definition.BakeAnchor, Vector2.One, false,
            VisualOverride, texture);

        visual.Scale = Vector2.One * InstanceSize;
    }
    #endregion
}