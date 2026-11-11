"""Fewer triangles on the campaign map's models and ground without changing how they look.

The models on the map are a few centimetres tall and are drawn four times a frame (twice for the picture, once into each
of two shadow maps), so every triangle that can go saves four. Nothing here touches a texture, a UV or a material: a
model keeps every texel where it was.

  1. Hidden triangles go: one whose front can't be seen from anywhere at or above the horizon (sunk inside another
     part, or an underside) is never drawn to the screen, nor into a shadow map from a light overhead.
  2. Edges are collapsed onto one of their own ends, so every corner keeps a UV the model already had, and only where
     nothing shows:
       - the surface stays within --eps of every point it had (units on the table at the largest size the model stands at)
       - a flat-shaded face may lean at most --facet-deg from how it began (its light is its slope)
       - a smooth-shaded spot keeps its light: the normal the new triangle gives it turns at most --normal-deg
       - a corner on the rim of a UV island only slides along that rim, where the rim is straight in the texture
       - a corner on the rim of a hole (where hidden triangles went) only slides along it, where it is straight
     What is left of a low-poly model after that is mostly its own facets, so the saving is modest on trees and large
     on houses (their insides) and on the ground (its plains).

Run from Unity: CampaignMapBuilder.ReduceTriangles() (Table map/Scripts/Editor/CampaignMapBuilder.Triangles.cs) writes
the meshes to Library/MapReduce, runs `reduce` here, draws every result beside its original from eight sides at twice
the closest a player gets, runs `choose` here (a result is taken only if it looks the same in those pictures), and
writes the taken meshes back into their assets.

    python Tools/map_mesh_reduce.py reduce <folder>
    python Tools/map_mesh_reduce.py choose <folder> [--limit 7]
    python Tools/map_mesh_reduce.py one <in.bin> <out.bin> --scale <size> [--eps .04] [--facet-deg 20] [--ground]

The .bin files: int32 vertex count, index count, has-colour; float32 positions, normals, UVs; colours (4 bytes); int32 indices.
"""
import argparse, heapq, math, os, shutil, struct, sys
import numpy as np
from numba import njit

# A model is worth doing when it has this many triangles, and this many over all its copies on the map.
MIN_TRIANGLES = 200
MIN_TRIANGLES_ON_MAP = 4000
# The two tries per model: the usual one, and a tighter one for a model the usual one shows on.
TRIES = (("A", .04, 20.0), ("B", .02, 12.0))
GROUND_EPS = .02          # the road lies 0.08 over the ground, so the ground may not move more than a fraction of that


def load(path):
    b = open(path, "rb").read()
    nv, ni, hc = struct.unpack_from("<iii", b, 0); o = 12
    P = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(np.float64); o += nv * 12
    N = np.frombuffer(b, "<f4", nv * 3, o).reshape(nv, 3).astype(np.float64); o += nv * 12
    UV = np.frombuffer(b, "<f4", nv * 2, o).reshape(nv, 2).astype(np.float64); o += nv * 8
    C = None
    if hc: C = np.frombuffer(b, "u1", nv * 4, o).reshape(nv, 4).copy(); o += nv * 4
    T = np.frombuffer(b, "<i4", ni, o).reshape(-1, 3).astype(np.int64)
    return P, N, UV, C, T


def save(path, P, N, UV, C, T):
    with open(path, "wb") as f:
        f.write(struct.pack("<iii", len(P), T.size, 1 if C is not None else 0))
        f.write(P.astype("<f4").tobytes()); f.write(N.astype("<f4").tobytes()); f.write(UV.astype("<f4").tobytes())
        if C is not None: f.write(C.astype("u1").tobytes())
        f.write(T.astype("<i4").tobytes())


