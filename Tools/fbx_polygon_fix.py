"""Removes one polygon from a mesh inside a binary FBX, leaving everything else in the file as it is.

Why: Unity logs "A polygon of Mesh 'X' in <file> is self-intersecting and has been discarded" every time it
imports a model with a polygon its triangulator can't cut up. The polygon is already missing from the mesh in
the game, so taking it out of the file gives the same mesh without the warning. Blender's own importer and
exporter are NOT used (a re-export renames bones and changes axes): the file's node tree is read with Blender's
FBX parser, one Geometry node is edited, and the tree is written back.

Run (Blender 5.2, headless):
  blender --background --python Tools/fbx_polygon_fix.py -- info   <in.fbx> <mesh name> [all]
  blender --background --python Tools/fbx_polygon_fix.py -- copy   <in.fbx> <out.fbx>
  blender --background --python Tools/fbx_polygon_fix.py -- remove <in.fbx> <out.fbx> <mesh name> <polygon index>
  blender --background --python Tools/fbx_polygon_fix.py -- split  <in.fbx> <out.fbx> <mesh name> <polygon index> 1,2,4 2,3,4

split puts triangles in the polygon's place, each three corner numbers within the polygon: for a polygon Unity
kept part of (removing it changes the mesh), give the triangles Unity made of it.

info lists the mesh's polygons with more than three corners that look suspect and the triangle count the file
holds; Unity's imported triangle count is that minus the discarded polygon's (corners - 2). After a remove,
check in Unity that every mesh of the file is unchanged (vertices, triangles, normals, UVs) and the warning gone.
"""
import sys
import array
from io_scene_fbx import parse_fbx, encode_bin, data_types

ARGS = sys.argv[sys.argv.index("--") + 1:]


# ── Reading and writing the node tree ──

def rebuild(elem):
    out = encode_bin.FBXElem(elem.id)
    for value, kind in zip(elem.props, elem.props_type):
        if kind == data_types.BOOL: out.add_bool(value)
        elif kind == data_types.CHAR: out.add_char(value)
        elif kind == data_types.INT8: out.add_int8(value)
        elif kind == data_types.INT16: out.add_int16(value)
        elif kind == data_types.INT32: out.add_int32(value)
        elif kind == data_types.INT64: out.add_int64(value)
        elif kind == data_types.FLOAT32: out.add_float32(value)
        elif kind == data_types.FLOAT64: out.add_float64(value)
        elif kind == data_types.BYTES: out.add_bytes(value)
        elif kind == data_types.STRING: out.add_string(value)
        elif kind == data_types.INT32_ARRAY: out.add_int32_array(value)
        elif kind == data_types.INT64_ARRAY: out.add_int64_array(value)
        elif kind == data_types.FLOAT32_ARRAY: out.add_float32_array(value)
        elif kind == data_types.FLOAT64_ARRAY: out.add_float64_array(value)
        elif kind == data_types.BOOL_ARRAY: out.add_bool_array(value)
        elif kind == data_types.BYTE_ARRAY: out.add_byte_array(value)
        else: raise ValueError("unknown property type %r in %r" % (kind, elem.id))
    for child in elem.elems:
        out.elems.append(rebuild(child))
    return out


def save(root, version, path):
    top = encode_bin.FBXElem(b"")
    for child in root.elems:
        top.elems.append(rebuild(child))
    encode_bin.write(path, top, version)


def child(elem, name):
    for e in elem.elems:
        if e.id == name:
            return e
    return None


def clean(raw):
    return raw.decode("utf-8", "replace").split("\x00")[0]


def geometry_of(root, mesh_name):
    """The Geometry node joined to the Model called mesh_name."""
    objects = child(root, b"Objects")
    models = {e.props[0]: clean(e.props[1]) for e in objects.elems if e.id == b"Model"}
    geoms = {e.props[0]: e for e in objects.elems if e.id == b"Geometry"}
    found = []
    for c in child(root, b"Connections").elems:
        if c.props[0] == b"OO" and c.props[1] in geoms and models.get(c.props[2]) == mesh_name:
            found.append(geoms[c.props[1]])
    if len(found) != 1:
        raise SystemExit("mesh %r: %d geometries found" % (mesh_name, len(found)))
    return found[0]


