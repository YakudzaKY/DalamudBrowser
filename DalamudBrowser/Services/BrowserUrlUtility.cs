using System;
using System.IO;

namespace DalamudBrowser.Services;

internal static class BrowserUrlUtility
{
    public static string Normalize(string? url)
    {
        if (!TryCreateAbsoluteUri(url, out var uri))
        {
            return url?.Trim() ?? string.Empty;
        }

        return uri.AbsoluteUri;
    }

    public static bool TryCreateAbsoluteUri(string? url, out Uri uri)
    {
        uri = null!;
        var trimmed = url?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var parsedUri))
        {
            uri = parsedUri;
            return true;
        }

        var queryIndex = trimmed.IndexOf('?');
        var pathPart = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
        var suffix = queryIndex >= 0 ? trimmed[queryIndex..] : string.Empty;
        if (!Path.IsPathFullyQualified(pathPart))
        {
            return false;
        }

        var fileUri = new Uri(pathPart, UriKind.Absolute).AbsoluteUri + suffix;
        if (!Uri.TryCreate(fileUri, UriKind.Absolute, out parsedUri))
        {
            return false;
        }

        uri = parsedUri;
        return true;
    }

    public static bool TryGetLocalFilePath(string? url, out string path)
    {
        path = string.Empty;
        if (!TryCreateAbsoluteUri(url, out var uri) || !uri.IsFile)
        {
            return false;
        }

        path = uri.LocalPath;
        return !string.IsNullOrWhiteSpace(path);
    }

    public static bool TryGetActOverlayWebSocket(string? url, out Uri webSocketUri)
    {
        webSocketUri = null!;
        if (!TryCreateAbsoluteUri(url, out var uri))
        {
            return false;
        }

        var value = TryGetQueryParameterValue(uri, "OVERLAY_WS");
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        if (!string.Equals(parsedUri.Scheme, "ws", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(parsedUri.Scheme, "wss", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        webSocketUri = parsedUri;
        return true;
    }

    public static bool IsLikelyActOverlay(string? url)
    {
        if (TryGetActOverlayWebSocket(url, out _))
        {
            return true;
        }

        if (!TryCreateAbsoluteUri(url, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        return path.Contains("/cactbot/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/overlayplugin/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/raidboss", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/jobs", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNavigableScheme(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetQueryParameterValue(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query) || query.Length <= 1)
        {
            return null;
        }

        var querySpan = query.AsSpan(1);
        while (querySpan.Length > 0)
        {
            int ampersandIndex = querySpan.IndexOf('&');
            ReadOnlySpan<char> segment = ampersandIndex >= 0 ? querySpan[..ampersandIndex] : querySpan;
            querySpan = ampersandIndex >= 0 ? querySpan[(ampersandIndex + 1)..] : ReadOnlySpan<char>.Empty;

            if (segment.Length == 0)
            {
                continue;
            }

            int separatorIndex = segment.IndexOf('=');
            ReadOnlySpan<char> rawName = separatorIndex >= 0 ? segment[..separatorIndex] : segment;

            bool nameMatch;
            if (rawName.IndexOfAny('+', '%') == -1)
            {
                nameMatch = rawName.Equals(key.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var decodedName = Uri.UnescapeDataString(rawName.ToString().Replace('+', ' '));
                nameMatch = string.Equals(decodedName, key, StringComparison.OrdinalIgnoreCase);
            }

            if (!nameMatch)
            {
                continue;
            }

            ReadOnlySpan<char> rawValue = separatorIndex >= 0 ? segment[(separatorIndex + 1)..] : ReadOnlySpan<char>.Empty;
            if (rawValue.Length == 0)
            {
                return string.Empty;
            }

            if (rawValue.IndexOfAny('+', '%') == -1)
            {
                return rawValue.ToString();
            }

            return Uri.UnescapeDataString(rawValue.ToString().Replace('+', ' '));
        }

        return null;
    }
}
