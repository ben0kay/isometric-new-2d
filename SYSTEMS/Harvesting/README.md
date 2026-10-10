# Harvest profiles — Phase 3

Plants, trees and rocks reference an external `HarvestProfile` through the
species definition's `HarvestDrops` field. Work and mining strength remain
on the species. Every migrated species keeps its original path and ID.

## Editing drops

Open the species `.tres`, expand `HarvestDrops`, then edit `Drops`.
Profiles live beside species, named `<Species>Harvest.tres`. Each entry has:

| Field | Meaning |
|---|---|
| ItemId | An existing registered inventory item ID, such as `bark` |
| MinimumCount / MaximumCount | Inclusive quantity range, 1–100000 |
| Chance | Independent probability: 0 disables; 0.75 = 75%; 1 guarantees |

Maximum 64 entries; duplicate item IDs are rejected. Configure one entry
with the desired range instead of repeating an item. Disabled entries still
validate their IDs. Missing profiles are errors; an explicit empty profile
intentionally harvests the source without rewards. Optional-only profiles
can also legitimately roll no rewards and deplete once.

New species require an explicit profile; changing rewards requires no new
gameplay C#. Legacy HarvestItemId/HarvestUnits/BonusHarvestItems remain for
ore/tool compatibility but no longer control plants, trees or rocks.
An older sprite-batch exporter still writing only those fields must add a
profile before its new species can be harvested. Updating that tool is a
separate task. Ore batches and ground digging remain unchanged until Phase 4.

Each species has its own profile, so editing a migrated species does not
silently alter another species. If you intentionally share a profile later,
all referencing species use the shared settings. Item definitions remain
single resources in ITEMS/Definitions; assigning Bark never duplicates it.

## Installed content

12 definitions were migrated: three rocks; Frond, AlienShrub and both marsh
plants; CarbonTree and the four VerdigrisWilds trees.
Their primary quantities and existing plant bonuses are preserved.
CarbonTree and Wilds Tree01 additionally demonstrate Bark 1–3 at 75%, keeping
their existing one Carbon. No resin or extra Carbon was invented.
Phase3Migration.json records the source/profile paths and reward settings;
it is a migration audit, not a second runtime catalog.

## Performance and reward acceptance

ResourceWorld compiles a profile once per world, resolving catalog IDs into
immutable reward settings. It retains profiles/item resources, never resource
hosts. Runtime quantities belong to the completed batch. Restart the scene
after Inspector edits; cached compiled profiles do not live-refresh.

No new Process/PhysicsProcess callbacks are added. Randomness runs only on
completion. All batch entries validate before any pickups are accepted.
Pickups are prepared together, queued in the source's own layer, and included
immediately by the existing pending-drop save pipeline.
If acceptance fails, the source remains and retries the same prepared batch.

## Randomness and persistence

Harvest-v1 seeds SHA-256 with the world seed, stable layer/source identity
and species resource path. A defined SplitMix64 stream handles independent
chances and unbiased inclusive quantities. Time, node IDs and GetHashCode
are not used. Reload with unchanged configuration reproduces the same roll;
completed/depleted sources never roll again. Partial work uses existing
ResourceChanges records. No reward fields or version changes were added to
the save format. Editing/reordering entries changes uncompleted outcomes;
do not edit live profiles between a failed attempt and reload.

Saves fingerprint generation resources. HarvestRecipeCompatibility allows
only known reviewed pre-migration species fingerprints to match this exact
migrated species payload with its original profile payloads. Other generation
changes remain rejected. A successful resave stamps new profile dependencies
using the normal save mechanism. Older species variants outside the reviewed
baseline remain subject to normal rejection.

**Editing profiles after saving:** current saves also fingerprint profiles.
Changing drop data can therefore require a new campaign. This phase keeps
strict recipe validation rather than broadly bypassing it; general content
hot updates/save compatibility are a separate design task. Do not delete
saves or weaken validation to get around a mismatch.

## Verification

Optional resource/evaluator checks: run the supplied
`Tests/HarvestProfileChecks.tscn` as the current scene in Godot. It loads all
12 actual profile files and checks quantities, independent guaranteed drops,
0%/100%, zero rewards, unknown IDs, duplicates, invalid ranges/probabilities
and deterministic retry rolls. It prints success and exits; failures exit 1.
This scene does not simulate full world persistence or transactions.

Local gameplay checks:

1. Compile C# and open species/profile resources without errors.
2. Gather Frond/Shrub and marsh plants; compare prior quantities and bonuses.
3. Harvest CarbonTree/Tree01: Carbon stays one; successful Bark rolls give 1–3.
4. Partially mine a tree/rock, unload its chunk, return and finish it.
5. Save immediately after harvesting before pickup, quit, Continue and collect
   exactly the saved drops. Resave; depleted objects must remain depleted.
6. Load a pre-Phase-3 save from the reviewed baseline, then resave/reload.
7. Exercise a supported cave layer and check source-layer drop ownership.
8. In a disposable new campaign test an empty profile and an optional-only
   profile; no-roll harvesting must deplete without an infinite retry loop.
9. Check ore batches, ground digging, equipment and crafting still behave
   as before. Restore disposable test edits before returning to real saves.

Static generation checks are recorded in the ongoing-work notes. Godot,
.NET and PowerShell execution were unavailable in the delivery environment;
local compilation and gameplay checks remain necessary.
