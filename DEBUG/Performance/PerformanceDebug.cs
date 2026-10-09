// Records the last 60 seconds of frame intervals in a taller debug graph.
// Shows average and peak times, slow frames and observed managed GC collections.
// Does not identify individual methods or measure GC pause duration.
using Godot;
using System;
using System.Diagnostics;

public partial class PerformanceDebug : Control
{
	#region Configuration
	[ExportGroup("Performance Debug")]
	[Export] public Vector2 PanelSize { get; set; } = new(720, 400);
	[Export] public Vector2 ScreenMargin { get; set; } = new(16, 16);
	#endregion

	#region History
	private const double HistorySeconds = 60.0;
	private const double SampleSeconds = 0.1;
	private const int Capacity = 1024;

	private const double SlowMs = 1000.0 / 30.0;
	private const double HitchMs = 100.0;

	private struct Sample
	{
		public double Time, Seconds, AverageMs, WorstMs;
		public int Frames, SlowFrames, Hitches, Collections;
	}

	private readonly Sample[] _history = new Sample[Capacity];
	private int _first, _count;
	private long _previousTick;
	private double _time, _bucketSeconds, _bucketWorst;
	private int _bucketFrames, _bucketSlow, _bucketHitches;
	private int _bucketCollections;
	private int _gen0;

	private bool _frozen;
	private int _scaleMode;
	private Button _freeze, _clear, _scale;
	private Font _font;

	private static readonly Color TextColour = new(0.9f, 0.94f, 1f);
	private static readonly Color AverageColour = new(0.35f, 0.7f, 1f);
	private static readonly Color GoodColour = new(0.3f, 0.95f, 0.65f);
	private static readonly Color WarningColour = new(1f, 0.75f, 0.3f);
	private static readonly Color SlowColour = new(1f, 0.35f, 0.25f);
	private static readonly Color GcColour = new(0.8f, 0.45f, 1f);
	#endregion

	#region Lifecycle
	// =========================================================
	// Create controls while leaving the graph transparent to gameplay mouse input.
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Ignore;
		_font = ThemeDB.FallbackFont;

		_scale = CreateButton("Scale: Auto", CycleScale);
		_freeze = CreateButton("Freeze", ToggleFreeze);
		_clear = CreateButton("Clear", ClearHistory);

