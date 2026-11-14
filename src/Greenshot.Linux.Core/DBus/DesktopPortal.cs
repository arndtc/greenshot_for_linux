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

using Tmds.DBus.Protocol;

namespace Greenshot.Linux.Core.DBus;

/// <summary>
/// Writes the arguments of a portal call; handleToken goes into the options as handle_token
/// </summary>
public delegate void ArgumentWriter(ref MessageWriter writer, string handleToken);

/// <summary>
/// The answer of a portal request, see org.freedesktop.portal.Request
/// </summary>
public sealed record PortalResponse(uint Code, Dictionary<string, VariantValue> Results)
{
    public bool IsSuccess => Code == 0;

    public bool IsCancelled => Code == 1;
}

/// <summary>
/// Calls to the xdg-desktop-portal (https://flatpak.github.io/xdg-desktop-portal/docs/) on the session bus.
/// Most portal methods return a request handle at once and send the result later with the Response signal of that request,
/// <see cref="RequestAsync"/> hides this.
/// </summary>
public sealed class DesktopPortal : IDisposable
{
    public const string BusName = "org.freedesktop.portal.Desktop";
    public const string ObjectPath = "/org/freedesktop/portal/desktop";
    private const string RequestInterface = "org.freedesktop.portal.Request";

    private DesktopPortal(DBusConnection connection)
    {
        Connection = connection;
    }

    public DBusConnection Connection { get; }

    public static async Task<DesktopPortal> ConnectAsync()
    {
        var address = DBusAddress.Session ?? throw new InvalidOperationException("There is no D-Bus session bus (DBUS_SESSION_BUS_ADDRESS)");
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        return new DesktopPortal(connection);
    }

    /// <summary>
    /// Associate this D-Bus connection with <paramref name="appId"/> via org.freedesktop.host.portal.Registry
    /// (xdg-desktop-portal ≥ 1.20). Host apps must do this before any other portal call; from 1.21 GlobalShortcuts
    /// rejects CreateSession with NotAllowed "An app id is required" otherwise. Sandboxed apps (Flatpak/Snap) already
    /// have an id and skip. Missing Registry on older portals is ignored.
    /// When Register fails with "App info not found", installs a user desktop file whose Exec= is the running process
    /// and retries once (needed for bare <c>dotnet greenshot.dll</c> without packaging/install.sh).
    /// </summary>
    public async Task RegisterHostAppAsync(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || IsSandboxed())
        {
            return;
        }

        try
        {
            await CallRegisterAsync(appId).ConfigureAwait(false);
            Log.Info($"Registered host app id '{appId}' with the desktop portal");
        }
        catch (DBusExceptionBase ex) when (IsMissingRegistry(ex))
        {
            Log.Debug($"Host portal Registry is not available: {ex.Message}");
        }
        catch (DBusExceptionBase ex) when (IsAppInfoNotFound(ex))
        {
            var desktopPath = await HostAppDesktopFile.EnsureForRunningProcessAsync().ConfigureAwait(false);
            if (desktopPath == null)
            {
                Log.Warn($"Registering host app id '{appId}' failed (app info not found) and no resolvable Exec could be built. {HostAppDesktopFile.InstallHint}");
                return;
            }

            Log.Info($"Wrote {desktopPath} with Exec for this process so the host portal can find '{appId}', retrying Register");
            // One immediate retry, then one more after a short wait (desktop-file cache)
            if (await TryRegisterAfterDesktopInstallAsync(appId).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(700).ConfigureAwait(false);
            if (await TryRegisterAfterDesktopInstallAsync(appId).ConfigureAwait(false))
            {
                return;
            }

            Log.Warn($"Registering host app id '{appId}' still failed after writing {desktopPath}. {HostAppDesktopFile.InstallHint}");
        }
        catch (DBusExceptionBase ex)
        {
            Log.Warn($"Registering host app id '{appId}' with the desktop portal failed: {ex.Message}", ex);
        }
    }

    private async Task CallRegisterAsync(string appId)
    {
        // MessageWriter is a ref struct: same pattern as CreateCall (no using var — WriteOptions needs ref)
        var writer = Connection.GetMessageWriter();
        MessageBuffer message;
        try
        {
            writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: "org.freedesktop.host.portal.Registry",
                member: "Register", signature: "sa{sv}");
            writer.WriteString(appId);
            writer.WriteOptions([]);
            message = writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }

