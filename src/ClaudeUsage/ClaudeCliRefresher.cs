using System.ComponentModel;
using System.Diagnostics;
using Serilog;

namespace ClaudeUsage;

public interface IUsageRefresher
{
	Task<bool> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Runs <c>claude -p /usage</c>, the same screen the user can open by hand. Claude Code answers it from the
/// usage endpoint and refreshes its own cache as a side effect. No prompt reaches a model and, with
/// <c>--no-session-persistence</c>, no session is saved.
/// </summary>
public sealed class ClaudeCliRefresher(ILogger logger) : IUsageRefresher
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

	private bool _reportedMissing;

	public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
	{
		var executable = ClaudeCliLocator.Find();
		if (executable is null)
		{
			if (!_reportedMissing)
			{
				logger.Warning("Claude Code was not found, so the usage cache cannot be refreshed. Set {Variable} to its path.", ClaudeCliLocator.PathEnvironmentVariable);
				_reportedMissing = true;
			}

			return false;
		}

		_reportedMissing = false;
		return await RunAsync(executable, cancellationToken);
	}

	private async Task<bool> RunAsync(string executable, CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo(executable)
		{
			// A neutral directory keeps Claude Code from loading a project's settings and trust prompts.
			WorkingDirectory = Path.GetTempPath(),
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		startInfo.ArgumentList.Add("-p");
		startInfo.ArgumentList.Add("/usage");
		startInfo.ArgumentList.Add("--no-session-persistence");

		try
		{
			using var process = Process.Start(startInfo);
			if (process is null)
			{
				return false;
			}

			process.StandardInput.Close();
			var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
			var errors = process.StandardError.ReadToEndAsync(cancellationToken);

			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeout.CancelAfter(Timeout);
			try
			{
				await process.WaitForExitAsync(timeout.Token);
				await Task.WhenAll(output, errors);
			}
			catch (OperationCanceledException)
			{
				process.Kill(entireProcessTree: true);
				cancellationToken.ThrowIfCancellationRequested();
				logger.Warning("Claude Code did not finish the usage refresh within {Seconds} seconds.", Timeout.TotalSeconds);
				return false;
			}

			return process.ExitCode == 0;
		}
		catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
		{
			logger.Warning(ex, "Could not run Claude Code to refresh the usage cache.");
			return false;
		}
	}
}
