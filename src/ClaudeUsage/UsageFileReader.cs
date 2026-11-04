using System.Text.Json;

namespace ClaudeUsage;

/// <summary>
/// Reads the usage Claude Code already fetched for its own <c>/usage</c> screen. A missing, locked or
/// half-written file keeps the last good reading, and a reading older than Claude Code's own limit counts as
/// no data.
/// </summary>
public sealed class UsageFileReader : IDisposable
{
	public const string PathEnvironmentVariable = "CLAUDE_USAGE_FILE";

	private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(60);

	private readonly string _path;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private DateTime _lastWrite;
	private long _lastLength = -1;
	private volatile UsageSnapshot _lastGood = UsageSnapshot.Empty;

	public UsageFileReader(string? path = null)
	{
		_path = path ?? DefaultPath;
	}

	public static string DefaultPath { get; } = ResolveDefaultPath();

	/// <summary>
	/// When Claude Code last fetched usage, even if that reading is too old to show.
	/// </summary>
	public DateTimeOffset? FetchedAt => _lastGood.FetchedAt;

	public async Task<UsageSnapshot> ReadAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			await RefreshAsync(cancellationToken);
			return IsStale(_lastGood) ? UsageSnapshot.Empty : _lastGood;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();

	private static string ResolveDefaultPath()
	{
		var configDirectory = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
		var directory = string.IsNullOrWhiteSpace(configDirectory)
			? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
			: configDirectory;

		return Path.Combine(directory, ".claude.json");
	}

	private static bool IsStale(UsageSnapshot snapshot) =>
		snapshot.FetchedAt is not { } fetchedAt || DateTimeOffset.UtcNow - fetchedAt > MaxAge;

	private async Task RefreshAsync(CancellationToken cancellationToken)
	{
		try
		{
			var info = new FileInfo(_path);
			if (!info.Exists)
			{
				_lastGood = UsageSnapshot.Empty;
				return;
			}

			if (info.LastWriteTimeUtc == _lastWrite && info.Length == _lastLength)
			{
				return;
			}

			await using var stream = new FileStream(
				_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
			using var reader = new StreamReader(stream);
			_lastGood = UsageSnapshot.Parse(await reader.ReadToEndAsync(cancellationToken));
			_lastWrite = info.LastWriteTimeUtc;
			_lastLength = info.Length;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			// Claude Code rewrites this file while running, so a failed read is retried on the next poll.
		}
	}
}
