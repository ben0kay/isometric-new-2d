Cave implementation plan
Project: isometric-new-2d
Date: 7 October 2026
Status: design proposal; no cave implementation changes made.
Goal
Enter caves through surface openings without loading another complete world scene. Descend through a visible tunnel, explore an underground world, and return through an entrance. Cave biomes have their own catalog and seeded distribution, independent of the surface biome above them.
Use the existing GreyRockSurface shader include for the first cave ground. Keep all cave-specific files in WORLD/Generation/Caves/. Shared surface/cave coordination lives in WORLD/Layers/. Reuse suitable biome resources and selection code from WORLD/Generation/Biomes/.
Delivery process
Before each pass, present its specific plan, new files, existing-file edits and test criteria before giving implementation code. Deliver complete small files and complete replacement functions for larger files in chat. Do not write GitHub or require the user to search for tiny snippets.
The filenames below are the proposed complete new-file inventory for these three passes. Verify current repository paths and integration points before each pass. Announce any necessary inventory change before providing code. Godot-generated .uid and import metadata are not counted as authored files.
Pass 1 — Layer switching and one test cave
Behaviour
Build one deliberate entrance, descending tunnel and chamber in the existing world scene. Reuse the player, inventory, weapons and combat. Introduce Surface and Cave as location states, separate from inventory/debug input modes.
The entrance provides a reversible transition zone. While descending, fade the surface toward the configured obstruction opacity; once inside, hide the surface completely. The entrance transition, rather than screen direction or player Y, controls the layer switch. Use hysteresis/explicit boundaries so standing on a threshold does not repeatedly switch layers.
Render cave ground as projected 2D geometry, using GreyRockSurface through a small cave-ground wrapper shader. Keep cave logical collision positions separate from visual depth offsets, matching the existing terrain approach. Give the cave its own height provider so surface hills do not move underground artwork.
New files — 9
New file	Responsibility
WORLD/Layers/WorldLayer.cs	Shared Surface/Cave enum.
WORLD/Layers/WorldLayerController.cs	Own the player's active layer, transition progress, layer roots and coordinated switch events. Resolve safe entry/return positions and prevent duplicate transitions.
WORLD/Layers/WorldLayerMember.cs	Reusable membership helper for actors and world objects. Preserve original collision settings and restore them when active; identify the layer for targeting and interaction.
WORLD/Generation/Caves/CaveWorld.cs	Cave feature entry point. Create/find the cave roots, register entrances and provide the first test layout. Later coordinate streaming.
WORLD/Generation/Caves/CaveEntrance.cs	Detect entrance traversal and coordinate the reversible descent/ascent with the layer controller.
WORLD/Generation/Caves/CaveEntrance.tscn	Reusable entrance trigger and adjustable tunnel/transition markers. No additional whole-world scene.
WORLD/Generation/Caves/CaveTerrainElevation.cs	Underground logical-height and visual-depth sampling for cave artwork and aiming.
WORLD/Generation/Caves/Rendering/CaveGround.gdshader	Wrap the existing GreyRockSurface include for cave ground, lighting and coordinates. No duplicate rock texture/include.
WORLD/Generation/Caves/DEBUG/CaveLayerTest.cs	Isolated, removable setup for one surface entrance, tunnel, chamber and test targets/objects.


