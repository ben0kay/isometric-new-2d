// Dedicated non-spatial female-companion cue queue.
// Critical dialogue may interrupt a lower-priority line; clips are added later.
using Godot;
using System.Collections.Generic;

public partial class CompanionVoice : Node
{
    #region State
    private AudioManager _audio;
    private string _currentId = "";
    private int _currentPriority;
    private readonly List<AudioDefinition> _waiting = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Use the central manager's one-voice audio budget and completion event.
    public override void _Ready()
    {
        _audio = GetParent<AudioManager>();
        _audio.SoundFinished += OnSoundFinished;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_audio))
            _audio.SoundFinished -= OnSoundFinished;
    }
    #endregion

    #region Requests
    // =========================================================
    // Queue a known non-positional voice clip using the same IDs as future alerts.
    public bool Say(string cueId)
    {
        AudioDefinition sound = _audio?.Catalog?.Find(cueId);
        if (sound == null || sound.Stream == null ||
            sound.Category != GameAudioCategory.Voice ||
            sound.Mode != GameAudioMode.OneShot || sound.Positional)
            return false;

        if (_currentId == cueId || _waiting.Exists(x => x.Id == cueId))
            return false;

        if (_currentId.Length == 0) return Begin(sound);

        int max = Mathf.Max(0, _audio.Settings.VoiceQueueLimit);
        if (sound.Priority > _currentPriority)
        {
            // Mark ourselves idle before stopping so its completion callback
            // cannot accidentally start a queued low-priority line first.
            string old = _currentId;
            _currentId = "";
            _currentPriority = 0;
            _audio.StopOneShot(old);
            return Begin(sound);
        }

        if (max == 0) return false;
        if (_waiting.Count >= max)
        {
            AudioDefinition last = _waiting[_waiting.Count - 1];
            if (sound.Priority <= last.Priority) return false;
            _waiting.RemoveAt(_waiting.Count - 1);
        }

        int index = _waiting.FindIndex(x => x.Priority < sound.Priority);
        if (index < 0) _waiting.Add(sound);
        else _waiting.Insert(index, sound);
        return true;
    }

    // =========================================================
    // Start the clip only when there is an available voice playback slot.
    private bool Begin(AudioDefinition sound)
    {
        if (!_audio.Play(sound.Id)) return false;
        _currentId = sound.Id;
        _currentPriority = sound.Priority;
        return true;
    }

    // =========================================================
    // Advance after a voice finishes; unrelated world sounds are ignored.
    private void OnSoundFinished(string id)
    {
        if (id != _currentId || _currentId.Length == 0) return;
        _currentId = "";
        _currentPriority = 0;
        while (_waiting.Count > 0)
        {
            AudioDefinition next = _waiting[0];
            _waiting.RemoveAt(0);
            if (Begin(next)) break;
        }
    }
    #endregion
}
