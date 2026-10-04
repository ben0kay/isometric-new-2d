// Evaluates physical capacity and encumbrance independently from slot storage.
// Receives numeric limits so other container systems can reuse capacity checks.
using Godot;

[Tool, GlobalClass]
public partial class CarryingRules : Resource
{
    #region Movement
    [ExportGroup("Movement Penalty")]
    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float MinimumSpeedFactor { get; set; } = 0.55f;
    [Export(PropertyHint.Range, "0.1,4,0.1")]
    public float PenaltyExponent { get; set; } = 1f;
    #endregion

    #region Without Backpack
    [ExportGroup("Without Backpack")]
    [Export] public float ComfortableWeightKg { get; set; } = 8f;
    [Export] public float MaximumWeightKg { get; set; } = 16f;
    #endregion

    #region Capacity
    // =========================================================
    // Reject a proposed load that exceeds weight or volume capacity.
    public bool Allows(
        float weight, float volume, float maxWeight, float maxVolume,
        out string reason)
    {
        if (weight > Mathf.Max(0f, maxWeight) + 0.0001f)
        {
            reason = "Too heavy — maximum carrying weight reached.";
            return false;
        }

        if (volume > Mathf.Max(0f, maxVolume) + 0.0001f)
        {
            reason = "Backpack volume is full.";
            return false;
        }

        reason = "";
        return true;
    }

    // =========================================================
    // Gradually reduce speed above comfortable weight.
    public float SpeedFactor(float weight, float comfortable, float maximum)
    {
        comfortable = Mathf.Max(0f, comfortable);
        maximum = Mathf.Max(comfortable + 0.01f, maximum);
        float burden = Mathf.Clamp(
            (weight - comfortable) / (maximum - comfortable), 0f, 1f);

        return Mathf.Lerp(
            1f, Mathf.Clamp(MinimumSpeedFactor, 0.1f, 1f),
            Mathf.Pow(burden, Mathf.Max(0.1f, PenaltyExponent)));
    }
    #endregion
}