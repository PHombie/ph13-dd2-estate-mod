#!/usr/bin/env python3
"""DD1's bitmap fonts and text styles for the offline previews (tools/preview_*.py).

Reference implementation of src/DD2Estate/Dd1/Dd1Fonts.cs: change one, change the other. Reads the player's own
Darkest Dungeon (1) install; nothing is copied into the repo.

    from dd1_bmfont import Dd1Text
    text = Dd1Text(dd1_root)
    text.draw(canvas, (286, 70), "The Darkest Estate", "town_estate_title")

How DD1 sets text:
  * fonts/fonts.darkest: `font:` entries name a BMFont file (`.id "dwarven_axe_large" .file "fonts/dwarvenaxe-l.fnt"`),
    `font_ref:` entries say which of them a text style uses (`.id "town_roster_name" .font "dwarven_axe_medium"`).
    The layout files and the code refer to the styles. There are no sizes: a font is drawn 1:1 on the 1920x1080
    screen. colours/base.colours.darkest gives the same style ids their colours (`.rgba` or `.shared_id`).
  * A .fnt file is AngelCode BMFont text: `common lineHeight= base= scaleW= scaleH=`, one `page` per texture,
    `char id= x= y= width= height= xoffset= yoffset= xadvance= page=`, `kerning first= second= amount=`.
    The cursor is the top-left corner of the line cell: a glyph is drawn at cursor + (xoffset, yoffset).
  * x/y/width/height are pixels of the page as it is stored. `scaleW`/`scaleH` are not to be trusted: the two
    DwarvenAxe fonts declare 512x256 and 1024x512 over pages of 256x256 and 512x512, and their glyph
    rectangles only make sense in the stored pixels.
  * Coverage is the alpha channel (the grey value of an 8-bit page).
  * popup.fnt (style pop_text: the numbers and words over a hero) is drawn with an outline (`info outline=4`,
    `common alphaChnl=1 redChnl=0`): the colour channels hold the glyph, alpha the glyph with its outline. DD1
    gives the two their own colours (pop_text_<kind>, pop_text_outline_<kind>); `draw(..., outline=...)`.

A "size" in the plugin is TextMeshPro's and means what it means for DD2's own font, whose capitals are 0.705 of
the size tall and whose line is 1.2 of it: a DD1 font's native (1:1) size is the larger of cap height / 0.705
and line height / 1.2 (`BMFont.native_size`: DwarvenAxe large 53, medium 34, Ubuntu medium 24, small 23). At
any size its capitals and its line are then no taller than DD2's, and the boxes made for DD2's font hold.
"""
import math
import os
import re

from PIL import Image, ImageChops

CAP_PER_EM = 0.705                   # cap height / point size of DD2's NDDunkel font (its TMP face info: 62 / 88)
LINE_PER_EM = 1.2                    # its line height / point size (105.6 / 88)

# FALLBACK, used only when fonts/fonts.darkest cannot be read: DD1's own table.
FONT_FILES = {
    "ubuntu_small": "fonts/ubuntu.fnt", "ubuntu_medium": "fonts/ubuntu_m.fnt",
    "dwarven_axe_medium": "fonts/dwarvenaxe-m.fnt", "dwarven_axe_large": "fonts/dwarvenaxe-l.fnt",
    "popup": "fonts/popup.fnt",
}
# The plugin's five roles (UiKit.Dd1Text) and the DD1 font each stands for.
ROLES = {
    "title": "dwarven_axe_large", "header": "dwarven_axe_medium", "body": "ubuntu_medium",
    "small": "ubuntu_small", "numbers": "ubuntu_medium",
}
# FALLBACK colours (colours/base.colours.darkest).
COLOURS = {"neutral": (174, 172, 162), "notable": (200, 180, 110), "harmful": (177, 25, 0), "stress": (224, 221, 206)}

_PAIR = re.compile(r'(\w+)=("[^"]*"|\S+)')