# ── Step 1: what can be seen ──
@njit(cache=True)
def _seen(P, T, dirs, res, centre, radius):
    """The triangles that show in at least one picture of the model, drawn from each direction with a depth buffer."""
    m = T.shape[0]; seen = np.zeros(m, np.uint8)
    zbuf = np.empty((res, res)); ids = np.empty((res, res), np.int32)
    X = np.empty(P.shape[0]); Y = np.empty(P.shape[0]); Z = np.empty(P.shape[0])
    s = res / (2 * radius * 1.02)
    for k in range(dirs.shape[0]):
        d = dirs[k]
        ux, uy, uz = 0.0, 1.0, 0.0
        if abs(d[1]) > .99: ux, uy, uz = 1.0, 0.0, 0.0
        rx = uy * d[2] - uz * d[1]; ry = uz * d[0] - ux * d[2]; rz = ux * d[1] - uy * d[0]
        rl = math.sqrt(rx * rx + ry * ry + rz * rz); rx /= rl; ry /= rl; rz /= rl
        vx = d[1] * rz - d[2] * ry; vy = d[2] * rx - d[0] * rz; vz = d[0] * ry - d[1] * rx
        for i in range(P.shape[0]):
            px = P[i, 0] - centre[0]; py = P[i, 1] - centre[1]; pz = P[i, 2] - centre[2]
            X[i] = (px * rx + py * ry + pz * rz) * s + res * .5
            Y[i] = (px * vx + py * vy + pz * vz) * s + res * .5
            Z[i] = px * d[0] + py * d[1] + pz * d[2]
        zbuf[:, :] = -1e30; ids[:, :] = -1
        for t in range(m):
            a = T[t, 0]; b = T[t, 1]; c = T[t, 2]
            ex = P[b, 0] - P[a, 0]; ey = P[b, 1] - P[a, 1]; ez = P[b, 2] - P[a, 2]
            fx = P[c, 0] - P[a, 0]; fy = P[c, 1] - P[a, 1]; fz = P[c, 2] - P[a, 2]
            nx = ey * fz - ez * fy; ny = ez * fx - ex * fz; nz = ex * fy - ey * fx
            if nx * d[0] + ny * d[1] + nz * d[2] <= 0: continue   # its back is to the eye (the materials cull back faces)
            x0 = X[a]; y0 = Y[a]; x1 = X[b]; y1 = Y[b]; x2 = X[c]; y2 = Y[c]
            area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
            if area == 0: continue
            lo_x = max(0, int(math.floor(min(x0, x1, x2)))); hi_x = min(res - 1, int(math.ceil(max(x0, x1, x2))))
            lo_y = max(0, int(math.floor(min(y0, y1, y2)))); hi_y = min(res - 1, int(math.ceil(max(y0, y1, y2))))
            for py_ in range(lo_y, hi_y + 1):
                cy = py_ + .5
                for px_ in range(lo_x, hi_x + 1):
                    cx = px_ + .5
                    w0 = ((x1 - cx) * (y2 - cy) - (x2 - cx) * (y1 - cy)) / area
                    w1 = ((x2 - cx) * (y0 - cy) - (x0 - cx) * (y2 - cy)) / area
                    w2 = 1 - w0 - w1
                    if w0 < 0 or w1 < 0 or w2 < 0: continue
                    z = w0 * Z[a] + w1 * Z[b] + w2 * Z[c]
                    if z > zbuf[py_, px_]: zbuf[py_, px_] = z; ids[py_, px_] = t
        for py_ in range(res):
            for px_ in range(res):
                if ids[py_, px_] >= 0: seen[ids[py_, px_]] = 1
    return seen


def view_directions(n, lowest_deg):
    """Directions to the eye, spread evenly over the sky down to a little under the horizon."""
    out = []; g = math.pi * (3 - math.sqrt(5)); lo = math.sin(math.radians(lowest_deg))
    for i in range(n):
        y = 1 - (i + .5) / n * (1 - lo); r = math.sqrt(max(0, 1 - y * y)); a = i * g
        out.append((math.cos(a) * r, y, math.sin(a) * r))
    return np.array(out)


def toward(up):
    """A rotation that takes +Y to the given direction."""
    up = np.array(up, float); up /= np.linalg.norm(up)
    a = np.array([1.0, 0, 0]) if abs(up[1]) > .9 else np.array([0, 1.0, 0])
    x = np.cross(a, up); x /= np.linalg.norm(x); z = np.cross(x, up)
    return np.stack([x, up, z], axis=1)


