// Standalone synthetic-tone tests for the game audio foundation.
// F9 cycles demonstrations without requiring WAV/OGG files in the repository.
using Godot;
using System;

public enum AudioTestScenario { CycleExamples, PriorityBurst, Intermittent, VoiceQueue }

public partial class AudioTester : Node
{
    #region Configuration
    [ExportGroup("Preview")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Key PreviewKey { get; set; } = Key.F9;
    [Export] public AudioTestScenario Scenario { get; set; }
        = AudioTestScenario.CycleExamples;
    #endregion

#if DEBUG
    #region State
    private AudioManager _audio;
    private Node2D _source;
    private int _step;
    private bool _loopOn, _intermittentOn;
    private const string ClickId = "debug_audio_click";
    private const string WorldId = "debug_audio_world";
    private const string HumId = "debug_audio_hum";
    private const string BirdId = "debug_audio_bird";
    private const string VoiceId = "debug_audio_voice";
    #endregion

    #region Lifecycle
    // =========================================================
    // Register in-memory 16-bit tone samples; no production files are changed.
    public override void _Ready()
    {
        _audio = AudioManager.Find(this);
        if (_audio == null) return;

        _source = new Node2D { Name = "AudioTestSource" };
        AddChild(_source);
        AudioCatalog catalog = _audio.Catalog;

        Register(catalog, ClickId, 680f, GameAudioMode.OneShot,
            GameAudioCategory.UI, false, 50);
        Register(catalog, WorldId, 420f, GameAudioMode.OneShot,
            GameAudioCategory.WorldSfx, true, 50);
        Register(catalog, HumId, 140f, GameAudioMode.Loop,
            GameAudioCategory.WorldSfx, true, 35);
        Register(catalog, BirdId, 950f, GameAudioMode.Intermittent,
            GameAudioCategory.Ambience, true, 15);
        Register(catalog, VoiceId, 535f, GameAudioMode.OneShot,
            GameAudioCategory.Voice, false, 95);
    }

    // =========================================================
    // Release the owned test emitter and all active test registrations.
    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(_audio)) return;
        _audio.StopEmitter(HumId, _source);
        _audio.StopEmitter(BirdId, _source);
    }
    #endregion

    #region Input
    // =========================================================
    // Exercise each audible audio scenario by pressing F9 in the game.
    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (!Enabled || input is not InputEventKey key ||
            !key.Pressed || key.Echo || key.PhysicalKeycode != PreviewKey)
            return;

        _audio = AudioManager.Find(this);
        if (_audio == null)
        {
            GD.PushWarning("AudioTester: missing AudioManager.");
            return;
        }
        Camera2D camera = GetViewport().GetCamera2D();
        Vector2 centre = camera?.GetScreenCenterPosition() ?? Vector2.Zero;

        switch (Scenario)
        {
            case AudioTestScenario.PriorityBurst:
                for (int i = 0; i < 36; i++)
                    _audio.PlayAt(WorldId,
                        centre + new Vector2((i % 9 - 4) * 90f, 0f));
                GD.Print("AudioTester: sent 36 one-shot requests into the pooled budget.");
                break;
            case AudioTestScenario.Intermittent:
                _source.GlobalPosition = centre + new Vector2(180f, 0f);
                _intermittentOn = !_intermittentOn;
                if (_intermittentOn) _audio.StartEmitter(BirdId, _source);
                else _audio.StopEmitter(BirdId, _source);
                GD.Print("AudioTester: intermittent emitter = " + _intermittentOn);
                break;
            case AudioTestScenario.VoiceQueue:
                CompanionVoice voice = _audio.GetNode<CompanionVoice>("CompanionVoice");
                voice.Say(VoiceId);
                break;
            default:
                switch (_step++ % 4)
                {
                    case 0:
                        _audio.Play(ClickId);
                        GD.Print("AudioTester: UI click, centre speakers.");
                        break;
                    case 1:
                        _audio.PlayAt(WorldId, centre + new Vector2(-240f, 0f));
                        GD.Print("AudioTester: world tone to LEFT.");
                        break;
                    case 2:
                        _audio.PlayAt(WorldId, centre + new Vector2(240f, 0f));
                        GD.Print("AudioTester: world tone to RIGHT.");
                        break;
                    case 3:
                        _source.GlobalPosition = centre + new Vector2(100f, 0f);
                        _loopOn = !_loopOn;
                        if (_loopOn) _audio.StartEmitter(HumId, _source);
                        else _audio.StopEmitter(HumId, _source);
                        GD.Print("AudioTester: machine loop = " + _loopOn);
                        break;
                }
                break;
        }
        GetViewport().SetInputAsHandled();
    }
    #endregion

    #region Test clips
    // =========================================================
    // Add only transient debug definitions, leaving every .tres unchanged.
    private static void Register(AudioCatalog catalog, string id, float hz,
        GameAudioMode mode, GameAudioCategory category,
        bool spatial, int priority)
    {
        if (catalog.Find(id) != null) return;
        bool loop = mode == GameAudioMode.Loop;
        catalog.Sounds.Add(new AudioDefinition
        {
            Id = id, Stream = Tone(hz, loop),
            Mode = mode, Category = category,
            Positional = spatial, MaxDistance = 850f,
            Priority = priority, CooldownSeconds = 0f,
            MaximumInstances = 30,
            FadeInSeconds = 0.25f, FadeOutSeconds = 0.3f,
            IntervalMinSeconds = 2f, IntervalMaxSeconds = 4f
        });
    }

    // =========================================================
    // Generate 16-bit mono PCM in memory, with loop points for humming.
    private static AudioStreamWav Tone(float frequency, bool loop)
    {
        const int rate = 22050;
        int samples = loop ? rate : rate / 3;
        byte[] data = new byte[samples * 2];

        for (int i = 0; i < samples; i++)
        {
            double envelope = loop ? 1d :
                Math.Min(1d, Math.Min(i / 200d, (samples - i) / 300d));
            short value = (short)(Math.Sin(i * Math.PI * 2d * frequency / rate) *
                6500d * envelope);
            data[i * 2] = (byte)(value & 0xff);
            data[i * 2 + 1] = (byte)((value >> 8) & 0xff);
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = (AudioStreamWav.FormatEnum)1,
            MixRate = rate, Stereo = false,
            LoopMode = (AudioStreamWav.LoopModeEnum)(loop ? 1 : 0),
            LoopBegin = 0, LoopEnd = samples
        };
    }
    #endregion
#endif
}
