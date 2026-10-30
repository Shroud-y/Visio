# Visio — lightweight docs reader

Легкий переглядач документів для Windows. Лише перегляд, без редагування.

| Формат | Як показується |
|---|---|
| PDF | вбудований PDF-переглядач Edge (масштаб, пошук, друк) |
| DOCX, DOCM, DOTX | сторінки зі стилями, таблицями, картинками, колонтитулами ([docx-preview](https://github.com/VolodymyrBaydalka/docxjs)) |
| XLSX, XLSM, XLSB, XLS, ODS, CSV, TSV | таблиця з вкладками аркушів ([SheetJS](https://sheetjs.com)) |
| PPTX, PPSX | слайди списком ([pptx-preview](https://www.npmjs.com/package/pptx-preview)) |
| RTF | сторінка ([rtf.js](https://github.com/tbluemel/rtf.js)) |
| MD | Markdown ([markdown-it](https://github.com/markdown-it/markdown-it)) |
| TXT, JSON, XML, код | текст з підсвіткою ([highlight.js](https://highlightjs.org)), UTF-8 / UTF-16 / Windows-1251 |
| PNG, JPG, GIF, WEBP, SVG, BMP; MP4, WEBM, MP3, WAV | нативно |
| DOC, PPT, ODT, ODP, VSD(X) | перетворення в PDF через [LibreOffice](https://www.libreoffice.org), якщо він встановлений |

Клавіші: `Ctrl+O` відкрити · `Ctrl+F` / `F3` пошук · `Ctrl+колесо`, `Ctrl+=`, `Ctrl+-`, `Ctrl+0` масштаб · `F11` повний екран · `Ctrl+P` друк. Файл можна перетягнути у вікно.

## Встановлення

Потрібні [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) і WebView2 Runtime (у Windows 11 вже є).

```bash
dotnet publish src/Visio -c Release -o dist/Visio
```

Скопіюйте `dist/Visio` куди завгодно і, щоб додати Visio в «Відкрити за допомогою», запустіть:

```bash
powershell -ExecutionPolicy Bypass -File dist/Visio/register.ps1
```

Скрипт пише лише в `HKCU\Software\Classes` (без прав адміністратора). Прибрати: той самий скрипт з `-Uninstall`.

## Структура

- `viewer/` — рендерер на HTML/JS; працює і в звичайному браузері: `python -m http.server` у корені, потім `/viewer/index.html?src=../samples/document.docx`.
- `src/Visio/` — WPF-оболонка з WebView2: вікно, діалог відкриття, роздача файлів сторінці (`DocServer`), конвертація через LibreOffice (`Converter`).
- `tools/make_samples.py` — генерує тестові файли в `samples/`.
