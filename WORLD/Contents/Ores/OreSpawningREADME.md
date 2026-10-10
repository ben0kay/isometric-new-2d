# Surface ore generation — first pass

Code and settings live beside the ore actors in WORLD/Contents/Ores.
This pass permits Iron in all surface biomes. Underground ore population and
per-biome overrides are future work. Content Pass 4 must be installed first.

## Settings

Edit DefaultOreSpawnSettings.tres in Godot:
- Enabled: global on/off.
- Definition: existing external OreDefinition; initially IronDeposit.tres.
- ChancePerChunk: 0.15 by default. One selection roll per chunk.
- PlacementAttempts: six candidate positions in a selected chunk; at most one ore.
- ClearancePixels: additional footprint padding against solids/reservations.
- MaximumHeightVariation: normalized terrain-height difference at nearby samples.

15% is before terrain/collision rejection, so actual density is lower than
one deposit per seven chunks. No ore is guaranteed near player spawn.
Placement respects the spawn-clear radius, basins, holes/chasms and solids.
It runs before rocks/trees, under the existing chunk activation frame budget.
Independent random streams preserve existing rock/vegetation random sequences;
those objects avoid the newly occupied ore footprint in new campaigns.
No per-frame ore update or scan is added. Deposits belong to chunk.Obstacles
and are removed using the existing retirement pipeline.

## Saves

Settings are exported on ChunkController.OreSpawns and automatically captured
and fingerprinted by CampaignRecipe, including the referenced ore/drop resources.
New campaigns inherit DefaultOreSpawnSettings.tres. Existing campaigns that lack
OreSpawns are restored with null settings and keep generated ore disabled.
No new section/version or save JSON edits. This preserves old map recipes.
Changing placement settings after saving can require a new campaign.

Candidate identity uses surface/ore/chunkX/chunkY/attempt. Binding occurs before
scene attachment. Partial records restore their exact recorded position/size,
remaining units and work. Depleted records reserve the chunk's one ore slot;
no alternate candidate replaces a mined deposit. Ordinary chunk unloads never
record depletion. Unchanged recipes reproduce unmodified candidate streams;
actual placement also depends on existing object/terrain reservations.

## Install and test

Close Godot; preview/apply InstallOreGenerationPass1.ps1 from the project root.
Reopen and build C#. This installer does not alter loading UI, biome membership,
ore rewards, or existing placed test deposits.

1. Start a NEW campaign using world_infinite; an old Continue keeps ore disabled.
2. Explore multiple chunks. Iron should be uncommon, can occur in any surface
   biome, and should avoid water, holes, spawn clearance and solid overlap.
3. Mine one partly. Save, fully quit, Continue: position/work/remaining units persist.
4. Exhaust one, unload its chunk and return; save/quit/Continue: no replacement ore.
5. Save immediately after mining with drops uncollected: iron/bonus pickups survive.
6. Continue an older campaign: its original no-generated-ore world still loads.
7. Optional standalone scene: Tests/OreSpawnChecks.tscn validates actual resources,
   zero/full chance, a large selection sample and stable candidate identities.

For a quick density check use ChancePerChunk=1 in a disposable new campaign;
terrain can still reject candidates. Restore 0.15 and use a fresh campaign afterward.
world_test's three placed deposits also remain available for reward testing.
Static checks are not a substitute for local Godot compilation/gameplay tests.
