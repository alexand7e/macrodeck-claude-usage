namespace ClaudeUsage;

/// <summary>
/// Decides when the usage cache is worth refreshing by running Claude Code. A refresh is due once the cache is
/// older than <see cref="StaleAfter"/>, and a failed attempt backs off so a missing or logged-out Claude Code is
/// not started over and over.
/// </summary>
public sealed class UsageRefreshPolicy
{
	public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

	private static readonly TimeSpan InitialBackoff = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(30);

	private readonly object _gate = new();
	private DateTimeOffset _nextAttempt = DateTimeOffset.MinValue;
	private TimeSpan _backoff = InitialBackoff;

	public bool ShouldRefresh(DateTimeOffset now, DateTimeOffset? fetchedAt)
	{
		lock (_gate)
		{
			return now >= _nextAttempt && (fetchedAt is null || now - fetchedAt.Value >= StaleAfter);
		}
	}

	public void RecordAttempt(DateTimeOffset now, bool succeeded)
	{
		lock (_gate)
		{
			if (succeeded)
			{
				_backoff = InitialBackoff;
				_nextAttempt = now;
				return;
			}

			_nextAttempt = now + _backoff;
			_backoff = TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));
		}
	}
}
