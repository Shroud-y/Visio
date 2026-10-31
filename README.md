# Visio — lightweight docs reader

A small, read-only document viewer for Windows. Open a file, read it, close it — no editing, no Office required.

## Download

Get the latest zip from **[Releases](https://github.com/Shroud-y/Visio/releases/latest)**:

- `Visio-<version>-win-x64.zip` — for most users; unzip and run `Visio.exe`.
- `Visio-<version>-win-x64-lite.zip` — smaller, needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

Requires Windows 10/11 x64 with the WebView2 Runtime (built into Windows 11). The app isn't code-signed, so SmartScreen
may warn on first launch: **More info > Run anyway**.

On first launch Visio offers to become your default viewer (also in the **⋯** menu). Windows doesn't let apps set
themselves as default, so Visio registers itself (current user only, no admin rights) and opens
**Settings > Default apps > Visio**, where one click on **Set default** finishes the job. If you move the folder, Visio
updates its registration on the next launch. For scripted setups, or to remove the registration:

```bash
powershell -ExecutionPolicy Bypass -File register.ps1
```

```bash
powershell -ExecutionPolicy Bypass -File register.ps1 -Uninstall
```

## Features

- **Auto-reload**: when the open file changes on disk (re-saved, replaced), Visio reloads it and keeps zoom and scroll.
- **Remembers the window** size, position and maximized state.
- **Update notice**: once a day Visio asks GitHub for the latest release and shows a banner if there is a newer one.
  Nothing is downloaded or installed automatically. Check by hand from the **⋯** menu.

Settings live in `%LOCALAPPDATA%\Visio\settings.json` (set `"CheckForUpdates": false` to turn off the update check).

## Supported formats

| Format | Rendered by |
|---|---|
| PDF | Edge's built-in PDF viewer (zoom, search, print) |
| DOCX, DOCM, DOTX | pages with styles, tables, images, headers/footers ([docx-preview](https://github.com/VolodymyrBaydalka/docxjs)) |
| XLSX, XLSM, XLSB, XLS, ODS, CSV, TSV | grid with sheet tabs ([SheetJS](https://sheetjs.com)) |
| PPTX, PPSX | slides in a list ([pptx-preview](https://www.npmjs.com/package/pptx-preview)) |
| RTF | page ([rtf.js](https://github.com/tbluemel/rtf.js)) |
| Markdown | [markdown-it](https://github.com/markdown-it/markdown-it) |
| TXT, JSON, XML, source code | highlighted text ([highlight.js](https://highlightjs.org)); UTF-8, UTF-16 and Windows-1251 |
| PNG, JPG, GIF, WEBP, SVG, BMP; MP4, WEBM, MP3, WAV | natively |
| DOC, PPT, ODT, ODP, VSD(X) | converted to PDF with [LibreOffice](https://www.libreoffice.org), if installed |

Keys: `Ctrl+O` open · `Ctrl+F` / `F3` find · `Ctrl+wheel`, `Ctrl+=`, `Ctrl+-`, `Ctrl+0` zoom · `F11` full screen ·
`Ctrl+P` print. You can also drag a file into the window.

## Building

```bash
dotnet publish src/Visio -c Release -o dist/Visio
```

Release zips (same as CI):

```bash
powershell -ExecutionPolicy Bypass -File tools/package.ps1 -Version 0.1.0
```

## Releasing

Push a version tag; the [Release workflow](.github/workflows/release.yml) builds both zips and publishes a GitHub
release with them:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

Tags with a suffix (`v0.2.0-beta.1`) become pre-releases. Running the workflow manually from the Actions tab only builds
the zips as artifacts. The release text comes from [.github/release-notes.md](.github/release-notes.md).

## Layout

- `viewer/` — the HTML/JS renderer. It also works in a plain browser: run `python -m http.server` in the repo root and
  open `/viewer/index.html?src=../samples/document.docx`.
- `src/Visio/` — WPF host with WebView2: window, open dialog, serving files to the page (`DocServer`), LibreOffice
  conversion (`Converter`).
- `tools/` — `package.ps1` (release zips), `register.ps1` (scripted file-type registration), `make_samples.py` (test documents).

## License

[MIT](LICENSE). Bundled third-party libraries are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
