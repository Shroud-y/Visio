"""Generate test documents for the viewer into ../samples.

Requires: pip install python-docx openpyxl python-pptx reportlab Pillow
Run:      python tools/make_samples.py
"""
import io
import json
from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "samples"
OUT.mkdir(exist_ok=True)

UA = "Привіт! Це тестовий документ: їжак, ґанок, Єва, ще."


def make_image():
    img = Image.new("RGB", (640, 400), "#4f7cff")
    d = ImageDraw.Draw(img)
    d.ellipse((220, 100, 420, 300), fill="#ffd34d")
    d.rectangle((20, 20, 200, 80), fill="#ffffff")
    img.save(OUT / "image.png")
    img.save(OUT / "image.jpg", quality=85)
    (OUT / "image.svg").write_text(
        '<svg xmlns="http://www.w3.org/2000/svg" width="300" height="200">'
        '<rect width="300" height="200" fill="#2a9d8f"/><circle cx="150" cy="100" r="60" fill="#e9c46a"/>'
        '<script>alert(1)</script></svg>',
        encoding="utf-8",
    )
    return img


def make_pdf():
    from reportlab.lib.pagesizes import A4
    from reportlab.pdfbase import pdfmetrics
    from reportlab.pdfbase.ttfonts import TTFont
    from reportlab.pdfgen import canvas

    pdfmetrics.registerFont(TTFont("Arial", "C:/Windows/Fonts/arial.ttf"))
    c = canvas.Canvas(str(OUT / "document.pdf"), pagesize=A4)
    for page in range(1, 4):
        c.setFont("Arial", 20)
        c.drawString(72, 770, f"Сторінка {page}")
        c.setFont("Arial", 12)
        c.drawString(72, 740, UA)
        c.showPage()
    c.save()


