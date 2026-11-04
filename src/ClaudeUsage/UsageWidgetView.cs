using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;

namespace ClaudeUsage;

/// <summary>
/// The Claude Usage tile: two rings on a wide tile, two bars on a single cell.
/// </summary>
public static class UsageWidgetView
{
	private const string Background = "#1f1e1d";
	private const string Accent = "#d97757";
	private const string Warning = "#f0b35a";
	private const string Critical = "#e5484d";
	private const string Unavailable = "#6b6a66";
	private const string Track = "#3a3834";
	private const string TextPrimary = "#faf9f5";
	private const string TextSecondary = "#b0aea5";

	private const string MarkPath = "M0.12 0.88 L0.12 0.58 L0.34 0.58 L0.34 0.88 Z "
		+ "M0.4 0.88 L0.4 0.34 L0.62 0.34 L0.62 0.88 Z "
		+ "M0.68 0.88 L0.68 0.1 L0.9 0.1 L0.9 0.88 Z";

	private sealed record Window(string Key, string Label, Func<UsageSnapshot, UsageWindow?> Select);

	private static readonly Window FiveHour = new("five-hour", "5h", snapshot => snapshot.FiveHour);
	private static readonly Window SevenDay = new("seven-day", "7d", snapshot => snapshot.SevenDay);

	public static UiResponsive Build(UiSurface surface, UiState<UsageReading> state) => new()
	{
		Key = "usage",
		Default = Compact(surface, state),
		Variants =
		[
			new UiResponsiveVariant { MinWidth = 1.5, Content = Wide(surface, state) },
		],
	};

	public static UsageReading Sample(DateTimeOffset now) => new(
		new UsageSnapshot(
			new UsageWindow(42, now.AddHours(2).AddMinutes(10)),
			new UsageWindow(68, now.AddDays(3).AddHours(4))),
		now);

	private static UiStack Wide(UiSurface surface, UiState<UsageReading> state) => new()
	{
		Key = "wide",
		Direction = UiComponentDirections.Horizontal,
		Align = UiComponentAlignments.Center,
		Background = Background,
		Padding = UiWidgetSafeArea.For(surface),
		Gap = UiSize.FromBasis(0.03),
		Children = [Brand("wide", 0.16), Ring(FiveHour, state), Ring(SevenDay, state)],
	};

	private static UiStack Compact(UiSurface surface, UiState<UsageReading> state) => new()
	{
		Key = "compact",
		Justify = UiComponentJustify.SpaceBetween,
		Background = Background,
		Padding = UiWidgetSafeArea.For(surface),
		Children = [CompactHeader(), Bar(FiveHour, state), Bar(SevenDay, state)],
	};

	private static UiStack Brand(string prefix, double width) => new()
	{
		Key = $"{prefix}-brand",
		MainSize = width,
		Justify = UiComponentJustify.Center,
		Gap = UiSize.FromBasis(0.02),
		Children =
		[
			Mark($"{prefix}-mark", width),
			new UiTextRun
			{
				Key = $"{prefix}-name",
				Text = "Claude",
				Size = 0.06,
				MinSize = 0.04,
				Weight = UiComponentTextWeights.SemiBold,
				Color = TextSecondary,
				Align = UiComponentAlignments.Center,
			},
		],
	};

	private static UiStack CompactHeader() => new()
	{
		Key = "compact-header",
		MainSize = 0.16,
		Direction = UiComponentDirections.Horizontal,
		Align = UiComponentAlignments.Center,
		Gap = UiSize.FromBasis(0.03),
		Children =
		[
			Mark("compact-mark", 0.16),
			new UiTextRun
			{
				Key = "compact-name",
				Text = "Claude",
				Size = 0.12,
				MinSize = 0.08,
				Weight = UiComponentTextWeights.SemiBold,
				Color = TextPrimary,
				Fill = true,
			},
		],
	};

	private static UiShape Mark(string key, double size) => new()
	{
		Key = key,
		Shape = UiComponentShapes.Path,
		Path = MarkPath,
		Color = Accent,
		MainSize = size,
	};

	private static UiStack Ring(Window window, UiState<UsageReading> state) => new()
	{
		Key = $"ring-{window.Key}",
		Fill = true,
		Align = UiComponentAlignments.Center,
		Justify = UiComponentJustify.SpaceBetween,
		Children =
		[
			new UiLayer
			{
				Key = $"ring-{window.Key}-dial",
				Fill = true,
				Children =
				[
					new UiGauge
					{
						Key = $"ring-{window.Key}-gauge",
						Level = UiValue.From(() => (PercentOf(window, state) ?? 0) / 100),
						StartAngle = 0,
						EndAngle = 360,
						Thickness = UiSize.FromParent(0.11, 0.012),
						LevelColor = UiValue.From(() => ColorFor(PercentOf(window, state))),
						TrackColor = Track,
					},
					new UiStack
					{
						Key = $"ring-{window.Key}-center",
						Justify = UiComponentJustify.Center,
						Align = UiComponentAlignments.Center,
						Children =
						[
							new UiTextRun
							{
								Key = $"ring-{window.Key}-percent",
								Text = UiText.From(() => PercentText(PercentOf(window, state))),
								Size = UiSize.FromParent(0.22, 0.04),
								Weight = UiComponentTextWeights.Bold,
								Color = TextPrimary,
								Align = UiComponentAlignments.Center,
							},
						],
					},
				],
			},
			new UiTextRun
			{
				Key = $"ring-{window.Key}-caption",
				Text = UiText.From(() => $"{window.Label} · {ResetsInOf(window, state) ?? "--"}"),
				Size = 0.085,
				MinSize = 0.05,
				Color = TextSecondary,
				Align = UiComponentAlignments.Center,
			},
		],
	};

	private static UiStack Bar(Window window, UiState<UsageReading> state) => new()
	{
		Key = $"bar-{window.Key}",
		Gap = UiSize.FromBasis(0.015),
		Children =
		[
			new UiTextRun
			{
				Key = $"bar-{window.Key}-text",
				Text = UiText.From(() => $"{window.Label} {PercentText(PercentOf(window, state))}"),
				Size = 0.13,
				MinSize = 0.08,
				Weight = UiComponentTextWeights.Bold,
				Color = TextPrimary,
			},
			new UiRangeBar
			{
				Key = $"bar-{window.Key}-range",
				MainSize = 0.07,
				Start = 0,
				End = UiValue.From(() => (PercentOf(window, state) ?? 0) / 100),
				StartColor = UiValue.From(() => ColorFor(PercentOf(window, state))),
				EndColor = UiValue.From(() => ColorFor(PercentOf(window, state))),
			},
		],
	};

	private static double? PercentOf(Window window, UiState<UsageReading> state) =>
		UsageDisplay.Percent(window.Select(state.Value.Snapshot), state.Value.Now);

	private static string? ResetsInOf(Window window, UiState<UsageReading> state) =>
		UsageDisplay.ResetsIn(window.Select(state.Value.Snapshot), state.Value.Now);

	private static string PercentText(double? percent) => percent is null ? "--" : $"{percent.Value:0}%";

	private static string ColorFor(double? percent) => percent switch
	{
		null => Unavailable,
		>= 85 => Critical,
		>= 60 => Warning,
		_ => Accent,
	};
}
