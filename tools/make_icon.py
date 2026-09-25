"""
Generates HitSpark's icon (icon.png, 512x512) from one set of constants.

It shares LootView's panel language - the deep-navy glass and brass frame every window of
both plugins is built from - so the two read as siblings. The subject is HitSpark's own:
an impact burst, white-hot at the core and cooling to ember, with the spark rays of a
critical hit and the arc of a number flying in from below.

Run from the repository root:  python tools/make_icon.py
"""
import math

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

# ---------------------------------------------------------------- palette ------
# Panel colours straight from src/UI/Theme.cs.
INK_TOP = (0x18, 0x21, 0x33)
INK_BOTTOM = (0x06, 0x09, 0x10)
CRYSTAL = (0x6F, 0xB8, 0xE0)
GOLD = (0xD6, 0xB0, 0x68)
GOLD_BRIGHT = (0xF2, 0xD7, 0x96)

# The burst: a hot core cooling outward, like the crit colours in the presets.
WHITE_HOT = (0xFF, 0xFB, 0xEE)
FLARE = (0xFF, 0xDF, 0x7A)
EMBER = (0xFF, 0x86, 0x22)
EMBER_DEEP = (0xC4, 0x36, 0x0A)
SCORCH = (0x4A, 0x14, 0x04)

# ---------------------------------------------------------------- geometry -----
SIZE = 512
CORNER = 104
FRAME_INSET = 36
FRAME_RADIUS = 72
FRAME_WIDTH = 3.5
BRACKET = 34

# The burst sits a little up and right, leaving room for the trail coming in from below-left.
CX, CY = 272, 236
TILT = math.radians(-14)

BURST_SPIKES = 12
BURST_OUTER = [150, 108, 132, 104, 146, 112, 128, 100, 142, 110, 134, 102]
BURST_INNER = 66

STAR_RADIUS = 118    # the bright four-point sparkle over the burst
STAR_WAIST = 20

RAYS = [             # (angle offset in spikes, length, width) - a few, not a sunburst
    (0.5, 206, 9), (3.5, 176, 7), (5.5, 200, 8), (8.5, 184, 7), (11.5, 196, 8),
]

# Quadratic curve of the incoming number, from lower left into the burst.
TRAIL = ((92, 446), (104, 312), (214, 284))

SPARKLES = [         # (x, y, radius, alpha)
    (410, 118, 16, 0.95),
    (118, 150, 10, 0.70),
    (420, 356, 11, 0.80),
    (352, 420, 7, 0.60),
    (176, 96, 6, 0.55),
]

DOTS = [(388, 190, 4.5), (150, 214, 3.5), (446, 262, 3.0), (300, 404, 3.5), (206, 408, 2.5)]


# ================================================================== helpers ====
def layer(n):
    return Image.new('RGBA', (n, n), (0, 0, 0, 0))


def fade(img, alpha):
    """Scales a layer's alpha channel, so it composites at the intended opacity."""
    r, g, b, a = img.split()
    return Image.merge('RGBA', (r, g, b, a.point(lambda v: int(v * alpha))))


def radial_field(n, cx, cy, radius, stops):
    """RGB field of a radial gradient; stops are (t, rgb) with t in [0,1] of `radius`."""
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    t = np.clip(np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2) / radius, 0, 1)
    out = np.zeros((n, n, 3), dtype=np.float32)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        seg = (t >= t0) & (t <= t1)
        u = np.where(seg, (t - t0) / max(t1 - t0, 1e-6), 0)
        for c in range(3):
            out[:, :, c] = np.where(seg, c0[c] * (1 - u) + c1[c] * u, out[:, :, c])
    return out


