# Fractured Horizons

> **Development status:** Early development / active prototyping
>
> **Genre:** Isometric 2D science-fiction survival, exploration, combat, crafting and base building
>
> **Technology:** Godot 4.x (.NET / C#)

## The Premise

**You were never supposed to land here.**

After an unexpected crash landing on a dangerous alien planet, the protagonist finds themselves stranded, underprepared and equipped with only limited emergency supplies. Survival means foraging, scrounging, improvising tools, finding food and water, and constructing a safe place to endure an unfamiliar world.

The character may understand advanced science and engineering, but **knowledge is not manufacturing capability**. Knowing how to build a circuit or refine metal doesn't mean having the equipment to do it. Starting with almost nothing, the player must gradually establish the tools, resources, workstations and infrastructure needed to recover increasingly sophisticated technology.

The long-term reward is going from struggling to produce a first iron bar to manufacturing advanced equipment, powered defences and potentially exotic alien technology.

## Game Vision

Fractured Horizons is intended to become a **large, content-rich survival game** with an explorable alien world, distinct biomes, environmental dangers, varied creatures and robots, meaningful base building and extensive crafting progression.

The design draws inspiration from:

- **ARK: Survival Evolved:** The breadth of survival content and the sense of advancement from primitive equipment to metal, advanced technology and a high-tech endgame.
- **Modded Minecraft:** Satisfying material-processing chains, specialised tools and workstations, and the feeling of unlocking whole new capabilities through better equipment.
- **Semi-realistic survival and engineering:** Materials should have believable origins and transformations, while actions remain approachable and fun.

**This is not intended to be Factorio or Satisfactory.** Large industrial logistics networks, conveyor-belt megafactories and factory optimisation are not the main game loop. Small-scale automation, powered machinery and defensive turrets may be introduced when they support survival or base defence.

### Design Pillars

1. **Survival first.** Early choices revolve around shelter, food, water, danger and limited resources. Each useful discovery or constructed tool should feel earned.
2. **Believable crafting, not tedious simulation.** Ore needs processing before it becomes usable metal; metal needs shaping and assembly before it becomes armour. Abstract the repetitive labour without skipping the meaningful stages.
3. **Workstations unlock capabilities.** Heat, smelting, forging, cutting, assembly, electronics and advanced fabrication require appropriate tools or stations. Improved stations can increase efficiency or replace earlier processing steps.
4. **Deep content with reusable materials.** A broad future catalog of resources, recipes, weapons, armour, buildings and equipment should share useful intermediate materials—not demand arbitrary one-use ingredients.
5. **Exploration feeds progression.** Dangerous biomes, resources, hostile entities and salvage provide reasons to venture beyond safety. Rare advanced components may be found long before the player can manufacture them.
6. **Arcade fun alongside semi-realism.** Progression should feel engaging, with understandable recipes and manageable processing times rather than mandatory micromanagement.

## Survival and Technology Progression

These are **broad, overlapping directions**, not rigid levels or a promise that every stage is implemented.

| Stage | Intended player experience |
| --- | --- |
| **Stranded / Primitive** | Emergency supplies, foraging, basic tools, fire, food and water, crude shelter, simple storage and defences. |
| **Early Metalworking** | Charcoal production, clay/stone furnaces, iron smelting, forge/anvil, metal bars, plates, basic metal tools and armour. |
| **Developed Mechanical** | Improved furnaces and tools, steel/alloys, reinforced structures, better weapons, machinery and workstations. |
| **Electrical / Advanced** | Restored power, generators, electronics, advanced manufacturing, specialised equipment and potentially turrets. |
| **High-tech / Exotic** | Sophisticated armour and shields, advanced weapons, alien materials, experimental machines and late-game technologies. |

Rather than relying solely on arbitrary character-level recipe unlocks, advancement should primarily emerge from **access to resources, tools, workstation capabilities, power and research into unfamiliar alien technology**. An early salvaged electronic component might be useful for repair but impossible to reproduce until much later.

### Example: Iron Ore to Armour

The first iron production process illustrates the intended balance between semi-realism and satisfying crafting. **This is a future design example, not a currently implemented recipe chain.**

1. **Gather materials:** Mine **Iron Ore** and collect **Clay**, **Rock** and suitable woody alien material.
2. **Produce charcoal:** Use a primitive **charcoal kiln** to prepare carbon-rich fuel and a reducing agent.
3. **Smelt the ore:** Feed iron ore and charcoal into a **clay/stone bloomery**, with sufficient heat and airflow, producing a crude **iron bloom** containing slag.
4. **Work the bloom:** Use a **forge, anvil and hammer** to consolidate the iron and form usable **iron bars**.
5. **Shape and assemble:** Make **metal plates and fittings**, then combine them with **padding and bindings** at an assembly workbench to create armour, weapons or structural parts.
6. **Upgrade production:** Develop better furnaces, metalworking tools, presses and eventually powered equipment, making earlier processes faster, more efficient or unnecessary.

These stages should represent believable processes without becoming a metallurgy simulator. For example, heating iron oxide ore is not sufficient by itself to make metallic iron, and a primitive bloomery produces a bloom rather than requiring molten cast iron. Existing generic `carbon` is **not automatically equivalent to charcoal** until its in-world properties are defined.

Useful intermediate materials should remain relevant: iron bars, plates and fittings can support many recipes throughout the game. Better workstations should feel like meaningful achievements, not simply cosmetic replacements.

## Current Project Foundations

Development currently prioritises a **modular, scalable and save-conscious foundation** before expanding the complete gameplay and content catalog. Existing and evolving systems include:

### World Generation and Environment

- Procedural world generation with chunk-based loading and unloading.
- Distinct biome definitions, terrain, ground resources and vegetation placement.
- Terrain elevation, environmental lighting and shadows.
- Biome-specific plants, trees and multiple sprite variations.

### Entities, Interaction and Survival

- Entity population, movement, combat and death-related loot foundations.
- Inventory, tools, harvesting and player interaction systems.
- Foundational food, water, fatigue and stamina mechanics.
- Contextual cursor-targeted interaction for containers and robot wreck salvage.

### Items, Crafting and Base Building

- Data-driven item definitions, catalogs, harvesting rewards and crafting recipes.
- Player inventory and existing crafting queue foundations.
- Placeable world-object and storage systems.
- **Future direction:** Primitive processing stations, tool/station requirements, metalworking, equipment manufacturing and richer building content.

### Persistence and Development Tools

- Saving and restoring player and world state, including important streamed-world changes.
- Persistence for dropped items, stored contents, wrecks and placed objects, with continued reliability work.
- Debugging and performance inspection tools.
- Custom workflows for biome content and preparing sprite artwork.

**Prototype equipment is not the intended final starting loadout.** The blaster, mining laser and shovel currently support testing. Do not remove or rebalance them solely to match the crash-landing premise; make any starting-equipment change through an explicit gameplay pass.

## Technology

- **Engine:** Godot 4.x (.NET / C#)
- **Perspective:** Isometric-style 2D
- **Primary language:** C#
- **Development platform:** Windows
- **Version control:** Git and GitHub
- **Development approach:** Incremental modular features, external resource definitions where appropriate, performance-minded implementation and save compatibility.

## Development Roadmap

The project is evolving. Current and future priorities include:

- Continuing to stabilise and verify world-state persistence.
- Expanding biome-specific environmental art and gameplay content.
- Improving entity behaviours, interactions and combat.
- Deepening survival, exploration and environmental hazards.
- Designing and implementing a first complete **primitive resource-processing and metalworking** progression slice.
- Adding meaningful tools, crafting stations, equipment and construction content in tested stages.
- Improving user interface, audio, visual feedback and original sprites.
- Continuing performance profiling and optimising streamed-world systems.

These are directions, **not claims that every feature is playable today**.

## Guidance for Future Development and Codex

This README expresses the **game's intended identity**, not permission to implement every idea at once. Before making changes, inspect the current code and feature-specific notes, follow [coding preferences](NOTES/CodePreferences.md), and implement one bounded, testable pass at a time.

Prefer reusable systems and resource-driven content with stable item/recipe IDs. Preserve existing saves, world identities and proven gameplay. Consult the source of truth for what is already implemented; a design example here should never be mistaken for shipped functionality.

The standalone [GAME_DIRECTION.md](GAME_DIRECTION.md) is retained as an earlier focused design reference; **this README is now the primary high-level overview**.

## Screenshots

*Gameplay screenshots and demonstrations will be added as the game's visuals develop.*

## Project Status

Fractured Horizons is an experimental, independently developed project in active prototyping. Mechanics, scope, architecture and presentation may change as the game develops.

---

*Independently developed with AI-assisted programming and development workflows.*