def visible(Pw, Tw, world_scale, ups, lowest_deg=-5.0):
    centre = (Pw.min(0) + Pw.max(0)) / 2; radius = float(np.linalg.norm(Pw - centre, axis=1).max())
    # A pixel is at most 0.004 units on the table: about a seventh of a screen pixel from the table's edge.
    res = int(min(1200, max(400, 2 * radius * world_scale / .004)))
    base = view_directions(600, lowest_deg)
    dirs = np.concatenate([base @ toward(u).T for u in ups])   # the sky as each copy of the model has it (a copy may lie tilted)
    return _seen(Pw, Tw, np.ascontiguousarray(dirs), res, centre, radius).astype(bool)


# ── Step 2: collapsing edges ──
def sub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def dot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def cross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def point_segment2(p, a, b):
    ab = sub(b, a); t = dot(sub(p, a), ab); d = dot(ab, ab)
    t = 0 if d <= 0 else max(0, min(1, t / d))
    q = (a[0] + ab[0] * t - p[0], a[1] + ab[1] * t - p[1], a[2] + ab[2] * t - p[2])
    return dot(q, q)


def point_triangle2(p, a, b, c):
    """Squared distance from a point to a triangle (Ericson, Real-Time Collision Detection)."""
    ab = sub(b, a); ac = sub(c, a); ap = sub(p, a)
    d1 = dot(ab, ap); d2 = dot(ac, ap)
    if d1 <= 0 and d2 <= 0: return dot(ap, ap)
    bp = sub(p, b); d3 = dot(ab, bp); d4 = dot(ac, bp)
    if d3 >= 0 and d4 <= d3: return dot(bp, bp)
    vc = d1 * d4 - d3 * d2
    if vc <= 0 and d1 >= 0 and d3 <= 0:
        v = d1 / (d1 - d3); q = (ap[0] - ab[0] * v, ap[1] - ab[1] * v, ap[2] - ab[2] * v); return dot(q, q)
    cp = sub(p, c); d5 = dot(ab, cp); d6 = dot(ac, cp)
    if d6 >= 0 and d5 <= d6: return dot(cp, cp)
    vb = d5 * d2 - d1 * d6
    if vb <= 0 and d2 >= 0 and d6 <= 0:
        w = d2 / (d2 - d6); q = (ap[0] - ac[0] * w, ap[1] - ac[1] * w, ap[2] - ac[2] * w); return dot(q, q)
    va = d3 * d6 - d5 * d4
    if va <= 0 and (d4 - d3) >= 0 and (d5 - d6) >= 0:
        w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); bc = sub(c, b)
        q = (bp[0] - bc[0] * w, bp[1] - bc[1] * w, bp[2] - bc[2] * w); return dot(q, q)
    n = cross(ab, ac); nn = dot(n, n)
    if nn <= 0: return min(point_segment2(p, a, b), point_segment2(p, a, c))
    d = dot(ap, n); return d * d / nn


