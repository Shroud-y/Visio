using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Visio;

/// <summary>
/// Serves opened documents to the page at https://doc.visio/{id}/{name}.
/// Only files explicitly registered here are reachable, never their folders.
/// Adds the CORS header the page needs to fetch() them and supports byte
/// ranges so video/audio can seek.
/// </summary>
internal sealed class DocServer
{
    public const string Host = "doc.visio";
    private const string Origin = "https://app.visio";
    private const int MaxRangeChunk = 8 * 1024 * 1024;

    private readonly CoreWebView2 _core;
    private readonly Dictionary<string, string> _files = new();

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".jfif"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".avif"] = "image/avif", [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon", [".svg"] = "image/svg+xml",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".webm"] = "video/webm", [".ogv"] = "video/ogg", [".mov"] = "video/quicktime",
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".wav"] = "audio/wav", [".ogg"] = "audio/ogg", [".oga"] = "audio/ogg",
        [".opus"] = "audio/ogg", [".flac"] = "audio/flac", [".aac"] = "audio/aac",
    };

    public DocServer(CoreWebView2 core)
    {
        _core = core;
        core.AddWebResourceRequestedFilter($"https://{Host}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnRequest;
    }

    /// <summary>Registers a file and returns the URL the page should load it from.</summary>
    public string Register(string path)
    {
        var id = Guid.NewGuid().ToString("N");
        _files[id] = path;
        return $"https://{Host}/{id}/{Uri.EscapeDataString(Path.GetFileName(path))}";
    }

    private void OnRequest(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
        string? path = null;
        if (_files.TryGetValue(segments[0], out var registered) && segments.Length == 2)
        {
            path = Uri.UnescapeDataString(segments[1]) == Path.GetFileName(registered)
                ? registered
                : ResolveSiblingImage(registered, Uri.UnescapeDataString(segments[1]));
        }
        if (path == null || !File.Exists(path))
        {
            e.Response = _core.Environment.CreateWebResourceResponse(null, 404, "Not Found", Headers("text/plain"));
            return;
        }

        var type = Mime.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        FileStream file;
        try
        {
            // Share everything so files open in Word/Excel can still be viewed.
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException ex)
        {
            e.Response = _core.Environment.CreateWebResourceResponse(null, 423, "Locked", Headers("text/plain"));
            System.Diagnostics.Debug.WriteLine(ex);
            return;
        }

        var length = file.Length;
        if (TryParseRange(e.Request.Headers, length, out var from, out var to))
        {
            var count = (int)Math.Min(to - from + 1, MaxRangeChunk);
            var buffer = new byte[count];
            file.Position = from;
            file.ReadExactly(buffer, 0, count);
            file.Dispose();
            e.Response = _core.Environment.CreateWebResourceResponse(new MemoryStream(buffer), 206, "Partial Content",
                Headers(type) + $"\r\nContent-Range: bytes {from}-{from + count - 1}/{length}\r\nContent-Length: {count}");
            return;
        }

        e.Response = _core.Environment.CreateWebResourceResponse(file, 200, "OK", Headers(type) + $"\r\nContent-Length: {length}");
    }

    /// <summary>
    /// Markdown references images relative to itself. Allow those, but only
    /// images, and only inside the document's own folder tree.
    /// </summary>
    private static string? ResolveSiblingImage(string document, string relative)
    {
        var dir = Path.GetDirectoryName(document)!;
        var full = Path.GetFullPath(Path.Combine(dir, relative));
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
        return Mime.TryGetValue(Path.GetExtension(full), out var type) && type.StartsWith("image/") ? full : null;
    }

    private static string Headers(string contentType) =>
        $"Content-Type: {contentType}\r\nAccess-Control-Allow-Origin: {Origin}\r\nAccept-Ranges: bytes\r\nCache-Control: no-store";

    private static bool TryParseRange(CoreWebView2HttpRequestHeaders headers, long length, out long from, out long to)
    {
        from = to = 0;
        if (length == 0 || !headers.Contains("Range")) return false;
        var value = headers.GetHeader("Range");
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || value.Contains(',')) return false;
        var parts = value[6..].Split('-');
        if (parts.Length != 2) return false;

        if (parts[0].Length == 0)
        {
            // "bytes=-N": the last N bytes.
            if (!long.TryParse(parts[1], out var suffix) || suffix <= 0) return false;
            from = Math.Max(0, length - suffix);
            to = length - 1;
            return true;
        }
        if (!long.TryParse(parts[0], out from) || from >= length) return false;
        to = parts[1].Length == 0 || !long.TryParse(parts[1], out var end) ? length - 1 : Math.Min(end, length - 1);
        return to >= from;
    }
}
