"""Painted-head pipeline, step 2: pixel art painted on the flat views of a room player head.

Reads Temp/RoomHead/views/<who>_<view>.png (rendered by Tools/room_head_views.eval.cs, Over times finer than the
painting grid) and writes Temp/RoomHead/paint/<who>_<view>.png (the painting at grid size, clear where there is no head)
and <who>_<view>_cls.png (red: 0 none, 1 skin, 2 hair and the rest, 3 eye, 4 brow).

Every texel is decided by what covers most of its block at full size, never by averaging two parts, so skin, hair, eyes
and brows keep clean edges. Skin and hair take a few flat tones each (their own palettes). Each eye is drawn last, as
pixel art: the white over the eye's own opening, a round iris where the Meshy iris is, a one-texel pupil, a lash line
along the top; nothing else is drawn over an eye texel. Brows are the Meshy painting's dark marks above the eyes,
tidied of specks, in a dark shade of the hair.

    python Tools/room_head_paint.py woman man
"""
import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

OVER = 8
BROW_ROWS = {"man": 2}   # brow depth limit per head (hers is as approved)
EYE_ROWS = {"man": 2}    # his tired lids leave a narrow opening: two rows of eye
EYE_DROP = {"woman": 1}  # her upper lids bulge over the eye's top row: the eye sits a row lower
VIEWS = ["front", "right", "back", "left", "top"]
os.makedirs("Temp/RoomHead/paint", exist_ok=True)


def kmeans(pts, k, seed=3, iters=25):
    pts = pts.astype(np.float32)
    rng = np.random.default_rng(seed)
    sub = pts[rng.choice(len(pts), min(len(pts), 40000), replace=False)]
    cent = [sub[rng.integers(len(sub))]]
    for _ in range(k - 1):
        d = np.min(((sub[:, None] - np.array(cent)[None]) ** 2).sum(-1), 1)
        cent.append(sub[rng.choice(len(sub), p=d / d.sum())] if d.sum() > 0 else sub[0])
    cent = np.array(cent)
    for _ in range(iters):
        lab = ((sub[:, None] - cent[None]) ** 2).sum(-1).argmin(1)
        for i in range(k):
            if (lab == i).any():
                cent[i] = sub[lab == i].mean(0)
    return cent


def nearest(pal, c):
    return pal[((pal[None] - c[:, None]) ** 2).sum(-1).argmin(1)]