def simplify(pos, tris, wed, UV, N, facet, pn, eps, rim_eps, uv_tol, cos_facet, cos_normal, lock_rim):
    """pos: points; tris: [p0, p1, p2] per triangle (indices into pos); wed: the model's own vertex per corner (its UV and
    normal); facet: per corner, whether it is flat-shaded; pn: the one normal a point has, or None where several meet.
    Collapses in place; returns the indices of the triangles left."""
    eps2 = eps * eps; rim2 = rim_eps * rim_eps
    alive = [True] * len(tris)
    vt = [set() for _ in pos]
    for t, tr in enumerate(tris):
        for p in tr: vt[p].add(t)
    absorbed = [[] for _ in pos]          # per point left: the points (and their normals) that were moved onto it
    n_start = [None] * len(tris)          # the way each triangle faced to begin with
    uvt = [tuple(np.round(UV[i], 7)) for i in range(len(UV))]

    def tri_normal(a, b, c):
        n = cross(sub(pos[b], pos[a]), sub(pos[c], pos[a])); l = math.sqrt(dot(n, n))
        return ((n[0] / l, n[1] / l, n[2] / l) if l > 0 else None), l * .5

    # A quadric per point (how far a place is from the planes of the triangles that met there, weighted by their size):
    # it only orders the collapses, cheapest first. What may be collapsed is decided by the checks below.
    Q = [[0.0] * 10 for _ in pos]
    for t, tr in enumerate(tris):
        n, area = tri_normal(*tr)
        n_start[t] = n
        if n is None: continue
        d = -dot(n, pos[tr[0]]); a, b, c = n
        q = (a * a, a * b, a * c, a * d, b * b, b * c, b * d, c * c, c * d, d * d)
        for p in tr:
            Qp = Q[p]
            for i in range(10): Qp[i] += q[i] * area

    def qcost(u, v):
        q = Q[u]; x, y, z = pos[v]
        return q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x + q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y + q[7] * z * z + 2 * q[8] * z + q[9]

    def neighbours(u):
        out = {}
        for t in vt[u]:
            for p in tris[t]:
                if p != u: out[p] = out.get(p, 0) + 1
        return out

    def corner(t, p): return wed[t][tris[t].index(p)]

    def try_collapse(u, v):
        """The triangles that go and the new corners of those that stay, or None when moving u onto v would show."""
        tu = vt[u]
        edge = [t for t in tu if v in tris[t]]
        if not edge: return None
        nu = neighbours(u)
        if any(c > 2 for c in nu.values()): return None              # three sheets meet here: left alone
        rim = [x for x, c in nu.items() if c == 1]
        if rim:                                                       # on the rim of a hole: only along it, where it is straight
            if lock_rim or len(rim) != 2 or v not in rim: return None
            x = rim[0] if rim[1] == v else rim[1]
            if point_segment2(pos[u], pos[x], pos[v]) > rim2: return None
        nv = neighbours(v)
        if any(c > 2 for c in nv.values()): return None
        if len(set(nu) & set(nv)) != len(edge): return None          # would pinch the surface
        # UV islands round u: triangles side by side whose UVs agree on the edge they share.
        tl = list(tu); group = {t: t for t in tl}
        def find(t):
            while group[t] != t: t = group[t]
            return t
        by_nb = {}
        for t in tl:
            for p in tris[t]:
                if p != u: by_nb.setdefault(p, []).append(t)
        open_slots = {}                                               # triangle -> the neighbours across which its island ends
        for x, ts in by_nb.items():
            joined = False
            if len(ts) == 2:
                a, b = ts
                if uvt[corner(a, u)] == uvt[corner(b, u)] and uvt[corner(a, x)] == uvt[corner(b, x)]:
                    group[find(a)] = find(b); joined = True
            if not joined:
                for t in ts: open_slots.setdefault(t, []).append(x)
        islands = {}
        for t in tl: islands.setdefault(find(t), []).append(t)
        new_wedge = {}
        for ts in islands.values():
            es = [t for t in ts if v in tris[t]]
            if not es: return None                                    # this island doesn't reach v: u is on a seam v is not on
            wv = corner(es[0], v)
            if any(uvt[corner(t, v)] != uvt[wv] for t in es): return None
            slots = [x for t in ts for x in open_slots.get(t, [])]
            if slots:                                                 # u is on this island's rim: only along it, where it is straight in the texture
                if len(es) != 1 or len(slots) != 2 or v not in slots: return None
                x = slots[0] if slots[1] == v else slots[1]
                tx = next(t for t in ts if x in tris[t] and x in open_slots.get(t, []))
                a = UV[corner(tx, x)]; b = UV[wv]; p = UV[corner(es[0], u)]
                ab = b - a; l2 = float(ab @ ab); tt = 0 if l2 <= 0 else max(0, min(1, float((p - a) @ ab) / l2))
                q = a + ab * tt - p
                if float(q @ q) > uv_tol * uv_tol: return None
            for t in ts:
                if t not in es: new_wedge[t] = wv
        # The triangles that stay: none turns over (in space or in the texture), and the light on each stays.
        changed = []
        for t in tl:
            if t in edge: continue
            tr = tris[t]; k = tr.index(u)
            n0, _ = tri_normal(*tr)
            nt = list(tr); nt[k] = v
            n1, a1 = tri_normal(*nt)
            if n1 is None or a1 < 1e-12: return None
            if n0 is not None and dot(n0, n1) < .2: return None
            fk = facet[t]
            # Flat-shaded: the light on a face is its slope, so it may only lean a little from how it began.
            if (fk[0] or fk[1] or fk[2]) and n_start[t] is not None and dot(n_start[t], n1) < cos_facet: return None
            # Smooth-shaded where several normals meet (a hard edge beside it): the corner takes v's normal, which must be the one it had.
            if not fk[k] and pn[u] is None:
                na = N[wed[t][k]]; nb = N[new_wedge[t]]
                if na[0] * nb[0] + na[1] * nb[1] + na[2] * nb[2] < cos_normal: return None
            w = list(wed[t]); uo = UV[w]; w[k] = new_wedge[t]; un = UV[w]
            so = (uo[1, 0] - uo[0, 0]) * (uo[2, 1] - uo[0, 1]) - (uo[2, 0] - uo[0, 0]) * (uo[1, 1] - uo[0, 1])
            sn = (un[1, 0] - un[0, 0]) * (un[2, 1] - un[0, 1]) - (un[2, 0] - un[0, 0]) * (un[1, 1] - un[0, 1])
            if so * sn < 0: return None
            changed.append((t, nt, w))
        # Every point the surface had here stays within eps of it, and where it is smooth-shaded, keeps its light.
        new_tris = {t: nt for t, nt, _ in changed}
        def star(p):
            out = []
            for t in (vt[p] if p != v else (vt[v] | tu)):
                if t in edge: continue
                out.append(new_tris.get(t, tris[t]))
            return out
        check = [(v, absorbed[u] + [(pos[u], pn[u])] + absorbed[v])]
        for x in nu:
            if x != v and absorbed[x]: check.append((x, absorbed[x]))
        for p, pts in check:
            st = star(p)
            if not st: return None
            for pt, pnorm in pts:
                best = 1e30; at = None
                for tr in st:
                    d = point_triangle2(pt, pos[tr[0]], pos[tr[1]], pos[tr[2]])
                    if d < best:
                        best = d; at = tr
                        if best <= eps2 and pnorm is None: break
                if best > eps2: return None
                if pnorm is not None:
                    # The normal the new triangle gives this spot (blended from its corners) against the one it had.
                    a, b, c = pos[at[0]], pos[at[1]], pos[at[2]]
                    e0 = sub(b, a); e1 = sub(c, a); e2 = sub(pt, a)
                    d00 = dot(e0, e0); d01 = dot(e0, e1); d11 = dot(e1, e1); d20 = dot(e2, e0); d21 = dot(e2, e1)
                    den = d00 * d11 - d01 * d01
                    if den <= 0: return None
                    wb = max(0.0, (d11 * d20 - d01 * d21) / den); wc = max(0.0, (d00 * d21 - d01 * d20) / den); wa = max(0.0, 1 - wb - wc)
                    fnrm = None; nx = ny = nz = 0.0
                    for wgt, q in ((wa, at[0]), (wb, at[1]), (wc, at[2])):
                        nq = pn[q]
                        if nq is None:
                            if fnrm is None: fnrm, _ = tri_normal(*at)
                            nq = fnrm
                            if nq is None: return None
                        nx += wgt * nq[0]; ny += wgt * nq[1]; nz += wgt * nq[2]
                    l = math.sqrt(nx * nx + ny * ny + nz * nz)
                    if l <= 0 or (nx * pnorm[0] + ny * pnorm[1] + nz * pnorm[2]) / l < cos_normal: return None
        return edge, changed

    version = [0] * len(pos)
    heap = []
    def push(u):
        for x in neighbours(u): heapq.heappush(heap, (qcost(u, x), u, x, version[u], version[x]))
    for u in range(len(pos)):
        if vt[u]: push(u)
    while heap:
        _, u, v, vu, vv = heapq.heappop(heap)
        if version[u] != vu or version[v] != vv or not vt[u] or not vt[v]: continue
        r = try_collapse(u, v)
        if r is None: continue
        edge, changed = r
        ring = list(neighbours(u))
        for t in edge:
            alive[t] = False
            for p in tris[t]: vt[p].discard(t)
        for t, nt, w in changed:
            tris[t] = nt; wed[t] = w; vt[u].discard(t); vt[v].add(t)
        absorbed[v] += absorbed[u] + [(pos[u], pn[u])]; absorbed[u] = []
        Qu = Q[u]; Qv = Q[v]
        for i in range(10): Qv[i] += Qu[i]
        vt[u] = set()
        for p in set(ring) | {v}: version[p] += 1
        for p in set(ring) | {v}:
            if vt[p]: push(p)
    return [t for t in range(len(tris)) if alive[t]]


