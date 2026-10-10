// Renders the sci-fi world-loading backdrop, vector ornamentation, and
// animated loading bar in a single CanvasItem (no per-segment UI nodes).
// The optional PNG can be dropped into Artwork/ later without script edits.
using Godot;
using System;

public partial class WorldLoadingVisual : Control
{
    #region State
    private LoadingScreenSettings _settings;
    private Texture2D _backdrop;
    private Font _font;
    private float _progress, _scan;
    private bool _indeterminate = true;
    private string _stage = "PREPARING PLANETARY SYSTEMS";
    private string _details = "Initializing startup services...";
    #endregion

    #region Lifecycle
    // =========================================================
    // Accept both an optional imported PNG and an attractive no-image fallback.
    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        Resized += QueueRedraw;
        MouseFilter = MouseFilterEnum.Stop;
    }

    // =========================================================
    // Load the image only if it exists: no missing-file warnings on a fresh pull.
    public void Configure(LoadingScreenSettings settings)
    {
        _settings = settings;
        string path = settings.BackdropPath;
        if (!string.IsNullOrWhiteSpace(path) &&
            ResourceLoader.Exists(path, "Texture2D"))
            _backdrop = GD.Load<Texture2D>(path);
        QueueRedraw();
    }

    // =========================================================
    // The screen owns animations; changing labels is allocation-light.
    public void UpdateDisplay(float progress, string stage,
        string details, bool indeterminate, float scan)
    {
        _progress = Mathf.Clamp(progress, 0f, 1f);
        _stage = stage;
        _details = details;
        _indeterminate = indeterminate;
        _scan = scan;
        QueueRedraw();
    }
    #endregion

    #region Drawing
    // =========================================================
    // Fully procedural loading UI, with optional cover-cropped backdrop art.
    public override void _Draw()
    {
        if (_settings == null || Size.X < 10f || Size.Y < 10f) return;

        float w = Size.X, h = Size.Y;
        Color cyan = _settings.Accent;
        Color muted = _settings.SecondaryText;
        Color white = _settings.TextColor;
        Color grid = _settings.GridColor;

        DrawBackground(w, h);
        DrawGrid(w, h, grid);
        DrawBorders(w, h, cyan, grid);

        float scale = Mathf.Clamp(Mathf.Min(w / 1920f, h / 1080f), 0.6f, 1.35f);
        int titleSize = Mathf.RoundToInt(36f * scale);
        int subtitleSize = Mathf.RoundToInt(15f * scale);
        int small = Mathf.RoundToInt(13f * scale);
        int mainSize = Mathf.RoundToInt(18f * scale);
        int numberSize = Mathf.RoundToInt(27f * scale);

        // Header and thin ornamental separator.
        TextCentered(_settings.Title, h * 0.23f, titleSize, white);
        TextCentered(_settings.Subtitle, h * 0.23f + 33f * scale,
            subtitleSize, cyan);
        float separatorY = h * 0.23f + 53f * scale;
        DrawLine(new Vector2(w * 0.36f, separatorY),
            new Vector2(w * 0.64f, separatorY), Transparent(cyan, 0.48f), 1f);
        DrawRect(new Rect2(w * 0.5f - 3f, separatorY - 3f, 6f, 6f), cyan);

        // Scanning central glyph, pure lines and arcs; no textures required.
        float glyphY = h * 0.46f;
        float radius = 38f * scale;
        Vector2 centre = new(w * 0.5f, glyphY);
        DrawArc(centre, radius, 0.25f, Mathf.Tau - 0.25f,
            64, Transparent(grid, 0.70f), 1.2f);
        DrawArc(centre, radius, _scan * Mathf.Tau,
            _scan * Mathf.Tau + 1.15f, 24, cyan, 2f);
        DrawArc(centre, radius * 0.70f, -_scan * Mathf.Tau,
            -_scan * Mathf.Tau + 1.8f, 24,
            Transparent(cyan, 0.58f), 1.3f);
        DrawLine(new Vector2(centre.X - 13f * scale, glyphY),
            new Vector2(centre.X + 13f * scale, glyphY),
            Transparent(cyan, 0.65f), 1.2f);
        DrawLine(new Vector2(centre.X, glyphY - 13f * scale),
            new Vector2(centre.X, glyphY + 13f * scale),
            Transparent(cyan, 0.65f), 1.2f);
        DrawRect(new Rect2(centre.X - 2f, glyphY - 2f, 4f, 4f), cyan);

        // Stage name, real-percent text, and a segmented continuous loading track.
        float barWidth = Mathf.Min(w * 0.69f, 1120f);
        float left = (w - barWidth) * 0.5f, right = left + barWidth;
        float stageY = h * 0.70f;
        Write(_stage, new Vector2(left, stageY), mainSize, white);
        if (_settings.ShowPercentage)
        {
            string percent = $"{Mathf.FloorToInt(_progress * 100f):00}%";
            TextRight(percent, new Vector2(right, stageY + 2f * scale),
                numberSize, cyan);
        }

        float barY = stageY + 18f * scale, barH = 8f * scale;
        DrawRect(new Rect2(left, barY, barWidth, barH), new Color("#142b39"));
        float amount = barWidth * _progress;
        if (amount > 0.5f)
            DrawRect(new Rect2(left, barY, amount, barH), cyan);

        // Tick marks and diagonal cutouts give the bar a precise vector finish.
        const int segments = 36;
        for (int i = 1; i < segments; i++)
        {
            float x = left + i * barWidth / segments;
            DrawLine(new Vector2(x, barY), new Vector2(x, barY + barH),
                new Color("#0c1a27"), 1.3f);
        }

        float scannerX = left + Mathf.PosMod(_scan, 1f) * barWidth;
        DrawRect(new Rect2(scannerX - 2.5f * scale, barY - 2f * scale,
            5f * scale, barH + 4f * scale), Transparent(cyan, 0.70f));
        DrawLine(new Vector2(left - 8f * scale, barY - 7f * scale),
            new Vector2(left - 8f * scale, barY + barH + 6f * scale),
            cyan, 1.8f);
        DrawLine(new Vector2(right + 8f * scale, barY - 7f * scale),
            new Vector2(right + 8f * scale, barY + barH + 6f * scale),
            cyan, 1.8f);

        if (_settings.ShowTechnicalDetails)
        {
            string caption = _indeterminate
                ? "SCANNING  //  " + _details
                : "SECTOR GENERATION  //  " + _details;
            Write(caption, new Vector2(left, barY + barH + 30f * scale),
                small, muted);
        }

        TextCentered("PLANETARY SURVEY PROTOCOL   •   PLEASE STAND BY",
            h * 0.92f, small, Transparent(muted, 0.88f));
    }

    // =========================================================
    // Maintain cover-style cropping instead of stretching the supplied PNG.
    private void DrawBackground(float w, float h)
    {
        Color baseColor = _settings.FallbackBackground;
        DrawRect(new Rect2(0f, 0f, w, h), baseColor);

        if (_backdrop != null)
        {
            Vector2 size = _backdrop.GetSize();
            if (size.X > 0f && size.Y > 0f)
            {
                float ratio = Mathf.Max(w / size.X, h / size.Y);
                Vector2 crop = new(w / ratio, h / ratio);
                Rect2 source = new((size - crop) * 0.5f, crop);
                DrawTextureRectRegion(_backdrop,
                    new Rect2(0f, 0f, w, h), source);
            }
            DrawRect(new Rect2(0f, 0f, w, h),
                new Color(0.02f, 0.04f, 0.08f, _settings.BackdropDarken));
        }
        else
        {
            // When no PNG exists, create a dimensional blue-black gradient.
            for (int i = 0; i < 28; i++)
            {
                float t = i / 27f;
                Color tint = new Color(0.10f, 0.22f, 0.29f,
                    0.15f * (1f - t));
                DrawRect(new Rect2(0f, h * t, w,
                    h / 27f + 1f), tint);
            }
        }
    }

    // =========================================================
    // Low-contrast orthogonal lines; fade near the focal center.
    private void DrawGrid(float w, float h, Color grid)
    {
        float spacing = Mathf.Clamp(w / 24f, 56f, 110f);
        for (float x = spacing; x < w; x += spacing)
            DrawLine(new Vector2(x, 0f), new Vector2(x, h),
                Transparent(grid, 0.075f), 1f);
        for (float y = spacing; y < h; y += spacing)
            DrawLine(new Vector2(0f, y), new Vector2(w, y),
                Transparent(grid, 0.075f), 1f);
        DrawLine(new Vector2(w * 0.5f, 0f),
            new Vector2(w * 0.5f, h), Transparent(grid, 0.2f), 1f);
    }

    // =========================================================
    // Corner brackets, scanning rails and small cyan vector subdivisions.
    private void DrawBorders(float w, float h, Color cyan, Color grid)
    {
        float mx = Mathf.Clamp(w * 0.055f, 22f, 105f);
        float my = Mathf.Clamp(h * 0.07f, 20f, 80f);
        float arm = Mathf.Clamp(w * 0.065f, 40f, 120f);
        Color strong = Transparent(cyan, 0.75f);
        Color quiet = Transparent(grid, 0.45f);

        DrawLine(new Vector2(mx, my), new Vector2(mx + arm, my), strong, 2f);
        DrawLine(new Vector2(mx, my), new Vector2(mx, my + arm * 0.6f), strong, 2f);
        DrawLine(new Vector2(w - mx - arm, my), new Vector2(w - mx, my), strong, 2f);
        DrawLine(new Vector2(w - mx, my), new Vector2(w - mx, my + arm * 0.6f), strong, 2f);
        DrawLine(new Vector2(mx, h - my), new Vector2(mx + arm, h - my), strong, 2f);
        DrawLine(new Vector2(mx, h - my - arm * 0.6f), new Vector2(mx, h - my), strong, 2f);
        DrawLine(new Vector2(w - mx - arm, h - my), new Vector2(w - mx, h - my), strong, 2f);
        DrawLine(new Vector2(w - mx, h - my - arm * 0.6f),
            new Vector2(w - mx, h - my), strong, 2f);

        float left = mx + arm + 14f, right = w - mx - arm - 14f;
        if (right > left)
        {
            DrawLine(new Vector2(left, my), new Vector2(right, my), quiet, 1f);
            DrawLine(new Vector2(left, h - my),
                new Vector2(right, h - my), quiet, 1f);
        }

        for (int i = 0; i < 7; i++)
        {
            float y = h * 0.28f + i * 18f;
            float length = i % 3 == 0 ? 19f : 9f;
            DrawLine(new Vector2(mx, y), new Vector2(mx + length, y),
                Transparent(cyan, 0.3f), 1f);
            DrawLine(new Vector2(w - mx - length, y),
                new Vector2(w - mx, y), Transparent(cyan, 0.3f), 1f);
        }
    }

    // =========================================================
    // Font is already shared by Godot's theme; no external font files.
    private void Write(string value, Vector2 at, int px, Color color) =>
        DrawString(_font, at, value, HorizontalAlignment.Left, -1f, px, color);

    private void TextCentered(string value, float y, int px, Color color) =>
        DrawString(_font, new Vector2(0f, y), value,
            HorizontalAlignment.Center, Size.X, px, color);

    private void TextRight(string value, Vector2 at, int px, Color color)
    {
        float textWidth = _font.GetStringSize(value,
            HorizontalAlignment.Left, -1f, px).X;
        Write(value, new Vector2(at.X - textWidth, at.Y), px, color);
    }

    private static Color Transparent(Color source, float alpha) =>
        new(source.R, source.G, source.B, source.A * alpha);
    #endregion
}
