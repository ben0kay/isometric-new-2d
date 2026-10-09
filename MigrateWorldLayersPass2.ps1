# One-time pass 2 migration. Run from the project root with Godot closed.
# Preflights every edit before writing and restores files if a write fails.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'project.godot'))) {
    throw 'Put this script beside project.godot, then run it again.'
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$pending = [ordered]@{}
$original = @{}

function Normalize-Source([string] $text) {
    return $text.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd([char]10) + "`n"
}

$patch = @'
--- a/COMBAT/Attacks/ContactAttack.cs
+++ b/COMBAT/Attacks/ContactAttack.cs
@@ -36,3 +36,5 @@
         _timer = 0.1;
-        _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
+        string layer = WorldLayerMember.For(_actor);
+        if (!GodotObject.IsInstanceValid(_navigation) || _navigation.Layer != layer)
+            _navigation = WorldNavigation.ForLayer(_actor, layer);
         if (_navigation == null) return;
@@ -41,3 +43,4 @@
         {
-            if (node is not Player player) continue;
+            if (node is not Player player ||
+                !WorldLayerMember.Same(_actor, player)) continue;
             Health target = player.GetNodeOrNull<Health>("Systems/Health");
--- a/COMBAT/Weapons/MiningEmitter.cs
+++ b/COMBAT/Weapons/MiningEmitter.cs
@@ -104,2 +104,3 @@
         Node2D collider = hit["collider"].AsGodotObject() as Node2D;
+        if (collider != null && !WorldLayerMember.Same(_source, collider)) return;
         bool worked = false;
@@ -116,3 +117,3 @@
                 worked = target.Mine(power, (item, count) =>
-                    resources.Spawn(item.Id, count, position));
+                    resources.Spawn(item.Id, count, position, collider));
             }
--- a/CONFIG/GlobalConfig.cs
+++ b/CONFIG/GlobalConfig.cs
@@ -32,5 +32,2 @@
 public float CaveBiomeScaleMultiplier { get; set; } = 1f;
-
-// Absolute terrain elevation of the main underground network.
-[Export] public float CaveFloorElevation { get; set; } = -160f;
 
--- a/DEBUG/Map/WorldMapPoiPreview.cs
+++ b/DEBUG/Map/WorldMapPoiPreview.cs
@@ -52,4 +52,3 @@
 
-        _cave = context.GetTree().GetFirstNodeInGroup(
-            "cave_world") as CaveWorld;
+        _cave = WorldLayerRuntime.Find(context)?.SurfaceUnderground;
 
--- a/ITEMS/World/ResourceWorld.cs
+++ b/ITEMS/World/ResourceWorld.cs
@@ -43,7 +43,9 @@
 	// Resolve an item and defer scene attachment outside physics queries.
-	public bool Spawn(string itemId, int count, Vector2 globalPosition)
+	public bool Spawn(string itemId, int count, Vector2 globalPosition,
+        Node owner = null)
 	{
 		ItemDefinition item = Catalog.Get(itemId);
+        Node2D objects = RootFor(owner);
 		if (item == null || count <= 0 ||
-			!GodotObject.IsInstanceValid(_objects))
+			!GodotObject.IsInstanceValid(objects))
 		{
@@ -60,5 +62,5 @@
 			PickupDelay = Math.Max(0, PickupDelay),
-			Position = _objects.ToLocal(globalPosition)
+			Position = objects.ToLocal(globalPosition)
 		};
-		_objects.CallDeferred(Node.MethodName.AddChild, pickup);
+		objects.CallDeferred(Node.MethodName.AddChild, pickup);
 		return true;
@@ -90,5 +92,6 @@
 public bool SpawnHarvest(string primaryId, int primaryCount,
-	string[] bonusIds, Vector2 globalPosition)
+	string[] bonusIds, Vector2 globalPosition, Node owner = null)
 {
-	if (primaryCount <= 0 || !GodotObject.IsInstanceValid(_objects))
+    Node2D objects = RootFor(owner);
+	if (primaryCount <= 0 || !GodotObject.IsInstanceValid(objects))
 		return false;
@@ -128,3 +131,3 @@
 			PickupDelay = Math.Max(0, PickupDelay),
-			Position = _objects.ToLocal(globalPosition + offset)
+			Position = objects.ToLocal(globalPosition + offset)
 		});
@@ -133,3 +136,3 @@
 	foreach (WorldPickup pickup in pickups)
-		_objects.CallDeferred(Node.MethodName.AddChild, pickup);
+		objects.CallDeferred(Node.MethodName.AddChild, pickup);
 	return true;
@@ -143,9 +146,4 @@
 {
-    string layer = WorldLayerMember.For(owner);
-    WorldLayerController layers = WorldLayerController.Find(this);
-    Node2D objects = layer == WorldLayerId.Underground1
-        ? layers?.Cave?.Objects : _objects;
-
-    if (item == null || count <= 0 ||
-        !GodotObject.IsInstanceValid(objects))
+    Node2D objects = RootFor(owner);
+    if (item == null || count <= 0 || !GodotObject.IsInstanceValid(objects))
         return false;
@@ -161,3 +159,2 @@
     };
-
     objects.CallDeferred(Node.MethodName.AddChild, pickup);
@@ -165,2 +162,14 @@
 }
+
+    // =========================================================
+    // Resolve source ownership independently from the player's current depth.
+    private Node2D RootFor(Node owner)
+    {
+        if (owner == null) return WorldLayerController.DropRoot(this, _objects);
+        string layer = WorldLayerMember.For(owner);
+        return layer == WorldLayerId.Surface ? _objects
+            : (WorldLayerRuntime.Find(this) ??
+                throw new InvalidOperationException("Missing layer runtime."))
+                .ObjectsFor(layer);
+    }
 }
--- a/SYSTEMS/Harvesting/ResourceHarvest.cs
+++ b/SYSTEMS/Harvesting/ResourceHarvest.cs
@@ -75,2 +75,3 @@
         if (!GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
+            !WorldLayerMember.Same(player, _host) ||
             player.GetNode<PlayerEquipment>("Systems/Equipment").CurrentTool != null ||
@@ -92,3 +93,3 @@
     if (!_resources.SpawnHarvest(id, units,
-        Definition.BonusHarvestItems, _host.GlobalPosition)) return false;
+        Definition.BonusHarvestItems, _host.GlobalPosition, _host)) return false;
 
--- a/SYSTEMS/Loot/LootWorld.cs
+++ b/SYSTEMS/Loot/LootWorld.cs
@@ -157,5 +157,4 @@
 
-    WorldLayerController layers = WorldLayerController.Find(this);
-    Node2D root = record.Layer == WorldLayerId.Underground1
-        ? layers.Cave.Objects : _objects;
+    Node2D root = record.Layer == WorldLayerId.Surface ? _objects
+        : WorldLayerRuntime.Find(this).ObjectsFor(record.Layer);
 
