// Defines an actor's visible combat silhouette independently from movement collision.
// Points and aiming position are relative to the artwork's ground anchor.
using Godot;
using System;

[Tool, GlobalClass]
public partial class CombatHitboxDefinition : Resource
{
    #region Configuration
    [Export] public Vector2[] Points { get; set; } =
    {
        new(-12, -48), new(12, -48),
        new(12, -2), new(-12, -2)
    };

    [Export] public Vector2 AimPoint { get; set; } = new(0, -28);
    #endregion

    #region Validation
    // =========================================================
    // Require usable finite silhouette coordinates.
    public void Validate()
    {
        if (Points == null || Points.Length < 3)
            throw new InvalidOperationException(
                "Combat hitbox requires at least three convex polygon points.");

        foreach (Vector2 point in Points)
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
                throw new InvalidOperationException(
                    "Combat hitbox points must be finite.");

        if (!float.IsFinite(AimPoint.X) || !float.IsFinite(AimPoint.Y))
            throw new InvalidOperationException(
                "Combat hitbox aiming position must be finite.");
    }
    #endregion
}