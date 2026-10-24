"""Flat swatches for a room player head, the way a Synty atlas is laid out, so PixelRestyle.GradeTexel can grade it like
the restyled body: skin is marked pure magenta (the grading puts the body's own Synty skin colour there), everything
else (hair, brows, eyes, lips that are not skin-coloured) takes one of a few flat colours. Specks of one inside the
other, and skin thinner than a few pixels lying on the hair, are merged into what surrounds them.

    python Tools/room_head_swatches.py <head bake png> <out swatch png> [k]
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, dst = sys.argv[1], sys.argv[2]
K = int(sys.argv[3]) if len(sys.argv) > 3 else 6


def chroma(c):
    s = c[..., :3].sum(-1, keepdims=True) + 1e-6
    return c[..., :2] / s


def skinish(c):
    r, g, b = c[..., 0], c[..., 1], c[..., 2]
    return (r > g * 1.04) & (g > b * .98) & (r > b * 1.1) & (c[..., :3].mean(-1) > .3)


h = np.array(Image.open(src).convert("RGB")).astype(np.float32) / 255
# texels the bake's short rays missed come out pure black: fill them from the nearest baked pixel
miss = h.sum(-1) == 0
if miss.any() and (~miss).any():
    idx = ndimage.distance_transform_edt(miss, return_distances=False, return_indices=True)
    h = h[idx[0], idx[1]]
s0 = skinish(h); med = np.median(chroma(h)[s0], 0)
skin = s0 & (np.abs(chroma(h) - med).sum(-1) < .045)
lum = h.mean(-1)
skin0 = skin.copy()
skin |= (np.abs(chroma(h) - med).sum(-1) < .1) & (lum > .2) & (lum < np.median(lum[skin]) * 1.02) & (h[..., 0] >= h[..., 2])
# clean the class map at full size: small islands of either class merge into the other, thin skin on hair is hair
scale = (h.shape[0] / 1024.) ** 2
for want, t in ((True, 60), (False, 400)):
    lab, n = ndimage.label(skin != want)
    sizes = ndimage.sum(np.ones_like(lab), lab, range(1, n + 1))
    small = np.isin(lab, np.nonzero(sizes < t * scale)[0] + 1)
    skin[small] = want
skin = ndimage.binary_opening(skin, structure=np.ones((9, 9), bool)) | (skin & ndimage.binary_erosion(skin, iterations=1) & False)
# eye whites keep their own swatch, outside the skin and the clustering
white = (h.mean(-1) > .6) & (h.max(-1) - h.min(-1) < .15)
skin &= ~white
rest = h[~skin & ~white]
rng = np.random.default_rng(3); sub = rest[rng.choice(len(rest), min(len(rest), 60000), replace=False)]
cent = [sub[rng.integers(len(sub))]]
for _ in range(K - 1):
    d = np.min(((sub[:, None] - np.array(cent)[None]) ** 2).sum(-1), 1); cent.append(sub[rng.choice(len(sub), p=d / d.sum())])
cent = np.array(cent)
for _ in range(25):
    lab = ((sub[:, None] - cent[None]) ** 2).sum(-1).argmin(1)
    for i in range(K):
        m = lab == i
        if m.any():
            cent[i] = sub[m].mean(0)
lab = np.concatenate([((rest[i:i + 200000, None] - cent[None]) ** 2).sum(-1).argmin(1) for i in range(0, len(rest), 200000)])
full = np.full(h.shape[:2], -1, np.int32); full[~skin & ~white] = lab
# skin-coloured swatches outside the skin (lit skin at its edge, pale highlights painted in the hair): next to skin they
# are skin, out in the hair they take the hair swatch most common round them
skinlike = [i for i in range(K) if skinish(cent[i][None])[0] or (cent[i][0] > cent[i][2] * 1.15 and cent[i].mean() > .45)]
if skinlike:
    stray = np.isin(full, skinlike)
    dist = ndimage.distance_transform_edt(~skin)
    near = stray & (dist <= 8 * (h.shape[0] / 1024.))
    near &= ndimage.distance_transform_edt(~white) > 14 * (h.shape[0] / 1024.)   # the irises beside the eye whites stay
    skin |= near; full[near] = -1
    far = stray & ~near
    if far.any():
        ok = (full >= 0) & ~np.isin(full, skinlike)
        idx = ndimage.distance_transform_edt(~ok, return_distances=False, return_indices=True)
        full[far] = full[idx[0][far], idx[1][far]]
print("skin-like swatches", [(cent[i] * 255).round().astype(int).tolist() for i in skinlike])
out = np.zeros_like(h); out[skin] = (1, 0, 1); m = full >= 0; out[m] = cent[full[m]]
lab_w, nw = ndimage.label(white)
eyes = np.zeros(white.shape, bool)
for i in range(1, nw + 1):
    m_ = lab_w == i; area = m_.sum()
    if area < 15 * scale or area > 3000 * scale: continue
    ring = ndimage.binary_dilation(m_, iterations=int(6 * h.shape[0] / 1024) + 1) & ~m_
    if skin0[ring].mean() > .45: eyes |= ndimage.binary_dilation(m_, iterations=int(16 * h.shape[0] / 1024) + 1)
eyes &= ~(h.mean(-1) < .12) | white | eyes
# the eye itself keeps the bake's colours; skin in the eye zone that joins the face's skin round it is the body's swatch,
# skin-coloured bits inside the eye (in the iris) stay the eye's
lab_e, ne = ndimage.label(eyes & skin0); edge = ndimage.binary_dilation(eyes) & ~eyes
face_skin = np.zeros(eyes.shape, bool)
for i in range(1, ne + 1):
    m_ = lab_e == i
    if (ndimage.binary_dilation(m_) & edge & skin).any(): face_skin |= m_
face_skin &= h.mean(-1) > np.median(h.mean(-1)[skin0]) * .8   # iris pixels are much darker than skin
keep = eyes & ~face_skin
# each eye drawn with three clean colours like a hand-placed pixel eye: the white, the iris, dark (pupil and lashes);
# anything nearer the skin is skin; a majority pass then tidies the edges so nothing smears into the white or the face
if keep.any():
    # the iris is the greener pixels (olive or hazel); the eyelid's lash line is brown like the hair and joins the dark
    irisPix = keep & ~white & (h[..., 1] > h[..., 0] * .74) & (h.mean(-1) > .15)
    irisC = np.median(h[irisPix], 0) if irisPix.sum() > 20 else np.median(h[keep & ~white & (h.mean(-1) > .18)], 0)
    dk = h[keep & ~white & ~irisPix]
    darkC = dk[dk.mean(-1) <= np.percentile(dk.mean(-1), 20)].mean(0) if len(dk) else np.array([.05, .04, .04])   # the pupil: the darkest of the eye
    pal = np.array([h[white].mean(0), irisC, darkC, np.median(h[skin0], 0)])
    lab_k = ((h[..., None, :] - pal[None, None]) ** 2).sum(-1).argmin(-1)
    lab_k[~keep] = 3
    for _ in range(2):
        votes = np.stack([ndimage.uniform_filter((lab_k == i).astype(float), size=3) for i in range(4)], -1)
        lab_k = np.where(keep, votes.argmax(-1), lab_k)
    for i in range(3):
        m_ = keep & (lab_k == i); out[m_] = pal[i]
    sk_ = keep & (lab_k == 3); out[sk_] = (1, 0, 1)
    print('eye colours', (pal[:3] * 255).round().astype(int).tolist())
out[face_skin] = (1, 0, 1)
print('eye pixels kept', int(eyes.sum()))
if white.any(): out[white & ~skin] = h[white].mean(0)
Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(dst)
print(dst, "skin", round(float(skin.mean()), 3), "swatches", (cent * 255).round().astype(int).tolist())

# ── at the final texel size ──
# With a size given, also write <out>_small.png (colours) and <out>_cls.png (class in red: 0 skin, 1 hair and the rest,
# 2 eye white, 3 iris, 4 pupil, 5 brow), each texel decided by class, never by averaging: eyes win when they cover
# 30% of a texel's block, brows when they cover 35%, else the larger of skin and the rest. Brows are dark marks enclosed
# by skin whose colour is nearer the hair than the skin; they get a darker shade of the hair, tidied so no jag or speck is
# left. The eye white is toned down so it doesn't glow next to graded skin.
if len(sys.argv) > 4:
    N = int(sys.argv[4]); H0, W0 = h.shape[:2]
    cls = np.ones((H0, W0), np.uint8); cls[skin] = 0
    if keep.any():
        for i, c in ((0, 2), (1, 3), (2, 4)): cls[keep & (lab_k == i)] = c
    cls[face_skin] = 0
    if keep.any(): cls[keep & (lab_k == 3)] = 0   # skin-coloured bits inside the eye zone are skin
    # brows: non-skin, non-eye islands enclosed by skin, nearer the hair colour than the skin, not big like a beard
    hairC = cent[np.argmax(np.bincount(full[full >= 0], minlength=K))] if (full >= 0).any() else np.array([.3, .2, .15])
    mark = cls == 1; lab_m, nm = ndimage.label(mark)
    skinC = np.median(h[skin0], 0)
    for i in range(1, nm + 1):
        m_ = lab_m == i; a_ = m_.sum()
        if a_ < 20 * scale or a_ > 2500 * scale: continue
        ring = ndimage.binary_dilation(m_, iterations=3) & ~m_
        if (cls[ring] == 0).mean() < .6: continue
        col = h[m_].mean(0)
        if ((col - hairC) ** 2).sum() < ((col - skinC) ** 2).sum(): cls[m_] = 5
    br_ = cls == 5; br_ = ndimage.binary_closing(br_, structure=np.ones((5, 5))) & ~(cls == 0) | br_; cls[br_ & (cls != 2) & (cls != 3) & (cls != 4)] = 5
    browC = hairC * .7
    # eye colours a pixel artist would pick: a warm off-white, an iris clearly darker than it, a near-black pupil
    irisV = pal[1] * min(1., .42 / max(pal[1].mean(), 1e-3)) if keep.any() else None
    eyeC = {2: np.array([.86, .82, .76]), 3: irisV, 4: np.array([.09, .06, .05])} if keep.any() else {}
    sc = np.zeros((N, N), np.uint8); col_ = np.zeros((N, N, 3), np.float32)
    for y in range(N):
        y0, y1 = y * H0 // N, max(y * H0 // N + 1, (y + 1) * H0 // N)
        for x in range(N):
            x0, x1 = x * W0 // N, max(x * W0 // N + 1, (x + 1) * W0 // N)
            b = cls[y0:y1, x0:x1].ravel(); n = len(b); cnt = np.bincount(b, minlength=6)
            if cnt[2:5].sum() >= .3 * n: c = 2 + int(np.argmax(cnt[2:5]))
            elif cnt[5] >= .35 * n: c = 5
            else: c = 0 if cnt[0] >= cnt[1] else 1
            sc[y, x] = c
            if c == 1:
                ob = out[y0:y1, x0:x1].reshape(-1, 3)[b == 1]; col_[y, x] = ob[len(ob) // 2] if len(ob) else hairC
    sc_before = sc.copy()
    for _ in range(2):   # brows: drop texels with fewer than two brow neighbours, fill gaps with three or more
        bm = sc == 5; nb = sum(np.roll(np.roll(bm, dy, 0), dx, 1) for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)))
        sc[bm & (nb < 2)] = 0; sc[(sc == 0) & (nb >= 3)] = 5
    # each eye drawn as pixel art at this size: its whole island white, an iris round the darkest middle, a pupil in it
    eyeM = (sc >= 2) & (sc <= 4); lab_e2, ne2 = ndimage.label(eyeM, structure=np.ones((3, 3)))
    sizes_e = ndimage.sum(np.ones_like(lab_e2), lab_e2, range(1, ne2 + 1)) if ne2 else []
    real = set(range(1, ne2 + 1))   # which of these are the eyes is decided on the head in 3D (room_head_grade.eval.cs)
    for i in range(1, ne2 + 1):
        m_ = lab_e2 == i; ys_, xs_ = np.nonzero(m_)
        if i not in real or len(ys_) < 3:   # pale bits elsewhere (lids, brows): the class most common round them
            ring = ndimage.binary_dilation(m_, iterations=1) & ~eyeM
            sc[m_] = np.bincount(sc[ring], minlength=6).argmax() if ring.any() else 0
            if sc[m_][0] == 1: col_[m_] = col_[ring & (sc == 1)][0] if (ring & (sc == 1)).any() else hairC
            continue
        sc[m_] = 2
        dark = m_ & np.isin(sc_before, [3, 4])
        cy, cx = (np.mean(np.nonzero(dark)[0]), np.mean(np.nonzero(dark)[1])) if dark.any() else (ys_.mean(), xs_.mean())
        r_ = max(1.0, .3 * np.sqrt(len(ys_)))
        yy_, xx_ = np.mgrid[0:N, 0:N]; d2 = (yy_ - cy) ** 2 + (xx_ - cx) ** 2
        sc[m_ & (d2 <= r_ * r_ + .5)] = 3
        py, px = int(round(cy)), int(round(cx))
        if m_[py, px]: sc[py, px] = 4
    for c, v in eyeC.items(): col_[sc == c] = v
    col_[sc == 5] = browC; col_[sc == 0] = (1, 0, 1)
    base = dst[:-4]
    Image.fromarray((np.clip(col_, 0, 1) * 255).astype(np.uint8)).save(base + "_small.png")
    Image.fromarray(np.stack([sc, sc, sc], -1)).save(base + "_cls.png")
    print("small", N, "classes", np.bincount(sc.ravel(), minlength=6).tolist())
