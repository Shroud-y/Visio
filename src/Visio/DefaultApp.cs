using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Visio;

/// <summary>
/// File-type registration for the current user (HKCU only, no admin rights).
/// Windows doesn't let apps make themselves the default; the best we can do is
/// register as a candidate and open the Settings page where the user confirms.
/// </summary>
internal static class DefaultApp
{
    private const string ProgId = "Visio.Document";
    private const string AppKey = @"Software\Visio";
    private const string CapabilitiesKey = AppKey + @"\Capabilities";
    private const string RegisteredName = "Visio";

    public static readonly string[] Extensions =
    [
        "pdf", "docx", "docm", "dotx", "dotm", "doc", "dot", "rtf", "odt", "ott", "wps",
        "xlsx", "xlsm", "xltx", "xlsb", "xls", "ods", "csv", "tsv",
        "pptx", "pptm", "ppsx", "potx", "ppt", "pps", "odp", "vsd", "vsdx",
        "md", "markdown", "txt", "log", "json", "xml", "yaml", "yml", "ini",
        "png", "jpg", "jpeg", "gif", "webp", "bmp", "svg",
    ];

    private static string ExePath => Environment.ProcessPath!;
    private static string OpenCommand => $"\"{ExePath}\" \"%1\"";

    /// <summary>Whether .docx files currently open with this Visio.exe.</summary>
    public static bool IsDefault()
    {
        var exe = AssocExecutable(".docx");
        return exe != null && string.Equals(Path.GetFullPath(exe), ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        return key?.GetValue(null) is string;
    }

    /// <summary>Points an existing registration at this exe, e.g. after the folder was moved.</summary>
    public static void RefreshIfMoved()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
        if (key?.GetValue(null) is string cmd && !string.Equals(cmd, OpenCommand, StringComparison.OrdinalIgnoreCase))
        {
            Register();
        }
    }

    public static void Register()
    {
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");

        // ProgID: what a file opens with.
        using (var prog = classes.CreateSubKey(ProgId))
        {
            prog.SetValue(null, "Document (Visio)");
            using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{ExePath}\",0");
            using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, OpenCommand);
        }

        // Application entry: the friendly name in "Open with".
        using (var app = classes.CreateSubKey(@"Applications\Visio.exe"))
        {
            app.SetValue("FriendlyAppName", "Visio");
            using (var cmd = app.CreateSubKey(@"shell\open\command")) cmd.SetValue(null, OpenCommand);
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var ext in Extensions) types.SetValue("." + ext, "");
        }

        foreach (var ext in Extensions)
        {
            using var owp = classes.CreateSubKey($@".{ext}\OpenWithProgids");
            owp.SetValue(ProgId, "");
        }

        // Capabilities + RegisteredApplications: what lists Visio in Settings > Default apps.
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", "Visio");
            caps.SetValue("ApplicationDescription", "Lightweight read-only viewer for documents, spreadsheets, presentations and images.");
            caps.SetValue("ApplicationIcon", $"\"{ExePath}\",0");
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var ext in Extensions) assoc.SetValue("." + ext, ProgId);
        }
        using (var reg = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
        {
            reg.SetValue(RegisteredName, CapabilitiesKey);
        }

        NotifyShell();
    }

    public static void Unregister()
    {
        using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true))
        {
            if (classes != null)
            {
                foreach (var ext in Extensions)
                {
                    using var owp = classes.OpenSubKey($@".{ext}\OpenWithProgids", writable: true);
                    owp?.DeleteValue(ProgId, throwOnMissingValue: false);
                }
                classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
                classes.DeleteSubKeyTree(@"Applications\Visio.exe", throwOnMissingSubKey: false);
            }
        }
        using (var reg = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true))
        {
            reg?.DeleteValue(RegisteredName, throwOnMissingValue: false);
        }
        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
        NotifyShell();
    }

    /// <summary>Registers and opens Settings on Visio's "Default apps" page.</summary>
    public static void RegisterAndOpenSettings()
    {
        Register();
        // registeredAppUser jumps straight to the app's page (Windows 11);
        // older builds ignore it and show the Default apps list.
        Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={RegisteredName}") { UseShellExecute = true });
    }

    private static string? AssocExecutable(string ext)
    {
        uint size = 1024;
        var sb = new StringBuilder((int)size);
        return AssocQueryString(0, AssocStrExecutable, ext, "open", sb, ref size) == 0 ? sb.ToString() : null;
    }

    private static void NotifyShell() => SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);

    private const int AssocStrExecutable = 2;
    private const int ShcneAssocChanged = 0x08000000;

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(int flags, int str, string assoc, string? extra, StringBuilder? outStr, ref uint outLen);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
}