Existing integration points
- World scene: attach the layer controller and cave entry point, using grouped helper roots rather than many loose nodes. Use the test helper to place the first entrance.
- TerrainVisual and terrain-height consumers: resolve the owner's layer-specific height provider; preserve current surface behaviour.
- Player movement and slope checks: choose surface clearance or cave clearance from the active layer.
- Actor combat hitboxes, projectile queries and weapon aiming: filter by layer and use the correct height provider. Update already-active projectiles during transitions so hidden-world targets cannot be hit.
- Gathering, digging, loot/container interaction and auto-pickup: require matching layer membership.
- Surface navigation/enemy activation and spawning: exclude the cave player as a surface target. Keep hidden-layer simulation policy explicit; do not disable the player's shared node with the surface root.
- Surface liquid exposure: stop surface water effects while underground.
- GlobalConfig: add a CAVES group only for truly global transition settings. Reuse the obstruction opacity setting for the surface fade target.
- Existing DEBUG map/HUD: label the active layer. In pass 1, explicitly mark the surface map as surface-only rather than presenting it as a cave map.
These are necessary shared-system edits, not separate cave versions of player, weapons or inventory.
Tests before pass 2
- Surface play behaves as before when the cave feature is disengaged.
- Enter and return repeatedly, including turning around partway through the tunnel.
- Moving upward on screen inside the chamber does not reveal the surface.
- Surface rocks, slopes and water do not affect cave movement or immersion.
- Bullets, melee, gathering, container prompts and pickup affect only the active layer.
- Enemies cannot target the player across layers; hitboxes retain their original team rules.
- Inventory/debug input modes still work independently of location state.
- Death/respawn returns the player to the intended layer and clears partial transitions.
- Cave ground, sprite elevation, draw order and collision remain aligned.
Pass 2 — Seeded cave layout and streaming
Behaviour
Replace the fixed test chamber with connected seeded tunnels and chambers. Entrance placement must connect to a traversable route; do not create isolated landing chambers. Generate floor/wall data before populations and navigation. Share absolute-coordinate sampling across chunk edges.
Use a cave-specific seed stream derived from the world seed. Keep nearby cave geometry active and retire distant artwork/colliders under frame budgets. Retain only the metadata needed for stable regeneration and later persistence. Continue using GreyRockSurface.
New files — 5
New file	Responsibility
WORLD/Generation/Caves/CaveGenerationSettings.cs	Resource for chamber size, tunnel width, density, depth and seed offset. Avoid scattering generation numbers through code.
WORLD/Generation/Caves/DefaultCaveGeneration.tres	Initial cave-generation profile.
WORLD/Generation/Caves/CaveGenerator.cs	Deterministic floor/wall/height sampling and entrance-route connections. Use a coherent connectivity model rather than unconnected random rooms.
WORLD/Generation/Caves/Chunks/CaveChunk.cs	Build cave ground, wall artwork and collision from one consistent sample; expose resumable build/retirement work.
WORLD/Generation/Caves/Chunks/CaveChunkController.cs	Stream nearby cave chunks and navigation, coordinate readiness and entrance preloading with CaveWorld.


Existing files extended
- CaveWorld replaces the fixed-layout source with the generator and streaming controller.
- CaveTerrainElevation reads the same generated height data used by cave meshes.
- CaveEntrance waits for the destination buffer before completing entry. Preload while approaching instead of freezing the player unexpectedly mid-tunnel.
- WorldLayerController selects the correct navigation/availability provider.
- CaveLayerTest exercises multiple entrances and distant travel.
Tests before pass 3
- Same seed gives identical cave geometry regardless of travel direction or chunk build order.
- Every test entrance connects to navigable cave terrain, with safe return access.
- No mesh, wall collision or height seams across chunk boundaries.
- Player cannot walk into unready chunks or through cave walls.
- Streaming/retirement work is budgeted; cache sizes remain bounded during long travel.
- Leaving and re-entering restores the same geometry.
- Surface terrain streaming remains independent and functional.
Pass 3 — Independent cave biomes
Behaviour
Add a separate cave catalog and biome distribution. A single cave biome can extend beneath several surface biomes; cave placement does not use the surface biome ID. Start with one neutral grey-rock biome to validate the architecture. Add contrasting biomes only after choosing their actual appearance and content.
Reuse the existing biome-selection framework where its assumptions fit. Cave biome definitions add cave-specific chamber/tunnel parameters through the resource inheritance/feature system. Optional features inherit shared cave defaults; adding a feature must not require editing every biome resource.
New files — 6
New file	Responsibility
WORLD/Generation/Caves/Biomes/CaveBiomeDefinition.cs	Extend the shared biome definition with cave-specific generation profiles and validation.
WORLD/Generation/Caves/Biomes/CaveBiomeCatalog.tres	Separate catalog referencing only cave biomes. Reuse the catalog resource class if compatible.
WORLD/Generation/Caves/Biomes/CaveBiomeWorld.cs	Own the cave biome sampler, independent seed stream and cave-specific defaults lookup. Reuse BiomeSampler where appropriate; do not borrow the surface world's sampler instance.
WORLD/Generation/Caves/Biomes/CaveBiomeDefaults.tres	Shared cave feature defaults; contains no registration list of individual biome-specific profiles.
WORLD/Generation/Caves/Biomes/Definitions/GreyCaverns/GreyCaverns.tres	First cave biome definition, selecting its own cave terrain/content profiles.
WORLD/Generation/Caves/Biomes/Definitions/GreyCaverns/GreyCavernsGeneration.tres	Grey Caverns chamber, tunnel and depth settings.


