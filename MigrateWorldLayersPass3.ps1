# One-time World Layer Migration pass 3. Godot must be closed.
# Based on pushed commit 375b968. Preflight all edits, then apply together.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'project.godot'))) {
    throw 'Save this script beside project.godot, then run it again.'
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$pending = [ordered]@{}
$original = @{}
function Normalize-Source([string] $text) {
    return $text.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd([char]10) + "`n"
}
$patch = @'
--- a/CONFIG/GlobalConfig.cs
+++ b/CONFIG/GlobalConfig.cs
@@ -36,4 +36,13 @@
 public float MinimumCaveHoleDistanceTiles { get; set; } = 96f;
 #endregion
+
+    #region Layer Transitions
+    [ExportGroup("LAYER TRANSITIONS")]
+    // Generation settings: restart the world after changing either multiplier.
+    [Export(PropertyHint.Range, "0.5,8,0.05")]
+    public float EntranceLengthMultiplier { get; set; } = 1f;
+    [Export(PropertyHint.Range, "0.5,4,0.05")]
+    public float EntranceSlopeMultiplier { get; set; } = 1.1f;
+    #endregion
 
 	#region Visibility
--- /dev/null
+++ b/DEBUG/DeepCavernsTest/DeepCavernsTest.cs
@@ -0,0 +1,99 @@
+// Optional world_infinite test placement for Upper Caverns -> Deep Caverns.
+// Remove this scene node and restart to remove the test connection entirely.
+using Godot;
+using System;
+
+public partial class DeepCavernsTest : Node
+{
+    #region Configuration
+    [ExportGroup("Test Connection")]
+    [Export] public bool Enabled { get; set; } = true;
+    [Export] public string UpperLayerId { get; set; } = "underground_1";
+    [Export] public string LowerLayerId { get; set; } = "underground_2";
+    [Export] public float BaseTunnelLengthTiles { get; set; } = 24f;
+    [Export] public float ApproachExtraTiles { get; set; } = 4f;
+    #endregion
+
+    #region State
+    private WorldLayerController _layers;
+    private WorldLayerConnection _connection;
+    private double _timer;
+    #endregion
+
+    #region Lifecycle
+    // =========================================================
+    // Keep placement separate from core generation and optional per scene.
+    public override void _Ready() { SetProcess(Enabled); }
+
+    // =========================================================
+    // Add the test near the first surface entrance actually used in this run.
+    public override void _Process(double delta)
+    {
+        _timer -= delta;
+        if (_timer > 0) return;
+        _timer = 0.2;
+        _layers ??= WorldLayerController.Find(this);
+        if (_layers?.Worlds == null) return;
+        _layers.LayerChanged += OnLayerChanged;
+        SetProcess(false);
+        TryPlace();
+    }
+
+    // =========================================================
+    // React to the shared layer notification instead of polling throughout gameplay.
+    private void OnLayerChanged(string from, string to, WorldLayerConnection connection)
+    {
+        TryPlace();
+    }
+
+    // =========================================================
+    // Reserve both sides before generating the test branch's terrain.
+    private void TryPlace()
+    {
+        if (!Enabled || _connection != null || _layers.Current != UpperLayerId ||
+            _layers.LastSurfaceConnection == null) return;
+        if (!float.IsFinite(BaseTunnelLengthTiles) || !float.IsFinite(ApproachExtraTiles) ||
+            ApproachExtraTiles < 3f)
+            throw new InvalidOperationException("Invalid DeepCavernsTest settings.");
+        WorldLayerRuntime runtime = _layers.Worlds;
+        CaveWorld upper = runtime.GetUnderground(UpperLayerId);
+        CaveWorld lower = runtime.GetUnderground(LowerLayerId);
+        WorldLayerConnection surface = _layers.LastSurfaceConnection;
+        if (surface.LowerLayer != UpperLayerId)
+            throw new InvalidOperationException("Test upper layer must join the selected surface entrance.");
+        Vector2 anchor = upper.Generator.RoomCentre(surface.AnchorCell);
+        // Place outside the existing chamber, away from its surface ramp.
+        Vector2 direction = Vector2.Right;
+        Vector2 mouth = anchor + direction * Mathf.Ceil(
+            upper.Settings.MaximumChamberRadius() + ApproachExtraTiles);
+        float length = WorldLayerConnection.LengthFor(WorldConfig.Find(this), BaseTunnelLengthTiles);
+        Vector2 end = mouth + direction * length;
+        Vector2I lowerAnchor = lower.Generator.RoomCell(end + direction * (
+            lower.Settings.MaximumChamberRadius() + lower.Settings.CellSpacingTiles + 8f));
+        float height = upper.Generator.VertexHeight(mouth);
+        _connection = new WorldLayerConnection($"TEST_DEEP_{surface.Id}",
+            UpperLayerId, LowerLayerId, mouth, direction, upper.TileToWorld(mouth),
+            height, length, lowerAnchor, anchor);
+        runtime.Connections.Register(_connection, rebuild: true);
+        if (_connection.Marker != null)
+        {
+            _connection.Marker.ShowDebugMarker = true;
+            _connection.Marker.QueueRedraw();
+        }
+        GD.Print($"[DeepCavernsTest] Follow the chamber's right-hand passage to " +
+            $"{lower.Definition.DisplayName}. Connection {_connection.Id}, " +
+            $"mouth {_connection.UpperPosition}, length {length:0.00} tiles.");
+        SetProcess(false);
+    }
+
+    // =========================================================
+    // Release the debug record when the optional node is removed.
+    public override void _ExitTree()
+    {
+        if (GodotObject.IsInstanceValid(_layers)) _layers.LayerChanged -= OnLayerChanged;
+        if (_connection != null && GodotObject.IsInstanceValid(_layers) &&
+            GodotObject.IsInstanceValid(_layers.Worlds) && _layers.Worlds.IsInsideTree())
+            _layers.Worlds.Connections.Remove(_connection);
+    }
+    #endregion
+}
--- a/DEBUG/Map/WorldMapPoiPreview.cs
+++ b/DEBUG/Map/WorldMapPoiPreview.cs
@@ -116,3 +116,3 @@
         if (GodotObject.IsInstanceValid(_cave))
-            foreach (CaveHole hole in _cave.Holes)
+            foreach (WorldLayerConnection hole in _cave.Connections)
                 if (playerTile.DistanceSquaredTo(hole.MouthTile) <= squared)
--- a/ENTITIES/Core/Entity.cs
+++ b/ENTITIES/Core/Entity.cs
@@ -435,3 +435,3 @@
 
-        CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(this);
+        WorldLayerPursuit pursuit = WorldLayerPursuit.Find(this);
         return pursuit != null &&
--- a/NOTES/OngoingWork/LayerMigration.md
+++ b/NOTES/OngoingWork/LayerMigration.md
@@ -41,6 +41,7 @@
 - Pass 2: Implemented; not fully tested yet.
-- Pass 3: Planned; not implemented.
+- Pass 3: Implemented; build and traversal verification pending.
 
 These statuses describe migration passes, not playable depth levels.
-Deep Caverns is registered but is not yet reachable through gameplay.
+Deep Caverns is reachable through the optional DeepCavernsTest connection.
+Natural placement of deeper entrances is separate future work.
 
@@ -58,3 +59,3 @@
 Pass 2 registers Upper Caverns and Deep Caverns as separate runtime worlds.
-Existing ramps still connect the surface to Upper Caverns only.
+Surface holes and deeper entrances now use shared bidirectional connections.
 
@@ -134,3 +135,3 @@
 Adding a layer definition does not automatically create an entrance
-or implement its content. Deep Caverns is not yet reachable.
+or implement its content. Pass 3 adds an optional debug connection to Deep Caverns.
 
@@ -154,3 +155,3 @@
 
-Status: Planned; not implemented.
+Status: Implemented; build and traversal verification pending.
 
@@ -177,2 +178,31 @@
 Different connection types may receive their own artwork and behaviour later.
+
+### Implemented Structure
+
+- WorldLayerConnection replaces CaveHole; it records both endpoint layer IDs.
+- WorldLayerConnections indexes one shared record into both relevant cave worlds.
+- WorldLayerLanding prepares and checks the exact destination's terrain.
+- WorldLayerSurface owns surface pausing and incremental registration.
+- WorldLayerController coordinates shared crossing and presentation.
+- WorldLayerPursuit replaces CaveEnemyPursuit and follows known connections.
+- CaveGenerator reserves upper approaches and lower ramps/landing connectors.
+
+The optional DeepCavernsTest node lives directly in world_infinite.
+Its isolated script lives under DEBUG/DeepCavernsTest/. It creates one test
+connection near the first surface entrance actually used during that run.
+Follow the first chamber's right-hand passage to its labelled doorway.
+Remove or disable the node and restart to remove this test placement.
+The registry does not automatically scatter deeper entrances yet.
+
+GlobalConfig contains LAYER TRANSITIONS:
+- EntranceLengthMultiplier = 1.0
+- EntranceSlopeMultiplier = 1.1
+
+Final ramp length = base length * length multiplier / slope multiplier.
+With fixed endpoint elevations, longer ramps are shallower. Greater layer
+depth comes from the layer definition's FloorElevation. Restart the world
+after editing generation controls. Final lengths must be 6 to 512 tiles.
+
+CaveHole.cs and CaveEnemyPursuit.cs are removed rather than left as adapters.
+A new shared generation scheduler and save persistence are not part of this pass.
 
@@ -212,3 +242,4 @@
 
-Current next step: finish Pass 2 verification, then prepare Pass 3.
+Current next step: build and test the complete Surface -> Upper Caverns ->
+Deep Caverns round trip, including reversal halfway down both ramps.
 
--- a/WORLD/Chunks/ChunkController.cs
+++ b/WORLD/Chunks/ChunkController.cs
@@ -301,3 +301,3 @@
 	int steps = 0;
-	CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(this);
+	WorldLayerPursuit pursuit = WorldLayerPursuit.Find(this);
 
--- a/WORLD/Generation/Caves/CaveEntrance.cs
+++ b/WORLD/Generation/Caves/CaveEntrance.cs
@@ -1,2 +1,2 @@
-// Draws a temporary stone doorway over the existing cave entrance.
+// Draws shared doorway artwork for surface and underground layer connections.
 // Artwork is separate from traversal, collision and surface clearance.
@@ -17,3 +17,3 @@
     public CaveWorld World { get; set; }
-    public CaveHole Hole { get; set; }
+    public WorldLayerConnection Connection { get; set; }
 
@@ -28,7 +28,7 @@
     {
-        if (World == null || Hole == null) return;
+        if (World == null || Connection == null) return;
 
-        Vector2 centre = Vector2.Up * Hole.RimHeight;
+        Vector2 centre = Vector2.Up * Connection.RimHeight;
         Vector2 direction = IsoGrid.TileToWorld(
-            Hole.Direction, World.TileSize).Normalized();
+            Connection.Direction, World.TileSize).Normalized();
 
@@ -184,3 +184,4 @@
             centre + new Vector2(-40f, -ArchHeight - 16f),
-            $"HOLE {Hole.Id}",
+            $"TO {WorldConfig.Find(this).GetLayerCatalog().Get(WorldLayerController.Find(this)?.Current == Connection.LowerLayer
+                ? Connection.UpperLayer : Connection.LowerLayer).DisplayName}",
             HorizontalAlignment.Left, -1f, 16, colour);
--- a/WORLD/Generation/Caves/CaveGenerator.cs
+++ b/WORLD/Generation/Caves/CaveGenerator.cs
@@ -75,5 +75,14 @@
     Vector2 point = new(tile.X, tile.Y);
 
-    foreach (CaveHole hole in _world.NearbyHoles(point))
+    foreach (WorldLayerConnection departure in _world.Departures)
+    {
+        Vector2 local = departure.Coordinates(point);
+        if (local.X >= -2f && local.X <= 1f && Mathf.Abs(local.Y) <= 3f)
+            return local.X <= 0f && Mathf.Abs(local.Y) <= 1f;
+        if (NearSegment(point, departure.UpperAnchor, departure.TileAt(-1f), 2f))
+            return true;
+    }
+
+    foreach (WorldLayerConnection hole in _world.NearbyConnections(point))
     {
         Vector2 local = hole.Coordinates(point);
@@ -86,10 +95,11 @@
     }
 
-    foreach (CaveHole hole in _world.NearbyHoles(point))
+    foreach (WorldLayerConnection hole in _world.NearbyConnections(point))
     {
         Vector2 end = hole.TileAt(hole.TunnelLength);
         Vector2 room = Centre(hole.AnchorCell.X, hole.AnchorCell.Y);
 
-        if (NearSegment(point, end, room, _entranceRadius))
+        if (point.DistanceSquaredTo(end) <= 9f ||
+            NearSegment(point, end, room, _entranceRadius))
             return true;
     }
@@ -121,10 +131,18 @@
         Vector2 b = new(neighbour.X, neighbour.Y);
 
