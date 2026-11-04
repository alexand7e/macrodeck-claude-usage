namespace ClaudeUsage;

public static class UsageDisplay
{
	/// <summary>
	/// Claude Code drops a window once its reset time passes, so a window that has reset has no usage yet.
	/// </summary>
	public static double? Percent(UsageWindow? window, DateTimeOffset now)
	{
		if (window is null)
		{
			return null;
		}

		return window.ResetsAt <= now ? 0d : window.Percent;
	}

	public static string? ResetsIn(UsageWindow? window, DateTimeOffset now)
	{
		if (window is null)
		{
			return null;
		}

		return window.ResetsAt <= now ? "-" : UsageFormat.Remaining(window.ResetsAt - now);
	}
}
