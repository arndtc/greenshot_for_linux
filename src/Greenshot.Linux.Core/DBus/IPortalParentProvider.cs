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

namespace Greenshot.Linux.Core.DBus;

/// <summary>
/// A short-lived focused application window for a portal request. GNOME refuses the Screenshot access dialog
/// unless the requesting app is focused ("Only the focused app is allowed to show a system access dialog").
/// </summary>
public interface IPortalParentSession : IAsyncDisposable
{
    /// <summary>
    /// Portal parent_window: <c>x11:&lt;xid&gt;</c>, <c>wayland:&lt;handle&gt;</c>, or empty when unknown
    /// </summary>
    string ParentWindow { get; }
}

/// <summary>
/// Presents a focused window before portal calls that may show a system access dialog (Screenshot on GNOME).
/// </summary>
public interface IPortalParentProvider
{
    Task<IPortalParentSession> BeginAsync(CancellationToken cancellationToken = default);
}
