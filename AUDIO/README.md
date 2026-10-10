# Fractured Horizons — Audio Foundation (Stage 1)

This folder is the **single home for game audio**. Audio gameplay hooks are deliberately **not yet connected**. The architecture uses Godot's native AudioStreamPlayer/AudioStreamPlayer2D and bus mixer rather than mixing audio manually.

## Inspector: where to configure audio

- **`AUDIO/AudioSettings.tres`** — master/music/ambience/world/UI/companion volume, playback budgets, distance and screen culling, maintenance interval, pause behaviour, future voice ducking.
- **`AUDIO/AudioCatalog.tres`** — register every sound definition. Contains sample entries, ready for clips.
- **`AUDIO/**/.../*.tres`** — individual sound ID, playback mode, audio clip, category, priority, cooldown, instances, stereo positioning and distance, fades and intermittent frequency.
- **`AUDIO/AudioBuses.tres`** — Godot's editable audio bus layout, selected from `project.godot` via `audio/buses/default_bus_layout`. The Audio dock exposes the buses.
- **`AUDIO/AudioManager.tscn`** — installed beneath `WorldInfinite/Systems`; contains `CompanionVoice` and its settings/catalog references.

After changing these Inspector values, run the game again to see the new settings. Changes to imported audio looping behaviour should be applied in the audio file's **Import** tab and reimported.

## Folder layout

```text
AUDIO/
├── README.md
├── AudioManager.cs / AudioManager.tscn
├── AudioSettings.cs / AudioSettings.tres
├── AudioDefinition.cs
├── AudioCatalog.cs / AudioCatalog.tres
├── AudioBuses.tres
├── Sounds/
│   ├── Player/Footstep.tres
│   ├── Combat/Impact.tres
│   ├── Environment/GeneratorHum.tres
│   ├── Environment/AlienBirdCall.tres
│   └── UI/NotificationChime.tres
├── Music/MenuTheme.tres
├── Ambience/MarshAmbience.tres
└── Companion/
    ├── CompanionVoice.cs
    └── EclipseWarning.tres

DEBUG/Audio/AudioTester.cs
WORLD/Scenes/world_infinite.tscn
    Systems/AudioManager
        CompanionVoice
    DEBUG/AudioTester
```

## Three playback modes

**OneShot:** e.g. footstep, gunshot, UI chime. The caller requests it once, and its player returns to the pool when the stream ends. Priority and max-instances/cooldown prevent overlap spam.

**Loop:** e.g. machine humming, swamp ambience or music. Gameplay calls `StartEmitter` once when switched on, then `StopEmitter` once when switched off. One registered sound ID per owner (or a global non-spatial ID) prevents duplicate looping. A loop relinquishes its audio player beyond its range or screen boundary, but remains registered so it can resume when approached. The owning object is held **weakly**; when that object disappears or unloads, the registration and player are released on the next maintenance check. A fade is applied when available.

**Intermittent:** e.g. alien bird calls. One registered emitter produces a discrete one-shot at randomized intervals. No always-playing sound node is required between calls.

**IMPORTANT:** The audio clip itself must support looping. For an imported WAV, configure **Import → Loop Mode → Forward** or provide loop metadata. For Ogg Vorbis, set its loop option. Selecting `Mode = Loop` in the AudioDefinition does *not* rewrite the imported audio stream.

## Mono/stereo and off-screen rules

`AudioDefinition.Positional` is **independent** of `Mode`:
- Positional one-shot = gunshot.
- Positional loop = generator hum.
- Non-positional one-shot = alert/companion voice.
- Non-positional loop = soundtrack/ambient mix.

Positional audio uses Godot's native AudioStreamPlayer2D stereo panning and distance attenuation. A `WorldAudioListener` is attached automatically to the current player camera. A positional sound has a maximum audible distance and a per-sound `AllowOffscreen` toggle. With screen culling enabled globally, sounds with `AllowOffscreen = false` must be inside the viewport plus the configurable margin. Allow-offscreen sounds can still be culled by distance.

Distance culling avoids starting inaudible streams. Regular emitter scans are spaced by `EmitterCheckSeconds`, not performed for every sound every frame. Loop resume margin helps prevent sound on/off chattering near the boundary.

## Playback pools and priority

