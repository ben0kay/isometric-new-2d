# Layer Generation and Connections

## Goal

Support any number of defined depths, including future Hell layers. Query seeded world information without loading physical chunks. Reconstruct natural connections from the seed and save gameplay changes separately.

## Rules

- Every layer has a stable ID, biome/generation settings and depth. Future connection rules name their destination layer explicitly; never infer it from names or assume Deep Caverns is the final layer.
- The upper layer owns a downward connection. Either endpoint can discover the same seeded record without visiting the other endpoint first.
- Use a spaced candidate grid with deterministic probability checks. Connection frequency scales candidate probability, not chunk counts or guaranteed entrances. Terrain suitability and spacing still apply.
- The lower endpoint joins the corridor to its own chamber network. Each endpoint uses its own biome and terrain rules.
- Queries sample logical global ground coordinates. They reuse generation services and do not activate layers, build chunks or spawn objects.
- Avoid recursive generation dependencies. Establish base biome data, then independently planned features, then physical content. Surface flowers may consult a planned underground lair before that lair is spawned.
- Keep queries local and caches bounded. Do not scan every depth for every tile or retain physical terrain for unvisited areas.
- Same seed, original spawn, generation settings and generator version reproduce the same base world. Seed alone is not a compatibility guarantee after generation rules change.

## Global Frequency Tuning

`CONFIG/GlobalConfig.cs` exposes `LayerConnectionFrequency` on CONFIG in the Inspector, keyed by the **upper/source layer ID**.

- `surface = 1`: existing surface candidate probability.
- `underground_1 = 0.5`: half the future downward candidate probability.
- `underground_2 = 0.25`: quarter the future downward candidate probability.
- `0` disables new natural downward connections from that layer; `2` doubles probability, capped at 100%. Allowed multiplier range is 0–8.
- A defined layer without an entry defaults to 1. Add an explicit setting for Hell or another new layer when adding its definition. Unknown IDs and invalid multipliers are rejected.
- This is generation tuning: restart after changing it and use a new campaign when assessing a changed layout. Campaign settings restore the tuning captured for that campaign.
- Pass 1 wires the surface setting into the existing entrance sampler. Underground settings are available to queries but do not create deeper connections until Pass 2. Debug connections are unaffected.

## Implementation Stages

1. **Queries and tuning — implemented by this installer.** Shared exact-layer biome query facade reusing current samplers; central frequency dictionary; surface probability integration; preserve current surface behaviour at multiplier 1.
2. **Permanent connection planner — pending.** Explicit layer-pair rules, seeded stable connection IDs, discovery from either side, bounded planning/caching, valid corridor endpoints and shared spacing rules. Replace reliance on the temporary deep-cavern test without making that debug script permanent save data.
3. **Restore integration — pending; completes save Pass 6.2.** Reconstruct nearby routes before terrain/player activation, support loading at any defined depth, preserve necessary route discovery/pins and verify travel back through multiple layers. Save only non-reconstructible identities or gameplay modifications.
4. **Return to save Pass 6.3 and Pass 7.** Liquid/basin changes, then combined persistence and menu verification.

## Save Boundaries

Seeded biomes, base chambers and unchanged natural corridors are reconstructed. Player layer/position, harvested vegetation, destroyed resources, entities, items, buildings, boss defeat and changed/player-created connections are persistent gameplay state. Feature/lair planning and flower hooks are future work; Pass 1 supplies biome queries only.

## Checks

Biome queries must match the existing samplers at the same position, work for inactive layers, leave chunk counts/active layer unchanged and reject unknown IDs. Frequency 1 preserves surface decisions; 0 rejects normal surface probability checks; 0.5/2 scale and cap candidate probability. Existing explicit debug probability bypasses remain intact. Test loading unexplored routes from below when the permanent planner is implemented.

Pass 1 automated verification passed: biome equality against existing samplers at positive/negative coordinates for all three currently defined layers; unchanged underground chunk counts/active layer; multiplier scaling/capping, normal surface 0/1 decisions and explicit debug bypass; invalid IDs/values; typed dictionary save-recipe roundtrip and migration of recipes missing the setting. Local visuals and changed-layout exploration still need gameplay checks.
