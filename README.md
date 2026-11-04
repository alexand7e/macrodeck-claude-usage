# Claude Usage for Macro Deck

<a href="https://macro-deck.app/store/com.alexand7e.claude-usage"><img alt="Open in the Macro Deck Store" src="https://assets.macro-deck.app/branding/Open%20in%20the%20Macro%20Deck%20Store%20Badge%20Dark.png" height="56"></a>

A Macro Deck 3 plugin that shows how much of your **Claude Code** usage limits you have used: the rolling
5-hour window and the weekly window, with the time left until each one resets.

It adds a ready-made **Claude Usage** deck widget and four variables you can use anywhere in Macro Deck.

This is an independent project. It is not made, endorsed or sponsored by Anthropic or by Macro Deck. "Claude" is
a trademark of Anthropic, used here only to say what the plugin shows.

## What you get

**Widget.** Add **Claude Usage** from the widget list.

- On a wide tile (2 cells or more) it draws two rings, one for 5 hours and one for the week, with the percentage
  in the middle and the time until reset below.
- On a single cell it draws two bars.
- The color is orange, turns amber at 60% and red at 85%. Without data it shows `--`.

**Variables.**

| Variable | Value |
|---|---|
| `claude_five_hour_percent` | Percentage of the 5-hour limit used |
| `claude_seven_day_percent` | Percentage of the weekly limit used |
| `claude_five_hour_resets_in` | Time until the 5-hour reset, for example `2h 30m` |
| `claude_seven_day_resets_in` | Time until the weekly reset, for example `3d 4h` |

Use them in a button label as `{{ vars.claude_five_hour_percent }}%` or bind them to a **Gauges** ring.

## Requirements

- Windows (x64). The plugin currently ships for `win-x64` only.
- Macro Deck 3.
- Claude Code (terminal or VS Code extension) signed in with a Claude Pro, Max or Ultra plan. Those are the plans
  that have these limits.

## How it works

Claude Code keeps the result of its own `/usage` screen in its configuration file, under the key
`cachedUsageUtilization` in `~/.claude.json` (or `$CLAUDE_CONFIG_DIR/.claude.json` when that variable is set). The
plugin polls that file every 5 seconds and shows what it finds.

Claude Code renews that cache only when something asks it to, for example when you open `/usage`. So when the
cache is **5 minutes old or more**, the plugin renews it for you by running `claude -p "/usage"`, the same screen
you would open by hand. That command prints your usage, updates the cache and exits, and it takes a few seconds.
It does not send a prompt to a model and it does not save a session (`--no-session-persistence`).

- The plugin looks for `claude.exe` on `PATH`, in `~/.local/bin`, in `%LOCALAPPDATA%\Programs\claude` and in the
  newest Claude Code extension of VS Code, VS Code Insiders, Cursor and Windsurf. Set `CLAUDE_USAGE_CLI` to a full
  path to use a specific copy.
- If a run fails or does not update the cache, the plugin waits 5 minutes before trying again, then 10, up to 30,
  so it never starts Claude Code in a loop.
- Set `CLAUDE_USAGE_REFRESH=off` to turn the automatic refresh off. The plugin then only shows what the cache
  already holds, which changes when you run `/usage` yourself.

## Privacy and data handling

- The plugin **reads one file**: Claude Code's `.claude.json`. That file also contains account details. The
  plugin parses it but only looks at the `cachedUsageUtilization` key, and it never logs, stores or transmits any
  part of it. It never writes to that file.
- The plugin **starts one program**: Claude Code (`claude -p "/usage"`), at most every 5 minutes and only when the
  cache is stale. It declares the `process:spawn` permission for this. It passes no secret to it and reads nothing
  from it except whether it succeeded.
- The plugin makes **no network requests of its own** and collects no personal data. When Claude Code runs
  `/usage` it contacts Anthropic with your own sign-in, exactly as it does when you run `/usage` yourself.
- It declares the `fs:user-files` permission because it reads a file outside its own data directory.

## Limitations

- The cache is an internal detail of Claude Code, not a documented interface. A Claude Code update can change its
  shape. If that happens the widget shows `--` until the plugin is updated.
- The numbers are as fresh as the last refresh, so up to about 5 minutes old, plus the seconds a refresh takes.
  If Claude Code cannot be found or run, the plugin shows the last cache it has. A cache older than 60 minutes, the
  same limit `/usage` applies, is treated as unavailable.
- A window whose reset time has passed is shown as 0%.
- The values are the plan limits Claude Code reports. They do not include usage from other devices unless Claude
  Code itself reports it.
- Each refresh runs a short Claude Code process, which also counts as an ordinary `/usage` request to Anthropic.
- Windows only for now.

## AI disclosure

The plugin does not use AI at runtime and does not talk to any AI service. The source code and the plugin icon
were created with the help of an AI assistant (Claude) and reviewed by the maintainer.

## Development

You need the .NET 10 SDK and the Macro Deck plugin CLI (`dotnet tool install --global MacroDeck.Plugin.Cli
--prerelease`).

```powershell
dotnet build
dotnet test
macrodeck-plugin test --project src/ClaudeUsage
macrodeck-plugin build --source src/ClaudeUsage --output ./artifacts
```

To run it against a local Macro Deck, turn on **Developer Mode** in the app, then, from `src/ClaudeUsage`:

```powershell
dotnet run --launch-profile "Macro Deck - Real Host"
```

Approve the pairing prompt in Macro Deck. Run it from that folder, because the plugin looks for `manifest.json` in
the current directory. While it runs, `dotnet build` cannot replace the executable, so stop it first.

`CLAUDE_USAGE_FILE` points the plugin at a different file with the same shape, which the tests use. It also turns
the automatic refresh off, because renewing a cache only makes sense for the real one.

See `AGENTS.md` for the rules this project follows (localization, async, logging and the Store gate).

## License

MIT, see [LICENSE](LICENSE).
