// Represents a mineable ore deposit with shared replacement artwork.
// Selects stable PNG variations and only bakes artwork when needed.
using Godot;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public partial class OreDeposit : Obstacle, IMiningTarget
{
	#region Configuration
	[ExportGroup("Deposit")]
	[Export] public OreDefinition Definition { get; set; }
	[Export] public float InstanceSize { get; set; } = 1f;

	[ExportGroup("Artwork Variation")]
	// -1 selects a stable variation from the deposit's world position.
    // 0 and above select a particular image from the sorted PNG folder.
    [Export(PropertyHint.Range, "-1,255,1,or_greater")]
    public int ArtworkVariant { get; set; } = -1;
    #endregion

    #region State
    public int RemainingUnits { get; private set; }
    public int RequiredStrength => Definition.RequiredMiningStrength;

    private float _work;
    private OreBatchPlan _plan;
    private IReadOnlyList<HarvestDropPlan.Reward> _prepared;
    private uint _worldSeed;
    private ResourceChanges _changes;
    #endregion

    #region Setup
    // =========================================================
	// Resolve the mining reward and configure the deposit's physical dimensions.
	protected override void ConfigureInstance()
	{
		if (Definition == null)
			throw new InvalidOperationException(
				"OreDeposit requires an OreDefinition.");

        ResourceWorld resources = ResourceWorld.Find(this);
        if (resources == null)
            throw new InvalidOperationException("OreDeposit requires ResourceWorld.");
        _plan = new OreBatchPlan(Definition, resources.Catalog);
        Node world = WorldConfig.Find(this).GetParent();
        _worldSeed = world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed;

		InstanceSize = Mathf.Max(0.1f, InstanceSize);
		Footprint = Definition.Footprint * InstanceSize;
		Height = Definition.Height * InstanceSize;
		VisualOverride = Definition.Visual;
        RemainingUnits = Definition.TotalUnits;
        _changes = ResourceChanges.Ensure(this);
        _changes.BindScene(this, Definition, "ore");
        ResourceChangeData saved = _changes.Get(this);
        if (saved != null)
        {
            RemainingUnits = Math.Min(RemainingUnits, saved.Units);
            _work = Mathf.Min(Definition.WorkPerBatch, saved.Work);
            if (saved.Depleted || RemainingUnits == 0) QueueFree();
        }
	}
	#endregion

	#region Mining
	// =========================================================
	// Consume an ore batch only after its reward has been accepted.
	public bool Mine(
		float power, Func<IReadOnlyList<HarvestDropPlan.Reward>, bool> collect)
	{
		if (IsQueuedForDeletion() || RemainingUnits <= 0 ||
			power <= 0f || !float.IsFinite(power) || collect == null)
			return false;

		float required = Definition.WorkPerBatch;
        _work = Mathf.Min(required, _work + power);
        _changes.Record(this, _work, RemainingUnits);

		if (_work < required) return true;

		int units = Math.Min(
			RemainingUnits, Definition.UnitsPerBatch);

		        _prepared ??= _plan.Prepare(_worldSeed, _changes.IdentityFor(this),
            Definition.ResourcePath, Definition.TotalUnits, RemainingUnits, Definition.UnitsPerBatch);
        if (!collect(_prepared)) return false;
        _prepared = null;

		_work = 0f;
        RemainingUnits -= units;
        _changes.Record(this, _work, RemainingUnits, RemainingUnits == 0);

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
	// Attach imported artwork immediately, or request the cached fallback bake.
	protected override async Task AttachArtworkAsync()
	{
		int variant = SelectArtworkVariant();

		TerrainVisual visual = TerrainVisual.AttachCustom(
			this, false, VisualOverride, variant);

		if (visual == null)
		{
			ImageTexture texture = await OreArtwork.Get(this, Definition);
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

			visual = TerrainVisual.Attach(
				this,
				new Rect2(
					Vector2.Zero,
					new Vector2(Definition.BakeSize.X, Definition.BakeSize.Y)),
				-Definition.BakeAnchor,
				Vector2.One,
				false,
				VisualOverride,
				texture,
				imageVariant: variant);
		}

		visual.Scale = Vector2.One * InstanceSize;
	}

	// =========================================================
	// Choose a repeatable image without storing or changing shared resource data.
	private int SelectArtworkVariant()
	{
		if (ArtworkVariant >= 0) return ArtworkVariant;

		int count = VisualOverride?.ImageVariantCount ?? 0;
		if (count <= 1) return 0;

		uint hash = IsoGrid.Hash(
			Mathf.RoundToInt(GlobalPosition.X),
			Mathf.RoundToInt(GlobalPosition.Y),
			0x0AE641u);

		return (int)(hash % (uint)count);
	}
	#endregion
}
