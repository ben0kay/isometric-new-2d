// Saves surviving population/scene entities and logical groups without storing engine nodes.
// Population restores stay budgeted; authored actors restore only on available active terrain.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class EntitySaveData
{
    public string Id { get; set; } = "";
    public string Owner { get; set; } = "";
    public string OriginLayer { get; set; } = WorldLayerId.Surface;
    public int ChunkX { get; set; }
    public int ChunkY { get; set; }
    public int Slot { get; set; }
    public string Scene { get; set; } = "";
    public string Definition { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public float X { get; set; }
    public float Y { get; set; }
    public float HomeX { get; set; }
    public float HomeY { get; set; }
    public int Health { get; set; }
    public ulong RandomSeed { get; set; }
    public ulong RandomState { get; set; }
    public bool HasRuntimeState { get; set; }
    public double WanderWait { get; set; }
    public double FeedingCooldown { get; set; }
    public double TargetTimer { get; set; }
    public double DecisionTimer { get; set; }
    public string GroupId { get; set; } = "";
}
public sealed class EntityGroupSaveData
{
    public string Id { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public string DisplayName { get; set; } = "Group";
    public bool ShareThreats { get; set; }
    public int MinimumMembers { get; set; }
    public bool FormationComplete { get; set; }
    public bool Dissolved { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public List<string> Members { get; set; } = new();
    public bool HasRoaming { get; set; }
    public int Mode { get; set; }
    public float WanderRadius { get; set; }
    public float RoamRadius { get; set; }
    public float StepDistance { get; set; }
    public double Interval { get; set; }
    public float HomeX { get; set; }
    public float HomeY { get; set; }
    public ulong RandomState { get; set; }
    public double RemainingStep { get; set; }
    public bool RoamingRunning { get; set; }
}
public sealed class EntitySavesData
{
    public int Version { get; set; } = 1;
    public List<EntitySaveData> Entities { get; set; } = new();
    public List<EntityGroupSaveData> Groups { get; set; } = new();
}

public partial class EntitySaves : Node
{
    #region State And Ownership
    public const string Section = "entities-groups";
    private readonly Dictionary<string, EntitySaveData> _entities = new();
    private readonly Dictionary<string, EntityGroupSaveData> _groups = new();
    private readonly Dictionary<string, Entity> _sceneNodes = new();
    private readonly Dictionary<string, EntityGroup> _groupNodes = new();
    private readonly List<string> _pendingScenes = new();
    private int _sceneCursor;
    private double _timer;
    public IEnumerable<EntitySaveData> SavedEntities => _entities.Values;

    // =========================================================
    // Resolve only this gameplay world's helper.
    public static EntitySaves Find(Node context) =>
        WorldConfig.TryFind(context)?.GetParent().GetNodeOrNull<EntitySaves>("EntitySaves");

    // =========================================================
    // Keep authored restoration on one low-frequency budget, independent of actor count.
    public override void _Ready() { SetProcess(false); SetPhysicsProcess(_pendingScenes.Count > 0); }

    // =========================================================
    // Release detached authored scene branches if their world closes before restoration.
    public override void _ExitTree()
    {
        foreach (Entity actor in _sceneNodes.Values)
            if (GodotObject.IsInstanceValid(actor) && !actor.IsInsideTree()) actor.Free();
        foreach (EntityGroup group in _groupNodes.Values)
            if (GodotObject.IsInstanceValid(group) && !group.IsInsideTree()) group.Free();
        _sceneNodes.Clear(); _groupNodes.Clear();
    }

    // =========================================================
    // Validate durable paths rather than runtime resources or traversal paths.
    private static bool Path(string value, string extension) => !string.IsNullOrEmpty(value) &&
        value.Length <= 1024 && value.StartsWith("res://", StringComparison.Ordinal) &&
        !value.Contains("::") && !value.Any(char.IsControl) && value.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
    // =========================================================
    // Bound stable identities and labels before allocating save lookup tables.
    private static bool Text(string value, bool empty = false) => value != null && value.Length <= 1024 &&
        (empty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);
    // =========================================================
    // Reject invalid timer values before restoring gameplay-time state.
    private static bool TimeValue(double value) => double.IsFinite(value) && value >= 0 && value <= 1e9;
    #endregion

    #region Format And Detached Initialization
    // =========================================================
    // Accept older campaigns with empty living/group history and reject conflicting records.
    public static EntitySavesData ReadSection(CampaignData data)
    {
        if (!data.Sections.TryGetValue(Section, out JsonElement section))
        {
            if (data.Version <= 4) return new();
            throw new InvalidDataException("Missing living-entity/group save section.");
        }
        EntitySavesData saved = section.Deserialize<EntitySavesData>();
        if (saved == null || saved.Version != 1 || saved.Entities == null || saved.Groups == null ||
            saved.Entities.Count + saved.Groups.Count > 100000) throw new InvalidDataException("Invalid entity/group section.");
        HashSet<string> dead = EntityDeaths.ReadSection(data).Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        HashSet<string> ids = new();
        Dictionary<string, EntitySaveData> actors = new();
        foreach (EntitySaveData actor in saved.Entities)
        {
            if (actor == null || !Text(actor.Id) || !ids.Add(actor.Id) || dead.Contains(actor.Id) ||
                !Text(actor.Owner, true) || !Text(actor.GroupId, true) || !Path(actor.Scene, ".tscn") || !Path(actor.Definition, ".tres") ||
                !float.IsFinite(actor.X) || !float.IsFinite(actor.Y) || !float.IsFinite(actor.HomeX) || !float.IsFinite(actor.HomeY) ||
                actor.Health <= 0 || !TimeValue(actor.WanderWait) || !TimeValue(actor.FeedingCooldown) ||
                !TimeValue(actor.TargetTimer) || !TimeValue(actor.DecisionTimer) || actor.Slot < 0 || actor.Slot > 7)
                throw new InvalidDataException("Invalid or conflicting living entity.");
            WorldLayerId.Validate(actor.Layer); WorldLayerId.Validate(actor.OriginLayer);
            actors.Add(actor.Id, actor);
            string identity = actor.Owner.Length > 0
                ? EntityDeaths.PopulationId(actor.Owner, actor.OriginLayer, data.Seed, new(actor.ChunkX, actor.ChunkY), actor.Slot)
                : actor.Id;
            if (identity != actor.Id || actor.Owner.Length == 0 && !actor.Id.StartsWith("scene:", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid entity origin identity.");
        }
        ids.Clear(); HashSet<string> keys = new(); HashSet<string> memberships = new();
        foreach (EntityGroupSaveData group in saved.Groups)
        {
            if (group == null || !Text(group.Id) || !ids.Add(group.Id) || !Text(group.GroupId) ||
                !Text(group.DisplayName, true) || group.MinimumMembers < 1 || group.MinimumMembers > 100000 ||
                !float.IsFinite(group.X) || !float.IsFinite(group.Y) || group.Members == null || group.Members.Count > 100000 ||
                !keys.Add(group.Layer + ":" + group.GroupId) || group.Dissolved && group.Members.Count > 0 ||
                group.HasRoaming && (group.Mode is < 0 or > 1 || !float.IsFinite(group.HomeX) || !float.IsFinite(group.HomeY) ||
                    !float.IsFinite(group.WanderRadius) || group.WanderRadius <= 0 || !float.IsFinite(group.RoamRadius) || group.RoamRadius <= 0 ||
                    !float.IsFinite(group.StepDistance) || group.StepDistance < 0 || !TimeValue(group.Interval) || group.Interval < 1 ||
                    !TimeValue(group.RemainingStep))) throw new InvalidDataException("Invalid entity group.");
            WorldLayerId.Validate(group.Layer);
            foreach (string member in group.Members)
            {
                actors.TryGetValue(member ?? "", out EntitySaveData actor);
                if (!Text(member) || !memberships.Add(member) || actor == null || actor.Layer != group.Layer || actor.GroupId != group.GroupId)
                    throw new InvalidDataException("Invalid logical group membership.");
            }
        }
        foreach (EntitySaveData actor in saved.Entities)
            if (actor.GroupId.Length > 0 && !memberships.Contains(actor.Id))
                throw new InvalidDataException("Missing saved group membership.");
        return saved;
    }

    // =========================================================
    // Hydrate before Ready, preserving authored scene branches and their custom children.
    public void Initialize(CampaignData data, Node world)
    {
        EntitySavesData saved = ReadSection(data);
        foreach (EntitySaveData actor in saved.Entities)
        {
            if (!Godot.FileAccess.FileExists(actor.Scene) || !Godot.FileAccess.FileExists(actor.Definition))
                throw new InvalidDataException("Saved entity recipe is unavailable: " + actor.Id);
            EntityDefinition definition = GD.Load<EntityDefinition>(actor.Definition);
            definition.Validate();
            if (actor.Health > definition.MaxHealth) throw new InvalidDataException("Saved entity health exceeds its definition.");
            if (actor.Owner.Length > 0)
            {
                EnemyPopulation owner = world.GetNodeOrNull<EnemyPopulation>(actor.Owner);
                if (owner == null || owner.EnemyScene?.ResourcePath != actor.Scene || WorldLayerMember.For(owner) != actor.OriginLayer)
                    throw new InvalidDataException("Saved population owner or prefab changed: " + actor.Owner);
            }
            if (!WorldConfig.Find(world).GetLayerCatalog().Layers.Any(l => l.Id == actor.Layer))
                throw new InvalidDataException("Saved entity layer is unavailable: " + actor.Layer);
            _entities.Add(actor.Id, actor);
        }
        foreach (EntityGroupSaveData group in saved.Groups) _groups.Add(group.Id, group);
        List<Node> branches = new(); Collect(world.GetNode("WorldObjects"), branches);
        foreach (Node node in branches)
        {
            if (node is Entity actor && _entities.TryGetValue(actor.PersistentId, out EntitySaveData state))
            {
                actor.GetParent().RemoveChild(actor); _sceneNodes.Add(state.Id, actor); _pendingScenes.Add(state.Id);
            }
            else if (node is EntityGroup group)
            {
                group.PersistentId = $"scene_group:{WorldLayerMember.For(group)}:{world.GetPathTo(group)}";
                if (!_groups.TryGetValue(group.PersistentId, out EntityGroupSaveData groupState)) continue;
                if ((group.GetNodeOrNull<GroupRoaming>("Systems/Roaming") != null) != groupState.HasRoaming)
                    throw new InvalidDataException("Authored group roaming recipe changed: " + groupState.Id);
                group.GetParent().RemoveChild(group);
                if (groupState.Dissolved) group.Free(); else _groupNodes.Add(groupState.Id, group);
            }
        }
        foreach (EntitySaveData state in saved.Entities.Where(e => e.Owner.Length == 0))
            if (!_sceneNodes.ContainsKey(state.Id)) throw new InvalidDataException("Authored entity scene changed: " + state.Id);
    }

    // =========================================================
    // Stop at independently persisted entity/group branches.
    private static void Collect(Node node, List<Node> branches)
    {
        if (node is Entity) { branches.Add(node); return; }
        if (node is EntityGroup) branches.Add(node);
        foreach (Node child in node.GetChildren()) Collect(child, branches);
    }

    // =========================================================
    // Restore one authored actor per interval; inactive/distant layers keep only records.
    public override void _PhysicsProcess(double delta)
    {
        _timer -= delta; if (_timer > 0) return; _timer = 0.25;
        WorldLayerRuntime layers = WorldLayerRuntime.Find(this); if (layers == null) return;
        for (int work = 0; work < Math.Min(8, _pendingScenes.Count); work++)
        {
            _sceneCursor %= _pendingScenes.Count; string id = _pendingScenes[_sceneCursor];
            EntitySaveData state = _entities[id]; Vector2 point = new(state.X, state.Y);
            if (state.Layer != layers.ActiveLayer || !layers.IsAvailable(state.Layer, point, 12)) { _sceneCursor++; continue; }
            Entity actor = _sceneNodes[id]; Prepare(actor, state);
            Node2D root = GetParent().GetNode<Node2D>("WorldObjects"); actor.Position = root.ToLocal(point); root.AddChild(actor);
            _sceneNodes.Remove(id); _pendingScenes.RemoveAt(_sceneCursor);
            if (_pendingScenes.Count == 0) SetPhysicsProcess(false);
            return;
        }
    }

    // =========================================================
    // Apply ownership before child Ready callbacks bind health, membership and navigation.
    public static void Prepare(Entity actor, EntitySaveData state)
    {
        actor.PersistentId = state.Id; actor.Definition = GD.Load<EntityDefinition>(state.Definition)
            ?? throw new InvalidDataException("Entity definition is unavailable.");
        actor.Definition.Validate();
        if (state.Health > actor.Definition.MaxHealth) throw new InvalidDataException("Saved entity health exceeds its definition.");
        actor.SpawnHome = new(state.HomeX, state.HomeY); actor.RandomSeed = state.RandomSeed;
        actor.GroupId = state.GroupId; actor.PendingSave = state;
        WorldLayerMember.Attach(actor, state.Layer).SetLayer(state.Layer);
    }
    #endregion

    #region Logical Groups
    // =========================================================
    // Use authored identities or stable layer-scoped IDs for generic runtime groups.
    public void RegisterGroup(EntityGroup group)
    {
        if (string.IsNullOrEmpty(group.PersistentId)) group.PersistentId = $"group:{WorldLayerMember.For(group)}:{group.GroupId}";
        if (!_groups.ContainsKey(group.PersistentId)) _groups.Add(group.PersistentId, group.CaptureSave());
    }

    // =========================================================
    // Recreate a saved group before its first live member joins; dormant members count logically.
    public EntityGroup ResolveGroup(Entity actor, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        string layer = WorldLayerMember.For(actor);
        EntityGroup existing = EntityGroup.Find(actor, id); if (existing != null) return existing;
        EntityGroupSaveData state = _groups.Values.FirstOrDefault(g => g.GroupId == id && g.Layer == layer);
        if (state == null || state.Dissolved) return null;
        EntityGroup group;
        if (_groupNodes.Remove(state.Id, out EntityGroup authored)) group = authored;
        else
        {
            group = new EntityGroup { Name = "RestoredGroup" };
            if (state.HasRoaming)
            {
                Node systems = new() { Name = "Systems" }; group.AddChild(systems);
                systems.AddChild(new GroupRoaming { Name = "Roaming" });
            }
        }
        group.PersistentId = state.Id; group.GroupId = state.GroupId;
        group.Position = GetParent().GetNode<Node2D>("WorldObjects").ToLocal(new(state.X, state.Y));
        WorldLayerMember.Attach(group, state.Layer).SetLayer(state.Layer);
        GetParent().GetNode("WorldObjects").AddChild(group);
        return group;
    }

    // =========================================================
    // Restore configuration, reserved memberships and roaming before normal group use.
    public void ApplyGroup(EntityGroup group)
    {
        if (string.IsNullOrEmpty(group.PersistentId) || !_groups.TryGetValue(group.PersistentId, out EntityGroupSaveData saved)) return;
        group.RestoreSave(saved);
    }

    // =========================================================
    // Preserve dissolution so an authored group cannot reappear on the next load.
    public void GroupDissolved(EntityGroup group)
    {
        if (string.IsNullOrEmpty(group.PersistentId)) return;
        EntityGroupSaveData state = group.CaptureSave(); state.Dissolved = true; state.Members.Clear(); _groups[state.Id] = state;
    }
    #endregion

    #region Capture
    // =========================================================
    // Snapshot all population caches first, then live actors and groups without duplicating IDs.
    public void Capture(CampaignData data)
    {
        Node world = GetParent(); EntityDeaths deaths = EntityDeaths.Find(this);
        foreach (EnemyPopulation population in world.GetNode("Systems").GetChildren().OfType<EnemyPopulation>())
            foreach (EntitySaveData state in population.CaptureEntities()) _entities[state.Id] = state;
        HashSet<string> live = new();
        foreach (Node node in GetTree().GetNodesInGroup("entities"))
        {
            if (node is not Entity actor || !world.IsAncestorOf(actor) || string.IsNullOrEmpty(actor.PersistentId) ||
                actor.IsQueuedForDeletion() || actor.Health?.IsAlive != true) continue;
            if (!live.Add(actor.PersistentId)) throw new InvalidDataException("Duplicate live entity identity.");
            _entities.TryGetValue(actor.PersistentId, out EntitySaveData state);
            if (state == null && !actor.PersistentId.StartsWith("scene:", StringComparison.Ordinal))
                throw new InvalidDataException("Persistent runtime entity has no owning population.");
            _entities[actor.PersistentId] = actor.CaptureSave(state ?? new() { Id = actor.PersistentId });
        }
        foreach (string id in _entities.Keys.ToArray()) if (deaths.WasKilled(id)) _entities.Remove(id);
        foreach (Node node in GetTree().GetNodesInGroup("entity_groups"))
            if (node is EntityGroup group && world.IsAncestorOf(group) && !group.IsQueuedForDeletion() && !string.IsNullOrEmpty(group.PersistentId))
                _groups[group.PersistentId] = group.CaptureSave();
        var memberships = _entities.Values.Where(e => e.GroupId.Length > 0).ToLookup(e => e.Layer + ":" + e.GroupId);
        foreach (EntityGroupSaveData group in _groups.Values)
        {
            group.Members = group.Dissolved ? new() : memberships[group.Layer + ":" + group.GroupId].Select(e => e.Id).ToList();
            if (!group.Dissolved && group.FormationComplete && group.Members.Count < group.MinimumMembers) group.Dissolved = true;
        }
        HashSet<string> availableGroups = _groups.Values.Where(g => !g.Dissolved).Select(g => g.Layer + ":" + g.GroupId).ToHashSet(StringComparer.Ordinal);
        foreach (EntitySaveData actor in _entities.Values)
            if (actor.GroupId.Length > 0 && !availableGroups.Contains(actor.Layer + ":" + actor.GroupId)) actor.GroupId = "";
        foreach (EntityGroupSaveData group in _groups.Values.Where(g => g.Dissolved)) group.Members.Clear();
        EntitySavesData saved = new() { Entities = _entities.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList(),
            Groups = _groups.Values.OrderBy(g => g.Id, StringComparer.Ordinal).ToList() };
        data.Sections[Section] = JsonSerializer.SerializeToElement(saved); data.Version = 5; data.Coverage = "world-player-resources-objects-entities";
        ReadSection(data);
    }
    #endregion
}
