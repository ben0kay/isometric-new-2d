// Central registry for the audio definitions used by the runtime manager.
// In-game debug testers can temporarily add definitions without altering .tres files.
using Godot;

[Tool, GlobalClass]
public partial class AudioCatalog : Resource
{
    [Export] public Godot.Collections.Array<AudioDefinition> Sounds
        { get; set; } = new();

    // =========================================================
    // Resolve a stable sound ID; duplicate IDs should be avoided in the Inspector.
    public AudioDefinition Find(string id)
    {
        foreach (AudioDefinition sound in Sounds)
            if (sound != null && sound.Id == id) return sound;
        return null;
    }
}
