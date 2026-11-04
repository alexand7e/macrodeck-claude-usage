using MacroDeck.Ui.Runtime;
using Serilog;

namespace ClaudeUsage;

public sealed record UsageReading(UsageSnapshot Snapshot, DateTimeOffset Now);

/// <summary>
/// Polls the usage file and exposes the latest reading as view state shared by every widget session.
/// </summary>
public sealed class UsageTracker(UsageFileReader reader, ILogger logger) : IAsyncDisposable
{
	private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

	private CancellationTokenSource? _cancellation;
	private Task? _loop;

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
		_cancellation.Dispose();
		_cancellation = null;
		_loop = null;
	}

	private async Task RunAsync(CancellationToken cancellationToken)
	{
		try
		{
			using var timer = new PeriodicTimer(PollInterval);
			do
			{
				Current.Set(new UsageReading(await reader.ReadAsync(cancellationToken), DateTimeOffset.UtcNow));
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
}
