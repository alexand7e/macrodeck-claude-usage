using NUnit.Framework;
using Serilog;

namespace ClaudeUsage.Tests;

[TestFixture]
public sealed class UsageRefreshPolicyTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void A_cache_that_was_never_fetched_is_due()
	{
		Assert.That(new UsageRefreshPolicy().ShouldRefresh(Now, null), Is.True);
	}

	[TestCase(0, false)]
	[TestCase(299, false)]
	[TestCase(300, true)]
	[TestCase(3600, true)]
	public void A_cache_is_due_once_it_is_five_minutes_old(int ageSeconds, bool expected)
	{
		var due = new UsageRefreshPolicy().ShouldRefresh(Now, Now.AddSeconds(-ageSeconds));

		Assert.That(due, Is.EqualTo(expected));
	}

	[Test]
	public void A_failed_attempt_backs_off_and_the_wait_doubles_up_to_thirty_minutes()
	{
		var policy = new UsageRefreshPolicy();
		var stale = Now.AddHours(-2);

		policy.RecordAttempt(Now, succeeded: false);
		Assert.That(policy.ShouldRefresh(Now.AddMinutes(4), stale), Is.False);
		Assert.That(policy.ShouldRefresh(Now.AddMinutes(5), stale), Is.True);

		policy.RecordAttempt(Now.AddMinutes(5), succeeded: false);
		Assert.That(policy.ShouldRefresh(Now.AddMinutes(14), stale), Is.False);
		Assert.That(policy.ShouldRefresh(Now.AddMinutes(15), stale), Is.True);

		var at = Now.AddMinutes(15);
		for (var i = 0; i < 6; i++)
		{
			policy.RecordAttempt(at, succeeded: false);
		}

		Assert.That(policy.ShouldRefresh(at.AddMinutes(29), stale), Is.False);
		Assert.That(policy.ShouldRefresh(at.AddMinutes(30), stale), Is.True);
	}

	[Test]
	public void A_success_clears_the_backoff()
	{
		var policy = new UsageRefreshPolicy();
		policy.RecordAttempt(Now, succeeded: false);
		policy.RecordAttempt(Now.AddMinutes(5), succeeded: false);

		policy.RecordAttempt(Now.AddMinutes(15), succeeded: true);

		Assert.That(policy.ShouldRefresh(Now.AddMinutes(15), Now.AddHours(-1)), Is.True);
		policy.RecordAttempt(Now.AddMinutes(15), succeeded: false);
		Assert.That(policy.ShouldRefresh(Now.AddMinutes(20), Now.AddHours(-1)), Is.True);
	}
}

[TestFixture]
public sealed class ClaudeCliLocatorTests
{
	private string _root = null!;

	[SetUp]
	public void SetUp() => _root = Directory.CreateTempSubdirectory("claude-locator-").FullName;

	[TearDown]
	public void TearDown() => Directory.Delete(_root, recursive: true);

	private string Touch(params string[] segments)
	{
		var path = Path.Combine([_root, .. segments]);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, string.Empty);
		return path;
	}

	private string Home => Path.Combine(_root, "home");

	private string LocalAppData => Path.Combine(_root, "local");

	[Test]
	public void The_override_wins_and_a_missing_override_finds_nothing()
	{
		var custom = Touch("custom", "claude.exe");
		Touch("home", ".local", "bin", "claude.exe");

		Assert.That(ClaudeCliLocator.Find(custom, [], Home, LocalAppData), Is.EqualTo(custom));
		Assert.That(ClaudeCliLocator.Find(Path.Combine(_root, "nope.exe"), [], Home, LocalAppData), Is.Null);
	}

	[Test]
	public void A_rooted_path_entry_is_used_and_a_relative_one_is_ignored()
	{
		var onPath = Touch("tools", "claude.exe");

		Assert.That(ClaudeCliLocator.Find(null, [".", "tools", Path.Combine(_root, "tools")], Home, LocalAppData), Is.EqualTo(onPath));
		Assert.That(ClaudeCliLocator.Find(null, [".", "tools"], Home, LocalAppData), Is.Null);
	}

	[Test]
	public void The_newest_editor_extension_is_chosen_by_version_number_not_by_text()
	{
		Touch("home", ".vscode", "extensions", "anthropic.claude-code-2.1.99-win32-x64", "resources", "native-binary", "claude.exe");
		var newest = Touch("home", ".vscode", "extensions", "anthropic.claude-code-2.1.309-win32-x64", "resources", "native-binary", "claude.exe");
		Touch("home", ".vscode", "extensions", "anthropic.claude-code-2.1.100-win32-x64", "resources", "native-binary", "claude.exe");

		Assert.That(ClaudeCliLocator.Find(null, [], Home, LocalAppData), Is.EqualTo(newest));
	}

	[Test]
	public void A_standalone_install_is_preferred_to_an_editor_extension()
	{
		var standalone = Touch("home", ".local", "bin", "claude.exe");
		Touch("home", ".vscode", "extensions", "anthropic.claude-code-2.1.309-win32-x64", "resources", "native-binary", "claude.exe");

		Assert.That(ClaudeCliLocator.Find(null, [], Home, LocalAppData), Is.EqualTo(standalone));
	}

	[Test]
	public void Nothing_found_returns_null()
	{
		Assert.That(ClaudeCliLocator.Find(null, [], Home, LocalAppData), Is.Null);
	}
}

