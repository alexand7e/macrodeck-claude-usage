using System.Text.Json;
using MacroDeck.Plugin.Testing;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using NUnit.Framework;
using Serilog;

namespace ClaudeUsage.Tests;

[TestFixture]
[NonParallelizable]
public sealed class UsageWidgetTests
{
	private string _usageFile = null!;

	[SetUp]
	public void SetUp()
	{
		_usageFile = Path.Combine(Path.GetTempPath(), $"claude-usage-{Guid.NewGuid():N}.json");
		Environment.SetEnvironmentVariable(UsageFileReader.PathEnvironmentVariable, _usageFile);
	}

	[TearDown]
	public void TearDown()
	{
		Environment.SetEnvironmentVariable(UsageFileReader.PathEnvironmentVariable, null);
		File.Delete(_usageFile);
	}

	private static UiSessionRequest Request(string kind, bool sample = false) => new()
	{
		UiModelVersion = 1,
		Surface = new UiSurface
		{
			Kind = kind,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>
			{
				[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(sample),
			},
		},
	};

	private static async Task<string> TreeJsonAsync(IUiSession session)
	{
		await Task.Yield();
		return JsonSerializer.Serialize(session.BuildTree());
	}

	[Test]
	public async Task The_widget_type_is_declared()
	{
		await using var harness = PluginTestHarness.Create(builder => builder
			.UseLocalization(Strings.LocalizationCatalog)
			.RegisterIntegration<PluginIntegration>());
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.WidgetTypeProvider.GetWidgetTypesAsync();

		Assert.That(outcome.Succeeded, Is.True);
	}

	[Test]
	public async Task Surfaces_other_than_widget_and_preview_are_declined()
	{
		await using var integration = new PluginIntegration(Log.Logger);

		var session = await integration.CreateSessionAsync(Request(UiSurfaceKinds.Config), CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	[Test]
	public async Task A_sample_request_shows_sample_numbers_without_reading_the_file()
	{
		await using var integration = new PluginIntegration(Log.Logger);

		await using var session = await integration.CreateSessionAsync(Request(UiSurfaceKinds.Preview, sample: true), CancellationToken.None);
		var json = await TreeJsonAsync(session!);

		Assert.That(json, Does.Contain("42%"));
		Assert.That(json, Does.Contain("68%"));
	}

	[Test]
	public async Task A_live_widget_shows_dashes_until_the_file_has_data()
	{
		await using var integration = new PluginIntegration(Log.Logger);

		await using var session = await integration.CreateSessionAsync(Request(UiSurfaceKinds.Widget), CancellationToken.None);
		var json = await TreeJsonAsync(session!);

		Assert.That(json, Does.Contain("--"));
	}

	[Test]
	public async Task A_live_widget_follows_the_file()
	{
		var resets = DateTimeOffset.UtcNow.AddHours(1).AddMinutes(30);
		await File.WriteAllTextAsync(_usageFile, UsageCacheFixture.Json(23.4, resets, 71, resets));
		await using var integration = new PluginIntegration(Log.Logger);
		await using var session = await integration.CreateSessionAsync(Request(UiSurfaceKinds.Widget), CancellationToken.None);
		await integration.InitializeAsync(new MacroDeck.Plugin.Testing.Fakes.FakeIntegrationContext());

		var json = string.Empty;
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (DateTime.UtcNow < deadline && !json.Contains("23%"))
		{
			await Task.Delay(100);
			json = await TreeJsonAsync(session!);
		}

		Assert.That(json, Does.Contain("23%"));
		Assert.That(json, Does.Contain("71%"));
	}
}
