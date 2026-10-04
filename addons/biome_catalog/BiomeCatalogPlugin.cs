// Automatically maintains the saved biome catalog from definition resources.
// Runs only in the editor; exported gameplay reads the saved catalog directly.
#if TOOLS
using Godot;
using System;
using System.Collections.Generic;

[Tool]
public partial class BiomeCatalogPlugin : EditorPlugin
{
    #region Configuration
    private const string DefinitionsPath =
        "res://WORLD/Generation/Biomes/Definitions";
    private const string CatalogPath =
        "res://WORLD/Generation/Biomes/BiomeCatalog.tres";
    #endregion

    #region State
    private EditorFileSystem _filesystem;
    private double _refreshDelay = -1;
    private bool _refreshing;
    #endregion

    #region Lifecycle
    // =========================================================
    // Watch filesystem changes and schedule an initial catalog refresh.
    public override void _EnterTree()
    {
        _filesystem = EditorInterface.Singleton.GetResourceFilesystem();
        _filesystem.FilesystemChanged += ScheduleRefresh;
        AddToolMenuItem("Refresh Biome Catalog",
            Callable.From(RefreshFromMenu));
        ScheduleRefresh();
        SetProcess(true);
    }

    // =========================================================
    // Remove editor subscriptions when the plugin is disabled or unloaded.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_filesystem))
            _filesystem.FilesystemChanged -= ScheduleRefresh;
        RemoveToolMenuItem("Refresh Biome Catalog");
    }

    // =========================================================
    // Wait for filesystem scanning to finish before rebuilding catalog membership.
    public override void _Process(double delta)
    {
        if (_refreshDelay < 0 || _refreshing) return;
        if (_filesystem.IsScanning() || _filesystem.IsImporting()) return;
        _refreshDelay -= delta;
        if (_refreshDelay > 0) return;

        _refreshDelay = -1;
        RefreshCatalog();
    }

    // =========================================================
    // Validate and refresh once more before the editor launches the game.
    public override bool _Build()
    {
        _refreshDelay = -1;
        return RefreshCatalog();
    }
    #endregion

    #region Discovery
    // =========================================================
    // Debounce file additions, removals and moves into one refresh.
    private void ScheduleRefresh()
    {
        _refreshDelay = 0.35;
    }

    // =========================================================
    // Provide a manual recovery command without requiring catalog edits.
    private void RefreshFromMenu()
    {
        ScheduleRefresh();
    }

    // =========================================================
    // Discover definitions and save resource references when membership changes.
    private bool RefreshCatalog()
    {
        if (_refreshing) return true;
        _refreshing = true;

        try
        {
            List<string> paths = new();
            CollectPaths(DefinitionsPath, paths);
            paths.Sort(StringComparer.Ordinal);

            BiomeCatalog discovered = new();
            foreach (string path in paths)
            {
                Resource resource = ResourceLoader.Load(path);
                if (resource == null)
                    throw new InvalidOperationException(
                        $"Could not load biome resource: {path}");

                if (resource is BiomeDefinition biome)
                    discovered.Biomes.Add(biome);
            }

            // Check missing IDs, duplicate IDs and the enabled biome count.
            discovered.GetEnabledBiomes();

            BiomeCatalog current = ResourceLoader.Exists(CatalogPath)
                ? ResourceLoader.Load<BiomeCatalog>(CatalogPath) : null;
            if (SameMembership(current, discovered)) return true;

            if (current != null)
                current.Biomes = discovered.Biomes;
            else
                current = discovered;

            Error error = ResourceSaver.Save(current, CatalogPath);
            if (error != Error.Ok)
                throw new InvalidOperationException(
                    $"Could not save biome catalog: {error}");

            _filesystem.UpdateFile(CatalogPath);
            GD.Print($"[Biomes] Catalog updated: {current.Biomes.Count} definition(s).");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError($"[Biomes] {error.Message}");
            return false;
        }
        finally
        {
            _refreshing = false;
        }
    }

    // =========================================================
    // Collect resource files beneath Definitions, including optional subfolders.
    private static void CollectPaths(string folder, List<string> paths)
    {
        using DirAccess directory = DirAccess.Open(folder);
        if (directory == null)
            throw new InvalidOperationException(
                $"Biome definitions folder is missing: {folder}");

        directory.ListDirBegin();
        string entry;
        while ((entry = directory.GetNext()) != "")
        {
            if (entry.StartsWith(".", StringComparison.Ordinal)) continue;
            string path = folder + "/" + entry;

            if (directory.CurrentIsDir())
                CollectPaths(path, paths);
            else if (entry.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)
                || entry.EndsWith(".res", StringComparison.OrdinalIgnoreCase))
                paths.Add(path);
        }
        directory.ListDirEnd();
    }

    // =========================================================
    // Avoid rewriting the catalog when its resource paths already match.
    private static bool SameMembership(BiomeCatalog a, BiomeCatalog b)
    {
        if (a == null || a.Biomes.Count != b.Biomes.Count) return false;
        for (int i = 0; i < a.Biomes.Count; i++)
        {
            if (a.Biomes[i] == null ||
                a.Biomes[i].ResourcePath != b.Biomes[i].ResourcePath)
                return false;
        }
        return true;
    }
    #endregion
}
#endif