/*
 * Greenshot - a free and open source screenshot tool
 * Copyright (C) 2007-2026 Thomas Braun, Jens Klingen, Robin Krom
 *
 * For more information see: https://getgreenshot.org/
 * The Greenshot project is hosted on GitHub https://github.com/greenshot/greenshot
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

namespace Greenshot.Linux.Core.Hotkeys;

/// <summary>
/// XFCE keyboard shortcuts via <c>xfconf-query</c> on channel <c>xfce4-keyboard-shortcuts</c>.
/// Detects conflicts (e.g. <c>xfce4-screenshooter</c> on Print) and can optionally assign
/// <c>greenshot capture …</c> commands.
/// </summary>
public static class XfceKeyboardShortcuts
{
    public const string Channel = "xfce4-keyboard-shortcuts";

    /// <summary>
    /// Default Print-family bindings Greenshot wants on XFCE (xfconf key → command).
    /// </summary>
    public static IReadOnlyList<(string XfconfKey, string Command, string Description)> DefaultBindings { get; } =
    [
        ("Print", "greenshot capture region", "Capture region"),
        ("<Alt>Print", "greenshot capture activewindow", "Capture active window"),
        ("<Primary>Print", "greenshot capture fullscreen", "Capture full screen"),
        ("<Shift>Print", "greenshot capture last", "Capture last region"),
    ];

    public static bool IsAvailable => ProcessRunner.IsOnPath("xfconf-query");

    /// <summary>
    /// Read the effective command for a shortcut key (custom overrides default).
    /// </summary>
    public static async Task<string?> GetCommandAsync(string xfconfKey, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        foreach (var prefix in new[] { "/commands/custom/", "/commands/default/" })
        {
            var value = await QueryAsync(prefix + xfconfKey, cancellationToken).ConfigureAwait(false);
            if (value != null)
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Log conflicts with known screenshot tools and return them for callers.
    /// </summary>
    public static async Task<IReadOnlyList<XfceShortcutConflict>> DetectConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        var conflicts = new List<XfceShortcutConflict>();
        if (!IsAvailable)
        {
            return conflicts;
        }

        foreach (var (key, desired, description) in DefaultBindings)
        {
            var current = await GetCommandAsync(key, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            if (IsGreenshotCommand(current))
            {
                continue;
            }

            var conflict = new XfceShortcutConflict(key, current, desired, description);
            conflicts.Add(conflict);
            Log.Warn(
                $"XFCE keyboard conflict: {key} is bound to '{current}' ({description} wants '{desired}'). " +
                "xfce4-screenshooter often owns Print / Alt+Print / Shift+Print. " +
                "Clear it in Settings → Keyboard → Application Shortcuts, or let Greenshot set " +
                "xfce4-keyboard-shortcuts (XfceSetShortcuts=True or GREENSHOT_XFCE_SET_SHORTCUTS=1).");
        }

        return conflicts;
    }

    /// <summary>
    /// Assign Greenshot capture commands under <c>/commands/custom/</c> when the current binding is empty,
    /// already greenshot, or a known screenshooter command.
    /// </summary>
    public static async Task<int> ApplyGreenshotBindingsAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            Log.Warn("xfconf-query not found; cannot set XFCE keyboard shortcuts");
            return 0;
        }

        int set = 0;
        foreach (var (key, command, description) in DefaultBindings)
        {
            var current = await GetCommandAsync(key, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(current) && !IsGreenshotCommand(current) && !IsScreenshooterCommand(current))
            {
                Log.Warn($"XFCE shortcut {key} is '{current}' — not overwriting (wanted {command} for {description})");
                continue;
            }

            if (await SetCustomAsync(key, command, cancellationToken).ConfigureAwait(false))
            {
                set++;
                Log.Info($"XFCE shortcut set: {key} → {command} ({description})");
            }
        }

        return set;
    }

    public static bool IsGreenshotCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command)
        && command.Contains("greenshot", StringComparison.OrdinalIgnoreCase);

    public static bool IsScreenshooterCommand(string? command) =>
        !string.IsNullOrWhiteSpace(command)
        && (command.Contains("xfce4-screenshooter", StringComparison.OrdinalIgnoreCase)
            || command.Contains("gnome-screenshot", StringComparison.OrdinalIgnoreCase)
            || command.Contains("spectacle", StringComparison.OrdinalIgnoreCase)
            || command.Contains("flameshot", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the process or settings ask Greenshot to claim XFCE Print keys.
    /// </summary>
    public static bool ShouldApplyFromEnvironmentOrSettings(bool settingsEnabled) =>
        settingsEnabled
        || string.Equals(Environment.GetEnvironmentVariable("GREENSHOT_XFCE_SET_SHORTCUTS"), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable("GREENSHOT_XFCE_SET_SHORTCUTS"), "true", StringComparison.OrdinalIgnoreCase);

    private static async Task<string?> QueryAsync(string property, CancellationToken cancellationToken)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("xfconf-query", ["-c", Channel, "-p", property],
                timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return null;
            }

            var text = result.OutputText.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception ex)
        {
            Log.Debug($"xfconf-query {property} failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> SetCustomAsync(string xfconfKey, string command, CancellationToken cancellationToken)
    {
        var property = "/commands/custom/" + xfconfKey;
        try
        {
            // Create the property if missing (-n), type string (-t string)
            var result = await ProcessRunner.RunAsync("xfconf-query",
                ["-c", Channel, "-p", property, "-n", "-t", "string", "-s", command],
                timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return true;
            }

            // Property may already exist: set without -n
            result = await ProcessRunner.RunAsync("xfconf-query",
                ["-c", Channel, "-p", property, "-s", command],
                timeout: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not set XFCE shortcut {property}: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// An XFCE shortcut that is bound to something other than Greenshot.
/// </summary>
public sealed record XfceShortcutConflict(string XfconfKey, string CurrentCommand, string DesiredCommand, string Description);
