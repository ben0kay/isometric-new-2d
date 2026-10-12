# LargeRock imported family

One gameplay species (`large_rock`) selects four equally weighted PNG variants.
The original SPRITES/Rock01.png–Rock04.png bytes were moved into the matching
LargeRock01–LargeRock04 folders, renamed and referenced directly. No replacement
artwork or per-spawn pixel scanning is used.

## Tuning

- LargeRock.tres: shared SizeRange (0.9–1.1), MirrorChance (0.5), harvesting and variant list.
- Each LargeRockNN.tres: GroundFootprint (width/depth), VisualHeight and SpawnWeight.
- Expand that variant's Visual resource for ArtworkScale, ImageAnchor, Offset,
  lighting and optional ground-shadow settings. ArtworkScale is authored uniformly
  (0.8 on both axes); the instance SizeRange multiplies artwork, footprint and height together.

Initial base-instance footprints/heights, in world units:

| Variant | Width | Depth | Height |
|---|---:|---:|---:|
| 01 | 144 | 60 | 132 |
| 02 | 166 | 52 | 96 |
| 03 | 104 | 60 | 155 |
| 04 | 142 | 66 | 160 |

These are approximations of ground contact rather than the PNG silhouette.
They can be refined after walking around the rocks in-game. Changes to these
resources alter the generation recipe and require a new campaign for layout testing.

## Generation and gameplay

RockSpawner retains BiomeScatter candidates, seed-derived RNG, chunk budgets,
spawn exclusion, ground clearance and WorldPlacement obstacle/basin/cave/persistent
reservation checks. It selects the variant and uniform size before checking the
actual footprint; the actor uses the same dimensions. All active surface biome
references, including both legacy fields and BiomeContent, now select LargeRock.
RocksPerChunk remains Basalt 8, Mountains 5, Rolling 3, Forest 2, Marsh 1, Wilds 2.
These are candidate frequencies; wider rocks may reduce accepted counts.

Mining still uses ResourceHarvest, 18 work, strength 1, and the shared profile:
one `rock` item with 100% probability. Sprite setup reuses TerrainVisual, terrain
elevation, depth sorting and shared lighting. Automatic obstacle shadow mode
preserves the existing footprint-based sun shadow; no second sprite projection
or new per-object shader is introduced. The new actor branch never uses the
primitive rock atlas or silently falls back if its image is missing.

Legacy SmallRock/Boulder/CliffRock resources and shared atlas code remain available
for old references and generic obstacles. They are not selected by active biomes.
Future rock families can reuse the same actor, variants and spawner; optional
slope/elevation placement rules can be added to the family and evaluated before
WorldPlacement. No speculative cliff-face generation is added now.

## Campaign compatibility

**Start a new campaign after pulling this change.** Consolidation changes biome
resource hashes, species paths, accepted footprints and RNG consumption. Existing
campaigns using the old recipe are expected to be refused by the unchanged save
compatibility checks. Old save files are not edited, depleted old species are not
silently remapped, and no fingerprint whitelist was added. Retaining legacy files
alone does not make an old campaign compatible; restoring the prior repository
version is the way to revisit its old layout. New LargeRock changes retain the
existing layer/chunk/candidate identity format and depletion reservations.

## Verification

C# compilation (all current project sources plus Godot source generators) passed.
Tests/LargeRockChecks.tscn runs headless/F6 checks for the real scatter/spawner
replaying 25 chunks identically, seeded choices, all four
variants, uniform size/mirroring, six biome counts/species, real actor rectangle
collision and PNG selection, solid overlap rejection, mining strength/partial
work, exact harvest rewards, depletion on chunk replay, saved change records and
changed-recipe rejection. A deliberately empty variant and an imported family without variants fail validation.
A full world-scene visual test could not complete in the headless renderer because
other existing objects await rendered atlas baking; no full gameplay pass is claimed.

Local gameplay checks: new campaign; explore all four silhouettes; walk around
wide/tall collision edges; mine a rock; leave/revisit and save/restart after mining;
check elevation, daytime/night lighting and single sun shadows. Visual fit and
actual walking remain local sign-off.
