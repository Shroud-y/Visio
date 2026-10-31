Lightweight read-only viewer for PDF, Word, Excel, PowerPoint, RTF, Markdown, text and images on Windows.

## Download

| File | Size | For |
|---|---|---|
| `Visio-*-win-x64.zip` | ~60 MB | **Most users.** Unzip and run `Visio.exe`, nothing else to install. |
| `Visio-*-win-x64-lite.zip` | ~2 MB | If you already have the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). |

Requires Windows 10 or 11 (x64) with the WebView2 Runtime, which Windows 11 already includes.

## Install

1. Unzip anywhere, e.g. `C:\Users\<you>\Apps\Visio`.
2. Run `Visio.exe`. On first launch it offers to become your default viewer: click **Set as default**, then
   **Set default** on the Settings page that opens (current user only, no admin rights).
3. Open a file with **Ctrl+O**, drag it into the window, or double-click it in Explorer.

To remove the file associations later: `powershell -ExecutionPolicy Bypass -File register.ps1 -Uninstall`.

**"Windows protected your PC"**: the app isn't code-signed, so SmartScreen may warn on first launch.
Click **More info > Run anyway**.

Legacy `.doc`, `.ppt` and `.odt` files open if [LibreOffice](https://www.libreoffice.org) is installed (they are converted to PDF).
