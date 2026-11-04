using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeUsage;

/// <summary>
/// Finds the Claude Code executable. It is often not on PATH: the VS Code extension bundles its own copy.
/// </summary>
public static partial class ClaudeCliLocator
{
	public const string PathEnvironmentVariable = "CLAUDE_USAGE_CLI";

	private const string ExecutableName = "claude.exe";

	private static readonly string[] ExtensionRoots = [".vscode", ".vscode-insiders", ".cursor", ".windsurf", ".vscode-oss"];

	public static string? Find() => Find(
		Environment.GetEnvironmentVariable(PathEnvironmentVariable),
		(Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator),
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

	public static string? Find(string? overridePath, IEnumerable<string> pathEntries, string homeDirectory, string localAppData)
	{
		if (!string.IsNullOrWhiteSpace(overridePath))
		{
			return File.Exists(overridePath) ? overridePath : null;
		}

		return Candidates(pathEntries, homeDirectory, localAppData).FirstOrDefault(File.Exists);
	}

	private static IEnumerable<string> Candidates(IEnumerable<string> pathEntries, string homeDirectory, string localAppData)
	{
		// Relative PATH entries depend on the working directory, so they are never trusted.
		foreach (var entry in pathEntries.Where(entry => Path.IsPathRooted(entry)))
		{
			yield return Path.Combine(entry, ExecutableName);
		}

		yield return Path.Combine(homeDirectory, ".local", "bin", ExecutableName);
		yield return Path.Combine(localAppData, "Programs", "claude", ExecutableName);

		foreach (var root in ExtensionRoots)
		{
			foreach (var extension in NewestExtensionsFirst(Path.Combine(homeDirectory, root, "extensions")))
			{
				yield return Path.Combine(extension, "resources", "native-binary", ExecutableName);
			}
		}
	}

	private static IEnumerable<string> NewestExtensionsFirst(string extensionsDirectory)
	{
		if (!Directory.Exists(extensionsDirectory))
		{
			return [];
		}

		return Directory.EnumerateDirectories(extensionsDirectory, "anthropic.claude-code-*")
			.Select(directory => (Directory: directory, Version: VersionOf(directory)))
			.Where(item => item.Version is not null)
			.OrderByDescending(item => item.Version)
			.Select(item => item.Directory);
	}

	private static Version? VersionOf(string directory)
	{
		var match = ExtensionVersion().Match(Path.GetFileName(directory));
		return match.Success && Version.TryParse(match.Groups[1].Value, out var version) ? version : null;
	}

	[GeneratedRegex(@"^anthropic\.claude-code-(\d+(?:\.\d+)+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ExtensionVersion();
}
