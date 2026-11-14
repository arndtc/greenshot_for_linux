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

namespace Greenshot.Linux.Core.X11;

/// <summary>
/// Opens a connection to the X server. Callers should use a dedicated connection per thread (Xlib is not thread-safe
/// without careful locking).
/// </summary>
internal sealed class X11Display : IDisposable
{
    private static int _threadsInitialized;

    public IntPtr Handle { get; }

    public int Screen { get; }

    public IntPtr Root { get; }

    public int Width { get; }

    public int Height { get; }

    private X11Display(IntPtr handle)
    {
        Handle = handle;
        Screen = X11Native.XDefaultScreen(handle);
        Root = X11Native.XRootWindow(handle, Screen);
        Width = X11Native.XDisplayWidth(handle, Screen);
        Height = X11Native.XDisplayHeight(handle, Screen);
    }

    public static bool IsAvailable
    {
        get
        {
            if (!SessionEnvironment.IsX11 && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                return false;
            }

            EnsureThreads();
            var display = X11Native.XOpenDisplay(null);
            if (display == IntPtr.Zero)
            {
                return false;
            }

            X11Native.XCloseDisplay(display);
            return true;
        }
    }

    public static X11Display? TryOpen()
    {
        EnsureThreads();
        var handle = X11Native.XOpenDisplay(null);
        return handle == IntPtr.Zero ? null : new X11Display(handle);
    }

    private static void EnsureThreads()
    {
        if (Interlocked.Exchange(ref _threadsInitialized, 1) == 0)
        {
            X11Native.XInitThreads();
        }
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            X11Native.XCloseDisplay(Handle);
        }
    }
}
