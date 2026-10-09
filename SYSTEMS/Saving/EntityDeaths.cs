// Remembers killed entity identities without retaining actors or running frame updates.
// Origin identities remain unchanged when actors move between world layers.
using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class EntityDeathData
{
    public string Id { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public string Definition { get; set; } = "";
    public string Scene { get; set; } = "";
}
public sealed class EntityDeathsData
{
    public int Version { get; set; } = 1;
    public List<EntityDeathData> Entries { get; set; } = new();
}

public partial class EntityDeaths : Node
{
    #region State And Ownership
    public const string Section = "entity-deaths";
    private readonly Dictionary<string, EntityDeathData> _dead = new();
    private readonly HashSet<string> _rewarded = new();
    public int Count => _dead.Count;

    // =========================================================
    // Resolve only the helper owned by the current campaign world.
    public static EntityDeaths Find(Node context)
    {
        return WorldConfig.TryFind(context)?.GetParent().GetNodeOrNull<EntityDeaths>("EntityDeaths");
    }

    // =========================================================
    // Remembering deaths requires no periodic scans or per-entity helper nodes.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Distinguish population owners, origin layers, world seeds, chunks and slots.
    public static string PopulationId(string owner, string layer, uint seed, Vector2I chunk, int slot)
    {
        WorldLayerId.Validate(layer);
        if (string.IsNullOrWhiteSpace(owner) || slot < 0)
            throw new ArgumentException("Population identity requires an owner and nonnegative slot.");
        return string.Join(":", "population", layer, owner,
            seed.ToString(CultureInfo.InvariantCulture), chunk.X.ToString(CultureInfo.InvariantCulture),
            chunk.Y.ToString(CultureInfo.InvariantCulture), slot.ToString(CultureInfo.InvariantCulture));
    }

    // =========================================================
    // Look up a death before instantiating or preparing any population actor.
    public bool WasKilled(string id) => !string.IsNullOrEmpty(id) && _dead.ContainsKey(id);

    // =========================================================
    // Use a stable fallback loot seed for authored actors without population seeds.
    public static ulong IdentitySeed(string id)
    {
        unchecked
        {
            ulong seed = 14695981039346656037UL;
            foreach (char character in id) { seed ^= character; seed *= 1099511628211UL; }
            return seed == 0 ? 1UL : seed;
        }
    }
    #endregion

    #region Load And Validation
    // =========================================================
    // Older saves start with no death history; new saves require their named section.
    public static EntityDeathsData ReadSection(CampaignData data)
    {
        if (!data.Sections.TryGetValue(Section, out JsonElement section))
        {
            if (data.Version <= 3) return new EntityDeathsData();
            throw new InvalidDataException("Missing entity-death save section.");
        }
        EntityDeathsData saved = section.Deserialize<EntityDeathsData>();
        if (saved == null || saved.Version != 1 || saved.Entries == null || saved.Entries.Count > 100000)
            throw new InvalidDataException("Invalid entity-death save section.");
        HashSet<string> ids = new();
        foreach (EntityDeathData entry in saved.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 1024 ||
                entry.Id.Any(char.IsControl) || !ids.Add(entry.Id) ||
                string.IsNullOrEmpty(entry.Layer) || entry.Layer.Length > 128 ||
                !ResourcePath(entry.Definition, ".tres") || !ResourcePath(entry.Scene, ".tscn"))
                throw new InvalidDataException("Invalid entity-death record.");
            WorldLayerId.Validate(entry.Layer);
        }
        return saved;
    }

    // =========================================================
    // Require stable external resource paths rather than runtime resource identities.
    private static bool ResourcePath(string path, string extension)
    {
        return !string.IsNullOrEmpty(path) && path.Length <= 1024 &&
            path.StartsWith("res://", StringComparison.Ordinal) && !path.Contains("::") &&
            !path.Any(char.IsControl) && path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================
    // Restore history and remove dead scene actors before any child Ready callback.
    public void Initialize(CampaignData data, Node world)
    {
        foreach (EntityDeathData entry in ReadSection(data).Entries)
        {
            if (!Godot.FileAccess.FileExists(entry.Definition) || !Godot.FileAccess.FileExists(entry.Scene))
                throw new InvalidDataException("A saved entity recipe is unavailable: " + entry.Id);
            _dead.Add(entry.Id, entry);
            _rewarded.Add(entry.Id);
        }
        List<Entity> actors = new();
        CollectSceneActors(world.GetNode("WorldObjects"), actors);
        HashSet<string> identities = new();
        foreach (Entity actor in actors)
        {
            if (string.IsNullOrEmpty(actor.PersistentId))
                actor.PersistentId = $"scene:{WorldLayerMember.For(actor)}:{world.GetPathTo(actor)}";
            if (!identities.Add(actor.PersistentId))
                throw new InvalidDataException("Duplicate scene entity identity: " + actor.PersistentId);
            if (!WasKilled(actor.PersistentId)) continue;
            actor.GetParent().RemoveChild(actor);
            actor.Free();
        }
    }

    // =========================================================
    // Only authored actors already in the detached scene receive scene identities.
    private static void CollectSceneActors(Node branch, List<Entity> actors)
    {
        if (branch is Entity actor) { actors.Add(actor); return; }
        foreach (Node child in branch.GetChildren()) CollectSceneActors(child, actors);
    }
    #endregion

    #region Death And Rewards
    // =========================================================
    // Keep a lightweight tombstone; retirement and QueueFree do not call this method.
    public void Record(Entity actor)
    {
        if (string.IsNullOrEmpty(actor.PersistentId) || _dead.ContainsKey(actor.PersistentId)) return;
        EntityDeathData entry = new()
        {
            Id = actor.PersistentId, Layer = WorldLayerMember.For(actor),
            Definition = actor.Definition.ResourcePath, Scene = actor.SceneFilePath
        };
        if (!ResourcePath(entry.Definition, ".tres") || !ResourcePath(entry.Scene, ".tscn"))
            throw new InvalidDataException("Persistent entity requires external definition and scene: " + entry.Id);
        _dead.Add(entry.Id, entry);
    }

    // =========================================================
    // Coordinate both health callbacks without delivering the same identity twice.
    public bool TryClaimRewards(Entity actor)
    {
        if (string.IsNullOrEmpty(actor.PersistentId)) return true;
        Record(actor);
        return _rewarded.Add(actor.PersistentId);
    }

    // =========================================================
    // Snapshot all tombstones, including actors whose chunks or layers are unloaded.
    public void Capture(CampaignData data)
    {
        EntityDeathsData saved = new() { Entries = _dead.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList() };
        data.Sections[Section] = JsonSerializer.SerializeToElement(saved);
        data.Version = 4;
        data.Coverage = "world-player-resources-objects-deaths";
        ReadSection(data);
    }
    #endregion
}
