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

namespace Greenshot.Linux.Core.X11;

/// <summary>
/// Subset of Xlib used for capture and global hotkeys on X11 sessions.
/// </summary>
internal static class X11Native
{
    internal const int Success = 0;
    internal const int ZPixmap = 2;
    internal const int KeyPress = 2;
    internal const int GrabModeAsync = 1;
    internal const int AnyModifier = 1 << 15;
    internal const uint Mod2Mask = 1 << 4;  // NumLock
    internal const uint LockMask = 1 << 1;  // CapsLock
    internal const uint ControlMask = 1 << 2;
    internal const uint Mod1Mask = 1 << 3;  // Alt
    internal const uint ShiftMask = 1 << 0;
    internal const long KeyPressMask = 1L << 0;
    internal const int IsUnmapped = 0;
    internal const int IsUnviewable = 1;
    internal const int IsViewable = 2;
    internal const int PropModeReplace = 0;
    internal const int XA_WINDOW = 33;
    internal const int XA_CARDINAL = 6;
    internal const int XA_STRING = 31;
    internal const int XA_ATOM = 4;

    [DllImport("libX11.so.6")]
    internal static extern int XInitThreads();

    [DllImport("libX11.so.6")]
    internal static extern IntPtr XOpenDisplay(string? display);

    [DllImport("libX11.so.6")]
    internal static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    internal static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    internal static extern IntPtr XRootWindow(IntPtr display, int screen);

    [DllImport("libX11.so.6")]
    internal static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    internal static extern int XDisplayWidth(IntPtr display, int screen);

    [DllImport("libX11.so.6")]
    internal static extern int XDisplayHeight(IntPtr display, int screen);

    [DllImport("libX11.so.6")]
    internal static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    internal static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6")]
    internal static extern IntPtr XGetImage(IntPtr display, IntPtr drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    [DllImport("libX11.so.6")]
    internal static extern int XDestroyImage(IntPtr image);

    [DllImport("libX11.so.6")]
    internal static extern int XGetWindowAttributes(IntPtr display, IntPtr window, out XWindowAttributes attributes);

    [DllImport("libX11.so.6")]
    internal static extern int XTranslateCoordinates(IntPtr display, IntPtr src, IntPtr dest, int srcX, int srcY,
        out int destX, out int destY, out IntPtr child);

    [DllImport("libX11.so.6")]
    internal static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent,
        out IntPtr children, out uint nChildren);

    [DllImport("libX11.so.6")]
    internal static extern int XFree(IntPtr data);

    [DllImport("libX11.so.6")]
    internal static extern ulong XStringToKeysym(string str);

    [DllImport("libX11.so.6")]
    internal static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport("libX11.so.6")]
    internal static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow,
        bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6")]
    internal static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport("libX11.so.6")]
    internal static extern int XSelectInput(IntPtr display, IntPtr window, long eventMask);

    [DllImport("libX11.so.6")]
    internal static extern int XNextEvent(IntPtr display, out XEvent xevent);

    [DllImport("libX11.so.6")]
    internal static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    internal static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    internal static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, long length,
        bool delete, IntPtr reqType, out IntPtr actualType, out int actualFormat, out ulong nItems, out ulong bytesAfter,
        out IntPtr prop);

    [DllImport("libX11.so.6")]
    internal static extern int XGetErrorHandler(IntPtr handler);

    [DllImport("libX11.so.6")]
    internal static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    [DllImport("libX11.so.6")]
    internal static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int XErrorHandler(IntPtr display, ref XErrorEvent errorEvent);

    [StructLayout(LayoutKind.Sequential)]
    internal struct XWindowAttributes
    {
        public int x;
        public int y;
        public int width;
        public int height;
        public int border_width;
        public int depth;
        public IntPtr visual;
        public IntPtr root;
        public int class_;
        public int bit_gravity;
        public int win_gravity;
        public int backing_store;
        public ulong backing_planes;
        public ulong backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed;
        public int map_state;
        public long all_event_masks;
        public long your_event_mask;
        public long do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XImage
    {
        public int width;
        public int height;
        public int xoffset;
        public int format;
        public IntPtr data;
        public int byte_order;
        public int bitmap_unit;
        public int bitmap_bit_order;
        public int bitmap_pad;
        public int depth;
        public int bytes_per_line;
        public int bits_per_pixel;
        public ulong red_mask;
        public ulong green_mask;
        public ulong blue_mask;
        public IntPtr obdata;
        // Function pointers follow; unused when we only read the fields above
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XKeyEvent
    {
        public int type;
        public ulong serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr root;
        public IntPtr subwindow;
        public ulong time;
        public int x;
        public int y;
        public int x_root;
        public int y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    /// <summary>
    /// Large enough for any XEvent; only <see cref="type"/> / <see cref="xkey"/> are read.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    internal struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public ulong resourceid;
        public ulong serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
    }
}