def make_text():
    (OUT / "readme.md").write_text(
        "# Заголовок\n\n" + UA + "\n\n- пункт **жирний**\n- пункт *курсив*\n\n"
        "| Колонка | Значення |\n|---|---|\n| a | 1 |\n| b | 2 |\n\n"
        "```python\ndef hello():\n    return 'світ'\n```\n\n"
        "![картинка](image.png)\n\n<script>alert('xss')</script>\n\n[посилання](https://example.com)\n",
        encoding="utf-8",
    )
    (OUT / "data.json").write_text(
        json.dumps({"name": "Visio", "items": [1, 2, 3], "ok": True}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    (OUT / "table.csv").write_text(
        "Назва,Кількість,Ціна\nЯблука,10,25.5\nГруші,5,40\n\"Сир, твердий\",2,180\n",
        encoding="utf-8",
    )


def make_docx(img):
    from docx import Document
    from docx.enum.text import WD_ALIGN_PARAGRAPH
    from docx.shared import Inches, Pt, RGBColor

    doc = Document()
    doc.sections[0].header.paragraphs[0].text = "Верхній колонтитул"
    doc.sections[0].footer.paragraphs[0].text = "Нижній колонтитул"
    doc.add_heading("Тестовий документ DOCX", 0)
    p = doc.add_paragraph(UA + " ")
    p.add_run("Жирний ").bold = True
    p.add_run("курсив ").italic = True
    r = p.add_run("червоний")
    r.font.color.rgb = RGBColor(0xC0, 0x20, 0x20)
    r.font.size = Pt(16)
    c = doc.add_paragraph("Вирівняно по центру")
    c.alignment = WD_ALIGN_PARAGRAPH.CENTER
    doc.add_heading("Списки", 1)
    for t in ("перший", "другий", "третій"):
        doc.add_paragraph(t, style="List Bullet")
    for t in ("один", "два"):
        doc.add_paragraph(t, style="List Number")
    doc.add_heading("Таблиця", 1)
    table = doc.add_table(rows=3, cols=3)
    table.style = "Table Grid"
    for i, row in enumerate(table.rows):
        for j, cell in enumerate(row.cells):
            cell.text = f"Р{i + 1}К{j + 1}"
    doc.add_heading("Зображення", 1)
    buf = io.BytesIO()
    img.save(buf, "PNG")
    buf.seek(0)
    doc.add_picture(buf, width=Inches(3))
    doc.add_page_break()
    doc.add_heading("Друга сторінка", 1)
    for i in range(30):
        doc.add_paragraph(f"Рядок {i + 1}. {UA}")
    doc.save(OUT / "document.docx")


def make_xlsx():
    from openpyxl import Workbook
    from openpyxl.styles import Font, PatternFill

    wb = Workbook()
    ws = wb.active
    ws.title = "Продажі"
    ws.append(["Товар", "Кількість", "Ціна", "Сума"])
    for c in ws[1]:
        c.font = Font(bold=True)
        c.fill = PatternFill("solid", fgColor="DDEBF7")
    rows = [("Яблука", 10, 25.5), ("Груші", 5, 40), ("Сир", 2, 180)]
    for i, (n, q, p) in enumerate(rows, start=2):
        ws.append([n, q, p, f"=B{i}*C{i}"])
    ws.append(["Разом", None, None, "=SUM(D2:D4)"])
    ws.merge_cells("A7:D7")
    ws["A7"] = "Об'єднана клітинка"
    ws2 = wb.create_sheet("Другий аркуш")
    for r in range(1, 201):
        ws2.append([r, f"Рядок {r}", r * 1.5])
    wb.save(OUT / "workbook.xlsx")
    # openpyxl writes formulas without cached values, like a fresh export would not;
    # keep this file to check how formula-only cells are shown.


def make_pptx(img):
    from pptx import Presentation
    from pptx.util import Inches, Pt

    prs = Presentation()
    s = prs.slides.add_slide(prs.slide_layouts[0])
    s.shapes.title.text = "Тестова презентація"
    s.placeholders[1].text = UA
    s = prs.slides.add_slide(prs.slide_layouts[1])
    s.shapes.title.text = "Список"
    body = s.placeholders[1].text_frame
    body.text = "Перший пункт"
    for t in ("Другий пункт", "Третій пункт"):
        body.add_paragraph().text = t
    s = prs.slides.add_slide(prs.slide_layouts[5])
    s.shapes.title.text = "Зображення і таблиця"
    buf = io.BytesIO()
    img.save(buf, "PNG")
    buf.seek(0)
    s.shapes.add_picture(buf, Inches(0.5), Inches(1.8), width=Inches(4))
    tbl = s.shapes.add_table(3, 2, Inches(5), Inches(1.8), Inches(4), Inches(1.5)).table
    for i in range(3):
        for j in range(2):
            tbl.cell(i, j).text = f"{i}:{j}"
    tb = s.shapes.add_textbox(Inches(0.5), Inches(6), Inches(8), Inches(0.6))
    tb.text_frame.text = "Текстове поле"
    tb.text_frame.paragraphs[0].runs[0].font.size = Pt(24)
    prs.save(OUT / "slides.pptx")


def make_rtf():
    def esc(s):
        return "".join(ch if ord(ch) < 128 else f"\\u{ord(ch) if ord(ch) < 32768 else ord(ch) - 65536}?" for ch in s)

    (OUT / "document.rtf").write_text(
        "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0 Arial;}}{\\colortbl;\\red200\\green30\\blue30;}\n"
        "\\f0\\fs32\\b " + esc("Заголовок RTF") + "\\b0\\fs22\\par\n"
        + esc(UA) + "\\par\n"
        "\\i " + esc("курсив") + "\\i0  \\cf1 " + esc("червоний") + "\\cf0\\par\n}",
        encoding="ascii",
    )
    # What WordPad / older Word write for Cyrillic: \ansicpg1251 with \'xx bytes.
    body = "".join(f"\\'{b:02x}" if b > 127 else chr(b) for b in UA.encode("cp1251"))
    (OUT / "document-1251.rtf").write_text(
        "{\\rtf1\\ansi\\ansicpg1251\\deff0{\\fonttbl{\\f0\\fswiss\\fcharset204 Arial;}}\n"
        "\\f0\\fs24 " + body + "\\par\n}",
        encoding="ascii",
    )


if __name__ == "__main__":
    img = make_image()
    make_pdf()
    make_text()
    make_docx(img)
    make_xlsx()
    make_pptx(img)
    make_rtf()
    print("samples written to", OUT)
