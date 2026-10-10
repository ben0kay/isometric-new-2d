# Shared World Feedback

This is the shared home for world-space UI effects: health bars now, and
potential shields, dodge trails, damage numbers or status indicators later.
Each future effect should remain separate from damage and movement logic.

## Configuration

Open `VISUALS/Feedback/HealthBars/DefaultHealthBarSettings.tres` in
the Godot Inspector. Change widths, maximum HP anchors, height, vertical
gap, color thresholds, animation or full-health visibility.

The bar width uses logarithmic scaling of **maximum** health, clamped to
`MinimumWidth` and `MaximumWidth`. The fill uses current/max HP.
Thus a 10,000-HP boss never produces a 10,000-pixel bar.

An `EntityDefinition` has an optional `HealthBarOverride` resource
for individual species. If empty, all entities reuse the default settings.
Custom world objects can call `WorldFeedback.Bind` with a `Health`,
a Node2D visual root and bounds; only shared wildlife/robot Entity actors
are wired in this initial implementation (not the player HUD).

## Runtime behavior

The health bar is attached to the elevated `TerrainVisual` beside its
`Artwork` child, so terrain/layer motion is inherited without another
per-frame position tracker. `SpawnVisualBounds` determines its top anchor.
`WorldFeedback.SetFacing` counteracts sprite facing flips.

The Health component's `Changed` and `Died` signals control display.
Full health is hidden by default; a partly damaged actor restored from a
save is initialized with the correct fill. Only a brief optional fill
transition invokes `_Process`. Chunk retirement frees the bar with the
entity; there is no separate persistence or world registry.

## Test locally

1. Compile/open the project, confirm no C# errors or missing .tres links.
2. Find a robot or wildlife entity: no bar at full health.
3. Damage it: bar shows above artwork and decreases from left to right.
4. Move entity on elevated terrain, turn left/right: bar follows and doesn't mirror.
5. Bring HP to full: bar hides. Kill: bar disappears.
6. Save/reload or unload/reload a wounded entity: percentage remains correct.
7. Adjust DefaultHealthBarSettings.tres widths, colors and VerticalGap.
8. Try species with 50, 500 and 10000 max HP: bar width stays in the set range.

No Godot runtime or local rendering tests were performed from GitHub.
