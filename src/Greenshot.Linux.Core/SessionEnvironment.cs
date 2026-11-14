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

namespace Greenshot.Linux.Core;

/// <summary>
/// Which display server the session uses. Prefer <c>XDG_SESSION_TYPE</c>; fall back to the presence of
/// <c>WAYLAND_DISPLAY</c> / <c>DISPLAY</c> (XWayland keeps DISPLAY set on Wayland, so that alone is not enough).
/// </summary>
public static class SessionEnvironment
{
    public static bool IsWayland
    {
        get
        {
            var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(session, "x11", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        }
    }

    public static bool IsX11
    {
        get
        {
            var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (string.Equals(session, "x11", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        }
    }

    /// <summary>
    /// Desktop name from <c>XDG_CURRENT_DESKTOP</c> (may be colon-separated, e.g. <c>XFCE</c>).
    /// </summary>
    public static string CurrentDesktop =>
        Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;

    public static bool IsXfce =>
        CurrentDesktop.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.Equals("XFCE", StringComparison.OrdinalIgnoreCase));
}
