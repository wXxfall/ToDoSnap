"""
从 使用说明.md 生成精美 PDF
使用 fpdf2 + 中文字体（微软雅黑）
"""
import re, os, sys
from fpdf import FPDF
sys.stdout.reconfigure(encoding='utf-8')

# ── 配置 ─────────────────────────────────────────
MD_FILE = r"f:\codex\have to do\TodoSnap-dist\使用说明.md"
OUT_PDF = r"f:\codex\have to do\TodoSnap-dist\使用说明.pdf"
FONT_PATH = r"C:\Windows\Fonts\msyh.ttc"

PAGE_W, PAGE_H = 210, 297  # A4
MARGIN_L = 22
MARGIN_R = 22
MARGIN_T = 28
MARGIN_B = 22
BODY_W = PAGE_W - MARGIN_L - MARGIN_R  # 166mm usable

ACCENT = (52, 119, 235)       # 蓝色强调
ACCENT_LIGHT = (235, 245, 255)
DARK = (30, 30, 30)
GRAY = (100, 100, 100)
LIGHT_GRAY = (240, 240, 240)
WHITE = (255, 255, 255)
LIGHT_BG = (248, 249, 252)

# ── 工具 ─────────────────────────────────────────
def strip_html(text):
    return re.sub(r'<[^>]+>', '', text)

def is_div_line(line):
    return re.match(r'^<div\s', line.strip()) or line.strip() == '</div>'

def strip_emoji(text):
    """移除 PDF 字体不支持的 emoji 等字符"""
    return re.sub(r'[\U0001F300-\U0001F9FF\u2600-\u27BF\uFE00-\uFE0F\u25B6\u25C0\u25B8]', '', text)

def clean_md(text):
    """移除 markdown 格式标记，保留纯文本"""
    text = strip_emoji(text)
    text = re.sub(r'\*\*(.+?)\*\*', r'\1', text)
    text = re.sub(r'\*(.+?)\*', r'\1', text)
    text = re.sub(r'`(.+?)`', r'\1', text)
    return text

