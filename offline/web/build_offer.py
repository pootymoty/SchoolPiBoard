#!/usr/bin/env python3
"""
Собирает страницу оферты для сайта из offline/docs/legal/offer-desktop.md.

Единственный источник текста — markdown-файл; править нужно только его,
а потом выполнить:

    python3 offline/web/build_offer.py

Скрипт кладёт рядом offer.html — самодостаточный файл, который можно
положить в templates Flask или отдать статикой. Всё, что в markdown
идёт до заголовка «ПУБЛИЧНАЯ ОФЕРТА» (служебные заметки для репозитория),
на страницу не попадает.

Зависимостей нет: markdown в файле используется ограниченный, поэтому
свой разбор короче, чем установка библиотеки на сервер.
"""

import html
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "docs" / "legal" / "offer-desktop.md"
TARGET = pathlib.Path(__file__).resolve().parent / "offer.html"

TEMPLATE = """<!DOCTYPE html>
<!--
  Оферта SchoolPiBoard. Файл СГЕНЕРИРОВАН из offline/docs/legal/offer-desktop.md
  скриптом offline/web/build_offer.py — руками не править, правки затрутся.

  В разметке намеренно нет удвоенных фигурных скобок и скобок с процентом:
  Jinja принимает их за подстановку, в том числе внутри HTML-комментариев,
  поэтому файл можно положить в templates Flask как есть.
-->
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Оферта — SchoolPiBoard</title>
  <meta name="description" content="Публичная оферта о заключении лицензионного договора на использование программы SchoolPiBoard.">
  <meta name="robots" content="noindex">
  <style>
    /* Палитра основного сайта school-pi.online. */
    :root {
      --page-bg: #f6f2eb;
      --card-bg: #fdf8e9;
      --text: #333333;
      --text-muted: #6a6f68;
      --accent: #788176;
      --border: #e3ddd0;
      --radius: 14px;
      --maxw: 800px;
      --font: "Segoe UI", Roboto, Arial, sans-serif;
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      background: var(--page-bg);
      color: var(--text);
      font-family: var(--font);
      font-size: 16px;
      line-height: 1.65;
    }

    .wrap {
      max-width: var(--maxw);
      margin: 0 auto;
      padding: 40px 20px 64px;
    }

    .doc {
      background: var(--card-bg);
      border: 1px solid var(--border);
      border-radius: var(--radius);
      padding: 40px;
    }

    h1 { font-size: 27px; line-height: 1.3; margin: 0 0 8px; }
    h2 { font-size: 20px; margin: 34px 0 12px; }
    h3 { font-size: 17px; font-weight: 600; color: var(--text-muted); margin: 0 0 26px; }
    p { margin: 0 0 12px; }

    a { color: var(--accent); }

    table {
      border-collapse: collapse;
      width: 100%;
      margin: 16px 0;
      font-size: 15px;
    }
    td {
      border-bottom: 1px solid var(--border);
      padding: 9px 12px;
      vertical-align: top;
    }
    td:first-child { color: var(--text-muted); width: 42%; }

    .table-scroll { overflow-x: auto; }

    hr {
      border: 0;
      border-top: 1px solid var(--border);
      margin: 28px 0;
    }

    .back {
      display: inline-block;
      margin-bottom: 22px;
      font-size: 15px;
      color: var(--text-muted);
      text-decoration: none;
    }
    .back:hover { color: var(--accent); }

    .updated {
      margin-top: 30px;
      padding-top: 18px;
      border-top: 1px solid var(--border);
      font-size: 14px;
      color: var(--text-muted);
    }

    @media (max-width: 600px) {
      .doc { padding: 24px 18px; border-radius: 0; margin: 0 -20px; border-left: 0; border-right: 0; }
      h1 { font-size: 23px; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <a class="back" href="/">← На главную</a>
    <article class="doc">
__BODY__
    </article>
  </div>
</body>
</html>
"""


def inline(text):
    """Экранирование плюс жирный шрифт, ссылки и голые адреса."""
    out = html.escape(text)
    out = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", out)
    out = re.sub(r"\[(.+?)\]\((.+?)\)", r'<a href="\2">\1</a>', out)
    out = re.sub(r"(?<![\">])(https?://[^\s<]+)", r'<a href="\1">\1</a>', out)
    return out


def convert(markdown):
    lines = markdown.splitlines()

    # Всё до заголовка оферты — заметки для репозитория, на сайт не идут.
    for i, line in enumerate(lines):
        if line.startswith("## ПУБЛИЧНАЯ ОФЕРТА"):
            lines = lines[i:]
            break
    else:
        sys.exit("не найден заголовок «## ПУБЛИЧНАЯ ОФЕРТА»")

    out = []
    paragraph = []
    table = []
    first_heading = True

    def flush_paragraph():
        if paragraph:
            out.append("<p>" + inline(" ".join(paragraph)) + "</p>")
            paragraph.clear()

    def flush_table():
        if not table:
            return
        rows = []
        for raw in table:
            cells = [c.strip() for c in raw.strip().strip("|").split("|")]
            # разделительная строка вида |---|---| и пустая шапка не нужны
            if all(re.fullmatch(r":?-+:?", c) for c in cells if c):
                continue
            if not any(cells):
                continue
            rows.append("<tr>" + "".join("<td>" + inline(c) + "</td>" for c in cells) + "</tr>")
        table.clear()
        if rows:
            out.append('<div class="table-scroll"><table>' + "".join(rows) + "</table></div>")

    for line in lines:
        stripped = line.strip()

        if stripped.startswith("|"):
            flush_paragraph()
            table.append(stripped)
            continue
        flush_table()

        if not stripped:
            flush_paragraph()
            continue

        if stripped.startswith("> "):
            continue  # служебные заметки

        if stripped == "---":
            flush_paragraph()
            continue

        if stripped.startswith("#"):
            flush_paragraph()
            level = len(stripped) - len(stripped.lstrip("#"))
            text = inline(stripped[level:].strip())
            if first_heading:
                out.append("<h1>" + text + "</h1>")
                first_heading = False
            elif level >= 3:
                out.append("<h3>" + text + "</h3>")
            else:
                out.append("<h2>" + text + "</h2>")
            continue

        if stripped.startswith("Дата публикации"):
            flush_paragraph()
            out.append('<p class="updated">' + inline(stripped) + "</p>")
            continue

        paragraph.append(stripped)

    flush_paragraph()
    flush_table()
    return "\n".join("      " + tag for tag in out)


def main():
    markdown = SOURCE.read_text(encoding="utf-8")
    page = TEMPLATE.replace("__BODY__", convert(markdown))
    TARGET.write_text(page, encoding="utf-8")
    print("готово:", TARGET.relative_to(ROOT), f"({len(page)} байт)")


if __name__ == "__main__":
    main()
