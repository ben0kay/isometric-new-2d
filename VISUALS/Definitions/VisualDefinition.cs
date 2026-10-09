// Defines replacement artwork, placement, lighting and ground shadows.
// Optional vegetation effects remain separate from shared visual behaviour.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class VisualDefinition : Resource
{
    #region Artwork
    [ExportGroup("Artwork")]
    [Export] public PackedScene VisualScene { get; set; }
    [Export] public Texture2D Image { get; set; }
    // Optional folder of equally likely PNG variants, scanned once per resource/run.
    [Export(PropertyHint.Dir)] public string ImageFolder { get; set; } = "";

    private string _cachedFolder;
    private readonly List<Texture2D> _folderImages = new();

    public int ImageVariantCount
    {
        get { EnsureFolderImages(); return _folderImages.Count; }
    }

    public Texture2D GetImage(int variant)
    {
        EnsureFolderImages();
        if (_folderImages.Count == 0) return Image;
        int index = ((variant % _folderImages.Count) + _folderImages.Count)
            % _folderImages.Count;
        return _folderImages[index];
    }

    private void EnsureFolderImages()
    {
        string folder = (ImageFolder ?? "").Trim().TrimEnd('/');
        if (_cachedFolder == folder) return;
        _cachedFolder = folder;
        _folderImages.Clear();
        if (folder.Length == 0) return;

        // ResourceLoader preserves original filenames in exported projects too.
        string[] files = ResourceLoader.ListDirectory(folder);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string file in files)
        {
            if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                continue;
            Texture2D texture = GD.Load<Texture2D>($"{folder}/{file}");
            if (texture != null) _folderImages.Add(texture);
        }
        if (_folderImages.Count == 0)
            GD.PushWarning($"Visual image folder '{folder}' has no loadable PNGs; using fallback artwork.");
    }
    #endregion

    #region Placement
    [ExportGroup("Placement")]
    [Export] public Vector2 Offset { get; set; } = Vector2.Zero;
    [Export] public Vector2 ArtworkScale { get; set; } = Vector2.One;
    [Export] public Vector2 ImageAnchor { get; set; } = new(0.5f, 1f);
    [Export] public CanvasItem.TextureFilterEnum ImageFilter { get; set; }
        = CanvasItem.TextureFilterEnum.Linear;
    #endregion

    #region Lighting
    [ExportGroup("Lighting")]
    [Export] public VisualLightingSettings Lighting { get; set; }
    #endregion

    #region Shadows
    [ExportGroup("Ground Shadows")]
    [Export] public GroundShadowSettings Shadows { get; set; }
    #endregion

    #region Vegetation
    [ExportGroup("Vegetation Effects")]
    [Export] public VegetationVisualSettings Vegetation { get; set; }
    #endregion
}