		GetViewport().SizeChanged += UpdateLayout;
		UpdateLayout();
		ClearHistory();
	}

	// =========================================================
	// Disconnect the viewport event when this overlay leaves the scene.
	public override void _ExitTree()
	{
		GetViewport().SizeChanged -= UpdateLayout;
	}

	// =========================================================
	// Measure frame intervals without allocating history objects each frame.
	public override void _Process(double delta)
	{
		long tick = Stopwatch.GetTimestamp();
		double seconds =
			(tick - _previousTick) / (double)Stopwatch.Frequency;
		_previousTick = tick;

		if (_frozen) return;

		double milliseconds = seconds * 1000.0;

		_time += seconds;
		_bucketSeconds += seconds;
		_bucketFrames++;
		_bucketWorst = Math.Max(_bucketWorst, milliseconds);

		if (milliseconds > SlowMs) _bucketSlow++;
		if (milliseconds >= HitchMs) _bucketHitches++;

		// Gen 0 counts also advance during higher-generation collections.
		// Count once rather than adding all three generations together.
		int gen0 = GC.CollectionCount(0);
		_bucketCollections += Math.Max(0, gen0 - _gen0);
		_gen0 = gen0;

		if (_bucketSeconds < SampleSeconds) return;

		AddSample(new Sample
		{
			Time = _time,
			Seconds = _bucketSeconds,
			AverageMs = _bucketSeconds * 1000.0 / _bucketFrames,
			WorstMs = _bucketWorst,
			Frames = _bucketFrames,
			SlowFrames = _bucketSlow,
			Hitches = _bucketHitches,
			Collections = _bucketCollections
		});

		ResetBucket();
		QueueRedraw();
	}
	#endregion

	#region Layout
	// =========================================================
	// Keep the panel in the bottom-right corner and within the viewport.
	private void UpdateLayout()
	{
		Vector2 viewport = GetViewport().GetVisibleRect().Size;
		Vector2 margin = new(
			Mathf.Max(0f, ScreenMargin.X),
			Mathf.Max(0f, ScreenMargin.Y));

		Vector2 requested = new(
			Mathf.Max(560f, PanelSize.X),
			Mathf.Max(320f, PanelSize.Y));

		Size = new Vector2(
			Mathf.Min(requested.X, Mathf.Max(1f, viewport.X - margin.X * 2f)),
			Mathf.Min(requested.Y, Mathf.Max(1f, viewport.Y - margin.Y * 2f)));

		Position = viewport - Size - margin;

		_scale.Position = new(Size.X - 292f, 8f);
		_scale.Size = new(112f, 28f);
		_freeze.Position = new(Size.X - 172f, 8f);
		_freeze.Size = new(76f, 28f);
		_clear.Position = new(Size.X - 88f, 8f);
		_clear.Size = new(76f, 28f);

		QueueRedraw();
	}

	// =========================================================
	// Create one button without allowing keyboard focus to capture movement keys.
	private Button CreateButton(string text, Action action)
	{
		Button button = new()
		{
			Text = text,
			FocusMode = FocusModeEnum.None,
			MouseFilter = MouseFilterEnum.Stop
		};
		button.Pressed += action;
		AddChild(button);
		return button;
	}
	#endregion

	#region Recording
	// =========================================================
	// Keep a bounded history and retain each bucket's worst frame.
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
	// Reset only the unfinished recording bucket.
	private void ResetBucket()
	{
		_bucketSeconds = _bucketWorst = 0;
		_bucketFrames = _bucketSlow = _bucketHitches = 0;
		_bucketCollections = 0;
	}

	// =========================================================
	// Freeze the display while gameplay continues normally.
	private void ToggleFreeze()
	{
		_frozen = !_frozen;
		_freeze.Text = _frozen ? "Resume" : "Freeze";

		_previousTick = Stopwatch.GetTimestamp();
		_gen0 = GC.CollectionCount(0);
		QueueRedraw();
	}

	// =========================================================
	// Clear history without changing gameplay or forcing garbage collection.
	private void ClearHistory()
	{
		_first = _count = 0;
		_time = 0;
		ResetBucket();

		_gen0 = GC.CollectionCount(0);
		_previousTick = Stopwatch.GetTimestamp();
		QueueRedraw();
	}

	// =========================================================
	// Switch between automatic and fixed graph scales without clearing history.
	private void CycleScale()
	{
		_scaleMode = (_scaleMode + 1) % 4;
		_scale.Text = _scaleMode switch
		{
			1 => "Scale: 50 ms",
			2 => "Scale: 100 ms",
			3 => "Scale: 250 ms",
			_ => "Scale: Auto"
		};
		QueueRedraw();
	}
	#endregion

	#region Drawing
	// =========================================================
	// Draw statistics and a rolling graph with readable timing references.
	public override void _Draw()
	{
		if (_font == null) return;

		DrawRect(new Rect2(Vector2.Zero, Size),
			new Color(0.025f, 0.035f, 0.05f, 0.94f));
		DrawRect(new Rect2(Vector2.Zero, Size),
			new Color(0.3f, 0.4f, 0.5f, 0.6f), false);

		Write(new(12, 27),
			$"PERFORMANCE — {(_frozen ? "FROZEN" : "LIVE")}", 15);

		double worst = 0, worstTime = 0;
		double totalSeconds = 0;
		int frames = 0, slow = 0, hitches = 0, collections = 0;
		Sample latest = default;

		for (int i = 0; i < _count; i++)
		{
			Sample sample = _history[(_first + i) % Capacity];
			latest = sample;
			totalSeconds += sample.Seconds;
			frames += sample.Frames;
			slow += sample.SlowFrames;
			hitches += sample.Hitches;
			collections += sample.Collections;

			if (sample.WorstMs > worst)
			{
				worst = sample.WorstMs;
				worstTime = sample.Time;
			}
		}

		double fps = latest.Seconds > 0
			? latest.Frames / latest.Seconds : 0;
		double averageMs = frames > 0
			? totalSeconds * 1000.0 / frames : 0;

		Write(new(12, 53),
			$"FPS {fps:F0}  |  Latest avg {latest.AverageMs:F1} ms  |  " +
			$"Capture avg {averageMs:F1} ms");

		Write(new(12, 76), _count == 0
			? "Waiting for samples..."
			: $"Worst {worst:F1} ms — {_time - worstTime:F1}s ago  |  " +
			  $"Frames >33 ms: {slow}  |  ≥100 ms: {hitches}");

		Write(new(12, 97),
			$"GC observed: {collections}  |  " +
			$"Recorded {Math.Min(_time, HistorySeconds):F1}s of 60s", 13);

		Rect2 graph = new(
			new Vector2(58f, 120f),
			new Vector2(
				Mathf.Max(1f, Size.X - 74f),
				Mathf.Max(1f, Size.Y - 176f)));

		double ceiling = GraphCeiling(worst);
		DrawRect(graph, new Color(0.065f, 0.085f, 0.115f));

		DrawGrid(graph, ceiling);
		DrawReference(graph, ceiling, 16.667, "60 FPS", GoodColour);
		DrawReference(graph, ceiling, 33.333, "30 FPS", WarningColour);

		Vector2 previousPeak = Vector2.Zero;
		Vector2 previousAverage = Vector2.Zero;
		bool hasPrevious = false;
		int clipped = 0;

		for (int i = 0; i < _count; i++)
		{
			Sample sample = _history[(_first + i) % Capacity];
			float x = graph.End.X -
				(float)((_time - sample.Time) / HistorySeconds) *
				graph.Size.X;

			Vector2 peak = new(x, GraphY(graph, ceiling, sample.WorstMs));
			Vector2 average = new(
				x, GraphY(graph, ceiling, sample.AverageMs));

			Color colour = sample.WorstMs >= HitchMs
				? SlowColour
				: sample.WorstMs > SlowMs ? WarningColour : GoodColour;

			if (sample.Collections > 0)
			{
				DrawLine(new(x, graph.Position.Y), new(x, graph.End.Y),
					new Color(GcColour.R, GcColour.G, GcColour.B, 0.3f));
			}

			if (sample.WorstMs > SlowMs)
			{
				DrawLine(new(x, graph.End.Y), peak,
					new Color(colour.R, colour.G, colour.B, 0.25f));
			}

			if (hasPrevious)
			{
				DrawLine(previousPeak, peak, colour, 1.5f);
				DrawLine(previousAverage, average, AverageColour, 1.5f);
			}
			else
			{
				DrawCircle(peak, 2f, colour);
				DrawCircle(average, 2f, AverageColour);
			}

			if (sample.WorstMs > ceiling)
			{
				clipped++;
				DrawLine(
					new(x, graph.Position.Y),
					new(x, graph.Position.Y + 7f),
					SlowColour, 2f);
			}

			previousPeak = peak;
			previousAverage = average;
			hasPrevious = true;
		}

		if (clipped > 0)
		{
			Write(new(graph.Position.X + 8f, graph.Position.Y + 17f),
				$"{clipped} peaks above scale — actual worst {worst:F1} ms",
				12, SlowColour);
		}

		Write(new(12, Size.Y - 27f),
			"Blue = average  |  Green/amber/red = peak frame time", 12);
		Write(new(12, Size.Y - 9f),
			"Purple = GC observed  |  Freeze holds the capture; gameplay continues",
			12);
	}

	// =========================================================
	// Use a useful automatic scale or the chosen fixed ceiling.
	private double GraphCeiling(double worst)
	{
		return _scaleMode switch
		{
			1 => 50.0,
			2 => 100.0,
			3 => 250.0,
			_ => Math.Max(50.0, Math.Ceiling(worst / 50.0) * 50.0)
		};
	}

	// =========================================================
	// Clip drawing to the graph while preserving actual statistics.
	private static float GraphY(Rect2 graph, double ceiling, double value)
	{
		return graph.End.Y -
			(float)Math.Clamp(value / ceiling, 0.0, 1.0) * graph.Size.Y;
	}

	// =========================================================
	// Draw milliseconds vertically and ten-second intervals horizontally.
	private void DrawGrid(Rect2 graph, double ceiling)
	{
		Color gridColour = new(0.4f, 0.5f, 0.6f, 0.15f);

		for (int i = 0; i <= 4; i++)
		{
			float fraction = i / 4f;
			float y = graph.End.Y - graph.Size.Y * fraction;

			DrawLine(new(graph.Position.X, y),
				new(graph.End.X, y), gridColour);

			Write(new(5f, y + 4f),
				$"{ceiling * fraction:F0} ms", 11);
		}

		for (int seconds = 0; seconds <= 60; seconds += 10)
		{
			float x = graph.End.X -
				seconds / 60f * graph.Size.X;

			DrawLine(new(x, graph.Position.Y),
				new(x, graph.End.Y), gridColour);

			string label = seconds == 0 ? "now" : $"-{seconds}s";
			float offset = seconds == 60 ? 0f : seconds == 0 ? -24f : -15f;
			Write(new(x + offset, graph.End.Y + 18f), label, 11);
		}
	}

	// =========================================================
	// Draw target frame times without placing labels inside the plotted history.
	private void DrawReference(
		Rect2 graph, double ceiling, double milliseconds,
		string label, Color colour)
	{
		if (milliseconds > ceiling) return;

		float y = GraphY(graph, ceiling, milliseconds);
		DrawLine(new(graph.Position.X, y), new(graph.End.X, y),
			new Color(colour.R, colour.G, colour.B, 0.45f));

		// Tiny reference labels are omitted when a huge auto scale crowds them.
		if (graph.Size.Y * milliseconds / ceiling >= 14f)
			Write(new(graph.End.X - 49f, y - 3f), label, 10, colour);
	}

	// =========================================================
	// Draw text with the shared font instead of creating many Label nodes.
	private void Write(
		Vector2 position, string text, int size = 14,
		Color? colour = null)
	{
		DrawString(_font, position, text,
			HorizontalAlignment.Left, -1f, size, colour ?? TextColour);
	}
	#endregion
}
