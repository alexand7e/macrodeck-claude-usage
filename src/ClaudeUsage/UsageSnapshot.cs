using System.Globalization;
using System.Text.Json;

namespace ClaudeUsage;

public sealed record UsageWindow(double Percent, DateTimeOffset ResetsAt);

public sealed record UsageSnapshot(UsageWindow? FiveHour, UsageWindow? SevenDay, DateTimeOffset? FetchedAt = null)
{
	public static UsageSnapshot Empty { get; } = new(null, null);

	/// <summary>
	/// Reads the usage cache Claude Code keeps under <c>cachedUsageUtilization</c> in its own config file. Only
	/// that key is looked at; the rest of the file is account data this plugin has no use for.
	/// </summary>
	public static UsageSnapshot Parse(string json)
	{
		using var document = JsonDocument.Parse(json);
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty("cachedUsageUtilization", out var cache)
			|| cache.ValueKind != JsonValueKind.Object)
		{
			return Empty;
		}

		var fetchedAt = cache.TryGetProperty("fetchedAtMs", out var fetched) && fetched.ValueKind == JsonValueKind.Number
			? DateTimeOffset.FromUnixTimeMilliseconds(fetched.GetInt64())
			: (DateTimeOffset?)null;

		if (!cache.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Object)
		{
			return Empty with { FetchedAt = fetchedAt };
		}

		return new UsageSnapshot(
			ReadWindow(utilization, "five_hour"),
			ReadWindow(utilization, "seven_day"),
			fetchedAt);
	}

	private static UsageWindow? ReadWindow(JsonElement utilization, string name)
	{
		if (!utilization.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object
			|| !window.TryGetProperty("utilization", out var percent) || percent.ValueKind != JsonValueKind.Number
			|| !window.TryGetProperty("resets_at", out var resets) || resets.ValueKind != JsonValueKind.String
			|| !DateTimeOffset.TryParse(resets.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var resetsAt))
		{
			return null;
		}

		return new UsageWindow(percent.GetDouble(), resetsAt);
	}
}