@@ -203,9 +202,7 @@
 {
-    WorldLayerController layers = WorldLayerController.Find(this);
-    string current = layers?.Current ?? WorldLayerId.Surface;
-
+    WorldLayerRuntime runtime = WorldLayerRuntime.Find(this);
+    string current = runtime?.ActiveLayer ?? WorldLayerId.Surface;
     if (record.Layer != current) return false;
-
-    return record.Layer == WorldLayerId.Underground1
-        ? layers?.Cave.Streaming.IsAvailable(record.Position, 0f) == true
+    return runtime != null
+        ? runtime.IsAvailable(record.Layer, record.Position)
         : _chunks.IsNavigationPointAvailable(record.Position);
--- a/VISUALS/Shadows/GroundShadowWorld.cs
+++ b/VISUALS/Shadows/GroundShadowWorld.cs
@@ -35,3 +35,4 @@
     private Node _world;
-    private Node2D _surfaceRoot, _caveRoot;
+    private Node2D _surfaceRoot;
+    private readonly Dictionary<string, Node2D> _layerRoots = new();
     private WorldLayerController _layers;
@@ -393,13 +394,11 @@
         if (layer == WorldLayerId.Surface) return _surfaceRoot;
-
-        _layers ??= WorldLayerController.Find(this);
-        if (_layers?.Cave == null) return _surfaceRoot;
-
-        if (!GodotObject.IsInstanceValid(_caveRoot))
-        {
-            _caveRoot = new Node2D { Name = "SharedGroundShadows" };
-            _layers.Cave.Root.AddChild(_caveRoot);
-        }
-
-        return _caveRoot;
+        if (_layerRoots.TryGetValue(layer, out Node2D root) &&
+            GodotObject.IsInstanceValid(root)) return root;
+
+        WorldLayerRuntime runtime = WorldLayerRuntime.Find(this)
+            ?? throw new InvalidOperationException("Missing layer runtime for shadows.");
+        root = new Node2D { Name = "SharedGroundShadows" };
+        runtime.GetUnderground(layer).Root.AddChild(root);
+        _layerRoots[layer] = root;
+        return root;
     }
@@ -518,4 +517,5 @@
             _surfaceRoot.QueueFree();
-        if (GodotObject.IsInstanceValid(_caveRoot))
-            _caveRoot.QueueFree();
+        foreach (Node2D root in _layerRoots.Values)
+            if (GodotObject.IsInstanceValid(root)) root.QueueFree();
+        _layerRoots.Clear();
     }
--- a/WORLD/Contents/GroundResources/GroundResourceWorld.cs
+++ b/WORLD/Contents/GroundResources/GroundResourceWorld.cs
@@ -271,3 +271,3 @@
 
-            if (!_resources.Spawn(deposit.Definition.ItemId, 1, globalPoint))
+            if (!_resources.Spawn(deposit.Definition.ItemId, 1, globalPoint, this))
                 return false;
--- a/WORLD/Generation/Caves/CaveWorld.cs
+++ b/WORLD/Generation/Caves/CaveWorld.cs
@@ -1,3 +1,3 @@
-// Owns one shared cave network and its paired surface holes.
-// All entrances use the same cave root, generator and chunk streamer.
+// Owns one independent underground world, its terrain and runtime services.
+// Only the surface-connected layer owns surface entrance metadata.
 using Godot;
@@ -8,5 +8,6 @@
     #region State
-    public string LayerId { get; set; } = WorldLayerId.Underground1;
+    public string LayerId { get; set; } = "";
     public WorldLayerDefinition Definition { get; private set; }
     public CaveGenerationSettings Settings { get; private set; }
+    public uint WorldSeed { get; private set; }
     public CaveGenerator Generator { get; private set; }
@@ -26,2 +27,3 @@
 
+    private WorldLayerMember _objectsMember;
     private readonly List<CaveHole> _holes = new();
@@ -34,6 +36,6 @@
     // =========================================================
-    // Register the optional cave service for shared placement checks.
+    // Keep world ownership free of per-frame processing.
     public override void _EnterTree()
     {
-        AddToGroup("cave_world");
+        SetProcess(false);
     }
@@ -56,3 +58,4 @@
         RimHeight = rimHeight;
-        FloorElevation = WorldConfig.Find(this).CaveFloorElevation;
+        FloorElevation = Definition.FloorElevation;
+        WorldSeed = worldSeed;
 
@@ -60,3 +63,3 @@
             throw new System.InvalidOperationException(
-                "CaveFloorElevation must be finite and below zero.");
+                "The underground layer floor elevation must be finite and below zero.");
 
@@ -67,3 +70,3 @@
                     $"Cave floor must be below entrance '{hole.Id}'. " +
-                    "Lower CaveFloorElevation or raise that surface biome.");
+                    "Lower the layer definition's FloorElevation or raise that surface biome.");
         }
@@ -80,2 +83,3 @@
         Root.GlobalPosition = origin;
+        WorldLayerMember.Attach(Root, LayerId);
 
@@ -83,2 +87,3 @@
         Root.AddChild(Objects);
+        _objectsMember = WorldLayerMember.Attach(Objects, LayerId);
 
@@ -135,2 +140,14 @@
         Streaming.Configure(this);
+        AddChild(new WorldNavigation { Name = "Navigation", Cave = this });
+    }
+    #endregion
+
+    #region Activation
+    // =========================================================
+    // Separate chunk work from drawing and object processing on inactive depths.
+    public void SetActive(bool active)
+    {
+        Root.Visible = active;
+        _objectsMember.SetActive(active);
+        Streaming.SetActive(active);
     }
@@ -214,10 +231,7 @@
 {
-    CaveWorld world = context.GetTree().GetFirstNodeInGroup(
-        "cave_world") as CaveWorld;
-
-    if (world == null) return false;
-
-    float objectRadius =
-        footprint.Length() * 0.5f + padding.Length();
-
+    WorldLayerRuntime runtime = WorldLayerRuntime.Find(context);
+    if (runtime == null) return false;
+    CaveWorld world = runtime.SurfaceUnderground;
+
+    float objectRadius = footprint.Length() * 0.5f + padding.Length();
     foreach (CaveHole hole in world.Holes)
@@ -226,11 +240,6 @@
         float squared = radius * radius;
-
         if (point.DistanceSquaredTo(hole.SurfacePosition) < squared ||
-            point.DistanceSquaredTo(
-                hole.OutsidePosition(world.TileSize)) < squared)
-        {
+            point.DistanceSquaredTo(hole.OutsidePosition(world.TileSize)) < squared)
             return true;
-        }
-    }
-
+    }
     return false;
@@ -277,3 +286,3 @@
     {
-        return Planner.Nearby(tile);
+        return Planner?.Nearby(tile) ?? _holes;
     }
--- a/WORLD/Generation/Caves/Chunks/CaveChunk.cs
+++ b/WORLD/Generation/Caves/Chunks/CaveChunk.cs
@@ -241,5 +241,5 @@
 
-        GenerationMetadataLease.Attach(this, generation, area);
-
-        foreach (int step in generation.PrepareArea(area))
+        GenerationMetadataLease.Attach(this, generation, area, _world.LayerId);
+
+        foreach (int step in generation.PrepareArea(area, _world.LayerId))
             yield return step;
