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
using Greenshot.Linux.Core.Capturing;
using Greenshot.Linux.Core.Compositor;
using Greenshot.Linux.Core.Hotkeys;
using Greenshot.Linux.Core.Ipc;
using Greenshot.Linux.Core.X11;

namespace Greenshot.Linux.Tests.X11;

public class SessionEnvironmentTests
{
    [Fact]
    public void IsX11_false_when_session_type_is_wayland()
    {
        using var _ = new EnvScope(("XDG_SESSION_TYPE", "wayland"), ("WAYLAND_DISPLAY", "wayland-0"), ("DISPLAY", ":0"));
        Assert.False(SessionEnvironment.IsX11);
        Assert.True(SessionEnvironment.IsWayland);
    }

    [Fact]
    public void IsX11_true_when_session_type_is_x11()
    {
        using var _ = new EnvScope(("XDG_SESSION_TYPE", "x11"), ("WAYLAND_DISPLAY", null), ("DISPLAY", ":0"));
        Assert.True(SessionEnvironment.IsX11);
        Assert.False(SessionEnvironment.IsWayland);
    }

    [Fact]
    public void IsXfce_detects_desktop_token()
    {
        using var _ = new EnvScope(("XDG_CURRENT_DESKTOP", "XFCE"));
        Assert.True(SessionEnvironment.IsXfce);
    }

    [Fact]
    public void IsXfce_false_for_gnome()
    {
        using var _ = new EnvScope(("XDG_CURRENT_DESKTOP", "GNOME"));
        Assert.False(SessionEnvironment.IsXfce);
    }
}

public class XfceKeyboardShortcutsTests
{
    [Theory]
    [InlineData("greenshot capture region", true)]
    [InlineData("/usr/bin/greenshot capture fullscreen", true)]
    [InlineData("xfce4-screenshooter -r", false)]
    [InlineData(null, false)]
    public void IsGreenshotCommand(string? command, bool expected) =>
        Assert.Equal(expected, XfceKeyboardShortcuts.IsGreenshotCommand(command));

    [Theory]
    [InlineData("xfce4-screenshooter", true)]
    [InlineData("xfce4-screenshooter -w", true)]
    [InlineData("flameshot gui", true)]
    [InlineData("greenshot capture region", false)]
    public void IsScreenshooterCommand(string command, bool expected) =>
        Assert.Equal(expected, XfceKeyboardShortcuts.IsScreenshooterCommand(command));

    [Fact]
    public void ShouldApply_from_env()
    {
        using var _ = new EnvScope(("GREENSHOT_XFCE_SET_SHORTCUTS", "1"));
        Assert.True(XfceKeyboardShortcuts.ShouldApplyFromEnvironmentOrSettings(settingsEnabled: false));
    }

    [Fact]
    public void DefaultBindings_cover_print_family()
    {
        Assert.Contains(XfceKeyboardShortcuts.DefaultBindings, b => b.XfconfKey == "Print");
        Assert.Contains(XfceKeyboardShortcuts.DefaultBindings, b => b.XfconfKey == "<Alt>Print");
        Assert.Contains(XfceKeyboardShortcuts.DefaultBindings, b => b.XfconfKey == "<Primary>Print");
        Assert.Contains(XfceKeyboardShortcuts.DefaultBindings, b => b.XfconfKey == "<Shift>Print");
        Assert.All(XfceKeyboardShortcuts.DefaultBindings, b => Assert.StartsWith("greenshot capture", b.Command));
    }
}

public class X11HotkeyParseTests
{
    [Fact]
    public void TryParseHotkey_print_and_modifiers()
    {
        if (!X11Display.IsAvailable)
        {
            return; // headless CI without DISPLAY
        }

        using var display = X11Display.TryOpen();
        Assert.NotNull(display);

        Assert.True(X11GlobalHotkeys.TryParseHotkey(display.Handle, "PrintScreen", out var kc, out var mods, out _));
        Assert.NotEqual((byte)0, kc);
        Assert.Equal(0u, mods);

        Assert.True(X11GlobalHotkeys.TryParseHotkey(display.Handle, "Alt + PrintScreen", out _, out mods, out _));
        Assert.Equal(X11Native.Mod1Mask, mods);

        Assert.True(X11GlobalHotkeys.TryParseHotkey(display.Handle, "Ctrl + PrintScreen", out _, out mods, out _));
        Assert.Equal(X11Native.ControlMask, mods);

        Assert.True(X11GlobalHotkeys.TryParseHotkey(display.Handle, "Shift + PrintScreen", out _, out mods, out _));
        Assert.Equal(X11Native.ShiftMask, mods);
    }

