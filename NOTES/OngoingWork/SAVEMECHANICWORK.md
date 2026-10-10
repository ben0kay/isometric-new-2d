# Campaign Save Persistence

## Current Status

Manual profile-owned campaign saving is implemented for the currently supported gameplay state. Passes 1–5 and 6.1–6.2 are implemented. Pass 7 finishes save/recovery menu messaging and provides combined verification. Local gameplay sign-off remains required; this does not claim every possible future mechanic is persistent.

| Pass | Scope | Status |
|---|---|---|
| 1–2 | Profile ownership, atomic files/backup, world recipe/time, exact player state and surface position | Implemented; surface position confirmed locally. |
| 3 | Changed generated resources and chunk regeneration | Implemented. |
| 4 | Drops, storage/loot, wrecks, structures and health | Implemented; optional lifetime data stored, expiry not yet programmed. |
| 5.1–5.3 | Stable entity identities, deaths/rewards, living/retired entities, exact layer ownership and logical groups | Implemented. |
| 6.1 | Natural surface-connected cave restoration | Implemented; underground position/items confirmed locally. |
| 6.2 | Permanent connections and exact deeper-layer restoration | Implemented through the three connection stages; see [LayerGenerationConnections.md](LayerGenerationConnections.md). |
| 6.3 | Changed liquid/basin contents or levels | Deferred by agreement. `LiquidBody.SetFill` exists, but current gameplay does not mutate generated fill levels. |
| 7 | Combined verification, profile campaign selection/overwrite behaviour and menu/recovery finish | Implemented finishing changes; automated results below; local checklist remains. |

## Save Contents and Ownership

Each profile has one current campaign slot. **Continue** loads that selected profile's slot. **New Campaign** asks before starting fresh when a slot exists; the old save remains until the new campaign is successfully saved. Separate profiles retain independent campaign files.

A successful paused **Save Game** captures:

- Campaign identity, seed, original spawn, generation settings/resource definitions and world time/eclipse state.
- Exact player global ground position/layer, underground surface-return entrance identity/position, health, stats/modifiers, reserves, inventory/equipment, hotbar, crafting and survival state.
- Changed rocks, trees, plants, ores, ground deposits and consumed/cleared grass, retaining stable identities through chunk retirement.
- Physical drops, optional remaining lifetime, container and loot contents including empty caches, wrecks, placed structures and saved health.
- Entity deaths with reward identities, supported surviving/authored/population/retired actor state, exact layer transfers and logical group membership/formation/roaming/dissolution.

Unchanged seeded terrain, biomes, chambers and natural connections regenerate. Runtime targets, paths, reservations and engine references rebuild. A save requires the selected campaign's living, grounded player on available terrain. Underground saves require a known natural surface return entrance; temporary `TEST_` corridor footprints are rejected.

## Loading, Files and Compatibility

Campaign schema is **version 6** with four named gameplay sections: resource changes, world objects, entity deaths and entity state. Versions 1–5 remain readable under their original supported coverage and upgrade on successful Save. Missing gameplay state from older saves cannot be reconstructed retroactively. Earlier game versions cannot read version 6.

Continue validates the recipe and profile identity, freezes actions/crossings during initialization, reconstructs necessary entrance/nearby route metadata, activates the saved layer, waits for terrain, and restores player/time/items before gameplay resumes. Original spawn remains the generation/respawn anchor. Unknown layers, missing routes, unavailable terrain or incompatible resources fail visibly without silently moving the player to surface.

Files are written through a flushed temporary file and atomic replacement. A readable previous primary becomes the backup. Invalid replacement data cannot overwrite the primary. A readable backup can recover a damaged/missing primary without rewriting it during load. Backup recovery now opens a visible **Campaign recovered** notice with gameplay paused; dismiss it and Resume when ready. Failed loading preserves existing files and offers Main Menu.

The permanent connection planner changed generation resources in connection Pass 2. Saves from before that recipe change may be refused by compatibility checks. Pass 3/Pass 7 do not bypass those checks or change existing save files merely by installing.

## Pass 7 Menu Finish

- Removed obsolete world/player-only and partial-save messages. Save success reports the campaign and exact committed position/layer.
- Main Menu identifies the single campaign slot per profile; Continue explains its selected-profile behaviour and is disabled only when no primary/backup exists.
- Existing new-campaign replacement confirmation remains explicit. Repeated campaign-launch clicks are suppressed during the deferred scene transition; immediate validation failures allow retry.
- Successful backup recovery is shown in-game rather than only in the console.
- Options/autosave remain outside this pass. Multiple named save slots are not implemented.

## Verification and Local Sign-off

Previous automated stage checks cover resource mutation/retirement/regeneration; items, storage/empty loot, wrecks/structures/health; surviving/retired entities, groups, death rewards and duplicate prevention; profile separation, version upgrades, malformed-save primary preservation and backup recovery; exact surface/Upper/deep restoration and natural return routes. Headless fixtures substitute artwork and may control spawning/transitions.

Pass 7 automated checks passed:

- Full production C# compilation, including current audio/notification sources.
- One combined paused campaign capture and fresh-process restoration: player inventory/equipment, restored world time, partial generated-resource work/depleted grass, drop identity/count/layer/optional lifetime, storage, empty loot, and structure health.
- Authored/living/retired/transferred entities, health/home state, logical group reservations/dissolution, repeated remembered-actor scans without duplication, and resaving dormant state.
- Malformed replacement data preserving the primary, readable-backup recovery, independent profile slots, real Save UI success/position text, Continue availability, and new-campaign confirmation cancellation preserving the slot.
- A separate disposable damaged-primary run: visible paused recovery notice, Resume, combined restoration, and transferred-cave actor restoration.
- Installer preview/application/exact payload/rerun and zero-write conflict protection.

Headless checks use fixture actors/containers, controlled population/visibility and artwork substitutes; the structure fixture exercises real creation/capture/restore without an inventory placement transaction. Surface/deep position and natural return-route tests are from the preceding integration pass. These checks do not replace local walking, rendered UI or long-session gameplay tests.

1. Profile A: change inventory/vitals, partially harvest and deplete resources, drop/pick up items, use storage/loot and place/damage a structure. Damage/kill nearby entities. Save, leave chunks, revisit and verify changes.
2. Close completely, select A and Continue. Check exact position, inventory, crafting, world time and changed objects/entities; watch for duplicate items/rewards or regenerated depleted resources.
3. Save and restart on the surface, in Upper Caverns and in natural Deep Caverns. Verify items/layer/position and walk back through natural connections. Disable DeepCavernsTest for these tests. Repeat away from the original surface entrance and near a ramp.
4. Profile B: verify its Continue/save is independent of A. Start a new campaign, cancel the replacement prompt, then start and leave without saving; A/B's previous saved campaign must still exist. Save a new campaign only when deliberately replacing that profile's slot.
5. Check Save success/failure, Exit's unsaved-progress confirmation, Main Menu/Continue, and backup-recovery notice. Do not damage real saves for testing; use a disposable profile/copy.

## Deferred Work

- Liquid/basin mutation persistence (6.3): revisit when gameplay can drain, fill or otherwise change levels/contents. Generated initial fill currently reconstructs from the recipe.
- Autosave/options scheduling; actual dropped-item expiry/countdown.
- Future player-created/modified connections, new gameplay systems, and feature/lair/boss mechanics: add their non-reconstructible state when implemented.
- Complete the local checklist and investigate any reported failures before describing persistence as fully gameplay-verified.
