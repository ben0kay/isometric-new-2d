# Fractured Horizons — Gameplay Vision & Direction

> **Status:** Design direction / proposal, not an implementation checklist.
>
> **Core idea:** A grounded, expandable, isometric 2D science-fiction survival action RPG combining meaningful exploration, crafting, research, equipment progression, and combat.
>
> **Relationship to README:** The root README remains the current project overview. This document develops the longer-term player experience and fun factor. Existing feature notes and code are the source of truth for implementation status.

## The player fantasy

A technically trained scientist or expedition engineer has crash-landed on an unfamiliar alien planet. They understand science and technology, but have only limited emergency equipment, salvaged components, and no industrial infrastructure. Knowledge alone cannot replace tools, resources, power, or manufacturing capability.

The player should feel increasingly capable because they learn about the world, establish infrastructure, recover technology, and improve their equipment—not because they acquire fantasy magic or arbitrary stat points.

## Core design pillars

1. **Explore for a reason.** Each biome contains meaningful resources, threats, mysteries, locations, and discoveries, not merely different scenery.
2. **Survive through decisions, not chores.** Preparation, environmental hazards, limited supplies, and risk-reward choices matter. Avoid piling on survival meters without fun consequences.
3. **Reward combat without becoming a fantasy RPG.** Responsive encounters, distinctive enemies, rare salvage, meaningful weapon differences, and equipment builds provide the excitement of an action RPG.
4. **Progress through technology.** Research, recovered knowledge, station capabilities, equipment modules, and materials open new options. Workstations unlock what the player can actually manufacture.
5. **Make the world reactive.** Eclipses, migrations, storms, and other events can alter behaviour, accessibility, danger, and opportunities.
6. **Build for expansion.** Biomes, species, equipment, recipes, encounters, research, and locations should use reusable systems and data definitions wherever practical.
7. **Finish a fun vertical slice before scaling content.** A handful of interconnected systems is worth more than dozens of decorative biomes.

## Primary gameplay loop

**Explore → Encounter danger → Collect samples, resources and salvage → Return or establish shelter → Research/process/craft → Improve equipment and infrastructure → Venture farther.**

Each expedition should offer a meaningful objective and an opportunity for an unexpected discovery. Retreat, preparation, and returning better equipped should feel rewarding.

### Example session

The player needs a mineral for a suit upgrade. In the highlands they discover an alien structure guarded by creatures. Their weapon overheats and supplies run low, forcing a retreat. At the outpost they research a recovered sample, learn of the creatures' electrical vulnerability, build a cooling or electrical module, and return with a better plan.

## Biomes as gameplay

- **Swampy Marsh:** Movement-limiting water, concealed threats, biological resources, environmental protection requirements.
- **Rocky Mountains:** Challenging passes and elevation, high-value minerals, dangerous chokepoints.
- **Rolling Hill Forest:** Reduced sightlines, stalking predators, hidden sites and biological discoveries.
- **Other biomes:** Each new region should have a distinctive *mechanical* identity as well as art and ecology.

Avoid universal biome-level stat penalties without meaningful counterplay. Use environmental systems to create choices and tools to overcome obstacles.

## Combat and equipment progression

Borrow Grim Dawn's **build variety, enemy diversity, and satisfying rewards**, not its fantasy setting or need for dozens of particle-heavy abilities.

Possible modular loadout:
- Power core: capacity, regeneration, and heat trade-offs.
- Suit protection: armour, shields, atmospheric filtering.
- Arm or tool module: extraction, mobility, or close-range utility.
- Utility module: biological scanning, drones, detection, or field repair.
- Weapons: different damage delivery, range, heat, ammunition/energy, and enemy matchups.

These are **concepts**, not promises of implemented equipment slots. Prefer a small number of mechanically distinct modules over many percentage-only upgrades.

## Crafting, research and outposts

Preserve the existing crafting and workstation direction. Hand crafting supports field improvisation; station capabilities unlock manufacturing; processing stations handle materials and eventually persistent jobs. Research can come from scanning life, examining ruins, salvaging technology, and analyzing samples.

Keep automation modest and purposeful. The game is **not intended to become a conveyor-belt factory simulator**. Outposts should support expedition preparation, processing, storage, repair, power, and limited defence.

## Narrative objective and endgame

Provide a completable central goal, potentially restoring communications, constructing an escape system, or understanding why the planet behaves unnaturally. Eclipses could tie into a mystery involving alien machinery or ecology, but the precise explanation remains undecided.

Suggested overlapping phases:
1. Survive the crash and craft an improvised field workbench.
2. Establish a reliable outpost, processing, equipment, and defences.
3. Investigate diverse biomes, alien organisms, ruins, and dangerous facilities.
4. Develop advanced modules, mobility, scientific tools, and research capability.
5. Discover the central mystery and complete a major objective.
6. Continue optionally with dangerous expeditions, rare encounters, new regions, and further technology.

Do **not** require endless procedural content to ship a satisfying main objective.

## First playable vertical slice

Target a compelling 45–60-minute experience before major content expansion:

1. Explore from the crash site and gather materials.
2. Craft and place the Improvised Field Workbench using the existing crafting/placement systems.
3. Encounter a hostile creature and acquire alien resin through a reliable route.
4. Process or research a discovery with a clear purpose.
5. Make one meaningful weapon or suit improvement.
6. Enter a dangerous point of interest and face a tougher encounter.
7. Return with a discovery that opens the next objective.

**Success criterion:** Players want to undertake another expedition because they anticipate a meaningful new capability or discovery—not simply because more terrain exists.

## Technical and architecture guidance

**Keep the current Godot 4 .NET / C# project unless a specific technical blocker is demonstrated.** Reuse the existing chunk streaming, biomes, environmental effects, entities, item definitions, crafting, placement, interaction, persistence, and visual work.

Before moving folders or replacing systems:
- Audit actual dependency boundaries, Godot resource/scene references, and save compatibility.
- Identify concrete pain points: duplicated logic, circular dependencies, hardcoded content, expensive per-frame work, and unclear ownership.
- Prefer small, tested, reversible refactors over a wholesale rewrite.
- Keep stable IDs for items, recipes, entities, and save data.
- Make authored content data-driven when it reduces repeated code; do not build generic frameworks prematurely.
- Preserve working systems, game visuals, and test equipment until an explicit gameplay pass changes them.
- Profile performance before optimizing speculative bottlenecks.
- Follow `NOTES/CodePreferences.md` and existing feature-specific plans.

### Decisions still open

- Is the primary emphasis survival/exploration or combat/loot, or a balanced hybrid?
- How demanding should hunger, thirst, environmental exposure, and equipment upkeep be?
- How much base building and automation should exist?
- What is the main narrative mystery and final objective?
- What is the minimum equipment system that makes two playstyles feel different?

## Next steps

1. Review the repository architecture and existing feature notes; produce a risk-ranked refactor proposal **without moving code yet**.
2. Define the smallest complete gameplay loop using existing systems.
3. Implement and playtest that loop, one bounded pass at a time.
4. Expand biomes, creatures, research, and equipment only after the loop is enjoyable.

**Guiding rule:** Make exploration consequential, combat satisfying, crafting purposeful, and every new system capable of enriching the others.