def reduce(P, N, UV, C, T, world_scale, eps_world, texels, facet_deg=20.0, normal_deg=3.0, ups=((0, 1, 0),), ground=False):
    """ground: a rolling smooth-shaded sheet (nothing hidden, its border kept as it is: its cut edge meets it)."""
    # Points at one place are one point (the model's vertices are split wherever a UV or a normal changes).
    size = float(np.linalg.norm(P.max(0) - P.min(0)))
    key = np.round(P / (size * 1e-6 + 1e-9)).astype(np.int64)
    _, first, pid = np.unique(key, axis=0, return_index=True, return_inverse=True)
    pid = pid.reshape(-1); Pw = P[first]; Tp = pid[T]
    ok = (Tp[:, 0] != Tp[:, 1]) & (Tp[:, 1] != Tp[:, 2]) & (Tp[:, 0] != Tp[:, 2])
    fn = np.cross(Pw[Tp[:, 1]] - Pw[Tp[:, 0]], Pw[Tp[:, 2]] - Pw[Tp[:, 0]]); fl = np.linalg.norm(fn, axis=1)
    ok &= fl > 1e-14
    # Which way a triangle faces: with the model's own normals (Unity's triangles run clockwise seen from the front).
    fnu = fn / np.maximum(fl[:, None], 1e-30)
    sign = 1.0 if np.median((fnu * N[T[:, 0]]).sum(1)[ok]) > 0 else -1.0
    # A corner is flat-shaded when its normal is its triangle's own.
    facet = np.stack([(fnu * sign * N[T[:, k]]).sum(1) > .9995 for k in range(3)], axis=1)
    if ground: facet[:] = False
    T, Tp, facet = T[ok], Tp[ok], facet[ok]
    if not ground:
        vis = visible(np.ascontiguousarray(Pw), np.ascontiguousarray(Tp if sign > 0 else Tp[:, ::-1]), world_scale, ups)
        T, Tp, facet = T[vis], Tp[vis], facet[vis]
    seen = len(T)
    pos = [tuple(p) for p in Pw]
    # The one normal a point has, where every corner meeting there is smooth-shaded with the same normal.
    acc = np.zeros((len(Pw), 3)); cnt = np.zeros(len(Pw)); bad = np.zeros(len(Pw), bool)
    for k in range(3):
        np.add.at(acc, Tp[:, k], N[T[:, k]]); np.add.at(cnt, Tp[:, k], 1); np.logical_or.at(bad, Tp[:, k], facet[:, k])
    mean = acc / np.maximum(np.linalg.norm(acc, axis=1, keepdims=True), 1e-30)
    for k in range(3): np.logical_or.at(bad, Tp[:, k], (mean[Tp[:, k]] * N[T[:, k]]).sum(1) < .999)
    pn = [None if (bad[i] or cnt[i] == 0) else tuple(mean[i]) for i in range(len(Pw))]
    tris = [list(map(int, t)) for t in Tp]; wed = [list(map(int, t)) for t in T]
    if eps_world > 0 and len(tris) > 0:
        eps = eps_world / world_scale
        idx = simplify(pos, tris, wed, UV, N, [tuple(bool(x) for x in f) for f in facet], pn, eps, eps * .1, .35 / max(texels, 1),
                       math.cos(math.radians(facet_deg)), math.cos(math.radians(normal_deg)), lock_rim=ground)
        tris = [tris[t] for t in idx]; wed = [wed[t] for t in idx]; facet = facet[idx]
    # Vertices out: a corner's UV and colour are its own vertex's; its normal the triangle's own where it was flat-shaded.
    verts = {}; oP, oN, oUV, oC, oT = [], [], [], [], []
    for t in range(len(tris)):
        a, b, c = tris[t]
        n = np.cross(Pw[b] - Pw[a], Pw[c] - Pw[a]) * sign; l = np.linalg.norm(n); n = n / l if l > 0 else np.array([0, 1.0, 0])
        for k in range(3):
            w = wed[t][k]
            nk = n if facet[t][k] else N[w]
            keyv = (tris[t][k], w, tuple(np.round(nk, 4)))
            i = verts.get(keyv)
            if i is None:
                i = verts[keyv] = len(oP); oP.append(Pw[tris[t][k]]); oN.append(nk); oUV.append(UV[w])
                if C is not None: oC.append(C[w])
            oT.append(i)
    out = (np.array(oP).reshape(-1, 3), np.array(oN).reshape(-1, 3), np.array(oUV).reshape(-1, 2), np.array(oC).reshape(-1, 4) if C is not None else None, np.array(oT, dtype=np.int64).reshape(-1, 3))
    return out, seen


