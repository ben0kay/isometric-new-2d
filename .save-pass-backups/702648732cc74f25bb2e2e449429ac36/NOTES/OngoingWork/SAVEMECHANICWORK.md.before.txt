| Pass | Scope | Check before moving on |
|---|---|---|
| **1. Profile-owned save foundation** | Campaign identity, versioned save format with named sections, safe temporary-file replacement and backup recovery. Connect the existing pause Save button and basic New/Continue flow. | Profiles A and B save/load separate campaign metadata; failures preserve the previous save. Clearly mark this as partial persistence. |
| **2. World and player restoration** | Generation configuration, world time/eclipse phase, original spawn, player stats/reserves, inventory/equipment/hotbar and crafting. Establish controlled startup and restoration. | Quit and reload on the surface with the same player state and world layout. |
| **3. Generated resource changes** | Shared generated identities; rocks, trees, plants, ores, ground deposits and consumed/cleared grass. Integrate with chunk regeneration. | Harvest, leave until the chunk unloads, return, then quit/reload: changes remain. |
| **4. Items, containers and structures** | Loose drops, ordinary storage, loot containers, wrecks, buildings and health. Add records where only live nodes exist today. | No lost or duplicated items; empty containers stay empty; buildings survive. |
| **5. Entities and groups** | Population records plus live actors, persistent deaths, transferred actors and relevant group state. Coordinate death rewards with Pass 4. | Damaged, dead and transferred entities restore correctly without duplicate loot or wildlife. |
| **6. Underground restoration and liquids** | Complete exact-depth loading, required connections, basin changes and saves on entrance ramps. | Save/load in Surface, Upper Caverns and Deep Caverns, then traverse back successfully. |
| **7. Complete-save verification and menu finish** | Load Campaign selection, overwrite handling, recovery messages and combined regression checks. | One save restores every supported section across profiles and unloaded chunks. |

## Current Save Progress

- Passes 1–3: implemented. Surface position fix confirmed locally.
- Pass 4: installer supplied; items, containers, wrecks, structures and health; optional drop lifetime stored, no expiry countdown. The reviewed push 3ccdd95 does not yet include this installer's files. Verify local installation and push before the next code review.
- Pass 5 Stage 1: this installer adds stable population/scene entity IDs, saved deaths and coordinated reward identities. Existing saves upgrade to version 4 on Save; prior entity deaths cannot be reconstructed.
- Pass 5 Stage 2: surviving entities and population records — position, health, home and live layer/transfer ownership.
- Pass 5 Stage 3: relevant group/population state and duplicate-free restoration.
- Pass 6: exact underground player restoration, required connections and liquid/basin changes.
- Pass 7: full combined verification and remaining menu/recovery work.
- Persistence remains partial. Automatic saving/options and the actual dropped-item expiry timer are future features.
