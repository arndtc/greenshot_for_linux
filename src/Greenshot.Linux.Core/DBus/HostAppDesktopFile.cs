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

using System.Reflection;
using System.Text;

namespace Greenshot.Linux.Core.DBus;

/// <summary>
/// Ensures a user desktop file exists with an absolute, resolvable Exec= so
/// org.freedesktop.host.portal.Registry.Register can find the app id (GIO DesktopAppInfo).
/// </summary>
public static class HostAppDesktopFile
{
    public const string InstallHint =
        "Install the desktop entry with packaging/install.sh (or ensure ~/.local/share/applications/" +
        AppIdentity.Id + ".desktop has Exec= pointing at an existing absolute binary). " +
        "dotnet run alone is not enough for host portal Registry / GlobalShortcuts.";

    /// <summary>
    /// Absolute desktop-file path under XDG data home, or null when HOME is unset.
    /// </summary>
    public static string? UserDesktopPath()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
            {
                return null;
            }

            dataHome = Path.Combine(home, ".local", "share");
        }

        return Path.Combine(dataHome, "applications", AppIdentity.Id + ".desktop");
    }

    /// <summary>
    /// Exec= value for the running process: absolute binary, or `dotnet` + absolute dll when hosted.
    /// </summary>
    public static string? ResolveExec()
    {
        var process = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(process) || !File.Exists(process))
        {
            return null;
        }

        if (Path.GetFileNameWithoutExtension(process).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var dll = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(dll) || !File.Exists(dll))
            {
                return null;
            }

            return Quote(process) + " " + Quote(dll);
        }

        return Quote(process);
    }

    /// <summary>
    /// Write or update the user desktop file so Exec= is the running process. Returns the path written, or null.
    /// Waits for <c>update-desktop-database</c> so GIO / the portal can see the file before Register retries.
    /// </summary>
    public static async Task<string?> EnsureForRunningProcessAsync()
    {
        var path = UserDesktopPath();
        var exec = ResolveExec();
        if (path == null || exec == null)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(path);
        if (directory == null)
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        // No UTF-8 BOM: GIO rejects desktop files that don't start with '['
        File.WriteAllText(path, BuildDesktopEntry(exec), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (ProcessRunner.IsOnPath("update-desktop-database"))
        {
            try
            {
                await ProcessRunner.RunAsync("update-desktop-database", [directory], timeout: TimeSpan.FromSeconds(10))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug($"update-desktop-database failed: {ex.Message}");
            }
        }

        // GIO / portal may still need a beat after the file and cache update
        await Task.Delay(400).ConfigureAwait(false);
        return path;
    }

    internal static string BuildDesktopEntry(string exec) =>
        $"""
         [Desktop Entry]
         Type=Application
         Name=Greenshot
         GenericName=Screenshot Tool
         Comment=Capture a region, a window or the full screen
         Exec={exec} --open-with %F
         Icon={AppIdentity.Id}
         Terminal=false
         Categories=Graphics;
         Keywords=screenshot;capture;print;screen;
         StartupWMClass=greenshot
         MimeType=image/png;image/jpeg;image/webp;image/bmp;image/gif;

         """;

    private static string Quote(string path) =>
        path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
}
