# Notifications — Fractured Horizons

## Purpose and appearance

A shared, modular **event-driven** notification HUD with two independent displays:

- **Major alerts:** top-centre, translucent dark hexagonal banners with a coloured severity border, title, description and footer. One visible at a time, priority-aware.
- **Small toasts:** top-left, compact translucent hexagonal banners that slide in from the left, stack and slide away. Consecutive same-item pickups/crafts merge quantities.

The style remains sleek, understated science fiction with cyan as the primary accent. Red = danger, amber = warnings, cyan = discovery / inventory, green = crafting, gold = achievements and blue = information. No imported art, shaders or new audio have been added.

## One central Inspector resource

**Edit `res://UI/Notifications/NotificationSettings.tres` in Godot's Inspector.** This is the main home for adjustable notification mechanics and layout. It is referenced by `NotificationManager.tscn`, so the HUD loads it automatically.

| Inspector group | Settings | Defaults |
| --- | --- | --- |
| Global Behaviour | Pause Timers When Paused | true |
| Major Alerts - Queue | Maximum Pending Major Alerts, Major Queue Lifetime Seconds, Interrupt For Critical Alerts, Critical Priority Threshold | 8, 20s, true, 90 |
| Major Alerts - Presentation | Width, Height, Top Margin, Entrance Seconds, Exit Seconds | 600, 128, 26, 0.34s, 0.25s |
| Toasts - Limits | Maximum Visible Toasts, Maximum Pending Toasts | 3, 5 |
| Toasts - Timing | Toast Duration, Entrance, Exit | 3.2s, 0.26s, 0.22s |
| Toasts - Layout | Width, Height, Top Margin, Left Margin, Spacing | 368, 76, 28, 18, 10 |

Changes to this resource are applied when the notification UI is next instantiated; **restart the running scene** after tuning values.

For properties that belong to a **specific major alert** (ID, title, description, tone, priority, hold duration, repeat cooldown, once-per-session flag, future voice cue), open that alert's file under `MajorAlerts/Definitions/`. Keeping these per-alert avoids forcing one duration or priority onto every event.

## Folder structure

```text
UI/
└── Notifications/
    ├── README.md
    ├── NotificationSettings.cs
    ├── NotificationSettings.tres      <- main settings to edit
    ├── NotificationManager.cs
    ├── NotificationManager.tscn
    ├── NotificationFrame.cs
    ├── MajorAlerts/
    │   ├── MajorAlertCatalog.cs
    │   ├── MajorAlertCatalog.tres
    │   ├── MajorAlertDefinition.cs
    │   ├── MajorAlertDisplay.cs
    │   └── Definitions/
    │       ├── EclipseImminent.tres
    │       ├── BossDetected.tres
    │       ├── BiomeDiscovered.tres
    │       └── HazardDetected.tres
    └── Toasts/
        ├── ToastNotification.cs
        └── ToastDisplay.cs

DEBUG/Notifications/NotificationTester.cs   <- testing only
WORLD/Scenes/world_infinite.tscn
    DEBUG/NotificationTester                 <- test node
```

The production notification manager is automatically attached by `UI/Inventory/UIInventoryMaster.cs` to the player's HUD; it stays above the normal HUD and below the pause menu. The real gameplay sources do not know about UI coordinates, colours or tweens.

## Phase 1 — Visual foundation (complete)

Native Godot six-sided panels, top-centre alerts and sliding/stacking top-left banners. Severity colour palette, resource-based alert definitions and a separate F4 tester under `WorldInfinite/DEBUG`.

## Phase 1.5 — Mechanical refinement (complete)

### Major alerts

1. Alerts enter a **bounded priority queue**. Higher-priority pending alerts display first; equal priorities preserve arrival order.
2. When the pending queue is full, a higher-priority incoming alert replaces its **lowest-priority** queued entry; equal/lower-priority requests are rejected.
3. If **Interrupt For Critical Alerts** is enabled, a new alert with priority at least **Critical Priority Threshold** interrupts a lower-priority banner currently on screen. Interrupted alerts are not replayed automatically.
4. Pending alerts older than **Major Queue Lifetime Seconds** are skipped when they reach the front of the queue. This prevents belated warnings after a long sequence of alerts.
5. Requests for a major alert already **playing or queued** are ignored. Repeat cooldowns start **on display**, not when queued.
6. **Once Per Session** alerts are marked seen only when they actually begin displaying. The set survives scene reloads during the current game process, but is not linked to an individual save/profile yet.