--- a/WORLD/Generation/Caves/Chunks/CaveChunkController.cs
+++ b/WORLD/Generation/Caves/Chunks/CaveChunkController.cs
@@ -24,3 +24,4 @@
     private Player _player;
-    private bool _active;
+    private bool _active, _preloadEntrances;
+    private Vector2? _preloadPoint;
     private Vector2I _focus;
@@ -42,2 +43,3 @@
         _world = world;
+        SetProcess(false);
     }
@@ -65,4 +67,7 @@
                 tile = _world.WorldToTile(_player.GlobalPosition);
+            else if (_preloadPoint.HasValue)
+                tile = _world.WorldToTile(_preloadPoint.Value);
             else
             {
+                if (!_preloadEntrances) return;
                 CaveHole hole =
@@ -97,2 +102,27 @@
     // =========================================================
+    // Prepare surface mouths only in the layer explicitly connected to surface.
+    public void SetEntrancePreloading(bool enabled)
+    {
+        _preloadEntrances = enabled;
+        SetProcess(_active || enabled || _preloadPoint.HasValue);
+    }
+
+    // =========================================================
+    // Prepare a requested destination without enabling its collisions.
+    public void RequestPreload(Vector2 point)
+    {
+        _preloadPoint = point;
+        SetProcess(true);
+    }
+
+    // =========================================================
+    // Stop temporary work while retaining already-built destination chunks.
+    public void CancelPreload()
+    {
+        _preloadPoint = null;
+        SetProcess(_active || _preloadEntrances);
+    }
+
+
+    // =========================================================
     // Change collision and visibility for every completed cave chunk.
@@ -101,2 +131,3 @@
         _active = active;
+        SetProcess(_active || _preloadEntrances || _preloadPoint.HasValue);
         foreach (CaveChunk chunk in _chunks.Values)
@@ -254,3 +285,3 @@
             Mathf.Abs(difference.Y) <= radius) ||
-            pursuit?.RetainCave(pair.Key) == true)
+            pursuit?.RetainCave(_world, pair.Key) == true)
             continue;
--- a/WORLD/Generation/Caves/Surface/CaveEntrancePlanner.cs
+++ b/WORLD/Generation/Caves/Surface/CaveEntrancePlanner.cs
@@ -19,2 +19,3 @@
     private CaveWorld _cave;
+    private readonly float _floorElevation;
 
@@ -32,5 +33,7 @@
 public CaveEntrancePlanner(
-    Node world, ChunkController chunks, CaveGenerationSettings settings)
+    Node world, ChunkController chunks, CaveGenerationSettings settings,
+    float floorElevation)
 {
     _world = world;
+    _floorElevation = floorElevation;
     _chunks = chunks;
@@ -177,3 +180,3 @@
                 if (result.Accepted &&
-                    result.RimHeight > _config.CaveFloorElevation + 32f)
+                    result.RimHeight > _floorElevation + 32f)
                 {
--- a/WORLD/Layers/CaveEnemyPursuit.cs
+++ b/WORLD/Layers/CaveEnemyPursuit.cs
@@ -14,2 +14,3 @@
         public CaveHole Portal;
+        public CaveWorld PortalWorld;
         public IDisposable Lease;
@@ -38,3 +39,3 @@
     // =========================================================
-    // Create one helper and one cave instance of the shared navigation service.
+    // Create pursuit coordination; each runtime world owns its navigation.
     public static void Ensure(WorldLayerController layers, Node world)
@@ -42,9 +43,2 @@
         if (Find(layers) != null) return;
-
-        if (layers.Cave.GetNodeOrNull<WorldNavigation>("Navigation") == null)
-            layers.Cave.AddChild(new WorldNavigation
-            {
-                Name = "Navigation",
-                Cave = layers.Cave
-            });
 
@@ -189,3 +183,3 @@
     public void PlayerCrossed(
-        CaveHole hole, string destination, Player player)
+        CaveWorld portalWorld, CaveHole hole, string destination, Player player)
     {
@@ -204,5 +198,10 @@
 
-            if (hole != null && record.Member.Layer != destination)
+            if (hole != null && record.Member.Layer != destination &&
+                (record.Member.Layer == WorldLayerId.Surface ||
+                    record.Member.Layer == portalWorld.LayerId) &&
+                (destination == WorldLayerId.Surface ||
+                    destination == portalWorld.LayerId))
             {
                 record.Portal = hole;
+                record.PortalWorld = portalWorld;
 
@@ -210,3 +209,3 @@
                     _surfaceGround.ToLocal(hole.SurfacePosition),
-                    _layers.Cave.TileSize);
+                    portalWorld.TileSize);
 
@@ -232,3 +231,3 @@
             ? record.Portal.SurfacePosition
-            : record.Portal.OutsidePosition(_layers.Cave.TileSize);
+            : record.Portal.OutsidePosition(record.PortalWorld.TileSize);
         return true;
@@ -242,3 +241,11 @@
         CaveHole hole = record.Portal;
+        CaveWorld world = record.PortalWorld;
         string destination = WorldLayerMember.For(actor.Target);
+
+        if (world == null ||
+            (destination != WorldLayerId.Surface && destination != world.LayerId))
+        {
+            ClearPortal(record);
+            return;
+        }
 
@@ -252,3 +259,3 @@
             ? hole.SurfacePosition
-            : hole.OutsidePosition(_layers.Cave.TileSize);
+            : hole.OutsidePosition(world.TileSize);
 
@@ -258,7 +265,7 @@
         Vector2 landing;
