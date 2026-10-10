# World Loading Screen

A self-contained sci-fi loading overlay for both test and campaign worlds.

The scene draws its own animated **percentage**, **36-part progress bar**,
scanner sweep, rotating concentric rings, corner brackets and thin vector
grid. It optionally shows a cinematic PNG backdrop but works immediately
with its built-in blue-black gradient.

## Files

```text
UI/Loading/
    WorldLoadingScreen.tscn
    WorldLoadingScreen.cs      # Reads readiness and dismisses UI
    WorldLoadingVisual.cs      # Animated procedural drawing
    LoadingScreenSettings.cs   # Inspector-editable settings
    DefaultLoadingScreenSettings.tres
    Artwork/
        README.md              # Drop a PNG in this folder
    README.md
```

The scene is already instanced directly below the world root in:
- `WORLD/Scenes/world_test.tscn`
- `WORLD/Scenes/world_infinite.tscn`

The only other existing file touched is `WORLD/Chunks/ChunkController.cs`.
It now exposes **read-only** `StartupArtworkReady` and
`GetStartupProgress(out ready, out required, out stage)`.
None of the world's generation, chunk budgets, save data, or player
movement behavior has been changed.

## Add your own loading image

1. Place your file at exactly:
   `res://UI/Loading/Artwork/LoadingBackdrop.png`.
2. Reopen Godot / let its importer finish and run the world.
3. The loader automatically uses the PNG next time the scene starts.
   No code, `.tscn` or `.tres` editing is necessary.

A **1920 x 1080 or higher, 16:9 PNG** looks best. The image scales
to **cover** the screen, preserving aspect ratio and cropping centrally.
The title and progress graphics sit on top of an adjustable dark overlay.
Without a PNG, the procedural background appears automatically.

To use a different name or image location, edit the `BackdropPath`
property in `DefaultLoadingScreenSettings.tres` in Godot's Inspector.
There is deliberately no missing external-resource reference in the
scene file: fresh clones open cleanly before you add any PNG.

## How loading progress is calculated

`ChunkController` waits for all chunks in its **startup activation buffer**
(the visible viewport plus `ActivationMargin`) to be fully `Ready`.
The percent uses **completed required chunks / total required chunks**.
Only that actual buffer counts. The separate preparation/retention rings
do not increase the required total.

Progress updates every `StatusRefreshSeconds` and the displayed fill
eases smoothly toward the most recent real count. Since chunks complete
in batches, the smooth display may trail actual completion briefly.
No artificial timer or invented percentage is used:

- While the three initial artwork atlases load/bake, the bar scans at 0%.
  Their own progress isn't numeric, so no fake estimate is displayed.
- During terrain startup, the number reflects the actual activation buffer.
- After `ChunkController.WorldReady`, it waits at up to 99% while
  `CampaignSession.IsLoadingFor` reports campaign initialization.
- When startup and campaign restoration have both completed, it reaches
  100%, fades out and frees itself from the scene tree.
- In direct world test runs without a CampaignSession, `WorldReady` is
  the final readiness gate.

This screen is a regular CanvasLayer under the world root rather than
`WorldObjects`, which ChunkController intentionally disables until ready.
It uses `ProcessMode.Always`, so it animates independently without
unfreezing or pausing the gameplay world.

## Edit colors and timing

Open `UI/Loading/DefaultLoadingScreenSettings.tres`.

- `Enabled`: enable/disable the entire loading screen
- `Title`, `Subtitle`: heading text
- `ShowTechnicalDetails`: stage information and ready chunk count
- `ShowPercentage`: numeric percentage
- `BackdropPath`, `BackdropDarken`: PNG slot and readability overlay
- `FallbackBackground`, `Accent`, `TextColor`, `SecondaryText`,
  `GridColor`: sci-fi UI palette
- `StatusRefreshSeconds`: frequency of backend progress sampling
- `ProgressCatchupSpeed`: how quickly the bar catches up to real progress
- `FadeOutSeconds`: fade after reaching 100%; 0 disables fading
- `ScanSpeed`: scanner/ring animation speed

The screen runs its vector animation while visible, with one canvas
Control handling drawing and no individual progress-segment nodes.
It frees itself after loading so there's no gameplay-time overhead.

## Test

1. Pull changes and build the Godot C# project.
2. Open `WORLD/Scenes/world_test.tscn` and run. You should see the loader,
   changing stage descriptions, a smooth bar and a percentage.
3. Once the starting area is ready, confirm the overlay fades and the
   player is already free to move.
4. Check `world_infinite.tscn`, including continue-from-save if used:
   the screen remains up until final campaign restoration completes.
5. Drop a PNG in `Artwork/LoadingBackdrop.png` and repeat.
6. Change Enabled or accent/transition values in the default `.tres`.
7. Test at another viewport size. Overlay should scale and crop correctly.
8. If the screen never leaves, inspect Godot's Errors and the chunk
   startup status; it never forces startup to finish prematurely.

No live Godot compile or rendering verification was available remotely.