# ── PDF 类 ───────────────────────────────────────
class DocPDF(FPDF):
    def __init__(self):
        super().__init__('P', 'mm', 'A4')
        self.set_auto_page_break(True, MARGIN_B)
        self.add_font("CN", "", FONT_PATH)
        self.add_font("CN", "B", FONT_PATH)
        self.y0 = MARGIN_T  # 当前 Y
        self.add_page()

    def add_page(self, *args, **kwargs):
        super().add_page(*args, **kwargs)
        self.y0 = MARGIN_T

    def write_line(self, text, font="CN", style="", size=10, color=DARK,
                   align='L', h=5.5, left=None, right=None):
        x = left if left is not None else (MARGIN_L if align == 'L' else PAGE_W - MARGIN_R)
        self.set_xy(x, self.y0)
        self.set_font(font, style, size)
        self.set_text_color(*color)
        if right:
            self.cell(right - x, h, text, align=align)
        else:
            w = PAGE_W - MARGIN_L - MARGIN_R if align != 'L' else BODY_W
            self.cell(w, h, text, align=align)
        self.y0 += h

    def write_multi(self, text, font="CN", style="", size=10, color=DARK,
                    w=None, h=5.5, left=None):
        x = left if left is not None else MARGIN_L
        w = w if w else BODY_W
        self.set_xy(x, self.y0)
        self.set_font(font, style, size)
        self.set_text_color(*color)
        self.multi_cell(w, h, text, align='L')
        self.y0 = self.get_y()

    def hr(self, thickness=0.4, color=LIGHT_GRAY, pad=4):
        self.y0 += pad
        self.set_draw_color(*color)
        self.set_line_width(thickness)
        self.line(MARGIN_L, self.y0, PAGE_W - MARGIN_R, self.y0)
        self.y0 += pad + 1

    def box(self, text, bg=LIGHT_GRAY, font="CN", style="", size=9.5,
            color=DARK, pad_x=4, pad_y=3, radius=2):
        self.y0 += 1
        w = BODY_W - 2
        # Draw filled rounded rect
        self.set_fill_color(*bg)
        self.set_draw_color(*bg)
        x0, y0 = MARGIN_L + 1, self.y0
        self.rect(x0, y0, w, 20, style='F')  # placeholder height
        # Measure text height
        self.set_font(font, style, size)
        lines = self.multi_cell(w - pad_x * 2, 5.5, text, dry_run=True, output="LINES")
        box_h = max(len(lines) * 5.5, 10) + pad_y * 2
        # Redraw with correct height
        self.set_fill_color(*bg)
        self.set_draw_color(*bg)
        self.rect(x0, y0, w, box_h, style='F')
        # Write text
        self.set_xy(x0 + pad_x, y0 + pad_y)
        self.set_text_color(*color)
        self.multi_cell(w - pad_x * 2, 5.5, text)
        self.y0 = self.get_y() + 2

    def badge(self, text, bg=ACCENT, text_color=WHITE, font="CN", style="B", size=11):
        """标题背景条"""
        self.y0 += 2
        self.set_fill_color(*bg)
        self.set_draw_color(*bg)
        bar_h = 9
        self.rect(MARGIN_L, self.y0, BODY_W, bar_h, style='F')
        # Left accent stripe
        self.set_fill_color(ACCENT[0], ACCENT[1], ACCENT[2])
        self.rect(MARGIN_L, self.y0, 3, bar_h, style='F')
        self.set_text_color(*text_color)
        self.set_font(font, style, size)
        self.set_xy(MARGIN_L + 7, self.y0 + 1.2)
        self.cell(BODY_W - 10, 6.5, text)
        self.y0 += bar_h + 3

    def section_title(self, text):
        """H2 标题"""
        self.y0 += 5
        self.set_fill_color(*ACCENT_LIGHT)
        self.rect(MARGIN_L + 1, self.y0, BODY_W - 2, 10, style='F')
        # Left bar
        self.set_fill_color(*ACCENT)
        self.rect(MARGIN_L + 1, self.y0, 3.5, 10, style='F')
        self.set_text_color(*DARK)
        self.set_font("CN", "B", 13)
        self.set_xy(MARGIN_L + 8, self.y0 + 1.5)
        self.cell(BODY_W - 12, 7, text)
        self.y0 += 12

    def sub_title(self, text):
        """H3 标题"""
        self.y0 += 4
        self.set_text_color(*ACCENT)
        self.set_font("CN", "B", 11.5)
        self.set_xy(MARGIN_L + 2, self.y0)
        self.cell(BODY_W - 4, 6, text)
        self.y0 += 8

    def table(self, headers, rows, col_widths=None):
        """绘制表格"""
        if not col_widths:
            col_widths = [BODY_W / len(headers)] * len(headers)
        # Header
        self.y0 += 2
        y_start = self.y0
        self.set_fill_color(*ACCENT)
        x = MARGIN_L
        for i, (h, cw) in enumerate(zip(headers, col_widths)):
            self.set_xy(x, self.y0)
            self.set_font("CN", "B", 9)
            self.set_text_color(*WHITE)
            self.cell(cw, 8, f" {h}", fill=True)
            x += cw
        self.y0 += 8
        # Rows
        for ri, row in enumerate(rows):
            bg = LIGHT_BG if ri % 2 == 0 else WHITE
            x = MARGIN_L
            for i, (cell, cw) in enumerate(zip(row, col_widths)):
                self.set_xy(x, self.y0)
                self.set_fill_color(*bg)
                self.set_font("CN", "", 9)
                self.set_text_color(*DARK)
                self.cell(cw, 7.5, f" {cell}", fill=True)
                x += cw
            self.y0 += 7.5
        self.y0 += 2

    def bullet(self, text, indent=8, size=9.5):
        x0 = MARGIN_L + indent
        self.set_font("CN", "", size)
        self.set_text_color(*DARK)
        self.set_xy(x0, self.y0)
        self.cell(5, 5.5, "•")
        self.set_xy(x0 + 5, self.y0)
        self.multi_cell(BODY_W - indent - 5, 5.5, text)
        self.y0 = max(self.y0 + 5.5, self.get_y())

    def check(self, text, indent=8, size=9.5):
        x0 = MARGIN_L + indent
        self.set_font("CN", "", size)
        self.set_text_color(*DARK)
        self.set_xy(x0, self.y0)
        self.cell(5, 5.5, "✅")
        self.set_xy(x0 + 7, self.y0)
        self.multi_cell(BODY_W - indent - 7, 5.5, text)
        self.y0 = max(self.y0 + 5.5, self.get_y())

    def qa(self, question, answer):
        self.y0 += 2
        # Question
        self.set_font("CN", "B", 10.5)
        self.set_text_color(*ACCENT)
        self.set_xy(MARGIN_L + 2, self.y0)
        self.cell(BODY_W - 4, 6, question)
        self.y0 += 6.5
        # Answer
        self.set_font("CN", "", 9.5)
        self.set_text_color(*GRAY)
        self.set_xy(MARGIN_L + 6, self.y0)
        self.multi_cell(BODY_W - 8, 5.5, answer)
        self.y0 += 2


