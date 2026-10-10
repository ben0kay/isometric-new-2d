// Keeps only changed resources, with stable identities independent of live chunk nodes.
// Placement reservations preserve generation spacing without retaining collision or artwork.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class ResourceChangeData
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Definition { get; set; } = "";
    public float Work { get; set; }
    public int Units { get; set; }
    public bool Depleted { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Depth { get; set; }
    public float SpacingX { get; set; }
    public float SpacingY { get; set; }
}

public sealed class ResourceChangesData
{
    public int Version { get; set; } = 1;
    public List<ResourceChangeData> Entries { get; set; } = new();
}

public partial class ResourceChanges : Node
{
    #region State
    public const string Section = "resource-changes";
    private const string Identity = "persistent_resource_id";
    private const string Family = "persistent_resource_kind";
    private const string DefinitionPath = "persistent_resource_definition";
    private const float CellSize = 256f;
    private readonly Dictionary<string, ResourceChangeData> _changes = new();
    private readonly Dictionary<Vector2I, List<ResourceChangeData>> _reservations = new();
    private float _maximumExtent;
    public int Count => _changes.Count;
    #endregion

    #region Ownership
    // =========================================================
    // Resolve the current world rather than caching references across profile changes.
    public static ResourceChanges Find(Node context)
    {
        Node world = WorldConfig.TryFind(context)?.GetParent();
        return world?.GetNodeOrNull<ResourceChanges>("ResourceChanges");
    }

    // =========================================================
    // Give Sandbox and direct scene testing the same session persistence as Campaign.
    public static ResourceChanges Ensure(Node context)
    {
        Node world = WorldConfig.Find(context).GetParent();
        ResourceChanges changes = world.GetNodeOrNull<ResourceChanges>("ResourceChanges");
        if (changes != null) return changes;
        changes = new ResourceChanges { Name = "ResourceChanges" };
        world.AddChild(changes);
        return changes;
    }

