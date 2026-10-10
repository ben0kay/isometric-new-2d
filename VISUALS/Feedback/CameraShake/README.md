# Camera Shake — Shared Event Pipeline

Any gameplay system can request an impact, world event or earthquake without
knowing how a camera is implemented. The **camera decides** whether to react,
how strong to react and when to settle back.

## Files

```text
VISUALS/Feedback/CameraShake/
  CameraShakeBus.cs
  CameraShakeController.cs
  CameraShakeSettings.cs
  DefaultCameraShakeSettings.tres
  CameraShake.tscn
  README.md
```

The reusable **CameraShake.tscn** scene is already instanced at
`PLAYER/Player.tscn → Camera2D → CameraShake`.
No edits were made to Player.cs, damage or earthquake mechanics.

## Send requests from any future gameplay event

Request strength is normally **0..1**, duration is **seconds**.
The controller limits the combined result to its configured maximum.

### Direct shake — player is bitten by a dinosaur or hit by a boss

```csharp
CameraShakeBus.ShakeTarget(player, 0.65f, 0.25f);
```

The recipient is a `Node2D` such as the affected `Player` or its `Camera2D`.
Only the matching current camera responds.

### Spatial shake — explosion or nearby ground slam

```csharp
CameraShakeBus.ShakeAt(explosionNode, 0.8f, 0.35f, 600f);
```

Arguments: **source Node2D, strength, duration, radius** (world units/pixels).
The bus uses `source.GlobalPosition` and `WorldLayerMember.For(source)`.
Camera effects fade with distance, and a camera in a different layer ignores
the event. Ensure the source has the correct assigned world layer.

### Spatial shake without a scene node

```csharp
CameraShakeBus.ShakeAt(impactWorldPosition, WorldLayerId.Surface,
    0.8f, 0.35f, 600f);
```

Explicitly specify the appropriate layer (surface, cavern, etc.).

### Global shake — large earthquake

```csharp
CameraShakeBus.ShakeAll(0.35f, 3.0f);
```

This reaches every currently active camera, on any layer.

These calls are intentionally **not hardcoded into existing events**.
When earthquake, dinosaur or explosion mechanics are added, those mechanics
can call the relevant method where their effect occurs.

## How the pipeline works

1. An event calls a static method on `CameraShakeBus`.
2. Active `CameraShakeController` instances subscribe to the request event.
3. Each controller checks target, world layer and distance as applicable.
4. Concurrent requests blend via root-sum-square; the resulting strength is
   capped and converted to pixel movement using `CameraShakeSettings`.
5. The controller animates `Camera2D.Offset`, then restores its original
   offset exactly when the last impulse ends.
6. With no active requests, the controller has **no frame processing**.
   Subscription is removed when the scene is freed.

This leaves your existing camera position and smoothing configuration intact.
There is no Autoload, timer loop or central scene node to set up.

## Adjust the default .tres

Open `DefaultCameraShakeSettings.tres` in Godot's Inspector.

| Property | Default | Purpose |
| --- | --- | --- |
| Enabled | true | Master enable/disable |
| StrengthMultiplier | 1 | Overall intensity multiplier |
| MaximumStrength | 1 | Cap on the combined shake |
| MaximumOffsetPixels | 6 | Maximum displacement |
| ShakeFrequencyHz | 15 | Speed of smooth vibration |
| FadeInSeconds | 0.03 | Soft start of each request |
| FadeOutFraction | 0.45 | Portion of each request used for fade out |
| MaximumConcurrentRequests | 12 | Max retained requests per camera |
| DistanceFalloffPower | 1.4 | How fast radius effects weaken |

A long subtle earthquake can coexist with a short strong bite. Rather
than piling up unlimited motion, requests combine and stay capped.

## Future Options menu

Without changing the shared settings resource, a menu can use:

```csharp
var shake = player.GetNode<CameraShakeController>("Camera2D/CameraShake");
shake.SetEnabled(false);  // Disable; stop and restore camera immediately
shake.SetEnabled(true);   // Allow future requests
shake.SetStrength(0.5f);  // 50% of the default effect
shake.SetStrength(0f);    // Effectively off
```

These controls are per-camera. A future Options menu would store preferences
and apply them to whichever player camera is active.

## Try it now with no gameplay-code edits

1. Pull the repository changes, open Godot and compile your C# project.
2. Open `PLAYER/Player.tscn`; find the **CameraShake** child of Camera2D.
3. Select the CameraShake instance and enable **Test Shake On Ready** in
   its Inspector. This is off by default.
4. Run the game: the camera should make one short shake at startup.
5. Disable the test flag again; ordinary gameplay won't shake until an
   event requests it.
6. Adjust `MaximumOffsetPixels` or `ShakeFrequencyHz` in the `.tres`
   and repeat to see the difference. Confirm normal follow/smoothing and
   that there is no lingering camera offset.
7. In the future, paste a `CameraShakeBus` call into an explosion, quake,
   weapon, attack or other event at the moment it fires.

Godot runtime compilation and in-game testing still need checking locally.
