using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Testing;
using NUnit.Framework;

namespace ClaudeUsage.Tests;

[TestFixture]
[NonParallelizable]
public sealed class PluginIntegrationTests
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

	private static PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder => builder
			.UseLocalization(Strings.LocalizationCatalog)
			.RegisterIntegration<PluginIntegration>());

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness();

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task Percent_variables_are_unavailable_while_the_file_does_not_exist()
	{
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var reading = (await harness.Variables.GetAsync("five-hour-percent")).DataAs<VariableReadingDto>();

		Assert.That(reading!.Value.Kind, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task Percent_and_reset_variables_follow_the_file()
	{
		var resets = DateTimeOffset.UtcNow.AddHours(2).AddMinutes(30);
		await File.WriteAllTextAsync(_usageFile, UsageCacheFixture.Json(23.5, resets, 41.2, resets));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var fiveHour = (await harness.Variables.GetAsync("five-hour-percent")).DataAs<VariableReadingDto>();
		var sevenDay = (await harness.Variables.GetAsync("seven-day-percent")).DataAs<VariableReadingDto>();
		var resetsIn = (await harness.Variables.GetAsync("five-hour-resets-in")).DataAs<VariableReadingDto>();

		Assert.That(fiveHour!.Value.Number, Is.EqualTo(23.5));
		Assert.That(sevenDay!.Value.Number, Is.EqualTo(41.2));
		Assert.That(resetsIn!.Value.Text, Is.EqualTo("2h 29m").Or.EqualTo("2h 30m"));
	}

	[Test]
	public async Task A_cache_older_than_an_hour_reads_as_unavailable()
	{
		var resets = DateTimeOffset.UtcNow.AddHours(2);
		await File.WriteAllTextAsync(
			_usageFile,
			UsageCacheFixture.Json(23.5, resets, 41.2, resets, fetchedAt: DateTimeOffset.UtcNow.AddHours(-2)));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var reading = (await harness.Variables.GetAsync("five-hour-percent")).DataAs<VariableReadingDto>();

		Assert.That(reading!.Value.Kind, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task A_window_that_has_reset_reads_as_zero_percent()
	{
		var past = DateTimeOffset.UtcNow.AddMinutes(-5);
		await File.WriteAllTextAsync(_usageFile, UsageCacheFixture.Json(80, past, null, null));
		await using var harness = CreateHarness();
		await harness.InitializeIntegrationsAsync();

		var fiveHour = (await harness.Variables.GetAsync("five-hour-percent")).DataAs<VariableReadingDto>();
		var sevenDay = (await harness.Variables.GetAsync("seven-day-percent")).DataAs<VariableReadingDto>();

		Assert.That(fiveHour!.Value.Number, Is.EqualTo(0));
		Assert.That(sevenDay!.Value.Kind, Is.EqualTo("unavailable"));
	}
}

[TestFixture]
public sealed class UsageSnapshotTests
{
	[Test]
	public void Both_windows_and_the_fetch_time_are_read()
	{
		var fetched = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
		var snapshot = UsageSnapshot.Parse(
			UsageCacheFixture.Json(10, fetched.AddHours(1), 20.5, fetched.AddDays(3), fetched));

		Assert.That(snapshot.FiveHour!.Percent, Is.EqualTo(10));
		Assert.That(snapshot.SevenDay!.Percent, Is.EqualTo(20.5));
		Assert.That(snapshot.SevenDay!.ResetsAt, Is.EqualTo(fetched.AddDays(3)));
		Assert.That(snapshot.FetchedAt, Is.EqualTo(fetched));
	}

	[Test]
	public void The_real_claude_code_shape_is_understood()
	{
		var snapshot = UsageSnapshot.Parse(
			"""
			{"cachedUsageUtilization":{"fetchedAtMs":1793802384476,"accountUuid":"x","utilization":{
			"five_hour":{"utilization":18,"resets_at":"2026-11-04T15:40:00.318317+00:00","limit_dollars":null},
			"seven_day":{"utilization":11,"resets_at":"2026-11-08T11:00:00.318335+00:00"},
			"limits":[{"kind":"session","percent":18}],"extra_usage":{"is_enabled":false}}}}
			""");

		Assert.That(snapshot.FiveHour!.Percent, Is.EqualTo(18));
		Assert.That(snapshot.FiveHour!.ResetsAt, Is.EqualTo(DateTimeOffset.Parse("2026-11-04T15:40:00.318317+00:00", System.Globalization.CultureInfo.InvariantCulture)));
		Assert.That(snapshot.SevenDay!.Percent, Is.EqualTo(11));
	}

	[Test]
	public void Null_or_missing_windows_are_absent()
	{
		var snapshot = UsageSnapshot.Parse(UsageCacheFixture.Json(null, null, null, null));

		Assert.That(snapshot.FiveHour, Is.Null);
		Assert.That(snapshot.SevenDay, Is.Null);
	}

	[TestCase("{}")]
	[TestCase("[]")]
	[TestCase("""{"cachedUsageUtilization":null}""")]
	public void A_file_without_the_usage_cache_is_empty(string json)
	{
		var snapshot = UsageSnapshot.Parse(json);

		Assert.That(snapshot.FiveHour, Is.Null);
		Assert.That(snapshot.SevenDay, Is.Null);
	}

	[TestCase(45, "1m")]
	[TestCase(60 * 5 + 20, "6m")]
	[TestCase(3600 * 2 + 60 * 30, "2h 30m")]
	[TestCase(86400 * 3 + 3600 * 4, "3d 4h")]
	public void Remaining_time_is_formatted_compactly(int seconds, string expected)
	{
		Assert.That(UsageFormat.Remaining(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
	}
}

/// <summary>
/// The localization set is generated from <c>Localization/*.resx</c>, so these guard the wiring rather
/// than any wording: a missing catalog registration leaves every label showing its raw key.
/// </summary>
[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.alexand7e.claude-usage"));
	}

	[Test]
	public void English_is_the_default_culture()
	{
		Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
		Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
	}
}
