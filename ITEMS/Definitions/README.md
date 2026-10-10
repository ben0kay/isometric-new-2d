# Item Definition Library

This directory is the canonical home for **new inventory item definitions**.
`ITEMS/ItemCatalog.tres` remains the master registry. Existing C# built-in
items stay active until Phase 2 migrates them.

## Categories and destination folders

Use `ITEMS/Definitions/<Category>/<Subcategory>/<PascalItemName>/<PascalItemName>.tres`.
Only create directories when there is content to place in them; do not make
placeholder directories just to show the full taxonomy.

| Category | Subcategories |
| --- | --- |
| RawMaterials | Ores, Minerals, Organics, Gases, Liquids |
| RefinedMaterials | Ingots, Alloys, Sheets, Polymers, Chemicals, Textiles, GlassCeramics, Composites |
| Components | Fasteners, Mechanical, Structural, Electrical, Electronics, Power, Communications, Hydraulics, PipesValves |
| Technology | Computing, Sensors, Modules, Robotics, Navigation, AdvancedTech, AlienTech |
| Combat | Ammunition, Explosives, Grenades, Mines, WeaponAttachments |
| Equipment | Tools, Weapons, Armor, Clothing, Backpacks, Gadgets |
| Survival | Sleeping, Camping, Shelter, Containers, Environmental |
| Food | Raw, Cooked, Ingredients, Preserved, Drinks |
| Consumables | Medical, Utility, Fuel |
| Placeables | Workstations, Production, Power, Storage, Structures, Furniture, Lighting, Defenses, Agriculture, Communications |
| Vehicles | Parts, Engines, Propulsion, Chassis, Deployables |

A folder describes **what an item is**, not every action it performs.
Capabilities such as `Placeable`, `Consumable` and `Attack` are defined
by the item resource, not by the folder name. For example, a deployable
sleeping bag can live under `Survival/Sleeping` and still be placeable.
World object scenes stay in `WORLD/` or `WORLDABLES/`, not in this library.

## Register a new item

1. Create a named folder and an external `ItemDefinition` `.tres` within
   the selected subcategory; use a stable, unique lower-snake-case `Id`.
   The ID is a save identity and **must not change when a folder moves**.
2. Set `DisplayName`, `ShortName`, `MaxStack`, `WeightKg`,
   `VolumeLitres`, and optional `Icon`/`Tint` and capability resources.
   Missing icons use the existing catalog fallback.
3. Make or update the subcategory's `ItemCatalog` resource and add the
   item to its `Items` array. Name the catalog
   `<Subcategory>Catalog.tres` at that subcategory root.
4. If the subcategory is new, connect its catalog to its parent category
   catalog through `Categories`. Similarly connect a new top-level
   category catalog to `ITEMS/ItemCatalog.tres`.
5. Keep `IncludeBuiltInItems` enabled **only on the master** during the
   Phase 1/2 transition. New child catalogs leave it false (default).
6. Check for duplicate IDs, missing paths, valid item parameters and
   intended capabilities. Do not introduce another item with the same ID
   or register the same resource twice.

**Files are not auto-registered by being present in a folder.** Runtime
lookup goes through the explicit catalog tree. An item-creation PowerShell
tool should edit the item file and the smallest necessary catalog links,
not scan folders during gameplay or modify gameplay C#.

### Implemented example: Bark

```text
ITEMS/ItemCatalog.tres
└── Definitions/RawMaterials/RawMaterialsCatalog.tres
    └── Organics/OrganicsCatalog.tres
        └── Bark/Bark.tres  (Id: bark)
```

Bark is an ordinary, stackable item with a placeholder icon. It is
registered and can be resolved by `ItemCatalog.Get("bark")` after
initialization. **No tree currently drops Bark**; customizable harvest
profiles and tree assignment are scheduled for Phase 3.

## Testing Phase 1 locally

1. Pull the Phase 1 commit and open the project in Godot. Check that
   `Bark.tres` and both category catalogs open without resource errors.
2. Follow `ItemCatalog.tres` → RawMaterialsCatalog → OrganicsCatalog →
   Bark and confirm the item ID and values.
3. Run the game and check for catalog loading errors; existing items,
   equipment and ordinary gathering should behave as before.
4. For a collection/save test, use a **temporary debug-only item spawn**
   via the existing `ResourceWorld.Spawn("bark", count, position)` API;
   collect it, save, reload and confirm the stack is unchanged.
   This fixture is not connected to gameplay in Phase 1.
5. Confirm duplicate-ID rejection and repeated catalog initialization
   only in a disposable local test; **do not** leave a deliberate duplicate
   registered in the shipped catalogs.

Phase 1 changes only resource files and documentation, not C# systems.
Git history is used for change tracking; no separate backup files.
