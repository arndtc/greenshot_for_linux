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

namespace Greenshot.Linux.Core.Capturing;

/// <summary>
/// Keeps capture titles and editor window titles from accumulating
/// <c> - Greenshot image editor</c> when the editor itself is captured.
/// </summary>
public static class CaptureTitle
{
    /// <summary>
    /// Matches <c>Texts.Editor("title")</c> in greenshot.en-US.ini (and translations that keep this English form
    /// in the window title from Avalonia). Strip repeatedly so doubled suffixes collapse.
    /// </summary>
    public const string EditorWindowTitle = "Greenshot image editor";

    public static string StripEditorWindowSuffix(string? title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return title ?? string.Empty;
        }

        var text = title.TrimStart('*').TrimStart();
        var suffix = " - " + EditorWindowTitle;
        while (text.EndsWith(suffix, StringComparison.Ordinal))
        {
            text = text[..^suffix.Length];
        }

        return text;
    }
}
