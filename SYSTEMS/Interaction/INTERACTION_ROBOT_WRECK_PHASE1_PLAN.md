# Universal Interaction — Robot Wreck and Storage Slice

Status: implemented in this change; focused headless checks passed; local gameplay sign-off pending.
Baseline: main 0b3329d. Supersedes the original E-first staging by the user's explicit instruction: cursor + LMB only, with the wheel and suppression delivered together.

## Current experience

- Hover a wreck or ordinary container's visible artwork within its existing interaction reach.
- One faint cyan wheel appears at the visual centre. Click the centre Interact button with LMB to open that exact object's existing storage UI.
- There is no E interaction or E container-close handler. Use the window's Close button, Escape or Delete.
- LMB weapon/tool/item use is suppressed while the wheel is visible, including background clicks. Holding LMB through dismissal never resumes world use until release and a fresh press.
- Walking, sprinting and RMB jumping remain available while the nonmodal wheel is visible. Inventory, container and pause modes retain priority.
- Robot wrecks retain their existing take-only loot; ordinary containers retain deposit/withdraw behaviour.

## Ownership and files

- SYSTEMS/Interaction/IInteractionProvider.cs: object-owned primary action contract and small immutable descriptor.
- SYSTEMS/Interaction/StorageInteractionProvider.cs: delegates eligibility to WorldStorage and opening to StorageHud.TryOpen.
- SYSTEMS/Interaction/InteractionTarget.cs: runtime-only interaction Area2D under artwork; does not process input or block movement/combat. Uses collision bit 21.
- SYSTEMS/Interaction/InteractionResolver.cs: bounded interaction-mask point query (32 hits), stable nearest visual-centre choice with instance-ID tie break.
- PLAYER/Interaction/PlayerInteractionController.cs: one player target/wheel owner; roughly 13 Hz queries plus fresh click resolution; eligibility checked before execution. Maintains the target while cursor is on its wheel. Modal and other GUI controls take precedence.
- UI/Interaction/InteractionRadialHud.cs: one reusable cyan wheel and actual centre button, anchored in screen coordinates and clamped to viewport.
- PLAYER/PlayerInput.cs: checks interaction before world-use output; jump remains independent from the LMB-use gate.
- SYSTEMS/Input/InputModes.cs: reuses the existing release guard without claiming a modal movement mode; watches physical LMB and the existing player_use action.
- SYSTEMS/Storage/WorldStorage.cs: starts runtime target attachment after initialization, waiting for asynchronously created artwork.
- WORLDABLES/Objects/Storage/UI/StorageHud.cs: explicit selected-container opening; removed global nearest-container scanning, old E prompt and E handlers. Existing transfers, close-on-range-loss and modal ownership remain.

## Save compatibility

No scene/resource bytes, saved identities, inventory transactions, loot rolling or saving schema are changed. BasicRobotWreck.tscn and Player.tscn remain unchanged. Interaction targets attach at runtime and are not saved. Existing script changes are excluded by the existing recipe fingerprint rules. Local Continue of a pre-change campaign remains part of sign-off; validation has not been bypassed.

## Verification

- Startup correction: controller attachment is deferred because StorageHud.Ready runs while Player is setting up children. The focused fixture now reproduces child-Ready/parent-busy ordering.
- C# build against the available updated runtime source: passed.
- Focused headless Godot checks: runtime storage target attachment and sprite/elevation alignment, storage reach and hidden-target rejection, point targeting/off-art rejection, provider eligibility, exact action dispatch, immediate execution recheck, modal priority, held/released world-use suppression, queued target deletion, viewport clamp and real centre button: passed.
- These are focused component checks, not a claim that natural robot combat, every item or a full campaign was manually played remotely.

## Local gameplay sign-off

1. Kill a robot: one normal wreck. Hover its artwork within reach, then click Interact. Confirm existing take-only loot and no weapon shot.
2. Open an ordinary crate through the same wheel; deposit and withdraw. Close with the button/Escape/Delete. E should do nothing.
3. Move/sprint/RMB-jump while the wheel is visible. Open inventory/pause; wheel disappears and menus retain control.
4. Hold LMB through hover entry, hover loss, opening and closing loot. No laser/shot/use until release and a new press. Test weapon, laser, food and placement.
5. Hover outside reach or on another layer; no wheel. Test overlapping crates, terrain elevation, camera motion, chunk unloading and removal of a hovered object.
6. Continue an existing campaign; save/restart with partial and empty wreck loot. Confirm no rerolls, duplicate rewards or lost contents.

## Later work

Real secondary actions, crafting stations, vehicles/refuelling, structure pickup and hover outlines belong to separate passes. Implement new object behaviour through the provider contract; do not add per-object input polling or duplicate inventory/save logic. Custom scene artwork currently uses a conservative rectangle when its root is not a Sprite2D; refine custom hit shapes when adding such content.
