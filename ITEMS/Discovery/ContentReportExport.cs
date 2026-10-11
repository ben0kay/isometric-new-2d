// Run this scene with F6, or headlessly through ExportContentReport.ps1.
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

public partial class ContentReportExport : Node
{
    [Export] public string OutputDirectory { get; set; } = "user://ContentDiscovery";

    public override void _Ready()
    {
        SetProcess(false);
        try
        {
            foreach (string argument in OS.GetCmdlineUserArgs())
                if (argument.StartsWith("--content-report-dir=", StringComparison.Ordinal))
                    OutputDirectory = argument.Substring("--content-report-dir=".Length);
            string directory = ProjectSettings.GlobalizePath(OutputDirectory);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Choose an output directory.");
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            var report = ContentDiscovery.Build();
            File.WriteAllText(Path.Combine(directory, "ContentIndex.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "ContentIndex.md"), Markdown(report), new UTF8Encoding(false));
            GD.Print($"Content report: {directory}");
            GD.Print($"Registered items: {report.Items.Count(x => x.Registered)}; sources: {report.Sources.Count}; " +
                $"registered recipes: {report.Recipes.Count(x => x.Registered)}; errors: {report.Diagnostics.Count(x => x.Severity == "error")}; " +
                $"warnings: {report.Diagnostics.Count(x => x.Severity == "warning")}");
            foreach (var diagnostic in report.Diagnostics)
                GD.Print($"[{diagnostic.Severity}] {diagnostic.Path} / {diagnostic.Field}: {diagnostic.Message}");
            GetTree().Quit(report.Valid ? 0 : 1);
        }
        catch (Exception error) { GD.PushError("Content export failed: " + error); GetTree().Quit(1); }
    }

    public static string Markdown(ContentDiscovery.Report report)
    {
        static string Cell(object value) => (value?.ToString() ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        static string Join(System.Collections.Generic.IEnumerable<string> values) => string.Join("; ", values);
        static string Number(double value) => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        text.AppendLine("# Content discovery report\n");
        text.AppendLine($"Schema {report.SchemaVersion}. Validation: {(report.Valid ? "PASS" : "ERRORS FOUND")}.\n");
        text.AppendLine("Generated from resources. This is not a live campaign, spawn guarantee or editable database. Regenerate after changes.\n");
        text.AppendLine("## Items and reverse lookup\n");
        text.AppendLine("| ID | Resource | Registered | Catalog locations | Capabilities | Source definitions | Recipes consuming | Recipes producing |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var item in report.Items)
            text.AppendLine($"| {Cell(item.Id)} | {Cell(item.Path)} | {item.Registered} | {Cell(Join(item.RegistrationPaths))} | {Cell(Join(item.Capabilities))} | {Cell(Join(item.SourcePaths))} | {Cell(Join(item.ConsumedBy))} | {Cell(Join(item.ProducedBy))} |");
        text.AppendLine("\n## Source rewards\n");
        text.AppendLine("Chance is conditional on that extraction event, not on encountering/spawning the source. Ore primary quantities use min(remaining units, batch size).\n");
        text.AppendLine("| Kind / ID | Source | Item | Min–max | Chance | Context | Edit location | Field |");
        text.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var source in report.Sources)
            foreach (var reward in source.Rewards)
                text.AppendLine($"| {Cell(source.Kind + "/" + source.Id)} | {Cell(source.Path)} | {Cell(reward.ItemId)} | {reward.MinimumCount}–{reward.MaximumCount} | {Number(reward.Chance * 100)}% | {Cell(reward.Context)} | {Cell(reward.EditPath)} | {Cell(reward.Field)} |");
        text.AppendLine("\n## Extraction requirements\n");
        text.AppendLine("| Source | Extraction | Work | Strength | Total deposit units | Units per batch | Registration locations |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var source in report.Sources)
            text.AppendLine($"| {Cell(source.Path)} | {Cell(source.Extraction)} | {Number(source.Work)} | {source.RequiredStrength} | {source.TotalUnits} | {source.UnitsPerBatch} | {Cell(Join(source.RegistrationPaths))} |");
        text.AppendLine("\n## Biome selection bindings\n");
        text.AppendLine("Configured selection means catalog-listed, biome enabled with positive selection weight, family counts enabled and positive species weight. Placement profiles, climate, terrain, active layers and campaign overrides may still prevent spawning. Grass has no harvest rewards and is outside this item-source report.\n");
        text.AppendLine("| Source | Biome | Catalogs | Family | Species weight | Configured selection | Edit location |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var source in report.Sources)
            foreach (var biome in source.Biomes)
                text.AppendLine($"| {Cell(source.Path)} | {Cell(biome.BiomeId)} | {Cell(Join(biome.CatalogPaths))} | {Cell(biome.Family)} | {Number(biome.Weight)} | {biome.ConfiguredForSelection} | {Cell(biome.EditPath)} |");
        text.AppendLine("\n## Shared profiles\n");
        text.AppendLine("| Profile | Shared | Referencing sources |");
        text.AppendLine("|---|---|---|");
        foreach (var profile in report.Profiles)
            text.AppendLine($"| {Cell(profile.Path)} | {profile.Shared} | {Cell(Join(profile.SourcePaths))} |");
        text.AppendLine("\n## Crafting recipes\n");
        text.AppendLine("| ID | Resource | Registered | Ingredients | Output | Duration | Catalogs |");
        text.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var recipe in report.Recipes)
            text.AppendLine($"| {Cell(recipe.Id)} | {Cell(recipe.Path)} | {recipe.Registered} | {Cell(Join(recipe.Ingredients.Select(x => x.ItemId + " ×" + x.Count)))} | {Cell(recipe.OutputItemId)} ×{recipe.OutputCount} | {Number(recipe.DurationSeconds)}s | {Cell(Join(recipe.RegistrationPaths))} |");
        text.AppendLine("\n## Surface ore settings\n");
        text.AppendLine("These settings resources are defined options, not proof they are assigned in an active campaign. Older campaigns may retain no ore spawning.\n");
        text.AppendLine("| Settings | Ore | Enabled | Chance per chunk before placement | Attempts |");
        text.AppendLine("|---|---|---|---|---|");
        foreach (var settings in report.OreSpawns)
            text.AppendLine($"| {Cell(settings.Path)} | {Cell(settings.DefinitionPath)} | {settings.Enabled} | {Number(settings.ChancePerChunk * 100)}% | {settings.PlacementAttempts} |");
        text.AppendLine("\n## Diagnostics\n");
        text.AppendLine("Nonfinite numbers are represented as zero in JSON rows solely to keep an invalid report serializable; diagnostics are authoritative. Never apply an invalid report as content.\n");
        text.AppendLine("| Severity | Path | Field | Message |");
        text.AppendLine("|---|---|---|---|");
        foreach (var diagnostic in report.Diagnostics)
            text.AppendLine($"| {Cell(diagnostic.Severity)} | {Cell(diagnostic.Path)} | {Cell(diagnostic.Field)} | {Cell(diagnostic.Message)} |");
        return text.ToString();
    }
}