class BMFont:
    def __init__(self, dd1, rel):
        self.rel = rel
        self.chars = {}
        self.kerning = {}
        self.line_height = self.base = 0
        self.outline = 0
        pages = {}
        folder = os.path.dirname(os.path.join(dd1, rel))
        with open(os.path.join(dd1, rel), encoding="utf-8", errors="replace") as f:
            for line in f:
                tag = line.split(" ", 1)[0].strip()
                kv = {k: v.strip('"') for k, v in _PAIR.findall(line)}
                if tag == "info":
                    self.outline = int(kv.get("outline", 0))
                elif tag == "common":
                    self.line_height, self.base = int(kv["lineHeight"]), int(kv["base"])
                elif tag == "page":
                    pages[int(kv["id"])] = kv["file"]
                elif tag == "char":
                    self.chars[int(kv["id"])] = {k: int(v) for k, v in kv.items()}
                elif tag == "kerning":
                    self.kerning[(int(kv["first"]), int(kv["second"]))] = int(kv["amount"])
        self._pages = {}
        # An outlined font (popup.fnt: alphaChnl=1, redChnl=0) keeps the glyph in the colour channels and glyph
        # and outline together in alpha; DD1 colours the two apart. _inside: the glyphs alone (Dd1Font.Asset),
        # _pages: with their outline (Dd1Font.Outline).
        self._inside = {}
        for index, name in pages.items():
            path = os.path.join(folder, name)
            if not os.path.isfile(path):
                continue                             # ubuntu.fnt names pages of other languages that may be absent
            img = Image.open(path)
            self._pages[index] = img.convert("L") if img.mode in ("L", "P") else img.convert("RGBA").getchannel("A")
            if self.outline > 0 and img.mode not in ("L", "P"):
                self._inside[index] = ImageChops.multiply(img.convert("RGBA").getchannel("R"), self._pages[index])
        self._glyphs = {}
        cap = self.chars.get(ord("H"))
        self.cap_height = cap["height"] if cap else self.base
        # TextMeshPro's point size at which the font is drawn 1:1 (Dd1Font.NativeSize).
        self.native_size = max(int(round(self.cap_height / CAP_PER_EM)), int(math.ceil(self.line_height / LINE_PER_EM)))

    def glyph(self, code, inside=False):
        """Coverage image of a character, or None (space, missing page). inside: of an outlined font the glyph
        without its outline."""
        key = (code, inside)
        if key in self._glyphs:
            return self._glyphs[key]
        c = self.chars.get(code)
        img = None
        pages = self._inside if inside and self._inside else self._pages
        if c and c["width"] > 0 and c["height"] > 0 and c["page"] in pages:
            img = pages[c["page"]].crop((c["x"], c["y"], c["x"] + c["width"], c["y"] + c["height"]))
        self._glyphs[key] = img
        return img

    def width(self, text):
        x = 0
        prev = None
        for ch in text:
            code = ord(ch)
            c = self.chars.get(code)
            if c is None:
                continue
            x += self.kerning.get((prev, code), 0) + c["xadvance"]
            prev = code
        return x

    def render(self, text, inside=False):
        """One line as an L image (coverage) of width(text) x line_height; glyphs may reach a little outside
        the advance box, hence the margin. Returns (image, margin)."""
        margin = 8
        img = Image.new("L", (max(1, self.width(text)) + 2 * margin, self.line_height + 2 * margin), 0)
        x = margin
        prev = None
        for ch in text:
            code = ord(ch)
            c = self.chars.get(code)
            if c is None:
                continue
            x += self.kerning.get((prev, code), 0)
            g = self.glyph(code, inside)
            if g is not None:
                box = (x + c["xoffset"], margin + c["yoffset"])
                region = img.crop((box[0], box[1], box[0] + g.width, box[1] + g.height))
                img.paste(ImageChops.lighter(region, g), box)    # neighbours may overlap: keep the stronger
            x += c["xadvance"]
            prev = code
        return img, margin



