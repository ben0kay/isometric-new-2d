// Shared menu destinations; future pause menus can reuse this scene routing.
using Godot;
using System;

public static class MenuNavigation
{
    public const string BootScene =
        "res://UI/Menus/Boot/BootScreen.tscn";

    public const string MainScene =
        "res://UI/Menus/Main/MainMenu.tscn";

    public const string CampaignScene =
        "res://WORLD/Scenes/world_infinite.tscn";

    public const string SandboxScene =
        "res://DEBUG/DebugBiomeLauncher/debugBiomeLauncher.tscn";

    // =========================================================
    // Restore the old pause state if loading the destination fails.
    public static void Open(Node context, string path)
    {
        SceneTree tree = context.GetTree();
        bool wasPaused = tree.Paused;
        tree.Paused = false;

        Error error = tree.ChangeSceneToFile(path);
        if (error == Error.Ok) return;

        tree.Paused = wasPaused;
        throw new InvalidOperationException(
            $"Could not open {path}: {error}");
    }
}