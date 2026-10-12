// Explicit tooling only: reads resource definitions without creating world actors.
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public static class ContentDiscovery
{
    public sealed class Report
    {
        public int SchemaVersion { get; set; } = 1;
        public string Scope { get; set; } = "Resource content; not a live campaign or guaranteed spawn report";
        public string[] ScanRoots { get; set; }
        public bool Valid => Diagnostics.All(d => d.Severity != "error");
        public List<Diagnostic> Diagnostics { get; set; } = new();
        public List<ItemRow> Items { get; set; } = new();
        public List<SourceRow> Sources { get; set; } = new();
        public List<RecipeRow> Recipes { get; set; } = new();
        public List<ProfileRow> Profiles { get; set; } = new();
        public List<OreSpawnRow> OreSpawns { get; set; } = new();
    }
    public sealed class Diagnostic
    {
        public string Severity { get; set; }
        public string Path { get; set; }
        public string Field { get; set; }
        public string Message { get; set; }
    }
    public sealed class ItemRow
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string DisplayName { get; set; }
        public string Type { get; set; }
        public int MaxStack { get; set; }
        public float WeightKg { get; set; }
        public float VolumeLitres { get; set; }
        public List<string> Capabilities { get; set; } = new();
        public bool Registered { get; set; }
        public List<string> RegistrationPaths { get; set; } = new();
        public List<string> SourcePaths { get; set; } = new();
        public List<string> ConsumedBy { get; set; } = new();
        public List<string> ProducedBy { get; set; } = new();
    }
    public sealed class RewardRow
    {
        public string ItemId { get; set; }
        public string EditPath { get; set; }
        public string Field { get; set; }
        public string Context { get; set; }
        public int MinimumCount { get; set; }
        public int MaximumCount { get; set; }
        public double Chance { get; set; }
    }
    public sealed class BindingRow
    {
        public string BiomeId { get; set; }
        public string BiomePath { get; set; }
        public List<string> CatalogPaths { get; set; } = new();
        public string EditPath { get; set; }
        public string Family { get; set; }
        public double Weight { get; set; }
        public bool BiomeEnabled { get; set; }
        public bool FamilyEnabled { get; set; }
        public bool ConfiguredForSelection { get; set; }
    }
    public sealed class SourceRow
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Kind { get; set; }
        public string Extraction { get; set; }
        public double Work { get; set; }
        public int RequiredStrength { get; set; }
        public int TotalUnits { get; set; }
        public int UnitsPerBatch { get; set; }
        public List<string> RegistrationPaths { get; set; } = new();
        public List<RewardRow> Rewards { get; set; } = new();
        public List<BindingRow> Biomes { get; set; } = new();
    }
    public sealed class IngredientRow
    {
        public string ItemId { get; set; }
        public int Count { get; set; }
        public string EditPath { get; set; }
    }
    public sealed class RecipeRow
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string[] RequiredCapabilities { get; set; } = Array.Empty<string>();
        public string OutputItemId { get; set; }
        public string OutputPath { get; set; }
        public int OutputCount { get; set; }
        public double DurationSeconds { get; set; }
        public bool Registered { get; set; }
        public List<string> RegistrationPaths { get; set; } = new();
        public List<IngredientRow> Ingredients { get; set; } = new();
    }
    public sealed class ProfileRow
    {
        public string Path { get; set; }
        public List<string> SourcePaths { get; set; } = new();
        public bool Shared => SourcePaths.Distinct(StringComparer.Ordinal).Count() > 1;
        public List<RewardRow> Rewards { get; set; } = new();
    }
    public sealed class OreSpawnRow
    {
        public string Path { get; set; }
        public string DefinitionPath { get; set; }
        public bool Enabled { get; set; }
        public double ChancePerChunk { get; set; }
        public int PlacementAttempts { get; set; }
        public string Scope { get; set; } = "Surface settings definition; actual campaign assignment and terrain acceptance are not inferred";
    }

    public static readonly string[] DefaultRoots =
        { "res://ITEMS", "res://PLAYER", "res://WORLD", "res://CONFIG",
          "res://SYSTEMS/Crafting", "res://SYSTEMS/Harvesting" };

    public static Report Build(string[] roots = null) => new Builder(roots ?? DefaultRoots).Run();

    private sealed class Builder
    {
        private readonly Report _report;
        private readonly Dictionary<string, Resource> _resources = new(StringComparer.Ordinal);
        private readonly HashSet<string> _dependencies = new(StringComparer.Ordinal);
        private readonly Dictionary<ItemDefinition, ItemRow> _items = new();
        private readonly Dictionary<WorldObjectDefinition, SourceRow> _sources = new();
        private readonly Dictionary<HarvestProfile, ProfileRow> _profiles = new();
        private readonly Dictionary<CraftingRecipe, RecipeRow> _recipes = new();
        private readonly Dictionary<BiomeDefinition, List<string>> _biomes = new();
        private ItemCatalog _catalog;
        private bool _catalogValid;

        public Builder(string[] roots) { _report = new Report { ScanRoots = roots.ToArray() }; }
        private void Error(string path, string field, string message) => _report.Diagnostics.Add(
            new Diagnostic { Severity = "error", Path = path, Field = field, Message = message });
        private void Warn(string path, string field, string message) => _report.Diagnostics.Add(
            new Diagnostic { Severity = "warning", Path = path, Field = field, Message = message });
        private bool Check(string path, string field, Action action)
        {
            try { action(); return true; }
            catch (Exception error) { Error(path, field, error.Message); return false; }
        }
        private static string PathOf(Resource resource, string fallback) =>
            string.IsNullOrEmpty(resource?.ResourcePath) ? fallback : resource.ResourcePath;
        private static bool External(Resource resource) => resource != null &&
            !string.IsNullOrEmpty(resource.ResourcePath) && !resource.ResourcePath.Contains("::");
        private static double Finite(double value) => double.IsFinite(value) ? value : 0;
        private static float Finite(float value) => float.IsFinite(value) ? value : 0;
        private static void Add(List<string> list, string value)
        { if (!list.Contains(value, StringComparer.Ordinal)) list.Add(value); }

        public Report Run()
        {
            foreach (string root in _report.ScanRoots.OrderBy(x => x, StringComparer.Ordinal)) Scan(root);
            _catalog = Load("res://ITEMS/ItemCatalog.tres") as ItemCatalog;
            _catalogValid = _catalog != null && Check("res://ITEMS/ItemCatalog.tres", "Initialize", _catalog.Initialize);
            if (_catalog == null) Error("res://ITEMS/ItemCatalog.tres", "script", "Expected ItemCatalog.");
            var crafting = Load("res://SYSTEMS/Crafting/CraftingCatalog.tres") as CraftingCatalog;
            var ground = Load("res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres") as GroundResourceCatalog;
            if (crafting == null) Error("res://SYSTEMS/Crafting/CraftingCatalog.tres", "script", "Expected CraftingCatalog.");
            if (ground == null) Error("res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres", "script", "Expected GroundResourceCatalog.");
            if (_catalogValid && crafting != null) Check(crafting.ResourcePath, "Validate", () => crafting.Validate(_catalog));
            if (_catalogValid && ground != null) Check(ground.ResourcePath, "Materials", () => ground.Validate(_catalog));
            if (_catalog != null) RegisterItems(_catalog, new HashSet<ItemCatalog>());
            foreach (var pair in _resources.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray())
            {
                switch (pair.Value)
                {
                    case ItemDefinition item: Item(item, pair.Key); break;
                    case CraftingRecipe recipe: Recipe(recipe, pair.Key); break;
                    case HarvestProfile profile: Profile(profile, pair.Key); break;
                    case PlantDefinition plant: Source(plant, pair.Key); break;
                    case TreeDefinition tree: Source(tree, pair.Key); break;
                    case RockDefinition rock: Source(rock, pair.Key); break;
                    case OreDefinition ore: Source(ore, pair.Key); break;
                    case BiomeDefinition biome: if (!_biomes.ContainsKey(biome)) _biomes.Add(biome, new()); break;
                }
            }
            foreach (var pair in _resources.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray())
            {
                if (pair.Value is BiomeCatalog biomes) BiomeCatalogRows(biomes, pair.Key);
                if (pair.Value is GroundResourceCatalog grounds) GroundRows(grounds, pair.Key, grounds == ground);
                if (pair.Value is CraftingCatalog recipes) RecipeCatalogRows(recipes, pair.Key, recipes == crafting);
                if (pair.Value is OreSpawnSettings settings) OreSettings(settings, pair.Key);
            }
            foreach (var pair in _biomes.OrderBy(x => PathOf(x.Key, x.Key.Id), StringComparer.Ordinal).ToArray())
                BiomeRows(pair.Key, pair.Value);
            foreach (var pair in _items)
            {
                if (!pair.Value.Registered) Warn(pair.Value.Path, "registration", "Defined item is not registered in the master item catalog.");
                foreach (var source in _report.Sources)
                    if (source.Rewards.Any(r => r.ItemId == pair.Value.Id)) Add(pair.Value.SourcePaths, source.Path);
                foreach (var recipe in _report.Recipes)
                {
                    if (recipe.Ingredients.Any(i => i.ItemId == pair.Value.Id)) Add(pair.Value.ConsumedBy, recipe.Path);
                    if (recipe.OutputItemId == pair.Value.Id) Add(pair.Value.ProducedBy, recipe.Path);
                }
            }
            foreach (var recipe in _report.Recipes.Where(x => !x.Registered))
                Warn(recipe.Path, "registration", "Defined recipe is not registered in the master crafting catalog.");
            DuplicateIds(_report.Items.Select(x => (x.Id, x.Path)), "item ID");
            DuplicateIds(_report.Recipes.Select(x => (x.Id, x.Path)), "recipe ID");
            DuplicateIds(_report.Sources.Select(x => (x.Kind + "/" + x.Id, x.Path)), "source kind/ID");
            _report.Items = _report.Items.OrderBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToList();
            _report.Sources = _report.Sources.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
            _report.Recipes = _report.Recipes.OrderBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToList();
            _report.Profiles = _report.Profiles.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
            _report.OreSpawns = _report.OreSpawns.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
            _report.Diagnostics = _report.Diagnostics.OrderBy(x => x.Path, StringComparer.Ordinal).ThenBy(x => x.Field, StringComparer.Ordinal).ThenBy(x => x.Message, StringComparer.Ordinal).ToList();
            return _report;
        }

        private void Scan(string path)
        {
            using var directory = DirAccess.Open(path);
            if (directory == null) { Error(path, "scan", "Cannot open configured scan root."); return; }
            directory.ListDirBegin();
            string name;
            while ((name = directory.GetNext()) != "")
            {
                if (name.StartsWith(".", StringComparison.Ordinal)) continue;
                string child = path.TrimEnd('/') + "/" + name;
                if (directory.CurrentIsDir()) Scan(child);
                else if (name.EndsWith(".tres", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".res", StringComparison.OrdinalIgnoreCase)) Load(child);
            }
            directory.ListDirEnd();
        }
        private Resource Load(string path)
        {
            if (_resources.TryGetValue(path, out var existing)) return existing;
            Check(path, "dependencies", () => Dependencies(path));
            Resource resource = null;
            Check(path, "load", () =>
            {
                resource = ResourceLoader.Load(path);
                if (resource == null) throw new InvalidOperationException("Resource failed to load; check Godot's errors for the dependency.");
            });
            if (resource != null) _resources[path] = resource;
            return resource;
        }
        private void Dependencies(string path)
        {
            if (!_dependencies.Add(path)) return;
            if (!ResourceLoader.Exists(path)) throw new InvalidOperationException("Missing resource: " + path);
            foreach (string entry in ResourceLoader.GetDependencies(path))
            {
                string dependency = entry.Split("::").Last();
                if (dependency.StartsWith("res://", StringComparison.Ordinal))
                {
                    if (!ResourceLoader.Exists(dependency)) Error(path, "dependency", "Missing resource: " + dependency);
                    else Dependencies(dependency);
                }
            }
        }
        private void DuplicateIds(IEnumerable<(string Id, string Path)> entries, string label)
        {
            foreach (var group in entries.GroupBy(x => x.Id, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(group.Key))
                    foreach (var row in group) Error(row.Path, label, "Missing identity.");
                else if (group.Select(x => x.Path).Distinct(StringComparer.Ordinal).Count() > 1)
                    foreach (var row in group) Error(row.Path, label, "Conflicting defined identity: " + group.Key);
            }
        }
        private void RegisterItems(ItemCatalog catalog, HashSet<ItemCatalog> visiting)
        {
            if (catalog == null || !visiting.Add(catalog)) return; // Initialize reports errors.
            string path = PathOf(catalog, "res://ITEMS/ItemCatalog.tres");
            if (catalog.Items != null)
                foreach (var item in catalog.Items.Where(x => x != null))
                {
                    var row = Item(item, path + "#Items");
                    row.Registered = _catalogValid && _catalog.Get(item.Id) == item;
                    Add(row.RegistrationPaths, path);
                }
            if (catalog.Categories != null)
                foreach (var child in catalog.Categories) RegisterItems(child, visiting);
            visiting.Remove(catalog);
        }
        private ItemRow Item(ItemDefinition item, string fallback)
        {
            if (_items.TryGetValue(item, out var row)) return row;
            row = new ItemRow { Id = item.Id, Path = PathOf(item, fallback), DisplayName = item.DisplayName,
                Type = item.GetType().Name, MaxStack = item.MaxStack, WeightKg = Finite(item.WeightKg), VolumeLitres = Finite(item.VolumeLitres) };
            if (!External(item)) Error(row.Path, "resource", "Inventory items need an external resource file for tooling.");
            if (string.IsNullOrWhiteSpace(item.Id) || item.Id != item.Id.Trim() || item.MaxStack < 1 ||
                !float.IsFinite(item.WeightKg) || item.WeightKg < 0 || !float.IsFinite(item.VolumeLitres) || item.VolumeLitres < 0)
                Error(row.Path, "identity/storage", "Invalid identity, stack, weight or volume.");
            if (item.Attack != null) row.Capabilities.Add("attack");
            if (item.Consumable != null) { row.Capabilities.Add("consumable"); Check(row.Path, "Consumable", item.Consumable.Validate); }
            if (item.Placeable != null) { row.Capabilities.Add("placeable"); Check(row.Path, "Placeable", item.Placeable.Validate); }
            if (row.Capabilities.Count > 1) Error(row.Path, "capabilities", "Conflicting primary-use capabilities.");
            if (item is BackpackDefinition) row.Capabilities.Add("backpack");
            _items[item] = row; _report.Items.Add(row); return row;
        }
        private ProfileRow Profile(HarvestProfile profile, string fallback)
        {
            if (_profiles.TryGetValue(profile, out var row)) return row;
            row = new ProfileRow { Path = PathOf(profile, fallback) };
            _profiles[profile] = row; _report.Profiles.Add(row);
            if (_catalogValid) Check(row.Path, "Drops", () => profile.Compile(_catalog));
            if (profile.Drops != null)
                for (int i = 0; i < profile.Drops.Count; i++)
                {
                    var drop = profile.Drops[i]; if (drop == null) continue;
                    row.Rewards.Add(new RewardRow { ItemId = drop.ItemId,
                        EditPath = PathOf(drop, row.Path + "#Drops[" + i + "]"), Field = "Drops[" + i + "]",
                        Context = "independent profile reward", MinimumCount = drop.MinimumCount,
                        MaximumCount = drop.MaximumCount, Chance = Finite(drop.Chance) });
                }
            return row;
        }
        private void ProfileRewards(SourceRow source, HarvestProfile profile, string field, string context)
        {
            if (profile == null) return;
            var row = Profile(profile, source.Path + "#" + field); Add(row.SourcePaths, source.Path);
            if (!External(profile)) Warn(row.Path, field, "Embedded profile; edit its containing file and subresource.");
            foreach (var reward in row.Rewards)
                source.Rewards.Add(new RewardRow { ItemId = reward.ItemId, EditPath = reward.EditPath,
                    Field = reward.Field, Context = context, MinimumCount = reward.MinimumCount,
                    MaximumCount = reward.MaximumCount, Chance = reward.Chance });
        }
        private SourceRow Source(WorldObjectDefinition definition, string fallback)
        {
            if (_sources.TryGetValue(definition, out var row)) return row;
            string kind = definition is OreDefinition ? "ore" : definition is PlantDefinition ? "plant" : definition is TreeDefinition ? "tree" : "rock";
            row = new SourceRow { Id = definition.Id, Path = PathOf(definition, fallback), Kind = kind };
            _sources[definition] = row; _report.Sources.Add(row);
            if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id != definition.Id.Trim()) Error(row.Path, "Id", "Missing or padded source identity.");
            if (!External(definition)) Error(row.Path, "resource", "Harvest species need an external definition for persistent identity.");
            if (!float.IsFinite(definition.SizeRange.X) || !float.IsFinite(definition.SizeRange.Y) ||
                definition.SizeRange.X <= 0 || definition.SizeRange.Y < definition.SizeRange.X ||
                !float.IsFinite(definition.MirrorChance) || definition.MirrorChance < 0 || definition.MirrorChance > 1)
                Error(row.Path, "variation", "Invalid size interval or mirror probability.");
            if (definition is OreDefinition ore)
            {
                row.Extraction = "successful ore batch; last primary batch may be smaller; bonuses once per batch";
                row.Work = Finite(ore.WorkPerBatch); row.RequiredStrength = ore.RequiredMiningStrength;
                row.TotalUnits = ore.TotalUnits; row.UnitsPerBatch = ore.UnitsPerBatch;
                if (_catalogValid) Check(row.Path, "extraction", () => new OreBatchPlan(ore, _catalog));
                string id = ore.YieldItem?.Id ?? ore.YieldItemId;
                row.Rewards.Add(new RewardRow { ItemId = id, EditPath = row.Path, Field = "YieldItem/YieldItemId; UnitsPerBatch",
                    Context = "primary per batch; min(remaining, UnitsPerBatch)", MinimumCount = 1, MaximumCount = ore.UnitsPerBatch, Chance = 1 });
                ProfileRewards(row, ore.BonusDrops, "BonusDrops", "independent bonus once per successful ore batch");
            }
            else
            {
                row.Extraction = kind == "plant" ? "one-shot hand gathering; HarvestWork is validated but does not delay gathering" : "whole source on completed mining work";
                row.Work = Finite(definition.HarvestWork); row.RequiredStrength = definition.RequiredMiningStrength;
                if (!float.IsFinite(definition.HarvestWork) || definition.HarvestWork <= 0 || definition.RequiredMiningStrength < 1)
                    Error(row.Path, "HarvestWork/RequiredMiningStrength", "Invalid harvest requirements.");
                if (definition.HarvestDrops == null) Error(row.Path, "HarvestDrops", "Missing harvest profile.");
                ProfileRewards(row, definition.HarvestDrops, "HarvestDrops", "independent reward once on source completion");
            }
            return row;
        }
        private RecipeRow Recipe(CraftingRecipe recipe, string fallback)
        {
            if (_recipes.TryGetValue(recipe, out var row)) return row;
            row = new RecipeRow { Id = recipe.Id, Path = PathOf(recipe, fallback), Category = recipe.Category,
                Description = recipe.Description, RequiredCapabilities = recipe.RequiredCapabilities ?? Array.Empty<string>(), OutputItemId = recipe.Output?.Id, OutputPath = recipe.Output?.ResourcePath,
                OutputCount = recipe.OutputCount, DurationSeconds = Finite(recipe.DurationSeconds) };
            _recipes[recipe] = row; _report.Recipes.Add(row);
            if (_catalogValid) Check(row.Path, "Validate", () => recipe.Validate(_catalog));
            if (!External(recipe)) Warn(row.Path, "resource", "Embedded recipe; edit its containing file and subresource.");
            if (recipe.Ingredients != null)
                for (int i = 0; i < recipe.Ingredients.Count; i++)
                {
                    var ingredient = recipe.Ingredients[i]; if (ingredient == null) continue;
                    row.Ingredients.Add(new IngredientRow { ItemId = ingredient.ItemId, Count = ingredient.Count,
                        EditPath = PathOf(ingredient, row.Path + "#Ingredients[" + i + "]") });
                }
            return row;
        }
        private void RecipeCatalogRows(CraftingCatalog catalog, string path, bool master)
        {
            if (_catalogValid && !master) Check(path, "Recipes", () => catalog.Validate(_catalog));
            if (catalog.Recipes == null) return;
            foreach (var recipe in catalog.Recipes.Where(x => x != null))
            { var row = Recipe(recipe, path + "#Recipes"); row.Registered |= master; Add(row.RegistrationPaths, path); }
        }
        private void GroundRows(GroundResourceCatalog catalog, string path, bool master)
        {
            if (_catalogValid && !master) Check(path, "Materials", () => catalog.Validate(_catalog));
            if (catalog.Materials == null) return;
            foreach (var material in catalog.Materials.Where(x => x != null))
            {
                string locator = PathOf(material, path + "#Materials/" + material.Id);
                var row = _report.Sources.FirstOrDefault(x => x.Path == locator);
                if (row == null)
                {
                    row = new SourceRow { Id = material.Id, Path = locator, Kind = "ground", Extraction = "one unit per successful dig",
                        Work = Finite(material.WorkPerUnit), RequiredStrength = material.RequiredShovelStrength,
                        TotalUnits = material.UnitsPerDeposit, UnitsPerBatch = 1 };
                    row.Rewards.Add(new RewardRow { ItemId = material.ItemId, EditPath = locator, Field = "ItemId",
                        Context = "one unit per successful dig", MinimumCount = 1, MaximumCount = 1, Chance = 1 });
                    _report.Sources.Add(row);
                }
                if (master) Add(row.RegistrationPaths, path);
            }
        }
        private void BiomeCatalogRows(BiomeCatalog catalog, string path)
        {
            Check(path, "Biomes", () => catalog.GetEnabledBiomes());
            if (catalog.Biomes == null) return;
            foreach (var biome in catalog.Biomes.Where(x => x != null))
            {
                if (!_biomes.TryGetValue(biome, out var locations)) _biomes[biome] = locations = new();
                Add(locations, path);
            }
        }
        private void BiomeRows(BiomeDefinition biome, List<string> catalogs)
        {
            string path = PathOf(biome, "biome/" + biome.Id);
            if (biome.Vegetation == null) { Error(path, "Vegetation", "Missing vegetation resource."); return; }
            Check(path, "climate/selection", biome.ValidateClimate);
            Check(path, "Vegetation", () => biome.Vegetation.Validate(biome.Id));
            Check(path, "Rocks", () => BiomeSpecies.Validate<RockDefinition>(biome.Rocks, biome.Id + "/Rocks", biome.RocksPerChunk > 0));
            Bind(biome, catalogs, biome.Vegetation.Plants, "Plants", biome.Vegetation.PlantPatches > 0 && biome.Vegetation.PlantsPerPatch > 0);
            Bind(biome, catalogs, biome.Vegetation.Trees, "Trees", biome.Vegetation.TreesPerChunk > 0);
            Bind(biome, catalogs, biome.Rocks, "Rocks", biome.RocksPerChunk > 0);
        }
        private void Bind(BiomeDefinition biome, List<string> catalogs, Godot.Collections.Array<BiomeSpecies> entries, string family, bool enabled)
        {
            if (entries == null) return;
            string biomePath = PathOf(biome, "biome/" + biome.Id);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry?.Definition is not PlantDefinition && entry?.Definition is not TreeDefinition && entry?.Definition is not RockDefinition) continue;
                var source = Source(entry.Definition, biomePath + "#" + family + "[" + i + "]");
                source.Biomes.Add(new BindingRow { BiomeId = biome.Id, BiomePath = biomePath,
                    CatalogPaths = catalogs.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                    EditPath = PathOf(entry, PathOf(biome.Content, biomePath) + "#" + family + "[" + i + "]"),
                    Family = family, Weight = Finite(entry.Weight), BiomeEnabled = biome.Enabled, FamilyEnabled = enabled,
                    ConfiguredForSelection = catalogs.Count > 0 && biome.Enabled && biome.SelectionWeight > 0 && enabled && entry.Weight > 0 });
            }
        }
        private void OreSettings(OreSpawnSettings settings, string path)
        {
            if (_catalogValid) Check(path, "Validate", () => settings.Validate(_catalog));
            _report.OreSpawns.Add(new OreSpawnRow { Path = path, DefinitionPath = settings.Definition?.ResourcePath,
                Enabled = settings.Enabled, ChancePerChunk = Finite(settings.ChancePerChunk), PlacementAttempts = settings.PlacementAttempts });
            if (settings.Definition != null) Add(Source(settings.Definition, path + "#Definition").RegistrationPaths, path);
        }
    }
}
