# Player01 — articulated cutout rig (first pass)

**Status:** Opt-in prototype. The existing `Player01.png` remains the working default until you select this scene in the Inspector. No movement, combat, collision, save or equipment code is replaced.

## Enable it

1. Pull the commit and let Godot C# reload/compile.
2. Open `PLAYER/Player.tscn` and select the **Player** root.
3. Expand **Artwork → Visual Override** and set **Visual Scene** to `res://PLAYER/Artwork/PlayerDirectionalRig.tscn`.
4. Keep **Artwork Scale = (0.42, 0.42)**. The scene uses the original 256×256 coordinates with feet grounded at source Y=224.
5. Save the player scene and run. To roll back immediately, clear the **Visual Scene** field; the existing **Image = Player01.png** remains configured.

## What this is

A **Node2D pivot cutout rig**, not a Godot Skeleton2D/deformable mesh. Twelve `Polygon2D` surfaces sample regions of the existing `Player01.png` using custom UV coordinates; this avoids regenerating artwork and retains the original painted look at rest. Select and rotate a named pivot in Godot to move its attached section and any children. The polygons and joint pivots are editable in the saved scene.

- `Motion`: visual bob separate from terrain elevation and jump height.
- `Pelvis` → `Torso` → `Head`: spine and neck.
- `FrontUpperArm` → `FrontForearm`, `BackUpperArm` → `BackForearm`: shoulders and elbows.
- `FrontThigh` → `FrontShin`, `BackThigh` → `BackShin`: hips and knees.
- `Backpack`: independent attachment.
- `ShadowSource`: fully transparent sprite using the original silhouette for the existing `GroundShadow` registration; **do not hide it**, since the shadow system checks the source's visibility.

## Animations

Open `Player01Rig.tscn`, select **AnimationPlayer**, and use the Animation dock to preview/edit:

| Animation | Runtime selection | Description |
| --- | --- | --- |
| `idle` | Stationary | Breathing and slight helmet movement |
| `walk` | Walking | Alternating leg/arm joint rotation |
| `run` | Sprint requested while moving | Faster/stronger stride |
| `jump` | `Player.IsAirborne` | Limb pose only; `PlayerJump.cs` still supplies vertical displacement |
| `dodge_preview` | Editor-only | Future dodge pose; **does not create dodge physics or input** |
| `RESET` | Editor utility | Neutral pivot/key state |

Rotations and `Motion.position` are keyed in **AnimationPlayer**, not generated every frame. `PlayerRigAnimator.cs` only chooses an animation on state changes. To adjust a pivot without moving the artwork's resting pose, reposition the pivot and compensate the child art/child joints; the PNG itself has no stored pivot metadata.

## Implementation notes / current limitations

- The painted original is a flattened image. Large rotations can reveal missing shoulder, knee or hip artwork at cut boundaries. The next artwork pass should paint extra overlap behind each joint or replace individual polygon surfaces with properly illustrated independent PNGs. Small/medium motions are the intent of this first pass.
- The invisible shadow proxy currently projects the *neutral* full-body silhouette, not separately animated limbs. It preserves the cheap existing moving-shadow system rather than introducing 12 moving shadow sources.
- World sunlight now supports textured `Polygon2D` art, using the existing shared lighting material cache. Water/submersion artwork masking is a separate pre-existing limitation for imported PNGs and rig artwork.
- Mirroring is still done by `Player.cs` at the `TerrainVisual` level. Distinct facing directions, advanced aiming/tool grips, blending between dodge and locomotion, and proper independently drawn joints are follow-up passes.
- Godot/.NET runtime compilation and visual animation preview could not be run in the connector environment. Inspect the scene in the editor and tune pivots/keyframes before considering the rig production-ready.

## Front and rear directions

The Player scene now uses `PlayerDirectionalRig.tscn`. Its passive child scenes
`Player01FrontRig.tscn` and `Player01RearRig.tscn` each expose the same editable
idle/walk/run/jump/dodge-preview animations. The original `Player01Rig.tscn`
remains available for rollback. Only the selected view's AnimationPlayer is active.

Upward screen movement selects the rear view, downward movement selects the front,
and sideways movement or standing still preserves the previous view. Existing
TerrainVisual mirroring covers left/right, including all limb pivots. Direction
changes during the same animation preserve cycle phase. Jump displacement and
collision are still supplied by Player/PlayerJump. Both views use child draw order
at Z=0 so world Y sorting uses the player's ground position. A single transparent
shadow proxy switches to the selected full-body texture, without per-limb shadows.

`Player01Rear.png` is a generated rear three-quarter up-right view, normalized to
256x256 with a 188px body height and feet at source Y=224. The rear has separately
authored polygon cutouts, joint pivots, and a backpack attached to the torso. As
with the front rig, larger rotations can expose missing artwork at cut boundaries;
this is a rigid first pass, not a deformable skeleton or independently painted limbs.

Built-in image generation used the current Player01.png as the identity reference.
Prompt: same astronaut viewed from the rear at an elevated three-quarter angle,
facing up-right; preserve off-white armour, dark joints, helmet hardware, compact
scientific backpack and restrained cyan accents; neutral standing pose with readable
limbs; transparent background, no floor/cast shadow, soft material shading.

After building C#, run `Tests/PlayerDirectionalRigChecks.gd` with Godot's --script
option for animation paths, float rotation keys, front/rear selection, phase retention,
limb motion, mirroring, Z layers and one shared shadow source.
