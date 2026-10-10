// Defines mining eligibility separately from extraction speed.
// Ore targets receive a drop callback instead of accessing player inventory.
using Godot;
using System;
using System.Collections.Generic;

public interface IMiningTarget
{
    int RequiredStrength { get; }
    bool Mine(float power, Func<IReadOnlyList<HarvestDropPlan.Reward>, bool> drop);
}

[Tool, GlobalClass]
public partial class MiningAttack : AttackDefinition
{
    #region Mining
    [ExportGroup("Mining")]
    [Export] public float Range { get; set; } = 180f;
    [Export] public int MiningStrength { get; set; } = 1;
    [Export] public float MiningPower { get; set; } = 6f;
    #endregion

    #region Appearance
    [ExportGroup("Beam")]
    [Export] public Color Tint { get; set; } = new("#83f5ff");
    #endregion

    #region Delivery
    // =========================================================
    // Dispatch through the owning actor's reusable mining emitter.
    public override void Deliver(Weapon weapon, Vector2 origin, Vector2 direction)
    {
        weapon.Source.GetNodeOrNull<MiningEmitter>(
            "Systems/MiningEmitter")?.Emit(this, direction);
    }
    #endregion
}
