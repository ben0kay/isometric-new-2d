// Loads baked artwork from disk or captures and saves it when missing.
// Shares capture, validation, timing and cleanup between all artwork families.
using Godot;
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

public static class ArtworkBaker
{
    #region Configuration
    private const string CacheDirectory = "user://artwork_cache";
    private const string CacheFormat = "premultiplied-png-v1";
    #endregion

    #region Resources
    // =========================================================
    // Load a required shader and report its exact path when missing.
    public static ShaderMaterial LoadMaterial(string path)
    {
        Shader shader = GD.Load<Shader>(path);
        if (shader == null)
            throw new InvalidOperationException($"Bake shader missing: {path}");
        return new ShaderMaterial { Shader = shader };
    }

    // =========================================================
    // Load valid cached pixels, otherwise create the painter and bake once.
    public static async Task<ImageTexture> LoadOrBake(
        Node host, string name, string revisionKey, Vector2I size,
        Func<Node2D> createPainter)
    {
        Stopwatch timer = Stopwatch.StartNew();
        string path = GetCachePath(name, revisionKey, size);

        if (Godot.FileAccess.FileExists(path))
        {
            using Image image = new();
            Error error = image.Load(path);

            if (error == Error.Ok && !image.IsEmpty()
                && image.GetWidth() == size.X && image.GetHeight() == size.Y)
            {
                ImageTexture cached = ImageTexture.CreateFromImage(image);
                GD.Print($"[Artwork] {name}: cached, {timer.ElapsedMilliseconds} ms");
                return cached;
            }

            GD.PushWarning($"[Artwork] {name}: invalid cache; rebuilding.");
        }

        // The factory runs only on a cache miss.
        Node2D painter = createPainter();
        ImageTexture texture = await Capture(
            host, painter, size, painter.Material, name, path);

        GD.Print($"[Artwork] {name}: baked, {timer.ElapsedMilliseconds} ms");
        return texture;
    }

    // =========================================================
    // Include artwork revision, dimensions, engine and renderer in the cache key.
    private static string GetCachePath(
        string name, string revisionKey, Vector2I size)
    {
        string engine = Engine.GetVersionInfo()["string"].AsString();
        string renderer = ProjectSettings.GetSetting(
            "rendering/renderer/rendering_method", "gl_compatibility").AsString();

        string key = $"{CacheFormat}|{name}|{revisionKey}"
            + $"|{size.X}x{size.Y}|{engine}|{renderer}";

        using SHA256 hash = SHA256.Create();
        string digest = Convert.ToHexString(
            hash.ComputeHash(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

        return $"{CacheDirectory}/{name}_{digest}.png";
    }
    #endregion

    #region Capture
    // =========================================================
    // Capture a supplied painter and release all temporary rendering nodes.
    public static async Task<ImageTexture> Capture(
        Node host, Node2D painter, Vector2I size,
        Material bakeMaterial, string label, string cachePath = null)
    {
        if (host == null || !host.IsInsideTree())
            throw new InvalidOperationException(
                $"{label}: baking requires a host inside the scene tree.");
        if (painter == null || painter.GetParent() != null)
            throw new ArgumentException(
                $"{label}: provide a new, unparented painter.");
        if (size.X <= 0 || size.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        SubViewport viewport = new()
        {
            Name = label,
            Size = size,
            TransparentBg = true,
            Disable3D = true,
            World2D = new World2D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled
        };

        try
        {
            painter.Material = bakeMaterial;
viewport.AddChild(painter);
host.CallDeferred(Node.MethodName.AddChild, viewport);

            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(viewport)
                || !viewport.IsInsideTree() || host.IsQueuedForDeletion())
                throw new InvalidOperationException(
                    $"{label}: capture was interrupted.");

            viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            await host.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            if (!GodotObject.IsInstanceValid(viewport) || !viewport.IsInsideTree())
                throw new InvalidOperationException(
                    $"{label}: viewport was removed before capture.");

            using Image image = viewport.GetTexture().GetImage();
            if (image.IsEmpty())
                throw new InvalidOperationException($"{label}: capture was empty.");

            // Preserve the captured premultiplied pixels exactly.
            ImageTexture texture = ImageTexture.CreateFromImage(image);
            if (cachePath != null) SaveCache(image, cachePath, label);
            return texture;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(viewport))
                viewport.QueueFree();
        }
    }

    // =========================================================
    // Save through a temporary file so interrupted writes cannot become cache hits.
    private static void SaveCache(Image image, string path, string label)
    {
        string temporary = path + ".tmp";
        try
        {
            Error directoryError = DirAccess.MakeDirRecursiveAbsolute(
                ProjectSettings.GlobalizePath(CacheDirectory));
            if (directoryError != Error.Ok)
            {
                GD.PushWarning(
                    $"[Artwork] {label}: cache directory failed: {directoryError}");
                return;
            }

            Error saveError = image.SavePng(temporary);
            if (saveError != Error.Ok)
            {
                GD.PushWarning($"[Artwork] {label}: cache save failed: {saveError}");
                return;
            }

            System.IO.File.Move(
                ProjectSettings.GlobalizePath(temporary),
                ProjectSettings.GlobalizePath(path), true);
        }
        catch (Exception error)
        {
            // A cache failure should not prevent playing with the baked texture.
            GD.PushWarning($"[Artwork] {label}: cache save failed: {error.Message}");
        }
        finally
        {
            string absolute = ProjectSettings.GlobalizePath(temporary);
            if (System.IO.File.Exists(absolute))
            {
                try { System.IO.File.Delete(absolute); }
                catch (Exception) { }
            }
        }
    }
    #endregion
}