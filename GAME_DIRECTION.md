# Fractured Horizons — Game Direction

> **Status:** High-level creative direction, not a list of implemented features or an authorization to add systems.
> **Purpose:** Keep game design, future content and Codex implementations aligned. For actual code behavior, inspect current source and feature-specific notes. For coding preferences see [NOTES/CodePreferences.md](NOTES/CodePreferences.md).

## Core premise

The protagonist **crash-lands unexpectedly on a dangerous alien planet**. Landing here was never the mission; they are stranded, underprepared and have only **limited emergency supplies**. Their immediate task is to **forage, scrounge, survive and build from almost nothing**.

The character may understand advanced science and engineering, but **knowledge is not manufacturing capability**. Knowing how to build a circuit does not mean being able to produce one without materials, tools, power and equipment. The long-term satisfaction comes from slowly rebuilding that capability.

## What kind of game is this?

- **Genre:** Long-form, content-rich, 2D isometric science-fiction **survival, exploration, combat, base building and crafting** game.
- **Atmosphere:** An unfamiliar, beautiful but hostile planet. Early survival feels precarious; every new tool, shelter, workstation and resource matters.
- **Progression feel:** Inspired by **ARK: Survival Evolved** — broad survival content and distinct primitive, metal, advanced and exotic-tech stages — with some of the meaningful resource-processing depth of **modded Minecraft**.
- **Not a factory simulator:** **Factorio/Satisfactory-style logistics and conveyor-belt megafactories are not the target**. Small optional automation, powered systems and defensive turrets may be added later.
- **Realism target:** **Believable principles, simplified execution**. Respect what materials and equipment are needed, without demanding tedious real-world industrial simulation.

## Design pillars

1. **Earned progression:** Primitive beginnings lead to increasingly capable tools, armour, weapons, structures and technology. Upgrades should change what the player can accomplish, not only increase stats.
2. **Materials have believable origins and uses:** Gather, harvest, mine, salvage, process and assemble. Avoid unexplained recipes that turn raw ore directly into finished armour.
3. **Crafting stations provide capabilities:** Heat, smelting, forging, cutting, assembly, electronics and advanced fabrication should require appropriate equipment. Better stations may combine steps or improve yields.
4. **Rich content, reusable foundations:** Aim for a large eventual catalog of meaningful resources, recipes, enemies, biomes, equipment and structures. Shared intermediate materials should support many products, not exist only to pad one recipe.
5. **Exploration and danger create demand:** Food, water, fatigue, environmental threats, hostile creatures/robots and distant resources motivate stronger equipment and safer bases. Salvage can provide rare advanced parts before the player can manufacture them.
6. **Fun over chores:** Use clear recipes, approachable workstations and timed/batched processing. Reserve complex optional mechanics for depth and upgrades, not mandatory micromanagement.

## Broad progression direction (overlapping, not rigid unlock tiers)

| Stage | Examples of player capability |
| --- | --- |
| **Stranded / Primitive** | Emergency supplies, foraging, basic tools, fire, water/food, shelter, crude containers, simple defences. |
| **Early metallurgy** | Charcoal production, clay/stone furnaces, iron smelting, forge/anvil, metal bars, plates, basic metal tools and armour. |
| **Developed mechanical** | Improved metalworking, steel/alloys, reinforced construction, better weapons, machinery and workstations. |
| **Electrical / Advanced** | Restored power, electronics, refined manufacturing, generators, specialised equipment, optional turrets. |
| **High-tech / Exotic** | Sophisticated suits/shields, advanced weapons and machinery, alien materials/technology and major late-game capabilities. |

Progress should generally be limited by **access to resources, tools, stations, power and unfamiliar alien research**, not arbitrary level gates alone. Advanced salvage found early may be usable or repairable long before it can be manufactured.

## Reference chain: iron ore to protective armour

**Illustrative future gameplay, not an implemented recipe list:**

1. **Gather:** Mine **Iron Ore**; collect **Clay**, **Rock** and suitable woody alien material. Make basic tools and furnace structures.
2. **Make charcoal:** A simple **charcoal kiln** processes woody material with limited oxygen into a carbon-rich smelting fuel/reductant.
3. **Smelt:** A **clay/stone bloomery** uses Iron Ore, charcoal, heat and airflow (e.g. bellows) to reduce iron-bearing minerals, yielding a **crude iron bloom** containing slag.
4. **Refine/shape:** A **forge, anvil and hammer** consolidate the bloom and work it into a **usable iron bar**. The game can abstract the repetitive forging actions.
5. **Manufacture:** Forge **iron plates/fittings**; combine plates with **padding and bindings** at an appropriate **assembly workbench** to make armour. The same components support buildings, weapons and tools.
6. **Improve later:** Better furnaces, fuels, presses and powered metalworking **speed up, improve yield or replace earlier stages**, providing a tangible sense of accomplishment.

Realism guardrails: Heating iron oxide ore alone is not enough; iron production needs a suitable reduction process. A bloomery need not melt the iron. **Existing generic `carbon` should not automatically be treated as charcoal** until its in-world properties are defined. Do not require detailed chemical simulations or repetitive manual hammer minigames.

## Crafting/system implications

- Prefer **data-driven item and recipe definitions** with stable IDs and reusable **station/tool capabilities**, rather than hardcoding recipes to particular building names.
- Distinguish **raw resources, processed materials, components and finished equipment**. Make intermediate items inventory-visible when they have meaningful reuse.
- Stations can have fuel, power and processing queues as needed; **large automation networks are not a prerequisite**.
- Preserve persistence and save compatibility when adding stations, processes or pickup/dismantle actions. Use small, playable vertical slices before large content expansions.
- Existing in-development equipment (including the **blaster, mining laser and shovel**) currently serves as prototype/test content. **Do not remove or re-balance it solely because of this vision document**; a future explicit starting-equipment pass can establish the true crash-landing loadout.
- The existing universal interaction system can eventually expose actions such as **Craft, Refuel, Repair, Pick Up or Dismantle** through a contextual menu, but these are not automatically implemented or approved by this document.

## Scope discipline for Codex and future design

This document describes **intent**, not a request to create all stages, items, buildings or mechanics now. Before implementing: inspect the current repository, align with feature-specific Markdown plans, preserve stable content IDs/save formats, and propose **one bounded, testable pass**. Open decisions include the exact starter inventory, technology/research rules, material recipes, station lineup and how much small-scale automation to support.
