namespace ClaudeUsage;

public static class UsageFormat
{
	public static string Remaining(TimeSpan remaining)
	{
		if (remaining.TotalDays >= 1)
		{
			return $"{(int)remaining.TotalDays}d {remaining.Hours}h";
		}

		if (remaining.TotalHours >= 1)
		{
			return $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
		}

		return $"{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
	}
}