def classify(img):
    """Full-size classes: 0 none, 1 skin, 2 hair/other, 3 eye white, 5 eye dark (iris, pupil), 4 brow."""
    rgb = img[..., :3]
    bg = (rgb[..., 0] > .9) & (rgb[..., 1] < .1) & (rgb[..., 2] > .9)
    lum = rgb.mean(-1); sat = rgb.max(-1) - rgb.min(-1)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    chrom = rgb / (rgb.sum(-1, keepdims=True) + 1e-6)
    skin0 = ~bg & (r > g * 1.02) & (g > b * .95) & (lum > .3)
    med = np.median(chrom[skin0], 0) if skin0.any() else np.array([.4, .33, .27])
    skin = ~bg & (np.abs(chrom - med).sum(-1) < .07) & (lum > .25)
    skin = ndimage.binary_opening(skin, iterations=1) | (skin & ndimage.binary_erosion(skin, iterations=0))
    cls = np.zeros(lum.shape, np.uint8); cls[~bg] = 2; cls[skin] = 1
    white = ~bg & (lum > .62) & (sat < .14)
    # eyes: white blobs that sit in the skin; an eye is the hull of its white blobs (white either side of the iris)
    lab, n = ndimage.label(white)
    eyes = []
    if n:
        sizes = ndimage.sum(white, lab, range(1, n + 1)); objs = ndimage.find_objects(lab)
        blobs = []
        for i in range(n):
            if sizes[i] < 6: continue
            sl = objs[i]; m = lab[sl] == i + 1
            ring = ndimage.binary_dilation(lab == i + 1, iterations=6) & ~(lab == i + 1)
            if ring.sum() == 0 or (cls[ring] == 1).mean() < .35: continue
            sy = np.nonzero(skin)[0]
            if len(sy) and (sl[0].start + sl[0].stop) / 2 > sy.min() + .5 * (sy.max() - sy.min()): continue   # eyes are on the upper face
            blobs.append(((sl[0].start + sl[0].stop) / 2, (sl[1].start + sl[1].stop) / 2, sl))
        # group blobs into eyes by distance
        used = set()
        for i, (yi, xi, sli) in enumerate(blobs):
            if i in used: continue
            group = [sli]; used.add(i)
            for j, (yj, xj, slj) in enumerate(blobs):
                if j in used: continue
                if abs(yj - yi) < 3 * OVER and abs(xj - xi) < 9 * OVER: group.append(slj); used.add(j)
            y0 = min(s[0].start for s in group); y1 = max(s[0].stop for s in group); x0 = min(s[1].start for s in group); x1 = max(s[1].stop for s in group)
            eyes.append((y0, y1, x0, x1))
    eyemask = np.zeros(lum.shape, bool); eyeinfo = []
    for (y0, y1, x0, x1) in eyes:
        pad = OVER // 2
        ys, xs = slice(max(0, y0 - pad), y1 + pad), slice(max(0, x0 - pad), x1 + pad)
        box = np.zeros(lum.shape, bool); box[ys, xs] = True
        opening = box & ~bg & ((cls != 1) | white) & ~((lum < .12) & ~white & False)
        opening &= ~ndimage.binary_dilation(cls == 1, iterations=0) | white
        dark = opening & ~white & (lum < .5)
        if (opening.sum() < 4): continue
        eyemask |= opening
        cy, cx = ndimage.center_of_mass(dark) if dark.any() else ((y0 + y1) / 2, (x0 + x1) / 2)
        diam = 2 * np.sqrt(dark.sum() / np.pi) if dark.any() else (y1 - y0)
        eyeinfo.append((cy, cx, diam, white[box].any()))
    cls[eyemask] = 3
    # brows: dark marks in the band above each eye, within the face
    for (y0, y1, x0, x1) in eyes:
        w = x1 - x0
        by0, by1 = int(max(0, y0 - 1.0 * w)), int(y0 - .1 * w)
        bx0, bx1 = int(max(0, x0 - .35 * w)), int(x1 + .35 * w)
        if by1 <= by0: continue
        reg = np.zeros(lum.shape, bool); reg[by0:by1, bx0:bx1] = True
        skinlum = np.median(lum[skin]) if skin.any() else .6
        dk = reg & ~bg & (cls == 2) & (lum < skinlum * .62)
        lab2, n2 = ndimage.label(dk)
        for i in range(1, n2 + 1):
            m = lab2 == i
            whole = ndimage.binary_dilation(m, iterations=1) & (cls == 2) & ~reg
            if whole.sum() > m.sum() * .5: continue   # part of the hair mass, not a brow
            if m.sum() < OVER * OVER * .6: continue
            cls[m] = 4
    return cls, bg, eyeinfo, eyes


