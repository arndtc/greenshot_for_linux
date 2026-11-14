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

using System.Runtime.InteropServices;
using Greenshot.Linux.Core.Capturing;
using Greenshot.Linux.Core.X11;

namespace Greenshot.Linux.Core.Compositor;

/// <summary>
/// Window and screen layout from the X11 root window (<c>_NET_CLIENT_LIST</c>, <c>_NET_WM_NAME</c>, geometry).
/// Enables window capture on XFCE and other X11 desktops without a Wayland compositor IPC.
/// </summary>
public sealed class X11Compositor : ICompositor
{
    public string Name { get; } = string.IsNullOrEmpty(SessionEnvironment.CurrentDesktop)
        ? "X11"
        : SessionEnvironment.CurrentDesktop;

    public bool KnowsWindows => true;

    /// <summary>
    /// When true (default), expand client bounds with <c>_NET_FRAME_EXTENTS</c> so title bars and borders are included.
    /// </summary>
    public bool IncludeDecorations { get; set; } = true;

    public Task<ScreenLayout?> GetLayoutAsync() => Task.FromResult(GetLayout());

    public Task<WindowInfo?> GetActiveWindowAsync() => Task.FromResult(GetActiveWindow());

    public Task<IReadOnlyList<WindowInfo>> GetVisibleWindowsAsync() =>
        Task.FromResult((IReadOnlyList<WindowInfo>)GetVisibleWindows());

    private static ScreenLayout? GetLayout()
    {
        using var display = X11Display.TryOpen();
        if (display == null)
        {
            return null;
        }

        // Single virtual screen for the root; multi-monitor is usually one big root on X11
        var screens = new List<ScreenInfo>
        {
            new("screen0", new LayoutRect(0, 0, display.Width, display.Height), 1.0, true),
        };
        return new ScreenLayout(screens);
    }

    private WindowInfo? GetActiveWindow()
    {
        using var display = X11Display.TryOpen();
        if (display == null)
        {
            return null;
        }

        var activeAtom = X11Native.XInternAtom(display.Handle, "_NET_ACTIVE_WINDOW", onlyIfExists: false);
        var windows = ReadWindowList(display, activeAtom);
        if (windows.Count > 0)
        {
            return DescribeWindow(display, windows[0], focused: true);
        }

        if (X11Native.XGetInputFocus(display.Handle, out var focus, out _) == X11Native.Success && focus != IntPtr.Zero
            && focus != display.Root)
        {
            return DescribeWindow(display, focus, focused: true);
        }

        return null;
    }

    private List<WindowInfo> GetVisibleWindows()
    {
        using var display = X11Display.TryOpen();
        if (display == null)
        {
            return [];
        }

        var listAtom = X11Native.XInternAtom(display.Handle, "_NET_CLIENT_LIST_STACKING", onlyIfExists: false);
        var windows = ReadWindowList(display, listAtom);
        if (windows.Count == 0)
        {
            listAtom = X11Native.XInternAtom(display.Handle, "_NET_CLIENT_LIST", onlyIfExists: false);
            windows = ReadWindowList(display, listAtom);
        }

        var active = GetActiveWindowId(display);
        // Stacking order: bottom → top; reverse so top-most is first (matches Sway/Hyprland)
        windows.Reverse();
        var result = new List<WindowInfo>();
        foreach (var window in windows)
        {
            var info = DescribeWindow(display, window, focused: window == active);
            if (info != null && !info.Bounds.IsEmpty)
            {
                result.Add(info);
            }
        }

        return result;
    }

    private static IntPtr GetActiveWindowId(X11Display display)
    {
        var activeAtom = X11Native.XInternAtom(display.Handle, "_NET_ACTIVE_WINDOW", onlyIfExists: false);
        var list = ReadWindowList(display, activeAtom);
        return list.Count > 0 ? list[0] : IntPtr.Zero;
    }

    private static List<IntPtr> ReadWindowList(X11Display display, IntPtr property)
    {
        var result = new List<IntPtr>();
        if (X11Native.XGetWindowProperty(display.Handle, display.Root, property, 0, 1024, false,
                (IntPtr)X11Native.XA_WINDOW, out _, out var format, out var nItems, out _, out var prop) != X11Native.Success
            || prop == IntPtr.Zero || nItems == 0)
        {
            if (prop != IntPtr.Zero)
            {
                X11Native.XFree(prop);
            }

            return result;
        }

        try
        {
            if (format != 32)
            {
                return result;
            }

            for (ulong i = 0; i < nItems; i++)
            {
                result.Add((IntPtr)(long)(ulong)Marshal.ReadIntPtr(prop, (int)i * IntPtr.Size));
            }
        }
        finally
        {
            X11Native.XFree(prop);
        }

        return result;
    }