def polygons(index):
    out, start = [], 0
    for i, v in enumerate(index):
        if v < 0:
            out.append((start, i + 1))
            start = i + 1
    return out


# ── Looking for the polygon ──

def note(points):
    """What might upset a triangulator: repeated places, corners in a line, a bent or folded outline."""
    n = len(points)
    notes = []
    for i in range(n):
        for j in range(i + 1, n):
            if sum((points[i][k] - points[j][k]) ** 2 for k in range(3)) < 1e-12:
                notes.append("same place %d,%d" % (i, j))
    normal = [0.0, 0.0, 0.0]
    for i in range(n):
        a, b = points[i], points[(i + 1) % n]
        normal[0] += (a[1] - b[1]) * (a[2] + b[2])
        normal[1] += (a[2] - b[2]) * (a[0] + b[0])
        normal[2] += (a[0] - b[0]) * (a[1] + b[1])
    length = sum(x * x for x in normal) ** 0.5
    if length < 1e-10:
        return notes + ["no area"]
    normal = [x / length for x in normal]
    centre = [sum(p[k] for p in points) / n for k in range(3)]
    size = max(sum((p[k] - centre[k]) ** 2 for k in range(3)) ** 0.5 for p in points)
    bend = max(abs(sum((p[k] - centre[k]) * normal[k] for k in range(3))) for p in points)
    if bend > 0.02 * size:
        notes.append("bent %.0f%%" % (100 * bend / size))
    turns = []
    for i in range(n):
        a, b, c = points[i - 1], points[i], points[(i + 1) % n]
        u = [b[k] - a[k] for k in range(3)]
        v = [c[k] - b[k] for k in range(3)]
        cr = (u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0])
        d = sum(cr[k] * normal[k] for k in range(3))
        scale = (sum(x * x for x in u) * sum(x * x for x in v)) ** 0.5
        turns.append(0 if scale == 0 or abs(d) < 1e-4 * scale else (1 if d > 0 else -1))
    if 0 in turns:
        notes.append("corners in a line %d" % turns.count(0))
    if 1 in turns and -1 in turns:
        notes.append("concave %d" % min(turns.count(1), turns.count(-1)))
    return notes


def info(path, mesh_name):
    root, version = parse_fbx.parse(path)
    geom = geometry_of(root, mesh_name)
    verts = child(geom, b"Vertices").props[0]
    index = child(geom, b"PolygonVertexIndex").props[0]
    polys = polygons(index)
    print("INFO %s: %d polygons, %d triangles in the file" % (mesh_name, len(polys), sum(e - s - 2 for s, e in polys)))
    for number, (s, e) in enumerate(polys):
        if e - s < 4:
            continue
        corners = [index[i] if index[i] >= 0 else -index[i] - 1 for i in range(s, e)]
        notes = note([(verts[3 * c], verts[3 * c + 1], verts[3 * c + 2]) for c in corners])
        if len(set(corners)) < len(corners):
            notes.append("repeated corner")
        if (len(ARGS) > 3 and e - s > 4) or (notes and (e - s > 4 or any(not x.startswith("bent") for x in notes))):
            print("INFO   polygon %d corners %d: %s" % (number, e - s, "; ".join(notes)))


# ── Taking it out ──

