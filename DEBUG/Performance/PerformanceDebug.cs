// Records a rolling 60-second frame-time history.
// Red spikes show slow frames; purple markers show observed GC collections.
// Measures intervals between callbacks, including engine work and waiting.
using Godot;
using System;
using System.Diagnostics;

public partial class PerformanceDebug : Control
{
    #region Configuration
    [ExportGroup("Performance Debug")]
    [Export] public Vector2 PanelPosition { get; set; } = new(16, 150);
    [Export] public Vector2 PanelSize { get; set; } = new(680, 250);
    #endregion

    #region History
    private const double HistorySeconds = 60.0;
    private const double SampleSeconds = 0.1;
    private const int Capacity = 1024;

    private struct Sample
    {
        public double Time, WorstMs, Fps;
        public int Collections;
    }

    private readonly Sample[] _history = new Sample[Capacity];
    private int _first, _count;
    private long _previousTick;
    private double _time, _bucketSeconds, _bucketWorst;
    private int _bucketFrames, _bucketCollections;
    private int _gen0, _gen1, _gen2;
    private bool _frozen;
    private Button _freeze;
    private Font _font;

    private static readonly Color TextColour = new(0.9f, 0.94f, 1f);
    private static readonly Color GoodColour = new(0.3f, 0.95f, 0.65f);
    private static readonly Color SlowColour = new(1f, 0.35f, 0.25f);
    private static readonly Color GcColour = new(0.8f, 0.45f, 1f);
    #endregion

    #region Lifecycle
    // =========================================================
    // Create two buttons; the rest of the panel ignores mouse input.
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        Position = PanelPosition;
        Size = new(
            Mathf.Max(460f, PanelSize.X),
            Mathf.Max(230f, PanelSize.Y));
        _font = ThemeDB.FallbackFont;

        _freeze = new Button
        {
            Text = "Freeze",
            Position = new(Size.X - 180f, 8f),
            Size = new(80f, 28f),
            FocusMode = FocusModeEnum.None
        };
        _freeze.Pressed += ToggleFreeze;
        AddChild(_freeze);

        Button clear = new()
        {
            Text = "Clear",
            Position = new(Size.X - 92f, 8f),
            Size = new(80f, 28f),
            FocusMode = FocusModeEnum.None
        };
        clear.Pressed += ClearHistory;
        AddChild(clear);

