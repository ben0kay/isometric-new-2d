// Describes debug checkboxes without coupling the menu to individual overlays.
// Providers can expose several options through one shared interface.
using Godot;
using System;
using System.Collections.Generic;

public interface IDebugOptionProvider
{
    IEnumerable<DebugOption> GetDebugOptions();
}

public sealed class DebugOption
{
    public const string Group = "debug_option_providers";

    public string Name { get; set; } = "";
    public int Order { get; set; }
    public Func<bool> Read { get; set; }
    public Action<bool> Write { get; set; }
    public DebugLegend[] Legends { get; set; } = Array.Empty<DebugLegend>();
}

public readonly struct DebugLegend
{
    public readonly string Text;
    public readonly Color Colour;

    // =========================================================
    // Describe one coloured legend line beneath a debug option.
    public DebugLegend(string text, Color colour)
    {
        Text = text;
        Colour = colour;
    }
}