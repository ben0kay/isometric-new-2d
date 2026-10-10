# Layer Generation and Connections

## Status

The three planned connection stages are implemented. Restore integration also completes save Pass 6.2. Local gameplay verification remains part of the save checklist in [SAVEMECHANICWORK.md](SAVEMECHANICWORK.md).

| Stage | Implemented behaviour |
|---|---|
| 1. Queries and tuning | Exact-layer biome queries without activating layers or building chunks; global connection-frequency multipliers. |
| 2. Permanent corridors | Explicit downward layer targets; seeded cave corridors discoverable from either endpoint; local preparation, bounded caches and spatial lookup. |
| 3. Restore integration | Save at any defined natural underground depth; prepare nearby routes before activation; restore exact position and retain the original surface return entrance. |

## Layer Rules

- Every layer has a stable ID, depth, biome catalog and generation settings. There is no maximum depth or assumption that Deep Caverns is the final layer; additional depths such as Hell require definitions and explicit incoming target rules.
- Surface entrances use the existing seeded surface planner. Underground definitions expose `DownwardLayerId`, `ConnectionChance`, `ConnectionSpacingTiles` and `ConnectionLengthTiles`. The target must exist and have a greater depth index.
- The upper layer owns each downward connection. Seed, stable layer-pair IDs and absolute candidate cells produce the same record whether discovered from above or below. Permanent corridor IDs are stable `L_...` identities.
- The source chamber joins the corridor mouth; its lower end joins the destination's chamber network. Source elevation uses base biome sampling. Conflicts between permanent pairs sharing a layer resolve deterministically from candidate data and ID priority.
- Connections are probabilistic and deliberately sparse. Configured spacing is a minimum; conservative geometry clearance may increase effective spacing. A higher chance does not guarantee a corridor in every chunk.
- Original spawn, seed, generation settings/resources and generator behaviour jointly determine compatibility. Seed alone cannot preserve a world after generation rules change.

## Global Frequency Tuning

Select **CONFIG** in `WORLD/Scenes/world_infinite.tscn` and expand **Layer Connection Frequency**. Dictionary keys name the upper/source layer.

| Key | Default multiplier | Current meaning |
|---|---:|---|
| `surface` | 1 | Surface to the catalog's surface-entrance underground layer. |
| `underground_1` | 0.5 | Upper Caverns to Deep Caverns. |
| `underground_2` | 0.25 | Reserved for a downward target when one is configured. |

`0` disables normal downward candidates; `1` uses base chance; `2` doubles it, capped at 100%. Allowed multipliers are 0–8. Missing entries for defined layers default to 1; unknown IDs/invalid values are rejected. Debug probability bypasses are unaffected.

Campaign recipes capture these settings and restore them on Continue. Restart and use a new test campaign to assess Inspector tuning changes. For quicker corridor testing, temporarily use `underground_1 = 8`, then restore 0.5.

## Queries and Streaming

`WorldGenerationQueries.BiomeAt(layer, logicalGlobalPosition)` reuses the exact layer's sampler, including inactive layers. Queries do not load chunks, activate layers or spawn objects. Feature/lair planning and surface flowers reacting to deeper features are future work.

Chunk preparation builds local metadata before terrain samples it. Lookup is indexed by touching layer pairs and corridor footprints. Each pair targets 512 cached decisions, including empty decisions; protected loaded working sets may exceed the target. Evicted unpinned records unregister their spatial entries and markers and can regenerate later. Chunk leases and the remembered surface entrance protect required metadata.

Continue first reconstructs the saved surface return entrance, then plans around the saved position with a temporary lease and a 2 ms per-frame planning budget. The saved layer activates only after planning; real chunk streaming takes over protection once terrain is ready. Intervening route segments regenerate as approached; no whole-journey scan or physical loading of every depth is needed.

## Persistence Boundaries

Base biomes, chambers and unchanged natural corridors regenerate. Exact player layer/position, original return entrance identity, and supported gameplay changes belong to the save system. Changed/player-created connections do not yet have gameplay mutation mechanics or a persistence section.

`DeepCavernsTest` is optional temporary geometry. Disable its **Enabled** property when testing natural corridors. Saving inside a `TEST_` corridor footprint is rejected. Pass 2 changed the generation recipe; older recipes may fail existing compatibility checks. Pass 3 did not introduce another schema/resource change.

## Verification

Automated checks cover biome-query equality/no activation, frequency validation, fresh-process upper-first/lower-first corridor equality, duplicate prevention, landing/controller descent and return, lower-room floor continuity, metadata eviction/pins/spatial cleanup, and a real deep save followed by fresh-process exact-depth restoration and return through Upper Caverns to surface. Guarded installers check preview, application, rerun and zero-write conflict rejection.

Headless tests use fixed seeds, increased test frequency, artwork substitutes and controlled transitions. Local walking, visuals, camera/collision seams and long-distance travel still need gameplay checks. The planned connection phase is complete; future feature planning and mutation mechanics are separate work.
