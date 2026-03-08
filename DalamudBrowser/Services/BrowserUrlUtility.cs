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

    public static bool IsNavigableScheme(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase);
    }
}
