using System.Globalization;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MapLightBaker;

public static class Output
{
    /// <summary>
    /// Dilate covered texels outward by a few pixels so the atlas padding gutters
    /// take on neighbor colors. Without this, bilinear sampling at face edges
    /// pulls in the background and you get dark seams.
    /// </summary>
    public static void Dilate(float[] rgb, bool[] covered, int w, int h, int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            var newCovered = (bool[])covered.Clone();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int pi = y * w + x;
                if (covered[pi]) continue;

                double r = 0, g = 0, b = 0; int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (!covered[ni]) continue;
                    r += rgb[ni * 3 + 0]; g += rgb[ni * 3 + 1]; b += rgb[ni * 3 + 2]; n++;
                }
                if (n > 0)
                {
                    rgb[pi * 3 + 0] = (float)(r / n);
                    rgb[pi * 3 + 1] = (float)(g / n);
                    rgb[pi * 3 + 2] = (float)(b / n);
                    newCovered[pi] = true;
                }
            }
            Array.Copy(newCovered, covered, covered.Length);
        }
    }

    public static void WritePng(string path, float[] rgb, int w, int h)
    {
        using var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int pi = y * w + x;
            // Simple Reinhard-ish compress then gamma to 8-bit. Keeps highlights
            // from clipping hard; your engine can treat this as sRGB albedo-mult.
            byte R = ToByte(rgb[pi * 3 + 0]);
            byte G = ToByte(rgb[pi * 3 + 1]);
            byte B = ToByte(rgb[pi * 3 + 2]);
            img[x, y] = new Rgba32(R, G, B, 255);
        }
        img.SaveAsPng(path);
    }

    private static byte ToByte(float v)
    {
        double x = v / (1.0 + v);              // tonemap
        x = Math.Pow(Math.Clamp(x, 0, 1), 1.0 / 2.2);   // gamma
        return (byte)Math.Clamp((int)(x * 255 + 0.5), 0, 255);
    }

    /// <summary>
    /// Write a JSON manifest mapping each face to its atlas UV rect and the
    /// world-space basis used, so your loader can compute per-vertex lightmap UVs
    /// exactly the way the baker did.
    /// </summary>
    public static void WriteManifest(
        string path,
        List<FaceLightmap> rects,
        IReadOnlyList<(BrushFace face, int id)> faces,
        int atlasW, int atlasH)
    {
        var byId = new Dictionary<int, FaceLightmap>();
        foreach (var r in rects) byId[r.FaceId] = r;

        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.Append("{\n");
        sb.Append($"  \"atlasWidth\": {atlasW},\n");
        sb.Append($"  \"atlasHeight\": {atlasH},\n");
        sb.Append("  \"faces\": [\n");

        bool first = true;
        foreach (var (face, id) in faces)
        {
            if (!byId.TryGetValue(id, out var r)) continue;
            if (!first) sb.Append(",\n");
            first = false;

            // Per-vertex lightmap UVs (0..1 in atlas space) for this face's winding.
            sb.Append("    {\n");
            sb.Append($"      \"faceId\": {id},\n");
            sb.Append($"      \"texture\": \"{Escape(face.Texture)}\",\n");
            sb.Append($"      \"atlasRect\": [{r.AtlasX}, {r.AtlasY}, {r.Width}, {r.Height}],\n");
            sb.Append("      \"verts\": [\n");

            for (int vi = 0; vi < face.Winding.Count; vi++)
            {
                Vec3 p = face.Winding[vi];
                // Local face coords -> atlas pixel -> normalized UV.
                double lu = (p - r.Origin).Dot(r.S) / r.TexelSize;
                double lv = (p - r.Origin).Dot(r.T) / r.TexelSize;
                double u = (r.AtlasX + lu) / atlasW;
                double v = (r.AtlasY + lv) / atlasH;

                sb.Append("        { ");
                sb.Append(string.Format(ci,
                    "\"pos\": [{0:0.####}, {1:0.####}, {2:0.####}], \"uv\": [{3:0.######}, {4:0.######}]",
                    p.X, p.Y, p.Z, u, v));
                sb.Append(vi < face.Winding.Count - 1 ? " },\n" : " }\n");
            }

            sb.Append("      ]\n");
            sb.Append("    }");
        }

        sb.Append("\n  ]\n}\n");
        File.WriteAllText(path, sb.ToString());
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