    // =========================================================
    // This owner has no frame updates and stores no references to resource nodes.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Save Section
    // =========================================================
    // Accept old world/player saves and reject malformed or unsupported change records.
    public static ResourceChangesData ReadSection(CampaignData data)
    {
        if (!data.Sections.TryGetValue(Section, out JsonElement section))
        {
            if (data.Version == 1) return new ResourceChangesData();
            throw new InvalidDataException("Missing generated-resource save section.");
        }
        ResourceChangesData saved = section.Deserialize<ResourceChangesData>();
        if (saved == null || saved.Version != 1 || saved.Entries == null || saved.Entries.Count > 100000)
            throw new InvalidDataException("Invalid generated-resource save section.");
        HashSet<string> identities = new();
        foreach (ResourceChangeData entry in saved.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 1024 ||
                !identities.Add(entry.Id) ||
                entry.Kind is not ("rock" or "tree" or "plant" or "grass" or "ore" or "ground") ||
                string.IsNullOrEmpty(entry.Definition) ||
                !entry.Definition.StartsWith("res://", StringComparison.Ordinal) ||
                !entry.Definition.Split("::")[0].EndsWith(".tres", StringComparison.OrdinalIgnoreCase) ||
                (entry.Kind != "ground" && entry.Definition.Contains("::")) || entry.Units < 0 ||
                !float.IsFinite(entry.Work) || entry.Work < 0 ||
                !float.IsFinite(entry.X) || !float.IsFinite(entry.Y) ||
                !float.IsFinite(entry.Width) || !float.IsFinite(entry.Depth) ||
                !float.IsFinite(entry.SpacingX) || !float.IsFinite(entry.SpacingY) ||
                entry.Width < 0 || entry.Depth < 0 || entry.SpacingX < 0 || entry.SpacingY < 0 ||
                entry.Width > 100000 || entry.Depth > 100000 || entry.SpacingX > 100000 || entry.SpacingY > 100000)
                throw new InvalidDataException("Invalid generated-resource change record.");
        }
        return saved;
    }

    // =========================================================
    // Load before the detached world's Ready callbacks or chunk generation can run.
    public void Initialize(CampaignData data)
    {
        foreach (ResourceChangeData entry in ReadSection(data).Entries)
        {
            if (!Godot.FileAccess.FileExists(entry.Definition.Split("::")[0]))
                throw new InvalidDataException("Missing saved resource definition: " + entry.Definition);
            _changes.Add(entry.Id, entry);
            Reserve(entry);
        }
    }

    // =========================================================
    // Snapshot changed records only, including changes belonging to unloaded chunks.
    public void Capture(CampaignData data)
    {
        ResourceChangesData saved = new() { Entries = _changes.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList() };
        data.Sections[Section] = JsonSerializer.SerializeToElement(saved);
        data.Version = 2;
        data.Coverage = "world-player-resources";
    }
    #endregion

    #region Stable Identity
    // =========================================================
    // Bind the original scatter candidate before adding a generated node to the tree.
    public bool BindGenerated(Node2D host, Resource definition, string kind,
        Vector2I chunk, int candidate, string layer = WorldLayerId.Surface)
    {
        string id = FormattableString.Invariant($"{layer}/{kind}/{chunk.X}/{chunk.Y}/{candidate}");
        Bind(host, definition, kind, id);
        return Get(host)?.Depleted != true;
    }

    // =========================================================
    // Give hand-placed scene resources identities relative to their owning world.
    public void BindScene(Node2D host, Resource definition, string kind)
    {
        if (host.HasMeta(Identity)) return;
        Node world = WorldConfig.Find(host).GetParent();
        Bind(host, definition, kind, $"{WorldLayerMember.For(host)}/{kind}/scene/{world.GetPathTo(host)}");
    }

    // =========================================================
    // Keep identity metadata on its host without adding a helper node per resource.
    private void Bind(Node2D host, Resource definition, string kind, string id)
    {
        string path = definition?.ResourcePath ?? "";
        if (string.IsNullOrEmpty(path) || path.Contains("::"))
            throw new InvalidDataException("Persistent resources need separate definition files.");
        if (_changes.TryGetValue(id, out ResourceChangeData saved) && saved.Definition != path)
            throw new InvalidDataException("Saved resource recipe changed: " + id);
        host.SetMeta(Identity, id);
        host.SetMeta(Family, kind);
        host.SetMeta(DefinitionPath, path);
    }

    // =========================================================
    // Expose the already-bound source identity without retaining any live host reference.
    public string IdentityFor(Node2D host)
    {
        if (!host.HasMeta(Identity))
            throw new InvalidDataException("Harvest source has no persistent identity.");
        return host.GetMeta(Identity).AsString();
    }

    // Read scalar saved state without retaining a generated object's live node.
    public ResourceChangeData GetByIdentity(string id)
    {
        _changes.TryGetValue(id, out ResourceChangeData saved);
        return saved;
    }

    // =========================================================
    // Resolve a host's change record without retaining the host after unloading.
    public ResourceChangeData Get(Node2D host)
    {
        if (!host.HasMeta(Identity)) return null;
        _changes.TryGetValue(host.GetMeta(Identity).AsString(), out ResourceChangeData saved);
        return saved;
    }

    // =========================================================
    // Record accepted work, depletion or remaining ore units at the mutation point.
    public void Record(Node2D host, float work = 0, int units = 0, bool depleted = false)
    {
        if (!host.HasMeta(Identity)) return;
        string id = host.GetMeta(Identity).AsString();
        if (!_changes.TryGetValue(id, out ResourceChangeData entry))
        {
            Vector2 footprint = host is Obstacle obstacle ? obstacle.Footprint : Vector2.Zero;
            Vector2 spacing = host is Tree tree ? tree.Definition.Spacing * tree.SizeMultiplier : Vector2.Zero;
            entry = new ResourceChangeData
            {
                Id = id, Kind = host.GetMeta(Family).AsString(),
                Definition = host.GetMeta(DefinitionPath).AsString(),
                X = host.GlobalPosition.X, Y = host.GlobalPosition.Y,
                Width = footprint.X, Depth = footprint.Y,
                SpacingX = spacing.X, SpacingY = spacing.Y
            };
            _changes.Add(id, entry);
        }
        bool wasDepleted = entry.Depleted;
        entry.Work = work; entry.Units = units; entry.Depleted = depleted;
        if (depleted && !wasDepleted) Reserve(entry);
    }

    // =========================================================
    // Share chunk/slot identities with shader-based ground deposits.
    public ResourceChangeData Ground(Vector3I slot)
    {
        _changes.TryGetValue(GroundId(slot), out ResourceChangeData entry);
        return entry;
    }

    // =========================================================
    // Ground patches retain extracted units and unfinished shovel work without nodes.
    public void RecordGround(Vector3I slot, GroundResourceDefinition definition, int removed, float work)
    {
        string id = GroundId(slot);
        if (!_changes.TryGetValue(id, out ResourceChangeData entry))
            _changes.Add(id, entry = new ResourceChangeData { Id = id, Kind = "ground", Definition = definition.ResourcePath });
        if (entry.Definition != definition.ResourcePath)
            throw new InvalidDataException("Saved ground deposit recipe changed: " + id);
        entry.Units = removed; entry.Work = work;
        entry.Depleted = removed >= definition.UnitsPerDeposit;
    }

    // =========================================================
    // Format ground identities without culture-sensitive coordinates.
    private static string GroundId(Vector3I slot)
    {
        return FormattableString.Invariant($"surface/ground/{slot.X}/{slot.Y}/{slot.Z}");
    }
    #endregion

    #region Generation Reservations
    // =========================================================
    // Keep only harvested solid footprints in a small spatial index for generation.
    private void Reserve(ResourceChangeData entry)
    {
        if (!entry.Depleted || entry.Kind is not ("rock" or "tree" or "ore")) return;
        Vector2I cell = Cell(new Vector2(entry.X, entry.Y));
        if (!_reservations.TryGetValue(cell, out List<ResourceChangeData> entries))
            _reservations.Add(cell, entries = new());
        entries.Add(entry);
        _maximumExtent = Mathf.Max(_maximumExtent,
            Mathf.Max(Mathf.Max(entry.Width, entry.Depth), Mathf.Max(entry.SpacingX, entry.SpacingY)));
    }

    // =========================================================
    // Query nearby reservation records only during chunk population.
    private IEnumerable<ResourceChangeData> Nearby(Vector2 point, Vector2 size)
    {
        float radius = (_maximumExtent + Mathf.Max(size.X, size.Y)) * 0.5f;
        Vector2I low = Cell(point - Vector2.One * radius), high = Cell(point + Vector2.One * radius);
        for (int y = low.Y; y <= high.Y; y++)
        for (int x = low.X; x <= high.X; x++)
            if (_reservations.TryGetValue(new Vector2I(x, y), out List<ResourceChangeData> entries))
                foreach (ResourceChangeData entry in entries) yield return entry;
    }

    // =========================================================
    // Preserve the source's old generation footprint, while allowing its own candidate.
    public bool Blocks(Vector2 point, Vector2 footprint, Vector2 padding)
    {
        foreach (ResourceChangeData entry in Nearby(point, footprint + padding * 2))
        {
            Vector2 difference = point - new Vector2(entry.X, entry.Y);
            if (difference.LengthSquared() < 0.01f) continue;
            Vector2 separation = (footprint + new Vector2(entry.Width, entry.Depth)) * 0.5f + padding;
            if (Mathf.Abs(difference.X) < separation.X && Mathf.Abs(difference.Y) < separation.Y) return true;
        }
        return false;
    }

    // =========================================================
    // Preserve tree canopy spacing as well as its narrow trunk footprint.
    public bool NearTree(Vector2 point, Vector2 spacing)
    {
        foreach (ResourceChangeData entry in Nearby(point, spacing))
        {
            if (entry.Kind != "tree") continue;
            Vector2 difference = point - new Vector2(entry.X, entry.Y);
            if (difference.LengthSquared() < 0.01f) continue;
            Vector2 combined = (spacing + new Vector2(entry.SpacingX, entry.SpacingY)) * 0.5f;
            float width = Mathf.Max(1, combined.X), depth = Mathf.Max(1, combined.Y);
            if (difference.X * difference.X / (width * width) + difference.Y * difference.Y / (depth * depth) < 1) return true;
        }
        return false;
    }

    // =========================================================
    // Convert a logical ground position to a reservation lookup cell.
    private static Vector2I Cell(Vector2 point)
    {
        return new Vector2I(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Y / CellSize));
    }
    #endregion
}
