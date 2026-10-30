using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Visio;

/// <summary>
/// Converts formats the page can't render (legacy binary Office, OpenDocument
/// text/slides, Visio drawings) to PDF with a locally installed LibreOffice.
/// </summary>
internal static class Converter
{
    private static readonly HashSet<string> Convertible = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".dot", ".wps", ".wpd", ".odt", ".ott", ".fodt", ".sxw",
        ".ppt", ".pps", ".pot", ".odp", ".otp", ".fodp", ".sxi",
        ".vsd", ".vsdx", ".odg", ".pub",
    };

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Visio", "converted");

    public static bool Needs(string path) => Convertible.Contains(Path.GetExtension(path));

    public static string? FindLibreOffice()
    {
        var candidates = new List<string>();
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            using var key = root.OpenSubKey(@"SOFTWARE\LibreOffice\UNO\InstallPath");
            if (key?.GetValue(null) is string dir) candidates.Add(Path.Combine(dir, "soffice.exe"));
        }
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(pf), "LibreOffice", "program", "soffice.exe"));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Returns the path of a PDF rendering of <paramref name="path"/>, converting if not cached.</summary>
    public static async Task<string> ToPdfAsync(string path, string soffice)
    {
        Directory.CreateDirectory(CacheDir);
        var info = new FileInfo(path);
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")));
        var cached = Path.Combine(CacheDir, key + ".pdf");
        if (File.Exists(cached)) return cached;

        // Convert a copy with a plain name: soffice names its output after the input,
        // and a separate profile lets this run alongside an open LibreOffice window.
        var work = Path.Combine(CacheDir, key);
        Directory.CreateDirectory(work);
        try
        {
            var input = Path.Combine(work, "input" + info.Extension.ToLowerInvariant());
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var dst = File.Create(input))
            {
                await src.CopyToAsync(dst);
            }

            var profile = new Uri(Path.Combine(CacheDir, "lo-profile")).AbsoluteUri;
            var psi = new ProcessStartInfo(soffice)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[] { $"-env:UserInstallation={profile}", "--headless", "--norestore", "--convert-to", "pdf", "--outdir", work, input })
            {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Не вдалося запустити LibreOffice");
            var stderr = proc.StandardError.ReadToEndAsync();
            _ = proc.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                proc.Kill(entireProcessTree: true);
                throw new TimeoutException("LibreOffice не встиг сконвертувати файл за 2 хвилини");
            }

            var output = Path.Combine(work, "input.pdf");
            if (!File.Exists(output))
            {
                throw new InvalidOperationException("LibreOffice не зміг сконвертувати файл. " + (await stderr).Trim());
            }
            File.Move(output, cached, overwrite: true);
            return cached;
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }
}