        await Connection.CallMethodAsync(message).ConfigureAwait(false);
    }

    private async Task<bool> TryRegisterAfterDesktopInstallAsync(string appId)
    {
        try
        {
            await CallRegisterAsync(appId).ConfigureAwait(false);
            Log.Info($"Registered host app id '{appId}' with the desktop portal");
            return true;
        }
        catch (DBusExceptionBase ex) when (IsMissingRegistry(ex))
        {
            Log.Debug($"Host portal Registry is not available: {ex.Message}");
            return true; // nothing more to do
        }
        catch (DBusExceptionBase)
        {
            return false;
        }
    }

    private static bool IsAppInfoNotFound(DBusExceptionBase ex) =>
        ex.Message.Contains("App info not found", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Flatpak and Snap set the portal app id; Registry.Register is for unsandboxed host apps only.
    /// </summary>
    private static bool IsSandboxed() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SNAP_NAME"))
        || File.Exists("/.flatpak-info");

    private static bool IsMissingRegistry(DBusExceptionBase ex)
    {
        // Older portals (< 1.20) have no Registry; treat that as optional, not a hard failure.
        var message = ex.Message;
        return message.Contains("UnknownMethod", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UnknownInterface", StringComparison.OrdinalIgnoreCase)
            || message.Contains("UnknownObject", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The version of a portal interface, null when the portal doesn't offer it
    /// </summary>
    public async Task<uint?> GetVersionAsync(string portalInterface)
    {
        MessageBuffer message;
        using (var writer = Connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: "org.freedesktop.DBus.Properties",
                member: "Get", signature: "ss");
            writer.WriteString(portalInterface);
            writer.WriteString("version");
            message = writer.CreateMessage();
        }

        try
        {
            return await Connection.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null)
                .ConfigureAwait(false);
        }
        catch (DBusExceptionBase ex)
        {
            Log.Debug($"{portalInterface} is not available: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Does a service own the name on the session bus? E.g. org.kde.StatusNotifierWatcher for a tray,
    /// org.freedesktop.Notifications for notifications
    /// </summary>
    public async Task<bool> NameHasOwnerAsync(string name)
    {
        MessageBuffer message;
        using (var writer = Connection.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus", @interface: "org.freedesktop.DBus",
                member: "NameHasOwner", signature: "s");
            writer.WriteString(name);
            message = writer.CreateMessage();
        }

        try
        {
            return await Connection.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadBool(), null).ConfigureAwait(false);
        }
        catch (DBusExceptionBase ex)
        {
            Log.Debug($"NameHasOwner {name} failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The handle_token for the options, unique for this connection
    /// </summary>
    public static string NewToken() => "greenshot" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Call a portal method which returns a request handle and wait for its Response.
    /// The first argument after the header has to be written by <paramref name="writeArguments"/>,
    /// which gets the handle_token to put in the options.
    /// </summary>
    public async Task<PortalResponse> RequestAsync(string portalInterface, string member, string signature,
        ArgumentWriter writeArguments, CancellationToken cancellationToken = default)
    {
        var token = NewToken();
        var expectedPath = RequestPath(token);
        var response = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Subscribe before calling, the response can come before the call returns
        using var subscription = await SubscribeResponseAsync(expectedPath, response).ConfigureAwait(false);

        var message = CreateCall(portalInterface, member, signature, writeArguments, token);
        var handle = await Connection.CallMethodAsync(message, static (m, _) => m.GetBodyReader().ReadObjectPathAsString(), null)
            .ConfigureAwait(false);

        // Portals before version 0.9 didn't use the handle_token for the path
        using var fallbackSubscription = handle != expectedPath
            ? await SubscribeResponseAsync(handle, response).ConfigureAwait(false)
            : null;

        await using var registration = cancellationToken.Register(() =>
        {
            response.TrySetCanceled(cancellationToken);
            _ = CloseRequestAsync(handle);
        });
        return await response.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// MessageWriter is a ref struct: it has to be passed by reference, a copy writes the bytes
    /// but the original doesn't know about them and creates a message with a broken body
    /// </summary>
    private MessageBuffer CreateCall(string portalInterface, string member, string signature, ArgumentWriter writeArguments, string token)
    {
        var writer = Connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(destination: BusName, path: ObjectPath, @interface: portalInterface, member: member, signature: signature);
            writeArguments(ref writer, token);
            return writer.CreateMessage();
        }
        finally
        {
            writer.Dispose();
        }
    }

    private string RequestPath(string token)
    {
        var sender = (Connection.UniqueName ?? string.Empty).TrimStart(':').Replace('.', '_');
        return $"{ObjectPath}/request/{sender}/{token}";
    }

    private Task<IDisposable> SubscribeResponseAsync(string path, TaskCompletionSource<PortalResponse> response)
    {
        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Interface = RequestInterface,
            Member = "Response",
            Path = path,
        };
        return Connection.WatchAsync(rule,
            static (m, _) =>
            {
                var reader = m.GetBodyReader();
                var code = reader.ReadUInt32();
                var results = reader.ReadDictionaryOfStringToVariantValue();
                return new PortalResponse(code, results);
            },
            value => response.TrySetResult(value),
            ex => response.TrySetException(ex));
    }

    private async Task CloseRequestAsync(string handle)
    {
        try
        {
            MessageBuffer message;
            using (var writer = Connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: BusName, path: handle, @interface: RequestInterface, member: "Close");
                message = writer.CreateMessage();
            }

            await Connection.CallMethodAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug($"Closing the portal request {handle} failed: {ex.Message}");
        }
    }

    public void Dispose() => Connection.Dispose();
}
