using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using Serilog;

namespace ClaudeUsage;

/// <summary>
/// Publishes the Claude Code rate limit usage as variables and as a deck widget, both read from the usage
/// cache Claude Code keeps for its own /usage screen.
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IWidgetTypeProvider, IUiProvider, IAsyncDisposable
{
	private const string UsageWidgetId = "usage";

	private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

	private readonly ILogger _logger;
	private readonly UsageFileReader _usageFile;
	private readonly UsageTracker _tracker;

	public PluginIntegration(ILogger logger)
	{
		_logger = logger.ForContext<PluginIntegration>();
		_usageFile = new UsageFileReader(Environment.GetEnvironmentVariable(UsageFileReader.PathEnvironmentVariable));
		_tracker = new UsageTracker(_usageFile, _logger);
	}

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public string ProviderName => "Claude Usage";

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
	];

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("claude_five_hour_percent", VariableType.Numeric, decimalPlaces: 0, refreshInterval: RefreshInterval)
			with { Id = "five-hour-percent", DisplayName = Strings.Variables.FiveHourPercent.Name(), Description = Strings.Variables.FiveHourPercent.Description(), Unit = "%", SemanticKind = VariableSemanticKinds.Percentage },
		VariableDefinition.Eager("claude_seven_day_percent", VariableType.Numeric, decimalPlaces: 0, refreshInterval: RefreshInterval)
			with { Id = "seven-day-percent", DisplayName = Strings.Variables.SevenDayPercent.Name(), Description = Strings.Variables.SevenDayPercent.Description(), Unit = "%", SemanticKind = VariableSemanticKinds.Percentage },
		VariableDefinition.Eager("claude_five_hour_resets_in", VariableType.Text, refreshInterval: RefreshInterval)
			with { Id = "five-hour-resets-in", DisplayName = Strings.Variables.FiveHourResetsIn.Name(), Description = Strings.Variables.FiveHourResetsIn.Description() },
		VariableDefinition.Eager("claude_seven_day_resets_in", VariableType.Text, refreshInterval: RefreshInterval)
			with { Id = "seven-day-resets-in", DisplayName = Strings.Variables.SevenDayResetsIn.Name(), Description = Strings.Variables.SevenDayResetsIn.Description() },
	];

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var snapshot = await _usageFile.ReadAsync(cancellationToken);
		var now = DateTimeOffset.UtcNow;

		return localId switch
		{
			"five-hour-percent" => ReadPercent(snapshot.FiveHour, now),
			"seven-day-percent" => ReadPercent(snapshot.SevenDay, now),
			"five-hour-resets-in" => ReadResetsIn(snapshot.FiveHour, now),
			"seven-day-resets-in" => ReadResetsIn(snapshot.SevenDay, now),
			_ => VariableReading.Unavailable,
		};
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [UsageWidgetType];

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
	{
		await context.RegisterWidgetTypeAsync(UsageWidgetType, cancellationToken);
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var surface = request.Surface;
		if (surface.Kind is not (UiSurfaceKinds.Widget or UiSurfaceKinds.Preview))
		{
			return Task.FromResult<IUiSession?>(null);
		}

		// A picker card is drawn before anything is bound, so it shows sample numbers instead of live data.
		var state = UiWidgetSurface.IsSampleRequested(surface)
			? new UiState<UsageReading>(UsageWidgetView.Sample(DateTimeOffset.UtcNow))
			: _tracker.Current;

		return Task.FromResult<IUiSession?>(new ViewSession(new UiView(surface, UsageWidgetView.Build(surface, state))));
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		_tracker.Start();
		_logger.Information("Initialized.");
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => _tracker.StopAsync().AsTask();

	public async ValueTask DisposeAsync()
	{
		await _tracker.StopAsync();
		_usageFile.Dispose();
	}

	private static WidgetTypeDescriptor UsageWidgetType { get; } = new(
		UsageWidgetId,
		Strings.Widgets.Usage.Name(),
		Strings.Widgets.Usage.Description(),
		"{}",
		"""{"type":"object"}""",
		false,
		new Dictionary<string, string>());

	private static VariableReading ReadPercent(UsageWindow? window, DateTimeOffset now) =>
		UsageDisplay.Percent(window, now) is { } percent ? VariableReading.Of(percent) : VariableReading.Unavailable;

	private static VariableReading ReadResetsIn(UsageWindow? window, DateTimeOffset now) =>
		UsageDisplay.ResetsIn(window, now) is { } text ? VariableReading.Of(text) : VariableReading.Unavailable;
}
