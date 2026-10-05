# Remember

Ideas to implement later. These are design notes, not implemented features.

## Fertile soil

### Core idea
- Fertile soil appears as visible patches on the ground terrain.
- The player can plant crops directly in a natural fertile-soil patch, or dig up its soil and carry it home to create a farm.
- Each patch has a finite remaining soil volume. Digging removes some volume and adds the corresponding soil to the player's inventory.
- The patch visibly gets smaller and smaller as its soil is removed. At zero volume, the fertile patch disappears completely, exposing the underlying terrain.
- Collected soil has both volume and weight, so existing backpack space, volume and weight limits apply.

### Suggested implementation
- Track initial and remaining soil volume in litres, with a configurable usable topsoil depth.
- Derive the remaining patch area from volume and depth. For a similarly shaped patch, scale its linear dimensions by sqrt(remaining volume / initial volume), rather than scaling width directly by the volume ratio.
- Use an irregular patch shape or depleted sections so excavation can look natural; preserve logical soil quantities independently of the artwork.
- Soil items define weight per litre through bulk density. Use the same units for excavation, inventory and placing soil to prevent duplication or loss.
- Only remove as much soil as the inventory can accept. If capacity is insufficient, refuse the dig or transfer a smaller valid amount.
- Placing collected soil creates or expands a cultivated bed. A bed needs sufficient soil depth/volume for its crop; a tiny amount should not create unlimited planting space.

### Survival realism suggestions
- Treat the deposit as fertile topsoil over ordinary ground, rather than implying the entire terrain vanishes when the patch is exhausted.
- Require suitable tools and time/effort to excavate. Large amounts are better transported in sacks, a cart or vehicle than a backpack.
- Consider moisture, fertility and crop-specific soil depth later. Wet soil can weigh more; dry soil still occupies volume.
- Let compost or organic matter replenish fertility over time without creating soil volume from nothing.
- Handle planted crops before excavating their occupied soil: require harvesting/uprooting, or clearly warn that digging will destroy them.
- Distinguish fertility from moisture: fertile soil still needs water, suitable light and temperature.

### First playable pass
Finite soil patches -> dig into inventory -> carry home -> place a soil bed -> plant one crop type. Add detailed moisture, nutrient depletion and compost systems after this loop works.