# ── 解析与渲染 ────────────────────────────────────
def parse_and_render():
    with open(MD_FILE, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    pdf = DocPDF()

    i = 0
    n = len(lines)
    in_yaml = False
    yaml_lines = []
    in_code = False
    code_buf = []
    in_div = False

    while i < n:
        line = lines[i].rstrip()

        # Skip YAML frontmatter
        if i == 0 and line.strip() == '---':
            in_yaml = True
            i += 1
            continue
        if in_yaml:
            if line.strip() == '---':
                in_yaml = False
                i += 1
                continue
            yaml_lines.append(line)
            i += 1
            continue

        # Skip empty lines at page top
        if pdf.y0 <= MARGIN_T + 5 and line.strip() == '':
            i += 1
            continue

        # ── HTML div ──
        if '<div' in line and 'align="center"' in line:
            in_div = True
            i += 1
            continue
        if in_div and line.strip() == '</div>':
            in_div = False
            i += 1
            continue

        # ── H1 (center) ──
        if line.startswith('# ') and not line.startswith('## '):
            text = line[2:].strip()
            pdf.y0 += 8
            pdf.set_font("CN", "B", 22)
            pdf.set_text_color(*DARK)
            pdf.set_xy(MARGIN_L, pdf.y0)
            pdf.cell(BODY_W, 10, text, align='C')
            pdf.y0 += 12
            i += 1
            continue

        # ── Centered subtitle (bold text between HR in div) ──
        if line.startswith('**v') and '·' in line:
            pdf.set_font("CN", "", 11)
            pdf.set_text_color(*GRAY)
            pdf.set_xy(MARGIN_L, pdf.y0)
            pdf.cell(BODY_W, 7, line.replace('**', ''), align='C')
            pdf.y0 += 10
            i += 1
            continue

        # ── HR in div ──
        if line.strip() == '---' and in_div:
            pdf.y0 += 4
            i += 1
            continue

        # ── Horizontal rule between sections ──
        if line.strip() == '---' and not in_div:
            pdf.hr()
            i += 1
            continue

        # ── H2 ──
        if line.startswith('## ') and not line.startswith('### '):
            text = clean_md(line[3:].strip())
            pdf.section_title(text)
            i += 1
            continue

        # ── H3 ──
        if line.startswith('### '):
            text = clean_md(line[4:].strip())
            pdf.sub_title(text)
            i += 1
            continue

        # ── Blockquote ──
        if line.startswith('>'):
            # Collect all blockquote lines
            bq_lines = []
            while i < n and lines[i].strip().startswith('>'):
                bq_lines.append(lines[i].strip()[1:].strip())
                i += 1
            text = '\n'.join(bq_lines)
            text = clean_md(text)
            pdf.box(text)
            continue

        # ── Table ──
        if line.strip().startswith('|'):
            # Collect table
            table_lines = []
            while i < n and lines[i].strip().startswith('|'):
                table_lines.append(lines[i].strip())
                i += 1
            if len(table_lines) >= 2:
                # Parse header
                header_cells = [c.strip() for c in table_lines[0].split('|')[1:-1]]
                # Skip separator line
                # Parse rows
                rows = []
                for tl in table_lines[2:]:
                    cells = [c.strip() for c in tl.split('|')[1:-1]]
                    rows.append([clean_md(c) for c in cells])
                headers = [clean_md(h) for h in header_cells]
                # Calculate column widths
                ncols = len(headers)
                base_w = BODY_W / ncols
                if any('说明' in h or '详情' in h or '功能' in h or '描述' in h or '路径' in h for h in headers):
                    if ncols >= 3:
                        col_widths = [base_w * 0.6] + [base_w * 1.4] * (ncols - 1)
                    elif ncols == 2 and any('说明' in h or '描述' in h or '路径' in h for h in headers):
                        col_widths = [base_w * 0.8, base_w * 1.2]
                    else:
                        col_widths = [base_w] * ncols
                else:
                    col_widths = [base_w] * ncols
                pdf.table(headers, rows, col_widths)
            continue

        # ── Code block ──
        if line.strip().startswith('```'):
            in_code = not in_code
            if not in_code and code_buf:
                code_text = '\n'.join(code_buf)
                pdf.y0 += 2
                pdf.set_fill_color(245, 245, 248)
                pdf.set_draw_color(220, 220, 230)
                pdf.rect(MARGIN_L + 2, pdf.y0, BODY_W - 4, len(code_buf) * 5 + 8, style='DF')
                pdf.set_font("CN", "", 8.5)
                pdf.set_text_color(*GRAY)
                for cl in code_buf:
                    pdf.set_xy(MARGIN_L + 6, pdf.y0 + 4)
                    pdf.cell(BODY_W - 12, 5, cl)
                    pdf.y0 += 5
                pdf.y0 += 4
                code_buf = []
            i += 1
            continue
        if in_code:
            code_buf.append(line)
            i += 1
            continue

        # ── Bullet / Check item ──
        if line.strip().startswith('- ✅'):
            text = clean_md(line.strip()[2:].strip())
            pdf.check(text)
            i += 1
            continue
        if line.strip().startswith('- '):
            text = clean_md(line.strip()[2:].strip())
            pdf.bullet(text)
            i += 1
            continue

        # ── Bold standalone line (like "**完成待办**：...") ──
        if line.startswith('**') and '**' in line[2:]:
            text = clean_md(line.strip())
            pdf.set_font("CN", "B", 10)
            pdf.set_text_color(*DARK)
            pdf.set_xy(MARGIN_L + 2, pdf.y0)
            pdf.multi_cell(BODY_W - 4, 5.5, text)
            pdf.y0 += 2
            i += 1
            continue

        # ── Ordered list ──
        if re.match(r'^\d+\.\s', line.strip()):
            text = clean_md(re.sub(r'^\d+\.\s', '', line.strip()))
            pdf.write_line(f"  {text}", size=9.5, h=5.5)
            i += 1
            continue

        # ── Plain text ──
        if line.strip():
            text = clean_md(line.strip())
            pdf.set_font("CN", "", 10)
            pdf.set_text_color(*DARK)
            pdf.set_xy(MARGIN_L, pdf.y0)
            pdf.multi_cell(BODY_W, 5.5, text)
            pdf.y0 += 1
        else:
            pdf.y0 += 2

        i += 1

    # ── Footer ──
    pdf.y0 += 10
    pdf.set_font("CN", "", 9)
    pdf.set_text_color(*GRAY)
    pdf.set_xy(MARGIN_L, pdf.y0)
    pdf.cell(BODY_W, 6, "TodoSnap v1.1.1 — 使用说明", align='C')

    pdf.output(OUT_PDF)
    print(f"✅ PDF 已生成: {OUT_PDF}")
    print(f"   文件大小: {os.path.getsize(OUT_PDF) / 1024:.1f} KB")


if __name__ == '__main__':
    os.chdir(os.path.dirname(MD_FILE))
    parse_and_render()
