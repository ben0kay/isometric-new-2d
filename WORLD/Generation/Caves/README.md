# Cave System

## Purpose

This folder owns underground terrain generation, cave biomes, entrances,
and cave streaming.

The game uses projected 2D terrain. Underground areas are separate world
layers within the same gameplay scene, sharing the player, inventory,
equipment, and other common systems.

The goal is seamless exploration from the surface into caves and,
eventually, through multiple progressively deeper underground layers.

## Current Implementation

Pass 1 uses stable string identities: surface and underground_1.
WorldLayerDefinition supplies display names, depths, generation kinds,
and biome references. WorldLayerCatalog validates and resolves those IDs.
The old fixed WorldLayer enum has been removed.

Only the existing surface and first cave are instantiated currently.
Independent additional layer worlds and generalized connections are
planned for passes 2 and 3.

Entrances connect surface locations to underground tunnels. Each entrance
has a transition ramp joining the surface elevation to the cave floor.

The surface fades during the transition and becomes hidden underground.
Layer coordination controls visibility, collision, and interaction
eligibility.

Cave geometry streams in chunks around relevant locations. Floor and wall
drawing are batched, and collision follows the generated floor layout.

Generation uses seeded world coordinates so cave layouts remain
consistent when chunks are recreated.

## Cave Biomes

Cave biome distribution is independent of surface biome distribution.

One underground biome can extend beneath several surface biomes.
Underground boundaries do not need to match the surface above.

The first two cave biomes are:

- Open Caverns: large, irregular chambers with broad connecting passages.
- Winding Passages: smaller chambers with narrow, curved tunnels.

Both currently use GreyRockSurface and flat floors.

Each biome has its own terrain generator while sharing common chamber,
passage, and floor sampling tools.

The current wall outlines vary with the generated layout, but wall
geometry remains tile-based. Smoother wall rendering is future work.

## Organisation

Cave-specific systems live in WORLD/Generation/Caves.

Biome definitions follow this structure:

WORLD/Generation/Caves/Biomes/Definitions/<BiomeName>/

Each biome folder contains its definition, specialized terrain generator,
content resources, and future biome-specific settings.

Shared ground shaders remain in the shared ground folder.

Reuse compatible biome resource types:

- BiomeCatalog
- BiomeContent
- BiomeVegetation
- BiomeSpecies

Caves use their own catalog and resource instances. Cave definitions must
not be registered in the surface catalog.

Shared content settings do not automatically make surface spawning code
suitable for caves. Underground placement must respect cave walls,
floor clearance, entrances, and layer membership.

## Content and Ore

Cave content resources are established, but underground vegetation and
ore placement are a later pass.

Ore deposits reuse the shared mining and replacement-artwork systems.

Planned differences:

- Surface ore is generally poorer and less abundant per deposit.
- Underground ore is generally richer.
- Deposit artwork communicates its resource type and richness.
- Cave biomes determine resource selection and placement frequency.

These are content differences, not separate mining implementations.

## Planned Multiple Layers

The current two-layer system will be extended to support additional
underground depths.

Each layer will have a data-driven definition containing:

- Stable internal ID.
- Player-facing display name.
- Depth index.
- Logical elevation.
- Biome catalog.
- Generation settings.
- Content and progression modifiers.

Example identities:

surface       -> Surface
underground_1 -> Upper Caverns
underground_2 -> Deep Caverns
underground_3 -> The Abyss

These names are examples, not required hardcoded states.

Code and future saved data use stable IDs. The HUD uses display names.
Depth is a separate value rather than being parsed from an ID.

Adding a layer should mean adding a definition and connections, without
adding another named state throughout the project.

## Layer Types and Biomes

Layer identity, layer type, and biome are separate concepts.

A layer has a unique identity. Its type describes whether it is surface
or underground. Its local biome describes the geography and content.

The same cave biome can appear at several depths with different content
or progression settings, reusing its terrain generator.

Each underground layer has its own seeded biome distribution and layout.

The HUD may eventually show both layer and biome, for example:

Deep Caverns — Winding Passages

## Connections Between Layers

Future connections explicitly identify:

- Source layer and location.
- Destination layer and location.
- Transition route and landing requirements.

A connection must not assume that every cave exit leads to the surface.

The intended traversal is:

Surface -> Upper Caverns -> Deep Caverns -> further depths

Connections may also return upward or link to another suitable location.

Destination terrain and landing clearance must be ready before traversal
completes. Returning through a connection should remain reliable.

## Layer Isolation

Collision, targeting, interaction, navigation, and content ownership must
identify the specific layer.

Being underground is not enough to establish that two objects can
interact: objects on different underground depths must remain separated.

Generated object identities must include their layer so future
persistence distinguishes identical coordinates at different depths.

Persistent harvesting, loot, and terrain edits are future work. Stable
layer-qualified identities should be established before implementing them.

## Streaming and Performance Direction

Reuse underground generation and streaming systems across depths.

Do not fully generate every layer simultaneously. Keep the active area
and relevant connection destinations prepared.

The planned scheduling direction is:

- One shared generation budget across participating layers.
- Destination preloading when approaching a connection.
- Priority for required terrain and safe landing areas.
- Lower priority for less urgent content.
- Temporary retention of departure chunks when useful for returning.
- Per-layer loading distances and content settings.

Shared scheduling does not require identical settings for every layer.

Generation operations must be small enough to yield between frames.
A frame budget cannot prevent a large indivisible task from causing a
spike.

Transition lag still needs measurement. Surface construction is a
possible contributor, not an established diagnosis.

## Planned Work Order

1. Establish layer identities and definitions (pass 1).
2. Support independent runtime layer worlds (pass 2).
3. Generalize connections and test two underground depths (pass 3).
4. Add richer underground content after traversal is reliable.

See NOTES/OngoingWork/README.md for the migration handoff.
Transition profiling and a shared generation scheduler remain separate
future work. Additional playable underground layers are not implemented
by pass 1.

## Maintenance Rules

- Preserve existing surface terrain blending and generation.
- Keep layer transitions separate from inventory and debug input modes.
- Reuse the player, inventory, weapons, and common entity systems.
- Keep terrain generation separate from content selection.
- Avoid duplicate cave implementations for each depth.
- Keep expensive work budgeted and caches bounded.
- Keep drawing, collision, and sampled floor height consistent.
- Verify same-seed generation across chunk boundaries and travel directions.
- Test entering, reversing on a ramp, exiting elsewhere, and returning.
- Verify that objects cannot interact across different layers.