# ── The whole map ──
def rows(folder):
    """index.txt, written by Unity: file|kind (prop or ground)|name|largest size|triangles|copies on the map|part of a place (0/1)|texture width|ups."""
    out = []
    for l in open(os.path.join(folder, "index.txt"), encoding="utf8"):
        c = l.rstrip("\n").split("|")
        out.append(dict(file=c[0], kind=c[1], name=c[2], scale=float(c[3]), tris=int(c[4]), copies=int(c[5]), place=c[6] == "1", texels=float(c[7]),
                        ups=[tuple(float(v) for v in u.split(",")) for u in c[8].split(";")]))
    return out


def eligible(r):
    """A piece of a place is left alone: it is seen closest, and the hover outline is built from its mesh."""
    return r["kind"] == "prop" and not r["place"] and r["tris"] >= MIN_TRIANGLES and r["tris"] * r["copies"] >= MIN_TRIANGLES_ON_MAP


def _one(job):
    folder, r = job
    P, N, UV, C, T = load(os.path.join(folder, "mesh", r["file"]))
    if r["kind"] == "ground":
        out, _ = reduce(P, N, UV, C, T, 1.0, GROUND_EPS, r["texels"], ground=True)
        save(os.path.join(folder, "A", r["file"]), *out)
        return f"{r['file']}|{r['name']}|{len(T)}|{len(out[4])}|{len(out[4])}"
    counts = []
    for name, eps, facet_deg in TRIES:
        out, _ = reduce(P, N, UV, C, T, r["scale"], eps, r["texels"], facet_deg=facet_deg, ups=r["ups"])
        save(os.path.join(folder, name, r["file"]), *out); counts.append(len(out[4]))
    return f"{r['file']}|{r['name']}|{len(T)}|{counts[0]}|{counts[1]}"