    [Fact]
    public void TryParseHotkey_rejects_none()
    {
        if (!X11Display.IsAvailable)
        {
            return;
        }

        using var display = X11Display.TryOpen();
        Assert.NotNull(display);
        Assert.False(X11GlobalHotkeys.TryParseHotkey(display.Handle, "None", out _, out _, out var error));
        Assert.NotNull(error);
    }
}

public class X11ScreenGrabberTests
{
    [Fact]
    public async Task Grab_root_when_display_available()
    {
        if (!X11ScreenGrabber.IsAvailable)
        {
            return;
        }

        var layout = new ScreenLayout([new ScreenInfo("screen0", new LayoutRect(0, 0, 100, 100), 1.0, true)]);
        var grabber = new X11ScreenGrabber();
        using var image = await grabber.GrabAsync(layout, includeCursor: false);
        Assert.True(image.Width >= 100);
        Assert.True(image.Height >= 100);
        Assert.Equal("x11", grabber.Name);
    }
}

public class ScreenCaptureGrabberSelectionTests
{
    [Fact]
    public void UnavailableMessage_mentions_x11()
    {
        Assert.Contains("X11", ScreenCaptureService.UnavailableMessage, StringComparison.OrdinalIgnoreCase);
    }
}

public class X11FrameExtentsTests
{
    [Fact]
    public void ExpandWithFrameExtents_matches_xfce_example()
    {
        // Client 817×485 at (205,179); extents 5,5,29,5 → framed 827×519 at (200,150)
        var client = new LayoutRect(205, 179, 817, 485);
        var screen = new LayoutRect(0, 0, 1920, 1200);
        var framed = X11Compositor.ExpandWithFrameExtents(client, left: 5, right: 5, top: 29, bottom: 5, screen);
        Assert.Equal(200, framed.X);
        Assert.Equal(150, framed.Y);
        Assert.Equal(827, framed.Width);
        Assert.Equal(519, framed.Height);
    }

    [Fact]
    public void ExpandWithFrameExtents_clips_to_screen()
    {
        var client = new LayoutRect(2, 2, 100, 100);
        var screen = new LayoutRect(0, 0, 1920, 1200);
        var framed = X11Compositor.ExpandWithFrameExtents(client, left: 10, right: 10, top: 30, bottom: 10, screen);
        Assert.Equal(0, framed.X);
        Assert.Equal(0, framed.Y);
        Assert.True(framed.Width <= 1920);
        Assert.True(framed.Height <= 1200);
    }
}

public class CaptureTitleTests
{
    [Theory]
    [InlineData("My window", "My window")]
    [InlineData("My window - Greenshot image editor", "My window")]
    [InlineData("My window - Greenshot image editor - Greenshot image editor", "My window")]
    [InlineData("*My window - Greenshot image editor", "My window")]
    [InlineData("", "")]
    public void StripEditorWindowSuffix(string input, string expected) =>
        Assert.Equal(expected, CaptureTitle.StripEditorWindowSuffix(input));

    [Fact]
    public void CaptureDetails_Title_strips_editor_suffix()
    {
        var details = new CaptureDetails { Title = "Shot - Greenshot image editor - Greenshot image editor" };
        Assert.Equal("Shot", details.Title);
    }
}

public class AppCommandHelpTests
{
    [Fact]
    public void Usage_mentions_x11_for_window_capture()
    {
        Assert.Contains("X11", AppCommand.Usage, StringComparison.Ordinal);
        Assert.DoesNotContain("capture window        Select a window and capture it (Sway and Hyprland)", AppCommand.Usage);
    }
}

/// <summary>
/// Temporarily sets environment variables and restores them on dispose.
/// </summary>
internal sealed class EnvScope : IDisposable
{
    private readonly (string Key, string? Previous)[] _previous;

    public EnvScope(params (string Key, string? Value)[] values)
    {
        _previous = values.Select(v => (v.Key, Environment.GetEnvironmentVariable(v.Key))).ToArray();
        foreach (var (key, value) in values)
        {
            if (value == null)
            {
                Environment.SetEnvironmentVariable(key, null);
            }
            else
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }

    public void Dispose()
    {
        foreach (var (key, previous) in _previous)
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }
}
