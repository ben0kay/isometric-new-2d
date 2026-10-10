// Main Inspector-editable audio limits, world culling, fade and mixer preferences.
// Individual sound definitions own their own distance, priority and playback mode.
using Godot;

[Tool, GlobalClass]
public partial class AudioSettings : Resource
{
    #region Global mixing
    [ExportGroup("Volume (dB)")]
    [Export(PropertyHint.Range, "-60,6,0.5")] public float MasterDb { get; set; } = 0f;
    [Export(PropertyHint.Range, "-60,6,0.5")] public float MusicDb { get; set; } = 0f;
    [Export(PropertyHint.Range, "-60,6,0.5")] public float AmbienceDb { get; set; } = 0f;
    [Export(PropertyHint.Range, "-60,6,0.5")] public float WorldSfxDb { get; set; } = 0f;
    [Export(PropertyHint.Range, "-60,6,0.5")] public float UiDb { get; set; } = 0f;
    [Export(PropertyHint.Range, "-60,6,0.5")] public float VoiceDb { get; set; } = 0f;
    #endregion

    #region Playback limits
    [ExportGroup("One-Shot Budgets")]
    [Export(PropertyHint.Range, "1,64,1")] public int MaximumWorldOneShots { get; set; } = 24;
    [Export(PropertyHint.Range, "1,16,1")] public int MaximumUiOneShots { get; set; } = 4;
    [Export(PropertyHint.Range, "1,8,1")] public int MaximumVoiceOneShots { get; set; } = 1;
    [ExportGroup("Loop Budgets")]
    [Export(PropertyHint.Range, "1,64,1")] public int MaximumWorldLoops { get; set; } = 12;
    [Export(PropertyHint.Range, "1,32,1")] public int MaximumAmbientLoops { get; set; } = 6;
    [Export(PropertyHint.Range, "1,1024,1")] public int MaximumRegisteredEmitters { get; set; } = 256;
    [Export(PropertyHint.Range, "0.05,2,0.05")] public float EmitterCheckSeconds { get; set; } = 0.25f;
    #endregion

    #region World culling
    [ExportGroup("Culling")]
    [Export] public bool DistanceCulling { get; set; } = true;
    [Export] public bool ScreenCulling { get; set; } = true;
    [Export(PropertyHint.Range, "0,500,10")] public float ScreenMarginPixels { get; set; } = 120f;
    [Export(PropertyHint.Range, "0,1000,10")] public float LoopResumeMargin { get; set; } = 80f;
    [Export] public bool PauseWorldAudioWithGame { get; set; } = true;
    #endregion

    #region Future companion mixing
    [ExportGroup("Companion Voice")]
    [Export(PropertyHint.Range, "0,12,1")] public int VoiceQueueLimit { get; set; } = 4;
    [Export] public bool DuckMusicDuringVoice { get; set; } = true;
    [Export(PropertyHint.Range, "-30,0,1")] public float VoiceMusicDuckDb { get; set; } = -8f;
    #endregion
}
