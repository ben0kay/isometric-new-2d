# Installs save Pass 5 Stage 1: stable entity identities and deaths. Requires Pass 4.
# Entity baseline reviewed at 3ccdd95; save baseline is the supplied Pass 4 installer. No Git operations.
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

            Membership.JoinAssignedGroup();

            Component<EntityDeathLoot>("DeathLoot");
            Health.Died += OnDeath;

            ImageTexture texture = await Definition.GetArtwork(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

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

        int work = System.Math.Clamp(ChunksPerUpdate, 1, 16);
        for (int i = 0; i < work; i++)
        {
            Player player = _players[_cursor % _players.Count];
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
                break;
            }
        }

        if (!record.Chosen || !CanActivate(record.Definition, record.Position)) return;

        Entity actor = EnemyScene.Instantiate<Entity>();
        actor.PersistentId = record.Id;
        actor.Definition = record.Definition;
        actor.SpawnPending = true;
        actor.SpawnHome = record.Home;
        actor.RandomSeed = SeedFor(record.Coordinate, record.Slot + 1);
        actor.Position = _objects.ToLocal(record.Position);
        actor.Visible = false;
        actor.Died += () => record.Dead = true;

        record.Actor = actor;
        _objects.AddChild(actor);
        actor.Health.RestoreState(record.Vitality);
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

        if (WorldLayerMember.For(actor) != WorldLayerId.Surface)
            continue;

        Vector2 point = actor.GlobalPosition;
        bool visible = IsOnScreen(record.Definition, point);
        bool retire = !actor.HasTarget && !visible &&
            (!NearPlayers(point, RetireDistance) ||
             !_chunks.IsNavigationPointAvailable(point));

        if (retire)
        {
            record.Position = point;
            record.Home = actor.Home;
            record.Vitality = actor.Health.Current;
            actor.CollisionLayer = 0;
            actor.Hide();
            actor.SetPhysicsProcess(false);
            actor.QueueFree();
            record.Actor = null;
            _active.RemoveAt(i);
            continue;
        }

        if (!actor.IsActivated && actor.Initialized &&
            CanActivate(record.Definition, point))
            actor.Activate();
    }
}
    #endregion
}
'@
$Changes['ENTITIES/Death/EntityDeathLoot.cs'] = @'
// Generates entity death drops or records the configured robot wreck.
// Delivery is separate from actor movement, combat and presentation.
using Godot;
using System;

public partial class EntityDeathLoot : Node
{
    #region State
    public InventoryStorage GeneratedContents { get; private set; }

    private Entity _actor;
    private bool _generated;
    #endregion

    #region Lifecycle
    // =========================================================
    // Subscribe after the shared actor has cached its health component.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Entity>();
        _actor.Health.Died += OnDeath;

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Remove the subscription when the actor leaves the world.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actor?.Health))
            _actor.Health.Died -= OnDeath;
    }
    #endregion

    #region Delivery
    // =========================================================
    // Deliver the configured death result once.
    private void OnDeath()
    {
        if (_generated) return;
        _generated = true;

        try
        {
            EntityDeaths deaths = EntityDeaths.Find(this);
            if (deaths != null && !deaths.TryClaimRewards(_actor)) return;

            if (_actor.Definition.DeathDelivery ==
                EntityDeathDelivery.RobotWreck)
            {
                RecordWreck();
                return;
            }

            LootTable table = _actor.Definition.DeathLoot;
            if (table == null) return;

            ResourceWorld resources = ResourceWorld.Find(this);

            if (resources == null)
                throw new InvalidOperationException(
                    "Entity loot requires ResourceWorld.");

            StorageDefinition capacity = new()
            {
                SlotCount = 96,
                MaximumWeightKg = float.MaxValue,
                CapacityLitres = float.MaxValue
            };

            ulong seed = _actor.RandomSeed != 0
                ? _actor.RandomSeed : !string.IsNullOrEmpty(_actor.PersistentId)
                    ? EntityDeaths.IdentitySeed(_actor.PersistentId) : _actor.GetInstanceId();

            GeneratedContents = table.Generate(
                resources.Catalog, capacity, seed);

            DeliverGroundDrops(resources);
        }
        catch (Exception error)
        {
            GD.PushError(
                $"Entity '{_actor.Definition.SpeciesId}' death delivery failed: {error}");
        }
    }

    // =========================================================
    // Preserve the existing persistent robot wreck identity and delivery.
    private void RecordWreck()
    {
        string layer = WorldLayerMember.For(_actor);
        ulong identity = _actor.RandomSeed != 0
            ? _actor.RandomSeed : _actor.GetInstanceId();

        string wreckId = !string.IsNullOrEmpty(_actor.PersistentId)
            ? "dead_entity:" + _actor.PersistentId
            : $"{layer}:dead_robot:{_actor.Definition.SpeciesId}:{identity}";

        LootWorld.GetOrCreate(this).RecordRobotDeath(
            wreckId, _actor.GlobalPosition, layer);
    }

    // =========================================================
    // Deliver generated stacks as ordinary same-layer world pickups.
    private void DeliverGroundDrops(ResourceWorld resources)
    {
        for (int i = 0; i < GeneratedContents.SlotCount; i++)
        {
            InventoryStack stack = GeneratedContents.Get(i);
            if (stack.IsEmpty) continue;

            Vector2 offset = Vector2.FromAngle(i * 2.4f) * 12f;

            if (!resources.SpawnItemFor(
                _actor, stack.Item, stack.Count,
                _actor.GlobalPosition + offset))
                GD.PushError(
                    $"Could not deliver death loot '{stack.Item.Id}'.");
        }
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
              data.Version == 4 && data.Coverage == "world-player-resources-objects-deaths") ||
            data.ProfileId != profile || !Guid.TryParseExact(data.CampaignId, "N", out _) ||
            data.Player == null || data.Settings == null || data.Resources == null ||
            data.Sections == null ||
            (data.Version == 1 ? data.Sections.Count != 0 :
                data.Version == 2 ? data.Sections.Count != 1 || !data.Sections.ContainsKey(ResourceChanges.Section) :
                data.Sections.Count != (data.Version == 3 ? 2 : 3) ||
                    !data.Sections.ContainsKey(ResourceChanges.Section) ||
                    !data.Sections.ContainsKey(WorldObjectSaves.Section) ||
                    (data.Version == 4 && !data.Sections.ContainsKey(EntityDeaths.Section))))
            throw new InvalidDataException("Unsupported or invalid campaign save.");
        ResourceChanges.ReadSection(data);
        WorldObjectSaves.ReadSection(data);
        EntityDeaths.ReadSection(data);
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
$Changes['SYSTEMS/Saving/EntityDeaths.cs'] = @'
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

