# Notifications — Fractured Horizons

## Purpose

A shared, modular, **event-driven** HUD notification system with two presentation styles. The visual direction is **sleek sci-fi**: dark translucent surfaces, slender cyan outlines, elongated hexagonal silhouettes, small geometric emblems and restrained slide/fade animation. No artwork or shaders are required for the initial version.

All notification code and configuration resources live inside `UI/Notifications/`; unrelated gameplay systems should only call its public API. Avoid adding per-frame polling to find notifications.

## Folder layout

- `NotificationManager.cs` — single entry point; major alert queue, priority, cooldown and public API.
- `NotificationManager.tscn` — instanced automatically by `UI/Inventory/UIInventoryMaster.cs`.
- `DEBUG/Notifications/NotificationTester.cs` — separate testing script, attached to `WorldInfinite/DEBUG/NotificationTester` (outside this UI folder).
- `NotificationFrame.cs` — common translucent six-sided frame for both displays.
- `MajorAlerts/MajorAlertDefinition.cs` — exported Godot Resource fields for alert identity, text, category, duration, priority, cooldown and future voice cue.
- `MajorAlerts/MajorAlertCatalog.cs` + `.tres` — list of available major alerts, looked up by stable ID.
- `MajorAlerts/MajorAlertDisplay.cs` — one top-centre major alert, vertically animated.
- `MajorAlerts/Definitions/*.tres` — user-editable presets.
- `Toasts/ToastNotification.cs` — lightweight payload and notification tone.
- `Toasts/ToastDisplay.cs` — top-left sliding banners, stacking, additive same-key merging and removal.

## Phase 1 — UI foundation (implemented)

- The notification canvas attaches to the existing player HUD (`InventoryHud` inherits `UIInventoryMaster`) and stays above the normal HUD, below the pause menu.
- Major alerts display **top centre** and queue by priority; each ID supports cooldown and optional once-per-session suppression.
- Small notifications slide in **from the left**, stack up to three and slide out after roughly 3.2 seconds.
- Repeated item/crafting events with the **same key** add quantities together and refresh the toast lifetime. For example, Plant Fibre +1 followed by Plant Fibre +2 becomes Plant Fibre +3.
- Colour language: **cyan** discovery and inventory, **amber** warnings, **red** threats, **green** crafting, **gold** achievements, and **blue** information.
- Visuals are made from native Godot UI controls and polygon drawing. No imported sprites, shaders or external fonts.
- **No inventory, crafting, eclipse or biome events have been connected yet.** The displays and alert resources are ready, but actual gameplay triggers are a later phase.

### Preview in Godot (isolated debug scene node)

The production `NotificationManager.cs` no longer reads F8 or contains sample notification code.

The test script is `res://DEBUG/Notifications/NotificationTester.cs`, attached in
`WORLD/Scenes/world_infinite.tscn` under the new scene-tree branch:

```text
WorldInfinite
└── DEBUG (Node)
    └── NotificationTester (Node, script: NotificationTester.cs)
```

All pre-existing debug nodes remain in their original locations.

Pull the repository, let Godot import the new script, build the C# project, then run
`WorldInfinite` in the editor. Press **F8** repeatedly to preview:

1. Plant Fibre +1
2. Plant Fibre +2 (combines with the previous notice if still visible)
3. Crafted Iron Plate ×2
4. Eclipse Imminent (amber)
5. Hostile Signature Detected (red)
6. Region Discovered (cyan)
7. Hazardous Conditions (amber)

The sample input code is wrapped in `#if DEBUG`, so it does not compile into C# Release
builds. The tester additionally exposes `Enabled` and `PreviewKey` in the Godot Inspector.
To unplug the tester at any time, disable its **Enabled** checkbox, set the `DEBUG`
parent's **Process Mode** to **Disabled**, or remove the `NotificationTester` node.
None of these require changing the production notification system.

The region discovery sample uses `OncePerSession`; after it has been displayed once
during a running game, requesting the same preview again will not display it.

### Calling the system from gameplay

Resolve the local HUD from a node inside the scene tree:

    NotificationManager notifications = NotificationManager.Find(this);
    notifications?.ShowMajor("eclipse_imminent");
    notifications?.ShowItem("plant_fibre", "Plant Fibre", 3);
    notifications?.ShowCrafted("iron_plate", "Iron Plate", 2);
    notifications?.ShowToast("bag_full", "Inventory full", "No free space", ToastTone.Warning);

Use **stable IDs/keys**: the item ID for pickups, recipe/output ID for crafting, or the alert definition ID for major alerts. Omit the key (empty string) only when every toast should be a separate entry. Gameplay systems should not control colours, screen coordinates, timers or tweens.

### Adding a new major alert

1. Duplicate an existing `MajorAlerts/Definitions/*.tres` file in Godot and assign a unique `Id`.
2. Edit Title, Description, Footer, BadgeText, Tone, Priority, DurationSeconds, CooldownSeconds and OncePerSession in the Inspector.
3. Add that definition resource to `MajorAlerts/MajorAlertCatalog.tres` → `Alerts` array.
4. Trigger it from gameplay with `NotificationManager.Find(this)?.ShowMajor("your_alert_id");`.

`VoiceCueId` is only a **future integration hook**; it does not play audio. `OncePerSession` persists across scene reloads during the current running game, but is not save-persistent. If it becomes a first-time-ever campaign alert, the discovery state must eventually be saved outside this UI.

## Later phases (planned; not yet implemented)

**Phase 2 — Inventory and crafting:** subscribe to structured successful item additions and completed crafts instead of parsing the inventory's existing text-only `Notice`. Ensure crafting material consumption is not misrepresented as collection. Add appropriate feedback for inventory full, failed actions, and item removal when useful.

**Phase 3 — World events:** a small eclipse threshold detector requests `eclipse_imminent`; player-biome transitions request discovery notices; boss/hazard systems emit `boss_detected` / `hazard_detected` at their own detection points. Persist *first-ever* discoveries through the campaign save system in that phase.

**Phase 4 — Polish and accessibility:** optional sounds/companion voice cues, scaling and opacity controls, duration settings, notification history, localization and HUD-safe-area adjustments.

## Performance and boundaries

Everything is **read-only with respect to save data** in Phase 1. Banners are created only on notification requests; there is no game-world polling. The toast count and pending major alert count are bounded. Rendering uses small native UI polygons. Presentation remains independent from the underlying event sources.

**Note:** This implementation has been checked against the repository structure but has not been compiled or visually run in the user's Windows/Godot environment yet. Test the F8 preview before connecting gameplay events.
