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

using System.Collections.Concurrent;
using Tmds.DBus.Protocol;

namespace Greenshot.Linux.Core.DBus;

public enum NotificationSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Notifications with org.freedesktop.Notifications (mako, dunst, GNOME, KDE, ...), the toasts of the Windows version.
/// Clicking a notification runs its action, e.g. opening the saved file.
/// </summary>
public sealed class DesktopNotifications : IDisposable
{
    private const string BusName = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private const string NotificationsInterface = "org.freedesktop.Notifications";

    private readonly DBusConnection _connection;
    private readonly ConcurrentDictionary<uint, Action> _actions = new();
    private IDisposable? _subscription;
    private IDisposable? _closedSubscription;

    public DesktopNotifications(DBusConnection connection)
    {
        _connection = connection;
    }

    public async Task StartAsync()
    {
        _subscription = await _connection.WatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = NotificationsInterface, Member = "ActionInvoked", Path = ObjectPath },
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                return reader.ReadUInt32();
            },
            id =>
            {
                if (_actions.TryRemove(id, out var action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception actionException)
                    {
                        Log.Error("The notification action failed", actionException);
                    }
                }
            }).ConfigureAwait(false);

        // Forget actions of notifications which are gone
        _closedSubscription = await _connection.WatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = NotificationsInterface, Member = "NotificationClosed", Path = ObjectPath },
            static (m, _) => m.GetBodyReader().ReadUInt32(),
            id => _actions.TryRemove(id, out _)).ConfigureAwait(false);
    }

    /// <param name="imagePath">A picture to show in the notification, the thumbnail of the capture</param>
    /// <param name="onClick">Runs when the notification is clicked</param>
    public async Task ShowAsync(string summary, string body, NotificationSeverity severity = NotificationSeverity.Info,
        string? imagePath = null, Action? onClick = null)
    {
        try
        {
            var message = CreateNotify(summary, body, severity, imagePath, onClick != null);
            var id = await _connection.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadUInt32(), null).ConfigureAwait(false);
            if (onClick != null)
            {
                _actions[id] = onClick;
            }
        }
        catch (Exception ex) when (IsMissingNotificationsDaemon(ex))
        {
            // No org.freedesktop.Notifications: try the desktop portal (available with xdg-desktop-portal-gtk etc.)
            try
            {
                await ShowViaPortalAsync(summary, body, severity, imagePath).ConfigureAwait(false);
            }
            catch (Exception portalEx)
            {
                Log.Warn($"Could not show the notification \"{summary}\": {ex.Message}; portal fallback: {portalEx.Message}");
            }
        }
        catch (Exception ex)
        {
            // Without a notification daemon there are no notifications, that is not an error of the capture
            Log.Warn($"Could not show the notification \"{summary}\": {ex.Message}");
        }
    }

    private static bool IsMissingNotificationsDaemon(Exception ex) =>
        ex.Message.Contains("org.freedesktop.Notifications", StringComparison.Ordinal)
        && (ex.Message.Contains("ServiceUnknown", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("NameHasNoOwner", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("was not provided by any .service files", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// org.freedesktop.portal.Notification.AddNotification — no click actions (portal default-action needs a separate handler).
    /// </summary>
    private async Task ShowViaPortalAsync(string summary, string body, NotificationSeverity severity, string? imagePath)
    {
        var writer = _connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(destination: DesktopPortal.BusName, path: DesktopPortal.ObjectPath,
                @interface: "org.freedesktop.portal.Notification", member: "AddNotification", signature: "ssa{sv}");
            writer.WriteString(AppIdentity.Id);
            writer.WriteString("greenshot-" + Guid.NewGuid().ToString("N")[..12]);
            // Portal icon is a nested variant; skip file thumbnails here (title/body still show).
            _ = imagePath;
            var notification = new Dictionary<string, object>
            {
                ["title"] = summary,
                ["body"] = body,
                ["priority"] = severity == NotificationSeverity.Error ? "urgent" : severity == NotificationSeverity.Warning ? "high" : "normal",
            };

            writer.WriteOptions(notification);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        await _connection.CallMethodAsync(message).ConfigureAwait(false);
        Log.Debug($"Showed notification \"{summary}\" via org.freedesktop.portal.Notification");
    }

    private MessageBuffer CreateNotify(string summary, string body, NotificationSeverity severity, string? imagePath, bool clickable)
    {
        // A local, not a using variable: WriteOptions needs the writer by reference
        var writer = _connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: NotificationsInterface,
                member: "Notify", signature: "susssasa{sv}i");
            writer.WriteString("Greenshot");
            writer.WriteUInt32(0);
            writer.WriteString(severity == NotificationSeverity.Info ? AppIdentity.Id : "dialog-" + severity.ToString().ToLowerInvariant());
            writer.WriteString(summary);
            writer.WriteString(body);
            writer.WriteArray(clickable ? new[] { "default", summary } : Array.Empty<string>());
            var hints = new Dictionary<string, object>
            {
                ["desktop-entry"] = AppIdentity.Id,
                ["urgency"] = severity == NotificationSeverity.Error ? (byte)2 : (byte)1,
            };
            if (imagePath != null)
            {
                hints["image-path"] = imagePath;
            }

            writer.WriteOptions(hints);
            writer.WriteInt32(-1);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _closedSubscription?.Dispose();
        _closedSubscription = null;
    }
}
