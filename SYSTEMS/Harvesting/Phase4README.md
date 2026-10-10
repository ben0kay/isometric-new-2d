# Phase 4: ore batches and ground yields

## Extraction behavior

Ore mining still creates loose pickups in the source's own world layer.
It does not insert rewards directly into a backpack. A full backpack leaves
pickups on the ground; existing slot/weight/volume checks run during pickup.
The previous single-item callback is now a complete-batch callback. The
world validates and prepares every pickup before accepting any reward.
Only after acceptance does the deposit subtract primary units/reset work.
Pending pickups remain included in immediate saves through the Phase 3 pipeline.

Primary ore: YieldItemId (or a canonical YieldItem resource), TotalUnits,
UnitsPerBatch, WorkPerBatch and RequiredMiningStrength retain their meaning.
The last partial batch gives exactly the remaining primary units.
YieldItem and YieldItemId must agree if both are supplied.
Bad settings/unknown IDs fail before extraction, rather than silently clamping.

## Editing bonus drops

Edit WORLD/Contents/Ores/Iron/IronBonusHarvest.tres in Godot.
The example gives Rock 1–2 independently at 35% per successful batch.
Iron's original primary yield and work are unchanged.

A new ore can reference its own external HarvestProfile in BonusDrops.
Each entry uses ItemId, MinimumCount, MaximumCount and Chance (0–1).
Null or an explicit empty BonusDrops profile means no bonus.
Primary ore IDs cannot appear in bonuses; duplicate bonus IDs are rejected.
At most 63 independent extras plus the primary reward are supported.
All IDs must already be registered in ITEMS/ItemCatalog.tres.

Bonuses roll ONCE PER BATCH, including a final partial batch. Their quantity
is not multiplied or prorated by primary units. Chances are independent,
not weighted choices. Optional bonuses never suppress the primary ore.

The seed includes world seed, stable source/layer identity, ore definition
path and extracted-unit progress before this batch. A failed payout keeps
the prepared batch; reloading reconstructs it from saved remaining units.
No random outcomes/save sections are added. No per-frame scans are added.
Restart the world after editing profiles. Reordering/changing bonus settings
can change outcomes and normally requires a new campaign after resaving.

## Ground resources

Sand and Clay remain in GroundResourceCatalog.tres as separate subresources.
Their ItemId references resolve through the same external item catalog.
One successful digging event produces one loose unit before decrementing
remaining quantities. Ground shader/deposit generation is untouched.
Catalog validation now rejects duplicate material IDs, missing references,
invalid numerical data and oversized quantities.
Ground does not gain bonus profiles in this phase.

## Existing saves

CampaignRecipe accepts only the exact reviewed IronDeposit migration with
its exact installed bonus profile and a known pre-migration fingerprint.
Unrelated generation changes remain rejected. The Phase 3 allowance stays.
Resaving records the normal current ore/profile fingerprints. Subsequent
profile edits may need a new campaign. No save JSON is edited and no campaign
or resource-change version is bumped.

## Local verification

Close Godot, preview/apply the installer, reopen and build C#.
Run SYSTEMS/Harvesting/Tests/OreBatchChecks.tscn independently if desired.
It checks actual resources, ranges, final partial quantities, deterministic
retry rolls, null/0%/100% profiles and invalid settings. It has not been run
in the remote environment; it does not test full live-world persistence.

1. Mine Iron: normal iron units still drop; several batches sometimes also
   drop 1–2 Rock. Exhaust a deposit and confirm it disappears.
2. Use a weak tool: no progress/rewards. Leave partial mining work, save,
   quit completely and continue: work and remaining primary units restore.
3. Save immediately after extraction before collecting: both accepted ore
   and bonus pickups survive, without restoring consumed deposit units.
4. Leave the chunk and return: remaining/depleted state stays correct.
5. Fill backpack slots, weight or volume: loose drops stay on the ground;
   free capacity and collect them without duplication or loss.
6. Test an underground deposit: rewards stay in that deposit's layer.
7. Dig Sand and Clay: one-unit drops, partial work and remaining patches
   persist after quit/continue and chunk reload.
8. Continue a reviewed existing campaign, resave and continue again.
9. In a disposable NEW campaign, use TotalUnits=7, UnitsPerBatch=3: primary
   batches must be 3,3,1; bonuses roll once on all three batches.
10. For an integration harness, pass a callback returning false to OreDeposit.Mine:
    remaining units must stay unchanged and work remain capped. Retry with
    an accepting callback: same reward batch; exactly one subtraction.
    The normal mining emitter uses ResourceWorld.SpawnHarvest, not inventory.

Source/static checks are separate from compilation and gameplay validation.