-        foreach (CaveHole hole in _world.NearbyHoles(a))
+        foreach (WorldLayerConnection departure in _world.Departures)
+        {
+            Vector2 localA = departure.Coordinates(a);
+            Vector2 localB = departure.Coordinates(b);
+            if (localA.X <= 0f && localA.X > -1.01f && Mathf.Abs(localA.Y) <= 1f &&
+                localB.X > 0f) return true;
+        }
+
+        foreach (WorldLayerConnection hole in _world.NearbyConnections(a))
         {
             Vector2 localA = hole.Coordinates(a);
             Vector2 localB = hole.Coordinates(b);
 
-            if (Mathf.Abs(localA.X) < 0.01f &&
+            if (localA.X >= 0f && localA.X < 1.01f &&
                 Mathf.Abs(localA.Y) <= 1f && localB.X < 0f)
                 return true;
@@ -142,5 +160,13 @@
         float floor = _baseHeight + Biomes.At(tile).FloorHeight(tile);
 
-        foreach (CaveHole hole in _world.NearbyHoles(tile))
+        foreach (WorldLayerConnection departure in _world.Departures)
+        {
+            Vector2 local = departure.Coordinates(tile);
+            if (local.X >= -2f && local.X <= 0.6f && Mathf.Abs(local.Y) <= 2.5f)
+                return Mathf.Lerp(floor, departure.RimHeight,
+                    Mathf.Clamp((local.X + 2f) / 2f, 0f, 1f));
+        }
+
+        foreach (WorldLayerConnection hole in _world.NearbyConnections(tile))
         {
             Vector2 local = hole.Coordinates(tile);
@@ -159,5 +185,5 @@
     // =========================================================
     // Match the rendered floor triangles when sampling actor elevation.
-    public float HeightAt(Vector2 tile, float unusedRimHeight)
+    public float HeightAt(Vector2 tile)
     {
         Vector2 centre = new(
@@ -176,4 +202,18 @@
             ? a * (1f - u) + b * (u - v) + c * v
             : a * (1f - v) + c * u + d * (v - u);
+    }
+    #endregion
+
+    #region Connection Anchors
+    // =========================================================
+    // Expose the shared chamber lattice for reserved connection approaches.
+    public Vector2 RoomCentre(Vector2I cell) { return Centre(cell.X, cell.Y); }
+
+    // =========================================================
+    // Select a nearby normal chamber for the lower landing connector.
+    public Vector2I RoomCell(Vector2 point)
+    {
+        return new Vector2I(Mathf.RoundToInt((point.X - HubX) / Settings.CellSpacingTiles),
+            Mathf.RoundToInt(point.Y / Settings.CellSpacingTiles));
     }
     #endregion
--- a/WORLD/Generation/Caves/CaveHole.cs
+++ /dev/null
@@ -1,62 +0,0 @@
-// Describes one surface hole connected to the shared cave network.
-// Its surface clearance is reserved independently from rendered artwork.
-using Godot;
-
-public sealed class CaveHole
-{
-    #region Data
-    public readonly string Id;
-    public readonly Vector2 MouthTile, Direction;
-    public readonly Vector2 SurfacePosition;
-    public readonly Vector2I AnchorCell;
-    public readonly float RimHeight, TunnelLength;
-
-    public float SurfaceClearRadius { get; set; } = 100f;
-    public CaveEntrance Marker { get; set; }
-    #endregion
-
-    #region Construction
-    // =========================================================
-    // Store one cardinal entrance direction and its surface/cave pairing.
-    public CaveHole(
-        string id, Vector2 mouthTile, Vector2 direction,
-        Vector2 surfacePosition, float rimHeight,
-        float tunnelLength, Vector2I anchorCell)
-    {
-        Id = id;
-        MouthTile = mouthTile;
-        Direction = direction;
-        SurfacePosition = surfacePosition;
-        RimHeight = rimHeight;
-        TunnelLength = tunnelLength;
-        AnchorCell = anchorCell;
-    }
-    #endregion
-
-    #region Coordinates
-    // =========================================================
-    // Return distance along the tunnel and distance across it.
-    public Vector2 Coordinates(Vector2 caveTile)
-    {
-        Vector2 offset = caveTile - MouthTile;
-        Vector2 across = new(-Direction.Y, Direction.X);
-        return new Vector2(offset.Dot(Direction), offset.Dot(across));
-    }
-
-    // =========================================================
-    // Convert entrance-relative coordinates into shared cave coordinates.
-    public Vector2 TileAt(float along, float across = 0f)
-    {
-        return MouthTile + Direction * along +
-            new Vector2(-Direction.Y, Direction.X) * across;
-    }
-
-    // =========================================================
-    // Return a surface position just outside the tunnel mouth.
-    public Vector2 OutsidePosition(Vector2 tileSize)
-    {
-        return SurfacePosition -
-            IsoGrid.TileToWorld(Direction * 0.9f, tileSize);
-    }
-    #endregion
-}
--- a/WORLD/Generation/Caves/CaveHole.cs.uid
+++ /dev/null
@@ -1 +0,0 @@
-uid://cppy6t3y7hcph
--- a/WORLD/Generation/Caves/CaveTerrainElevation.cs
+++ b/WORLD/Generation/Caves/CaveTerrainElevation.cs
@@ -16,3 +16,3 @@
         return World.Generator.HeightAt(
-            World.WorldToTile(point), World.RimHeight);
+            World.WorldToTile(point));
     }
--- a/WORLD/Generation/Caves/CaveWorld.cs
+++ b/WORLD/Generation/Caves/CaveWorld.cs
@@ -18,16 +18,15 @@
     public CaveTerrainElevation Elevation { get; private set; }
     public Vector2 TileSize { get; private set; }
-    public float RimHeight { get; private set; }
         public float FloorElevation { get; private set; }
 
-        public float DepthPixels => RimHeight - FloorElevation;
-    public float TunnelLengthTiles => Settings.EntranceTunnelLengthTiles;
     public ShaderMaterial GroundMaterial { get; private set; }
     public ImageTexture WhiteTexture { get; private set; }
 
     private WorldLayerMember _objectsMember;
-    private readonly List<CaveHole> _holes = new();
-    public IReadOnlyList<CaveHole> Holes => _holes;
-    public CaveEntrance Entrance => _holes.Count > 0 ? _holes[0].Marker : null;
+    private readonly List<WorldLayerConnection> _connections = new();
+    public IReadOnlyList<WorldLayerConnection> Connections => _connections;
+    private readonly List<WorldLayerConnection> _departures = new();
+    public IReadOnlyList<WorldLayerConnection> Departures => _departures;
+    public bool Active { get; private set; }
         public CaveEntrancePlanner Planner { get; set; }
     #endregion
@@ -44,7 +43,6 @@
     // Build one shared cave network at the configured underground elevation.
     public void Build(
-        Vector2 origin, Vector2 tileSize, float rimHeight,
-        uint worldSeed, CaveGenerationSettings settings,
-        IReadOnlyList<CaveHole> holes)
+        Vector2 origin, Vector2 tileSize,
+        uint worldSeed, CaveGenerationSettings settings)
     {
         Definition = WorldConfig.Find(this).GetLayerCatalog().Get(LayerId);
@@ -56,5 +54,4 @@
         Settings.Validate();
         TileSize = tileSize;
-        RimHeight = rimHeight;
         FloorElevation = Definition.FloorElevation;
         WorldSeed = worldSeed;
@@ -63,16 +60,4 @@
             throw new System.InvalidOperationException(
                 "The underground layer floor elevation must be finite and below zero.");
-
-        foreach (CaveHole hole in holes)
-        {
-            if (hole.RimHeight <= FloorElevation)
-                throw new System.InvalidOperationException(
-                    $"Cave floor must be below entrance '{hole.Id}'. " +
-                    "Lower the layer definition's FloorElevation or raise that surface biome.");
-        }
-
-        _holes.Clear();
-        foreach (CaveHole hole in holes)
-            _holes.Add(hole);
 
                 Generator = new CaveGenerator(
@@ -121,19 +106,4 @@
         });
 
-        PackedScene scene = GD.Load<PackedScene>(
-            "res://WORLD/Generation/Caves/CaveEntrance.tscn");
-
-        foreach (CaveHole hole in _holes)
-        {
-            CaveEntrance marker = scene.Instantiate<CaveEntrance>();
-            marker.Name = $"Hole_{hole.Id}";
-            marker.World = this;
-            marker.Hole = hole;
-            hole.Marker = marker;
-            AddChild(marker);
-            marker.GlobalPosition = hole.SurfacePosition;
-            marker.QueueRedraw();
-        }
-
         Streaming = new CaveChunkController { Name = "CaveStreaming" };
         AddChild(Streaming);
@@ -148,8 +118,22 @@
     public void SetActive(bool active)
     {
+        Active = active;
         Root.Visible = active;
+        Root.Modulate = Colors.White;
         _objectsMember.SetActive(active);
         Streaming.SetActive(active);
     }
+    // =========================================================
+    // Reveal a paused departure layer without restoring its collisions or processing.
+    public void SetPreview(float opacity)
+    {
+        if (Active) return;
+        bool visible = opacity > 0.001f;
+        if (Root.Visible != visible) Root.Visible = visible;
+        Color colour = Root.Modulate;
+        if (Mathf.IsEqualApprox(colour.A, opacity)) return;
+        colour.A = opacity;
+        Root.Modulate = colour;
+    }
     #endregion
 
@@ -179,12 +163,12 @@
     // =========================================================
     // Find the closest surface hole for underground preloading while above ground.
-    public CaveHole NearestSurfaceHole(Vector2 point)
-    {
-        CaveHole best = null;
+    public WorldLayerConnection NearestConnection(Vector2 point)
+    {
+        WorldLayerConnection best = null;
         float distance = float.MaxValue;
 
-        foreach (CaveHole hole in _holes)
-        {
-            float candidate = point.DistanceSquaredTo(hole.SurfacePosition);
+        foreach (WorldLayerConnection hole in _connections)
+        {
+            float candidate = point.DistanceSquaredTo(hole.UpperPosition);
             if (candidate >= distance) continue;
             distance = candidate;
@@ -197,7 +181,7 @@
     // =========================================================
     // Identify an entrance ramp without using the player's screen direction.
-    public CaveHole TransitionAt(Vector2 tile)
-    {
-        foreach (CaveHole hole in _holes)
+    public WorldLayerConnection TransitionAt(Vector2 tile)
+    {
+        foreach (WorldLayerConnection hole in _connections)
         {
             Vector2 local = hole.Coordinates(tile);
@@ -235,9 +219,10 @@
 
     float objectRadius = footprint.Length() * 0.5f + padding.Length();
-    foreach (CaveHole hole in world.Holes)
-    {
-        float radius = hole.SurfaceClearRadius + objectRadius;
+    foreach (WorldLayerConnection hole in world.Connections)
+    {
+        if (hole.UpperLayer != WorldLayerId.Surface) continue;
+        float radius = hole.ClearRadius + objectRadius;
         float squared = radius * radius;
-        if (point.DistanceSquaredTo(hole.SurfacePosition) < squared ||
+        if (point.DistanceSquaredTo(hole.UpperPosition) < squared ||
             point.DistanceSquaredTo(hole.OutsidePosition(world.TileSize)) < squared)
             return true;
@@ -250,8 +235,8 @@
     // =========================================================
     // Register one prepared entrance before nearby terrain and objects generate.
-    public void AddHole(CaveHole hole)
-    {
-        if (_holes.Contains(hole)) return;
-        _holes.Add(hole);
+    public void AttachConnection(WorldLayerConnection hole)
+    {
+        if (_connections.Contains(hole)) return;
+        _connections.Add(hole);
 
         PackedScene scene = GD.Load<PackedScene>(
@@ -261,9 +246,9 @@
         marker.Name = $"Hole_{hole.Id}";
         marker.World = this;
-        marker.Hole = hole;
+        marker.Connection = hole;
         hole.Marker = marker;
 
         AddChild(marker);
-        marker.GlobalPosition = hole.SurfacePosition;
+        marker.GlobalPosition = hole.UpperPosition;
         marker.QueueRedraw();
     }
@@ -271,7 +256,7 @@
     // =========================================================
     // Release distant cached entrance artwork; its seed can recreate it later.
-    public void RemoveHole(CaveHole hole)
-    {
-        _holes.Remove(hole);
+    public void DetachConnection(WorldLayerConnection hole)
+    {
+        _connections.Remove(hole);
 
         if (GodotObject.IsInstanceValid(hole.Marker))
@@ -282,8 +267,28 @@
 
     // =========================================================
+    // Index the upper approach independently from lower ramp ownership.
+    public void AttachDeparture(WorldLayerConnection connection)
+    {
+        if (!_departures.Contains(connection)) _departures.Add(connection);
+    }
+
+    // =========================================================
+    // Release an explicitly removed upper approach.
+    public void DetachDeparture(WorldLayerConnection connection)
+    {
+        _departures.Remove(connection);
+    }
+
+    // =========================================================
     // Restrict terrain sampling to mouths near the requested cave coordinate.
-    public IEnumerable<CaveHole> NearbyHoles(Vector2 tile)
-    {
-        return Planner?.Nearby(tile) ?? _holes;
+    public IEnumerable<WorldLayerConnection> NearbyConnections(Vector2 tile)
+    {
+        if (Planner != null)
+            foreach (WorldLayerConnection connection in Planner.Nearby(tile))
+                yield return connection;
+        foreach (WorldLayerConnection connection in _connections)
+            if (connection.UpperLayer != WorldLayerId.Surface &&
+                connection.SampleArea.HasPoint(tile))
+                yield return connection;
     }
     #endregion
--- a/WORLD/Generation/Caves/Chunks/CaveChunk.cs
+++ b/WORLD/Generation/Caves/Chunks/CaveChunk.cs
@@ -9,3 +9,3 @@
     public Vector2I Coordinate { get; private set; }
-    public bool Ready { get; private set; }
+    public new bool Ready { get; private set; }
 
@@ -148,3 +148,4 @@
         _member.SetActive(active);
-        _member.SetOpacity(active ? 1f : 0f);
+        // The world root owns opacity; paused chunks can remain visible during a ramp.
+        _member.SetOpacity(1f);
     }
--- a/WORLD/Generation/Caves/Chunks/CaveChunkController.cs
+++ b/WORLD/Generation/Caves/Chunks/CaveChunkController.cs
@@ -72,4 +72,4 @@
                 if (!_preloadEntrances) return;
-                CaveHole hole =
-                    _world.NearestSurfaceHole(_player.GlobalPosition);
+                WorldLayerConnection hole =
+                    _world.NearestConnection(_player.GlobalPosition);
                 if (hole == null) return;
@@ -137,9 +137,7 @@
     // =========================================================
-    // Require a completed buffer around an actual registered mouth.
-    public bool EntryReady(CaveHole hole = null)
-    {
-        if (_failed || hole == null) return false;
-
-        Vector2I centre = CoordinateAt(hole.MouthTile);
-
+    // Require completed chunk neighbours around either connection endpoint.
+    public bool AreaReady(Vector2 point)
+    {
+        if (_failed) return false;
+        Vector2I centre = CoordinateAt(_world.WorldToTile(point));
         for (int x = centre.X - 1; x <= centre.X + 1; x++)
@@ -147,6 +145,31 @@
             if (!_chunks.TryGetValue(new Vector2I(x, y), out CaveChunk chunk) ||
-                !chunk.Ready)
-                return false;
-
+                !chunk.Ready) return false;
         return true;
+    }
+
+    // =========================================================
+    // Rebuild only affected chunks when a debug connection is added after loading.
+    public void InvalidateArea(Rect2 area)
+    {
+        List<Vector2I> remove = new();
+        foreach (var pair in _chunks)
+        {
+            int size = _world.Settings.ChunkSize;
+            Rect2 bounds = new(new Vector2(pair.Key.X * size, pair.Key.Y * size),
+                Vector2.One * size);
+            if (bounds.Grow(1f).Intersects(area)) remove.Add(pair.Key);
+        }
+        foreach (Vector2I key in remove)
+        {
+            CaveChunk chunk = _chunks[key];
+            if (chunk == _building)
+            {
+                _work?.Dispose(); _work = null; _building = null;
+            }
+            chunk.SetActive(false);
+            chunk.Visible = false;
+            chunk.QueueFree();
+            _chunks.Remove(key);
+            ChunkAvailabilityChanged?.Invoke(key);
+        }
     }
@@ -174,3 +197,3 @@
 
-    foreach (CaveHole hole in _world.Holes)
+    foreach (WorldLayerConnection hole in _world.Connections)
     {
@@ -276,3 +299,3 @@
     List<Vector2I> remove = new();
-    CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(this);
+    WorldLayerPursuit pursuit = WorldLayerPursuit.Find(this);
 
--- a/WORLD/Generation/Caves/README.md
+++ b/WORLD/Generation/Caves/README.md
@@ -26,4 +26,8 @@
 
-The existing surface ramps still connect only to the catalog's
-SurfaceEntranceLayerId. Deeper playable connections are pass 3 work.
+Pass 3 uses WorldLayerConnection records for surface and underground travel.
+The catalog's SurfaceEntranceLayerId remains the surface planner's destination.
+WorldLayerConnections registers both sides, while WorldLayerLanding checks
+ready floor and obstacles in the exact destination. WorldLayerSurface owns
+surface pausing, and WorldLayerPursuit follows known adjacent connections.
+The old CaveHole and CaveEnemyPursuit implementations have been removed.
 Underground chunk metadata is layer-aware; deeper layers do not prepare
@@ -157,3 +161,3 @@
 
-Future connections explicitly identify:
+Shared connections explicitly identify:
 
@@ -222,4 +226,3 @@
 Transition profiling and a shared generation scheduler remain separate
-future work. Additional playable underground layers are not implemented
-until pass 3.
+future work. Pass 3 is implemented but still needs build and gameplay testing.
 
@@ -237 +240,28 @@
 - Verify that objects cannot interact across different layers.
+
+## Pass 3 Testing And Controls
+
+world_infinite includes an optional DeepCavernsTest node. Its script is isolated
+under DEBUG/DeepCavernsTest/. After the first surface entrance is used, it carves
+a right-hand branch from that entrance's upper cave chamber to a marked descent.
+The lower ramp and its normal-room connector use the same shared metadata.
+Only affected pre-existing chunks are invalidated, then rebuilt under the
+existing streamer budget. Remove the node and restart to remove test placement.
+This is not yet biome-controlled natural placement of deeper entrances.
+
+CONFIG/GlobalConfig.cs, LAYER TRANSITIONS:
+- EntranceLengthMultiplier defaults to 1.0.
+- EntranceSlopeMultiplier defaults to 1.1.
+- Final length = base length * length multiplier / slope multiplier.
+- Allowed resulting lengths are 6 to 512 tiles.
+
+Apply changes before generation and restart after editing these controls.
+Endpoint elevations stay fixed; longer ramps reduce average physical slope.
+Set a deeper FloorElevation for a truly deeper destination.
+
+Test Surface -> Upper Caverns -> Deep Caverns and return through both ramps.
+Also test reversing before either descent finishes, switching at another
+surface entrance, enemy pursuit, drops, mining, shadows and death/respawn.
+Layer IDs isolate interaction eligibility; inactive terrain collision is disabled.
+Departure drawing may fade during a ramp while its simulation stays paused.
+The original surface entrance metadata remains pinned during a deeper journey.
--- a/WORLD/Generation/Caves/Surface/CaveEntrancePlanner.cs
+++ b/WORLD/Generation/Caves/Surface/CaveEntrancePlanner.cs
@@ -16,7 +16,8 @@
     private readonly WaterBasinWorld _basins;
     private readonly CaveSurfaceSampler _sampler;
-    private readonly GenerationCellCache<CaveHole> _cells;
+    private readonly GenerationCellCache<WorldLayerConnection> _cells;
     private CaveWorld _cave;
     private readonly float _floorElevation;
+    private readonly float _tunnelLength;
 
     private readonly int _offset, _stride;
@@ -46,4 +47,5 @@
     Settings = (CaveGenerationSettings)settings.Duplicate();
     Settings.Validate();
+    _tunnelLength = WorldLayerConnection.LengthFor(_config, Settings.EntranceTunnelLengthTiles);
 
     float minimum = _config.MinimumCaveHoleDistanceTiles;
@@ -60,5 +62,5 @@
     Settings.CellSpacingTiles = Mathf.Max(
         Settings.CellSpacingTiles,
-        Settings.EntranceTunnelLengthTiles + _offset +
+        Mathf.CeilToInt(_tunnelLength) + _offset +
         Mathf.CeilToInt(maximumWidth * 0.5f) + 6);
 
@@ -66,5 +68,5 @@
 
     _reach = new Vector2(
-        _offset, Settings.EntranceTunnelLengthTiles + _offset).Length();
+        _offset, _tunnelLength + _offset).Length();
 
     _stride = Mathf.CeilToInt(
@@ -90,7 +92,7 @@
         2f / (chunks.TileSize.Y * chunks.TileSize.Y)) + 3f;
 
-    _cells = new GenerationCellCache<CaveHole>(CacheLimit, hole =>
-    {
-        if (hole != null) _cave?.RemoveHole(hole);
+    _cells = new GenerationCellCache<WorldLayerConnection>(CacheLimit, hole =>
+    {
+        if (hole != null) WorldLayerRuntime.Find(_world)?.Connections.Remove(hole);
     });
 }
@@ -153,10 +155,12 @@
             Vector2 mouth = room + new Vector2(
                 sideX * _offset,
-                sideY * (Settings.EntranceTunnelLengthTiles + _offset));
+                sideY * (_tunnelLength + _offset));
+            // Align mouths to the collision tile lattice while keeping ramp length continuous.
+            mouth = new Vector2(Mathf.Round(mouth.X), Mathf.Round(mouth.Y));
             Vector2 direction = sideY > 0f ? Vector2.Up : Vector2.Down;
 
             Vector2 point = _ground.ToGlobal(
                 IsoGrid.TileToWorld(mouth, _chunks.TileSize));
-            CaveHole hole = null;
+            WorldLayerConnection hole = null;
 
             if (point.DistanceSquaredTo(_spawn) >
@@ -181,10 +185,10 @@
                     result.RimHeight > _floorElevation + 32f)
                 {
-                    hole = new CaveHole(
-                        $"C_{x}_{y}", mouth, direction, point,
-                        result.RimHeight, Settings.EntranceTunnelLengthTiles,
+                    hole = new WorldLayerConnection(
+                        $"C_{x}_{y}", WorldLayerId.Surface, cave.LayerId, mouth, direction, point,
+                        result.RimHeight, _tunnelLength,
                         anchor)
                     {
-                        SurfaceClearRadius = result.ClearRadius
+                        ClearRadius = result.ClearRadius
                     };
                 }
@@ -195,6 +199,6 @@
             if (hole != null)
             {
-                cave.AddHole(hole);
-                GD.Print($"[Caves] {hole.Id}: {hole.SurfacePosition}");
+                WorldLayerRuntime.Find(_world).Connections.Register(hole);
+                GD.Print($"[Caves] {hole.Id}: {hole.UpperPosition}");
             }
         }
@@ -203,5 +207,5 @@
     // =========================================================
     // Supply only nearby registered mouths to floor and elevation sampling.
-    public IEnumerable<CaveHole> Nearby(Vector2 tile)
+    public IEnumerable<WorldLayerConnection> Nearby(Vector2 tile)
     {
         int firstX = Mathf.FloorToInt((tile.X - HubX - _reach - 4f) / _pitch);
@@ -212,5 +216,5 @@
         for (int y = firstY; y <= lastY; y++)
         for (int x = firstX; x <= lastX; x++)
-            if (_cells.TryGetValue(new Vector2I(x, y), out CaveHole hole) &&
+            if (_cells.TryGetValue(new Vector2I(x, y), out WorldLayerConnection hole) &&
                 hole != null)
                 yield return hole;
--- a/WORLD/Layers/CaveEnemyPursuit.cs
+++ /dev/null
@@ -1,369 +0,0 @@
-// Keeps existing pursuers active across layer changes.
-// Enemies cross through the player's registered entrance, never through the ground.
-using Godot;
-using System;
-using System.Collections.Generic;
-
-public partial class CaveEnemyPursuit : Node
-{
-    #region State
-    private sealed class Record
-    {
-        public Entity Actor;
-        public WorldLayerMember Member;
-        public CaveHole Portal;
-        public CaveWorld PortalWorld;
-        public IDisposable Lease;
-        public bool Simulating = true;
-        public bool Colliding = true;
-    }
-
-    private WorldLayerController _layers;
-    private Node2D _surfaceGround;
-    private ChunkController _surfaceChunks;
-    private InfiniteWorldGeneration _generation;
-    private double _timer;
-
-    private readonly Dictionary<Entity, Record> _records = new();
-    private readonly List<Entity> _remove = new();
-    #endregion
-
-    #region Lifecycle
-    // =========================================================
-    // Register the shared pursuit service.
-    public override void _EnterTree()
-    {
-        AddToGroup("cave_enemy_pursuit");
-    }
-
-    // =========================================================
-    // Create pursuit coordination; each runtime world owns its navigation.
-    public static void Ensure(WorldLayerController layers, Node world)
-    {
-        if (Find(layers) != null) return;
-
-        CaveEnemyPursuit helper = new()
-        {
-            Name = "EnemyPursuit",
-            _layers = layers,
-            _surfaceGround = world.GetNode<Node2D>("GroundChunks"),
-            _surfaceChunks = world.GetNode<ChunkController>(
-                "Systems/ChunkController"),
-            _generation = InfiniteWorldGeneration.Find(world)
-        };
-        layers.AddChild(helper);
-    }
-
-    // =========================================================
-    // Find the optional service without introducing a second enemy system.
-    public static CaveEnemyPursuit Find(Node context)
-    {
-        if (context == null || !context.IsInsideTree()) return null;
-        return context.GetTree().GetFirstNodeInGroup(
-            "cave_enemy_pursuit") as CaveEnemyPursuit;
-    }
-
-    // =========================================================
-    // Refresh the small live enemy set and layer presentation at ten hertz.
-    public override void _Process(double delta)
-    {
-        _timer -= delta;
-        if (_timer > 0.0) return;
-        _timer = 0.1;
-
-        TrackEnemies();
-        _remove.Clear();
-
-        foreach (var pair in _records)
-        {
-            Record record = pair.Value;
-            if (!GodotObject.IsInstanceValid(record.Actor) ||
-                record.Actor.IsQueuedForDeletion())
-            {
-                ClearPortal(record);
-                _remove.Add(pair.Key);
-                continue;
-            }
-
-            UpdateRecord(record);
-        }
-
-        foreach (Entity actor in _remove)
-            _records.Remove(actor);
-    }
-
-    // =========================================================
-    // Release entrance protection and restore surviving actors during teardown.
-    public override void _ExitTree()
-    {
-        foreach (Record record in _records.Values)
-        {
-            ClearPortal(record);
-            if (!GodotObject.IsInstanceValid(record.Member) ||
-                !GodotObject.IsInstanceValid(record.Actor) ||
-                record.Actor.IsQueuedForDeletion())
-                continue;
-
-            record.Member.SetPhysicsEnabled(true);
-            record.Member.SetActive(true);
-            record.Member.SetOpacity(1f);
-        }
-        _records.Clear();
-    }
-    #endregion
-
-    #region Ownership And Presentation
-    // =========================================================
-    // Capture each activated enemy once, independently from surface scenery.
-    private void TrackEnemies()
-    {
-        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
-        {
-            if (node is not Entity actor || _records.ContainsKey(actor) ||
-                actor.IsQueuedForDeletion() || !actor.Initialized ||
-                !actor.IsActivated || actor.SpawnPending ||
-                actor.Health?.IsAlive != true)
-                continue;
-
-            string layer = WorldLayerMember.For(actor);
-            WorldLayerMember member = WorldLayerMember.Attach(actor, layer);
-
-            // Populate the helper's original collision and presentation snapshot.
-            member.SetActive(false);
-            member.SetActive(true);
-
-            _records.Add(actor, new Record
-            {
-                Actor = actor,
-                Member = member
-            });
-        }
-    }
-
-    // =========================================================
-    // Keep hidden pursuers thinking, but disable their physical interaction.
-    private void UpdateRecord(Record record)
-    {
-        Entity actor = record.Actor;
-        if (actor.Health?.IsAlive != true) return;
-
-        if (!actor.HasTarget)
-            ClearPortal(record);
-        else if (record.Portal != null)
-            TryCross(record);
-
-        bool visible = record.Member.Layer == _layers.Current;
-        bool simulate = visible || actor.HasTarget;
-
-        if (simulate != record.Simulating)
-        {
-            // Capture original collision settings before pausing, not hidden zeros.
-            if (!simulate)
-                record.Member.SetPhysicsEnabled(true);
-
-            record.Member.SetActive(simulate);
-            record.Simulating = simulate;
-            record.Colliding = simulate;
-        }
-
-        bool collide = simulate && visible;
-        if (collide != record.Colliding)
-        {
-            record.Member.SetPhysicsEnabled(collide);
-            record.Colliding = collide;
-        }
-
-        record.Member.SetOpacity(visible ? 1f : 0f);
-    }
-    #endregion
-
-    #region Entrance Pursuit
-    // =========================================================
-    // Remember the entrance only for enemies already tracking this player.
-    public void PlayerCrossed(
-        CaveWorld portalWorld, CaveHole hole, string destination, Player player)
-    {
-        TrackEnemies();
-
-        foreach (Record record in _records.Values)
-        {
-            Entity actor = record.Actor;
-            if (!GodotObject.IsInstanceValid(actor) ||
-                actor.IsQueuedForDeletion() ||
-                !actor.HasTarget || actor.Target != player)
-                continue;
-
-            ClearPortal(record);
-            actor.ResetPursuitMovement();
-
-            if (hole != null && record.Member.Layer != destination &&
-                (record.Member.Layer == WorldLayerId.Surface ||
-                    record.Member.Layer == portalWorld.LayerId) &&
-                (destination == WorldLayerId.Surface ||
-                    destination == portalWorld.LayerId))
-            {
-                record.Portal = hole;
-                record.PortalWorld = portalWorld;
-
-                Vector2 tile = IsoGrid.WorldToTile(
-                    _surfaceGround.ToLocal(hole.SurfacePosition),
-                    portalWorld.TileSize);
-
-                record.Lease = _generation?.PinArea(
-                    new Rect2(tile - Vector2.One * 4f, Vector2.One * 8f));
-            }
-
-            UpdateRecord(record);
-        }
-    }
-
-    // =========================================================
-    // Supply a reachable mouth goal instead of the target's other-layer position.
-    public bool TryGetGoal(Entity actor, out Vector2 goal)
-    {
-        goal = actor.GlobalPosition;
-        if (!_records.TryGetValue(actor, out Record record) ||
-            record.Portal == null || !actor.HasTarget ||
-            WorldLayerMember.Same(actor, actor.Target))
-            return false;
-
-        goal = record.Member.Layer == WorldLayerId.Surface
-            ? record.Portal.SurfacePosition
-            : record.Portal.OutsidePosition(record.PortalWorld.TileSize);
-        return true;
-    }
-
-    // =========================================================
-    // Cross only at the mouth and only when destination ground is ready.
-    private void TryCross(Record record)
-    {
-        Entity actor = record.Actor;
-        CaveHole hole = record.Portal;
-        CaveWorld world = record.PortalWorld;
-        string destination = WorldLayerMember.For(actor.Target);
-
-        if (world == null ||
-            (destination != WorldLayerId.Surface && destination != world.LayerId))
-        {
-            ClearPortal(record);
-            return;
-        }
-
-        if (record.Member.Layer == destination)
-        {
-            ClearPortal(record);
-            return;
-        }
-
-        Vector2 mouth = record.Member.Layer == WorldLayerId.Surface
-            ? hole.SurfacePosition
-            : hole.OutsidePosition(world.TileSize);
-
-        if (actor.GlobalPosition.DistanceSquaredTo(mouth) > 20f * 20f)
-            return;
-
-        Vector2 landing;
-        if (destination == world.LayerId)
-        {
-            landing = world.TileToWorld(hole.TileAt(0.25f));
-            if (!world.Streaming.EntryReady(hole) ||
-                !world.Streaming.IsAvailable(landing, 10f))
-                return;
-        }
-        else
-        {
-            landing = hole.OutsidePosition(world.TileSize);
-            WorldNavigation surface = WorldNavigation.ForLayer(
-                this, WorldLayerId.Surface);
-
-            if (!_surfaceChunks.IsNavigationPointAvailable(landing, 10f) ||
-                surface == null || !surface.CanTravelDirectly(landing, landing))
-                return;
-        }
-
-        actor.CrossWorldLayer(destination, landing);
-        ClearPortal(record);
-    }
-
-    // =========================================================
-    // Release the cached entrance once pursuit no longer needs it.
-    private static void ClearPortal(Record record)
-    {
-        record.Lease?.Dispose();
-        record.Lease = null;
-        record.Portal = null;
-        record.PortalWorld = null;
-    }
-    #endregion
-
-    #region Streaming Protection
-    // =========================================================
-    // Keep the already-loaded surface approach until its pursuer crosses.
-    public bool RetainSurface(Vector2I coordinate)
-    {
-        Rect2 chunk = new(
-            new Vector2(coordinate.X * _surfaceChunks.ChunkSize,
-                coordinate.Y * _surfaceChunks.ChunkSize),
-            Vector2.One * _surfaceChunks.ChunkSize);
-
-        foreach (Record record in _records.Values)
-        {
-            if (!IsPursuing(record) ||
-                record.Member.Layer != WorldLayerId.Surface ||
-                record.Portal == null)
-                continue;
-
-            Vector2 from = IsoGrid.WorldToTile(
-                _surfaceGround.ToLocal(record.Actor.GlobalPosition),
-                _surfaceChunks.TileSize);
-            Vector2 to = IsoGrid.WorldToTile(
-                _surfaceGround.ToLocal(record.Portal.SurfacePosition),
-                _surfaceChunks.TileSize);
-
-            if (new Rect2(from, Vector2.Zero).Expand(to)
-                .Grow(_surfaceChunks.ChunkSize).Intersects(chunk))
-                return true;
-        }
-        return false;
-    }
-
-    // =========================================================
-    // Protect loaded cave floor along an active pursuer's local route.
-    public bool RetainCave(CaveWorld world, Vector2I coordinate)
-    {
-        int size = world.Settings.ChunkSize;
-        Rect2 chunk = new(
-            new Vector2(coordinate.X * size, coordinate.Y * size),
-            Vector2.One * size);
-
-        foreach (Record record in _records.Values)
-        {
-            if (!IsPursuing(record) ||
-                record.Member.Layer != world.LayerId)
-                continue;
-
-            Vector2 from = world.WorldToTile(
-                record.Actor.GlobalPosition);
-            Vector2 destination = record.Portal != null
-                ? record.Portal.OutsidePosition(record.PortalWorld.TileSize)
-                : record.Actor.Target.GlobalPosition;
-            Vector2 to = world.WorldToTile(destination);
-
-            if (new Rect2(from, Vector2.Zero).Expand(to)
-                .Grow(size).Intersects(chunk))
-                return true;
-        }
-        return false;
-    }
-
-    // =========================================================
-    // Ignore retired actors when checking temporary streaming protection.
-    private static bool IsPursuing(Record record)
-    {
-        return GodotObject.IsInstanceValid(record.Actor) &&
-            !record.Actor.IsQueuedForDeletion() &&
-            record.Actor.Health?.IsAlive == true &&
-            record.Actor.HasTarget;
-    }
-    #endregion
-}
--- a/WORLD/Layers/CaveEnemyPursuit.cs.uid
+++ /dev/null
@@ -1 +0,0 @@
-uid://st83db1ujds4
--- /dev/null
+++ b/WORLD/Layers/Connections/WorldLayerConnection.cs
@@ -0,0 +1,92 @@
+// One bidirectional connection with a descending ramp owned by its lower layer.
+// Endpoints share logical coordinates; elevation, drawing and collision use one route.
+using Godot;
+using System;
+
+public sealed class WorldLayerConnection
+{
+    #region Data
+    public readonly string Id, UpperLayer, LowerLayer;
+    public readonly Vector2 MouthTile, Direction, UpperPosition, UpperAnchor;
+    public readonly Vector2I AnchorCell;
+    public readonly float RimHeight, TunnelLength;
+    public float ClearRadius { get; set; } = 100f;
+    public CaveEntrance Marker { get; set; }
+    public Rect2 SampleArea { get; internal set; }
+    public Rect2 RampArea => new Rect2(TileAt(-2f), Vector2.Zero)
+        .Expand(TileAt(TunnelLength + 2f)).Grow(4f);
+    #endregion
+
+    #region Construction
+    // =========================================================
+    // Require distinct layer IDs and a cardinal direction for tile-edge openings.
+    public WorldLayerConnection(string id, string upperLayer, string lowerLayer,
+        Vector2 mouthTile, Vector2 direction, Vector2 upperPosition,
+        float rimHeight, float tunnelLength, Vector2I anchorCell,
+        Vector2 upperAnchor = default)
+    {
+        WorldLayerId.Validate(upperLayer);
+        WorldLayerId.Validate(lowerLayer);
+        if (string.IsNullOrWhiteSpace(id) || upperLayer == lowerLayer ||
+            !float.IsFinite(rimHeight) || !float.IsFinite(tunnelLength) ||
+            tunnelLength < 6f || !direction.IsFinite() ||
+            (direction != Vector2.Up && direction != Vector2.Down &&
+                direction != Vector2.Left && direction != Vector2.Right))
+            throw new InvalidOperationException("Invalid layer connection.");
+        Id = id; UpperLayer = upperLayer; LowerLayer = lowerLayer;
+        MouthTile = mouthTile; Direction = direction; UpperPosition = upperPosition;
+        RimHeight = rimHeight; TunnelLength = tunnelLength;
+        AnchorCell = anchorCell; UpperAnchor = upperAnchor;
+    }
+
+    // =========================================================
+    // Apply the two global controls once when constructing generation metadata.
+    public static float LengthFor(GlobalConfig config, float baseLength)
+    {
+        float length = config.EntranceLengthMultiplier;
+        float slope = config.EntranceSlopeMultiplier;
+        float result = baseLength * length / slope;
+        if (!float.IsFinite(length) || length <= 0f ||
+            !float.IsFinite(slope) || slope <= 0f ||
+            !float.IsFinite(result) || result < 6f || result > 512f)
+            throw new InvalidOperationException(
+                "Entrance multipliers must produce a tunnel between 6 and 512 tiles.");
+        return result;
+    }
+    #endregion
+
+    #region Coordinates
+    // =========================================================
+    // Resolve the other endpoint without assuming that an exit means surface.
+    public string Other(string layer)
+    {
+        if (layer == UpperLayer) return LowerLayer;
+        if (layer == LowerLayer) return UpperLayer;
+        throw new InvalidOperationException($"'{layer}' does not join '{Id}'.");
+    }
+
+    // =========================================================
+    // Return along/across coordinates on the common logical tile plane.
+    public Vector2 Coordinates(Vector2 tile)
+    {
+        Vector2 offset = tile - MouthTile;
+        return new Vector2(offset.Dot(Direction),
+            offset.Dot(new Vector2(-Direction.Y, Direction.X)));
+    }
+
+    // =========================================================
+    // Convert route coordinates into the common logical tile plane.
+    public Vector2 TileAt(float along, float across = 0f)
+    {
+        return MouthTile + Direction * along +
+            new Vector2(-Direction.Y, Direction.X) * across;
+    }
+
+    // =========================================================
+    // Use a shared mouth apron for upward landings.
+    public Vector2 OutsidePosition(Vector2 tileSize)
+    {
+        return UpperPosition - IsoGrid.TileToWorld(Direction * 0.9f, tileSize);
+    }
+    #endregion
+}
--- /dev/null
+++ b/WORLD/Layers/Connections/WorldLayerConnections.cs
@@ -0,0 +1,98 @@
+// Registers shared connection metadata and indexes it into both underground worlds.
+// Surface candidate discovery and debug placement feed the same traversal records.
+using Godot;
+using System;
+using System.Collections.Generic;
+
+public sealed class WorldLayerConnections
+{
+    #region State
+    private readonly WorldLayerRuntime _runtime;
+    private readonly Dictionary<string, WorldLayerConnection> _records = new();
+    public IEnumerable<WorldLayerConnection> All => _records.Values;
+    public WorldLayerConnections(WorldLayerRuntime runtime) { _runtime = runtime; }
+    #endregion
+
+    #region Registration
+    // =========================================================
+    // Validate both definitions before carving either side of a connection.
+    public void Register(WorldLayerConnection connection, bool rebuild = false)
+    {
+        if (_records.TryGetValue(connection.Id, out WorldLayerConnection existing))
+        {
+            if (existing == connection) return;
+            throw new InvalidOperationException($"Duplicate connection '{connection.Id}'.");
+        }
+        WorldLayerCatalog catalog = WorldConfig.Find(_runtime).GetLayerCatalog();
+        WorldLayerDefinition upper = catalog.Get(connection.UpperLayer);
+        WorldLayerDefinition lower = catalog.Get(connection.LowerLayer);
+        if (lower.Kind != WorldLayerKind.Underground ||
+            lower.DepthIndex <= upper.DepthIndex ||
+            connection.RimHeight <= lower.FloorElevation + 32f)
+            throw new InvalidOperationException("Connection must descend to a deeper layer.");
+
+        _records.Add(connection.Id, connection);
+        CaveWorld destination = _runtime.GetUnderground(connection.LowerLayer);
+        connection.SampleArea = connection.RampArea.Expand(
+            destination.Generator.RoomCentre(connection.AnchorCell)).Grow(
+                destination.Settings.MaximumTunnelWidth() * 0.5f + 2f);
+        destination.AttachConnection(connection);
+        CaveWorld source = _runtime.TryGetUnderground(connection.UpperLayer);
+        source?.AttachDeparture(connection);
+        if (!rebuild) return;
+        destination.Streaming.InvalidateArea(connection.RampArea.Expand(
+            destination.Generator.RoomCentre(connection.AnchorCell)).Grow(4f));
+        source?.Streaming.InvalidateArea(connection.RampArea.Expand(
+            connection.UpperAnchor).Grow(4f));
+    }
+
+    // =========================================================
+    // Release streamed surface decisions when no terrain or traveller pins them.
+    public void Remove(WorldLayerConnection connection)
+    {
+        if (!_records.Remove(connection.Id)) return;
+        _runtime.GetUnderground(connection.LowerLayer).DetachConnection(connection);
+        _runtime.TryGetUnderground(connection.UpperLayer)?.DetachDeparture(connection);
+    }
+    #endregion
+
+    #region Queries
+    // =========================================================
+    // Enumerate connections touching an exact layer, without depth-name assumptions.
+    public IEnumerable<WorldLayerConnection> ForLayer(string layer)
+    {
+        foreach (WorldLayerConnection connection in _records.Values)
+            if (connection.UpperLayer == layer || connection.LowerLayer == layer)
+                yield return connection;
+    }
+
+    // =========================================================
+    // Give pursuit the next known connection along the layer graph.
+    public WorldLayerConnection Next(string from, string to, Vector2 position)
+    {
+        if (from == to) return null;
+        Queue<string> queue = new();
+        Dictionary<string, WorldLayerConnection> first = new();
+        HashSet<string> visited = new() { from };
+        queue.Enqueue(from);
+        while (queue.Count > 0)
+        {
+            string current = queue.Dequeue();
+            // Prefer a nearby mouth for the first hop when several join the same depths.
+            List<WorldLayerConnection> links = new(ForLayer(current));
+            if (current == from) links.Sort((a, b) =>
+                a.UpperPosition.DistanceSquaredTo(position).CompareTo(
+                    b.UpperPosition.DistanceSquaredTo(position)));
+            foreach (WorldLayerConnection link in links)
+            {
+                string next = link.Other(current);
+                if (!visited.Add(next)) continue;
+                first[next] = current == from ? link : first[current];
+                if (next == to) return first[next];
+                queue.Enqueue(next);
+            }
+        }
+        return null;
+    }
+    #endregion
+}
--- /dev/null
+++ b/WORLD/Layers/Connections/WorldLayerLanding.cs
@@ -0,0 +1,90 @@
+// Prepares and validates exact destination terrain using existing streaming budgets.
+// Disabled physics is checked through floor data and obstacle footprints.
+using Godot;
+using System.Collections.Generic;
+
+public sealed class WorldLayerLanding
+{
+    #region State
+    private readonly WorldLayerRuntime _runtime;
+    private readonly ChunkController _surface;
+    private readonly Node2D _surfaceObjects;
+    public string Status { get; private set; } = "";
+
+    // =========================================================
+    // Cache the existing surface service and layer registry.
+    public WorldLayerLanding(WorldLayerRuntime runtime, Node world)
+    {
+        _runtime = runtime;
+        _surface = world.GetNode<ChunkController>("Systems/ChunkController");
+        _surfaceObjects = world.GetNode<Node2D>("WorldObjects");
+    }
+    #endregion
+
+    #region Preparation
+    // =========================================================
+    // Resolve the destination's local apron on the shared logical coordinate plane.
+    public Vector2 PositionFor(WorldLayerConnection connection, string destination)
+    {
+        CaveWorld lower = _runtime.GetUnderground(connection.LowerLayer);
+        return destination == connection.LowerLayer
+            ? lower.TileToWorld(connection.TileAt(0.25f))
+            : connection.OutsidePosition(lower.TileSize);
+    }
+
+    // =========================================================
+    // Start budgeted destination work; no complete world is built synchronously.
+    public bool Prepare(WorldLayerConnection connection, string from)
+    {
+        string destination = connection.Other(from);
+        Vector2 point = PositionFor(connection, destination);
+        if (destination == WorldLayerId.Surface)
+        {
+            bool ready = _surface.PrepareDestination(point);
+            Status = ready ? "checking landing" : _surface.GetMeta(
+                "destination_preload_status", "preparing surface").AsString();
+            return ready;
+        }
+        CaveWorld cave = _runtime.GetUnderground(destination);
+        cave.Streaming.RequestPreload(point);
+        bool complete = cave.Streaming.AreaReady(point);
+        Status = complete ? "checking landing" : $"preparing {cave.Definition.DisplayName}";
+        return complete;
+    }
+
+    // =========================================================
+    // Validate floor and obstacle clearance around the exact reserved endpoint.
+    public bool TryReady(WorldLayerConnection connection, string from, out Vector2 landing)
+    {
+        string destination = connection.Other(from);
+        landing = PositionFor(connection, destination);
+        Node2D objects = destination == WorldLayerId.Surface ? _surfaceObjects
+            : _runtime.GetUnderground(destination).Objects;
+        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);
+        // Lower arrivals stay inside the ramp; upper arrivals can use their clear apron.
+        int alternatives = destination == connection.LowerLayer ? 0 : 8;
+        Vector2 centre = landing;
+        for (int i = 0; i <= alternatives; i++)
+        {
+            Vector2 point = i == 0 ? centre : centre +
+                Vector2.FromAngle(Mathf.Tau * (i - 1) / 8f) * 20f;
+            if (!_runtime.IsAvailable(destination, point, 14f)) continue;
+            bool blocked = false;
+            foreach (Obstacle obstacle in obstacles)
+            {
+                if (!GodotObject.IsInstanceValid(obstacle) || obstacle.IsQueuedForDeletion()) continue;
+                Vector2 difference = point - obstacle.GlobalPosition;
+                Vector2 radius = obstacle.Footprint * 0.5f + Vector2.One * 18f;
+                if (Mathf.Abs(difference.X) < radius.X && Mathf.Abs(difference.Y) < radius.Y)
+                { blocked = true; break; }
+            }
+            if (blocked) continue;
+            landing = point;
+            Status = "destination ready";
+            return true;
+        }
+        Status = "terrain ready; landing blocked";
+        return false;
+    }
+    #endregion
+}
--- a/WORLD/Layers/WorldLayerController.cs
+++ b/WORLD/Layers/WorldLayerController.cs
@@ -1,4 +1,5 @@
-// Coordinates cave transitions, incremental surface registration and exit readiness.
-// Surface simulation stays paused while destination chunks are prepared.
+// Coordinates bidirectional layer connections without owning terrain generation.
+// Surface pausing, landing checks and connection metadata have focused helpers.
 using Godot;
+using System;
 using System.Collections.Generic;
@@ -7,5 +8,4 @@
 {
-
-        #region Configuration
-    [ExportGroup("Exit Loading")]
+    #region Configuration
+    [ExportGroup("Connection Loading")]
     [Export(PropertyHint.Range, "16,256,8")]
@@ -18,12 +18,9 @@
     public WorldLayerRuntime Worlds { get; private set; }
-    private CaveWorld TransitionWorld => Worlds.GetUnderground(
-        Current == WorldLayerId.Surface ? Worlds.SurfaceEntranceLayerId : Current);
-    public WorldLayerDefinition CurrentDefinition =>
-        _config.GetLayerCatalog().Get(Current);
-
-    private Node _world;
-    private Node2D _ground, _objects;
+    public WorldLayerDefinition CurrentDefinition => _config.GetLayerCatalog().Get(Current);
+    public WorldLayerConnection LastSurfaceConnection { get; private set; }
+    public event Action<string, string, WorldLayerConnection> LayerChanged;
+
     private Player _player;
+    private Health _health;
     private Camera2D _camera;
-    private SceneTree _tree;
     private Vector2 _cameraPosition;
@@ -31,19 +28,13 @@
     private ChunkController _surfaceChunks;
+    private WorldLayerSurface _surface;
+    private WorldLayerLanding _landing;
+    private InfiniteWorldGeneration _generation;
     private Label _status;
-
-    private float _surfaceOpacity = 1f;
-    private float _appliedOpacity = float.NaN;
-    private double _landingTimer;
-    private double _hudTimer;
-
-    private CaveHole _lastEntry, _pendingExit;
-    private bool _entryDeparted;
-    private bool _exitReady, _surfaceLoaded;
-    private Vector2 _exitLanding;
-    private string _exitStatus = "";
-
-    private readonly List<WorldLayerMember> _surface = new();
-    private readonly Dictionary<Node, WorldLayerMember> _roots = new();
-    private readonly HashSet<WorldLayerMember> _inheritedFade = new();
-    private readonly Queue<Node> _addedSurface = new();
+    private WorldLayerConnection _pending, _arrival;
+    private bool _ready, _arrivalDeparted;
+    private Vector2 _destination;
+    private double _landingTimer, _hudTimer;
+    private string _previewLayer;
+    private float _previewOpacity;
+    private readonly Dictionary<WorldLayerConnection, IDisposable> _routeLeases = new();
     #endregion
@@ -52,3 +43,3 @@
     // =========================================================
-    // Register the optional layer service.
+    // Register one controller without running before its dependencies exist.
     public override void _EnterTree()
@@ -56,8 +47,2 @@
         AddToGroup("world_layer_controller");
-    }
-
-    // =========================================================
-    // Wait for cave configuration.
-    public override void _Ready()
-    {
         SetProcess(false);
@@ -66,503 +51,253 @@
     // =========================================================
-    // Disconnect listeners and restore managed surface branches.
+    // Bind shared player/services and create small runtime helpers.
+    public void Configure(Node world, Player player, WorldLayerRuntime worlds)
+    {
+        Worlds = worlds; _player = player;
+        _health = player.GetNode<Health>("Systems/Health");
+        _config = WorldConfig.Find(world);
+        _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
+        _generation = InfiniteWorldGeneration.Find(world);
+        _camera = player.GetNode<Camera2D>("Camera2D");
+        _cameraPosition = _camera.Position;
+        _surface = new WorldLayerSurface { Name = "SurfacePresentation" };
+        AddChild(_surface);
+        _surface.Configure(world, player);
+        _landing = new WorldLayerLanding(worlds, world);
+        CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
+        AddChild(hud);
+        _status = new Label { Position = new Vector2(16, 200) };
+        _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
+        hud.AddChild(_status);
+        WorldLayerPursuit.Ensure(this, world);
+        SetProcess(true);
+    }
+
+    // =========================================================
+    // Advance preparation before checking the small physical crossing boundary.
+    public override void _Process(double delta)
+    {
+        if (!GodotObject.IsInstanceValid(_player)) return;
+        if (Current != WorldLayerId.Surface) _surfaceChunks.RetireUnusedChunks();
+        _surface.Drain();
+        _landingTimer -= delta; _hudTimer -= delta;
+        if (!_health.IsAlive && Current != WorldLayerId.Surface) ReturnToSurface();
+
+        WorldLayerConnection approach = FindApproach();
+        if (approach != _pending)
+        {
+            CancelPending();
+            _pending = approach;
+            _landingTimer = 0;
+        }
+        if (_pending != null && !_ready && _landing.Prepare(_pending, Current))
+        {
+            _surface.Drain();
+            if (_landingTimer <= 0)
+            {
+                _landingTimer = 0.25;
+                _ready = _landing.TryReady(_pending, Current, out _destination);
+            }
+        }
+        if (_pending != null && _ready && _health.IsAlive &&
+            InputModes.For(_player).GameplayAllowed && AtCrossing(_pending, _player.GlobalPosition))
+            Transfer(_pending, _destination);
+        UpdatePresentation(delta);
+    }
+
+    // =========================================================
+    // Release protected metadata and restore the shared camera on teardown.
     public override void _ExitTree()
     {
-        if (GodotObject.IsInstanceValid(_tree))
-            _tree.NodeAdded -= OnNodeAdded;
-
-        foreach (WorldLayerMember member in _surface)
-        {
-            if (!GodotObject.IsInstanceValid(member)) continue;
-            member.SetActive(true);
-            member.SetOpacity(1f);
-        }
-
-        if (GodotObject.IsInstanceValid(_camera))
-            _camera.Position = _cameraPosition;
-    }
-
-// =========================================================
-// Connect player, streaming, layer presentation and enemy pursuit.
-public void Configure(Node world, Player player, WorldLayerRuntime worlds)
-{
-    _world = world;
-    _player = player;
-    Worlds = worlds;
-    _ground = world.GetNode<Node2D>("GroundChunks");
-    _objects = world.GetNode<Node2D>("WorldObjects");
-    _config = WorldConfig.Find(world);
-    _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
-    _camera = player.GetNode<Camera2D>("Camera2D");
-    _cameraPosition = _camera.Position;
-
-    _tree = GetTree();
-    _tree.NodeAdded += OnNodeAdded;
-    CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
-    AddChild(hud);
-    _status = new Label { Position = new Vector2(16, 200) };
-    _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
-    hud.AddChild(_status);
-
-    CaveEnemyPursuit.Ensure(this, world);
-    SetProcess(true);
-}
-
-    // =========================================================
-    // Preload nearby exits while exploring, then switch layers at the mouth.
-    public override void _Process(double delta)
-    {
-        if (!GodotObject.IsInstanceValid(_player)) return;
-                if (Current != WorldLayerId.Surface)
-            _surfaceChunks.RetireUnusedChunks();
-
-        _landingTimer -= delta;
-        _hudTimer -= delta;
-
-        Health health = _player.GetNode<Health>("Systems/Health");
-        if (!health.IsAlive && Current != WorldLayerId.Surface)
-            ReturnToSurface();
-
-        Vector2 tile = TransitionWorld.WorldToTile(_player.GlobalPosition);
-
-        if (Current == WorldLayerId.Surface &&
-            health.IsAlive && InputModes.For(_player).GameplayAllowed)
-        {
-            foreach (CaveHole hole in TransitionWorld.Holes)
+        foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
+        _routeLeases.Clear();
+        if (GodotObject.IsInstanceValid(_camera)) _camera.Position = _cameraPosition;
+    }
+    #endregion
+
+    #region Connection Selection
+    // =========================================================
+    // Prefer a connection underfoot, otherwise preload the nearest known endpoint.
+    private WorldLayerConnection FindApproach()
+    {
+        CaveWorld primary = Worlds.SurfaceUnderground;
+        Vector2 tile = primary.WorldToTile(_player.GlobalPosition);
+        WorldLayerConnection best = null;
+        float distance = ExitPreloadDistanceTiles * ExitPreloadDistanceTiles;
+        foreach (WorldLayerConnection connection in Worlds.Connections.ForLayer(Current))
+        {
+            Vector2 local = connection.Coordinates(tile);
+            if (connection == _arrival && Current == connection.LowerLayer && !_arrivalDeparted)
             {
-                Vector2 local = hole.Coordinates(tile);
-                if (local.X >= 0f && local.X < 0.6f &&
-                    Mathf.Abs(local.Y) < 1.4f &&
-                    TransitionWorld.Streaming.EntryReady(hole))
-                {
-                    EnterCave(hole);
-                    tile = TransitionWorld.WorldToTile(_player.GlobalPosition);
-                    break;
-                }
+                if (local.X > 1f || local.X < -0.1f || Mathf.Abs(local.Y) > 2f)
+                    _arrivalDeparted = true;
+                else continue;
             }
-        }
-
+            // Ascending connections can be approached along their full ramp.
+            bool onRamp = Current == connection.LowerLayer &&
+                local.X >= -1.5f && local.X <= connection.TunnelLength &&
+                Mathf.Abs(local.Y) < 1.4f;
+            bool atMouth = local.X >= -2f && local.X <= 0.6f && Mathf.Abs(local.Y) < 1.4f;
+            if (onRamp || atMouth) return connection;
+            float candidate = tile.DistanceSquaredTo(connection.MouthTile);
+            if (candidate >= distance) continue;
+            distance = candidate; best = connection;
+        }
+        return best;
+    }
+
+    // =========================================================
+    // Never transfer from a distant preload: cross only at the actual mouth seam.
+    private bool AtCrossing(WorldLayerConnection connection, Vector2 position)
+    {
+        Vector2 local = connection.Coordinates(Worlds.SurfaceUnderground.WorldToTile(position));
+        return Mathf.Abs(local.Y) < 1.4f && (Current == connection.UpperLayer
+            ? local.X >= 0.05f && local.X <= 0.6f
+            : local.X >= -1.5f && local.X <= -0.45f);
+    }
+
+    // =========================================================
+    // Release only the old destination's temporary preparation request.
+    private void CancelPending()
+    {
+        if (_pending != null)
+        {
+            string target = _pending.Other(Current);
+            if (target != WorldLayerId.Surface) Worlds.CancelPreload(target);
+        }
+        _pending = null; _ready = false;
+    }
+    #endregion
+
+    #region Transfer
+    // =========================================================
+    // Change ownership, collision and movement together once landing is validated.
+    private void Transfer(WorldLayerConnection connection, Vector2 position)
+    {
+        string from = Current, destination = connection.Other(from);
+        if (from == WorldLayerId.Surface)
+        {
+            LastSurfaceConnection = connection;
+            _surface.Pause();
+        }
+        HoldRoute(connection);
+        CancelPending();
+        HidePreview();
+        Worlds.ActivateLayer(destination);
+        _player.GlobalPosition = position;
+        _player.Velocity = Vector2.Zero;
+        _arrival = connection;
+        _arrivalDeparted = destination != connection.LowerLayer;
+        Epoch++;
+        connection.Marker?.QueueRedraw();
+        if (destination == WorldLayerId.Surface)
+        {
+            _surface.Resume();
+            foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
+            _routeLeases.Clear();
+        }
+        _camera.ResetSmoothing();
+        StopMining();
+        WorldLayerPursuit.Find(this)?.PlayerCrossed(connection, destination, _player);
+        GD.Print($"[Layers] {from} -> {destination} through {connection.Id}");
+        LayerChanged?.Invoke(from, destination, connection);
+    }
+
+    // =========================================================
+    // Protect the original surface entrance during a multi-depth journey.
+    private void HoldRoute(WorldLayerConnection connection)
+    {
+        if (connection.UpperLayer != WorldLayerId.Surface || _routeLeases.ContainsKey(connection)) return;
+        _routeLeases.Add(connection, _generation.PinArea(new Rect2(
+            connection.MouthTile - Vector2.One * 4f, Vector2.One * 8f)));
+    }
+
+    // =========================================================
+    // Preserve death/respawn behaviour without treating every exit as surface.
+    public void ReturnToSurface()
+    {
+        if (Current == WorldLayerId.Surface) return;
+        CancelPending(); HidePreview();
+        Worlds.ActivateLayer(WorldLayerId.Surface);
+        if (LastSurfaceConnection != null)
+            _player.GlobalPosition = LastSurfaceConnection.OutsidePosition(Worlds.SurfaceUnderground.TileSize);
+        _player.Velocity = Vector2.Zero;
+        _surface.Resume();
+        foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
+        _routeLeases.Clear();
+        _arrival = null;
+        Epoch++;
+        _camera.Position = _cameraPosition;
+        _camera.ResetSmoothing();
+        StopMining();
+        // Respawn is a reset, not a traversable shortcut for pursuing actors.
+        WorldLayerPursuit.Find(this)?.PlayerCrossed(null, Current, _player);
+    }
+
+    // =========================================================
+    // Stop active mining across a change of collision world.
+    private void StopMining()
+    {
+        foreach (Node node in _player.GetNode("Systems").GetChildren())
+            if (node is MiningEmitter mining) mining.Stop();
+    }
+    #endregion
+
+    #region Presentation
+    // =========================================================
+    // Fade only the departure layer while traversing a lower-owned ramp.
+    private void UpdatePresentation(double delta)
+    {
+        string upper = null;
+        float target = 0f;
+        WorldLayerConnection ramp = null;
         if (Current != WorldLayerId.Surface)
         {
-            CaveHole approach = TransitionWorld.TransitionAt(tile);
-
-            // Avoid treating the initial descent as an exit request.
-            // Turning back immediately still allows returning to the surface.
-            if (!_entryDeparted && _lastEntry != null)
+            CaveWorld cave = Worlds.GetUnderground(Current);
+            Vector2 tile = cave.WorldToTile(_player.GlobalPosition);
+            ramp = cave.TransitionAt(tile);
+            if (ramp != null)
             {
-                float along = _lastEntry.Coordinates(tile).X;
-                if (approach != _lastEntry || along > 1f)
-                    _entryDeparted = true;
-                else if (along >= -0.1f)
-                    approach = null;
+                float descent = Mathf.Clamp(ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f);
+                upper = ramp.UpperLayer;
+                target = Mathf.Clamp(_config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f) * (1f - descent);
             }
-
-            // Outside a ramp, start loading the nearest nearby exit.
-            // A ramp always takes priority over distance-based selection.
-            if (approach == null)
-            {
-                float radius = Mathf.Clamp(
-                    ExitPreloadDistanceTiles, 16f, 256f);
-                float nearestDistance = radius * radius;
-
-                foreach (CaveHole hole in TransitionWorld.Holes)
-                {
-                    if (!_entryDeparted && hole == _lastEntry)
-                        continue;
-
-                    float distance = tile.DistanceSquaredTo(hole.MouthTile);
-                    if (distance >= nearestDistance) continue;
-
-                    nearestDistance = distance;
-                    approach = hole;
-                }
-            }
-
-            if (approach != _pendingExit)
-            {
-                _pendingExit = approach;
-                _exitReady = false;
-                _surfaceLoaded = false;
-                _landingTimer = 0;
-                _exitStatus = "";
-            }
-
-            if (_pendingExit != null && !_exitReady)
-            {
-                if (!_surfaceLoaded)
-                {
-                    _surfaceLoaded = _surfaceChunks.PrepareDestination(
-                        _pendingExit.OutsidePosition(TransitionWorld.TileSize));
-
-                    DrainAddedSurface();
-
-                    if (!_surfaceLoaded)
-                        _exitStatus = _surfaceChunks.GetMeta(
-                            "destination_preload_status",
-                            "loading surface").AsString();
-                }
-
-                if (_surfaceLoaded && _landingTimer <= 0)
-                {
-                    _landingTimer = 0.25;
-                    _exitReady = TrySurfaceLanding(
-                        _pendingExit, out _exitLanding);
-
-                    _exitStatus = _exitReady
-                        ? "surface ready"
-                        : "surface loaded; no safe landing found";
-                }
-            }
-
-            DrainAddedSurface();
-
-            if (_pendingExit != null && _exitReady &&
-                health.IsAlive && InputModes.For(_player).GameplayAllowed)
-            {
-                Vector2 local = _pendingExit.Coordinates(tile);
-
-                // Preloading from far away must never teleport the player.
-                // Switch only within the small apron at the actual mouth.
-                if (local.X >= -1.5f && local.X <= -0.45f &&
-                    Mathf.Abs(local.Y) < 1.4f)
-                {
-                    string id = _pendingExit.Id;
-                    RestoreSurface(_exitLanding);
-                    GD.Print($"[Layers] Exited through hole {id}.");
-                }
-            }
-        }
-
-        UpdatePresentation(delta);
-    }
-    #endregion
-
-    #region Incremental Surface Registration
-// =========================================================
-// Queue surface scenery while leaving independently managed actors alone.
-private void OnNodeAdded(Node node)
-{
-    if (Current == WorldLayerId.Surface || node is WorldLayerMember)
-        return;
-
-    bool surface = _ground.IsAncestorOf(node) || _objects.IsAncestorOf(node);
-
-    for (Node parent = node; parent != null; parent = parent.GetParent())
-    {
-        if (parent is Player || parent is Entity || parent is Projectile)
-            return;
-        if (_roots.ContainsKey(parent)) surface = true;
-    }
-
-    if (surface) _addedSurface.Enqueue(node);
-}
-
-    // =========================================================
-    // Pause new branches without revisiting existing surface hierarchies.
-    private void DrainAddedSurface()
-    {
-        while (_addedSurface.Count > 0)
-        {
-            Node node = _addedSurface.Dequeue();
-            if (!GodotObject.IsInstanceValid(node) ||
-                node.IsQueuedForDeletion() || !node.IsInsideTree())
-                continue;
-
-            bool covered = false;
-            for (Node parent = node; parent != null; parent = parent.GetParent())
-            {
-                if (!_roots.TryGetValue(parent, out WorldLayerMember member))
-                    continue;
-                covered = member.Covers(node);
-                break;
-            }
-
-            if (covered) continue;
-
-            WorldLayerMember added = RegisterRoot(node);
-            added.SetActive(false);
-            if (!_inheritedFade.Contains(added))
-                added.SetOpacity(_surfaceOpacity);
-        }
-    }
-
-    // =========================================================
-    // Register a branch and detect inherited canvas fading.
-    private WorldLayerMember RegisterRoot(Node node)
-    {
-        if (_roots.TryGetValue(node, out WorldLayerMember existing))
-            return existing;
-
-        bool inherited = false;
-        for (Node parent = node.GetParent(); parent != null; parent = parent.GetParent())
-        {
-            if (parent is CanvasItem && _roots.ContainsKey(parent))
-            {
-                inherited = true;
-                break;
-            }
-        }
-
-        WorldLayerMember member = WorldLayerMember.Attach(node, WorldLayerId.Surface);
-        _roots.Add(node, member);
-        _surface.Add(member);
-        if (inherited) _inheritedFade.Add(member);
-        return member;
-    }
-
-    // =========================================================
-    // Include branches independently registered during earlier visits.
-    private void RegisterExistingBranches(Node node)
-    {
-        if (node is WorldLayerMember || node.IsQueuedForDeletion()) return;
-
-        WorldLayerMember member =
-            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember");
-        if (member != null && member.Layer == WorldLayerId.Surface)
-            RegisterRoot(node);
-
-        foreach (Node child in node.GetChildren())
-            RegisterExistingBranches(child);
-    }
-
-// =========================================================
-// Pause scenery and population work while pursuit owns enemy activation.
-private void CaptureSurface()
-{
-    _surface.Clear();
-    _roots.Clear();
-    _inheritedFade.Clear();
-    _addedSurface.Clear();
-
-    RegisterRoot(_ground);
-    RegisterExistingBranches(_ground);
-
-    foreach (Node child in _objects.GetChildren())
-    {
-        if (child == _player || child is Entity || child is Projectile ||
-            child.IsQueuedForDeletion())
-            continue;
-
-        RegisterRoot(child);
-        RegisterExistingBranches(child);
-    }
-
-    Node systems = _world.GetNode("Systems");
-    foreach (string name in new[]
-    {
-        "ChunkController", "EnemyPopulation", "Atmosphere",
-        "GroundFog", "VegetationInteraction", "Surfaces"
-    })
-    {
-        Node node = systems.GetNodeOrNull<Node>(name);
-        if (node == null) continue;
-        RegisterRoot(node);
-        RegisterExistingBranches(node);
-    }
-
-    Node fading = _config.GetNodeOrNull<Node>("PlayerObstructionFade");
-    if (fading != null) RegisterRoot(fading);
-}
-    #endregion
-
-    #region Switching And Landing
- // =========================================================
-// Enter ready cave ground and tell existing pursuers which entrance was used.
-private void EnterCave(CaveHole hole)
-{
-    if (!TransitionWorld.Streaming.EntryReady(hole)) return;
-
-    Vector2 local = hole.Coordinates(TransitionWorld.WorldToTile(_player.GlobalPosition));
-    Vector2 entry = TransitionWorld.TileToWorld(
-        hole.TileAt(Mathf.Clamp(local.X, 0f, 0.5f)));
-    if (!TransitionWorld.Streaming.IsAvailable(entry)) return;
-
-    CaptureSurface();
-
-    foreach (WorldLayerMember member in _surface)
-        member.SetOpacity(1f);
-    foreach (WorldLayerMember member in _surface)
-        member.SetActive(false);
-
-    _player.GlobalPosition = entry;
-    _player.Velocity = Vector2.Zero;
-    _lastEntry = hole;
-    _entryDeparted = false;
-    _pendingExit = null;
-    _exitReady = false;
-    _surfaceLoaded = false;
-    _appliedOpacity = float.NaN;
-
-    Worlds.ActivateLayer(TransitionWorld.LayerId);
-    Epoch++;
-
-    CaveEnemyPursuit.Find(this)?.PlayerCrossed(
-        TransitionWorld, hole, Current, _player);
-
-    StopMining();
-    GD.Print($"[Layers] Entered through hole {hole.Id}.");
-}
-
-    // =========================================================
-    // Preserve the existing death and respawn integration.
-    public void ReturnToSurface()
-    {
-        if (Current == WorldLayerId.Surface) return;
-        RestoreSurface(_lastEntry != null
-            ? _lastEntry.OutsidePosition(TransitionWorld.TileSize)
-            : _player.GlobalPosition);
-    }
-
-// =========================================================
-// Restore the surface and preserve a route for enemies following out.
-private void RestoreSurface(Vector2 landing)
-{
-    CaveHole crossed = TransitionWorld.NearestSurfaceHole(landing);
-
-    DrainAddedSurface();
-    Worlds.ActivateLayer(WorldLayerId.Surface);
-
-    _player.GlobalPosition = landing;
-    _player.Velocity = Vector2.Zero;
-
-    foreach (WorldLayerMember member in _surface)
-    {
-        if (!GodotObject.IsInstanceValid(member)) continue;
-        member.SetActive(true);
-        if (!_inheritedFade.Contains(member))
-            member.SetOpacity(_surfaceOpacity);
-    }
-
-    Epoch++;
-    _pendingExit = null;
-    _exitReady = false;
-    _surfaceLoaded = false;
-    _appliedOpacity = float.NaN;
-    _camera.Position = _cameraPosition;
-    _camera.ResetSmoothing();
-
-    CaveEnemyPursuit.Find(this)?.PlayerCrossed(
-        TransitionWorld, crossed, Current, _player);
-
-    StopMining();
-}
-
-    // =========================================================
-    // Check ready terrain and obstacle footprints without disabled physics queries.
-    private bool TrySurfaceLanding(CaveHole hole, out Vector2 landing)
-    {
-        Vector2 centre = hole.OutsidePosition(TransitionWorld.TileSize);
-        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(_objects);
-
-        for (int i = 0; i <= 8; i++)
-        {
-            Vector2 point = i == 0 ? centre :
-                centre + Vector2.FromAngle(Mathf.Tau * (i - 1) / 8f) * 24f;
-
-            if (!_surfaceChunks.IsNavigationPointAvailable(point, 14f))
-                continue;
-
-            bool blocked = false;
-            foreach (Obstacle obstacle in obstacles)
-            {
-                if (!GodotObject.IsInstanceValid(obstacle) ||
-                    obstacle.IsQueuedForDeletion()) continue;
-
-                Vector2 difference = point - obstacle.GlobalPosition;
-                Vector2 separation = obstacle.Footprint * 0.5f + new Vector2(18f, 18f);
-                if (Mathf.Abs(difference.X) < separation.X &&
-                    Mathf.Abs(difference.Y) < separation.Y)
-                {
-                    blocked = true;
-                    break;
-                }
-            }
-
-            if (blocked) continue;
-            landing = point;
-            return true;
-        }
-
-        landing = centre;
-        return false;
-    }
-
-    // =========================================================
-    // Stop mining without depending on the component's node name.
-    private void StopMining()
-    {
-        foreach (Node node in _player.GetNode("Systems").GetChildren())
-            if (node is MiningEmitter mining)
-                mining.Stop();
-    }
-    #endregion
-
-    #region Presentation
-    // =========================================================
-    // Fade only when opacity changes and refresh diagnostic text periodically.
-    private void UpdatePresentation(double delta)
-    {
-        float target = 1f;
-        if (Current != WorldLayerId.Surface)
-        {
-            Vector2 tile = TransitionWorld.WorldToTile(_player.GlobalPosition);
-            CaveHole ramp = TransitionWorld.TransitionAt(tile);
-            float descent = ramp != null
-                ? Mathf.Clamp(ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f)
-                : 1f;
-
-            target = Mathf.Clamp(
-                _config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f) * (1f - descent);
-
-            float reference = ramp != null
-                ? Mathf.Lerp(ramp.RimHeight, TransitionWorld.RimHeight, descent)
-                : TransitionWorld.RimHeight;
+            float reference = ramp?.UpperLayer == WorldLayerId.Surface
+                ? ramp.RimHeight * (1f - Mathf.Clamp(
+                    ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f)) : 0f;
             _camera.Position = _cameraPosition + Vector2.Down *
-                (reference - TransitionWorld.Elevation.SampleWorldHeight(_player.GlobalPosition));
-        }
-        else
-            _camera.Position = _cameraPosition;
-
-        _surfaceOpacity = Mathf.MoveToward(
-            _surfaceOpacity, target,
+                (reference - cave.Elevation.SampleWorldHeight(_player.GlobalPosition));
+        }
+        else _camera.Position = _cameraPosition;
+
+        if (_previewLayer != upper)
+        {
+            HidePreview(); _previewLayer = upper;
+        }
+        _previewOpacity = Mathf.MoveToward(_previewOpacity, target,
             (float)delta / Mathf.Max(0.05f, _config.ObstructionFadeSeconds));
-
-        if (float.IsNaN(_appliedOpacity) ||
-            !Mathf.IsEqualApprox(_appliedOpacity, _surfaceOpacity))
-        {
-            _appliedOpacity = _surfaceOpacity;
-            foreach (WorldLayerMember member in _surface)
-                if (GodotObject.IsInstanceValid(member) &&
-                    !_inheritedFade.Contains(member))
-                    member.SetOpacity(_surfaceOpacity);
-        }
-
-        foreach (CaveWorld world in Worlds.UndergroundWorlds)
-        foreach (CaveHole hole in world.Holes)
-        {
-            if (!GodotObject.IsInstanceValid(hole.Marker)) continue;
-            bool visible = Current == WorldLayerId.Surface
-                ? world.LayerId == Worlds.SurfaceEntranceLayerId
-                : world.LayerId == Current &&
-                    (hole == _pendingExit || (!_entryDeparted && hole == _lastEntry));
-            if (hole.Marker.Visible != visible)
-                hole.Marker.Visible = visible;
-        }
+        _surface.SetOpacity(Current == WorldLayerId.Surface ? 1f :
+            upper == WorldLayerId.Surface ? _previewOpacity : 0f);
+        if (upper != null && upper != WorldLayerId.Surface)
+            Worlds.GetUnderground(upper).SetPreview(_previewOpacity);
 
         if (_hudTimer > 0) return;
-                PruneRetiredSurface();
         _hudTimer = 0.2;
-
-        if (Current != WorldLayerId.Surface)
-            _status.Text =
-                $"{CurrentDefinition.DisplayName} | {TransitionWorld.Streaming.ReadyCount}/{TransitionWorld.Streaming.LoadedCount} chunks\n" +
-                (_pendingExit == null ? "Explore the connected network" :
-                    $"Hole {_pendingExit.Id}: {_exitStatus}") +
-                " | M map is surface-only";
-        else
-        {
-            CaveHole nearest = TransitionWorld.NearestSurfaceHole(_player.GlobalPosition);
-            _status.Text = $"{CurrentDefinition.DisplayName} | Nearest hole: {nearest?.Id}\n" +
-                (TransitionWorld.Streaming.EntryReady(nearest)
-                    ? "Cave entrance ready" : "Preparing underground entrance...");
-        }
+        _surface.Prune();
+        foreach (WorldLayerConnection connection in Worlds.Connections.All)
+            if (GodotObject.IsInstanceValid(connection.Marker))
+                connection.Marker.Visible = Current == connection.UpperLayer ||
+                    (Current == connection.LowerLayer && connection == ramp);
+        _status.Text = $"{CurrentDefinition.DisplayName}\n" +
+            (_pending == null ? "Explore the connected world" :
+                $"{_pending.Id}: {(_ready ? "destination ready" : _landing.Status)}") +
+            (Current == WorldLayerId.Surface ? "" : " | M map is surface-only");
+    }
+
+    // =========================================================
+    // Remove departure previews without reactivating their simulation.
+    private void HidePreview()
+    {
+        if (_previewLayer != null && _previewLayer != WorldLayerId.Surface)
+            Worlds.GetUnderground(_previewLayer).SetPreview(0f);
+        _previewLayer = null; _previewOpacity = 0f;
     }
@@ -572,20 +307,16 @@
     // =========================================================
-    // Restrict cave movement to available floor, allowing axis sliding.
+    // Keep the shared player's footprint inside ready terrain, allowing axis sliding.
     public Vector2 ConstrainVelocity(Vector2 position, Vector2 velocity, double delta)
     {
-        if (Current == WorldLayerId.Surface || delta <= 0) return velocity;
-
+        if (delta <= 0) return velocity;
         Vector2 motion = velocity * (float)delta;
         if (CanTravel(position, motion)) return velocity;
-
-        bool canX = CanTravel(position, new Vector2(motion.X, 0f));
-        bool canY = CanTravel(position, new Vector2(0f, motion.Y));
-        if (canX && (!canY || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y)))
-            return new Vector2(velocity.X, 0f);
-        if (canY) return new Vector2(0f, velocity.Y);
-        return Vector2.Zero;
-    }
-
-    // =========================================================
-    // Hold the outward boundary until the matching surface exit is ready.
+        bool x = CanTravel(position, new Vector2(motion.X, 0f));
+        bool y = CanTravel(position, new Vector2(0f, motion.Y));
+        if (x && (!y || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y))) return new Vector2(velocity.X, 0f);
+        return y ? new Vector2(0f, velocity.Y) : Vector2.Zero;
+    }
+
+    // =========================================================
+    // Hold any connection seam until that exact destination has a safe landing.
     private bool CanTravel(Vector2 position, Vector2 motion)
@@ -594,3 +325,2 @@
         if (steps > 64) return false;
-
         for (int i = 1; i <= steps; i++)
@@ -598,13 +328,5 @@
             Vector2 point = position + motion * ((float)i / steps);
-            if (!TransitionWorld.Streaming.IsAvailable(point)) return false;
-
-            Vector2 tile = TransitionWorld.WorldToTile(point);
-            foreach (CaveHole hole in TransitionWorld.Holes)
-            {
-                Vector2 local = hole.Coordinates(tile);
-                if (local.X < -0.5f && local.X >= -1.5f &&
-                    Mathf.Abs(local.Y) < 1.4f &&
-                    (!_exitReady || _pendingExit != hole))
-                    return false;
-            }
+            if (Current != WorldLayerId.Surface && !Worlds.IsAvailable(Current, point, 14f)) return false;
+            foreach (WorldLayerConnection connection in Worlds.Connections.ForLayer(Current))
+                if (AtCrossing(connection, point) && (connection != _pending || !_ready)) return false;
         }
@@ -616,12 +338,11 @@
     // =========================================================
-    // Find the optional layer controller.
+    // Resolve the gameplay world's optional layer controller.
     public static WorldLayerController Find(Node context)
     {
-        if (context == null || !context.IsInsideTree()) return null;
-        return context.GetTree().GetFirstNodeInGroup(
-            "world_layer_controller") as WorldLayerController;
-    }
-
-    // =========================================================
-    // Select the appropriate elevation provider.
+        return context == null || !context.IsInsideTree() ? null :
+            context.GetTree().GetFirstNodeInGroup("world_layer_controller") as WorldLayerController;
+    }
+
+    // =========================================================
+    // Route artwork and aiming to the actor's exact elevation provider.
     public static float HeightFor(Node owner, Vector2 point)
@@ -630,11 +351,5 @@
         if (layer != WorldLayerId.Surface)
-        {
-            WorldLayerRuntime runtime = WorldLayerRuntime.Find(owner)
-                ?? throw new System.InvalidOperationException(
-                    $"No layer runtime for '{layer}'.");
-            return runtime.GetUnderground(layer).Elevation.SampleWorldHeight(point);
-        }
-
-        TerrainElevation elevation = owner.GetTree().GetFirstNodeInGroup(
-            "terrain_elevation") as TerrainElevation;
+            return (WorldLayerRuntime.Find(owner) ?? throw new InvalidOperationException(
+                $"No runtime for '{layer}'.")).GetUnderground(layer).Elevation.SampleWorldHeight(point);
+        TerrainElevation elevation = owner.GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;
         return elevation?.SampleWorldHeight(point) ?? 0f;
@@ -643,3 +358,3 @@
     // =========================================================
-    // Route player-created drops into the active layer.
+    // Keep player-created drops in the active world's object root.
     public static Node2D DropRoot(Node context, Node2D surfaceRoot)
@@ -650,22 +365,2 @@
     #endregion
-
-        // =========================================================
-    // Remove retired surface registrations during long underground journeys.
-    private void PruneRetiredSurface()
-    {
-        List<Node> remove = new();
-
-        foreach (var pair in _roots)
-            if (!GodotObject.IsInstanceValid(pair.Key) ||
-                !GodotObject.IsInstanceValid(pair.Value))
-                remove.Add(pair.Key);
-
-        foreach (Node node in remove)
-        {
-            WorldLayerMember member = _roots[node];
-            _roots.Remove(node);
-            _surface.Remove(member);
-            _inheritedFade.Remove(member);
-        }
-    }
 }
--- /dev/null
+++ b/WORLD/Layers/WorldLayerPursuit.cs
@@ -0,0 +1,343 @@
+// Manages layer activation for living entities and keeps existing pursuers active.
+// Actors cross known layer connections rather than teleporting through terrain.
+using Godot;
+using System;
+using System.Collections.Generic;
+
+public partial class WorldLayerPursuit : Node
+{
+    #region State
+    private sealed class Record
+    {
+        public Entity Actor;
+        public WorldLayerMember Member;
+        public WorldLayerConnection Portal;
+        public string TargetLayer;
+        public IDisposable Lease;
+        public bool Simulating = true;
+        public bool Colliding = true;
+    }
+
+    private WorldLayerController _layers;
+    private Node2D _surfaceGround;
+    private ChunkController _surfaceChunks;
+    private InfiniteWorldGeneration _generation;
+    private WorldLayerLanding _landing;
+    private Node2D _sharedActors;
+    private double _timer;
+
+    private readonly Dictionary<Entity, Record> _records = new();
+    private readonly List<Entity> _remove = new();
+    #endregion
+
+    #region Lifecycle
+    // =========================================================
+    // Register the shared pursuit service.
+    public override void _EnterTree()
+    {
+        AddToGroup("world_layer_pursuit");
+    }
+
+    // =========================================================
+    // Create pursuit coordination; each runtime world owns its navigation.
+    public static void Ensure(WorldLayerController layers, Node world)
+    {
+        if (Find(layers) != null) return;
+
+        WorldLayerPursuit helper = new()
+        {
+            Name = "EnemyPursuit",
+            _layers = layers,
+            _surfaceGround = world.GetNode<Node2D>("GroundChunks"),
+            _surfaceChunks = world.GetNode<ChunkController>(
+                "Systems/ChunkController"),
+            _generation = InfiniteWorldGeneration.Find(world),
+            _landing = new WorldLayerLanding(layers.Worlds, world),
+            _sharedActors = world.GetNode<Node2D>("WorldObjects")
+        };
+        layers.AddChild(helper);
+    }
+
+    // =========================================================
+    // Find the optional service without introducing a second enemy system.
+    public static WorldLayerPursuit Find(Node context)
+    {
+        if (context == null || !context.IsInsideTree()) return null;
+        return context.GetTree().GetFirstNodeInGroup(
+            "world_layer_pursuit") as WorldLayerPursuit;
+    }
+
+    // =========================================================
+    // Refresh the live entity set and layer presentation at ten hertz.
+    public override void _Process(double delta)
+    {
+        _timer -= delta;
+        if (_timer > 0.0) return;
+        _timer = 0.1;
+
+        TrackActors();
+        _remove.Clear();
+
+        foreach (var pair in _records)
+        {
+            Record record = pair.Value;
+            if (!GodotObject.IsInstanceValid(record.Actor) ||
+                record.Actor.IsQueuedForDeletion())
+            {
+                ClearPortal(record);
+                _remove.Add(pair.Key);
+                continue;
+            }
+
+            UpdateRecord(record);
+        }
+
+        foreach (Entity actor in _remove)
+            _records.Remove(actor);
+    }
+
+    // =========================================================
+    // Release entrance protection and restore surviving actors during teardown.
+    public override void _ExitTree()
+    {
+        foreach (Record record in _records.Values)
+        {
+            ClearPortal(record);
+            if (!GodotObject.IsInstanceValid(record.Member) ||
+                !GodotObject.IsInstanceValid(record.Actor) ||
+                record.Actor.IsQueuedForDeletion())
+                continue;
+
+            record.Member.SetPhysicsEnabled(true);
+            record.Member.SetActive(true);
+            record.Member.SetOpacity(1f);
+        }
+        _records.Clear();
+    }
+    #endregion
+
+    #region Ownership And Presentation
+    // =========================================================
+    // Capture each activated entity once, independently from surface scenery.
+    private void TrackActors()
+    {
+        foreach (Node node in GetTree().GetNodesInGroup("entities"))
+        {
+            if (node is not Entity actor || _records.ContainsKey(actor) ||
+                actor.IsQueuedForDeletion() || !actor.Initialized ||
+                !actor.IsActivated || actor.SpawnPending ||
+                actor.Health?.IsAlive != true)
+                continue;
+
+            string layer = WorldLayerMember.For(actor);
+            WorldLayerMember member = WorldLayerMember.Attach(actor, layer);
+            // Living actors use a neutral parent so a hidden departure root cannot hide a pursuer.
+            if (actor.GetParent() != _sharedActors) actor.Reparent(_sharedActors, true);
+
+            // Populate the helper's original collision and presentation snapshot.
+            member.SetActive(false);
+            member.SetActive(true);
+
+            _records.Add(actor, new Record
+            {
+                Actor = actor,
+                Member = member
+            });
+        }
+    }
+
+    // =========================================================
+    // Keep hidden pursuers thinking, but disable their physical interaction.
+    private void UpdateRecord(Record record)
+    {
+        Entity actor = record.Actor;
+        if (actor.Health?.IsAlive != true) return;
+
+        if (!actor.HasTarget)
+            ClearPortal(record);
+        else
+        {
+            string targetLayer = WorldLayerMember.For(actor.Target);
+            if (record.TargetLayer != targetLayer) SelectPortal(record, targetLayer);
+            if (record.Portal != null) TryCross(record);
+        }
+
+        bool visible = record.Member.Layer == _layers.Current;
+        bool simulate = visible || actor.HasTarget;
+
+        if (simulate != record.Simulating)
+        {
+            // Capture original collision settings before pausing, not hidden zeros.
+            if (!simulate)
+                record.Member.SetPhysicsEnabled(true);
+
+            record.Member.SetActive(simulate);
+            record.Simulating = simulate;
+            record.Colliding = simulate;
+        }
+
+        bool collide = simulate && visible;
+        if (collide != record.Colliding)
+        {
+            record.Member.SetPhysicsEnabled(collide);
+            record.Colliding = collide;
+        }
+
+        record.Member.SetOpacity(visible ? 1f : 0f);
+    }
+    #endregion
+
+    #region Entrance Pursuit
+    // =========================================================
+    // Update pursuit routes for entities already tracking this player.
+    public void PlayerCrossed(WorldLayerConnection connection, string destination, Player player)
+    {
+        TrackActors();
+        foreach (Record record in _records.Values)
+        {
+            Entity actor = record.Actor;
+            if (!GodotObject.IsInstanceValid(actor) || actor.IsQueuedForDeletion()) continue;
+            if (actor.HasTarget && actor.Target == player)
+            {
+                actor.ResetPursuitMovement();
+                ClearPortal(record);
+                if (connection != null) SelectPortal(record, destination);
+            }
+            // Include passive wildlife immediately, rather than waiting for the next refresh.
+            UpdateRecord(record);
+        }
+    }
+
+    // =========================================================
+    // Select the next adjacent connection, even when the target has moved two depths away.
+    private void SelectPortal(Record record, string destination)
+    {
+        ClearPortal(record);
+        record.TargetLayer = destination;
+        record.Portal = _layers.Worlds.Connections.Next(record.Member.Layer,
+            destination, record.Actor.GlobalPosition);
+        if (record.Portal?.UpperLayer == WorldLayerId.Surface)
+            record.Lease = _generation.PinArea(new Rect2(
+                record.Portal.MouthTile - Vector2.One * 4f, Vector2.One * 8f));
+    }
+
+    // =========================================================
+    // Supply a reachable mouth goal instead of the target's other-layer position.
+    public bool TryGetGoal(Entity actor, out Vector2 goal)
+    {
+        goal = actor.GlobalPosition;
+        if (!_records.TryGetValue(actor, out Record record) ||
+            record.Portal == null || !actor.HasTarget ||
+            WorldLayerMember.Same(actor, actor.Target))
+            return false;
+
+        goal = record.Member.Layer == record.Portal.UpperLayer
+            ? record.Portal.UpperPosition : record.Portal.OutsidePosition(
+                _layers.Worlds.GetUnderground(record.Portal.LowerLayer).TileSize);
+        return true;
+    }
+
+    // =========================================================
+    // Cross one adjacent connection only at its mouth and after safe destination preparation.
+    private void TryCross(Record record)
+    {
+        Entity actor = record.Actor;
+        WorldLayerConnection connection = record.Portal;
+        string from = record.Member.Layer;
+        if (from != connection.UpperLayer && from != connection.LowerLayer)
+        { ClearPortal(record); return; }
+        if (from == WorldLayerMember.For(actor.Target))
+        { ClearPortal(record); return; }
+        Vector2 mouth = from == connection.UpperLayer ? connection.UpperPosition :
+            connection.OutsidePosition(_layers.Worlds.GetUnderground(connection.LowerLayer).TileSize);
+        if (actor.GlobalPosition.DistanceSquaredTo(mouth) > 20f * 20f) return;
+        if (!_landing.Prepare(connection, from) ||
+            !_landing.TryReady(connection, from, out Vector2 landing)) return;
+        actor.CrossWorldLayer(connection.Other(from), landing);
+        ClearPortal(record);
+        actor.ResetPursuitMovement();
+    }
+
+    // =========================================================
+    // Release the cached entrance once pursuit no longer needs it.
+    private static void ClearPortal(Record record)
+    {
+        record.Lease?.Dispose();
+        record.Lease = null;
+        record.Portal = null;
+        record.TargetLayer = null;
+    }
+    #endregion
+
+    #region Streaming Protection
+    // =========================================================
+    // Keep the already-loaded surface approach until its pursuer crosses.
+    public bool RetainSurface(Vector2I coordinate)
+    {
+        Rect2 chunk = new(
+            new Vector2(coordinate.X * _surfaceChunks.ChunkSize,
+                coordinate.Y * _surfaceChunks.ChunkSize),
+            Vector2.One * _surfaceChunks.ChunkSize);
+
+        foreach (Record record in _records.Values)
+        {
+            if (!IsPursuing(record) ||
+                record.Member.Layer != WorldLayerId.Surface ||
+                record.Portal == null)
+                continue;
+
+            Vector2 from = IsoGrid.WorldToTile(
+                _surfaceGround.ToLocal(record.Actor.GlobalPosition),
+                _surfaceChunks.TileSize);
+            Vector2 to = IsoGrid.WorldToTile(
+                _surfaceGround.ToLocal(record.Portal.UpperPosition),
+                _surfaceChunks.TileSize);
+
+            if (new Rect2(from, Vector2.Zero).Expand(to)
+                .Grow(_surfaceChunks.ChunkSize).Intersects(chunk))
+                return true;
+        }
+        return false;
+    }
+
+    // =========================================================
+    // Protect loaded cave floor along an active pursuer's local route.
+    public bool RetainCave(CaveWorld world, Vector2I coordinate)
+    {
+        int size = world.Settings.ChunkSize;
+        Rect2 chunk = new(
+            new Vector2(coordinate.X * size, coordinate.Y * size),
+            Vector2.One * size);
+
+        foreach (Record record in _records.Values)
+        {
+            if (!IsPursuing(record) ||
+                record.Member.Layer != world.LayerId)
+                continue;
+
+            Vector2 from = world.WorldToTile(
+                record.Actor.GlobalPosition);
+            Vector2 destination = record.Portal != null
+                ? (record.Member.Layer == record.Portal.UpperLayer ? record.Portal.UpperPosition :
+                    record.Portal.OutsidePosition(_layers.Worlds.GetUnderground(record.Portal.LowerLayer).TileSize))
+                : record.Actor.Target.GlobalPosition;
+            Vector2 to = world.WorldToTile(destination);
+
+            if (new Rect2(from, Vector2.Zero).Expand(to)
+                .Grow(size).Intersects(chunk))
+                return true;
+        }
+        return false;
+    }
+
+    // =========================================================
+    // Ignore retired actors when checking temporary streaming protection.
+    private static bool IsPursuing(Record record)
+    {
+        return GodotObject.IsInstanceValid(record.Actor) &&
+            !record.Actor.IsQueuedForDeletion() &&
+            record.Actor.Health?.IsAlive == true &&
+            record.Actor.HasTarget;
+    }
+    #endregion
+}
--- a/WORLD/Layers/WorldLayerRuntime.cs
+++ b/WORLD/Layers/WorldLayerRuntime.cs
@@ -11,2 +11,3 @@
     public string SurfaceEntranceLayerId { get; private set; }
+    public WorldLayerConnections Connections { get; private set; }
     public CaveWorld SurfaceUnderground => GetUnderground(SurfaceEntranceLayerId);
@@ -36,2 +37,3 @@
         SurfaceEntranceLayerId = catalog.SurfaceEntranceLayerId;
+        Connections = new WorldLayerConnections(this);
         _surfaceObjects = world.GetNode<Node2D>("WorldObjects");
@@ -52,7 +54,6 @@
             AddChild(cave);
-            cave.Build(ground.GlobalPosition, chunks.TileSize, 0f,
+            cave.Build(ground.GlobalPosition, chunks.TileSize,
                 SeedFor(chunks.WorldSeed, definition.Id),
                 surfaceDestination ? surfacePlanner.Settings
-                    : definition.CreateCaveSettings(),
-                Array.Empty<CaveHole>());
+                    : definition.CreateCaveSettings());
             cave.Streaming.ConfigurePlayer(player);
@@ -135,2 +136,3 @@
         {
+            cave.Streaming.CancelPreload();
             cave.SetActive(cave.LayerId == id);
--- /dev/null
+++ b/WORLD/Layers/WorldLayerSurface.cs
@@ -0,0 +1,231 @@
+// Pauses and fades surface scenery while shared connections handle traversal.
+// New streamed branches are captured incrementally; actors remain pursuit-owned.
+using Godot;
+using System.Collections.Generic;
+
+public partial class WorldLayerSurface : Node
+{
+    #region State
+    private Node _world;
+    private Node2D _ground, _objects;
+    private Player _player;
+    private WorldConfig _config;
+    private SceneTree _tree;
+    private bool _paused;
+    private float _surfaceOpacity = 1f;
+    private readonly List<WorldLayerMember> _surface = new();
+    private readonly Dictionary<Node, WorldLayerMember> _roots = new();
+    private readonly HashSet<WorldLayerMember> _inheritedFade = new();
+    private readonly Queue<Node> _addedSurface = new();
+    #endregion
+
+    #region Lifecycle
+    // =========================================================
+    // Bind existing branches without creating another surface world.
+    public void Configure(Node world, Player player)
+    {
+        _world = world; _player = player;
+        _ground = world.GetNode<Node2D>("GroundChunks");
+        _objects = world.GetNode<Node2D>("WorldObjects");
+        _config = WorldConfig.Find(world);
+        _tree = GetTree();
+        _tree.NodeAdded += OnNodeAdded;
+        SetProcess(false);
+    }
+
+    // =========================================================
+    // Pause surface simulation once on leaving surface.
+    public void Pause()
+    {
+        if (_paused) return;
+        CaptureSurface();
+        _paused = true;
+        foreach (WorldLayerMember member in _surface) member.SetActive(false);
+    }
+
+    // =========================================================
+    // Restore captured process and physics settings on returning.
+    public void Resume()
+    {
+        Drain();
+        _paused = false;
+        foreach (WorldLayerMember member in _surface)
+        {
+            if (!GodotObject.IsInstanceValid(member)) continue;
+            member.SetActive(true);
+        }
+        SetOpacity(1f);
+    }
+
+    // =========================================================
+    // Write presentation only when the requested opacity actually changes.
+    public void SetOpacity(float opacity)
+    {
+        if (Mathf.IsEqualApprox(_surfaceOpacity, opacity)) return;
+        _surfaceOpacity = opacity;
+        foreach (WorldLayerMember member in _surface)
+            if (GodotObject.IsInstanceValid(member) && !_inheritedFade.Contains(member))
+                member.SetOpacity(opacity);
+    }
+
+    // =========================================================
+    // Restore managed branches and disconnect the scene listener.
+    public override void _ExitTree()
+    {
+        if (GodotObject.IsInstanceValid(_tree)) _tree.NodeAdded -= OnNodeAdded;
+        foreach (WorldLayerMember member in _surface)
+            if (GodotObject.IsInstanceValid(member))
+            {
+                member.SetActive(true); member.SetOpacity(1f);
+            }
+    }
+    #endregion
+
+    #region Incremental Surface Registration
+// =========================================================
+// Queue surface scenery while leaving independently managed actors alone.
+private void OnNodeAdded(Node node)
+{
+    if (!_paused || node is WorldLayerMember)
+        return;
+
+    bool surface = _ground.IsAncestorOf(node) || _objects.IsAncestorOf(node);
+
+    for (Node parent = node; parent != null; parent = parent.GetParent())
+    {
+        if (parent is Player || parent is Entity || parent is Projectile)
+            return;
+        if (_roots.ContainsKey(parent)) surface = true;
+    }
+
+    if (surface) _addedSurface.Enqueue(node);
+}
+
+    // =========================================================
+    // Pause new branches without revisiting existing surface hierarchies.
+    public void Drain()
+    {
+        while (_addedSurface.Count > 0)
+        {
+            Node node = _addedSurface.Dequeue();
+            if (!GodotObject.IsInstanceValid(node) ||
+                node.IsQueuedForDeletion() || !node.IsInsideTree())
+                continue;
+
+            bool covered = false;
+            for (Node parent = node; parent != null; parent = parent.GetParent())
+            {
+                if (!_roots.TryGetValue(parent, out WorldLayerMember member))
+                    continue;
+                covered = member.Covers(node);
+                break;
+            }
+
+            if (covered) continue;
+
+            WorldLayerMember added = RegisterRoot(node);
+            added.SetActive(false);
+            if (!_inheritedFade.Contains(added))
+                added.SetOpacity(_surfaceOpacity);
+        }
+    }
+
+    // =========================================================
+    // Register a branch and detect inherited canvas fading.
+    private WorldLayerMember RegisterRoot(Node node)
+    {
+        if (_roots.TryGetValue(node, out WorldLayerMember existing))
+            return existing;
+
+        bool inherited = false;
+        for (Node parent = node.GetParent(); parent != null; parent = parent.GetParent())
+        {
+            if (parent is CanvasItem && _roots.ContainsKey(parent))
+            {
+                inherited = true;
+                break;
+            }
+        }
+
+        WorldLayerMember member = WorldLayerMember.Attach(node, WorldLayerId.Surface);
+        _roots.Add(node, member);
+        _surface.Add(member);
+        if (inherited) _inheritedFade.Add(member);
+        return member;
+    }
+
+    // =========================================================
+    // Include branches independently registered during earlier visits.
+    private void RegisterExistingBranches(Node node)
+    {
+        if (node is WorldLayerMember || node.IsQueuedForDeletion()) return;
+
+        WorldLayerMember member =
+            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember");
+        if (member != null && member.Layer == WorldLayerId.Surface)
+            RegisterRoot(node);
+
+        foreach (Node child in node.GetChildren())
+            RegisterExistingBranches(child);
+    }
+
+// =========================================================
+// Pause scenery and population work while pursuit owns enemy activation.
+private void CaptureSurface()
+{
+    _surface.Clear();
+    _roots.Clear();
+    _inheritedFade.Clear();
+    _addedSurface.Clear();
+
+    RegisterRoot(_ground);
+    RegisterExistingBranches(_ground);
+
+    foreach (Node child in _objects.GetChildren())
+    {
+        if (child == _player || child is Entity || child is Projectile ||
+            child.IsQueuedForDeletion())
+            continue;
+
+        RegisterRoot(child);
+        RegisterExistingBranches(child);
+    }
+
+    Node systems = _world.GetNode("Systems");
+    foreach (string name in new[]
+    {
+        "ChunkController", "EnemyPopulation", "Atmosphere",
+        "GroundFog", "VegetationInteraction", "Surfaces"
+    })
+    {
+        Node node = systems.GetNodeOrNull<Node>(name);
+        if (node == null) continue;
+        RegisterRoot(node);
+        RegisterExistingBranches(node);
+    }
+
+    Node fading = _config.GetNodeOrNull<Node>("PlayerObstructionFade");
+    if (fading != null) RegisterRoot(fading);
+}
+    #endregion
+
+        // =========================================================
+    // Remove retired surface registrations during long underground journeys.
+    public void Prune()
+    {
+        List<Node> remove = new();
+
+        foreach (var pair in _roots)
+            if (!GodotObject.IsInstanceValid(pair.Key) ||
+                !GodotObject.IsInstanceValid(pair.Value))
+                remove.Add(pair.Key);
+
+        foreach (Node node in remove)
+        {
+            WorldLayerMember member = _roots[node];
+            _roots.Remove(node);
+            _surface.Remove(member);
+            _inheritedFade.Remove(member);
+        }
+    }
+}
--- a/WORLD/Scenes/world_infinite.tscn
+++ b/WORLD/Scenes/world_infinite.tscn
@@ -26,2 +26,4 @@
 [ext_resource type="Script" uid="uid://d3v5aarbwxiuf" path="res://VISUALS/Lighting/WorldLighting.cs" id="lighting_shared"]
+
+[ext_resource type="Script" path="res://DEBUG/DeepCavernsTest/DeepCavernsTest.cs" id="deep_caverns_test"]
 
@@ -129 +131,4 @@
 script = ExtResource("21_infinite")
+
+[node name="DeepCavernsTest" type="Node" parent="."]
+script = ExtResource("deep_caverns_test")

'@
# The doubled backslash matches a literal backslash; this is patch data, not code.
$patch = [regex]::Replace($patch, '\\u([0-9a-fA-F]{4})',
    [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        return [string][char][Convert]::ToInt32($match.Groups[1].Value, 16)
    })
$lines = $patch.Replace("`r`n", "`n").Replace("`r", "`n").Split([char]10)
$i = 0
while ($i -lt $lines.Length) {
    if (-not $lines[$i].StartsWith('--- ')) { $i++; continue }
    $oldHeader = $lines[$i]
    $created = $oldHeader -eq '--- /dev/null'
    $i++
    if ($i -ge $lines.Length) { throw 'Incomplete patch header.' }
    $deleted = $lines[$i] -eq '+++ /dev/null'
    if ($deleted) { $path = $oldHeader.Substring(6) }
    elseif ($lines[$i].StartsWith('+++ b/')) { $path = $lines[$i].Substring(6) }
    else { throw 'Invalid embedded patch header.' }
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
        if ($created) { $source += $after }
        else {
            if ($before.Length -eq 0) { throw "Empty patch match in $path" }
            $at = $source.IndexOf($before, [System.StringComparison]::Ordinal)
            if ($at -lt 0 -or $source.IndexOf($before,
                $at + $before.Length, [System.StringComparison]::Ordinal) -ge 0) {
                throw "Expected code missing or ambiguous in $path. No files changed."
            }
            $source = $source.Substring(0, $at) + $after +
                $source.Substring($at + $before.Length)
        }
    }
    if ($deleted) {
        if ($source.Length -ne 0) { throw "Incomplete deletion of $path" }
        $pending[$path] = $null
    } else { $pending[$path] = $source }
}
if ($pending.Count -ne 26) { throw 'Incomplete patch. No files changed.' }
$written = New-Object 'System.Collections.Generic.List[string]'
try {
    foreach ($path in $pending.Keys) {
        $full = Join-Path $root $path
        $written.Add($path)
        if ($null -eq $pending[$path]) { Remove-Item -LiteralPath $full }
        else {
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full)) | Out-Null
            [System.IO.File]::WriteAllText($full, $pending[$path], $utf8)
        }
    }
} catch {
    foreach ($path in $written) {
        $full = Join-Path $root $path
        if ($null -eq $original[$path]) {
            if (Test-Path $full) { Remove-Item -LiteralPath $full }
        } else { [System.IO.File]::WriteAllBytes($full, $original[$path]) }
    }
    throw
}
foreach ($name in @('MigrateWorldLayersPass1.ps1', 'MigrateWorldLayersPass2.ps1')) {
    $oldScript = Join-Path $root $name
    if (Test-Path $oldScript) { Remove-Item -LiteralPath $oldScript -ErrorAction SilentlyContinue }
}
Write-Host 'Pass 3 applied. Build C#, then run WORLD/Scenes/world_infinite.tscn.'
Write-Host 'Enter Upper Caverns, follow the first chamber right to the marked Deep Caverns doorway.'
Write-Host 'CONFIG -> LAYER TRANSITIONS controls length and slope. Restart after edits.'
Write-Host 'Remove/disable the DeepCavernsTest scene node to remove test placement on the next run.'
Write-Host 'Test both descents and returns, then delete this one-time migration script.'
