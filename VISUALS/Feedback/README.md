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
per-frame position tracker. When the `Artwork` child is a `Sprite2D`,
feedback uses its real rendered rectangle (including artwork offsets and
scaling) to locate the top centre. This avoids the excessive empty space
from the much larger `SpawnVisualBounds`, which is retained as a fallback
for custom drawn `Node2D` art. `WorldFeedback.SetFacing` counteracts
sprite facing flips.

In `DefaultHealthBarSettings.tres`, `VerticalGap` controls the small
clearance above the sprite; `VerticalOffset` allows a larger signed
adjustment without changing species' gameplay bounds. A **positive
VerticalOffset moves the bar DOWN**, and a negative offset moves it UP.
Damage numbers share the corrected sprite-top anchor but keep their own
`VerticalGap`. Restart/recreate entities after changing the `.tres`.

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

## Damage Numbers (first pass)

- Folder: `VISUALS/Feedback/DamageNumbers/`.
- Tune `DefaultDamageNumberSettings.tres` for enable/disable, text size and
  colors, outline, offset, duration, merging, float/fade and pop.
- A `Health.DamageApplied(int)` signal reports **actual HP removed** after
  resistance/immunity and clamping. It does not fire for save restoration,
  healing, maximum-HP changes or blocked hits. It also reports environmental
  and survival damage. No critical-hit or shield system is implied.
- Each entity owns one idle `DamageNumbers` component with one lazily
  created Label. Hits inside `MergeWindowSeconds` add to the same number:
  `-24` then `-48`. Hits outside this window replace the old number
  rather than spawning overlapping numbers. Each hit restarts the animation.
- The number follows TerrainVisual, remains readable on facing flips and
  disappears when its entity dies, hides, or streams out. Lethal-hit numbers
  disappear with the dying entity (no separate death VFX lifetime yet).
- An optional `EntityDefinition.DamageNumberOverride` can override the
  global settings for one species.
- For a future Options menu, `WorldFeedback.SetDamageNumbersEnabled(bool)`
  gates an already attached display per actor, without editing shared
  settings. The menu/global broadcast itself is **not implemented** yet.
  `DamageNumberSettings.Enabled` is the global default resource switch.
- No changes to inventory, AI, weapon behavior or save formats.

### Damage number checks in Godot

1. Damage a wildlife or robot entity by 24: see `-24` float up and fade.
2. Hit again within 0.35 s: see `-48` rather than two labels.
3. Hit after 0.35 s: the existing number is replaced with a new `-24`.
4. Disable damage numbers in the default `.tres` then relaunch:
   they are absent while the existing health bars still work.
5. Test the pop, lifetime, color, float distance and merge tuning.
6. Face left/right, move across elevated terrain, and retire a hurt actor.
7. Confirm resistance-reduced damage numbers and that a blocked hit
   (invulnerability or full resistance) produces no number.
8. Confirm healing, changing maximum health, and save restoration do not
   produce damage numbers.

Source integration was reviewed; Godot/C# runtime compilation and gameplay
checks still require local testing.