-        if (destination == WorldLayerId.Underground1)
-        {
-            landing = _layers.Cave.TileToWorld(hole.TileAt(0.25f));
-            if (!_layers.Cave.Streaming.EntryReady(hole) ||
-                !_layers.Cave.Streaming.IsAvailable(landing, 10f))
+        if (destination == world.LayerId)
+        {
+            landing = world.TileToWorld(hole.TileAt(0.25f));
+            if (!world.Streaming.EntryReady(hole) ||
+                !world.Streaming.IsAvailable(landing, 10f))
                 return;
@@ -267,5 +274,5 @@
         {
-            landing = hole.OutsidePosition(_layers.Cave.TileSize);
-            WorldNavigation surface = GetTree().GetFirstNodeInGroup(
-                "world_navigation") as WorldNavigation;
+            landing = hole.OutsidePosition(world.TileSize);
+            WorldNavigation surface = WorldNavigation.ForLayer(
+                this, WorldLayerId.Surface);
 
@@ -287,2 +294,3 @@
         record.Portal = null;
+        record.PortalWorld = null;
     }
@@ -323,5 +331,5 @@
     // Protect loaded cave floor along an active pursuer's local route.
-    public bool RetainCave(Vector2I coordinate)
-    {
-        int size = _layers.Cave.Settings.ChunkSize;
+    public bool RetainCave(CaveWorld world, Vector2I coordinate)
+    {
+        int size = world.Settings.ChunkSize;
         Rect2 chunk = new(
@@ -333,11 +341,11 @@
             if (!IsPursuing(record) ||
-                record.Member.Layer != WorldLayerId.Underground1)
-                continue;
-
-            Vector2 from = _layers.Cave.WorldToTile(
+                record.Member.Layer != world.LayerId)
+                continue;
+
+            Vector2 from = world.WorldToTile(
                 record.Actor.GlobalPosition);
             Vector2 destination = record.Portal != null
-                ? record.Portal.OutsidePosition(_layers.Cave.TileSize)
+                ? record.Portal.OutsidePosition(record.PortalWorld.TileSize)
                 : record.Actor.Target.GlobalPosition;
-            Vector2 to = _layers.Cave.WorldToTile(destination);
+            Vector2 to = world.WorldToTile(destination);
 
--- a/WORLD/Layers/WorldLayerCatalog.cs
+++ b/WORLD/Layers/WorldLayerCatalog.cs
@@ -10,2 +10,4 @@
     #region Definitions
+    [Export] public string SurfaceEntranceLayerId { get; set; }
+        = WorldLayerId.Underground1;
     [Export] public Godot.Collections.Array<WorldLayerDefinition> Layers { get; set; }
@@ -39,2 +41,7 @@
 
+        if (!index.TryGetValue(SurfaceEntranceLayerId, out WorldLayerDefinition entry) ||
+            entry.Kind != WorldLayerKind.Underground)
+            throw new InvalidOperationException(
+                "SurfaceEntranceLayerId must identify an underground layer.");
+
         _index = index;
--- a/WORLD/Layers/WorldLayerController.cs
+++ b/WORLD/Layers/WorldLayerController.cs
@@ -15,5 +15,7 @@
     #region State
-    public string Current { get; private set; } = WorldLayerId.Surface;
+    public string Current => Worlds?.ActiveLayer ?? WorldLayerId.Surface;
     public int Epoch { get; private set; }
-    public CaveWorld Cave { get; private set; }
+    public WorldLayerRuntime Worlds { get; private set; }
+    private CaveWorld TransitionWorld => Worlds.GetUnderground(
+        Current == WorldLayerId.Surface ? Worlds.SurfaceEntranceLayerId : Current);
     public WorldLayerDefinition CurrentDefinition =>
@@ -46,3 +48,2 @@
     private readonly Queue<Node> _addedSurface = new();
-    private WorldLayerMember _underground;
     #endregion
@@ -84,3 +85,3 @@
 // Connect player, streaming, layer presentation and enemy pursuit.
-public void Configure(Node world, Player player, CaveWorld cave)
+public void Configure(Node world, Player player, WorldLayerRuntime worlds)
 {
@@ -88,3 +89,3 @@
     _player = player;
-    Cave = cave;
+    Worlds = worlds;
     _ground = world.GetNode<Node2D>("GroundChunks");
@@ -92,3 +93,2 @@
     _config = WorldConfig.Find(world);
-    _config.GetLayerCatalog().Get(Current);
     _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
@@ -97,11 +97,4 @@
 
-    _underground = WorldLayerMember.Attach(cave.Root, cave.LayerId);
-    _underground.SetActive(false);
-    _underground.SetOpacity(0f);
-    cave.Streaming.ConfigurePlayer(player);
-    cave.Streaming.SetActive(false);
-
     _tree = GetTree();
     _tree.NodeAdded += OnNodeAdded;
-
     CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
@@ -121,3 +114,3 @@
         if (!GodotObject.IsInstanceValid(_player)) return;
-                if (Current == WorldLayerId.Underground1)
+                if (Current != WorldLayerId.Surface)
             _surfaceChunks.RetireUnusedChunks();
@@ -128,6 +121,6 @@
         Health health = _player.GetNode<Health>("Systems/Health");
-        if (!health.IsAlive && Current == WorldLayerId.Underground1)
+        if (!health.IsAlive && Current != WorldLayerId.Surface)
             ReturnToSurface();
 
-        Vector2 tile = Cave.WorldToTile(_player.GlobalPosition);
+        Vector2 tile = TransitionWorld.WorldToTile(_player.GlobalPosition);
 
@@ -136,3 +129,3 @@
         {
-            foreach (CaveHole hole in Cave.Holes)
+            foreach (CaveHole hole in TransitionWorld.Holes)
             {
@@ -141,6 +134,6 @@
                     Mathf.Abs(local.Y) < 1.4f &&
-                    Cave.Streaming.EntryReady(hole))
+                    TransitionWorld.Streaming.EntryReady(hole))
                 {
                     EnterCave(hole);
-                    tile = Cave.WorldToTile(_player.GlobalPosition);
+                    tile = TransitionWorld.WorldToTile(_player.GlobalPosition);
                     break;
@@ -150,5 +143,5 @@
 
-        if (Current == WorldLayerId.Underground1)
-        {
-            CaveHole approach = Cave.TransitionAt(tile);
+        if (Current != WorldLayerId.Surface)
+        {
+            CaveHole approach = TransitionWorld.TransitionAt(tile);
 
@@ -173,3 +166,3 @@
 
-                foreach (CaveHole hole in Cave.Holes)
+                foreach (CaveHole hole in TransitionWorld.Holes)
                 {
@@ -200,3 +193,3 @@
                     _surfaceLoaded = _surfaceChunks.PrepareDestination(
-                        _pendingExit.OutsidePosition(Cave.TileSize));
+                        _pendingExit.OutsidePosition(TransitionWorld.TileSize));
 
@@ -250,3 +243,3 @@
 {
-    if (Current != WorldLayerId.Underground1 || node is WorldLayerMember)
+    if (Current == WorldLayerId.Surface || node is WorldLayerMember)
         return;
@@ -378,8 +371,8 @@
 {
-    if (!Cave.Streaming.EntryReady(hole)) return;
-
-    Vector2 local = hole.Coordinates(Cave.WorldToTile(_player.GlobalPosition));
-    Vector2 entry = Cave.TileToWorld(
+    if (!TransitionWorld.Streaming.EntryReady(hole)) return;
+
+    Vector2 local = hole.Coordinates(TransitionWorld.WorldToTile(_player.GlobalPosition));
+    Vector2 entry = TransitionWorld.TileToWorld(
         hole.TileAt(Mathf.Clamp(local.X, 0f, 0.5f)));
-    if (!Cave.Streaming.IsAvailable(entry)) return;
+    if (!TransitionWorld.Streaming.IsAvailable(entry)) return;
 
@@ -401,6 +394,3 @@
 
-    _underground.SetActive(true);
-    _underground.SetOpacity(1f);
-    Cave.Streaming.SetActive(true);
-    Current = WorldLayerId.Underground1;
+    Worlds.ActivateLayer(TransitionWorld.LayerId);
     Epoch++;
@@ -408,3 +398,3 @@
     CaveEnemyPursuit.Find(this)?.PlayerCrossed(
-        hole, Current, _player);
+        TransitionWorld, hole, Current, _player);
 
@@ -420,3 +410,3 @@
         RestoreSurface(_lastEntry != null
-            ? _lastEntry.OutsidePosition(Cave.TileSize)
+            ? _lastEntry.OutsidePosition(TransitionWorld.TileSize)
             : _player.GlobalPosition);
@@ -428,8 +418,6 @@
 {
-    CaveHole crossed = Cave.NearestSurfaceHole(landing);
+    CaveHole crossed = TransitionWorld.NearestSurfaceHole(landing);
 
     DrainAddedSurface();
-    Cave.Streaming.SetActive(false);
-    _underground.SetActive(false);
-    _underground.SetOpacity(0f);
+    Worlds.ActivateLayer(WorldLayerId.Surface);
 
@@ -446,3 +434,2 @@
 
-    Current = WorldLayerId.Surface;
     Epoch++;
@@ -456,3 +443,3 @@
     CaveEnemyPursuit.Find(this)?.PlayerCrossed(
-        crossed, Current, _player);
+        TransitionWorld, crossed, Current, _player);
 
@@ -465,3 +452,3 @@
     {
-        Vector2 centre = hole.OutsidePosition(Cave.TileSize);
+        Vector2 centre = hole.OutsidePosition(TransitionWorld.TileSize);
         List<Obstacle> obstacles = WorldPlacement.CollectObstacles(_objects);
@@ -517,6 +504,6 @@
         float target = 1f;
-        if (Current == WorldLayerId.Underground1)
-        {
-            Vector2 tile = Cave.WorldToTile(_player.GlobalPosition);
-            CaveHole ramp = Cave.TransitionAt(tile);
+        if (Current != WorldLayerId.Surface)
+        {
+            Vector2 tile = TransitionWorld.WorldToTile(_player.GlobalPosition);
+            CaveHole ramp = TransitionWorld.TransitionAt(tile);
             float descent = ramp != null
@@ -529,6 +516,6 @@
             float reference = ramp != null
-                ? Mathf.Lerp(ramp.RimHeight, Cave.RimHeight, descent)
-                : Cave.RimHeight;
+                ? Mathf.Lerp(ramp.RimHeight, TransitionWorld.RimHeight, descent)
+                : TransitionWorld.RimHeight;
             _camera.Position = _cameraPosition + Vector2.Down *
-                (reference - Cave.Elevation.SampleWorldHeight(_player.GlobalPosition));
+                (reference - TransitionWorld.Elevation.SampleWorldHeight(_player.GlobalPosition));
         }
@@ -551,7 +538,10 @@
 
-        foreach (CaveHole hole in Cave.Holes)
+        foreach (CaveWorld world in Worlds.UndergroundWorlds)
+        foreach (CaveHole hole in world.Holes)
         {
             if (!GodotObject.IsInstanceValid(hole.Marker)) continue;
-            bool visible = Current == WorldLayerId.Surface ||
-                hole == _pendingExit || (!_entryDeparted && hole == _lastEntry);
+            bool visible = Current == WorldLayerId.Surface
+                ? world.LayerId == Worlds.SurfaceEntranceLayerId
+                : world.LayerId == Current &&
+                    (hole == _pendingExit || (!_entryDeparted && hole == _lastEntry));
             if (hole.Marker.Visible != visible)
@@ -564,5 +554,5 @@
 
-        if (Current == WorldLayerId.Underground1)
+        if (Current != WorldLayerId.Surface)
             _status.Text =
-                $"{CurrentDefinition.DisplayName} | {Cave.Streaming.ReadyCount}/{Cave.Streaming.LoadedCount} chunks\n" +
+                $"{CurrentDefinition.DisplayName} | {TransitionWorld.Streaming.ReadyCount}/{TransitionWorld.Streaming.LoadedCount} chunks\n" +
                 (_pendingExit == null ? "Explore the connected network" :
@@ -572,5 +562,5 @@
         {
-            CaveHole nearest = Cave.NearestSurfaceHole(_player.GlobalPosition);
+            CaveHole nearest = TransitionWorld.NearestSurfaceHole(_player.GlobalPosition);
             _status.Text = $"{CurrentDefinition.DisplayName} | Nearest hole: {nearest?.Id}\n" +
-                (Cave.Streaming.EntryReady(nearest)
+                (TransitionWorld.Streaming.EntryReady(nearest)
                     ? "Cave entrance ready" : "Preparing underground entrance...");
@@ -585,3 +575,3 @@
     {
-        if (Current != WorldLayerId.Underground1 || delta <= 0) return velocity;
+        if (Current == WorldLayerId.Surface || delta <= 0) return velocity;
 
@@ -608,6 +598,6 @@
             Vector2 point = position + motion * ((float)i / steps);
-            if (!Cave.Streaming.IsAvailable(point)) return false;
-
-            Vector2 tile = Cave.WorldToTile(point);
-            foreach (CaveHole hole in Cave.Holes)
+            if (!TransitionWorld.Streaming.IsAvailable(point)) return false;
+
+            Vector2 tile = TransitionWorld.WorldToTile(point);
+            foreach (CaveHole hole in TransitionWorld.Holes)
             {
@@ -638,5 +628,10 @@
     {
-        WorldLayerController controller = Find(owner);
-        if (controller != null && WorldLayerMember.For(owner) == WorldLayerId.Underground1)
-            return controller.Cave.Elevation.SampleWorldHeight(point);
+        string layer = WorldLayerMember.For(owner);
+        if (layer != WorldLayerId.Surface)
+        {
+            WorldLayerRuntime runtime = WorldLayerRuntime.Find(owner)
+                ?? throw new System.InvalidOperationException(
+                    $"No layer runtime for '{layer}'.");
+            return runtime.GetUnderground(layer).Elevation.SampleWorldHeight(point);
+        }
 
@@ -651,5 +646,4 @@
     {
-        WorldLayerController controller = Find(context);
-        return controller?.Current == WorldLayerId.Underground1
-            ? controller.Cave.Objects : surfaceRoot;
+        WorldLayerRuntime runtime = WorldLayerRuntime.Find(context);
+        return runtime == null ? surfaceRoot : runtime.ObjectsFor(runtime.ActiveLayer);
     }
--- a/WORLD/Layers/WorldLayerDefinition.cs
+++ b/WORLD/Layers/WorldLayerDefinition.cs
@@ -21,2 +21,3 @@
     [Export] public CaveGenerationSettings CaveSettings { get; set; }
+    [Export] public float FloorElevation { get; set; }
     #endregion
@@ -37,5 +38,5 @@
             if (Id != WorldLayerId.Surface || DepthIndex != 0 ||
-                CaveSettings != null)
+                CaveSettings != null || FloorElevation != 0f)
                 throw new InvalidOperationException(
-                    "The surface layer must use ID 'surface', depth 0 and no cave settings.");
+                    "The surface requires ID 'surface', depth 0, elevation 0 and no cave settings.");
         }
@@ -44,5 +45,6 @@
             if (Id == WorldLayerId.Surface || DepthIndex < 1 ||
-                CaveSettings == null)
+                CaveSettings == null || !float.IsFinite(FloorElevation) ||
+                FloorElevation >= 0f)
                 throw new InvalidOperationException(
-                    $"Underground layer '{Id}' requires a positive depth and cave settings.");
+                    $"Underground layer '{Id}' requires a positive depth, negative elevation and cave settings.");
         }
--- a/WORLD/Navigation/WorldNavigation.cs
+++ b/WORLD/Navigation/WorldNavigation.cs
@@ -101,3 +101,3 @@
     {
-        AddToGroup(Cave == null ? "world_navigation" : "cave_navigation");
+        AddToGroup("world_navigation");
         _config = WorldConfig.Find(this);
@@ -140,6 +140,14 @@
         if (actor == null || !actor.IsInsideTree()) return null;
-        string group = WorldLayerMember.For(actor) == WorldLayerId.Underground1
-            ? "cave_navigation" : "world_navigation";
-
-        return actor.GetTree().GetFirstNodeInGroup(group) as WorldNavigation;
+        return ForLayer(actor, WorldLayerMember.For(actor));
+    }
+
+    // =========================================================
+    // Resolve navigation by exact identity across all registered layer services.
+    public static WorldNavigation ForLayer(Node context, string layer)
+    {
+        if (context == null || !context.IsInsideTree()) return null;
+        foreach (Node node in context.GetTree().GetNodesInGroup("world_navigation"))
+            if (node is WorldNavigation navigation && navigation.Layer == layer)
+                return navigation;
+        return null;
     }
--- a/WORLD/Streaming/GenerationMetadataLease.cs
+++ b/WORLD/Streaming/GenerationMetadataLease.cs
@@ -15,3 +15,4 @@
     public static void Attach(
-        Node owner, InfiniteWorldGeneration generation, Rect2 area)
+        Node owner, InfiniteWorldGeneration generation, Rect2 area,
+        string layer = WorldLayerId.Surface)
     {
@@ -24,3 +25,3 @@
             Name = "GenerationMetadata",
-            _lease = generation.PinArea(area)
+            _lease = generation.PinArea(area, layer)
         };
--- a/WORLD/Streaming/InfiniteWorldGeneration.cs
+++ b/WORLD/Streaming/InfiniteWorldGeneration.cs
@@ -17,5 +17,5 @@
     private bool _initialized;
 
-    public CaveWorld Cave { get; private set; }
+    public WorldLayerRuntime Worlds { get; private set; }
     #endregion
 
@@ -50,20 +50,17 @@
         {
             WorldLayerDefinition definition =
-                config.GetLayerCatalog().Get(WorldLayerId.Underground1);
+                config.GetLayerCatalog().Get(config.GetLayerCatalog().SurfaceEntranceLayerId);
             CaveGenerationSettings settings = definition.CreateCaveSettings();
 
-            _planner = new CaveEntrancePlanner(_world, chunks, settings);
-            Cave = new CaveWorld { Name = "CaveWorld", Planner = _planner };
-            AddChild(Cave);
-
-            Node2D ground = _world.GetNode<Node2D>("GroundChunks");
-            Cave.Build(
-                ground.GlobalPosition, chunks.TileSize, 0f,
-                chunks.WorldSeed, _planner.Settings, Array.Empty<CaveHole>());
+            _planner = new CaveEntrancePlanner(
+                _world, chunks, settings, definition.FloorElevation);
+            Player player = _world.GetNode<Player>("WorldObjects/Player");
+            Worlds = new WorldLayerRuntime { Name = "LayerWorlds" };
+            AddChild(Worlds);
+            Worlds.Initialize(_world, player, chunks, _planner);
 
             WorldLayerController layers = new() { Name = "WorldLayers" };
             AddChild(layers);
-            layers.Configure(
-                _world, _world.GetNode<Player>("WorldObjects/Player"), Cave);
+            layers.Configure(_world, player, Worlds);
         }
 
@@ -84,5 +81,6 @@
     // =========================================================
     // Finish local water and entrance decisions before geometry or props sample them.
-    public IEnumerable<int> PrepareArea(Rect2 area)
+    public IEnumerable<int> PrepareArea(
+        Rect2 area, string layer = WorldLayerId.Surface)
     {
         if (!_initialized)
@@ -90,9 +88,16 @@
                 "Infinite generation did not initialize successfully.");
 
+        if (layer != WorldLayerId.Surface &&
+            layer != Worlds?.SurfaceEntranceLayerId)
+        {
+            Worlds?.GetUnderground(layer);
+            yield break;
+        }
+
         foreach (int step in _basins.PrepareArea(area.Grow(2f)))
             yield return step;
 
         if (_planner != null)
-            foreach (int step in _planner.PrepareArea(area, Cave))
+            foreach (int step in _planner.PrepareArea(area, Worlds.SurfaceUnderground))
                 yield return step;
     }
@@ -101,9 +106,17 @@
         // =========================================================
     // Hold both surface and entrance metadata until the owning chunk retires.
-    public IDisposable PinArea(Rect2 area)
+    public IDisposable PinArea(
+        Rect2 area, string layer = WorldLayerId.Surface)
     {
         if (!_initialized)
             throw new InvalidOperationException(
                 "Infinite generation did not initialize successfully.");
+
+        if (layer != WorldLayerId.Surface &&
+            layer != Worlds?.SurfaceEntranceLayerId)
+        {
+            Worlds?.GetUnderground(layer);
+            return new GenerationLease(() => { });
+        }
 
         IDisposable water = _basins.PinArea(area.Grow(4f));
--- a/WORLD/Layers/Definitions/Underground1/Underground1.tres
+++ b/WORLD/Layers/Definitions/Underground1/Underground1.tres
@@ -11,2 +11,3 @@
 DepthIndex = 1
+FloorElevation = -160.0
 Kind = 1
--- a/WORLD/Layers/WorldLayers.tres
+++ b/WORLD/Layers/WorldLayers.tres
@@ -1,2 +1,2 @@
-[gd_resource type="Resource" script_class="WorldLayerCatalog" load_steps=5 format=3]
+[gd_resource type="Resource" script_class="WorldLayerCatalog" load_steps=6 format=3]
 
@@ -7,4 +7,6 @@
 
+[ext_resource type="Resource" path="res://WORLD/Layers/Definitions/Underground2/Underground2.tres" id="5_deep"]
+
 [resource]
 script = ExtResource("1_catalog")
-Layers = Array[ExtResource("2_definition")]([ExtResource("3_surface"), ExtResource("4_underground")])
+Layers = Array[ExtResource("2_definition")]([ExtResource("3_surface"), ExtResource("4_underground"), ExtResource("5_deep")])
--- a/WORLD/Generation/Caves/README.md
+++ b/WORLD/Generation/Caves/README.md
@@ -21,5 +21,13 @@
 
-Only the existing surface and first cave are instantiated currently.
-Independent additional layer worlds and generalized connections are
-planned for passes 2 and 3.
+Pass 2 adds WorldLayerRuntime: independent underground service instances,
+indexed by exact layer ID. Upper Caverns and Deep Caverns each own their
+generator, seed, elevation, chunks, objects and navigation. Dormant layers
+build no chunks until requested. Definitions own FloorElevation.
+
+The existing surface ramps still connect only to the catalog's
+SurfaceEntranceLayerId. Deeper playable connections are pass 3 work.
+Underground chunk metadata is layer-aware; deeper layers do not prepare
+surface basin or entrance metadata. Layer seeds now include a stable
+identity hash, so previous underground layouts may change for the same
+world seed. Surface generation is unchanged.
 
@@ -212,6 +220,6 @@
 
-See NOTES/OngoingWork/README.md for the migration handoff.
+See NOTES/OngoingWork/LayerMigration.md for the migration handoff.
 Transition profiling and a shared generation scheduler remain separate
 future work. Additional playable underground layers are not implemented
-by pass 1.
+until pass 3.
 
--- a/NOTES/OngoingWork/LayerMigration.md
+++ b/NOTES/OngoingWork/LayerMigration.md
@@ -46,5 +46,7 @@
 - Chunk streaming around the player.
 
-The current layer identity is a fixed Surface/Cave distinction.
+Layer identities are stable strings resolved through WorldLayerCatalog.
+WorldLayerRuntime now owns independent underground service instances.
+Existing ramps still connect the surface to Upper Caverns only.
 
 The additional depths described below are planned work, not implemented
@@ -53,5 +55,5 @@
 ## Pass 1 \u2014 Layer Identities and Definitions
 
-Status: Next pass. Code not yet applied.
+Status: Pass 1 applied. Confirm traversal tests before continuing.
 
 Introduce extensible layer identities and data-driven layer definitions.
@@ -87,5 +89,5 @@
 ## Pass 2 \u2014 Independent Layer Worlds
 
-Status: Planned.
+Status: Code applied; build and traversal verification pending.
 
 Make world ownership and runtime services work with explicit layer IDs.
@@ -118,4 +120,14 @@
 - Existing surface/cave behaviour still works.
 - Obsolete single-cave assumptions are removed from migrated systems.
+
+Pass 2 adds WorldLayerRuntime.cs and Underground2 definition/settings.
+Each underground definition owns FloorElevation. The global
+CaveFloorElevation export is removed. SurfaceEntranceLayerId in the
+catalog selects the destination of existing surface ramps.
+
+Navigation, elevation, shadows, drops and wrecks resolve exact layer IDs.
+Dormant depths do not stream until activated or explicitly preloaded.
+Stable identity hashes separate their seeds; existing cave layouts can
+change for the same world seed. Deep Caverns is not yet reachable.
 
 ## Pass 3 \u2014 Connections Between Layers
--- /dev/null
+++ b/WORLD/Layers/WorldLayerRuntime.cs
@@ -0,0 +1,157 @@
+// Owns independent underground runtime worlds and resolves them by exact layer ID.
+// Dormant layers own services but build no chunks until activated or preloaded.
+using Godot;
+using System;
+using System.Collections.Generic;
+
+public partial class WorldLayerRuntime : Node
+{
+    #region State
+    public string ActiveLayer { get; private set; } = WorldLayerId.Surface;
+    public string SurfaceEntranceLayerId { get; private set; }
+    public CaveWorld SurfaceUnderground => GetUnderground(SurfaceEntranceLayerId);
+
+    private Node2D _surfaceObjects;
+    private ChunkController _surfaceChunks;
+    private readonly Dictionary<string, CaveWorld> _underground =
+        new(StringComparer.Ordinal);
+    public IEnumerable<CaveWorld> UndergroundWorlds => _underground.Values;
+    #endregion
+
+    #region Construction
+    // =========================================================
+    // Register one runtime owner for this gameplay world.
+    public override void _EnterTree()
+    {
+        AddToGroup("world_layer_runtime");
+        SetProcess(false);
+    }
+
+    // =========================================================
+    // Build service instances only; chunk construction remains demand-driven.
+    public void Initialize(Node world, Player player,
+        ChunkController chunks, CaveEntrancePlanner surfacePlanner)
+    {
+        WorldLayerCatalog catalog = WorldConfig.Find(world).GetLayerCatalog();
+        SurfaceEntranceLayerId = catalog.SurfaceEntranceLayerId;
+        _surfaceObjects = world.GetNode<Node2D>("WorldObjects");
+        _surfaceChunks = chunks;
+        Node2D ground = world.GetNode<Node2D>("GroundChunks");
+
+        foreach (WorldLayerDefinition definition in catalog.Layers)
+        {
+            if (definition.Kind != WorldLayerKind.Underground) continue;
+            bool surfaceDestination = definition.Id == SurfaceEntranceLayerId;
+
+            CaveWorld cave = new()
+            {
+                Name = definition.Id,
+                LayerId = definition.Id,
+                Planner = surfaceDestination ? surfacePlanner : null
+            };
+            AddChild(cave);
+            cave.Build(ground.GlobalPosition, chunks.TileSize, 0f,
+                SeedFor(chunks.WorldSeed, definition.Id),
+                surfaceDestination ? surfacePlanner.Settings
+                    : definition.CreateCaveSettings(),
+                Array.Empty<CaveHole>());
+            cave.Streaming.ConfigurePlayer(player);
+            cave.SetActive(false);
+            _underground.Add(definition.Id, cave);
+        }
+
+        ActivateLayer(WorldLayerId.Surface);
+        GD.Print($"[Layers] {_underground.Count} independent underground worlds ready.");
+    }
+
+    // =========================================================
+    // Derive deterministic seeds without relying on randomized string hashes.
+    private static uint SeedFor(uint worldSeed, string id)
+    {
+        unchecked
+        {
+            uint hash = 2166136261u;
+            foreach (char character in id)
+                hash = (hash ^ character) * 16777619u;
+            return worldSeed ^ hash;
+        }
+    }
+    #endregion
+
+    #region Queries
+    // =========================================================
+    // Find the runtime registry in the current gameplay scene.
+    public static WorldLayerRuntime Find(Node context)
+    {
+        if (context == null || !context.IsInsideTree()) return null;
+        return context.GetTree().GetFirstNodeInGroup(
+            "world_layer_runtime") as WorldLayerRuntime;
+    }
+
+    // =========================================================
+    // Resolve a constructed underground world without falling back to another.
+    public CaveWorld GetUnderground(string id)
+    {
+        return _underground.TryGetValue(id, out CaveWorld cave)
+            ? cave : throw new InvalidOperationException(
+                $"No underground runtime exists for '{id}'.");
+    }
+
+    // =========================================================
+    // Query optional ownership without creating any world or chunk.
+    public CaveWorld TryGetUnderground(string id)
+    {
+        return id != null && _underground.TryGetValue(id, out CaveWorld cave)
+            ? cave : null;
+    }
+
+    // =========================================================
+    // Select the exact layer's world-object root.
+    public Node2D ObjectsFor(string id)
+    {
+        return id == WorldLayerId.Surface
+            ? _surfaceObjects : GetUnderground(id).Objects;
+    }
+
+    // =========================================================
+    // Check completed terrain in the requested layer.
+    public bool IsAvailable(string id, Vector2 point, float clearance = 0f)
+    {
+        return id == WorldLayerId.Surface
+            ? _surfaceChunks.IsNavigationPointAvailable(point, clearance)
+            : GetUnderground(id).Streaming.IsAvailable(point, clearance);
+    }
+    #endregion
+
+    #region Activation
+    // =========================================================
+    // Activate one underground world and disable all other underground collisions.
+    public void ActivateLayer(string id)
+    {
+        if (id != WorldLayerId.Surface) GetUnderground(id);
+        ActiveLayer = id;
+
+        foreach (CaveWorld cave in _underground.Values)
+        {
+            cave.SetActive(cave.LayerId == id);
+            cave.Streaming.SetEntrancePreloading(
+                id == WorldLayerId.Surface &&
+                cave.LayerId == SurfaceEntranceLayerId);
+        }
+    }
+
+    // =========================================================
+    // Request destination preparation without revealing or activating its world.
+    public void Preload(string id, Vector2 point)
+    {
+        GetUnderground(id).Streaming.RequestPreload(point);
+    }
+
+    // =========================================================
+    // Release temporary preparation once a connection no longer needs it.
+    public void CancelPreload(string id)
+    {
+        GetUnderground(id).Streaming.CancelPreload();
+    }
+    #endregion
+}
--- /dev/null
+++ b/WORLD/Layers/Definitions/Underground2/Underground2.tres
@@ -0,0 +1,15 @@
+[gd_resource type="Resource" script_class="WorldLayerDefinition" load_steps=4 format=3]
+
+[ext_resource type="Script" path="res://WORLD/Layers/WorldLayerDefinition.cs" id="1_definition"]
+[ext_resource type="Resource" path="res://WORLD/Generation/Caves/Biomes/CaveBiomeCatalog.tres" id="2_biomes"]
+[ext_resource type="Resource" path="res://WORLD/Layers/Definitions/Underground2/Underground2Generation.tres" id="3_settings"]
+
+[resource]
+script = ExtResource("1_definition")
+Id = "underground_2"
+DisplayName = "Deep Caverns"
+DepthIndex = 2
+Kind = 1
+FloorElevation = -480.0
+Biomes = ExtResource("2_biomes")
+CaveSettings = ExtResource("3_settings")
--- /dev/null
+++ b/WORLD/Layers/Definitions/Underground2/Underground2Generation.tres
@@ -0,0 +1,17 @@
+[gd_resource type="Resource" script_class="CaveGenerationSettings" load_steps=2 format=3]
+
+[ext_resource type="Script" path="res://WORLD/Generation/Caves/CaveGenerationSettings.cs" id="1_settings"]
+
+[resource]
+script = ExtResource("1_settings")
+SeedOffset = 73129
+EntranceTunnelLengthTiles = 14
+CellSpacingTiles = 32
+ChamberRadiusRange = Vector2(4, 8)
+TunnelWidthTiles = 3.0
+ExtraConnectionChance = 0.25
+ChunkSize = 8
+LoadRadiusChunks = 3
+RetainRadiusChunks = 4
+BuildBudgetMs = 1.0
+RetireChunksPerFrame = 1
'@
# Decode ASCII-safe Unicode in the embedded patch (Windows PowerShell 5 compatible).
$patch = [regex]::Replace($patch, '\\u([0-9a-fA-F]{4})',
    [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        return [string][char][Convert]::ToInt32($match.Groups[1].Value, 16)
    })
$lines = $patch.Replace("`r`n", "`n").Replace("`r", "`n").Split([char]10)
$i = 0
while ($i -lt $lines.Length) {
    if (-not $lines[$i].StartsWith('--- ')) { $i++; continue }
    $created = $lines[$i] -eq '--- /dev/null'
    $i++
    if ($i -ge $lines.Length -or -not $lines[$i].StartsWith('+++ b/')) {
        throw 'Invalid embedded file header.'
    }
    $path = $lines[$i].Substring(6)
    $full = Join-Path $root $path
    if ($pending.Contains($path)) { throw "Duplicate patch file: $path" }
    if ($created) {
        if (Test-Path $full) { throw "New file already exists: $path. No files changed." }
        $source = ''
        $original[$path] = $null
    } else {
        if (-not (Test-Path $full)) { throw "Missing file: $path. No files changed." }
        $original[$path] = [System.IO.File]::ReadAllBytes($full)
        $source = Normalize-Source ([System.IO.File]::ReadAllText($full))
    }
    $i++
    while ($i -lt $lines.Length -and -not $lines[$i].StartsWith('--- ')) {
        if (-not $lines[$i].StartsWith('@@ ')) { $i++; continue }
        $i++
        $oldLines = New-Object 'System.Collections.Generic.List[string]'
        $newLines = New-Object 'System.Collections.Generic.List[string]'
        while ($i -lt $lines.Length -and
            -not $lines[$i].StartsWith('@@ ') -and
            -not $lines[$i].StartsWith('--- ')) {
            $line = $lines[$i]
            if ($line.Length -gt 0) {
                switch ($line.Substring(0, 1)) {
                    ' ' { $oldLines.Add($line.Substring(1)); $newLines.Add($line.Substring(1)) }
                    '-' { $oldLines.Add($line.Substring(1)) }
                    '+' { $newLines.Add($line.Substring(1)) }
                    default { throw "Invalid patch line in $path" }
                }
            }
            $i++
        }
        $before = ''
        $after = ''
        if ($oldLines.Count -gt 0) { $before = ($oldLines -join "`n") + "`n" }
        if ($newLines.Count -gt 0) { $after = ($newLines -join "`n") + "`n" }
        if ($created) {
            $source += $after
        } else {
            if ($before.Length -eq 0) { throw "Empty match in $path" }
            $at = $source.IndexOf($before, [System.StringComparison]::Ordinal)
            if ($at -lt 0 -or $source.IndexOf($before,
                $at + $before.Length, [System.StringComparison]::Ordinal) -ge 0) {
                throw "Expected code missing or ambiguous in $path. No files changed."
            }
            $source = $source.Substring(0, $at) + $after +
                $source.Substring($at + $before.Length)
        }
    }
    $pending[$path] = $source
}
if ($pending.Count -ne 27) { throw 'Incomplete embedded patch. No files changed.' }

$written = New-Object 'System.Collections.Generic.List[string]'
try {
    foreach ($path in $pending.Keys) {
        $full = Join-Path $root $path
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full)) | Out-Null
        $written.Add($path)
        [System.IO.File]::WriteAllText($full, $pending[$path], $utf8)
    }
} catch {
    foreach ($path in $written) {
        $full = Join-Path $root $path
        if ($null -eq $original[$path]) {
            if (Test-Path $full) { Remove-Item $full }
        } else {
            [System.IO.File]::WriteAllBytes($full, $original[$path])
        }
    }
    throw
}
$previousScript = Join-Path $root 'MigrateWorldLayersPass1.ps1'
if (Test-Path $previousScript) { Remove-Item $previousScript }
Write-Host 'Pass 2 applied: 27 files updated/created. No Git command needed.'
Write-Host 'Build the C# project, then test Surface -> Upper Caverns -> Surface.'
Write-Host 'Deep Caverns is registered but has no entrance until pass 3.'
Write-Host 'Delete this one-time script after successful testing.'
