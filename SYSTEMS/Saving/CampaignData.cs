// Versioned campaign data; runtime nodes and shared resources never enter JSON.
using System;
using System.Collections.Generic;

public sealed class CampaignData
{
    public int Version { get; set; } = 1;
    public string Coverage { get; set; } = "world-player-only";
    public string ProfileId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public DateTime SavedUtc { get; set; }
    public uint Seed { get; set; }
    public float SpawnX { get; set; }
    public float SpawnY { get; set; }
    public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = new();
    public Dictionary<string, string> Resources { get; set; } = new();
    public PlayerSaveData Player { get; set; } = new();
    public double WorldSeconds { get; set; }
    public Dictionary<string, System.Text.Json.JsonElement> Sections { get; set; } = new();
}

public sealed class PlayerSaveData
{
    public string Layer { get; set; } = "surface";
    public float X { get; set; }
    public float Y { get; set; }
    public int Health { get; set; }
    public float[] Stats { get; set; } = Array.Empty<float>();
    public Dictionary<string, List<ModifierSaveData>> Modifiers { get; set; } = new();
    public float[] Reserves { get; set; } = Array.Empty<float>();
    public string Backpack { get; set; } = "";
    public Dictionary<string, string> ItemResources { get; set; } = new();
    public string[] Tools { get; set; } = Array.Empty<string>();
    public int SelectedTool { get; set; }
    public List<StackSaveData> Bag { get; set; } = new();
    public List<HotbarSaveData> Hotbar { get; set; } = new();
    public int SelectedHotbar { get; set; }
    public List<CraftSaveData> Crafting { get; set; } = new();
    public double[] Survival { get; set; } = Array.Empty<double>();
    public bool SprintExhausted { get; set; }
}

public sealed class ModifierSaveData
{
    public int Stat { get; set; }
    public int Operation { get; set; }
    public float Amount { get; set; }
}
public sealed class StackSaveData
{
    public string Item { get; set; } = "";
    public int Count { get; set; }
}
public sealed class HotbarSaveData
{
    public int Area { get; set; } = -1;
    public int Index { get; set; }
    public string Item { get; set; } = "";
}
public sealed class CraftSaveData
{
    public string Recipe { get; set; } = "";
    public int Remaining { get; set; }
    public double Elapsed { get; set; }
}
