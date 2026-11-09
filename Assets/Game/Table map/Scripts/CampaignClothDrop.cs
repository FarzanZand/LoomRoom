using System;
using System.IO;
using UnityEngine;

// The first reveal's cloth, simulated in Blender (Tools/campaign_cloth_drop.py) and played back here: a cloth the size
// of the map falls onto the bare table, then the land rises under it and pushes it into hills and folds. Frames are
// read from the baked .bytes file (CampaignMap.clothDropBake) and blended between, so the playback is smooth at any
// frame rate. Its last frame is also where the act cloths lie (CampaignMap.Drape), so the swap to them is seamless.
public class CampaignClothDrop
{
    public int nx, nz, frames;
    public float fps, riseStart, riseEnd;
    public Vector3[][] positions;   // [frame][z * nx + x], map-local
    public float Seconds => (frames - 1) / fps;

    public static CampaignClothDrop Read(TextAsset asset)
    {
        if (asset == null) return null;
        try { return ReadUnchecked(asset); }
        catch (Exception e) { Debug.LogWarning("[Campaign] the cloth drop bake could not be read (" + e.Message + "); the first reveal only rolls the cloth back"); return null; }
    }

    static CampaignClothDrop ReadUnchecked(TextAsset asset)
    {
        using var r = new BinaryReader(new MemoryStream(asset.bytes));
        int magic = r.ReadInt32(), version = r.ReadInt32();
        if (magic != 0x434C4F54 || version < 1 || version > 2) { Debug.LogWarning("[Campaign] the cloth drop bake is not one I can read"); return null; }
        var d = new CampaignClothDrop { nx = r.ReadInt32(), nz = r.ReadInt32(), frames = r.ReadInt32(), fps = r.ReadSingle(), riseStart = r.ReadSingle(), riseEnd = r.ReadSingle() };
        int n = d.nx * d.nz;
        long need = 32L + (long)d.frames * n * 3 * (version == 2 ? 2 : 4);
        if (d.nx < 2 || d.nz < 2 || d.frames < 1 || asset.bytes.Length < need) { Debug.LogWarning("[Campaign] the cloth drop bake is cut short"); return null; }
        d.positions = new Vector3[d.frames][];
        for (int f = 0; f < d.frames; f++)
        {
            var p = d.positions[f] = new Vector3[n];
            // Version 2 stores half floats (half the file), version 1 full floats.
            if (version == 2) for (int i = 0; i < n; i++) p[i] = new Vector3(Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16()), Mathf.HalfToFloat(r.ReadUInt16()));
            else for (int i = 0; i < n; i++) p[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        }
        return d;
    }

    // After the drop has played only its settled shape is needed (the act cloths lie on it): the other frames go.
    public void KeepLastFrame()
    {
        if (frames < 2) return;
        positions = new[] { positions[frames - 1] };
        frames = 1;
    }

    // The cloth at a time (seconds from the start), blended between the two frames round it.
    public void Sample(float seconds, Vector3[] into)
    {
        float f = Mathf.Clamp(seconds * fps, 0f, frames - 1);
        int a = Mathf.Min((int)f, frames - 1), b = Mathf.Min(a + 1, frames - 1);
        float t = f - a;
        var pa = positions[a]; var pb = positions[b];
        for (int i = 0; i < into.Length; i++) into[i] = Vector3.LerpUnclamped(pa[i], pb[i], t);
    }

    // The settled cloth's height at a map-local spot (bilinear over its grid, by where each vertex started).
    public float HeightAt(float lx, float lz, float halfX, float halfZ)
    {
        var last = positions[frames - 1];
        float fx = Mathf.Clamp01((lx / halfX + 1f) * .5f) * (nx - 1), fz = Mathf.Clamp01((lz / halfZ + 1f) * .5f) * (nz - 1);
        int ix = Mathf.Min(nx - 2, (int)fx), iz = Mathf.Min(nz - 2, (int)fz);
        float tx = fx - ix, tz = fz - iz;
        float h00 = last[iz * nx + ix].y, h10 = last[iz * nx + ix + 1].y, h01 = last[(iz + 1) * nx + ix].y, h11 = last[(iz + 1) * nx + ix + 1].y;
        return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
    }

    // A mesh for the cloth (UVs from where each vertex starts, like the act cloths' UVs).
    public Mesh BuildMesh(float halfX, float halfZ)
    {
        var verts = new Vector3[nx * nz]; var uvs = new Vector2[nx * nz];
        var tris = new int[(nx - 1) * (nz - 1) * 6];
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float lx = (x / (float)(nx - 1) * 2 - 1) * halfX, lz = (z / (float)(nz - 1) * 2 - 1) * halfZ;
                uvs[z * nx + x] = new Vector2(lx, lz) / 8f;
                verts[z * nx + x] = positions[0][z * nx + x];
            }
        int t = 0;
        for (int z = 0; z < nz - 1; z++)
            for (int x = 0; x < nx - 1; x++)
            {
                int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        var mesh = new Mesh { name = "Cloth drop", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.MarkDynamic();
        return mesh;
    }
}