[TestFixture]
[NonParallelizable]
public sealed class UsageTrackerRefreshTests
{
	private string _file = null!;

	[SetUp]
	public void SetUp() => _file = Path.Combine(Path.GetTempPath(), $"claude-tracker-{Guid.NewGuid():N}.json");

	[TearDown]
	public void TearDown() => File.Delete(_file);

	private sealed class FakeRefresher(Action? onRefresh = null, bool result = true) : IUsageRefresher
	{
		public int Calls { get; private set; }

		public Task<bool> RefreshAsync(CancellationToken cancellationToken)
		{
			Calls++;
			onRefresh?.Invoke();
			return Task.FromResult(result);
		}
	}

	private async Task<UsageTracker> StartTrackerAsync(FakeRefresher refresher)
	{
		var tracker = new UsageTracker(new UsageFileReader(_file), Log.Logger, refresher);
		tracker.Start();
		await Task.Delay(400);
		return tracker;
	}

	[Test]
	public async Task A_stale_cache_is_refreshed_and_the_new_numbers_are_shown()
	{
		var resets = DateTimeOffset.UtcNow.AddHours(2);
		await File.WriteAllTextAsync(_file, UsageCacheFixture.Json(10, resets, 20, resets, fetchedAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
		var refresher = new FakeRefresher(() =>
			File.WriteAllText(_file, UsageCacheFixture.Json(55.5, resets, 21, resets, fetchedAt: DateTimeOffset.UtcNow)));

		await using var tracker = await StartTrackerAsync(refresher);

		Assert.That(refresher.Calls, Is.EqualTo(1));
		Assert.That(tracker.Current.Value.Snapshot.FiveHour!.Percent, Is.EqualTo(55.5));
	}

	[Test]
	public async Task A_fresh_cache_is_left_alone()
	{
		var resets = DateTimeOffset.UtcNow.AddHours(2);
		await File.WriteAllTextAsync(_file, UsageCacheFixture.Json(10, resets, 20, resets, fetchedAt: DateTimeOffset.UtcNow.AddMinutes(-1)));
		var refresher = new FakeRefresher();

		await using var tracker = await StartTrackerAsync(refresher);

		Assert.That(refresher.Calls, Is.EqualTo(0));
		Assert.That(tracker.Current.Value.Snapshot.FiveHour!.Percent, Is.EqualTo(10));
	}

	[Test]
	public async Task A_missing_file_triggers_a_refresh()
	{
		var refresher = new FakeRefresher();

		await using var tracker = await StartTrackerAsync(refresher);

		Assert.That(refresher.Calls, Is.EqualTo(1));
	}

	[Test]
	public async Task A_run_that_does_not_move_the_cache_backs_off_instead_of_repeating()
	{
		var resets = DateTimeOffset.UtcNow.AddHours(2);
		await File.WriteAllTextAsync(_file, UsageCacheFixture.Json(10, resets, 20, resets, fetchedAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
		var refresher = new FakeRefresher(result: true);

		await using var tracker = await StartTrackerAsync(refresher);
		await Task.Delay(300);

		Assert.That(refresher.Calls, Is.EqualTo(1));
	}

	[Test]
	public async Task Without_a_refresher_nothing_is_started()
	{
		await using var tracker = new UsageTracker(new UsageFileReader(_file), Log.Logger);

		tracker.Start();
		await Task.Delay(300);

		Assert.That(tracker.Current.Value.Snapshot, Is.EqualTo(UsageSnapshot.Empty));
	}
}
