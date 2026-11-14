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

namespace Greenshot.Linux.Core.Hotkeys;

/// <summary>
/// One X11 key binding: Greenshot id and the key+modifiers to grab on the root window.
/// </summary>
public sealed record X11HotkeyBinding(string Id, string Hotkey, byte Keycode, uint Modifiers);

/// <summary>
/// Global hotkeys on X11 via <c>XGrabKey</c> on the root window. Registers each binding with NumLock and CapsLock
/// variants so the grab still matches when those locks are on. Used when the GlobalShortcuts portal is missing.
/// </summary>
public sealed class X11GlobalHotkeys : IDisposable
{
    private static int _lastXError;

    private readonly object _lock = new();
    private readonly Dictionary<(byte Keycode, uint Modifiers), string> _bindings = new();
    private X11Display? _display;
    private Thread? _thread;
    private volatile bool _running;
    private X11Native.XErrorHandler? _errorHandler;

    /// <summary>
    /// Called with the shortcut id when a grabbed key is pressed (not on the UI thread).
    /// </summary>
    public event Action<string>? Activated;

    public bool IsActive => _running && _display != null;

    public IReadOnlyCollection<string> BoundIds
    {
        get
        {
            lock (_lock)
            {
                return _bindings.Values.Distinct(StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <summary>
    /// Parse a Greenshot hotkey like "Ctrl + PrintScreen" into X11 keycode + modifier mask.
    /// </summary>
    public static bool TryParseHotkey(IntPtr display, string? hotkey, out byte keycode, out uint modifiers, out string? error)
    {
        keycode = 0;
        modifiers = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(hotkey) || hotkey.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            error = "No hotkey set";
            return false;
        }

        string? keyName = null;
        foreach (var part in hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= X11Native.ControlMask;
                    break;
                case "alt":
                    modifiers |= X11Native.Mod1Mask;
                    break;
                case "shift":
                    modifiers |= X11Native.ShiftMask;
                    break;
                case "win" or "windows" or "super" or "logo" or "meta":
                    modifiers |= 1u << 6; // Mod4Mask
                    break;
                case "printscreen" or "print" or "prtsc":
                    keyName = "Print";
                    break;
                default:
                    keyName = part.Length == 1 ? part.ToUpperInvariant() : part;
                    break;
            }
        }

        if (keyName == null)
        {
            error = $"No key in hotkey '{hotkey}'";
            return false;
        }

        var keysym = X11Native.XStringToKeysym(keyName);
        if (keysym == 0)
        {
            error = $"Unknown key '{keyName}' in hotkey '{hotkey}'";
            return false;
        }

        keycode = X11Native.XKeysymToKeycode(display, keysym);
        if (keycode == 0)
        {
            error = $"No keycode for '{keyName}'";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Open the display, grab the bindings (with lock-key variants), and start the event loop.
    /// Returns the ids that were successfully grabbed.
    /// </summary>
    public IReadOnlyList<string> Bind(IReadOnlyList<(string Id, string Hotkey)> hotkeys)
    {
        Unbind();
        var display = X11Display.TryOpen();
        if (display == null)
        {
            Log.Warn("X11 global hotkeys: could not open the display");
            return [];
        }

        _display = display;
        _errorHandler = OnXError;
        X11Native.XSetErrorHandler(_errorHandler);

        var bound = new List<string>();
        foreach (var (id, hotkey) in hotkeys)
        {
            if (!TryParseHotkey(display.Handle, hotkey, out var keycode, out var modifiers, out var error))
            {
                Log.Warn($"X11 hotkey '{id}' skipped: {error}");
                continue;
            }

            if (GrabWithLockVariants(display, keycode, modifiers, id))
            {
                bound.Add(id);
                Log.Info($"X11 hotkey grabbed: {id} = {hotkey} (keycode {keycode}, mods 0x{modifiers:X})");
            }
            else
            {
                Log.Warn(
                    $"X11 hotkey grab failed for {id} = {hotkey} (keycode {keycode}, mods 0x{modifiers:X}). " +
                    "Another client already owns this key (often xfce4-screenshooter on XFCE). " +
                    "Clear or reassign the conflicting binding, or set XFCE shortcuts to greenshot capture commands.");
            }
        }

        X11Native.XSelectInput(display.Handle, display.Root, X11Native.KeyPressMask);
        X11Native.XFlush(display.Handle);

        if (bound.Count == 0)
        {
            CleanupDisplay();
            return bound;
        }

        _running = true;
        _thread = new Thread(EventLoop) { IsBackground = true, Name = "Greenshot-X11-Hotkeys" };
        _thread.Start();
        return bound;
    }

    private bool GrabWithLockVariants(X11Display display, byte keycode, uint modifiers, string id)
    {
        uint[] variants =
        [
            modifiers,
            modifiers | X11Native.Mod2Mask,
            modifiers | X11Native.LockMask,
            modifiers | X11Native.Mod2Mask | X11Native.LockMask,
        ];

        bool any = false;
        foreach (var variant in variants.Distinct())
        {
            Interlocked.Exchange(ref _lastXError, 0);
            X11Native.XGrabKey(display.Handle, keycode, variant, display.Root, false, X11Native.GrabModeAsync, X11Native.GrabModeAsync);
            X11Native.XSync(display.Handle, false);
            if (Interlocked.CompareExchange(ref _lastXError, 0, 0) != 0)
            {
                X11Native.XUngrabKey(display.Handle, keycode, variant, display.Root);
                continue;
            }

            lock (_lock)
            {
                _bindings[(keycode, variant)] = id;
            }

            any = true;
        }

        return any;
    }

    private void EventLoop()
    {
        var display = _display;
        if (display == null)
        {
            return;
        }

        while (_running)
        {
            try
            {
                // Poll so Dispose can stop the loop without hanging forever in XNextEvent
                while (_running && X11Native.XPending(display.Handle) == 0)
                {
                    Thread.Sleep(50);
                }

                if (!_running)
                {
                    break;
                }

                X11Native.XNextEvent(display.Handle, out var ev);
                if (ev.type != X11Native.KeyPress)
                {
                    continue;
                }

                // Match the modifier bits we grabbed (Shift/Ctrl/Alt/Super + NumLock/CapsLock variants)
                const uint modifierMask = X11Native.ShiftMask | X11Native.LockMask | X11Native.ControlMask
                    | X11Native.Mod1Mask | X11Native.Mod2Mask | (1u << 6);
                uint state = ev.xkey.state & modifierMask;
                string? id;
                lock (_lock)
                {
                    _bindings.TryGetValue(((byte)ev.xkey.keycode, state), out id);
                }

                if (id != null)
                {
                    Activated?.Invoke(id);
                }
            }
            catch (Exception ex) when (_running)
            {
                Log.Warn("X11 hotkey event loop error", ex);
                Thread.Sleep(200);
            }
        }
    }

    private static int OnXError(IntPtr display, ref X11Native.XErrorEvent errorEvent)
    {
        Interlocked.Exchange(ref _lastXError, errorEvent.error_code);
        return 0;
    }

    public void Unbind()
    {
        _running = false;
        var thread = _thread;
        _thread = null;
        if (thread != null && thread.IsAlive)
        {
            // Wake XPending wait by closing the display after a short join attempt
            if (!thread.Join(TimeSpan.FromSeconds(1)))
            {
                Log.Debug("X11 hotkey thread did not stop in time");
            }
        }

        CleanupDisplay();
    }

    private void CleanupDisplay()
    {
        lock (_lock)
        {
            if (_display != null)
            {
                foreach (var (keycode, modifiers) in _bindings.Keys)
                {
                    X11Native.XUngrabKey(_display.Handle, keycode, modifiers, _display.Root);
                }

                _bindings.Clear();
                _display.Dispose();
                _display = null;
            }
        }
    }

    public void Dispose() => Unbind();
}
