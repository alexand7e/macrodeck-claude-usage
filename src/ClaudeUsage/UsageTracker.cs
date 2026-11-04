using MacroDeck.Ui.Runtime;
using Serilog;

namespace ClaudeUsage;

public sealed record UsageReading(UsageSnapshot Snapshot, DateTimeOffset Now);

/// <summary>
/// Polls the usage file and exposes the latest reading as view state shared by every widget session. When a
/// refresher is given, it also asks Claude Code to renew the cache once it has gone stale.
/// </summary>
public sealed class UsageTracker(UsageFileReader reader, ILogger logger, IUsageRefresher? refresher = null) : IAsyncDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

	private readonly UsageRefreshPolicy _policy = new();

	private CancellationTokenSource? _cancellation;
	private Task? _loop;
	private Task? _refresh;
	private bool _refreshFailing;

	public UiState<UsageReading> Current { get; } = new(new UsageReading(UsageSnapshot.Empty, DateTimeOffset.UtcNow));

	public void Start()
	{
		if (_loop is not null)
		{
			return;
		}

		_cancellation = new CancellationTokenSource();
		_loop = RunAsync(_cancellation.Token);
	}

	public ValueTask DisposeAsync() => StopAsync();

	/// <summary>
	/// Stops polling without disposing anything the tracker was given, so <see cref="Start"/> can run again
	/// after the host re-initializes the integration.
	/// </summary>
	public async ValueTask StopAsync()
	{
		if (_cancellation is null || _loop is null)
		{
			return;
		}

		await _cancellation.CancelAsync();
		await _loop;
		if (_refresh is not null)
		{
			await _refresh;
		}

		_cancellation.Dispose();
		_cancellation = null;
		_loop = null;
		_refresh = null;
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var timer = new PeriodicTimer(PollInterval);
			do
			{
				await ReadAsync(cancellationToken);
				StartRefreshIfDue(cancellationToken);
			}
			while (await timer.WaitForNextTickAsync(cancellationToken));
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			logger.Error(ex, "Usage polling stopped unexpectedly.");
		}
	}

	private async Task ReadAsync(CancellationToken cancellationToken) =>
		Current.Set(new UsageReading(await reader.ReadAsync(cancellationToken), DateTimeOffset.UtcNow));

	private void StartRefreshIfDue(CancellationToken cancellationToken)
	{
		if (refresher is null || (_refresh is { IsCompleted: false }) || !_policy.ShouldRefresh(DateTimeOffset.UtcNow, reader.FetchedAt))
		{
			return;
		}

		_refresh = RefreshAsync(cancellationToken);
	}

	private async Task RefreshAsync(CancellationToken cancellationToken)
	{
		try
		{
			var before = reader.FetchedAt;
			var ran = await refresher!.RefreshAsync(cancellationToken);
			await ReadAsync(cancellationToken);

			// A run that did not move the cache forward counts as failed, so it backs off instead of repeating.
			var succeeded = ran && reader.FetchedAt is { } after && (before is null || after > before);
			_policy.RecordAttempt(DateTimeOffset.UtcNow, succeeded);
			if (!succeeded && !_refreshFailing)
			{
				logger.Warning("Asking Claude Code for fresh usage did not update its cache. Retrying later.");
			}

			_refreshFailing = !succeeded;
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_policy.RecordAttempt(DateTimeOffset.UtcNow, succeeded: false);
			logger.Error(ex, "Refreshing the usage cache failed unexpectedly.");
		}
	}
}