for who in sys.argv[1:]:
    imgs = {v: np.array(Image.open(f"Temp/RoomHead/views/{who}_{v}.png").convert("RGB")).astype(np.float32) / 255 for v in VIEWS}
    G = imgs["front"].shape[0] // OVER
    data = {v: classify(imgs[v]) for v in VIEWS}
    # palettes from every view: skin 5 tones, hair and the rest 5 tones
    skinpx = np.concatenate([imgs[v][data[v][0] == 1] for v in VIEWS]); hairpx = np.concatenate([imgs[v][data[v][0] == 2] for v in VIEWS])
    skinPal = kmeans(skinpx, 5); hairPal = kmeans(hairpx, 5)
    hairDark = hairPal[np.argmin(hairPal.mean(1))]
    browC = hairDark * .85; lashC = hairDark * .6
    white = np.array([1., 1., 1.]); pupil = np.array([.08, .06, .05])
    fimg = imgs['front']; fcls = data['front'][0]; flum = fimg.mean(-1); fsat = fimg.max(-1) - fimg.min(-1)
    irisPx = fimg[(fcls == 3) & (flum > .15) & (flum < .55) & (fsat > .05)]
    irisC = np.median(irisPx, 0) if len(irisPx) > 10 else np.array([.4, .3, .2])
    print(who, 'iris', (irisC * 255).round().astype(int).tolist())
    for v in VIEWS:
        img = imgs[v]; cls, bg, eyeinfo, eyeboxes = data[v]
        out = np.zeros((G, G, 4), np.float32); oc = np.zeros((G, G), np.uint8)
        for y in range(G):
            for x in range(G):
                blk = cls[y * OVER:(y + 1) * OVER, x * OVER:(x + 1) * OVER].ravel(); n = len(blk)
                cnt = np.bincount(blk, minlength=5)
                if cnt[0] > n * .5: continue
                if cnt[3] >= n * .4: c = 3
                elif cnt[4] >= n * .35: c = 4
                else: c = 1 if cnt[1] >= cnt[2] else 2
                px = img[y * OVER:(y + 1) * OVER, x * OVER:(x + 1) * OVER].reshape(-1, 3)[blk == c] if c in (1, 2) else None
                col = np.median(px, 0) if px is not None and len(px) else (hairDark if c == 2 else skinPal[0])
                out[y, x, :3] = col; out[y, x, 3] = 1; oc[y, x] = {1: 1, 2: 2, 3: 3, 4: 4}[c]
        # flat tones
        m1 = oc == 1; out[m1, :3] = nearest(skinPal, out[m1, :3])
        m2 = oc == 2; out[m2, :3] = nearest(hairPal, out[m2, :3])
        # brows: tidy, then colour
        for _ in range(2):
            bm = oc == 4; nb = sum(np.roll(np.roll(bm, dy, 0), dx, 1) for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)))
            gone = bm & (nb < 1); oc[gone] = 1; out[gone, :3] = nearest(skinPal, out[gone, :3])
        # his brows: at most BROW_ROWS texels deep in each column, the ones nearest the eye (bumps on top are gone)
        if who in BROW_ROWS:
            for x in range(G):
                rows = np.nonzero(oc[:, x] == 4)[0]
                if len(rows) > BROW_ROWS[who]:
                    for y in rows[:-BROW_ROWS[who]]:
                        oc[y, x] = 1; out[y, x, :3] = nearest(skinPal, out[y:y + 1, x, :3])[0]
        out[oc == 4, :3] = browC
        # eyes, drawn last as pixel art where the Meshy eye is, nothing over them (three rows; two for his tired lids):
        #   lash: . L L L L L L L
        #   top:  . W W I I W W L      I iris (2 x 2)
        #   mid:  W W W P I W W W      P pupil
        #   low:  . W W W W W W .      corners cut evenly
        #   lid:  . s s s s s s .      soft lid shadow
        # columns run from the inner corner (toward the face's middle) outward
        oc[oc == 3] = 1
        skinX = np.nonzero(oc == 1)[1]; midX = skinX.mean() if len(skinX) else G / 2
        for (cy, cx, diam, _), (y0, y1, x0, x1) in zip(eyeinfo, eyeboxes):
            bw_ = (x1 - x0) / OVER; bh_ = (y1 - y0) / OVER
            w = int(np.clip(round(bw_ * .75), 6, 8)); R = EYE_ROWS.get(who, 3 if bh_ >= 2.5 else 2)
            gxc = (x0 + x1) / 2 / OVER; gyc = (y0 + y1) / 2 / OVER
            gx0 = int(round(gxc - w / 2)); top = int(round(gyc - R / 2)) + EYE_DROP.get(who, 0)
            if gxc < midX: inner, step = gx0 + w - 1, -1
            else: inner, step = gx0, 1
            ic = (w - 3) // 2
            # the old eye round the new one (lids and iris painted by Meshy) becomes skin first
            for row in range(top - 2, top + R + 2):
                for col in range(-2, w + 2):
                    x = inner + col * step
                    if 0 <= x < G and 0 <= row < G and oc[row, x] in (2, 3, 4):
                        nb = [out[yy_, xx_, :3] for yy_, xx_ in ((top - 3, x), (top + R + 2, x)) if 0 <= yy_ < G and oc[yy_, xx_] == 1]
                        out[row, x, :3] = nb[0] if nb else skinPal[2]; out[row, x, 3] = 1; oc[row, x] = 1
            def put(col, row, c, k):
                x = inner + col * step
                if 0 <= x < G and 0 <= row < G: out[row, x, :3] = c; out[row, x, 3] = 1; oc[row, x] = k
            # an almond framed top and bottom: a dark lash line over it (one texel longer at the outer end), pure white
            # with its corners cut evenly, a compact 2 x 2 iris with the pupil in it (it reads round at this size), and
            # a soft lid shadow under it, so the white stands out from light skin
            ic = (w - 2) // 2
            for col in range(1, w + 1): put(col if col < w else w - 1, top - 1 if col < w else top, lashC, 3)
            for r in range(R):
                cut = 1 if r == 0 or r == R - 1 else 0
                if R == 2: cut = 1 if r == 0 else 1
                for col in range(cut, w - cut): put(col, top + r, white, 3)
            for r in range(min(2, R)):
                rr = top + (r if R == 2 else r)
                put(ic, rr, irisC, 3); put(ic + 1, rr, irisC, 3)
            put(ic + (1 if step > 0 else 0), top + (1 if R >= 2 else 0), pupil, 3)
            lid = np.clip(skinPal[np.argsort(skinPal.mean(1))[1]] * .92, 0, 1)
            for col in range(1, w - 1): put(col, top + R, lid, 3)
        Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), "RGBA").save(f"Temp/RoomHead/paint/{who}_{v}.png")
        Image.fromarray(np.stack([oc] * 3, -1)).save(f"Temp/RoomHead/paint/{who}_{v}_cls.png")
        print(who, v, "grid", G, "classes", np.bincount(oc.ravel(), minlength=5).tolist(), "eyes", len(eyeinfo))
