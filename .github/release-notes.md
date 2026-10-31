Lightweight read-only viewer for PDF, Word, Excel, PowerPoint, RTF, Markdown, text and images on Windows.

## Download

| File | Size | For |
|---|---|---|
| `Visio-*-win-x64.zip` | ~60 MB | **Most users.** Unzip and run `Visio.exe`, nothing else to install. |
| `Visio-*-win-x64-lite.zip` | ~2 MB | If you already have the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). |

Requires Windows 10 or 11 (x64) with the WebView2 Runtime, which Windows 11 already includes.

## Install

1. Unzip anywhere, e.g. `C:\Users\<you>\Apps\Visio`.
2. Run `Visio.exe`, then open a file with **Ctrl+O** or drag it into the window.
3. Optional: add Visio to Explorer's **Open with** menu (current user only, no admin rights):
   ```
   powershell -ExecutionPolicy Bypass -File register.ps1
   ```
   To make it the default for a type: right-click a file > Open with > Choose another app > Visio > Always.
   Undo with `register.ps1 -Uninstall`.

**"Windows protected your PC"**: the app isn't code-signed, so SmartScreen may warn on first launch.
Click **More info > Run anyway**.

Legacy `.doc`, `.ppt` and `.odt` files open if [LibreOffice](https://www.libreoffice.org) is installed (they are converted to PDF).
