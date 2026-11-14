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

using Greenshot.Linux.Core;
using Greenshot.Linux.Core.DBus;

namespace Greenshot.Linux.Tests.DBus;

public sealed class HostAppDesktopFileTests
{
    [Fact]
    public void BuildDesktopEntry_puts_absolute_exec_and_app_id()
    {
        var text = HostAppDesktopFile.BuildDesktopEntry("/usr/bin/dotnet /tmp/greenshot.dll");
        Assert.Contains("Exec=/usr/bin/dotnet /tmp/greenshot.dll --open-with %F", text, StringComparison.Ordinal);
        Assert.Contains($"Icon={AppIdentity.Id}", text, StringComparison.Ordinal);
        Assert.Contains("StartupWMClass=greenshot", text, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallHint_mentions_install_script()
    {
        Assert.Contains("packaging/install.sh", HostAppDesktopFile.InstallHint, StringComparison.Ordinal);
        Assert.Contains("dotnet run", HostAppDesktopFile.InstallHint, StringComparison.Ordinal);
    }
}