def shaded(mask, field):
    """An RGBA layer: the gradient field painted through a greyscale mask."""
    rgb = Image.fromarray(np.clip(field, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')
    rgb.putalpha(mask)
    return rgb


def burst_polygon(s):
    pts = []
    for i in range(BURST_SPIKES * 2):
        angle = TILT + math.pi * i / BURST_SPIKES - math.pi / 2
        r = BURST_OUTER[i // 2] if i % 2 == 0 else BURST_INNER
        pts.append(s((CX + math.cos(angle) * r, CY + math.sin(angle) * r)))
    return pts


def star_polygon(s, cx, cy, r, waist, tilt=0.0):
    pts = []
    for i in range(8):
        angle = tilt + math.pi * i / 4 - math.pi / 2
        radius = r if i % 2 == 0 else waist
        pts.append(s((cx + math.cos(angle) * radius, cy + math.sin(angle) * radius)))
    return pts


def ray_polygon(s, angle, length, width):
    """A spark streak: starts inside the burst, widest a third of the way out, needle tip."""
    start, mid = 60, 60 + (length - 60) * 0.35
    dx, dy = math.cos(angle), math.sin(angle)
    nx, ny = -dy, dx
    return [
        s((CX + dx * start, CY + dy * start)),
        s((CX + dx * mid + nx * width, CY + dy * mid + ny * width)),
        s((CX + dx * length, CY + dy * length)),
        s((CX + dx * mid - nx * width, CY + dy * mid - ny * width)),
    ]


def bezier(p0, p1, p2, t):
    u = 1 - t
    return (u * u * p0[0] + 2 * u * t * p1[0] + t * t * p2[0],
            u * u * p0[1] + 2 * u * t * p1[1] + t * t * p2[1])


def corner_brackets():
    """L-shaped brass corner ornaments, hugging the frame's rounded corners (as LootView)."""
    lo, hi = FRAME_INSET, SIZE - FRAME_INSET
    out = []
    for (x, y, dx, dy) in [(lo, lo, 1, 1), (hi, lo, -1, 1), (lo, hi, 1, -1), (hi, hi, -1, -1)]:
        ox = x + dx * FRAME_RADIUS * 0.30
        oy = y + dy * FRAME_RADIUS * 0.30
        out.append(((ox, oy + dy * BRACKET), (ox, oy), (ox + dx * BRACKET, oy)))
    return out


def draw_sparkle(d, x, y, r):
    """A four-point mote, the shape of the crit sparks in the renderer."""
    waist = r * 0.16
    d.polygon([
        (x, y - r), (x + waist, y - waist), (x + r, y), (x + waist, y + waist),
        (x, y + r), (x - waist, y + waist), (x - r, y), (x - waist, y - waist),
    ], fill=GOLD_BRIGHT + (255,))
    d.ellipse([x - r * 0.2, y - r * 0.2, x + r * 0.2, y + r * 0.2], fill=(255, 255, 255, 255))


# ================================================================== render =====
def render_png(path, size=SIZE, ss=4):
    """Renders at `ss` times the target size, then downsamples for clean edges."""
    n = size * ss
    k = n / SIZE

    def s(xy):
        return (xy[0] * k, xy[1] * k)

    # --- panel: navy gradient, a cool top glow and the burst's warm light ----
    ramp = np.linspace(0.0, 1.0, n, dtype=np.float32)[:, None]
    base = np.zeros((n, n, 3), dtype=np.float32)
    for c in range(3):
        base[:, :, c] = INK_TOP[c] * (1 - ramp) + INK_BOTTOM[c] * ramp

    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    top_glow = np.clip(1.0 - ys / (n * 0.5), 0, 1) ** 2 * 0.30
    warm = np.clip(1.0 - np.sqrt((xs - CX * k) ** 2 + (ys - CY * k) ** 2) / (n * 0.62), 0, 1) ** 2.2
    for c in range(3):
        base[:, :, c] += CRYSTAL[c] * top_glow
        base[:, :, c] += EMBER[c] * warm * 0.62
        base[:, :, c] += FLARE[c] * warm ** 3 * 0.35

    panel = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), 'RGB').convert('RGBA')
    panel_mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(panel_mask).rounded_rectangle([0, 0, n - 1, n - 1], radius=CORNER * k, fill=255)
    panel.putalpha(panel_mask)
    out = panel

    # --- brass frame -------------------------------------------------------
    frame = layer(n)
    ImageDraw.Draw(frame).rounded_rectangle(
        [FRAME_INSET * k, FRAME_INSET * k, (SIZE - FRAME_INSET) * k, (SIZE - FRAME_INSET) * k],
        radius=FRAME_RADIUS * k, outline=GOLD + (255,), width=int(FRAME_WIDTH * k))
    out = Image.alpha_composite(out, fade(frame, 0.50))

    brackets = layer(n)
    bd = ImageDraw.Draw(brackets)
    for arm in corner_brackets():
        bd.line([s(arm[0]), s(arm[1]), s(arm[2])],
                fill=GOLD_BRIGHT + (255,), width=int((FRAME_WIDTH + 1.5) * k), joint='curve')
    out = Image.alpha_composite(out, fade(brackets, 0.78))

    # --- the incoming arc: a comet of ember that brightens as it lands ------
    trail = layer(n)
    td = ImageDraw.Draw(trail)
    steps = 120
    for i in range(steps):
        t = i / (steps - 1)
        x, y = s(bezier(*TRAIL, t))
        r = (3 + 15 * t ** 1.3) * k
        c = tuple(int(EMBER_DEEP[j] * (1 - t) + FLARE[j] * t) for j in range(3))
        td.ellipse([x - r, y - r, x + r, y + r], fill=c + (int(255 * (0.10 + 0.90 * t ** 1.4)),))
    # A soft glow under the comet, then the comet, then a hot core line along it.
    out = Image.alpha_composite(out, fade(trail.filter(ImageFilter.GaussianBlur(14 * k)), 0.9))
    out = Image.alpha_composite(out, trail)
    core_line = layer(n)
    cl = ImageDraw.Draw(core_line)
    for i in range(steps):
        t = i / (steps - 1)
        x, y = s(bezier(*TRAIL, t))
        r = (1 + 5 * t ** 1.5) * k
        cl.ellipse([x - r, y - r, x + r, y + r], fill=WHITE_HOT + (int(255 * t ** 2),))
    out = Image.alpha_composite(out, core_line)

    # --- spark rays between the burst's spikes ---------------------------
    rays_mask = Image.new('L', (n, n), 0)
    rd = ImageDraw.Draw(rays_mask)
    for offset, length, width in RAYS:
        angle = TILT + 2 * math.pi * offset / BURST_SPIKES - math.pi / 2
        rd.polygon(ray_polygon(s, angle, length, width), fill=255)
    ray_field = radial_field(n, CX * k, CY * k, 215 * k, [(0, WHITE_HOT), (0.45, FLARE), (1, EMBER)])
    rays = shaded(rays_mask, ray_field)
    # Rays fade toward their tips.
    tip_fade = np.clip(1.15 - np.sqrt((xs - CX * k) ** 2 + (ys - CY * k) ** 2) / (215 * k), 0, 1)
    ra = np.asarray(rays.getchannel('A'), dtype=np.float32) * tip_fade
    rays.putalpha(Image.fromarray(ra.astype(np.uint8), 'L'))
    out = Image.alpha_composite(out, rays)

    # --- the burst: scorched rim, then the ember body --------------------
    rim_mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(rim_mask).polygon(burst_polygon(s), fill=255, outline=255, width=int(7 * k))
    out = Image.alpha_composite(out, fade(shaded(rim_mask, np.full((n, n, 3), SCORCH, np.float32)), 0.85))

    body_mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(body_mask).polygon(burst_polygon(s), fill=255)

    # Bloom: the burst's own light spilling onto the panel.
    bloom = shaded(body_mask, np.full((n, n, 3), EMBER, np.float32)).filter(ImageFilter.GaussianBlur(26 * k))
    out = Image.alpha_composite(out, fade(bloom, 0.75))
    body_field = radial_field(n, CX * k, CY * k, 150 * k,
                              [(0, WHITE_HOT), (0.28, FLARE), (0.62, EMBER), (1, EMBER_DEEP)])
    out = Image.alpha_composite(out, shaded(body_mask, body_field))

    # --- the four-point flare over it, and the white-hot core -------------
    star_mask = Image.new('L', (n, n), 0)
    ImageDraw.Draw(star_mask).polygon(
        star_polygon(s, CX, CY, STAR_RADIUS, STAR_WAIST, TILT), fill=255)
    star_field = radial_field(n, CX * k, CY * k, STAR_RADIUS * k, [(0, WHITE_HOT), (0.55, WHITE_HOT), (1, FLARE)])
    out = Image.alpha_composite(out, fade(shaded(star_mask, star_field), 0.95))

    core = layer(n)
    for r, a in [(46, 0.18), (34, 0.35), (22, 0.7), (13, 1.0)]:
        cd_layer = layer(n)
        ImageDraw.Draw(cd_layer).ellipse(
            [(CX - r) * k, (CY - r) * k, (CX + r) * k, (CY + r) * k], fill=WHITE_HOT + (255,))
        core = Image.alpha_composite(core, fade(cd_layer, a))
    out = Image.alpha_composite(out, core)

    # --- motes and embers ----------------------------------------------------
    for (x, y, r, a) in SPARKLES:
        star = layer(n)
        draw_sparkle(ImageDraw.Draw(star), x * k, y * k, r * k)
        out = Image.alpha_composite(out, fade(star, a))

    dots = layer(n)
    dd = ImageDraw.Draw(dots)
    for (x, y, r) in DOTS:
        dd.ellipse([(x - r) * k, (y - r) * k, (x + r) * k, (y + r) * k], fill=FLARE + (255,))
    out = Image.alpha_composite(out, fade(dots, 0.85))

    # Keep everything inside the rounded panel.
    out.putalpha(Image.fromarray(np.minimum(np.asarray(out.getchannel('A')), np.asarray(panel_mask)), 'L'))

    icon = out.resize((size, size), Image.LANCZOS)
    icon.save(path)
    return icon


if __name__ == '__main__':
    render_png('icon.png')
    print('icon.png written')
