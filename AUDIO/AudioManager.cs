// World audio authority: pooled one-shots, positional stereo, priorities and owned emitters.
// Only registered nearby emitters get players; distant/unloaded chunks retain no audio nodes.
using Godot;
using System;
using System.Collections.Generic;

public partial class AudioManager : Node2D
{
    #region Configuration
    [ExportGroup("Resources")]
    [Export] public AudioSettings Settings { get; set; }
    [Export] public AudioCatalog Catalog { get; set; }

    public event Action<string> SoundFinished;
    #endregion

    #region Playback state
    private sealed class Slot
    {
        public bool Spatial, Active, Loop, Stopping;
        public string Id = "", EmitterKey = "";
        public int Priority;
        public GameAudioCategory Category;
        public ulong Sequence;
        public AudioStreamPlayer2D World;
        public AudioStreamPlayer Flat;
        public Tween Fade;
        public Node Player => Spatial ? World : Flat;
    }

    private sealed class Emitter
    {
        public string Key;
        public AudioDefinition Definition;
        public WeakReference<Node2D> Owner;
        public Slot Slot;
        public double NextPlay;
    }

    private readonly List<Slot> _slots = new();
    private readonly Dictionary<string, Emitter> _emitters = new();
    private readonly Dictionary<string, double> _lastPlay = new();
    private Camera2D _camera;
    private Node2D _player;
    private double _clock, _scanTimer;
    private ulong _sequence;
    private bool _wasPaused, _wasVoicePlaying;
    private readonly RandomNumberGenerator _rng = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the existing player camera and apply the central mixer settings.
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        AddToGroup("game_audio_manager");
        Settings ??= GD.Load<AudioSettings>("res://AUDIO/AudioSettings.tres") ?? new();
        Catalog ??= GD.Load<AudioCatalog>("res://AUDIO/AudioCatalog.tres") ?? new();
        _rng.Randomize();
        FindListener();
        ApplyBusVolumes();
    }

    // =========================================================
    // Maintain bounded registered emitters, owner cleanup and pause status.
    public override void _Process(double delta)
    {
        bool paused = GetTree().Paused && Settings.PauseWorldAudioWithGame;
        if (paused != _wasPaused)
        {
            _wasPaused = paused;
            foreach (Slot slot in _slots)
                if (slot.Active && slot.Category != GameAudioCategory.UI &&
                    slot.Category != GameAudioCategory.Voice)
                    SetStreamPaused(slot, paused);
        }

        if (paused) return;
        _clock += Math.Max(0d, delta);
        _scanTimer += Math.Max(0d, delta);
        if (_scanTimer < Mathf.Max(0.05f, Settings.EmitterCheckSeconds)) return;
        _scanTimer = 0d;

        if (!GodotObject.IsInstanceValid(_camera)) FindListener();
        foreach (Emitter emitter in new List<Emitter>(_emitters.Values))
            RefreshEmitter(emitter);

        bool voiceActive = _slots.Exists(x =>
            x.Active && x.Category == GameAudioCategory.Voice);
        if (voiceActive != _wasVoicePlaying)
        {
            _wasVoicePlaying = voiceActive;
            SetMusicDuck(voiceActive);
        }
    }

    // =========================================================
    // Discard all pool players, weak ownership registrations and music ducking.
    public override void _ExitTree()
    {
        foreach (Slot slot in _slots)
            if (slot.Active) Release(slot, false);
        _emitters.Clear();
        SetMusicDuck(false);
    }
    #endregion

    #region Lookup and one-shots
    // =========================================================
    // Resolve the active audio authority in the same viewport as the caller.
    public static AudioManager Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        foreach (Node node in context.GetTree().GetNodesInGroup("game_audio_manager"))
            if (node is AudioManager audio &&
                node.GetViewport() == context.GetViewport())
                return audio;
        return null;
    }

    // =========================================================
    // Non-positional playback (UI, music-style stingers, companion clips).
    public bool Play(string id)
    {
        AudioDefinition definition = Catalog?.Find(id);
        if (definition == null || definition.Positional) return false;
        return PlayDefinition(definition, Vector2.Zero, false);
    }

    // =========================================================
    // Play a one-shot sound at a fixed world position with distance culling.
    public bool PlayAt(string id, Vector2 worldPosition)
    {
        AudioDefinition definition = Catalog?.Find(id);
        if (definition == null) return false;
        return PlayDefinition(definition, worldPosition, true);
    }

    // =========================================================
    // Convenience for sounds emitted by existing moving gameplay actors.
    public bool PlayFrom(string id, Node2D source)
    {
        return source != null && GodotObject.IsInstanceValid(source) &&
            PlayAt(id, source.GlobalPosition);
    }

    // =========================================================
    // Apply per-ID cooldown, per-ID polyphony, distance rules and category caps.
    private bool PlayDefinition(AudioDefinition sound, Vector2 position, bool hasPosition)
    {
        if (sound == null || sound.Stream == null ||
            sound.Mode == GameAudioMode.Loop) return false;
        if (sound.Positional && (!hasPosition || !IsAudible(sound, position)))
            return false;
        if (_lastPlay.TryGetValue(sound.Id, out double last) &&
            _clock - last < Mathf.Max(0f, sound.CooldownSeconds))
            return false;

        Slot slot = Acquire(sound, false);
        if (slot == null) return false;
        StartSlot(slot, sound, position, false);
        _lastPlay[sound.Id] = _clock;
        return true;
    }
    #endregion

    #region Loop and intermittent registration
    // =========================================================
    // Start one unique loop or intermittent source per (owner, ID).
    // Null owner is allowed only for non-positional global ambience or music.
    public bool StartEmitter(string id, Node2D owner = null)
    {
        AudioDefinition sound = Catalog?.Find(id);
        if (sound == null || sound.Stream == null ||
            sound.Mode == GameAudioMode.OneShot) return false;
        if (sound.Positional && (owner == null ||
            !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree()))
            return false;

        string key = EmitterKey(id, owner);
        if (_emitters.ContainsKey(key) ||
            _emitters.Count >= Mathf.Max(1, Settings.MaximumRegisteredEmitters))
            return false;

        Emitter emitter = new()
        {
            Key = key,
            Definition = sound,
            Owner = owner == null ? null : new WeakReference<Node2D>(owner),
            NextPlay = _clock
        };
        _emitters.Add(key, emitter);
        RefreshEmitter(emitter);
        return true;
    }

    // =========================================================
    // Stop one machine/ambient emitter without touching others of the same ID.
    public bool StopEmitter(string id, Node2D owner = null)
    {
        string key = EmitterKey(id, owner);
        if (!_emitters.TryGetValue(key, out Emitter emitter)) return false;
        _emitters.Remove(key);
        if (emitter.Slot != null) FadeOut(emitter.Slot,
            emitter.Definition.FadeOutSeconds);
        emitter.Slot = null;
        return true;
    }

    // =========================================================
    // Release a disappearing chunk owner, or reacquire its player when audible.
    private void RefreshEmitter(Emitter emitter)
    {
        if (!_emitters.ContainsKey(emitter.Key)) return;

        Node2D owner = null;
        if (emitter.Owner != null &&
            (!emitter.Owner.TryGetTarget(out owner) ||
            !GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree() ||
            owner.IsQueuedForDeletion()))
        {
            _emitters.Remove(emitter.Key);
            if (emitter.Slot != null)
                FadeOut(emitter.Slot, emitter.Definition.FadeOutSeconds);
            return;
        }

        AudioDefinition sound = emitter.Definition;
        Vector2 position = owner?.GlobalPosition ?? Vector2.Zero;
        bool audible = !sound.Positional || IsAudible(sound, position,
            emitter.Slot != null ? Settings.LoopResumeMargin : 0f);

        if (!audible)
        {
            if (emitter.Slot != null)
            {
                FadeOut(emitter.Slot, sound.FadeOutSeconds);
                emitter.Slot = null;
            }
            return;
        }

        if (sound.Mode == GameAudioMode.Intermittent)
        {
            if (_clock >= emitter.NextPlay)
            {
                PlayDefinition(sound, position, owner != null);
                float min = Mathf.Max(0.1f, sound.IntervalMinSeconds);
                float max = Mathf.Max(min, sound.IntervalMaxSeconds);
                emitter.NextPlay = _clock + _rng.RandfRange(min, max);
            }
            return;
        }

        if (emitter.Slot == null)
        {
            Slot slot = Acquire(sound, true);
            if (slot == null) return;
            emitter.Slot = slot;
            slot.EmitterKey = emitter.Key;
            StartSlot(slot, sound, position, true);
            FadeIn(slot, sound.FadeInSeconds, sound.VolumeDb);
        }
        else if (emitter.Slot.World != null)
            emitter.Slot.World.GlobalPosition = position;
    }

    private static string EmitterKey(string id, Node2D owner) =>
        (owner == null ? "global" : owner.GetInstanceId().ToString()) + ":" + id;
    #endregion

    #region Pool and priority
    // =========================================================
    // Respect separate per-category and loop caps; steal only lower priorities.
    private Slot Acquire(AudioDefinition sound, bool loop)
    {
        int categoryCap = CategoryLimit(sound.Category, loop);
        List<Slot> same = _slots.FindAll(s => s.Active &&
            s.Category == sound.Category && s.Loop == loop);
        Slot candidate = null;

        int perId = _slots.FindAll(s => s.Active && s.Id == sound.Id).Count;
        if (perId >= Mathf.Max(1, sound.MaximumInstances))
            return null;

        if (same.Count >= categoryCap)
        {
            foreach (Slot slot in same)
                if (slot.Priority < sound.Priority &&
                    (candidate == null || slot.Priority < candidate.Priority ||
                    slot.Priority == candidate.Priority && slot.Sequence < candidate.Sequence))
                    candidate = slot;
            if (candidate == null) return null;
            Release(candidate);
        }

        Slot idle = _slots.Find(s => !s.Active && !s.Stopping &&
            s.Spatial == sound.Positional);
        if (idle != null) return idle;

        Slot created = new() { Spatial = sound.Positional };
        if (created.Spatial)
        {
            created.World = new AudioStreamPlayer2D { MaxPolyphony = 1 };
            AddChild(created.World);
            created.World.Finished += () => OnFinished(created);
        }
        else
        {
            created.Flat = new AudioStreamPlayer { MaxPolyphony = 1 };
            AddChild(created.Flat);
            created.Flat.Finished += () => OnFinished(created);
        }
        _slots.Add(created);
        return created;
    }

    // =========================================================
    // Use separated budgets so combat one-shots cannot evict machinery loops.
    private int CategoryLimit(GameAudioCategory category, bool loop)
    {
        int cap = category switch
        {
            GameAudioCategory.UI => Settings.MaximumUiOneShots,
            GameAudioCategory.Voice => Settings.MaximumVoiceOneShots,
            GameAudioCategory.Ambience => Settings.MaximumAmbientLoops,
            GameAudioCategory.Music => 1,
            _ => loop ? Settings.MaximumWorldLoops : Settings.MaximumWorldOneShots
        };
        return Mathf.Max(1, cap);
    }

    // =========================================================
    // Configure an idle pooled player and send it to the correct Godot bus.
    private void StartSlot(Slot slot, AudioDefinition sound, Vector2 position, bool loop)
    {
        StopTween(slot.Fade);
        slot.Active = true;
        slot.Stopping = false;
        slot.Id = sound.Id;
        slot.Category = sound.Category;
        slot.Priority = sound.Priority;
        slot.Loop = loop;
        slot.Sequence = ++_sequence;
        string bus = BusName(sound.Category);

        if (slot.Spatial)
        {
            slot.World.Stream = sound.Stream;
            slot.World.Bus = bus;
            slot.World.VolumeDb = sound.VolumeDb;
            slot.World.PitchScale = Mathf.Max(0.1f, sound.PitchScale);
            slot.World.MaxDistance = Mathf.Max(1f, sound.MaxDistance);
            slot.World.Attenuation = Mathf.Max(0.1f, sound.Attenuation);
            slot.World.GlobalPosition = position;
            slot.World.Play();
            slot.World.StreamPaused = _wasPaused &&
                sound.Category != GameAudioCategory.UI &&
                sound.Category != GameAudioCategory.Voice;
        }
        else
        {
            slot.Flat.Stream = sound.Stream;
            slot.Flat.Bus = bus;
            slot.Flat.VolumeDb = sound.VolumeDb;
            slot.Flat.PitchScale = Mathf.Max(0.1f, sound.PitchScale);
            slot.Flat.Play();
            slot.Flat.StreamPaused = _wasPaused &&
                sound.Category != GameAudioCategory.UI &&
                sound.Category != GameAudioCategory.Voice;
        }
    }

    // =========================================================
    // Return a slot to its pool and notify the companion queue of completion.
    private void Release(Slot slot, bool announce = true)
    {
        if (!slot.Active) return;
        StopTween(slot.Fade);
        string id = slot.Id, key = slot.EmitterKey;
        slot.Active = false;
        slot.Stopping = false;
        slot.EmitterKey = "";
        if (slot.World != null)
        {
            slot.World.Stop();
            slot.World.StreamPaused = false;
            slot.World.Stream = null;
        }
        if (slot.Flat != null)
        {
            slot.Flat.Stop();
            slot.Flat.StreamPaused = false;
            slot.Flat.Stream = null;
        }

        if (key.Length > 0 && _emitters.TryGetValue(key, out Emitter emitter) &&
            emitter.Slot == slot)
            emitter.Slot = null;

        if (announce) SoundFinished?.Invoke(id);
    }

    private void OnFinished(Slot slot)
    {
        if (slot.Active && !slot.Loop && !slot.Stopping) Release(slot);
    }

    // =========================================================
    // Stop the lowest-level player explicitly (e.g. superseded voice lines).
    public bool StopOneShot(string id)
    {
        Slot slot = _slots.Find(s => s.Active && !s.Loop && s.Id == id);
        if (slot == null) return false;
        Release(slot);
        return true;
    }
    #endregion

    #region Culling and effects
    // =========================================================
    // Resolve listener position and install one AudioListener2D on the camera.
    private void FindListener()
    {
        _camera = GetViewport().GetCamera2D();
        _player = GetTree().GetFirstNodeInGroup("players") as Node2D;
        if (_camera == null && _player != null)
            _camera = _player.GetNodeOrNull<Camera2D>("Camera2D");
        if (_camera == null) return;

        AudioListener2D listener =
            _camera.GetNodeOrNull<AudioListener2D>("WorldAudioListener");
        if (listener == null)
        {
            listener = new AudioListener2D { Name = "WorldAudioListener" };
            _camera.AddChild(listener);
        }
        listener.MakeCurrent();
    }

    // =========================================================
    // Reject far/offscreen work before allocating a stream player.
    private bool IsAudible(AudioDefinition sound, Vector2 position, float extra = 0f)
    {
        if (!sound.Positional) return true;
        Vector2 listener = _camera != null
            ? _camera.GetScreenCenterPosition()
            : _player?.GlobalPosition ?? Vector2.Zero;

        if (Settings.DistanceCulling && position.DistanceTo(listener) >
            Mathf.Max(1f, sound.MaxDistance) + Mathf.Max(0f, extra))
            return false;

        if (Settings.ScreenCulling && !sound.AllowOffscreen)
        {
            Vector2 screen = GetViewport().GetCanvasTransform() * position;
            Rect2 rect = GetViewport().GetVisibleRect().Grow(
                Mathf.Max(0f, Settings.ScreenMarginPixels));
            if (!rect.HasPoint(screen)) return false;
        }
        return true;
    }

    // =========================================================
    // Bring continuous sources in/out smoothly while respecting pooled slots.
    private void FadeIn(Slot slot, float seconds, float volume)
    {
        if (seconds <= 0f) return;
        if (slot.World != null) slot.World.VolumeDb = -60f;
        else slot.Flat.VolumeDb = -60f;
        slot.Fade = slot.Player.CreateTween();
        slot.Fade.TweenProperty(slot.Player, "volume_db", volume, seconds);
    }

    private void FadeOut(Slot slot, float seconds)
    {
        if (!slot.Active || slot.Stopping) return;
        StopTween(slot.Fade);
        if (seconds <= 0f)
        {
            Release(slot);
            return;
        }
        slot.Stopping = true;
        slot.Fade = slot.Player.CreateTween();
        slot.Fade.TweenProperty(slot.Player, "volume_db", -60f, seconds);
        slot.Fade.TweenCallback(Callable.From(() => Release(slot)));
    }

    private static void StopTween(Tween tween)
    {
        if (tween != null && tween.IsValid()) tween.Kill();
    }

    private static void SetStreamPaused(Slot slot, bool paused)
    {
        if (slot.World != null) slot.World.StreamPaused = paused;
        else slot.Flat.StreamPaused = paused;
    }
    #endregion

    #region Buses
    // =========================================================
    // Keep the Godot buses independently configurable in AudioSettings.tres.
    public void ApplyBusVolumes()
    {
        SetBus("Master", Settings.MasterDb);
        SetBus("Music", Settings.MusicDb);
        SetBus("Ambience", Settings.AmbienceDb);
        SetBus("WorldSFX", Settings.WorldSfxDb);
        SetBus("UI", Settings.UiDb);
        SetBus("CompanionVoice", Settings.VoiceDb);
    }

    private static void SetBus(string name, float db)
    {
        int bus = AudioServer.GetBusIndex(name);
        if (bus >= 0) AudioServer.SetBusVolumeDb(bus, db);
    }

    // =========================================================
    // Reduce music during spoken warnings without modifying its saved base volume.
    private void SetMusicDuck(bool voicePlaying)
    {
        float offset = voicePlaying && Settings.DuckMusicDuringVoice
            ? Settings.VoiceMusicDuckDb : 0f;
        SetBus("Music", Settings.MusicDb + offset);
    }

    private static string BusName(GameAudioCategory category) => category switch
    {
        GameAudioCategory.Music => "Music",
        GameAudioCategory.Ambience => "Ambience",
        GameAudioCategory.UI => "UI",
        GameAudioCategory.Voice => "CompanionVoice",
        _ => "WorldSFX"
    };
    #endregion
}
