// Narrow migration for adding the manual station component to the existing placed bench scene.
using Godot;
using System;
using System.Linq;
using System.Security.Cryptography;

public static class StationRecipeCompatibility
{
    private const string Scene = "res://WORLDABLES/Objects/Workstations/FieldWorkbench/FieldWorkbench.tscn";
    private const string Definition = "res://SYSTEMS/Crafting/Stations/Definitions/FieldWorkbenchStation.tres";
    private static readonly string[] Before = { "066C7F101B598A6709DE57F760D9B3B9251938DBDADA17AD501579F08E149AD0", "1716FF84B391E4F0666CEA33C02729B30BCF0E6E0C57BCB25205CEB91FF441D0", "26D66DEBB1BBDE4859D6DFED66771C3A947B2334BB61AD45D9E8E98D7285FB16", "8C2796DE0FED8D00ABE72EEE2C8F2A30A1D4059E32CFE395B7D8AE3C777B4A2C", "C7C70F976942AC23B0F7B0ADD5D0F2A622E38FD0B339AF7C212720B70D2E1A92", "FB5F3C56463745D81B0441E84C882BAB6A2A850588E020B0B46EAB557C859C5B", "FD4D0D2B904D19BB8FC57CD36938066F7BB95056DFAB7938BD4E408B1C3E942E", "FEBF05C30BF7CE6EBDDD2F4D77FBBDB19C3B431587A52325DCF228B167A26179" };

    // =========================================================
    // Preserve known Pass A benches without authorizing arbitrary scene or capability edits.
    public static bool Allows(string path, string previous) => path == Scene && Before.Contains(previous) &&
        Hash(Scene) == "AC8505EF81E0B2BCF9CFBF267DCC236B00D9DB924702149E75ED798C50822304" && Hash(Definition) == "1FFC4403DA7847FB0AA42FE24C36A63D125DB3DCAFBA61BC06466B6550D6199F";

    // =========================================================
    // Compare reviewed text consistently across BOM and Git line-ending differences.
    private static string Hash(string path)
    {
        if (!Godot.FileAccess.FileExists(path)) return "";
        string text = Godot.FileAccess.GetFileAsString(path).TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n') + "\n";
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
    }
}