def cmd_reduce(folder):
    from concurrent.futures import ProcessPoolExecutor
    for name, _, _ in TRIES: os.makedirs(os.path.join(folder, name), exist_ok=True)
    jobs = [(folder, r) for r in rows(folder) if r["kind"] == "ground" or eligible(r)]
    with ProcessPoolExecutor(max(1, min(8, os.cpu_count() or 1))) as ex: lines = list(ex.map(_one, jobs))
    open(os.path.join(folder, "reduce.txt"), "w", encoding="utf8").write("\n".join(lines) + "\n")
    print(f"{len(lines)} meshes tried")


def look(sheet, cell, cols, views):
    """How far each result looks from the original in Unity's pictures (columns: original, then each try; rows: views):
    the share of the model's pixels that changed, a pixel in or out of the outline counting in full and a tone change
    by its size, after a 3 px blur; the worst view counts."""
    from PIL import Image
    from scipy import ndimage
    im = np.asarray(Image.open(sheet).convert("RGB")).astype(int); ground = np.array([120.0, 110, 80])
    def prep(c):
        m = ~((c[..., 0] > 240) & (c[..., 1] < 15) & (c[..., 2] > 240)); f = c.astype(float).copy(); f[~m] = ground
        return m, ndimage.uniform_filter(f, size=(3, 3, 1))
    res = []
    for l in range(1, cols):
        worst = 0.0
        for v in range(views):
            a = im[v * cell:(v + 1) * cell, 0:cell]; b = im[v * cell:(v + 1) * cell, l * cell:(l + 1) * cell]
            ma, fa = prep(a); mb, fb = prep(b); u = ma | mb
            if u.sum() == 0: continue
            worst = max(worst, float((np.minimum(np.abs(fa - fb).max(2), 96) / 96)[u].mean()))
        res.append(worst)
    return res