class Dd1Text:
    """DD1's text styles: font and colour by `font_ref` id, drawn with the BMFont files."""

    def __init__(self, dd1):
        self.dd1 = dd1
        self.files = dict(FONT_FILES)
        self.refs = {}
        self.colours = dict(COLOURS)
        self._fonts = {}
        self._read_fonts()
        self._read_colours()

    def _read_fonts(self):
        path = os.path.join(self.dd1, "fonts", "fonts.darkest")
        if not os.path.isfile(path):
            return
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            for line in f:
                kv = dict(re.findall(r'\.(\w+)\s+"([^"]*)"', line))
                if line.lstrip().startswith("font:") and "id" in kv and "file" in kv:
                    self.files[kv["id"]] = kv["file"]
                elif line.lstrip().startswith("font_ref:") and "id" in kv and "font" in kv:
                    self.refs[kv["id"]] = kv["font"]

    def _read_colours(self):
        path = os.path.join(self.dd1, "colours", "base.colours.darkest")
        if not os.path.isfile(path):
            return
        shares = {}
        with open(path, encoding="utf-8-sig", errors="replace") as f:
            for line in f:
                m = re.search(r'\.id\s+"?([\w.]+)"?\s+\.(rgba|shared_id)\s+(.*)', line)
                if not m:
                    continue
                value = m.group(3).strip()
                if m.group(2) == "shared_id":
                    shares[m.group(1)] = value.split()[0].strip('"')
                elif value.startswith("#"):
                    h = value[1:].split()[0]
                    if len(h) == 3:
                        h = "".join(ch * 2 for ch in h)
                    self.colours[m.group(1)] = tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))
                else:
                    parts = value.split()
                    if len(parts) >= 3 and all(p.isdigit() for p in parts[:3]):
                        self.colours[m.group(1)] = tuple(int(p) for p in parts[:3])
        for _ in range(4):                           # shared ids may chain
            for key, other in shares.items():
                if other in self.colours:
                    self.colours[key] = self.colours[other]

    def font(self, style):
        """A font by role ("title"), DD1 font id ("dwarven_axe_large") or style id ("town_roster_name")."""
        font_id = ROLES.get(style, self.refs.get(style, style))
        if font_id not in self._fonts:
            self._fonts[font_id] = BMFont(self.dd1, self.files[font_id])
        return self._fonts[font_id]

    def colour(self, style, fallback="neutral"):
        return self.colours.get(style, self.colours[fallback])

    def width(self, text, style, size=None):
        font = self.font(style)
        return font.width(text) * ((size / font.native_size) if size else 1.0)

    def draw(self, canvas, pos, text, style, colour=None, anchor="l", size=None, max_width=None, alpha=1.0, outline=None):
        """Draws one line with its line cell's top edge at pos[1] (BMFont's cursor; TextMeshPro with top
        alignment puts it the same way). anchor: l / m / r = pos[0] is the left edge, centre or right edge.
        size: TextMeshPro point size (None = native, 1:1). max_width: shrink to fit, as auto-sizing does.
        outline: for an outlined font the outline's colour: it is drawn first, with the glyphs, and the glyphs
        alone go over it in `colour` (the plugin's two labels, RaidPopText). Returns the drawn width."""
        font = self.font(style)
        scale = (size / font.native_size) if size else 1.0
        if max_width and font.width(text) * scale > max_width:
            scale = max_width / font.width(text)
        if colour is None:
            colour = self.colour(style)
        elif isinstance(colour, str):
            colour = self.colour(colour)
        if isinstance(outline, str):
            outline = self.colour(outline)
        width = font.width(text) * scale
        left = pos[0] - (width / 2 if anchor == "m" else width if anchor == "r" else 0)
        layers = [(False, outline), (True, colour)] if outline is not None and font.outline > 0 else [(False, colour)]
        for inside, ink in layers:
            cover, margin = font.render(text, inside)
            if scale != 1.0:
                cover = cover.resize((max(1, int(round(cover.width * scale))), max(1, int(round(cover.height * scale)))), Image.BILINEAR)
            if alpha < 1.0:
                cover = cover.point(lambda v: int(v * alpha))
            layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
            tint = Image.new("RGBA", cover.size, tuple(ink[:3]) + (255,))
            tint.putalpha(cover)
            layer.paste(tint, (int(round(left - margin * scale)), int(round(pos[1] - margin * scale))))
            canvas.alpha_composite(layer)
        return width

    def wrap(self, text, style, width, size=None):
        font = self.font(style)
        scale = (size / font.native_size) if size else 1.0
        lines, line = [], ""
        for word in text.split():
            trial = (line + " " + word).strip()
            if font.width(trial) * scale <= width or not line:
                line = trial
            else:
                lines.append(line)
                line = word
        if line:
            lines.append(line)
        return lines

    def line_height(self, style, size=None):
        font = self.font(style)
        return font.line_height * ((size / font.native_size) if size else 1.0)