def replace(path, out_path, mesh_name, number, triangles):
    """Puts the given triangles (corner numbers within the polygon) in the polygon's place; none = remove it."""
    root, version = parse_fbx.parse(path)
    geom = geometry_of(root, mesh_name)
    index_elem = child(geom, b"PolygonVertexIndex")
    index = index_elem.props[0]
    polys = polygons(index)
    s, e = polys[number]
    n = e - s
    total = len(index)
    fresh_corners = [c for tri in triangles for c in tri]   # corner numbers within the old polygon, new order
    grow = len(fresh_corners) - n

    def corner(i):
        return index[i] if index[i] >= 0 else -index[i] - 1

    def swap(arr, first, count, stride, picks):
        """arr with `count` entries from `first` replaced by copies of the entries first + pick."""
        mid = array.array(arr.typecode)
        for pick in picks:
            mid.extend(arr[(first + pick) * stride:(first + pick + 1) * stride])
        return arr[:first * stride] + mid + arr[(first + count) * stride:]

    # Edges name the corner they start at. One that starts in the old polygon moves to a new triangle or
    # another polygon with the same two points, or goes (with its entry in every per-edge layer).
    edges_elem = child(geom, b"Edges")
    keep_edges, dropped = None, 0
    if edges_elem is not None:
        owner = {}
        for ps, pe in polys:
            if ps == s:
                continue
            for i in range(ps, pe):
                j = i + 1 if i + 1 < pe else ps
                owner.setdefault(frozenset((corner(i), corner(j))), i + grow if i >= e else i)
        for k, tri in enumerate(triangles):
            for m in range(3):
                pair = frozenset((corner(s + tri[m]), corner(s + tri[(m + 1) % 3])))
                owner.setdefault(pair, s + 3 * k + m)
        new_edges, keep_edges = array.array(edges_elem.props[0].typecode), []
        for k, start in enumerate(edges_elem.props[0]):
            if s <= start < e:
                j = start + 1 if start + 1 < e else s
                start = owner.get(frozenset((corner(start), corner(j))), -1)
                if start < 0:
                    continue
            elif start >= e:
                start += grow
            new_edges.append(start)
            keep_edges.append(k)
        dropped = len(edges_elem.props[0]) - len(new_edges)
        edges_elem.props[0] = new_edges
        print("FIX edges dropped", dropped)

    middle = array.array(index.typecode)
    for tri in triangles:
        middle.extend([corner(s + tri[0]), corner(s + tri[1]), -corner(s + tri[2]) - 1])
    index_elem.props[0] = index[:s] + middle + index[e:]

    for layer in geom.elems:
        if not layer.id.startswith(b"LayerElement"):
            continue
        mapping = child(layer, b"MappingInformationType").props[0]
        reference = child(layer, b"ReferenceInformationType").props[0]
        for data in layer.elems:
            if not data.props or not isinstance(data.props[0], array.array):
                continue
            arr = data.props[0]
            is_index = data.id.endswith(b"Index") or data.id == b"Materials"
            if not (reference == b"Direct" or is_index):
                continue
            if mapping == b"ByPolygonVertex":
                stride = len(arr) // total
                if stride * total != len(arr) or stride == 0:
                    raise SystemExit("layer %r %r: %d values for %d corners" % (layer.id, data.id, len(arr), total))
                data.props[0] = swap(arr, s, n, stride, fresh_corners)
            elif mapping == b"ByPolygon":
                stride = len(arr) // len(polys)
                if stride * len(polys) != len(arr) or stride == 0:
                    raise SystemExit("layer %r %r: %d values for %d polygons" % (layer.id, data.id, len(arr), len(polys)))
                data.props[0] = swap(arr, number, 1, stride, [0] * len(triangles))
            elif mapping == b"ByEdge":
                if keep_edges is not None:
                    count = len(keep_edges) + dropped
                    stride = len(arr) // count
                    if stride * count != len(arr) or stride == 0:
                        raise SystemExit("layer %r %r: %d values for %d edges" % (layer.id, data.id, len(arr), count))
                    fresh = array.array(arr.typecode)
                    for k in keep_edges:
                        fresh.extend(arr[k * stride:(k + 1) * stride])
                    data.props[0] = fresh
            elif mapping not in (b"ByVertice", b"ByVertex", b"AllSame", b"ByControlPoint"):
                raise SystemExit("layer %r: mapping %r not handled" % (layer.id, mapping))
    save(root, version, out_path)
    print("FIX polygon %d (%d corners) of %s -> %d triangles, %s" % (number, n, mesh_name, len(triangles), out_path))


if ARGS[0] == "info":
    info(ARGS[1], ARGS[2])
elif ARGS[0] == "copy":
    tree, ver = parse_fbx.parse(ARGS[1])
    save(tree, ver, ARGS[2])
    print("FIX copied", ARGS[2])
elif ARGS[0] == "remove":
    replace(ARGS[1], ARGS[2], ARGS[3], int(ARGS[4]), [])
elif ARGS[0] == "split":
    replace(ARGS[1], ARGS[2], ARGS[3], int(ARGS[4]), [tuple(int(c) for c in tri.split(",")) for tri in ARGS[5:]])
else:
    raise SystemExit(__doc__)
