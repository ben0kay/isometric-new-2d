// Displays a real startup progress indicator over the world until it
// can safely accept input. This is presentation-only: it never pauses,
// advances or alters world generation or campaign restoration.
using Godot;
using System;

public partial class WorldLoadingScreen : CanvasLayer
{
    #region Configuration
    [Export] public LoadingScreenSettings Settings { get; set; }
    #endregion

    #region State
    private ChunkController _chunks;
    private WorldLoadingVisual _visual;
    private float _targetProgress, _shownProgress, _scan, _fadeTime;
    private float _refreshTimer;
    private bool _readyToPlay, _indeterminate = true;
    private string _stage = "INITIALIZING PLANETARY SYSTEMS";
    private string _details = "Preparing startup services...";
    #endregion

    #region Lifecycle
    // =========================================================
    // Scene runs independently of the intentionally frozen WorldObjects root.
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _visual = GetNode<WorldLoadingVisual>("View");

        if (Settings == null || !Settings.Enabled)
        {
            QueueFree();
            return;
        }

        try { Settings.Validate(); }
        catch (Exception error)
        {
            GD.PushWarning($"World loading screen disabled: {error.Message}");
            QueueFree();
            return;
        }

        _chunks = GetNodeOrNull<ChunkController>("../Systems/ChunkController");
        if (_chunks == null)
        {
            GD.PushWarning("World loading screen requires a sibling Systems/ChunkController.");
            QueueFree();
            return;
        }

        _visual.Configure(Settings);
        _refreshTimer = 0f;
        UpdateStatus();
        SetProcess(true);
    }

    // =========================================================
    // Only animate visuals; never modify WorldReady or player process mode.
    public override void _Process(double delta)
    {
        float dt = (float)Math.Min(delta, 0.25);
        _scan = Mathf.PosMod(_scan + dt * Settings.ScanSpeed, 1f);
        _refreshTimer -= dt;
        if (_refreshTimer <= 0f)
        {
            _refreshTimer = Settings.StatusRefreshSeconds;
            UpdateStatus();
        }

        float smoothing = 1f - Mathf.Exp(-dt * Settings.ProgressCatchupSpeed);
        _shownProgress = Mathf.Lerp(_shownProgress, _targetProgress, smoothing);
        if (Mathf.Abs(_shownProgress - _targetProgress) < 0.001f)
            _shownProgress = _targetProgress;

        _visual.UpdateDisplay(_shownProgress, _stage,
            _details, _indeterminate, _scan);

        // After gameplay is ready, allow the bar to reach 100 before fading.
        if (!_readyToPlay || _shownProgress < 0.999f) return;

        if (Settings.FadeOutSeconds <= 0f)
        {
            QueueFree();
            SetProcess(false);
            return;
        }

        _fadeTime += dt;
        float alpha = 1f - Mathf.Clamp(_fadeTime / Settings.FadeOutSeconds, 0f, 1f);
        _visual.Modulate = new Color(1f, 1f, 1f, alpha);

        if (alpha <= 0f)
        {
            QueueFree();
            SetProcess(false);
        }
    }
    #endregion

    #region Progress Sampling
    // =========================================================
    // Read the activation buffer actually required for player movement.
    // CampaignSession is the final gate for restored saves and world clock.
    private void UpdateStatus()
    {
        _chunks.GetStartupProgress(out int ready, out int required,
            out ChunkBuildStage building);

        bool campaignLoading = CampaignSession.IsLoadingFor(this);
        _readyToPlay = _chunks.WorldReady && !campaignLoading;
        _indeterminate = !_chunks.StartupArtworkReady || required == 0;

        if (_readyToPlay)
        {
            _targetProgress = 1f;
            _stage = "ENVIRONMENT READY";
            _details = "LANDING ZONE ONLINE";
            _indeterminate = false;
            return;
        }

        if (!_chunks.StartupArtworkReady)
        {
            _targetProgress = 0f;
            _stage = "PREPARING CACHED ARTWORK";
            _details = "Loading terrain, vegetation and character atlases";
            return;
        }

        if (!_chunks.WorldReady)
        {
            _targetProgress = required > 0
                ? Mathf.Min(0.99f, ready / (float)required) : 0f;
            _stage = StageName(building);
            _details = required > 0
                ? $"{ready} / {required} required chunks ready"
                : "Locating initial terrain coverage";
            return;
        }

        // Chunk generation is complete, but saved-player restoration may continue.
        _targetProgress = 0.99f;
        _stage = "RESTORING CAMPAIGN";
        _details = "Restoring player state and synchronizing world systems";
    }

    // =========================================================
    // Human-friendly text instead of enum names like Queued and Prepared.
    private static string StageName(ChunkBuildStage stage) => stage switch
    {
        ChunkBuildStage.Queued => "SURVEYING PLANETARY DATA",
        ChunkBuildStage.TerrainData => "CALCULATING TERRAIN",
        ChunkBuildStage.Prepared => "PREPARING SURFACE FEATURES",
        ChunkBuildStage.TerrainUpload => "ASSEMBLING TERRAIN GEOMETRY",
        ChunkBuildStage.TerrainCollision => "BUILDING TERRAIN COLLISION",
        ChunkBuildStage.Rocks => "DISTRIBUTING MINERAL DEPOSITS",
        ChunkBuildStage.Trees => "ESTABLISHING FOREST BIOMES",
        ChunkBuildStage.Plants => "SEEDING ALIEN VEGETATION",
        ChunkBuildStage.Grass => "POPULATING GROUND COVER",
        ChunkBuildStage.Ready => "FINALIZING LANDING ZONE",
        _ => "PREPARING PLANETARY ENVIRONMENT"
    };
    #endregion
}