- Passes 1–3: implemented. Surface position fix confirmed locally.
- Pass 4: installer supplied; items, containers, wrecks, structures and health; optional drop lifetime stored, no expiry countdown. The reviewed push 3ccdd95 does not yet include this installer's files. Verify local installation and push before the next code review.
- Pass 5 Stage 1: this installer adds stable population/scene entity IDs, saved deaths and coordinated reward identities. Existing saves upgrade to version 4 on Save; prior entity deaths cannot be reconstructed.
- Pass 5 Stage 2: surviving entities and population records — position, health, home and live layer/transfer ownership.
- Pass 5 Stage 3: relevant group/population state and duplicate-free restoration.
- Pass 6: exact underground player restoration, required connections and liquid/basin changes.
- Pass 7: full combined verification and remaining menu/recovery work.
- Persistence remains partial. Automatic saving/options and the actual dropped-item expiry timer are future features.
'@
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = '6b2a1ecc974222cbc44726638da213e617219c43bd48c1e76c4ae9dc7aa0910c'
$Expected['SYSTEMS/Saving/CampaignRecipe.cs'] = 'd70e2e5b2c0f476b084e7765561fbce02bea2d32a35579b6408bdd006b3deb83'
$Expected['SYSTEMS/Saving/CampaignStore.cs'] = '5d0af41c6d1818cbe310b8833a295e7828fae2b91769c193b8a9ea634c90c028'
$Expected['ITEMS/World/WorldPickup.cs'] = '347a70bd49de95aa9758f4205e05aafd35f1a607619e044d4d8c545444b10a74'
$Expected['ITEMS/World/ResourceWorld.cs'] = '5ebe6b4abeae584e6da1537172e2024dd8db1408864aff85bcba3e9a77378b84'
$Expected['SYSTEMS/Storage/WorldStorage.cs'] = 'b5017033da52195be7467aca79708b01cb516f0f3bea9e04321fdc706d3bf33d'
$Expected['SYSTEMS/Loot/LootContainer.cs'] = 'b940c444f080ef070da7731e32e456c9886948b0a9c66e31f72b9733ae027f40'
$Expected['SYSTEMS/Loot/LootWorld.cs'] = '3dc2a01b859e341c1f93da484a179314a5463a8b973c2409dd7a442434cfdeab'
$Expected['SYSTEMS/Placement/PlacedObject.cs'] = 'ee5053c95d028b201241acca7d85880607920a6f52669357c92147c4019f8f14'
$Expected['SYSTEMS/Placement/PlacementWorld.cs'] = 'aa9c57c8eb30e7ee9a0908136dfdbcd6b1b2d5ae6b85ab622ab0fb29e0fdceb2'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = 'fc4157a4be9b63f118e462d2748f05c280c2f7e45b2204ffd0db119344c0ed42'
$Expected['SYSTEMS/Saving/WorldObjectSaves.cs'] = '1db1da422ac930f1a07e9d001ebaf7cc2b51e97715b507fe68f97a5bab16401f'
$Expected['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = '82247c43c8d91e33518a247b0eb7dc3ccc2481a40b19db396ff10f59f6b240e7'
$Expected['ENTITIES/Core/Entity.cs'] = 'fe6b299907bd84f77abccb7113e1d87859e80764db89cb7b5b3bbbe4c06eb75f'
$Expected['ENTITIES/Core/EnemyPopulation.cs'] = 'a942e071f5bdc9cf4158e070bbfd2d56a6066fc5e4275d354956e03b7cc301e9'
$Expected['ENTITIES/Death/EntityDeathLoot.cs'] = '770d6d428b5d3d9b69e7717e939ac6f958d158b29fc7933a0fa6075d56f91964'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = 'b707433817c88d772e9e8a1fb3a01eeb8eb2f9cfa382e39ecf32da9318199238'
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
if (!$Pending.Count) { Write-Host 'Pass 5 Stage 1 is already installed. No files changed.'; return }
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
Write-Host 'Kill a normal streamed robot, save, quit, Continue and revisit its origin chunk.'
Write-Host 'Existing saves upgrade on next Save. Surviving entity and group state are later stages.'