Existing files extended or reused
- CaveGenerator blends cave biome generation profiles without disconnecting routes.
- CaveGround continues to use GreyRockSurface until more cave surfaces are selected.
- Shared biome catalog discovery must distinguish surface and cave catalogs. Adding a cave biome must not automatically add it to the surface catalog.
- Shared feature/default lookup must accept the owning world's defaults instead of hardcoding surface BiomeDefaults for caves.
- Existing species/placement resources can describe cave populations where suitable. Only layer-aware spawners with verified cave assumptions are reused.
- Extend the existing DEBUG map with a cave-layer view; do not create a second input/map ownership system.
Tests
- Cave biome boundaries remain independent of surface boundaries for the same world seed.
- Changing only the cave seed/profile does not alter the surface world.
- New cave biomes are registered only in the cave catalog.
- Missing optional cave features inherit cave defaults without touching every biome file.
- Terrain transitions preserve connected, traversable tunnel routes.
- Cave populations, enemies, containers and projectiles remain on the cave layer.
- Debug map displays the selected layer's actual geometry and biome identity.
Proposed new-file total
Pass	New authored files
1: Layers and test cave	9
2: Layout and streaming	5
3: Cave biomes	6
Total	20


Files from earlier passes are extended in later passes instead of adding duplicate managers. No separate player, inventory or weapon implementations; no complete replacement world-test scene; no second GreyRockSurface.
Important design limits
- This remains projected 2D. The underground state represents a separate world layer; alpha fading alone does not separate physics.
- Surface biome blending, shaders and terrain generation remain intact.
- Initial cave rendering is floor/wall based. Elaborate cave ceilings, multiple stacked cave levels, cave liquids and mining walls are later features.
- Surface and cave physics masks must be planned against the project's existing layer allocations before implementation. Do not invent unused layer numbers without checking.
- Inactive-layer simulation needs a deliberate policy: hiding artwork is separate from pausing enemies or streaming chunks.
- Persistent cave harvesting, loot and edits need stable layer-qualified IDs. Establish these before adding persistent cave content; this plan does not promise save/load implementation yet.
- The grey test cave proves traversal and separation first. It does not guarantee the final cave art direction or procedural layout quality.
Next action
Present the detailed pass-1 implementation plan and confirm its exact integration points from the latest push. Then provide the code for pass 1 only. Test it before proceeding to pass 2.


UPDATE

# Cave System

## Purpose

This folder owns cave generation and underground-specific systems.

Caves exist within the same world as the surface. Entering a cave changes
the active world layer rather than loading a separate gameplay scene.

The player can travel underground and return to the surface through
another connected cave entrance.

## World Layers

Surface and cave layers have separate drawing and collision behaviour.

The surface remains briefly visible with reduced opacity during entrance
transitions. Once underground, cave visibility follows the player rather
than revealing the surface above.

Surface generation and cave generation share world coordinates so
entrances can connect the layers consistently.

## Cave Entrances

Cave holes connect surface locations to underground tunnels.

Cave systems may connect multiple entrances or form isolated areas with
only one entrance.

Entrance placement must account for surface suitability. Flat ground
alone is not enough: water, reserved areas, and other blocking features
also matter.

Surface objects should not obstruct entrance and exit areas.

## Cave Biomes

Cave biomes are independent of surface biomes.

A single underground biome can extend beneath several different surface
biomes. Underground biome boundaries do not need to match surface
boundaries.

Surface information may influence specific underground features where
useful, without determining the cave biome itself.

## Organisation

Keep cave-specific generation, biome definitions, settings, and related
resources inside WORLD/Generation/Caves.

Give each cave biome its own folder containing its related resources.

Reuse existing shared biome tools where they fit. Avoid copying surface
systems solely to give caves a separate implementation.

Shared ground shaders remain in the shared ground folder. Cave biome
resources reference those shaders rather than owning duplicate copies.

GreyRockSurface is the initial shared cave surface.

## Generation and Streaming

Caves should generate consistently from the world seed and absolute
world coordinates.

Keep lightweight surface information available when underground systems
need to evaluate potential entrances. Full surface objects and visuals
should be prepared as needed near a surface transition.

Budget expensive generation and landing checks across frames to avoid
freezes when entering or leaving caves.

## Ore Direction

Underground ore will use the shared ore deposit and artwork systems.

The intended distinction is:

- Surface deposits: poorer deposits with lower available quantities.
- Underground deposits: richer deposits with greater available quantities.
- Artwork: distinct appearances that communicate deposit richness.

These are planned content differences, not separate mining systems.

Cave biomes will eventually determine which ores appear and their
frequency. Ore placement is a later pass.

## Next Planned Pass

Introduce two cave biomes to test underground variety.

Each should have a clear visual identity and independently configurable
generation settings. Their exact terrain, surfaces, and distribution
will be discussed before implementation.

The first goal is to verify that underground biome regions can extend
beneath multiple surface biomes and remain consistent while exploring.

## Maintenance Rules

- Keep drawing, collision, and transitions coordinated by the active layer.
- Keep biome settings data-driven where practical.
- Keep species and ore artwork separate from generation rules.
- Preserve existing surface terrain blending.
- Avoid full surface generation merely to query a biome above a cave.
- Test entrance A, underground travel, and exit B after generation changes.