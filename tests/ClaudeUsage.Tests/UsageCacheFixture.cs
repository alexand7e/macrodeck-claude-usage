using System.Globalization;

namespace ClaudeUsage.Tests;

/// <summary>
/// Builds a file in the shape of Claude Code's config, including keys the plugin must ignore.
/// </summary>
public static class UsageCacheFixture
{
	public static string Json(
		double? fiveHourPercent,
		DateTimeOffset? fiveHourResets,
		double? sevenDayPercent,
		DateTimeOffset? sevenDayResets,
		DateTimeOffset? fetchedAt = null)
	{
		var fetched = (fetchedAt ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
		return "{\"oauthAccount\":{\"emailAddress\":\"not-read@example.com\"},"
			+ $"\"cachedUsageUtilization\":{{\"fetchedAtMs\":{fetched},\"utilization\":{{"
			+ $"\"five_hour\":{Window(fiveHourPercent, fiveHourResets)},"
			+ $"\"seven_day\":{Window(sevenDayPercent, sevenDayResets)},"
			+ "\"extra_usage\":{\"is_enabled\":false}}}}";
	}

	private static string Window(double? percent, DateTimeOffset? resets) =>
		percent is null || resets is null
			? "null"
			: string.Create(
				CultureInfo.InvariantCulture,
				$"{{\"utilization\":{percent},\"resets_at\":\"{resets.Value:yyyy-MM-ddTHH:mm:ss.ffffffzzz}\"}}");
}
