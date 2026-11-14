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
using Greenshot.Linux.Core.X11;
using SkiaSharp;

namespace Greenshot.Linux.Core.Capturing;

/// <summary>
/// Captures the root window with <c>XGetImage</c>. Used on X11 sessions (XFCE, etc.) when grim and the Screenshot
/// portal are missing. Wayland sessions keep grim / portal; this grabber is only chosen when the session is X11 or
/// <c>CaptureBackend=X11</c>.
/// </summary>
public sealed class X11ScreenGrabber : IScreenGrabber
{
    public string Name => "x11";

    public bool SupportsCursor => false;

    public static bool IsAvailable => X11Display.IsAvailable;

    public Task<SKBitmap> GrabAsync(ScreenLayout layout, bool includeCursor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Grab(layout), cancellationToken);
    }

    private static SKBitmap Grab(ScreenLayout layout)
    {
        using var display = X11Display.TryOpen()
            ?? throw new CaptureException("Could not open the X11 display for capture");

        var bounds = layout.Bounds;
        int x = Math.Max(0, bounds.X);
        int y = Math.Max(0, bounds.Y);
        int width = Math.Min(bounds.Width, display.Width - x);
        int height = Math.Min(bounds.Height, display.Height - y);
        if (width <= 0 || height <= 0)
        {
            // Fall back to the whole root when the toolkit layout is empty or off-screen
            x = 0;
            y = 0;
            width = display.Width;
            height = display.Height;
        }

        var imagePtr = X11Native.XGetImage(display.Handle, display.Root, x, y, (uint)width, (uint)height, ulong.MaxValue, X11Native.ZPixmap);
        if (imagePtr == IntPtr.Zero)
        {
            throw new CaptureException($"XGetImage failed for {width}x{height} at {x},{y}");
        }

        try
        {
            var ximage = Marshal.PtrToStructure<X11Native.XImage>(imagePtr);
            if (ximage.data == IntPtr.Zero || ximage.width <= 0 || ximage.height <= 0)
            {
                throw new CaptureException("XGetImage returned an empty image");
            }

            return ToBitmap(ximage);
        }
        finally
        {
            X11Native.XDestroyImage(imagePtr);
        }
    }

    private static SKBitmap ToBitmap(X11Native.XImage ximage)
    {
        int width = ximage.width;
        int height = ximage.height;
        int bpp = ximage.bits_per_pixel;
        int stride = ximage.bytes_per_line;
        if (bpp != 24 && bpp != 32)
        {
            throw new CaptureException($"Unsupported X11 image depth {ximage.depth} ({bpp} bits per pixel)");
        }

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var src = new byte[stride * height];
        Marshal.Copy(ximage.data, src, 0, src.Length);

        IntPtr destPtr = bitmap.GetPixels();
        int destStride = bitmap.RowBytes;
        var dest = new byte[destStride * height];

        bool littleEndian = BitConverter.IsLittleEndian;
        // X11 on little-endian hosts usually stores BGRX / BGRA in ZPixmap
        for (int row = 0; row < height; row++)
        {
            int srcRow = row * stride;
            int destRow = row * destStride;
            for (int col = 0; col < width; col++)
            {
                int s = srcRow + col * (bpp / 8);
                int d = destRow + col * 4;
                byte b, g, r;
                if (littleEndian)
                {
                    b = src[s];
                    g = src[s + 1];
                    r = src[s + 2];
                }
                else
                {
                    r = src[s];
                    g = src[s + 1];
                    b = src[s + 2];
                }

                dest[d] = b;
                dest[d + 1] = g;
                dest[d + 2] = r;
                dest[d + 3] = 255;
            }
        }

        Marshal.Copy(dest, 0, destPtr, dest.Length);
        return bitmap;
    }
}
