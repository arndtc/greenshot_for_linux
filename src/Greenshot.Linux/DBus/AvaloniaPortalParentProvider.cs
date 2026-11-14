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

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Greenshot.Linux.Core;
using Greenshot.Linux.Core.DBus;

namespace Greenshot.Linux.DBus;

/// <summary>
/// Shows a small topmost Avalonia window and activates it so GNOME will allow the Screenshot portal access dialog
/// when capture is started from the tray or a global hotkey (no focused Greenshot window otherwise).
/// </summary>
internal sealed class AvaloniaPortalParentProvider : IPortalParentProvider
{
    public Task<IPortalParentSession> BeginAsync(CancellationToken cancellationToken = default) =>
        Dispatcher.UIThread.CheckAccess()
            ? BeginOnUiAsync(cancellationToken)
            : Dispatcher.UIThread.InvokeAsync(() => BeginOnUiAsync(cancellationToken));

    private static async Task<IPortalParentSession> BeginOnUiAsync(CancellationToken cancellationToken)
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window
        {
            Title = "Greenshot",
            Width = 280,
            Height = 64,
            CanResize = false,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowDecorations = WindowDecorations.BorderOnly,
            Background = new SolidColorBrush(Color.FromRgb(0x2b, 0x2b, 0x2b)),
            Content = new TextBlock
            {
                Text = "Requesting screen capture…",
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(12),
            },
        };

        window.Opened += (_, _) => opened.TrySetResult();
        window.Show();
        window.Activate();

        await using (cancellationToken.Register(() => opened.TrySetCanceled(cancellationToken)))
        {
            await opened.Task.ConfigureAwait(true);
        }

        // Let the compositor treat this as the focused app before the portal call
        await Task.Delay(200, cancellationToken).ConfigureAwait(true);

        var parentWindow = ToPortalParentWindow(window);
        Log.Info($"Screenshot portal parent window '{parentWindow}'");
        return new Session(window, parentWindow);
    }

    private static string ToPortalParentWindow(TopLevel topLevel)
    {
        var handle = topLevel.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero)
        {
            Log.Debug("No platform handle yet for the portal parent window");
            return string.Empty;
        }

        var descriptor = handle.HandleDescriptor ?? string.Empty;
        // Avalonia on GNOME uses the X11 backend (XWayland). Native Wayland would need xdg-foreign.
        if (descriptor.Contains("X11", StringComparison.OrdinalIgnoreCase)
            || descriptor.Equals("XID", StringComparison.OrdinalIgnoreCase)
            || (OperatingSystem.IsLinux() && string.IsNullOrEmpty(descriptor)))
        {
            return $"x11:{handle.Handle.ToInt64():x}";
        }

        Log.Debug($"Unsupported portal parent handle descriptor '{descriptor}'");
        return string.Empty;
    }

    private sealed class Session(Window window, string parentWindow) : IPortalParentSession
    {
        public string ParentWindow { get; } = parentWindow;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        window.Close();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug($"Closing the portal parent window failed: {ex.Message}");
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Debug($"Closing the portal parent window failed: {ex.Message}");
            }
        }
    }
}