        ClearHistory();
    }

    // =========================================================
    // Sample every frame without allocating; redraw only ten times per second.
    public override void _Process(double delta)
    {
        long tick = Stopwatch.GetTimestamp();
        double seconds =
            (tick - _previousTick) / (double)Stopwatch.Frequency;
        _previousTick = tick;

        if (_frozen) return;

        _time += seconds;
        _bucketSeconds += seconds;
        _bucketFrames++;
        _bucketWorst = Math.Max(_bucketWorst, seconds * 1000.0);

        int gen0 = GC.CollectionCount(0);
        int gen1 = GC.CollectionCount(1);
        int gen2 = GC.CollectionCount(2);

        // Generation zero also increments for higher-generation collections.
        // Count its changes once, rather than adding all three generations.
        _bucketCollections += Math.Max(0, gen0 - _gen0);
        _gen0 = gen0;
        _gen1 = gen1;
        _gen2 = gen2;

        if (_bucketSeconds < SampleSeconds) return;

        AddSample(new Sample
        {
            Time = _time,
            WorstMs = _bucketWorst,
            Fps = _bucketFrames / _bucketSeconds,
            Collections = _bucketCollections
        });

        _bucketSeconds = _bucketWorst = 0;
        _bucketFrames = _bucketCollections = 0;
        QueueRedraw();
    }
    #endregion

    #region Recording
    // =========================================================
    // Keep bounded history; one long frame remains visible as a peak.
    private void AddSample(Sample sample)
    {
        if (_count == Capacity)
        {
            _first = (_first + 1) % Capacity;
            _count--;
        }

        _history[(_first + _count) % Capacity] = sample;
        _count++;

        while (_count > 0 &&
            _history[_first].Time < _time - HistorySeconds)
        {
            _first = (_first + 1) % Capacity;
            _count--;
        }
    }

    // =========================================================
    // Freeze only the recording; gameplay continues normally.
    private void ToggleFreeze()
    {
        _frozen = !_frozen;
        _freeze.Text = _frozen ? "Resume" : "Freeze";
        _previousTick = Stopwatch.GetTimestamp();
        QueueRedraw();
    }

    // =========================================================
    // Reset the capture and GC baseline without forcing a collection.
    private void ClearHistory()
    {
        _first = _count = 0;
        _time = _bucketSeconds = _bucketWorst = 0;
        _bucketFrames = _bucketCollections = 0;
        _gen0 = GC.CollectionCount(0);
        _gen1 = GC.CollectionCount(1);
        _gen2 = GC.CollectionCount(2);
        _previousTick = Stopwatch.GetTimestamp();
        QueueRedraw();
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw peak frame times, a 60 FPS reference, and collection markers.
    public override void _Draw()
    {
        if (_font == null) return;

        DrawRect(new Rect2(Vector2.Zero, Size),
            new Color(0.025f, 0.035f, 0.05f, 0.93f));

        Write(new(12, 27),
            $"PERFORMANCE — {(_frozen ? "FROZEN" : "RECORDING")}");

        double worst = 0, worstTime = 0, fps = 0;
        int collections = 0;

        for (int i = 0; i < _count; i++)
        {
            Sample sample = _history[(_first + i) % Capacity];
            fps = sample.Fps;
            collections += sample.Collections;

            if (sample.WorstMs > worst)
            {
                worst = sample.WorstMs;
                worstTime = sample.Time;
            }
        }

        Write(new(12, 51),
            $"FPS {fps:F0}   |   Worst frame {worst:F1} ms   |   " +
            $"GC collections {collections}");

        Write(new(12, 72), _count == 0
            ? "Waiting for samples..."
            : $"Worst frame: {_time - worstTime:F1}s ago   |   " +
              $"Gen 0/1/2 totals: {_gen0}/{_gen1}/{_gen2}");

        Rect2 graph = new(
            new Vector2(54f, 94f),
            new Vector2(Size.X - 70f, Size.Y - 139f));

        // Expand automatically so even a multi-second freeze fits.
        double ceiling = Math.Max(
            100.0, Math.Ceiling(worst / 50.0) * 50.0);

        DrawRect(graph, new Color(0.07f, 0.09f, 0.12f));

        DrawReference(graph, ceiling, 16.667, "60 FPS", GoodColour);
        DrawReference(graph, ceiling, 33.333, "30 FPS",
            new Color(1f, 0.75f, 0.3f));

        Write(new(4, graph.Position.Y + 5f),
            $"{ceiling:F0}", 12);
        Write(new(8, graph.End.Y), "0 ms", 12);

        Vector2 previous = Vector2.Zero;
        bool hasPrevious = false;

        for (int i = 0; i < _count; i++)
        {
            Sample sample = _history[(_first + i) % Capacity];
            float x = graph.End.X -
                (float)((_time - sample.Time) / HistorySeconds) *
                graph.Size.X;
            float y = graph.End.Y -
                (float)(sample.WorstMs / ceiling) * graph.Size.Y;

            Vector2 point = new(x, y);
            Color colour = sample.WorstMs > 33.333
                ? SlowColour : GoodColour;

            if (sample.Collections > 0)
                DrawLine(new(x, graph.Position.Y),
                    new(x, graph.End.Y),
                    new Color(GcColour.R, GcColour.G, GcColour.B, 0.4f));

            if (hasPrevious)
                DrawLine(previous, point, colour, 1.5f);

            // Vertical peak keeps a single slow sample easy to spot.
            if (sample.WorstMs > 33.333)
                DrawLine(new(x, graph.End.Y), point,
                    new Color(1f, 0.35f, 0.25f, 0.5f));

            previous = point;
            hasPrevious = true;
        }

        Write(new(graph.Position.X, graph.End.Y + 18f), "-60s", 12);
        Write(new(graph.End.X - 30f, graph.End.Y + 18f), "now", 12);
        Write(new(12, Size.Y - 9f),
            "Line = worst frame per sample | Purple = GC observed | " +
            "Freeze saves this view", 12);
    }

    // =========================================================
    // Draw a target frame-time reference.
    private void DrawReference(
        Rect2 graph, double ceiling, double milliseconds,
        string label, Color colour)
    {
        float y = graph.End.Y -
            (float)(milliseconds / ceiling) * graph.Size.Y;

        DrawLine(new(graph.Position.X, y),
            new(graph.End.X, y),
            new Color(colour.R, colour.G, colour.B, 0.35f));

        Write(new(graph.Position.X + 5f, y - 3f), label, 11, colour);
    }

    // =========================================================
    // Use Godot's shared font without creating Label nodes.
    private void Write(
        Vector2 position, string text, int size = 14,
        Color? colour = null)
    {
        DrawString(_font, position, text,
            HorizontalAlignment.Left, -1f, size,
            colour ?? TextColour);
    }
    #endregion
}