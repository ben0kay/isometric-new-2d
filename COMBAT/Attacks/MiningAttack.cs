// Defines mining attack data and the shared mining-target contract.
// Targets receive a collection callback rather than depending on player storage.
using Godot;
using System;

public interface IMiningTarget
{
    // =========================================================
    // Apply extraction work and request collection when a batch is ready.
    bool Mine(float power, Func<ItemDefinition, int, bool> collect);
}

[GlobalClass]
public partial class MiningAttack : AttackDefinition
{
    #region Mining
    [ExportGroup("Mining")]
    [Export] public float Range { get; set; } = 180f;
    [Export] public float MiningPower { get; set; } = 6f;
    #endregion

    #region Appearance
    [ExportGroup("Beam")]
    [Export] public Color Tint { get; set; } = new("#83f5ff");
    #endregion

    #region Delivery
    // =========================================================
    // Dispatch mining through the owning actor's reusable emitter.
    public override void Deliver(Weapon weapon, Vector2 origin, Vector2 direction)
    {
        weapon.Source.GetNodeOrNull<MiningEmitter>(
            "Systems/MiningEmitter")?.Emit(this, direction);
    }
    #endregion
}