- `AudioManager` maintains **separate category budgets** for world SFX, UI, companion voice, ambient loops and other world loops.
- Players are pooled and reused. At capacity, an incoming sound only replaces a strictly **lower-priority** playing sound of the same category/mode, or is rejected.
- `MaximumInstances` is a per-ID limit in addition to the category limit.
- One-shot cooldowns are checked by stable sound ID.
- Playback slots are **not created per streamed chunk**. The central pool reuses them while owners register or unregister by stable IDs.
- The current version provides basic fade in/out, world pause/unpause, mixer volumes and an initial companion voice ducking mechanism. Further audio effects (EQ/reverb/compression) can be added later in Godot's bus mixer.

## Calling it from gameplay (future hooks)

```csharp
AudioManager audio = AudioManager.Find(this);

// Ordinary non-positional one-shot
audio?.Play("ui_notification");

// World-space one-shot
audio?.PlayAt("combat_impact", GlobalPosition); // From a Node2D actor
audio?.PlayFrom("player_footstep", this);         // From a Node2D actor

// Owner-managed repeating emitter; do not call StartEmitter every frame!
audio?.StartEmitter("generator_hum", this);
// When disabled/removed, call StopEmitter
audio?.StopEmitter("generator_hum", this);

// Global non-positional loop (one per ID)
audio?.StartEmitter("marsh_ambience");
audio?.StopEmitter("marsh_ambience");

// Dedicated companion queue
audio?.GetNode<CompanionVoice>("CompanionVoice")?.Say("eclipse_warning");
```

`Play` only accepts non-positional definitions. `PlayAt` accepts world-space sound coordinates. `StartEmitter` accepts only Loop and Intermittent definitions. Playback requests without assigned AudioStreams are rejected, not treated as errors that crash gameplay.

## Companion voice and notifications

The companion cue system is intentionally separate from `UI/Notifications/`. Your major-alert definitions already include a `VoiceCueId` field such as `eclipse_warning`. In a later pass, an event dispatcher can synchronize the alert banner and the matching female voice clip.

Companion voice clips must be assigned `Category = Voice`, `Mode = OneShot`, `Positional = false`. The companion queue allows only one speech clip at a time, rejects duplicates, and lets higher-priority warnings interrupt lower-priority speech. Music volume is temporarily reduced by the `VoiceMusicDuckDb` setting while a voice clip is active. **No live alerts trigger voice yet.**

## Test the foundation without importing audio files

Open `WORLD/Scenes/world_infinite.tscn`, then expand `DEBUG → AudioTester`. Build Godot C# and run the game. Press **F9** to hear temporary in-memory tones. These do *not* become project audio assets.

Inspector `Scenario` choices:
- **CycleExamples:** centre UI beep, left positional tone, right positional tone, then toggle a continuous positional hum.
- **PriorityBurst:** asks for 36 rapid world shots to exercise the global pool and per-sound instance limits.
- **Intermittent:** toggles a synthetic alien-bird emitter every 2–4 seconds.
- **VoiceQueue:** plays the synthetic voice-channel test tone, not a real companion voice.

Test with headphones if possible to distinguish spatial panning. The F9 shortcut can be changed through the tester's Inspector; it exists only in `#if DEBUG` code. The tester is removable independently of production audio.

## Planned follow-up passes

1. **Actual audio assets and mixing:** assign real .wav/.ogg clips to the example definitions, tune attenuation, fade and Godot bus levels.
2. **Gameplay hooks:** subscribe once to mining, crafting, movement, weapons, entities and biome ambience transitions. Avoid per-frame sound requests.
3. **Companion and alerts:** feed saved/generated female voice clips into the companion catalog, coordinate major alert banners and subtitles. Add real music and audio options.
4. **World integration refinements:** layer-specific effects (surface/caves), stable ambience transitions, long-running loop persistence policy, UI options and any needed audio profiling.

## Boundaries and testing

This initial pass adds the manager to the world scene, but **does not produce sound during ordinary play**, because the example definitions have no audio clips assigned. The developer-only tester synthesizes its samples at runtime.

Changes are isolated to the new AUDIO and DEBUG/Audio folders plus minimal scene/project bus configuration. The existing notification system, gameplay spawners, save passes and other debug nodes remain untouched.

This code was reviewed against the repository and official Godot API docs, but cannot be compiled/run in the GitHub connector. Build Godot C# after pulling and report the first error or unexpected behaviour, if any.