def cmd_choose(folder, limit):
    final = os.path.join(folder, "final"); os.makedirs(final, exist_ok=True)
    tried = {l.split("|")[0]: [int(v) for v in l.rstrip("\n").split("|")[2:5]] for l in open(os.path.join(folder, "reduce.txt"), encoding="utf8")}
    sheets = {l.split("|")[0]: l.rstrip("\n").split("|") for l in open(os.path.join(folder, "sheets", "sizes.txt"), encoding="utf8")}
    report = []; before = after = 0
    for r in rows(folder):
        f = r["file"]; use = None; shown = 0.0
        if f in tried and r["kind"] == "ground":
            use = "A"                                                  # held by its own rules (eps and the light on it), checked in game
        elif f in tried and f in sheets:
            s = sheets[f]; scores = look(os.path.join(folder, "sheets", f + ".png"), int(s[1]), int(s[2]), int(s[3]))
            for i, (name, _, _) in enumerate(TRIES):
                if scores[i] * 100 <= limit and tried[f][i + 1] < r["tris"] * .9: use = name; shown = scores[i]; break
        if use: shutil.copyfile(os.path.join(folder, use, f), os.path.join(final, f))
        left = tried[f][1 if use == "A" else 2] if use else r["tris"]
        before += r["tris"] * max(r["copies"], 1); after += left * max(r["copies"], 1)
        report.append(f"{f}|{r['name']}|{r['tris']}|{left}|{r['copies']}|{use or '-'}|{shown * 100:.1f}")
    open(os.path.join(folder, "report.txt"), "w", encoding="utf8").write("\n".join(report) + "\n")
    print(f"{sum(1 for l in report if l.split('|')[5] != '-')} meshes reduced; over all copies on the map {before} -> {after} triangles")


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["reduce", "choose", "one"]); ap.add_argument("paths", nargs="+")
    ap.add_argument("--limit", type=float, default=7.0, help="choose: the most a result may look different, in percent")
    ap.add_argument("--scale", type=float, default=1.0); ap.add_argument("--eps", type=float, default=.04)
    ap.add_argument("--facet-deg", type=float, default=20.0); ap.add_argument("--texels", type=float, default=64)
    ap.add_argument("--ground", action="store_true")
    a = ap.parse_args()
    if a.command == "reduce": cmd_reduce(a.paths[0])
    elif a.command == "choose": cmd_choose(a.paths[0], a.limit)
    else:
        P, N, UV, C, T = load(a.paths[0])
        out, seen = reduce(P, N, UV, C, T, a.scale, a.eps, a.texels, facet_deg=a.facet_deg, ground=a.ground)
        save(a.paths[1], *out)
        print(f"{len(T)} triangles, {seen} seen, {len(out[4])} left")
