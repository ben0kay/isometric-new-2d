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

## Optional burial experiment

Enabled on the four variants for this test. In each LargeRockNN.tres, expand
**Burial** and untick **Burial Enabled** to restore its original artwork.
Depth Pixels defaults to 14 texture pixels; Edge Pixels (2) controls the small
unevenness and Softness Pixels (1) controls the narrow alpha blend at the join.
Set Edge Pixels to 0 for a straight cutoff. Restart the test world after editing;
these settings are applied when an instance is created, not updated every frame.

The effect moves only the sprite down and clips it at the terrain visual's ground
anchor. PNG bytes, actor position, collision, mining, depth sorting, seed choices
and existing shadows are unchanged. The optional shader reuses WorldSurface
lighting and normal-map settings. Matching rocks share a cached material per
variant/source; there is no image-alpha scan, extra texture or per-rock updater.
No contact-shadow or sprite-shadow change is included in this experiment.

As with the other variant-resource edits, changed .tres fingerprints are detected
by campaign validation. Use a fresh test campaign to compare edited settings;
existing saves are not silently migrated or altered. This is a separate commit
from LargeRock integration and can be reverted as a whole.

Compilation and focused headless enable/disable, ground-line alignment,
material sharing, preserved lighting, collision and harvesting checks passed.
The final visual appearance and GPU cost still need local gameplay observation.

## Baked ground patches

`CONFIG/GlobalConfig.cs` exposes `RockPatchesEnabled`, `RockPatchSize`,
`RockPatchOpacity`, `RockPatchPebbleDensity`, and soil/stone colours. Restart
after edits. Disabling patches removes only soil/gravel; set each variant's
`BurialEnabled` false to restore its original unburied artwork. Burial defaults
to 22 texture pixels and never changes the collision node or authored footprint.

`RockPatchSurface.gdshaderinc` runs only in four shared 256px texture bakes.
`ArtworkBaker` persists premultiplied PNGs in `user://artwork_cache`; matching
settings reuse them next launch. Density/colour changes invalidate the cache.
Size and opacity affect only display and do not require a bake. Each rock adds
one static ground sprite; no pebble has a node, collision, shadow, or processing.
Runtime shader samples the finished texture and applies current flat-ground
world sunlight; the procedural soil/pebble loops never run each frame. Patches
follow terrain elevation, scale and mirroring, and disappear with their rock.

Ground patches are local flat overlays, best suited to relatively level terrain;
they do not deform over cliff edges or independently follow sloped terrain.

Run `Tests/RockPatchChecks.tscn` with F6 to verify actual texture pixels, shared
bakes, cache invalidation, disabled behavior, draw order and collision independence.
The existing LargeRock checks disable patches so they remain renderer-independent.