    private WindowInfo? DescribeWindow(X11Display display, IntPtr window, bool focused)
    {
        if (window == IntPtr.Zero || window == display.Root)
        {
            return null;
        }

        if (X11Native.XGetWindowAttributes(display.Handle, window, out var attrs) == 0
            || attrs.map_state != X11Native.IsViewable
            || attrs.override_redirect != 0)
        {
            return null;
        }

        if (X11Native.XTranslateCoordinates(display.Handle, window, display.Root, 0, 0, out var rootX, out var rootY, out _) == 0)
        {
            return null;
        }

        var client = new LayoutRect(rootX, rootY, attrs.width, attrs.height);
        var screen = new LayoutRect(0, 0, display.Width, display.Height);
        LayoutRect bounds;
        if (IncludeDecorations && TryReadFrameExtents(display, window, out var left, out var right, out var top, out var bottom))
        {
            bounds = ExpandWithFrameExtents(client, left, right, top, bottom, screen);
        }
        else
        {
            var clipped = client.Intersect(screen);
            bounds = clipped.IsEmpty ? client : clipped;
        }

        var title = CaptureTitle.StripEditorWindowSuffix(
            ReadUtf8Property(display, window, "_NET_WM_NAME")
            ?? ReadLatin1Property(display, window, "WM_NAME")
            ?? string.Empty);
        var appId = ReadUtf8Property(display, window, "WM_CLASS");
        if (appId != null)
        {
            // WM_CLASS is "instance\0class\0"; prefer the class
            var parts = appId.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            appId = parts.Length > 1 ? parts[^1] : parts.FirstOrDefault();
        }

        int pid = (int)ReadCardinal(display, window, "_NET_WM_PID");
        return new WindowInfo(title, appId, bounds, pid, window.ToString("X"), focused);
    }

    /// <summary>
    /// Expand client geometry by WM frame extents and clip to the screen (Windows-like framed capture).
    /// </summary>
    internal static LayoutRect ExpandWithFrameExtents(LayoutRect client, int left, int right, int top, int bottom, LayoutRect screen)
    {
        var expanded = new LayoutRect(
            client.X - left,
            client.Y - top,
            Math.Max(0, client.Width + left + right),
            Math.Max(0, client.Height + top + bottom));
        var clipped = expanded.Intersect(screen);
        return clipped.IsEmpty ? expanded : clipped;
    }

    private static bool TryReadFrameExtents(X11Display display, IntPtr window, out int left, out int right, out int top, out int bottom)
    {
        left = right = top = bottom = 0;
        var atom = X11Native.XInternAtom(display.Handle, "_NET_FRAME_EXTENTS", onlyIfExists: false);
        if (X11Native.XGetWindowProperty(display.Handle, window, atom, 0, 4, false, (IntPtr)X11Native.XA_CARDINAL,
                out _, out var format, out var nItems, out _, out var prop) != X11Native.Success
            || prop == IntPtr.Zero || nItems < 4 || format != 32)
        {
            if (prop != IntPtr.Zero)
            {
                X11Native.XFree(prop);
            }

            return false;
        }

        try
        {
            // format 32: array of longs (8 bytes on LP64)
            int stride = IntPtr.Size;
            left = (int)Marshal.ReadIntPtr(prop, 0 * stride).ToInt64();
            right = (int)Marshal.ReadIntPtr(prop, 1 * stride).ToInt64();
            top = (int)Marshal.ReadIntPtr(prop, 2 * stride).ToInt64();
            bottom = (int)Marshal.ReadIntPtr(prop, 3 * stride).ToInt64();
            return left >= 0 && right >= 0 && top >= 0 && bottom >= 0;
        }
        finally
        {
            X11Native.XFree(prop);
        }
    }

    private static string? ReadUtf8Property(X11Display display, IntPtr window, string name)
    {
        var atom = X11Native.XInternAtom(display.Handle, name, onlyIfExists: false);
        var utf8 = X11Native.XInternAtom(display.Handle, "UTF8_STRING", onlyIfExists: false);
        if (X11Native.XGetWindowProperty(display.Handle, window, atom, 0, 1024, false, utf8,
                out _, out var format, out var nItems, out _, out var prop) != X11Native.Success
            || prop == IntPtr.Zero || nItems == 0)
        {
            if (prop != IntPtr.Zero)
            {
                X11Native.XFree(prop);
            }

            return null;
        }

        try
        {
            if (format != 8)
            {
                return null;
            }

            return Marshal.PtrToStringUTF8(prop, (int)nItems);
        }
        finally
        {
            X11Native.XFree(prop);
        }
    }

    private static string? ReadLatin1Property(X11Display display, IntPtr window, string name)
    {
        var atom = X11Native.XInternAtom(display.Handle, name, onlyIfExists: false);
        if (X11Native.XGetWindowProperty(display.Handle, window, atom, 0, 1024, false, (IntPtr)X11Native.XA_STRING,
                out _, out var format, out var nItems, out _, out var prop) != X11Native.Success
            || prop == IntPtr.Zero || nItems == 0)
        {
            if (prop != IntPtr.Zero)
            {
                X11Native.XFree(prop);
            }

            return null;
        }

        try
        {
            if (format != 8)
            {
                return null;
            }

            return Marshal.PtrToStringAnsi(prop, (int)nItems);
        }
        finally
        {
            X11Native.XFree(prop);
        }
    }

    private static ulong ReadCardinal(X11Display display, IntPtr window, string name)
    {
        var atom = X11Native.XInternAtom(display.Handle, name, onlyIfExists: false);
        if (X11Native.XGetWindowProperty(display.Handle, window, atom, 0, 1, false, (IntPtr)X11Native.XA_CARDINAL,
                out _, out var format, out var nItems, out _, out var prop) != X11Native.Success
            || prop == IntPtr.Zero || nItems == 0)
        {
            if (prop != IntPtr.Zero)
            {
                X11Native.XFree(prop);
            }

            return 0;
        }

        try
        {
            return format == 32 ? (ulong)Marshal.ReadIntPtr(prop).ToInt64() : 0;
        }
        finally
        {
            X11Native.XFree(prop);
        }
    }
}