### Small toasts

1. No more than **Maximum Visible Toasts** are shown at once. Others wait in a **bounded pending queue** instead of evicting visible banners.
2. When the pending queue fills, its **oldest** entry is dropped in favour of newer activity.
3. New messages for the same **key, title and tone** merge within both the visible stack **and** the pending queue. Positive quantities add together without integer overflow. A visible toast's display timer refreshes on a merge.
4. A queued toast begins its full duration only **when displayed**. General non-quantity notices replace the previous notice's count when merged.
5. Empty keys are treated as distinct messages. Inventory and crafted messages use separate ID prefixes, so the same item is not incorrectly combined across the two categories.
6. Animations/timers are owned by each toast, and old timing tweens are killed when a notice is merged or removed.

### Pause behaviour

With **Pause Timers When Paused** enabled (default), both displays pause their bound tweens and the manager pauses its internal cooldown/expiry clock when the Godot scene tree is paused. This is controlled by the shared resource. If disabled, notification timing continues during pause.

### Known boundaries

- Queue limits are there to prevent endless on-screen spam; very old or overflowing messages can intentionally be dropped.
- Scene transitions remove currently visible and pending UI. **Once Per Session** major alert IDs remain remembered within the running process; full first-ever campaign discovery persistence is a later integration task.
- Alerts are **not** automatically generated by gameplay yet. The event producers need wiring in later passes.
- No notification-history log, player options menu, companion voice audio or save persistence for alerts is included in Phase 1.5.

## Testing in Godot

Pull the repository, let Godot import the new `.tres`, build the C# project, then run `WorldInfinite`.

Select **WorldInfinite → DEBUG → NotificationTester**, and choose the Inspector property **Scenario**. Press **F4** with the game running:

| Scenario | What F4 tests |
| --- | --- |
| Cycle Samples (default) | The original seven pickups/crafting/major alert examples |
| Toast Burst | 20 quick item pickups across 8 keys, followed by repeated items; check queue cap, merge, draining and stacking |
| Major Priority | Eclipse warning followed immediately by boss threat; boss should interrupt the warning with default priority settings |
| Duplicate Cooldown | Repeated crafting notices and duplicate hazard requests; confirms merging and duplicate suppression |
| Pause Timing | Displays a toast and requests boss alert; press Escape to pause and verify lifetimes freeze |

**Notes:** Godot uses F8 to stop the game, so F4 is intentional. The tester is compiled under `#if DEBUG`, lives outside the production UI, and can be disabled by its `Enabled` checkbox or unplugged via its `WorldInfinite/DEBUG` parent. Existing other debug nodes have not been relocated. Major test alerts still obey their real cooldown and once-per-session rules, so a test may be rejected when repeated too soon.

## Calling the system from real gameplay (future integration)

```csharp
NotificationManager notifications = NotificationManager.Find(this);
notifications?.ShowMajor("eclipse_imminent");
notifications?.ShowItem("plant_fibre", "Plant Fibre", 3);
notifications?.ShowCrafted("iron_plate", "Iron Plate", 2);
notifications?.ShowToast("bag_full", "Inventory full",
    "No free space", ToastTone.Warning);
```

`ShowMajor(id)` returns `true` when a request is accepted/queued, not necessarily when it is displayed. Its request can later expire while queued. Stable item IDs and alert IDs should be used rather than comparing display text.

To add a new major alert: duplicate a `.tres` from `MajorAlerts/Definitions/` using Godot, give it a unique `Id`, set its properties, add it to `MajorAlerts/MajorAlertCatalog.tres → Alerts`, then request that ID. `VoiceCueId` is currently a future audio hook only.

## Next phases

**Phase 2 — Live items/crafting:** emit structured successful-item-added and craft-completed events from the existing gameplay systems; avoid parsing their current text notices or confusing ingredient consumption with pickups.

**Phase 3 — World events:** eclipse threshold detector, biome entry tracking, boss/hazard sources, and first-ever discovery state stored in campaign saves.

**Phase 4 — Polish/options:** companion voice cues and effects, sound toggles, notification history, localization and additional accessibility/safe-area controls.

## Performance

No world scanning or per-object polling was added. All UI work happens on event requests or while native tweens run. The major and toast queues are bounded, and the small hexagonal frames are drawn with inexpensive native Godot controls.

**Validation:** source paths and GitHub resource references are inspected, but these C# edits cannot be compiled or play-tested in this GitHub-only environment. Verify the debug scenarios in Godot after pulling.
