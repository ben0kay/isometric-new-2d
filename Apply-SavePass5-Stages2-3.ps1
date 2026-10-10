# Installs Pass 5.2 and 5.3 against aa113d59. No Git operations.
# Run from your project root with Godot closed. -Preview checks without writing.
[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (!(Test-Path (Join-Path $ProjectRoot 'project.godot'))) { throw 'Choose the Godot project root.' }
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Godot*' }) {
    throw 'Close Godot before running this installer.'
}
$Utf8 = New-Object System.Text.UTF8Encoding($false)
function Normalize([string]$Text) { return $Text.TrimStart([char]0xFEFF).Replace("`r`n", "`n").TrimEnd("`r", "`n") }
function Digest([string]$Text) {
    $Hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($Hasher.ComputeHash($Utf8.GetBytes((Normalize $Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $Hasher.Dispose() }
}
if (!(Test-Path (Join-Path $ProjectRoot 'SYSTEMS/Saving/WorldObjectSaves.cs'))) {
    throw 'Install Apply-SavePass4.ps1 first. This stage requires Pass 4.'
}
$Changes = [ordered]@{}
$Expected = @{}
$Changes['ENTITIES/Core/Entity.cs'] = @'
// Coordinates shared entity lifecycle, capabilities and one movement tick.
// Species definitions select proactive combat or reactive threat behaviour.
using Godot;
using System;

public partial class Entity : EntityBody
{
    #region Configuration
    [Export] public EntityDefinition Definition { get; set; }

    [ExportGroup("Group")]
    [Export] public string GroupId { get; set; } = "";
    #endregion

    #region Public State
    public EntityGroupMember Membership { get; private set; }
    public EntityWandering Wandering { get; private set; }
    public EntityGrazing Grazing { get; private set; }
    public EntityThreatResponseComponent Threats { get; private set; }

    public Node2D Target => _targeting?.Target ?? Threats?.Target;
    public bool HasTarget =>
        _targeting?.HasTarget == true || Threats?.HasThreat == true;
    public bool HasThreat => HasTarget;
    public bool HasSight => _targeting?.HasSight == true;
    public Vector2 Home => Wandering?.Home ?? (SpawnHome ?? GlobalPosition);

    // Assigned by the owning population or detached scene; unchanged by layer transfers.
    public string PersistentId { get; set; } = "";
    public EntitySaveData PendingSave { get; set; }
    public bool StreamingRetirement { get; private set; }
    public Vector2? SpawnHome { get; set; }
    public ulong RandomSeed { get; set; }
    public bool SpawnPending { get; set; }
    public bool Initialized { get; private set; }
    public bool IsActivated { get; private set; }
    public bool IsOnScreenForAI => _onScreen;

    public override double NavigationPathInterval =>
        Definition?.PathInterval ?? 0.45;

    public int AiStaggerTicks => Mathf.Max(1, _onScreen
        ? (_config?.EnemyOnScreenStaggerTicks ?? 3)
        : (_config?.EnemyOffScreenStaggerTicks ?? 12));

    public event Action Died;
    #endregion

    #region Components And Scheduling
    private EntityTargeting _targeting;
    private EntityCombatMovement _movement;
    private EntitySequence _sequence;
    private EntityCombatController _combat;
    private TerrainVisual _visual;
    private GlobalConfig _config;
    private readonly RandomNumberGenerator _rng = new();

    private double _targetTimer, _decisionTimer;
    private uint _activeLayer;
    private bool _screenSampled, _onScreen;

    private bool Proactive =>
        Definition.Awareness == EntityAwareness.Proactive;
    #endregion

    #region Persistence
    // =========================================================
    // Snapshot durable state while leaving targets, paths and in-flight combat transient.
    public EntitySaveData CaptureSave(EntitySaveData state)
    {
        state.Id = PersistentId; state.Scene = SceneFilePath; state.Definition = Definition.ResourcePath;
        state.Layer = WorldLayerMember.For(this); state.X = GlobalPosition.X; state.Y = GlobalPosition.Y;
        state.HomeX = Home.X; state.HomeY = Home.Y; state.Health = Health.Current;
        state.RandomSeed = RandomSeed; state.RandomState = _rng.State; state.HasRuntimeState = true;
        state.TargetTimer = Math.Max(0, _targetTimer); state.DecisionTimer = Math.Max(0, _decisionTimer);
        state.GroupId = Membership?.Group != null ? Membership.GroupId : "";
        Wandering?.CaptureSave(state); Grazing?.CaptureSave(state);
        return state;
    }

    // =========================================================
    // Retirement reserves logical group membership rather than removing a living member.
    public void RetireForStreaming()
    {
        StreamingRetirement = true;
        Membership?.SuspendForStreaming();
        CollisionLayer = 0; CollisionMask = 0; Hide(); SetPhysicsProcess(false); QueueFree();
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply shared health, collision and optional weapon settings.
    public override void _EnterTree()
    {
        if (Definition == null)
            throw new InvalidOperationException(
                "Entity requires a Definition.");

        Definition.Validate();
        AddToGroup("entities");

        // Existing population, pursuit and debug services still use this group.
        if (Proactive) AddToGroup("enemies");

        MotionMode = MotionModeEnum.Floating;
        CollisionMask = Definition.BodyCollisionMask;
        _activeLayer = CollisionLayer;
        SetPhysicsProcess(false);

        Health health = GetNode<Health>("Systems/Health");
        health.MaxHealth = Definition.MaxHealth;
        health.Defense = Definition.Defense;

        if (Proactive)
        {
            Weapon weapon = Component<Weapon>("Weapon");
            weapon.Team = CombatTeam.Enemy;
            weapon.Attack =
                (Definition.Combat as RangedCombatSettings)?.Attack;
        }

        if (SpawnPending)
        {
            Hide();
            CollisionLayer = 0;
        }
    }

    // =========================================================
    // Bind shared capabilities and complete artwork before activation.
    public override async void _Ready()
    {
        try
        {
            BindSharedComponents();
            _config = WorldConfig.Find(this);
            _rng.Seed = RandomSeed != 0 ? RandomSeed : GetInstanceId();

            _targetTimer = _rng.Randf() * Definition.TargetInterval;
            _decisionTimer = _rng.Randf() * Definition.DecisionInterval;

            Membership = GetNode<EntityGroupMember>("Systems/Group");

            if (!string.IsNullOrWhiteSpace(GroupId))
                Membership.GroupId = GroupId;

            Wandering = Component<EntityWandering>("Wandering");

            if (Definition.GrazingEnabled)
            {
                Grazing = Component<EntityGrazing>("Grazing");
                Grazing.Bind(this);
            }

            if (Proactive)
            {
                _targeting = new EntityTargeting(
                    this, Definition.TargetGroups, IsEligibleTarget);

                Membership.ThreatActive = () => HasTarget;
                Membership.ThreatReceived += ReactToThreat;

                _sequence = Component<EntitySequence>("Sequence");
                _sequence.Bind(new EntitySequenceBinding
                {
                    Actor = this,
                    Weapon = GetNode<Weapon>("Systems/Weapon"),
                    RandomSeed = RandomSeed,
                    GetDefinition = () => Definition.Sequence,
                    GetTarget = () => Target,
                    IsActive = () =>
                        Initialized && IsActivated && !SpawnPending,
                    HasSight = () => HasSight,
                    GetMoveSpeed = () => Definition.MoveSpeed,
                    GetAttackRange = () => Definition.Combat.AttackRange,
                    GetForgetRange = () => Definition.ForgetRange
                });

                _combat =
                    Component<EntityCombatController>("Combat");

                _movement =
                    new EntityCombatMovement(this, Wandering, _rng);
            }
            else if (Definition.ThreatResponse != EntityThreatResponse.Ignore)
            {
                Threats =
                    Component<EntityThreatResponseComponent>("Threats");
                Threats.Bind(this);
            }

            Wandering.Bind(this, new EntityWanderSettings
            {
                Enabled = Definition.WanderingEnabled,
                Speed = Definition.WanderSpeed,
                Radius = Definition.WanderRadius,
                HomeLeash = Definition.HomeLeash,
                Wait = Definition.WanderWait,
                ArrivalDistance = Definition.WanderArrivalDistance,
                ReturnDistance = Definition.WanderReturnDistance,
                RequireDirectPath = Definition.RequireDirectWanderPath,
                ReturnBeforeWaiting = Definition.ReturnBeforeWaiting
            }, SpawnHome ?? GlobalPosition, Membership, Grazing,
                () => HasThreat, _rng, Definition.InitialWanderPause);

            if (PendingSave != null) Membership.GroupId = PendingSave.GroupId;
            Membership.JoinAssignedGroup();
            if (PendingSave != null)
            {
                Health.RestoreState(PendingSave.Health);
                if (PendingSave.HasRuntimeState)
                {
                    _rng.State = PendingSave.RandomState;
                    _targetTimer = PendingSave.TargetTimer; _decisionTimer = PendingSave.DecisionTimer;
                    Wandering.RestoreSave(PendingSave);
                    Grazing?.RestoreSave(PendingSave);
                }
                PendingSave = null;
            }

            Component<EntityDeathLoot>("DeathLoot");
            Health.Died += OnDeath;

            ImageTexture texture = await Definition.GetArtwork(this);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

            Rect2 region = Definition.PlaceholderDrawing != null
                ? new Rect2(Vector2.Zero, Definition.ArtworkSize)
                : Definition.AtlasRegion;

            _visual = TerrainVisual.Attach(
                this, region, Definition.ArtworkOrigin, Vector2.One,
                true, Definition.VisualOverride, texture);

            CanvasItem artwork = _visual.GetNode<CanvasItem>("Artwork");
            artwork.Modulate = Definition.VisualTint;

            if (artwork is Node2D node)
                node.Scale *= Definition.VisualScale;

            // The presentation component supports optional combat helpers.
            Component<EntityPresentation>("Presentation").Bind(this, artwork);

            Initialized = true;
            if (!SpawnPending) Activate();
        }
        catch (Exception error)
        {
            GD.PushError(
                $"Entity '{Definition.SpeciesId}' initialization failed: {error}");
            QueueFree();
        }
    }

    // =========================================================
    // Reuse a scene component or create one optional capability under Systems.
    private T Component<T>(string name) where T : Node, new()
    {
        Node systems = GetNode("Systems");
        T component = systems.GetNodeOrNull<T>(name);

        if (component != null) return component;

        component = new T { Name = name };
        systems.AddChild(component);
        return component;
    }

    // =========================================================
    // Disconnect actor-owned subscriptions and random resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Health))
            Health.Died -= OnDeath;

        if (Proactive && GodotObject.IsInstanceValid(Membership))
        {
            Membership.ThreatReceived -= ReactToThreat;
            Membership.ThreatActive = null;
        }

        _targeting?.Dispose();
        _rng.Dispose();
    }

    // =========================================================
    // Activate only when initialization and placement checks are complete.
    public void Activate()
    {
        if (!Initialized || !Health.IsAlive || IsActivated) return;

        SpawnPending = false;
        IsActivated = true;
        CollisionLayer = _activeLayer;
        Show();
        SetPhysicsProcess(true);
    }
    #endregion

    #region Updates
    // =========================================================
    // Coordinate behaviour before ticking movement exactly once.
    public override void _PhysicsProcess(double delta)
    {
        if (!Initialized || !IsActivated || SpawnPending ||
            IsQueuedForDeletion() || Health?.IsAlive != true)
        {
            _sequence?.Cancel();
            SetPhysicsProcess(false);
            return;
        }

        UpdateScreenState();
        if (Navigation == null) return;

        _targetTimer -= delta;
        _decisionTimer -= delta;

        Threats?.Tick(delta);
        Wandering.Tick(delta);

        if (Proactive)
            TickProactive(delta);
        else if (_decisionTimer <= 0.0 &&
            StaggeredUpdate.Due(this, AiStaggerTicks, 23))
        {
            _decisionTimer = DecisionInterval();

            if (Threats?.Decide() != true)
                Wandering.Decide();
        }

        Motor.Tick(delta);

        if (Definition.FaceMovement &&
            Velocity.LengthSquared() > 0.1f &&
            GodotObject.IsInstanceValid(_visual))
            _visual.Scale = new Vector2(
                Velocity.X < 0f ? -1f : 1f, 1f);
    }

    // =========================================================
    // Preserve proactive awareness, combat spacing and sequence execution.
    private void TickProactive(double delta)
    {
        if (HasTarget && !WorldLayerMember.Same(this, Target))
        {
            _sequence.Cancel();
            _targeting.ClearSight();
        }

        if (_targetTimer <= 0.0 &&
            StaggeredUpdate.Due(this, AiStaggerTicks, 11))
        {
            _targetTimer = _onScreen
                ? Definition.TargetInterval
                : Math.Max(Definition.TargetInterval,
                    _config.EnemyOffScreenTargetInterval);

            if (_targeting.SelectTarget(
                Definition.DetectionRange, Definition.ForgetRange))
            {
                _sequence.Cancel();
                _movement.Reset();
                Motor.Stop();
                _decisionTimer = 0.0;
            }
        }

        if (_decisionTimer <= 0.0 &&
            StaggeredUpdate.Due(this, AiStaggerTicks, 12))
        {
            _decisionTimer = DecisionInterval();
            _targeting.RefreshSight();

            if (!_sequence.IsRunning)
                _movement.Decide(
                    HasTarget ? Target : null, HasSight,
                    GetMovementSettings(), GetPursuitGoal());
        }

        _combat.Tick(delta);

        bool wasRunning = _sequence.IsRunning;
        _sequence.Tick(delta);

        if (wasRunning && !_sequence.IsRunning)
            _decisionTimer = 0.0;
    }

    // =========================================================
    // Use the configured decision interval with existing offscreen throttling.
    private double DecisionInterval()
    {
        return _onScreen
            ? Definition.DecisionInterval
            : Math.Max(Definition.DecisionInterval,
                _config.EnemyOffScreenDecisionInterval);
    }
    #endregion

    #region Targeting And Positioning
    // =========================================================
    // Accept configured target groups without assuming a particular species.
    private bool IsEligibleTarget(Node2D candidate)
    {
        if (Definition.TargetGroups == null) return false;

        foreach (string group in Definition.TargetGroups)
            if (!string.IsNullOrWhiteSpace(group) &&
                candidate.IsInGroup(group))
                return true;

        return false;
    }

    // =========================================================
    // Accept damage and group alerts for the proactive targeting behaviour.
    private void ReactToThreat(Node2D attacker)
    {
        if (!Initialized || !IsActivated || SpawnPending ||
            IsQueuedForDeletion() || Health?.IsAlive != true ||
            !EntityCombat.IsLiving(attacker) ||
            !IsEligibleTarget(attacker) ||
            !WorldLayerMember.Same(this, attacker) ||
            GlobalPosition.DistanceSquaredTo(attacker.GlobalPosition) >
                Definition.ForgetRange * Definition.ForgetRange)
            return;

        if (_targeting.SetTarget(attacker))
        {
            _sequence.Cancel();
            _movement.Reset();
            Wandering.Interrupt(false);
        }

        _targetTimer = Definition.TargetInterval;
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Supply shared chase or ranged spacing settings from the definition.
    private EntityCombatMovementSettings GetMovementSettings()
    {
        EntityCombatSettings combat = Definition.Combat;

        EntityCombatMovementSettings settings = new()
        {
            Speed = Definition.MoveSpeed,
            StopDistance = combat.StopDistance,
            Positioning = combat is MeleeCombatSettings
                ? EntityCombatPositioning.Chase
                : EntityCombatPositioning.None
        };

        if (combat is RangedCombatSettings ranged)
        {
            settings.Positioning = EntityCombatPositioning.KeepDistance;
            settings.PreferredRange = ranged.PreferredRange;
            settings.AttackRange = ranged.AttackRange;
            settings.BackAwayRange = ranged.BackAwayRange;
            settings.RangeBias = ranged.RangeBias;
        }

        return settings;
    }
    #endregion

    #region Layer Changes
    // =========================================================
    // Ask the existing cave pursuit service for an entrance approach.
    private Vector2? GetPursuitGoal()
    {
        if (!HasTarget || WorldLayerMember.Same(this, Target))
            return null;

        WorldLayerPursuit pursuit = WorldLayerPursuit.Find(this);
        return pursuit != null &&
            pursuit.TryGetGoal(this, out Vector2 mouth) ? mouth : null;
    }

    // =========================================================
    // Clear transient movement before changing a pursuit route or world layer.
    public void ResetPursuitMovement()
    {
        _sequence?.Cancel();
        Motor?.Stop();
        Grazing?.Release();
        _targeting?.ClearSight();
        _movement?.Reset();
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Transfer layers and establish the new solo home position.
    public void CrossWorldLayer(string layer, Vector2 position)
    {
        ResetPursuitMovement();
        if (Membership?.Group != null && WorldLayerMember.For(Membership.Group) != layer) Membership.LeaveGroup();

        WorldLayerMember.Attach(
            this, WorldLayerMember.For(this)).SetLayer(layer);

        GlobalPosition = position;
        Velocity = Vector2.Zero;
        Wandering?.SetHome(position);
        _targetTimer = 0.0;
    }
    #endregion

    #region Death And Visibility
    // =========================================================
    // Stop shared behaviour; the death delivery component owns loot and remains.
    private void OnDeath()
    {
        if (IsQueuedForDeletion()) return;

        _sequence?.Cancel();
        Motor?.Stop();
        Grazing?.Release();
        Velocity = Vector2.Zero;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;

        EntityDeaths.Find(this)?.Record(this);
        Died?.Invoke();
        Hide();
        QueueFree();
    }

    // =========================================================
    // Retain staggered visibility checks and prompt decisions on screen entry.
    private void UpdateScreenState()
    {
        if (_screenSampled && !StaggeredUpdate.Due(
            this, _config.EnemyScreenCheckTicks, 10))
            return;

        bool wasOnScreen = _onScreen;

        _onScreen = ScreenVisibility.Intersects(
            this, Definition.SpawnVisualBounds,
            Definition.VisualScale, _config.EnemyScreenMarginPixels);

        _screenSampled = true;

        if (_onScreen && !wasOnScreen)
        {
            _targetTimer = 0.0;
            _decisionTimer = 0.0;
        }
    }
    #endregion
}
'@
$Changes['ENTITIES/Core/EnemyPopulation.cs'] = @'
// Streams small biome enemy populations independently from terrain generation.
// Actors prepare hidden, activate off screen, and retain session state after retirement.
using Godot;
using System.Collections.Generic;
using System;
using System.IO;

public partial class EnemyPopulation : Node
{
    #region Configuration
    [ExportGroup("Population")]
    [Export] public PackedScene EnemyScene { get; set; }
    [Export] public bool Enabled { get; set; } = true;
    [Export] public int MaximumLiveEnemies { get; set; } = 16;

    [ExportGroup("Streaming")]
    [Export] public int ScanRadiusChunks { get; set; } = 3;
    [Export] public int ChunksPerUpdate { get; set; } = 3;
    [Export] public double UpdateInterval { get; set; } = 0.25;
    [Export] public float RetireDistance { get; set; } = 2200f;

    [ExportGroup("Spawn Safety")]
    [Export] public float MinimumPlayerDistance { get; set; } = 700f;
    [Export] public float ScreenMarginPixels { get; set; } = 128f;
    [Export] public int PlacementAttempts { get; set; } = 4;
    [Export] public int PhysicsChecksPerUpdate { get; set; } = 4;
    #endregion

    #region Records And State
    private sealed class SpawnRecord
    {
        public string Id;
        public Vector2I Coordinate;
        public int Slot;
        public Vector2[] Candidates;
        public EntityDefinition Definition;
        public Vector2 Position, Home;
        public int Vitality;
        public bool Chosen, Dead;
        public Entity Actor;
        public EntitySaveData State;
    }

    private readonly Dictionary<Vector3I, SpawnRecord> _records = new();
    private readonly List<SpawnRecord> _active = new();
    private readonly List<Player> _players = new();
    private readonly List<Vector2I> _scanOffsets = new();
    private readonly RandomNumberGenerator _rng = new();
    private readonly CircleShape2D _spawnShape = new() { Radius = 12f };
    private readonly PhysicsShapeQueryParameters2D _spawnQuery = new();

    private string _identityOwner, _originLayer;
    private EntityDeaths _deaths;
    private readonly Dictionary<(string Layer, Vector2I Cell), List<SpawnRecord>> _remembered = new();
    private readonly Dictionary<(string Layer, Vector2I Cell), int> _rememberCursors = new();
    private int _rememberCursor;
    private const float RememberCellSize = 1024f;
    private ChunkController _chunks;
    private WorldGenerator _generator;
    private TerrainElevation _elevation;
    private Node2D _objects, _ground;
    private double _timer;
    private int _cursor, _checksRemaining, _created;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve world services and prepare a near-to-far chunk scan.
    public override void _Ready()
    {
        _identityOwner = WorldConfig.Find(this).GetParent().GetPathTo(this).ToString();
        _originLayer = WorldLayerMember.For(this);
        _deaths = EntityDeaths.Find(this);
        _chunks = GetNode<ChunkController>("../ChunkController");
        _generator = GetNode<WorldGenerator>("../WorldGenerator");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _elevation = GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;

        _spawnQuery.Shape = _spawnShape;
        _spawnQuery.CollisionMask = 15u;
        _spawnQuery.CollideWithAreas = false;
        _spawnQuery.Margin = 0f;

        int radius = System.Math.Clamp(ScanRadiusChunks, 1, 8);
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            _scanOffsets.Add(new Vector2I(x, y));

        _scanOffsets.Sort((a, b) =>
        {
            int order = a.LengthSquared().CompareTo(b.LengthSquared());
            if (order != 0) return order;
            order = a.Y.CompareTo(b.Y);
            return order != 0 ? order : a.X.CompareTo(b.X);
        });

        if (EnemyScene == null) GD.PushError("EnemyPopulation requires EnemyScene.");
        RestoreRecords();
    }

    // =========================================================
    // Dispose reusable placement queries when the world closes.
    public override void _ExitTree()
    {
        _spawnQuery.Dispose();
        _spawnShape.Dispose();
        _rng.Dispose();
    }

    // =========================================================
    // Budget population work independently from terrain and navigation work.
    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled || EnemyScene == null || !_chunks.WorldReady) return;
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = System.Math.Max(0.1, UpdateInterval);
        _checksRemaining = System.Math.Max(1, PhysicsChecksPerUpdate);
        _created = 0;

        _players.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Player player &&
                player.GetNodeOrNull<Health>("Systems/Health")?.IsAlive == true)
                _players.Add(player);
        if (_players.Count == 0) return;

        MaintainActors();
        foreach (Player player in _players) ScanRemembered(player);

        int work = System.Math.Clamp(ChunksPerUpdate, 1, 16);
        for (int i = 0; i < work; i++)
        {
            Player player = _players[_cursor % _players.Count];
            if (WorldLayerMember.For(player) != _originLayer) continue;
            Vector2 tile = IsoGrid.WorldToTile(
                _ground.ToLocal(player.GlobalPosition), _chunks.TileSize);
            Vector2I centre = new(
                Mathf.FloorToInt(tile.X / _chunks.ChunkSize),
                Mathf.FloorToInt(tile.Y / _chunks.ChunkSize));
            Vector2I coordinate = centre + _scanOffsets[_cursor];
            _cursor = (_cursor + 1) % _scanOffsets.Count;
            DiscoverChunk(coordinate);
        }
    }
    #endregion

    #region Persistence And Remembered Locations
    // =========================================================
    // Hydrate chosen records without instantiating distant or inactive-layer actors.
    private void RestoreRecords()
    {
        EntitySaves saves = EntitySaves.Find(this); if (saves == null) return;
        foreach (EntitySaveData state in saves.SavedEntities)
        {
            if (state.Owner != _identityOwner) continue;
            if (state.OriginLayer != _originLayer || state.Scene != EnemyScene?.ResourcePath)
                throw new InvalidDataException("Population origin or prefab changed: " + state.Id);
            EntityDefinition definition = GD.Load<EntityDefinition>(state.Definition);
            if (definition == null || state.Health > definition.MaxHealth)
                throw new InvalidDataException("Invalid saved population definition or health.");
            SpawnRecord record = new()
            {
                Id = state.Id, Coordinate = new(state.ChunkX, state.ChunkY), Slot = state.Slot,
                Candidates = Array.Empty<Vector2>(), Definition = definition, Position = new(state.X, state.Y),
                Home = new(state.HomeX, state.HomeY), Vitality = state.Health, Chosen = true, State = state
            };
            _records.Add(new(state.ChunkX, state.ChunkY, state.Slot), record); IndexRecord(record);
        }
    }

    // =========================================================
    // Index by actual saved location so migrated actors need not revisit their origin chunk.
    private void IndexRecord(SpawnRecord record)
    {
        var key = (record.State.Layer, RememberCell(record.Position));
        if (!_remembered.TryGetValue(key, out List<SpawnRecord> entries)) _remembered[key] = entries = new();
        if (!entries.Contains(record)) entries.Add(record);
    }

    // =========================================================
    // Move the remembered location only when a live actor is captured or retired.
    private void RememberActor(SpawnRecord record)
    {
        var previous = (record.State.Layer, RememberCell(record.Position));
        if (_remembered.TryGetValue(previous, out List<SpawnRecord> entries))
        { entries.Remove(record); if (entries.Count == 0) { _remembered.Remove(previous); _rememberCursors.Remove(previous); } }
        record.State = record.Actor.CaptureSave(record.State); record.Position = new(record.State.X, record.State.Y);
        record.Home = new(record.State.HomeX, record.State.HomeY); record.Vitality = record.State.Health; IndexRecord(record);
    }

    // =========================================================
    // Check a bounded set of nearby cells and candidates, keeping the existing one-create budget.
    private void ScanRemembered(Player player)
    {
        if (_created != 0 || _active.Count >= Math.Max(1, MaximumLiveEnemies)) return;
        string layer = WorldLayerMember.For(player); Vector2I centre = RememberCell(player.GlobalPosition);
        int work = Math.Clamp(ChunksPerUpdate, 1, 16);
        for (int cell = 0; cell < work; cell++)
        {
            var key = (layer, centre + _scanOffsets[_rememberCursor++ % _scanOffsets.Count]);
            if (!_remembered.TryGetValue(key, out List<SpawnRecord> entries) || entries.Count == 0) continue;
            _rememberCursors.TryGetValue(key, out int cursor);
            int candidates = Math.Min(entries.Count, Math.Max(1, PhysicsChecksPerUpdate));
            for (int i = 0; i < candidates; i++)
            {
                SpawnRecord record = entries[cursor++ % entries.Count];
                if (!record.Dead && record.Actor == null && record.State.HasRuntimeState &&
                    record.Position.DistanceSquaredTo(player.GlobalPosition) <= RetireDistance * RetireDistance)
                    PrepareActor(record);
                if (_created != 0) break;
            }
            _rememberCursors[key] = cursor % entries.Count;
            if (_created != 0) return;
        }
    }

    // =========================================================
    // Saved actors may restore on screen, but require available terrain and clear collision.
    private bool CanRestore(SpawnRecord record)
    {
        WorldLayerRuntime layers = WorldLayerRuntime.Find(this); string layer = record.State.Layer;
        if (layers == null || layers.ActiveLayer != layer || !layers.IsAvailable(layer, record.Position, 12) ||
            _checksRemaining <= 0 || layer == WorldLayerId.Surface && WorldPlacement.IsBasinReserved(
                this, record.Position, new(24, 24), Vector2.Zero)) return false;
        _checksRemaining--; _spawnQuery.Transform = new(0, record.Position); _spawnQuery.Motion = Vector2.Zero;
        // An already prepared actor owns its own collider; activation need not query it again.
        return GodotObject.IsInstanceValid(record.Actor) || _objects.GetWorld2D().DirectSpaceState.IntersectShape(_spawnQuery, 1).Count == 0;
    }

    // =========================================================
    // Retire passive actors only when no same-layer player remains nearby.
    private bool NearLayerPlayers(Vector2 point, string layer, float distance)
    {
        foreach (Player player in _players)
            if (WorldLayerMember.For(player) == layer && point.DistanceSquaredTo(player.GlobalPosition) <= distance * distance) return true;
        return false;
    }

    // =========================================================
    // Snapshot every chosen living record, including actors retired or transferred between layers.
    public IEnumerable<EntitySaveData> CaptureEntities()
    {
        foreach (SpawnRecord record in _records.Values)
        {
            if (!record.Chosen || record.Dead || _deaths?.WasKilled(record.Id) == true) continue;
            if (GodotObject.IsInstanceValid(record.Actor) && !record.Actor.IsQueuedForDeletion() && record.Actor.Health?.IsAlive == true)
                RememberActor(record);
            yield return record.State;
        }
    }

    // =========================================================
    // Use cheap logical world cells rather than retaining generated terrain nodes.
    private static Vector2I RememberCell(Vector2 point) => new(
        Mathf.FloorToInt(point.X / RememberCellSize), Mathf.FloorToInt(point.Y / RememberCellSize));
    #endregion

    #region Stage 1 - Discover Seeded Records
    // =========================================================
    // Discover world-anchored candidates using the chunk's biome population recipe.
    private void DiscoverChunk(Vector2I coordinate)
    {
        Vector2 firstTile = new(
            coordinate.X * _chunks.ChunkSize, coordinate.Y * _chunks.ChunkSize);
        Vector2 centreTile = firstTile + Vector2.One * (_chunks.ChunkSize * 0.5f);
        BiomeEnemies population = _generator.GetBiome(centreTile).Enemies;
        if (population == null) return;

        _rng.Seed = SeedFor(coordinate, 0);
        int count = population.GetCount(_rng);

        for (int slot = 0; slot < count; slot++)
        {
            Vector3I key = new(coordinate.X, coordinate.Y, slot);
            if (!_records.TryGetValue(key, out SpawnRecord record))
            {
                int attempts = System.Math.Clamp(PlacementAttempts, 1, 8);
                Vector2[] candidates = new Vector2[attempts];
                _rng.Seed = SeedFor(coordinate, slot + 1);

                for (int i = 0; i < attempts; i++)
                {
                    Vector2 localTile = firstTile + new Vector2(
                        _rng.RandfRange(0.5f, _chunks.ChunkSize - 0.5f),
                        _rng.RandfRange(0.5f, _chunks.ChunkSize - 0.5f));
                    candidates[i] = _ground.ToGlobal(
                        IsoGrid.TileToWorld(localTile, _chunks.TileSize));
                }

                record = new SpawnRecord
                {
                    Id = EntityDeaths.PopulationId(_identityOwner, _originLayer,
                        _chunks.WorldSeed, coordinate, slot),
                    Coordinate = coordinate, Slot = slot, Candidates = candidates
                };
                record.Dead = _deaths?.WasKilled(record.Id) == true;
                _records.Add(key, record);
            }

            if (!record.Dead && record.Actor == null &&
                _active.Count < System.Math.Max(1, MaximumLiveEnemies) && _created == 0)
                PrepareActor(record);
        }
    }

    // =========================================================
    // Produce stable seeds without relying on randomized string hash codes.
    private ulong SeedFor(Vector2I coordinate, int salt)
    {
        unchecked
        {
            ulong seed = (uint)_chunks.WorldSeed;
            seed ^= (ulong)(uint)coordinate.X * 0x9E3779B185EBCA87UL;
            seed ^= (ulong)(uint)coordinate.Y * 0xC2B2AE3D27D4EB4FUL;
            seed ^= (ulong)(uint)salt * 0x165667B19E3779F9UL;
            return seed;
        }
    }
    #endregion

    #region Stage 2 - Prepare Hidden Actors
    // =========================================================
    // Choose a valid candidate and instantiate at most one hidden actor per update.
    private void PrepareActor(SpawnRecord record)
    {
        // Also protect existing retired records if their identity died elsewhere.
        if (record.Dead || _deaths?.WasKilled(record.Id) == true)
        {
            record.Dead = true;
            return;
        }
        if (!record.Chosen)
        {
            for (int i = 0; i < record.Candidates.Length; i++)
            {
                Vector2 point = record.Candidates[i];
                Vector2 tile = IsoGrid.WorldToTile(_ground.ToLocal(point), _chunks.TileSize);
                BiomeEnemies population = _generator.GetBiome(tile).Enemies;
                if (population == null) continue;

                _rng.Seed = SeedFor(record.Coordinate, 100 + record.Slot * 16 + i);
                EntityDefinition definition = population.Pick(_rng);
                if (definition == null || !CanActivate(definition, point)) continue;

                record.Definition = definition;
                record.Position = record.Home = point;
                record.Vitality = definition.MaxHealth;
                record.Chosen = true;
                record.State = new EntitySaveData
                {
                    Id = record.Id, Owner = _identityOwner, OriginLayer = _originLayer,
                    ChunkX = record.Coordinate.X, ChunkY = record.Coordinate.Y, Slot = record.Slot,
                    Scene = EnemyScene.ResourcePath, Definition = definition.ResourcePath, Layer = _originLayer,
                    X = point.X, Y = point.Y, HomeX = point.X, HomeY = point.Y, Health = definition.MaxHealth,
                    RandomSeed = SeedFor(record.Coordinate, record.Slot + 1)
                };
                IndexRecord(record);
                break;
            }
        }

        if (!record.Chosen) return;
        bool restoring = record.State?.HasRuntimeState == true;
        if (restoring ? !CanRestore(record) : !CanActivate(record.Definition, record.Position)) return;

        Entity actor = EnemyScene.Instantiate<Entity>();
        EntitySaves.Prepare(actor, record.State);
        actor.SpawnPending = true;
        actor.SpawnHome = record.Home;
        actor.RandomSeed = record.State.RandomSeed;
        actor.Position = _objects.ToLocal(record.Position);
        actor.Visible = false;
        actor.Died += () => record.Dead = true;

        record.Actor = actor;
        _objects.AddChild(actor);
        // Health and saved behaviour are restored by Entity before its artwork await.
        _active.Add(record);
        _created++;
    }
    #endregion

    #region Stage 3 - Activate Off Screen
// =========================================================
// Reject basin spawns before applying visibility and physics placement checks.
private bool CanActivate(EntityDefinition definition, Vector2 point)
{
    if (WorldPlacement.IsBasinReserved(
            this, point, new Vector2(24f, 24f), Vector2.Zero) ||
        !_chunks.IsNavigationPointAvailable(point, 12f) ||
        NearPlayers(point, MinimumPlayerDistance) ||
        IsOnScreen(definition, point) ||
        _checksRemaining <= 0)
        return false;

    _checksRemaining--;
    _spawnQuery.Transform = new Transform2D(0f, point);
    _spawnQuery.Motion = Vector2.Zero;

    return _objects.GetWorld2D().DirectSpaceState
        .IntersectShape(_spawnQuery, 1).Count == 0;
}

    // =========================================================
    // Reject artwork overlapping the actual camera view plus a conservative margin.
    private bool IsOnScreen(EntityDefinition definition, Vector2 point)
    {
        Viewport viewport = GetViewport();
        if (viewport.GetCamera2D() == null) return true;

        float height = _elevation?.SampleWorldHeight(point) ?? 0f;
        Vector2 origin = point + Vector2.Up * height;
        Rect2 bounds = new(
            definition.SpawnVisualBounds.Position * definition.VisualScale,
            definition.SpawnVisualBounds.Size * definition.VisualScale);
        Transform2D canvas = _objects.GetCanvasTransform();

        Rect2 screen = new(canvas * (origin + bounds.Position), Vector2.Zero);
        screen = screen.Expand(canvas * (origin + new Vector2(bounds.End.X, bounds.Position.Y)));
        screen = screen.Expand(canvas * (origin + bounds.End));
        screen = screen.Expand(canvas * (origin + new Vector2(bounds.Position.X, bounds.End.Y)));

        return screen.Intersects(viewport.GetVisibleRect().Grow(
            Mathf.Max(32f, ScreenMarginPixels)));
    }

    // =========================================================
    // Test safety distances against every living player in this world.
    private bool NearPlayers(Vector2 point, float distance)
    {
        float squared = distance * distance;
        foreach (Player player in _players)
            if (point.DistanceSquaredTo(player.GlobalPosition) <= squared) return true;
        return false;
    }
    #endregion

    #region Stage 4 - Preserve And Retire
// =========================================================
// Maintain surface population ownership without retiring transferred cave actors.
private void MaintainActors()
{
    for (int i = _active.Count - 1; i >= 0; i--)
    {
        SpawnRecord record = _active[i];
        Entity actor = record.Actor;

        if (!GodotObject.IsInstanceValid(actor) || actor.IsQueuedForDeletion())
        {
            record.Actor = null;
            _active.RemoveAt(i);
            continue;
        }

        string layer = WorldLayerMember.For(actor);
        Vector2 point = actor.GlobalPosition;
        bool visible = layer == (WorldLayerRuntime.Find(this)?.ActiveLayer ?? WorldLayerId.Surface) &&
            IsOnScreen(record.Definition, point);
        bool retire = !actor.HasTarget && !visible &&
            (!NearLayerPlayers(point, layer, RetireDistance) ||
             !(WorldLayerRuntime.Find(this)?.IsAvailable(layer, point) ?? false));

        if (retire)
        {
            RememberActor(record);
            actor.RetireForStreaming();
            record.Actor = null;
            _active.RemoveAt(i);
            continue;
        }

        if (!actor.IsActivated && actor.Initialized &&
            (record.State?.HasRuntimeState == true ? CanRestore(record) : CanActivate(record.Definition, point)))
            actor.Activate();
    }
}
    #endregion
}
'@
$Changes['ENTITIES/Groups/EntityGroup.cs'] = @'
// Owns generic group membership, formation and shared threat alerts.
// Herds and squads choose their own minimum member count and optional roaming.
using Godot;
using System.Collections.Generic;

public partial class EntityGroup : Node2D
{
    #region Configuration
    [Export] public string GroupId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Group";
    [Export] public bool ShareThreats { get; set; } = true;
    [Export] public int MinimumMembers { get; set; } = 2;
    #endregion

    #region State
    public string PersistentId { get; set; } = "";
    public GroupRoaming Roaming { get; private set; }
    public bool FormationComplete { get; private set; }
    public int MemberCount => _members.Count + _retired.Count;

    private readonly List<EntityGroupMember> _members = new();
    private readonly HashSet<string> _retired = new();
    private bool _closing;
    #endregion

    #region Persistence
    // =========================================================
    // Reserve one living member while its actor is retired by streaming.
    public void Suspend(EntityGroupMember member)
    {
        if (!_members.Remove(member) || _closing) return;
        if (member.Actor is Entity actor && !string.IsNullOrEmpty(actor.PersistentId)) _retired.Add(actor.PersistentId);
    }

    // =========================================================
    // Capture group configuration, logical membership and optional roaming state.
    public EntityGroupSaveData CaptureSave()
    {
        EntityGroupSaveData state = new()
        {
            Id = PersistentId, GroupId = GroupId, Layer = WorldLayerMember.For(this), DisplayName = DisplayName,
            ShareThreats = ShareThreats, MinimumMembers = System.Math.Max(1, MinimumMembers), FormationComplete = FormationComplete,
            X = GlobalPosition.X, Y = GlobalPosition.Y, Members = new(_retired)
        };
        foreach (EntityGroupMember member in _members)
            if (member.Actor is Entity actor && !string.IsNullOrEmpty(actor.PersistentId)) state.Members.Add(actor.PersistentId);
        Roaming?.CaptureSave(state);
        return state;
    }

    // =========================================================
    // Count dormant members before any live join can trigger minimum-member rules.
    public void RestoreSave(EntityGroupSaveData state)
    {
        DisplayName = state.DisplayName; ShareThreats = state.ShareThreats; MinimumMembers = state.MinimumMembers;
        FormationComplete = state.FormationComplete; _retired.Clear();
        foreach (string id in state.Members) _retired.Add(id);
        Roaming?.RestoreSave(state);
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Discover optional roaming without starting a group processing loop.
    public override void _Ready()
    {
        AddToGroup("entity_groups");
        WorldLayerMember.Attach(this, WorldLayerMember.For(this));
        Roaming = GetNodeOrNull<GroupRoaming>("Systems/Roaming");
        EntitySaves.Find(this)?.ApplyGroup(this);
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Detach surviving members when the group itself is removed.
    public override void _ExitTree()
    {
        _closing = true;
        for (int i = _members.Count - 1; i >= 0; i--)
            if (GodotObject.IsInstanceValid(_members[i]))
                _members[i].LeaveGroup();

        _members.Clear();
    }
    #endregion

    #region Membership
    // =========================================================
    // Resolve a group by ID within the member's world layer.
    public static EntityGroup Find(Node2D actor, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (Node node in actor.GetTree().GetNodesInGroup("entity_groups"))
            if (node is EntityGroup group && !group._closing &&
                !group.IsQueuedForDeletion() && group.GroupId == id &&
                WorldLayerMember.Same(actor, group) &&
                WorldConfig.TryFind(actor)?.GetParent() == WorldConfig.TryFind(group)?.GetParent())
                return group;

        return null;
    }

    // =========================================================
    // Accept one member without coupling membership to a species or AI class.
    public bool Join(EntityGroupMember member)
    {
        if (_closing || IsQueuedForDeletion()) return false;
        if (!_members.Contains(member)) _members.Add(member);
        if (member.Actor is Entity actor && !string.IsNullOrEmpty(actor.PersistentId))
        {
            _retired.Remove(actor.PersistentId);
            EntitySaves.Find(this)?.RegisterGroup(this);
        }
        return true;
    }

    // =========================================================
    // Finish staged spawning before applying minimum-member rules.
    public void CompleteFormation()
    {
        if (FormationComplete || _closing) return;

        FormationComplete = true;
        CheckMembership();

        if (!_closing)
            Roaming?.Start();
    }

    // =========================================================
    // Remove a member and apply the completed group's survival policy.
    public void Leave(EntityGroupMember member)
    {
        if (member.Actor is Entity actor) _retired.Remove(actor.PersistentId);
        if (!_members.Remove(member) || _closing || !FormationComplete)
            return;

        CheckMembership();
    }

    // =========================================================
    // Dissolve below the configured minimum and notify remaining members.
    private void CheckMembership()
    {
        if (MemberCount >= System.Math.Max(1, MinimumMembers))
            return;

        _closing = true;
        EntitySaves.Find(this)?.GroupDissolved(this);
        _retired.Clear();
        Roaming?.Stop();

        while (_members.Count > 0)
        {
            EntityGroupMember member = _members[^1];
            if (GodotObject.IsInstanceValid(member))
                member.LeaveGroup();
            else
                _members.RemoveAt(_members.Count - 1);
        }

        QueueFree();
    }
    #endregion

    #region Coordination
    // =========================================================
    // Check active threats only when a roaming step is due.
    public bool HasThreat()
    {
        foreach (EntityGroupMember member in _members)
            if (GodotObject.IsInstanceValid(member) &&
                member.IsAlive && member.ThreatActive?.Invoke() == true)
                return true;

        return false;
    }

    // =========================================================
    // Broadcast a threat without recipients rebroadcasting the alert.
    public void Alert(Node2D attacker, EntityGroupMember source)
    {
        if (!ShareThreats || _closing) return;

        foreach (EntityGroupMember member in _members)
            if (member != source && GodotObject.IsInstanceValid(member) &&
                member.IsAlive)
                member.ReceiveThreat(attacker);
    }

    // =========================================================
    // Let members reconsider movement when their shared area changes.
    public void NotifyAreaChanged()
    {
        foreach (EntityGroupMember member in _members)
            if (GodotObject.IsInstanceValid(member) && member.IsAlive)
                member.NotifyAreaChanged();
    }
    #endregion
}
'@
$Changes['ENTITIES/Groups/EntityGroupMember.cs'] = @'
// Connects a health-bearing actor to generic group membership and alerts.
// Movement and threat behaviour subscribe to events rather than living here.
using Godot;
using System;

public partial class EntityGroupMember : Node
{
    #region Configuration
    [Export] public string GroupId { get; set; } = "";
    #endregion

    #region State
    public Node2D Actor { get; private set; }
    public EntityGroup Group { get; private set; }
    public Func<bool> ThreatActive { get; set; }

    public event Action GroupChanged;
    public event Action AreaChanged;
    public event Action<Node2D> ThreatReceived;

    private Health _health;
    public bool IsAlive => GodotObject.IsInstanceValid(Actor) &&
        Actor.IsInsideTree() && !Actor.IsQueuedForDeletion() &&
        _health?.IsAlive == true;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared health before the actor begins its own ready callback.
    public override void _Ready()
    {
        Actor = GetParent().GetParent<Node2D>();
        _health = Actor.GetNode<Health>("Systems/Health");
        _health.Hit += OnHit;
        _health.Died += OnDeath;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Leave membership and release shared health subscriptions.
    public override void _ExitTree()
    {
        if (Actor is Entity entity && entity.StreamingRetirement) SuspendForStreaming();
        else LeaveGroup();
        if (GodotObject.IsInstanceValid(_health))
        {
            _health.Hit -= OnHit;
            _health.Died -= OnDeath;
        }
    }
    #endregion

    #region Membership
    // =========================================================
    // Join after the actor has bound its behaviour components and layer.
    public void JoinAssignedGroup()
    {
        EntityGroup next = Actor is Entity entity
            ? EntitySaves.Find(this)?.ResolveGroup(entity, GroupId) ?? EntityGroup.Find(Actor, GroupId)
            : EntityGroup.Find(Actor, GroupId);
        if (next == Group) return;

        LeaveGroup();
        if (next == null || !next.Join(this)) return;

        Group = next;
        GroupId = next.GroupId;
        GroupChanged?.Invoke();
    }

    // =========================================================
    // Clear membership before notifying movement and the previous group.
    public void LeaveGroup()
    {
        EntityGroup previous = Group;
        Group = null;

        if (previous == null) return;

        GroupId = "";
        GroupChanged?.Invoke();

        if (GodotObject.IsInstanceValid(previous))
            previous.Leave(this);
    }

    // =========================================================
    // Keep an unloaded living member in the group's logical membership.
    public void SuspendForStreaming()
    {
        EntityGroup previous = Group; Group = null;
        if (GodotObject.IsInstanceValid(previous)) previous.Suspend(this);
    }

    // =========================================================
    // Deliver an alert to the actor's chosen threat behaviour.
    public void ReceiveThreat(Node2D attacker)
    {
        ThreatReceived?.Invoke(attacker);
    }

    // =========================================================
    // Notify movement without specifying how that movement is implemented.
    public void NotifyAreaChanged()
    {
        AreaChanged?.Invoke();
    }
    #endregion

    #region Health Events
    // =========================================================
    // Deliver the local threat and optionally alert other group members.
    private void OnHit()
    {
        Node2D attacker = _health.LastDamageSource;
        ReceiveThreat(attacker);

        if (GodotObject.IsInstanceValid(Group) &&
            !Group.IsQueuedForDeletion())
            Group.Alert(attacker, this);
    }

    // =========================================================
    // Update membership immediately when this actor dies.
    private void OnDeath()
    {
        LeaveGroup();
    }
    #endregion
}
'@
$Changes['ENTITIES/Groups/GroupRoaming.cs'] = @'
// Supplies an optional shared wander area for a generic group.
// Fixed areas do no roaming work; periodic areas use one timer.
using Godot;

public enum GroupRoamingMode { Fixed, PeriodicSteps }

public partial class GroupRoaming : Node
{
    #region Configuration
    [Export] public GroupRoamingMode Mode { get; set; } =
        GroupRoamingMode.PeriodicSteps;
    [Export] public float WanderRadius { get; set; } = 300f;
    [Export] public float RoamRadius { get; set; } = 500f;
    [Export] public float StepDistance { get; set; } = 60f;
    [Export] public double IntervalSeconds { get; set; } = 60.0;
    #endregion

    #region State
    public Vector2 Home { get; private set; }
    public Vector2 Centre => _group.GlobalPosition;

    private EntityGroup _group;
    private Timer _timer;
    private readonly RandomNumberGenerator _rng = new();
    #endregion

    #region Persistence
    // =========================================================
    // Snapshot roaming configuration, original home, random state and remaining gameplay time.
    public void CaptureSave(EntityGroupSaveData state)
    {
        state.HasRoaming = true; state.Mode = (int)Mode; state.WanderRadius = WanderRadius; state.RoamRadius = RoamRadius;
        state.StepDistance = StepDistance; state.Interval = System.Math.Max(1, IntervalSeconds);
        state.HomeX = Home.X; state.HomeY = Home.Y; state.RandomState = _rng.State;
        state.RoamingRunning = GodotObject.IsInstanceValid(_timer) && !_timer.IsStopped();
        state.RemainingStep = state.RoamingRunning ? _timer.TimeLeft : 0;
    }

    // =========================================================
    // Restore after Ready has initialized the helper, without applying offline elapsed time.
    public void RestoreSave(EntityGroupSaveData state)
    {
        Mode = (GroupRoamingMode)state.Mode; WanderRadius = state.WanderRadius; RoamRadius = state.RoamRadius;
        StepDistance = state.StepDistance; IntervalSeconds = state.Interval; Home = new(state.HomeX, state.HomeY);
        _rng.State = state.RandomState;
        if (state.RoamingRunning && Mode != GroupRoamingMode.Fixed) { Start(); _timer.Start(System.Math.Max(0.001, state.RemainingStep)); }
        else Stop();
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Retain the original anchor without starting a processing loop.
    public override void _Ready()
    {
        _group = GetParent().GetParent<EntityGroup>();
        Home = _group.GlobalPosition;
        _rng.Randomize();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release roaming resources when the group ends.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_timer))
            _timer.Timeout -= Step;

        _rng.Dispose();
    }
    #endregion

    #region Roaming
    // =========================================================
    // Start periodic movement only after group formation is complete.
    public void Start()
    {
        if (Mode == GroupRoamingMode.Fixed) return;

        if (_timer == null)
        {
            _timer = new Timer
            {
                Name = "CentreStepTimer",
                ProcessCallback = Timer.TimerProcessCallback.Physics
            };
            AddChild(_timer);
            _timer.Timeout += Step;
        }

        _timer.Start(System.Math.Max(1.0, IntervalSeconds));
    }

    // =========================================================
    // Suspend future roaming steps without moving the current centre.
    public void Stop()
    {
        if (GodotObject.IsInstanceValid(_timer))
            _timer.Stop();
    }

    // =========================================================
    // Attempt one bounded centre step while the group is calm.
    private void Step()
    {
        _timer.WaitTime = System.Math.Max(1.0, IntervalSeconds);

        if (Mode == GroupRoamingMode.Fixed)
        {
            Stop();
            return;
        }

        if (_group.IsQueuedForDeletion() ||
            !_group.FormationComplete || _group.HasThreat() ||
            StepDistance <= 0f || RoamRadius <= 0f)
            return;

        WorldNavigation navigation = WorldNavigation.For(_group);
        if (navigation == null) return;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = Centre +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) * StepDistance;

            if (point.DistanceSquaredTo(Home) > RoamRadius * RoamRadius ||
                !navigation.CanTravelDirectly(Centre, point))
                continue;

            _group.GlobalPosition = point;
            _group.NotifyAreaChanged();
            return;
        }
    }
    #endregion
}
'@
$Changes['ENTITIES/Movement/EntityWandering.cs'] = @'
// Shares wandering, waiting and optional grazing between entity species.
// The actor owns the single motor tick; group roaming remains optional.
using Godot;
using System;

public partial class EntityWandering : Node
{
    #region State
    public Vector2 Home { get; private set; }

    private EntityBody _actor;
    private EntityGroupMember _membership;
    private EntityGrazing _grazing;
    private Func<bool> _hasThreat;
    private RandomNumberGenerator _rng;
    private bool _ownsRandom;
    private EntityWanderSettings _settings;
    private double _wait, _travelTime;

    private GroupRoaming SharedArea
    {
        get
        {
            EntityGroup group = _membership?.Group;
            return GodotObject.IsInstanceValid(group) &&
                !group.IsQueuedForDeletion() ? group.Roaming : null;
        }
    }

    public bool UsesSharedArea => GodotObject.IsInstanceValid(SharedArea);
    public Vector2 Centre => UsesSharedArea ? SharedArea.Centre : Home;
    public float Radius => UsesSharedArea
        ? SharedArea.WanderRadius : _settings.Radius;

    private bool HasThreat => _hasThreat?.Invoke() == true;
    #endregion

    #region Persistence
    // =========================================================
    // Preserve waiting and solo home; routes and grass reservations are replanned.
    public void CaptureSave(EntitySaveData state) { state.WanderWait = Math.Max(0, _wait); }

    // =========================================================
    // Apply after membership callbacks so they cannot overwrite the saved solo anchor.
    public void RestoreSave(EntitySaveData state)
    {
        Home = new(state.HomeX, state.HomeY); _wait = state.WanderWait; _travelTime = 0;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared movement with optional membership, grazing and threat policy.
    public void Bind(
        EntityBody actor, EntityWanderSettings settings, Vector2 home,
        EntityGroupMember membership = null,
        EntityGrazing grazing = null,
        Func<bool> hasThreat = null,
        RandomNumberGenerator random = null,
        bool initialPause = false)
    {
        _actor = actor;
        _settings = settings;
        Home = home;
        _membership = membership;
        _grazing = grazing;
        _hasThreat = hasThreat;
        _rng = random;
        _ownsRandom = random == null;

        if (_ownsRandom)
        {
            _rng = new RandomNumberGenerator();
            _rng.Randomize();
        }

        if (_membership != null)
        {
            _membership.GroupChanged += OnGroupChanged;
            _membership.AreaChanged += OnAreaChanged;
        }

        if (initialPause) Pause();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release optional subscriptions and owned random resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_membership))
        {
            _membership.GroupChanged -= OnGroupChanged;
            _membership.AreaChanged -= OnAreaChanged;
        }

        if (_ownsRandom) _rng?.Dispose();
    }

    // =========================================================
    // Move the solo home anchor when the actor changes world layers.
    public void SetHome(Vector2 position)
    {
        Home = position;
    }
    #endregion

    #region Movement
    // =========================================================
    // Advance peaceful activity without executing movement a second time.
    public void Tick(double delta)
    {
        _wait -= delta;

        if (_grazing?.Tick(delta) == true ||
            !_actor.Motor.HasGoal)
            return;

        if (HasThreat)
        {
            _travelTime = 0.0;
            return;
        }

        _travelTime += delta;

        if (_actor.Motor.Arrived)
        {
            _actor.Motor.Stop();
            _travelTime = 0.0;

            if (_grazing?.BeginEating() != true)
                Pause();
        }
        else if (_actor.Motor.IsStuck || _travelTime > 20.0)
        {
            Interrupt();
            _wait = 2.0;
        }
    }

    // =========================================================
    // Choose optional forage or a destination within the current wander area.
    public void Decide()
    {
        EntityMotor motor = _actor.Motor;

        if (!_settings.Enabled)
        {
            motor.Stop();
            return;
        }

        Vector2 centre = Centre;
        float radius = Radius;
        Vector2 position = _actor.GlobalPosition;

        if (_settings.HomeLeash > 0f &&
            position.DistanceSquaredTo(centre) >
                _settings.HomeLeash * _settings.HomeLeash)
        {
            _wait = 0.0;
            ReturnToCentre();
            return;
        }

        if (_grazing?.HasTarget == true)
        {
            if (_grazing.TargetInside(centre, radius)) return;
            Interrupt();
        }

        if (motor.HasGoal)
        {
            if (!motor.Arrived && !motor.IsStuck) return;
            motor.Stop();
            Pause();
        }

        if (_settings.ReturnBeforeWaiting &&
            position.DistanceSquaredTo(centre) > radius * radius)
        {
            ReturnToCentre();
            return;
        }

        if (_wait > 0.0) return;

        if (position.DistanceSquaredTo(centre) > radius * radius)
        {
            ReturnToCentre();
            return;
        }

        WorldNavigation navigation = _actor.Navigation;
        if (navigation == null) return;

        if (_grazing?.TryReserve(centre, radius) == true)
        {
            _travelTime = 0.0;
            motor.SetGoal(_grazing.TargetPosition, _settings.Speed, 18f);
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = centre +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
                Mathf.Sqrt(_rng.Randf()) * radius;

            Vector2 start = _settings.RequireDirectPath ? position : point;

            if (!navigation.CanTravelDirectly(start, point))
                continue;

            _travelTime = 0.0;
            motor.SetGoal(
                point, _settings.Speed, _settings.ArrivalDistance);
            return;
        }

        _wait = 2.0;
    }

    // =========================================================
    // Return to the solo home or optional shared roaming centre.
    public void ReturnToCentre()
    {
        _travelTime = 0.0;
        _actor.Motor.SetGoal(
            Centre, _settings.Speed, _settings.ReturnDistance);
    }

    // =========================================================
    // Pause between destinations or after an optional meal.
    public void Pause()
    {
        _wait = _rng.RandfRange(_settings.Wait.X, _settings.Wait.Y);
    }

    // =========================================================
    // Release peaceful activity while optionally preserving combat movement.
    public void Interrupt(bool preserveCombat = true)
    {
        _grazing?.Release();
        _travelTime = 0.0;
        _wait = 0.0;

        if (!preserveCombat || !HasThreat)
            _actor.Motor.Stop();
    }
    #endregion

    #region Group Events
    // =========================================================
    // Establish a solo anchor when a shared roaming area is lost.
    private void OnGroupChanged()
    {
        if (!UsesSharedArea)
            Home = _actor.GlobalPosition;

        Interrupt();
    }

    // =========================================================
    // Reconsider peaceful movement when an optional group anchor changes.
    private void OnAreaChanged()
    {
        if (HasThreat || _grazing?.TargetInside(Centre, Radius) == true)
            return;

        Interrupt();
    }
    #endregion
}
'@
$Changes['ENTITIES/Grazing/EntityGrazing.cs'] = @'
// Owns grass reservations, eating progress and feeding cooldown.
// The existing GrazingWorld remains the shared grass index and depletion service.
using Godot;

public partial class EntityGrazing : Node
{
    #region State
    private Entity _actor;
    private GrazingWorld _world;
    private Grass _target;
    private bool _eating;
    private double _elapsed, _cooldown;

    public bool HasTarget => GodotObject.IsInstanceValid(_target) &&
        !_target.IsQueuedForDeletion();
    public Vector2 TargetPosition => _target.GlobalPosition;
    #endregion

    #region Persistence
    // =========================================================
    // Keep feeding cooldown; an unfinished meal releases its transient reservation.
    public void CaptureSave(EntitySaveData state) { state.FeedingCooldown = System.Math.Max(0, _cooldown); }

    // =========================================================
    // Resume gameplay time without consuming feeding cooldown while offline.
    public void RestoreSave(EntitySaveData state) { Release(); _cooldown = state.FeedingCooldown; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind grazing once without adding another processing loop.
    public void Bind(Entity actor)
    {
        _actor = actor;
        _world = GrazingWorld.GetOrCreate(actor);
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Relinquish any grass reservation when the component exits.
    public override void _ExitTree()
    {
        Release();
    }
    #endregion

    #region Feeding
    // =========================================================
    // Advance feeding; return true while eating occupies this movement tick.
    public bool Tick(double delta)
    {
        _cooldown -= delta;

        if (_target != null && !HasTarget)
        {
            Release();
            _actor.Motor.Stop();
        }

        if (!_eating || !HasTarget) return false;

        _elapsed += delta;
        if (_elapsed >= _actor.Definition.GrazingDuration)
        {
            _world.Consume(_actor, _target);
            Release();
            _cooldown = _actor.Definition.FeedingCooldown;
            _actor.Wandering.Pause();
        }

        return true;
    }

    // =========================================================
    // Reserve reachable grass within the current wander area.
    public bool TryReserve(Vector2 centre, float radius)
    {
        if (!_actor.Definition.GrazingEnabled || _cooldown > 0.0)
            return false;

        _target = _world.Reserve(_actor, centre, radius);
        return HasTarget;
    }

    // =========================================================
    // Start the eating timer once movement reaches the reserved tuft.
    public bool BeginEating()
    {
        if (!HasTarget) return false;
        _eating = true;
        _elapsed = 0.0;
        return true;
    }

    // =========================================================
    // Check whether reserved grass still belongs to a changed wander area.
    public bool TargetInside(Vector2 centre, float radius)
    {
        return HasTarget &&
            centre.DistanceSquaredTo(_target.GlobalPosition) <= radius * radius;
    }

    // =========================================================
    // Release reservations and eating state without resetting feeding cooldown.
    public void Release()
    {
        if (GodotObject.IsInstanceValid(_world))
            _world.Release(_actor, _target);

        _target = null;
        _eating = false;
        _elapsed = 0.0;
    }
    #endregion
}
'@
$Changes['SYSTEMS/Saving/CampaignSession.cs'] = @'
// Coordinates world/player restoration and the changed-resource save section.
using Godot;
using System;
using System.Diagnostics;
using System.IO;

public partial class CampaignSession : Node
{
    private CampaignData _data;
    private bool _restoring, _finished;
    private Node _world;
    private Player _player;
    private ChunkController _chunks;
    private WorldClock _clock;
    private ProcessModeEnum _objectsMode;
    private readonly Stopwatch _startup = new();

    // =========================================================
    // Instantiate a detached world and apply its recipe before Ready runs.
    public static void Launch(Node menu, bool continueCampaign)
    {
        PlayerProfile profile = ProfileStore.Selected
            ?? throw new InvalidOperationException("Select a profile first.");
        CampaignData data = continueCampaign ? CampaignStore.Load(profile.Id) : new CampaignData
        {
            ProfileId = profile.Id, CampaignId = Guid.NewGuid().ToString("N")
        };
        if (continueCampaign)
        {
            CampaignRecipe.Validate(data);
            if (data.Player.Layer != WorldLayerId.Surface ||
                !float.IsFinite(data.Player.X) || !float.IsFinite(data.Player.Y) ||
                !float.IsFinite(data.SpawnX) || !float.IsFinite(data.SpawnY))
                throw new InvalidDataException("This pass restores surface campaigns only.");
        }
        PackedScene scene = GD.Load<PackedScene>(MenuNavigation.CampaignScene)
            ?? throw new IOException("Campaign scene is unavailable.");
        Node world = scene.Instantiate();
        try
        {
            Player player = world.GetNode<Player>("WorldObjects/Player");
            ChunkController chunks = world.GetNode<ChunkController>("Systems/ChunkController");
            if (continueCampaign)
            {
                CampaignRecipe.Apply(world, data);
                // Begin loading at the actual save point, not the original landing site.
                player.Position = new Vector2(data.Player.X, data.Player.Y);
            }
            else
            {
                // A campaign uses a fresh seed rather than the scene's fixed test seed.
                using RandomNumberGenerator random = new();
                random.Randomize();
                chunks.WorldSeed = random.Randi();
                data.Seed = chunks.WorldSeed;
                data.SpawnX = player.Position.X; data.SpawnY = player.Position.Y;
                CampaignRecipe.Capture(world, data);
            }
            EntityDeaths deaths = new() { Name = "EntityDeaths" };
            deaths.Initialize(data, world);
            world.AddChild(deaths);
            EntitySaves entities = new() { Name = "EntitySaves" };
            entities.Initialize(data, world);
            world.AddChild(entities);
            ResourceChanges resources = new() { Name = "ResourceChanges" };
            resources.Initialize(data);
            world.AddChild(resources);
            WorldObjectSaves objects = new() { Name = "WorldObjectSaves" };
            objects.Initialize(data, world);
            world.AddChild(objects);
            world.AddChild(new CampaignSession
                { Name = "CampaignSession", _data = data, _restoring = continueCampaign });
        }
        catch { world.Free(); throw; }
        SceneTree tree = menu.GetTree();
        // Retire the old scene before activating the detached campaign.
        Callable.From(() =>
        {
            Node previous = tree.CurrentScene;
            if (previous != null) { tree.Root.RemoveChild(previous); previous.QueueFree(); }
            tree.Paused = false;
            tree.Root.AddChild(world);
            tree.CurrentScene = world;
        }).CallDeferred();
    }

    // =========================================================
    // Locate this world's campaign without keeping static references to old scenes.
    private static CampaignSession FindCampaign(Node context)
    {
        for (Node node = context; node != null; node = node.GetParent())
        {
            CampaignSession campaign = node.GetNodeOrNull<CampaignSession>("CampaignSession");
            if (campaign != null) return campaign;
        }
        return null;
    }

    // =========================================================
    // Keep respawning and generation clearance anchored to the original landing site.
    public static Vector2? OriginalSpawnFor(Node context)
    {
        if (context is not Player player) return null;
        CampaignSession campaign = FindCampaign(player);
        if (campaign?._data == null) return null;
        Node2D objects = player.GetParent<Node2D>();
        return objects.ToGlobal(new Vector2(campaign._data.SpawnX, campaign._data.SpawnY));
    }

    // =========================================================
    // Show exactly which coordinates were committed by the manual save action.
    public static string SavedPositionFor(Node context)
    {
        CampaignSession campaign = FindCampaign(context);
        if (campaign?._data?.Player == null) return "";
        PlayerSaveData player = campaign._data.Player;
        return $"Saved position: X {player.X:0.##}, Y {player.Y:0.##} ({player.Layer}).";
    }

    // =========================================================
    // Let terrain startup run while player actions and item collection remain frozen.
    public override void _Ready()
    {
        _world = GetParent();
        _player = _world.GetNode<Player>("WorldObjects/Player");
        _chunks = _world.GetNode<ChunkController>("Systems/ChunkController");
        _objectsMode = ProcessModeEnum.Inherit;
        ProcessPriority = 1000;
        _startup.Start();
        try { WorldObjectSaves.Find(this).RestoreBuildings(_world); }
        catch (Exception error)
        {
            _world.GetNode("WorldObjects").ProcessMode = ProcessModeEnum.Disabled;
            FailLoad(error);
        }
    }

    // =========================================================
    // Wait for terrain, player artwork and the deferred world clock, then restore once.
    public override void _Process(double delta)
    {
        if (_finished) return;
        Node objects = _world.GetNode("WorldObjects");
        objects.ProcessMode = ProcessModeEnum.Disabled;
        try
        {
            _clock ??= WorldEclipse.Find(this)?.GetNodeOrNull<WorldClock>("WorldClock");
            if (!_chunks.WorldReady || _player.Controls == null || !_player.IsPhysicsProcessing() || _clock == null)
            {
                if (_startup.Elapsed.TotalSeconds > 120)
                    throw new IOException("Campaign initialization did not finish; check Godot's errors.");
                return;
            }
            if (_restoring)
            {
                Vector2 position = new(_data.Player.X, _data.Player.Y);
                _player.GlobalPosition = position;
                if (!_chunks.PrepareDestination(position))
                {
                    if (_startup.Elapsed.TotalSeconds > 120)
                        throw new IOException("Saved destination could not be prepared.");
                    return;
                }
                if (!_chunks.IsNavigationPointAvailable(position, 12f))
                    throw new InvalidDataException("Saved position is no longer on available surface terrain.");
                RestorePlayer();
                _player.GlobalPosition = position;
                _player.Velocity = Vector2.Zero;
                _clock.RestoreSave(_data.WorldSeconds);
            }
            else _clock.RestoreSave(0);
            WorldObjectSaves.Find(this).RestoreDrops(this);
            Camera2D camera = _player.GetNode<Camera2D>("Camera2D");
            camera.ResetSmoothing();
            camera.ForceUpdateScroll();
            if (_restoring)
                GD.Print($"[CampaignLoad] Profile {_data.ProfileId}: restored " +
                    $"{_player.GlobalPosition}, saved ({_data.Player.X}, {_data.Player.Y}).");
            PauseMenu.Attach(_player, _player.Controls).BindSave(Save);
            objects.ProcessMode = _objectsMode;
            _finished = true; SetProcess(false);
            if (_restoring && !string.IsNullOrEmpty(CampaignStore.RecoveryMessage))
                GD.Print(CampaignStore.RecoveryMessage);
        }
        catch (Exception error) { FailLoad(error); }
    }

    // =========================================================
    // Restore capacities first, physical items second, shortcuts and vitals last.
    private void RestorePlayer()
    {
        PlayerSaveData saved = _data.Player;
        _player.GetNode<PlayerStats>("Systems/Stats").RestoreSave(saved);
        ItemCatalog items = ResourceWorld.Find(this).Catalog;
        _player.GetNode<PlayerInventory>("Systems/Inventory").RestoreSave(saved, items);
        _player.GetNode<PlayerHotbar>("Systems/Hotbar").RestoreSave(saved);
        _player.GetNode<PlayerVitals>("Systems/Vitals").RestoreSave(saved);
        _player.GetNode<PlayerCrafting>("Systems/Crafting").RestoreSave(saved);
        _player.GetNode<PlayerSurvival>("Systems/Survival").RestoreSave(saved);
    }

    // =========================================================
    // Capture supported sections only while paused, on solid surface ground.
    private void Save()
    {
        if (!_finished || !GetTree().Paused || ProfileStore.Selected?.Id != _data.ProfileId)
            throw new InvalidOperationException("No paused campaign belongs to the selected profile.");
        if (WorldLayerMember.For(_player) != WorldLayerId.Surface)
            throw new InvalidOperationException("Underground saving comes with the layer-restoration pass.");
        if (_player.IsAirborne || !_player.GetNode<Health>("Systems/Health").IsAlive)
            throw new InvalidOperationException("Save while alive and standing on the ground.");
        PlayerSaveData saved = new()
        {
            X = _player.GlobalPosition.X, Y = _player.GlobalPosition.Y,
            Layer = WorldLayerId.Surface,
            Health = _player.GetNode<Health>("Systems/Health").Current
        };
        _player.GetNode<PlayerStats>("Systems/Stats").CaptureSave(saved);
        _player.GetNode<PlayerVitals>("Systems/Vitals").CaptureSave(saved);
        _player.GetNode<PlayerInventory>("Systems/Inventory").CaptureSave(saved);
        _player.GetNode<PlayerHotbar>("Systems/Hotbar").CaptureSave(saved);
        _player.GetNode<PlayerCrafting>("Systems/Crafting").CaptureSave(saved);
        _player.GetNode<PlayerSurvival>("Systems/Survival").CaptureSave(saved);
        _data.Player = saved;
        _data.WorldSeconds = _clock.ElapsedSeconds;
        _data.SavedUtc = DateTime.UtcNow;
        ResourceChanges.Find(this).Capture(_data);
        WorldObjectSaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureResourceDefinitions(_data);
        EntityDeaths.Find(this).Capture(_data);
        EntitySaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureObjectDefinitions(_data);
        CampaignRecipe.CaptureEntityDefinitions(_data);
        CampaignStore.Write(_data);
        GD.Print($"[CampaignSave] Profile {_data.ProfileId}: " +
            $"({saved.X}, {saved.Y}), original spawn ({_data.SpawnX}, {_data.SpawnY}).");
    }

    // =========================================================
    // Keep failed restoration frozen and show an exit route without saving over it.
    private void FailLoad(Exception error)
    {
        _finished = true; SetProcess(false);
        GetTree().Paused = true;
        AcceptDialog dialog = new()
        {
            Title = "Campaign load failed", DialogText = error.Message +
                "\nYour existing save was preserved.",
            ProcessMode = ProcessModeEnum.Always
        };
        AddChild(dialog);
        dialog.GetOkButton().Text = "MAIN MENU";
        dialog.Confirmed += () => MenuNavigation.Open(this, MenuNavigation.MainScene);
        dialog.PopupCentered(new Vector2I(640, 240));
        GD.PushError(error.Message);
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignStore.cs'] = @'
// Keeps each profile's current campaign separate and replaces saves atomically.
using Godot;
using System;
using System.IO;
using System.Text.Json;

public static class CampaignStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string RecoveryMessage { get; private set; } = "";

    // =========================================================
    // Resolve only validated stable IDs beneath Godot's user directory.
    public static string PathFor(string profile)
    {
        if (!Guid.TryParseExact(profile, "N", out _))
            throw new InvalidDataException("Invalid profile identity.");
        return ProjectSettings.GlobalizePath($"user://Profiles/{profile}/campaign.json");
    }

    // =========================================================
    // Keep a damaged save visible to Continue so it can report its actual error.
    public static bool Exists(string profile)
    {
        string path = PathFor(profile);
        return File.Exists(path) || File.Exists(path + ".bak");
    }

    // =========================================================
    // Validate ownership and the supported format before exposing a save.
    private static CampaignData Read(string path, string profile)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Campaign file exceeds this pass's size limit.");
        CampaignData data = JsonSerializer.Deserialize<CampaignData>(File.ReadAllText(path));
        if (data == null ||
            !(data.Version == 1 && data.Coverage == "world-player-only" ||
              data.Version == 2 && data.Coverage == "world-player-resources" ||
              data.Version == 3 && data.Coverage == "world-player-resources-objects" ||
              data.Version == 4 && data.Coverage == "world-player-resources-objects-deaths" ||
              data.Version == 5 && data.Coverage == "world-player-resources-objects-entities") ||
            data.ProfileId != profile || !Guid.TryParseExact(data.CampaignId, "N", out _) ||
            data.Player == null || data.Settings == null || data.Resources == null ||
            data.Sections == null ||
            (data.Version == 1 ? data.Sections.Count != 0 :
                data.Version == 2 ? data.Sections.Count != 1 || !data.Sections.ContainsKey(ResourceChanges.Section) :
                data.Sections.Count != (data.Version == 3 ? 2 : data.Version == 4 ? 3 : 4) ||
                    !data.Sections.ContainsKey(ResourceChanges.Section) ||
                    !data.Sections.ContainsKey(WorldObjectSaves.Section) ||
                    (data.Version >= 4 && !data.Sections.ContainsKey(EntityDeaths.Section)) ||
                    (data.Version == 5 && !data.Sections.ContainsKey(EntitySaves.Section))))
            throw new InvalidDataException("Unsupported or invalid campaign save.");
        ResourceChanges.ReadSection(data);
        WorldObjectSaves.ReadSection(data);
        EntityDeaths.ReadSection(data);
        EntitySaves.ReadSection(data);
        return data;
    }

    // =========================================================
    // Recover a readable previous save without overwriting damaged files.
    public static CampaignData Load(string profile)
    {
        RecoveryMessage = "";
        string path = PathFor(profile);
        try { return Read(path, profile); }
        catch (Exception original)
        {
            try
            {
                CampaignData data = Read(path + ".bak", profile);
                RecoveryMessage = "Loaded the previous campaign backup.";
                return data;
            }
            catch
            {
                throw new IOException("Could not read the campaign or its backup. " +
                    "Existing files were preserved. " + original.Message, original);
            }
        }
    }

    // =========================================================
    // Flush all bytes before replacing the current save; preserve a valid backup.
    public static void Write(CampaignData data)
    {
        string path = PathFor(data.ProfileId), temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        using (FileStream stream = new(temporary, FileMode.Create,
            System.IO.FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        Read(temporary, data.ProfileId);
        if (File.Exists(path))
        {
            bool valid = false;
            try { Read(path, data.ProfileId); valid = true; } catch { }
            File.Replace(temporary, path, valid ? path + ".bak" : null);
        }
        else File.Move(temporary, path);
        RecoveryMessage = "";
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignRecipe.cs'] = @'
// Records exported world settings and detects incompatible generation resources.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

public static class CampaignRecipe
{
    private static readonly string[] Owners =
        { "CONFIG", "Systems/WorldGenerator", "Systems/ChunkController" };

    // =========================================================
    // Capture exported settings before Ready callbacks derive terrain and cave seeds.
    public static void Capture(Node world, CampaignData data)
    {
        data.Settings.Clear(); data.Resources.Clear();
        foreach (string owner in Owners)
        {
            Node node = world.GetNode(owner);
            Dictionary<string, string> values = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) == 0 ||
                    (usage & PropertyUsageFlags.Storage) == 0) continue;
                string name = property["name"].AsString();
                Variant value = node.Get(name);
                if (value.VariantType == Variant.Type.Object)
                {
                    Resource resource = value.AsGodotObject() as Resource;
                    if (resource == null) { values[name] = "null"; continue; }
                    if (string.IsNullOrEmpty(resource.ResourcePath) || resource.ResourcePath.Contains("::"))
                        throw new InvalidDataException($"Save needs a separate resource file for {owner}/{name}.");
                    values[name] = "@resource:" + resource.ResourcePath;
                    Stamp(resource.ResourcePath, data.Resources);
                }
                else values[name] = GD.VarToStr(value);
            }
            data.Settings[owner] = values;
        }
        // A null generator catalog uses the surface definition inside LayerCatalog.
        Stamp("res://WORLD/Layers/WorldLayers.tres", data.Resources);
    }

    // =========================================================
    // Include hard-coded ground catalogs and scene resource definitions in this save's recipe.
    public static void CaptureResourceDefinitions(CampaignData data)
    {
        Stamp("res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres", data.Resources);
        foreach (ResourceChangeData entry in ResourceChanges.ReadSection(data).Entries)
            Stamp(entry.Definition.Split("::")[0], data.Resources);
    }

    // =========================================================
    // Stamp object scenes, storage/loot definitions and external item references.
    public static void CaptureObjectDefinitions(CampaignData data)
    {
        foreach (string path in WorldObjectSaves.ReadSection(data).Recipes)
            Stamp(path, data.Resources);
    }

    // =========================================================
    // Include entity recipes so death identities cannot silently change species or prefab.
    public static void CaptureEntityDefinitions(CampaignData data)
    {
        foreach (EntitySaveData actor in EntitySaves.ReadSection(data).Entities)
        {
            Stamp(actor.Definition, data.Resources); Stamp(actor.Scene, data.Resources);
        }
        foreach (EntityDeathData entry in EntityDeaths.ReadSection(data).Entries)
        {
            Stamp(entry.Definition, data.Resources);
            Stamp(entry.Scene, data.Resources);
        }
    }

    // =========================================================
    // Hash referenced settings and their dependencies without storing engine objects.
    private static void Stamp(string path, Dictionary<string, string> stamps)
    {
        if (stamps.ContainsKey(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        if (!Godot.FileAccess.FileExists(path))
            throw new InvalidDataException($"Missing world resource: {path}");
        stamps[path] = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(path)));
        foreach (string dependency in ResourceLoader.GetDependencies(path))
        {
            string resolved = dependency.Split("::").Last();
            if (resolved.StartsWith("res://", StringComparison.Ordinal)) Stamp(resolved, stamps);
        }
    }

    // =========================================================
    // Refuse changed resource recipes rather than silently rebuilding a different map.
    public static void Validate(CampaignData data)
    {
        if (data.Settings.Count != Owners.Length || data.Resources.Count == 0 || data.Resources.Count > 4096)
            throw new InvalidDataException("Missing or invalid generation recipe.");
        foreach (var stamp in data.Resources)
        {
            if (!stamp.Key.StartsWith("res://", StringComparison.Ordinal) ||
                !Godot.FileAccess.FileExists(stamp.Key) ||
                Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(stamp.Key))) != stamp.Value)
                throw new InvalidDataException("World generation resources changed since this save. " +
                    "Start a new campaign or restore the original resources. " + stamp.Key);
        }
    }

    // =========================================================
    // Apply settings to a detached scene so initialization sees the saved recipe.
    public static void Apply(Node world, CampaignData data)
    {
        Validate(data);
        foreach (string owner in Owners)
        {
            if (!data.Settings.TryGetValue(owner, out var values) || values == null || values.Count > 256)
                throw new InvalidDataException("Missing saved world settings.");
            Node node = world.GetNode(owner);
            HashSet<string> exported = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) != 0 &&
                    (usage & PropertyUsageFlags.Storage) != 0)
                    exported.Add(property["name"].AsString());
            }
            foreach (var setting in values)
            {
                if (!exported.Contains(setting.Key) || setting.Value == null)
                    throw new InvalidDataException("Unsupported saved setting.");
                if (setting.Value.StartsWith("@resource:", StringComparison.Ordinal))
                {
                    string path = setting.Value.Substring(10);
                    if (!data.Resources.ContainsKey(path))
                        throw new InvalidDataException("Unvalidated resource reference.");
                    Resource resource = ResourceLoader.Load(path)
                        ?? throw new InvalidDataException("Saved resource is unavailable.");
                    node.Set(setting.Key, resource);
                }
                else
                {
                    Variant value = GD.StrToVar(setting.Value);
                    if (value.VariantType == Variant.Type.Object || value.VariantType == Variant.Type.Callable ||
                        value.VariantType == Variant.Type.Signal)
                        throw new InvalidDataException("Invalid saved world setting type.");
                    node.Set(setting.Key, value);
                }
            }
        }
        world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed = data.Seed;
    }
}
'@
$Changes['UI/Menus/Pause/PauseMenu.cs'] = @'
// Pauses gameplay while its own UI remains active; save support binds separately.
using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
    #region Configuration
    [ExportGroup("Layout")]
    [Export] public float PanelWidth { get; set; } = 440f;
    #endregion

    #region State
    private PlayerInput _controls;
    private InputModes _modes;
    private Control _screen;
    private Button _resume, _save;
    private AcceptDialog _message;
    private ConfirmationDialog _confirm;
    private Action _saveAction, _exitAction;
    private bool _open;
    private Input.MouseModeEnum _previousMouse;
    public bool IsOpen => _open;
    #endregion

    #region Setup
    // =========================================================
    // Attach once per player without editing every playable world scene.
    public static PauseMenu Attach(Player player, PlayerInput controls)
    {
        PauseMenu existing = player.GetNodeOrNull<PauseMenu>("PauseMenu");
        if (existing != null) return existing;

        PauseMenu menu = GD.Load<PackedScene>(
            "res://UI/Menus/Pause/PauseMenu.tscn")
            .Instantiate<PauseMenu>();

        menu.Name = "PauseMenu";
        menu._controls = controls;
        menu._modes = InputModes.For(player);
        player.AddChild(menu);
        return menu;
    }

    // =========================================================
    // Only this menu branch ignores the scene-tree pause state.
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 200;
        BuildUi();
        _screen.Hide();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Enable saving only when the real persistent saver is connected.
    public void BindSave(Action saveAction)
    {
        _saveAction = saveAction;
        if (_save == null) return;

        _save.Disabled = saveAction == null;
        _save.TooltipText = saveAction == null
            ? "Campaign saving is unavailable in this scene."
            : "Partial save: world/player, resources, items, containers and buildings.";
    }
    #endregion

    #region Input and Pause
    // =========================================================
    // Let existing interfaces consume ESC before opening pause.
    public override void _UnhandledInput(InputEvent input)
    {
        if (_controls == null || !PlayerInput.IsPauseRequest(input))
            return;

        if (!_open && (!_modes.GameplayAllowed ||
            GetTree().Paused || GetViewport().GuiIsDragging()))
            return;

        GetViewport().SetInputAsHandled();

        if (_open) ResumeGame();
        else Open();
    }

    // =========================================================
    // Stop player actions, claim input, then pause the scene tree.
    private void Open()
    {
        _open = true;
        _previousMouse = Input.MouseMode;
        _modes.Push(this, PlayerInputMode.Pause);
        _controls.Suspend();

        Input.MouseMode = Input.MouseModeEnum.Visible;
        _screen.Show();
        GetTree().Paused = true;
        _resume.GrabFocus();
    }

    // =========================================================
    // Release ownership and prevent held-input retriggers.
    private void ResumeGame()
    {
        if (!_open) return;

        _message.Hide();
        _confirm.Hide();
        _exitAction = null;
        _screen.Hide();
        _modes.Release(this);
        _controls.Resume();

        Input.MouseMode = _previousMouse;
        _open = false;
        GetTree().Paused = false;
    }

    // =========================================================
    // Never leave the tree paused if the owning player is removed.
    public override void _ExitTree()
    {
        if (!_open) return;

        if (GodotObject.IsInstanceValid(_modes))
            _modes.Release(this);

        GetTree().Paused = false;
        Input.MouseMode = _previousMouse;
    }
    #endregion

    #region Layout
    // =========================================================
    // Build a blocking overlay with the existing shared button styles.
    private void BuildUi()
    {
        _screen = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        AddChild(_screen);
        _screen.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        ColorRect dim = new()
        {
            Color = new Color(0.015f, 0.035f, 0.05f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        _screen.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        CenterContainer centre = new();
        _screen.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0)
        };

        panel.AddThemeStyleboxOverride(
            "panel", UIButtonFactory.Box(
                UIButtonFactory.Ink, UIButtonFactory.Accent));

        centre.AddChild(panel);

        VBoxContainer contents = new();
        contents.AddThemeConstantOverride("separation", 12);
        panel.AddChild(contents);
        contents.AddChild(UIButtonFactory.Label("PAUSED", 30));

        _resume = AddButton(contents, "Resume", ResumeGame);
        _save = AddButton(contents, "Save Game", SaveGame);

        AddButton(contents, "Options", () => ShowMessage(
            "Options", "Options will be added later."));

        AddButton(contents, "Exit to Main Menu",
            () => ConfirmExit(false));

        AddButton(contents, "Exit Game",
            () => ConfirmExit(true));

        AddButton(contents, "About", () => ShowMessage(
            "About", "A science-fiction survival world.\n" +
            "About content is a placeholder."));

        _message = new AcceptDialog { Exclusive = true };
        AddChild(_message);
        UIButtonFactory.Apply(_message.GetOkButton());

        _confirm = new ConfirmationDialog
        {
            Exclusive = true,
            Title = "Leave game?"
        };

        AddChild(_confirm);
        _confirm.GetOkButton().Text = "LEAVE";
        UIButtonFactory.Apply(_confirm.GetOkButton());
        UIButtonFactory.Apply(_confirm.GetCancelButton());

        _confirm.Confirmed += CompleteExit;
        _confirm.Canceled += () => _exitAction = null;
        BindSave(_saveAction);
    }

    // =========================================================
    // Share button construction, focus handling and styling.
    private static Button AddButton(
        VBoxContainer parent, string text, Action action)
    {
        Button button = UIButtonFactory.Create(text, action);
        parent.AddChild(button);
        return button;
    }
    #endregion

    #region Actions
    // =========================================================
    // Invoke only an explicitly connected persistent saver.
    private void SaveGame()
    {
        if (_saveAction == null) return;

        try
        {
            _saveAction();
            ShowMessage("Partial campaign saved", "World, player, resources, items, containers and buildings saved to your profile.\n" +
                CampaignSession.SavedPositionFor(this) + "\n" +
                "Surviving entities, deaths and groups are included. Underground player restoration is a later pass.");
        }
        catch (Exception error)
        {
            ShowMessage("Save failed", error.Message);
        }
    }

    // =========================================================
    // Ask before leaving unsaved gameplay.
    private void ConfirmExit(bool quit)
    {
        _exitAction = quit
            ? () => GetTree().Quit()
            : () => MenuNavigation.Open(
                this, ProfileStore.Selected == null
                    ? MenuNavigation.BootScene
                    : MenuNavigation.MainScene);

        _confirm.DialogText =
            "Leave this game? Any unsaved progress will be lost.";

        _confirm.PopupCentered(new Vector2I(460, 180));
    }

    // =========================================================
    // Keep gameplay paused if returning to a menu fails.
    private void CompleteExit()
    {
        Action action = _exitAction;
        _exitAction = null;

        try { action?.Invoke(); }
        catch (Exception error)
        {
            ShowMessage("Could not leave", error.Message);
        }
    }

    // =========================================================
    // Show placeholders or errors while gameplay remains paused.
    private void ShowMessage(string title, string text)
    {
        _message.Title = title;
        _message.DialogText = text;
        _message.PopupCentered(new Vector2I(460, 180));
    }
    #endregion
}
'@
$Changes['SYSTEMS/Saving/EntitySaves.cs'] = @'
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
'@
$Changes['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = @'
# Planned Work â€” Menus, Persistence and Local Lighting

## Purpose

Two upcoming work areas:

1. Main menu, in-game pause menu and persistent saves.
2. Local light sources that illuminate the world.

These are planned features. This document does not mark them as implemented.

Keep both systems modular and reuse the existing world layers, input modes,
inventory systems and shared visual settings where appropriate.

---

## 1 â€” Main Menu, Pause Menu and Persistent Saves

### Goal

Provide a proper entry point to the game and allow the player to save,
quit and continue without losing world changes.

### Main Menu

Initial options:

- Continue â€” available when a valid save exists.
- New Campaign â€” create a new world and player.
- Load Campaign â€” choose an existing save.
- Change Profile - can switch between profiles
- Sandbox - my testing
- Options. - placeholder
- About - just about this game - placeholder
- Exit.

Keep menu presentation separate from world creation and save/load logic.
Do not embed these systems in world_infinite or the player script.

### In-Game Pause Menu

Press ESC during gameplay to open the pause menu.

Initial options:

- Resume.
- Save Game.
- Options.
- Return to Main Menu.
- Exit Game.

Use the shared input-mode system to prevent movement, shooting, hotbar
scrolling and world interactions while the menu is open.

For this single-player first pass, opening the pause menu pauses world
simulation. Menu controls must continue processing.

ESC should first close the active interface where appropriate, such as
the inventory or debug map, rather than opening several menus together.

### Persistent Saves

Saving must preserve the playable world, not only the player's position
and the world seed.

Save data should include:

- Save format version.
- World identity and generation seed.
- Generation settings needed to reproduce the world.
- Player position and exact layer ID.
- Player vitals and progression.
- Inventory, equipment and hotbar assignments.
- Changes to generated world objects.
- Harvested or depleted resources.
- Container contents and claimed loot.
- Placed objects and structures.
- Relevant changes to liquids and basins.
- Persistent entities where required.
- Persistent connections between layers where required.

Generated terrain can be recreated from its seed and settings.
Store changes to that generated world rather than saving every unchanged
tile or loaded node.

Unloading a chunk must not discard its persistent changes.
Returning to a chunk or reloading a save must not restore harvested
resources or regenerate already-claimed loot.

Persistent object identities must include the world layer so objects at
the same coordinates on different depths remain separate.

### Reliability

- Store saves in Godot's user data location.
- Write to a temporary file before replacing the previous save.
- Keep a recoverable previous save.
- Detect unsupported or damaged saves and show a useful message.
- Include a save format version for future migrations.
- Confirm before overwriting or abandoning unsaved progress.

Do not treat debug test placement as permanent world content by default.

### Suggested Passes

1. Menu scenes and ESC/input-mode integration.
2. Save format, world identity and player persistence.
3. Persistent chunk changes, containers and world objects.
4. Remaining systems, recovery handling and end-to-end verification.

Do not label persistence complete until all currently relevant gameplay
changes survive quitting and loading.

### Completion Checks

- New Game starts a fresh world.
- Continue restores the correct save and layer.
- ESC pauses and resumes cleanly.
- Gameplay input does not leak through menus.
- Inventory and hotbar assignments survive loading.
- Harvested resources remain harvested.
- Claimed loot stays claimed.
- Container contents and placed objects survive loading.
- Chunk unloading does not lose changes.
- Surface and underground progress remain independent.
- A failed save does not destroy the previous valid save.

---

## 2 â€” Local Light Sources

### Goal

Allow local sources to illuminate nearby terrain and objects.

Examples:

- Lamps and placed lights.
- Glowing enemies or wildlife.
- Bioluminescent plants.
- Powered equipment.
- Temporary effects and projectiles.

An emissive-looking sprite or glow alone is not enough: the source should
also affect nearby world surfaces.

### Design Direction

Use one reusable light definition and runtime system.

A source should be able to define:

- Colour.
- Intensity.
- Radius.
- Enabled state.
- Optional flicker or pulse.
- Whether it moves.
- Whether it requires shadows.

Keep species-specific light settings with their species and object-specific
settings with their objects.

Shared lighting behaviour belongs with the existing visual lighting
systems. Global quality limits and multipliers can live in CONFIG.

### Compatibility

Review the existing terrain and sprite shaders before choosing the
implementation.

Local lighting must work with:

- Procedural ground surfaces.
- Imported and baked sprites.
- Terrain elevation and projected artwork.
- Existing sun lighting and shadows.
- Surface and underground layers.

Lights must respect exact layer identities. A surface light must not
illuminate a cave directly below it merely because their coordinates
overlap.

Decide explicitly how local lighting interacts with fading layers during
entrance transitions.

### Performance

Start with a small number of lights and measure the cost in a dense biome.

- Exclude distant lights from active lighting work.
- Stop unloaded lights from updating.
- Avoid scanning every world object for every light each frame.
- Avoid separate processing helpers on every vegetation instance.
- Update static light data only when it changes.
- Limit simultaneous visible lights through shared quality settings.
- Make local shadow casting optional and reserve it for selected sources.

Do not assume a light is cheap because its visual effect looks simple.

### Suggested Passes

1. Review shader compatibility and implement one test light.
2. Verify terrain, sprites, elevation and layer isolation.
3. Add reusable definitions and attach lights to objects and entities.
4. Add quality controls and measure performance with multiple sources.
5. Consider flicker, pulses and selected local shadows afterward.

### Completion Checks

- A test lamp visibly illuminates nearby ground and sprites.
- Light fades smoothly with distance.
- Moving sources remain aligned with their artwork.
- Lights affect only the correct world layer.
- Lights disappear correctly when their objects unload or are removed.
- Existing sunlight and shadows remain correct.
- Multiple lights have a measured, acceptable performance cost.
- Disabling local lighting restores the baseline appearance.

---

## Working Rules

- Review the latest GitHub push before preparing each pass.
- Keep scripts focused and folders clearly organised.
- Reuse existing shared systems where appropriate.
- Provide complete replacement files or functions.
- Remove temporary test nodes and obsolete implementations after testing.
- Update this document with actual implementation and verification status.

## Save Passes 1â€“2 â€” Applied, Verification Pending

- One current campaign slot per stable profile ID; manual pause-menu saving only.
- Versioned JSON, temporary-file replacement, previous-save backup and recovery.
- Fresh campaign seeds, exported world settings and resource recipe compatibility checks.
- Surface player position, original spawn, stats/modifiers, vitals, inventory, equipment, hotbar, crafting and world clock.
- Resource definition edits are detected and incompatible loads are refused; resource migrations remain future work.
- Save while alive, grounded and on the surface. Underground restore is a later pass.
- Not yet saved: harvested resources, grass changes, loose drops, containers, loot, buildings, entities, basin changes or connections.
- Debug herd and deep-cave test systems are unchanged and outside this pass.
- Build and local gameplay verification must be completed before marking tested.

Checks: two separate profiles; pause/save/quit/Continue; same surface position, original respawn location, seed/layout, reserves, inventory, selected hotbar, crafting progress and eclipse time; second save backup; rejected incompatible recipe; no save creation from Sandbox/F6.


## Save Pass 3 — Changed Resources

- One world-owned ResourceChanges helper; no frame processing or references to retired nodes.
- Stable generated IDs use world layer, resource family, chunk coordinate and original scatter candidate index. Scene resources use world-relative node paths. Ground deposits use chunk/slot IDs.
- Only changed resources are recorded: unfinished harvesting/mining work, remaining ore, depleted rocks/trees/plants/ore, removed grass, extracted ground-deposit units and unfinished shovel work.
- Grazing and successful building grass clearing share Grass.Clear. Normal chunk retirement never records grass as consumed.
- Removed solids retain lightweight generation-only footprint/spacing reservations. They have no collision, navigation blockers, artwork or shadows.
- Resource artwork callbacks check that their hosts still exist after awaiting a bake, so harvesting/unloading during initialization cannot access a freed node.
- Spawners draw original variation values and retain plant/grass spacing before skipping removed candidates.
- Change records load before world initialization, remain after chunks unload and are included by the existing pause Save action.
- Version 1 world/player saves still load with empty resource history. The next successful save writes version 2 with the named resource-changes section. Old game code cannot load version 2.
- Changes made before installing this pass cannot be reconstructed from older saves.
- Persistence remains partial: loose drops, containers, structures and entities are subsequent passes. A harvested source is now saved as removed, but its uncollected loose reward is not yet saved; collect rewards before quitting until Pass 4.
- Saving underground is still blocked until the layer restoration pass. Generated identities are layer-scoped for future extension.
- Local gameplay check: partially mine one rock/ore; completely harvest another rock/tree/plant; clear/graze grass; extract ground material; leave until chunks unload and return; save/quit/Continue with the same profile. Check another profile remains separate.
- Automated verification passed: production C# compilation, version 1 save loading, mutation/save, chunk retirement/regeneration, separate-process restore, profile separation, invalid resource-section rejection and installer conflict protection/idempotence. Runtime tests use headless artwork substitutes and controlled ore/ground-deposit fixtures; local visual gameplay verification remains required.


## Save Pass 4 — Items, Containers and Structures

- Existing version 1/2 saves still load. The next successful save writes version 3, retaining both resource-changes and world-objects sections. Earlier game code cannot read version 3.
- Loose stacks save stable GUIDs, exact quantities, item references, world positions, owning layers, pickup radius and remaining pickup delay. Accepted drops still awaiting deferred AddChild are included in the snapshot.
- Optional RemainingLifetimeSeconds is saved/restored. Null means no expiry. This pass does not decrement lifetime or implement an expiry timer. A future gameplay-time expiry mechanic can update this field; offline time must not consume it. Zero-lifetime records are excluded from saves/restoration.
- Shared item encoding resolves catalog IDs and external resource references, including starter tools absent from the master catalog. Exact storage slots are restored without collection/merging.
- Ordinary containers retain their contents by explicit layer-scoped PersistentId or scene-relative path. Placed containers use the building GUID. Loot caches retain full, partial and completely empty contents, including containers whose visible nodes retired.
- Death-wreck records include stable death ID, layer and position; LootWorld rebuilds them through its existing availability checks. Duplicate death identities do not generate another wreck.
- Placed objects save stable GUID, source item/scene identities, grid anchor, footprint and health. They restore before natural resource generation/navigation, without charging inventory or clearing grass again. Destroyed placed objects are absent from subsequent snapshots.
- Scene world-object health is restored separately; player/entity/placed-object health stays with its corresponding owner. Destroyed scene world objects are removed before Ready. Current landing pod/crate have no health component; this supports scene objects that actually own Health.
- Startup safely initializes saved loot caches before the first loot roll, including containers whose Ready precedes LootWorld.Ready. Object/item recipe assets are included in compatibility stamps.
- This remains staged persistence. Entity health/deaths/population/groups are Pass 5; saving the player underground and exact-depth restoration remain Pass 6. A killed entity may return until Pass 5 despite its saved drops/wreck. Debug herd and deep-cave test systems remain unchanged.
- Automated verification: full production C# compilation; separate-process headless save/reload with controlled scene fixtures covering pending/partial drops, lifetime and layers, exact storage, empty/retired loot, structures and scene health; backup recovery and profile isolation; version 1/2 load and version 3 upgrade; installer preview, application, idempotent rerun and conflict rejection. Headless artwork uses a test substitute; local visual gameplay checks remain.
- Local checks: leave a loose drop; partially pick up a larger stack; deposit and withdraw storage; empty a loot crate; partially loot a wreck and unload its visible node; place, damage and destroy separate buildings; save/quit/Continue and verify quantities, empty containers, surviving buildings, health and original world layer. Check another profile remains separate.


## Save Pass 5, Stage 1 — Stable Entity Identity and Deaths

- Requires Save Pass 4. The next successful save writes campaign version 4 with resource-changes, world-objects and entity-deaths sections. Version 1/2/3 campaigns still load, starting with no entity-death history. Earlier game code cannot read version 4.
- One world-owned EntityDeaths helper retains lightweight tombstones; no per-frame scanning, retired actor references or per-entity persistence helper nodes.
- Population identity uses the owning population's world-relative path, origin layer, world seed, original chunk and slot. It deliberately does not depend on actor position, selected candidate, species or engine instance ID.
- Authored entities already present in the detached WorldObjects scene receive world-relative scene identities. Dead authored actors are removed before EnterTree/Ready, so reload cannot issue their death rewards again.
- Population discovery and preparation reject killed identities. Normal streaming retirement and QueueFree do not create death records. Tombstones outlive chunk/layer unloading.
- An entity retains its origin identity during layer transfers. Its death record separately stores the actual death layer. A surface population actor killed underground therefore cannot reappear at its original surface spawn.
- Actor death and death-delivery callbacks share the same history. A separate identity reward claim prevents duplicate callbacks/actors from issuing another reward. Persisted robot wreck IDs use the stable origin identity; actual wreck/drop position and layer remain owned by Pass 4.
- Saved entity scene/definition resources are included in compatibility stamps. Invalid or duplicate death records are rejected before replacing the current save. Existing backup recovery and profile isolation remain in use.
- Debug TallowbackTest and deep-cavity test spawning remain unchanged and are not assigned persistent identities by this pass. Do not use the temporary debug herd to verify persistent wildlife. Future runtime spawners need stable origin identities and must consult EntityDeaths before creating actors.
- Stage 1 does not restore surviving entity position, health, home, AI timers, transferred living actors or group state. Stage 2 handles surviving population/actor state; Stage 3 handles population/group restoration. A death that happened before this stage cannot be reconstructed from an older save's wreck alone.
- Automated verification passed: full production C# compilation; real health/death delivery with controlled headless actor fixtures; origin/death-layer identity separation; population rediscovery after clearing records; retirement exclusion; duplicate reward suppression; invalid death-section rejection preserving the primary; separate-process restore, backup recovery and profile isolation; version 3 load/version 4 upgrade and the surviving-state stage boundary; installer preview/apply/idempotence and zero-write conflict rejection. Artwork is substituted for headless testing; local visual gameplay remains to be checked.
- Local check: kill a normal streamed robot; save, quit, Continue and revisit its origin chunk. It stays absent and its existing reward does not duplicate. Leave/revisit the chunk before saving too. Confirm a second profile has its own death history. Test transferred deaths when convenient; player saves remain surface-only until Pass 6.

## Save Pass 5, Stages 2–3 — Living Entities, Population and Groups

- Implemented against reviewed push aa113d5. Preserve the newer notification, inventory and vegetation work; this installer changes only entity/group persistence, save coordination, pause-save wording and the two ongoing-work save notes.
- Campaign version 5 adds the named entities-groups section. Versions 1–4 remain readable and upgrade on the next successful Save; prior unsaved living-entity changes cannot be reconstructed. Earlier game code cannot read version 5.
- Chosen population records preserve stable origin identity, prefab/species, actual position, solo home, health, exact current layer, random seed/state, waiting/feeding cooldowns and group membership. Both live and retired records are included, independently of loaded origin chunks.
- Remembered actors are indexed by actual saved position and exact layer. Existing population intervals, physics-check limits and one-create-per-update budgets remain in use. Loaded survivors may restore on screen; new seeded spawns retain their original visibility/distance checks.
- Inactive/distant cave survivors remain lightweight records and never force all cave chunks to load. They can restore when that exact layer is active and nearby terrain/collision checks permit. Origin identity remains unchanged across transfers, so the original surface slot cannot create a duplicate.
- Authored scene entities retain their scene branches/custom children. Saved authored actors are detached before Ready and reattach when their terrain is available, with definition, ownership and membership configured before behaviour binding. Dead authored actors remain governed by Stage 1.
- Group persistence includes stable identity, layer, centre, formation/minimum-member settings, threat-sharing policy, logical member IDs and optional roaming home/configuration/random state/remaining timer. Offline time does not advance roaming or actor cooldowns.
- Streaming retirement reserves a living member's logical slot instead of dissolving a group. Returning actors replace the reserved slot. Real deaths, departures and cross-layer departures can dissolve undersized groups; dissolution tombstones prevent authored groups from returning on Continue.
- Combat targets, navigation paths, in-flight attacks and grass reservations are transient and are reacquired. Unfinished grazing meals restart; already consumed grass remains covered by Pass 3. Do not describe this as serialization of every temporary AI action.
- Debug TallowbackTest and deep-cavity test placement remain outside persistent entity identity, as previously requested. Generic groups persist when authored or linked to persistent members; the temporary debug herd is not the entity save verification target.
- Automated checks passed: full production C# compilation including the latest notification/inventory sources; actual seeded population preparation and retirement; real health/death callbacks; authored and retired cold-process restoration; home/health/wait and logical group state; remaining roaming timer; dissolution; repeated scans without duplicate actors; dormant cave records followed by actual cave activation/restoration; backup recovery/profile isolation; version 4 load/version 5 upgrade retaining previous deaths; invalid entity-section rejection preserving the primary; installer preview/application/idempotence and zero-write conflict rejection. Headless fixtures substitute artwork and the visibility predicate, using the established biome assets; local visuals and newly edited biome content remain to be checked.
- Local checks: damage/move a streamed robot, retire/revisit it, Save/quit/Continue and verify health/home/location; repeat with a transferred survivor when convenient. Verify group membership after retirement and permanent dissolution after deaths. Check a second profile. Player saves remain surface-only until Pass 6.
- Next: Pass 6 exact underground player restoration/connections/liquid changes; Pass 7 combined regression checks and remaining menu work. Drop expiry and automatic saving/options remain future features.
'@
$Changes['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = @'
| Pass | Scope | Check before moving on |
|---|---|---|
| **1. Profile-owned save foundation** | Campaign identity, versioned save format with named sections, safe temporary-file replacement and backup recovery. Connect the existing pause Save button and basic New/Continue flow. | Profiles A and B save/load separate campaign metadata; failures preserve the previous save. Clearly mark this as partial persistence. |
| **2. World and player restoration** | Generation configuration, world time/eclipse phase, original spawn, player stats/reserves, inventory/equipment/hotbar and crafting. Establish controlled startup and restoration. | Quit and reload on the surface with the same player state and world layout. |
| **3. Generated resource changes** | Shared generated identities; rocks, trees, plants, ores, ground deposits and consumed/cleared grass. Integrate with chunk regeneration. | Harvest, leave until the chunk unloads, return, then quit/reload: changes remain. |
| **4. Items, containers and structures** | Loose drops, ordinary storage, loot containers, wrecks, buildings and health. Add records where only live nodes exist today. | No lost or duplicated items; empty containers stay empty; buildings survive. |
| **5. Entities and groups** | Population records plus live actors, persistent deaths, transferred actors and relevant group state. Coordinate death rewards with Pass 4. | Damaged, dead and transferred entities restore correctly without duplicate loot or wildlife. |
| **6. Underground restoration and liquids** | Complete exact-depth loading, required connections, basin changes and saves on entrance ramps. | Save/load in Surface, Upper Caverns and Deep Caverns, then traverse back successfully. |
| **7. Complete-save verification and menu finish** | Load Campaign selection, overwrite handling, recovery messages and combined regression checks. | One save restores every supported section across profiles and unloaded chunks. |

## Current Save Progress

- Passes 1–3: implemented; surface position fix confirmed locally.
- Pass 4: installed and verified in the reviewed source. Physical drops, storage/loot, wrecks, structures and health persist. Drop lifetime is stored; the expiry countdown remains future work.
- Pass 5.1: installed; stable origin IDs, deaths and coordinated reward identities.
- Pass 5.2: this installer adds surviving authored/population state, retired records and exact living layer/transfer ownership with lazy restoration near available terrain.
- Pass 5.3: this installer adds logical group membership, formation/roaming state and persistent dissolution. Streaming retirement preserves membership; restored actors cannot duplicate their origin slot.
- Versions 1–4 upgrade to campaign version 5 on Save. Living changes made before this pass cannot be reconstructed. Local gameplay checks remain required after the supplied automated checks.
- Next — Pass 6: exact underground player restoration, required connections and liquid/basin changes. Saving the player underground is still blocked.
- Then — Pass 7: full combined verification, load-selection/overwrite handling and remaining menu/recovery work.
- Persistence remains staged. Automatic saving/options and actual dropped-item expiry are separate future features. Combat targets/paths/reservations are rebuilt rather than persisted as engine references.
'@
$Expected['ENTITIES/Core/Entity.cs'] = 'b19cf0ace3ae577b5974c963b31c7e51f93e7e3228d25fcce1a06afaff8c61a4'
$Expected['ENTITIES/Core/EnemyPopulation.cs'] = '669f82a6a01aa0c291ba4a96b81147bfa5ca34879f8d43861920dc8f64c26ea9'
$Expected['ENTITIES/Groups/EntityGroup.cs'] = '456fee67f7b4cf450df249c7a7678e4ef44241a96db244d8539c34533432c3f5'
$Expected['ENTITIES/Groups/EntityGroupMember.cs'] = '01f6a0891fc74c98b8c6ca77d725f27da33b87637ae8e96b1c5fbf9ad1ec8176'
$Expected['ENTITIES/Groups/GroupRoaming.cs'] = 'c79374b09a77be29125709996c0eb22e509dea66a28ac1fcbfcc225e32242085'
$Expected['ENTITIES/Movement/EntityWandering.cs'] = '61bc64e901199e01f222f539dc7e199217a3666eaeec89141731a313982e134c'
$Expected['ENTITIES/Grazing/EntityGrazing.cs'] = '537e673dd525537d98e49eca03ba57fd37f84fb6c291abd730c015dd9cb04603'
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = 'e41ecf5c8a1c5978c49a25c468520fd9b4124c338604afc272eab59b85c92392'
$Expected['SYSTEMS/Saving/CampaignStore.cs'] = 'dbeb9cc84f1b1063b56e3f9d9db8eb2cbdb960f1800b996a60f8c3226a8e18ad'
$Expected['SYSTEMS/Saving/CampaignRecipe.cs'] = '24d5590c26d385f8e2437473014903dabfba6aab22ffc4b686acd9c6e88953d7'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = 'fc4157a4be9b63f118e462d2748f05c280c2f7e45b2204ffd0db119344c0ed42'
$Expected['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = 'da28869aa83210663fa7f39e69b7a70cf3cad90e7206c32145bd154c764df45f'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = '26934e79f4c8618f3132f36a209252c90fa870ee6f69443c6dd1b6d089ba9b1b'
$Expected['SYSTEMS/Saving/EntityDeaths.cs'] = '1aa9054eb40e56653d831badcddd7c2e58fca4e62bd4a7c170ad3994876b01e4'
$Expected['SYSTEMS/Saving/WorldObjectSaves.cs'] = '1db1da422ac930f1a07e9d001ebaf7cc2b51e97715b507fe68f97a5bab16401f'
$Expected['WORLD/Layers/WorldLayerMember.cs'] = 'b67af24c730e6102476627f7f71811c208fc09883c96669652c2b63a217107ec'
$Expected['ENTITIES/Core/EntityBody.cs'] = 'ab5dc4fc93a3b5a4dd79764798ec554345722216053a25db2dd44446cc5fdbe9'
# Validate all dependencies and targets before touching any project file.
$Conflicts = New-Object System.Collections.Generic.List[string]
$Pending = New-Object System.Collections.Generic.List[string]
foreach ($Relative in $Expected.Keys) {
    $Path = Join-Path $ProjectRoot $Relative
    if (!(Test-Path $Path)) { $Conflicts.Add("Missing: $Relative"); continue }
    $Hash = Digest ([IO.File]::ReadAllText($Path))
    $AlreadyUpdated = $Changes.Contains($Relative) -and $Hash -eq (Digest $Changes[$Relative])
    if ($Hash -ne $Expected[$Relative] -and !$AlreadyUpdated) { $Conflicts.Add("Changed since reviewed push: $Relative") }
}
foreach ($Relative in $Changes.Keys) {
    $Path = Join-Path $ProjectRoot $Relative
    if (Test-Path $Path) {
        if ((Digest ([IO.File]::ReadAllText($Path))) -eq (Digest $Changes[$Relative])) { continue }
        if (!$Expected.ContainsKey($Relative)) { $Conflicts.Add("Existing new-file destination: $Relative"); continue }
    }
    $Pending.Add($Relative)
}
if ($Conflicts.Count) { throw ($Conflicts -join "`n") }
if (!$Pending.Count) { Write-Host 'Pass 5.2 and 5.3 are already installed. No files changed.'; return }
Write-Host ('Files to install/update: ' + $Pending.Count)
$Pending | ForEach-Object { Write-Host ('  ' + $_) }
if ($Preview) { Write-Host 'Preview complete. No files changed.'; return }
$Backup = Join-Path $ProjectRoot ('.save-pass-backups/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($Backup) | Out-Null
$Original = @{}
# Backups use .txt so the C# compiler cannot compile duplicate scripts.
foreach ($Relative in $Pending) {
    $Path = Join-Path $ProjectRoot $Relative
    if (Test-Path $Path) {
        $Original[$Relative] = [IO.File]::ReadAllBytes($Path)
        $Copy = Join-Path $Backup ($Relative + '.before.txt')
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Copy)) | Out-Null
        [IO.File]::WriteAllBytes($Copy, $Original[$Relative])
    }
}
$Written = New-Object System.Collections.Generic.List[string]
try {
    foreach ($Relative in $Pending) {
        $Path = Join-Path $ProjectRoot $Relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
        $Written.Add($Relative)
        [IO.File]::WriteAllText($Path, $Changes[$Relative] + "`n", $Utf8)
    }
}
catch {
    foreach ($Relative in $Written) {
        $Path = Join-Path $ProjectRoot $Relative
        if ($Original.ContainsKey($Relative)) { [IO.File]::WriteAllBytes($Path, $Original[$Relative]) }
        elseif (Test-Path $Path) { Remove-Item -LiteralPath $Path -Force }
    }
    throw
}
Write-Host ('Installed. Original files backed up in: ' + $Backup)
Write-Host 'Reopen Godot, build C#, and use Continue with the same profile.'
Write-Host 'Damage/move a normal entity; retire/revisit it; save, quit and Continue with the same profile.'
Write-Host 'Existing saves upgrade to version 5 on Save. Underground player restoration remains Pass 6.'
