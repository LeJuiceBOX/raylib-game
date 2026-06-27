namespace MapLightBaker;

/// <summary>
/// Per-face lightmap allocation. Each face gets its own rectangle in the atlas.
/// We build a planar basis on the face, measure its extent in world units, and
/// allocate texels = extent * texelsPerUnit. A simple shelf packer places the
/// rectangles. Padding around each rect prevents bilinear bleed between faces.
/// </summary>
public sealed class FaceLightmap
{
    public int FaceId;
    public Vec3 Origin;          // world point mapping to atlas (0,0) of this rect
    public Vec3 S;               // world-space axis for atlas U (unit length)
    public Vec3 T;               // world-space axis for atlas V (unit length)
    public double TexelSize;     // world units per texel
    public int Width, Height;    // texel dimensions of this face's rect
    public int AtlasX, AtlasY;   // placement in the atlas
    public Vec3 Normal;

    /// <summary>World position of the center of a texel local to this face's rect.</summary>
    public Vec3 TexelToWorld(int lx, int ly)
    {
        double u = (lx + 0.5) * TexelSize;
        double v = (ly + 0.5) * TexelSize;
        return Origin + S * u + T * v;
    }
}

public static class LightmapLayout
{
    /// <summary>
    /// Build per-face lightmap rects and pack them into an atlas of the chosen
    /// width (height grows as needed). Returns the rects and the atlas height.
    /// </summary>
    public static (List<FaceLightmap> rects, int atlasW, int atlasH) Build(
        IReadOnlyList<(BrushFace face, int id)> faces,
        double texelsPerUnit,
        int atlasWidth,
        int padding)
    {
        var rects = new List<FaceLightmap>();
        double texelSize = 1.0 / texelsPerUnit;

        foreach (var (face, id) in faces)
        {
            if (face.Winding.Count < 3) continue;

            Vec3 n = face.Plane.N;

            // Planar basis on the face.
            Vec3 up = Math.Abs(n.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
            Vec3 s = up.Cross(n).Normalized;
            Vec3 t = n.Cross(s).Normalized;

            // Project winding to (s,t) and find bounds.
            double minU = double.MaxValue, minV = double.MaxValue;
            double maxU = double.MinValue, maxV = double.MinValue;
            foreach (var p in face.Winding)
            {
                double pu = p.Dot(s);
                double pv = p.Dot(t);
                if (pu < minU) minU = pu;
                if (pv < minV) minV = pv;
                if (pu > maxU) maxU = pu;
                if (pv > maxV) maxV = pv;
            }

            int w = Math.Max(1, (int)Math.Ceiling((maxU - minU) * texelsPerUnit));
            int h = Math.Max(1, (int)Math.Ceiling((maxV - minV) * texelsPerUnit));

            // Origin in world: the (minU, minV) corner on the plane.
            // Reconstruct a world point with those projected coords on the plane.
            // Start from the plane's closest point to origin, then offset.
            Vec3 planeOrigin = n * face.Plane.D;
            double baseU = planeOrigin.Dot(s);
            double baseV = planeOrigin.Dot(t);
            Vec3 origin = planeOrigin + s * (minU - baseU) + t * (minV - baseV);

            rects.Add(new FaceLightmap
            {
                FaceId = id,
                Origin = origin,
                S = s,
                T = t,
                TexelSize = texelSize,
                Width = w,
                Height = h,
                Normal = n,
            });
        }

        int atlasH = ShelfPack(rects, atlasWidth, padding);
        return (rects, atlasWidth, atlasH);
    }

    /// <summary>
    /// Dead-simple shelf packer: sort by height, lay left-to-right into rows.
    /// Good enough for coarse lightmaps; not optimal but fast and predictable.
    /// </summary>
    private static int ShelfPack(List<FaceLightmap> rects, int atlasWidth, int padding)
    {
        var ordered = rects.OrderByDescending(r => r.Height).ToList();

        int x = padding, y = padding, shelfH = 0;
        foreach (var r in ordered)
        {
            if (x + r.Width + padding > atlasWidth)
            {
                // New shelf.
                x = padding;
                y += shelfH + padding;
                shelfH = 0;
            }
            r.AtlasX = x;
            r.AtlasY = y;
            x += r.Width + padding;
            shelfH = Math.Max(shelfH, r.Height);
        }
        return y + shelfH + padding;
    }
}
