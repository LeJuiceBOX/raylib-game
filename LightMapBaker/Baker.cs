using System.Collections.Concurrent;

namespace MapLightBaker;

public sealed class BakeSettings
{
    public double TexelsPerUnit = 0.25;   // 4 world units per texel — coarse, HL2-ish
    public int AtlasWidth = 1024;
    public int Padding = 2;

    // The map's light_radius/light_energy are Godot OmniLight values that are tiny
    // relative to Quake-unit geometry, so we scale them into world space here.
    public double RangeScale = 200.0;     // world units of reach per unit of light_radius
    public double EnergyScale = 4.0;      // brightness multiplier per unit of light_energy

    public double Ambient = 0.08;         // flat floor light so shadows aren't pure black
    public int ShadowSamples = 1;         // >1 softens shadows (area sampling); 1 = hard
    public double LightSize = 8.0;        // world radius for soft sampling when ShadowSamples>1
}

public static class Baker
{
    /// <summary>
    /// Bake direct lighting into the atlas. Returns an RGB float buffer
    /// (length atlasW*atlasH*3) plus a coverage mask (which texels were written).
    /// </summary>
    public static (float[] rgb, bool[] covered) Bake(
        List<FaceLightmap> rects,
        OcclusionGrid grid,
        List<OmniLight> lights,
        int atlasW, int atlasH,
        BakeSettings cfg)
    {
        var rgb = new float[atlasW * atlasH * 3];
        var covered = new bool[atlasW * atlasH];

        // Parallelize across faces; texels within a face are independent too,
        // but per-face is a clean unit and keeps memory access local.
        Parallel.ForEach(rects, rect =>
        {
            for (int ly = 0; ly < rect.Height; ly++)
            for (int lx = 0; lx < rect.Width; lx++)
            {
                Vec3 worldPos = rect.TexelToWorld(lx, ly);
                // Nudge off the surface to avoid self-intersection on shadow rays.
                Vec3 sample = worldPos + rect.Normal * 0.1;

                Vec3 lit = ShadeTexel(sample, rect.Normal, rect.FaceId, lights, grid, cfg);

                int ax = rect.AtlasX + lx;
                int ay = rect.AtlasY + ly;
                if (ax < 0 || ax >= atlasW || ay < 0 || ay >= atlasH) continue;
                int pi = ay * atlasW + ax;
                rgb[pi * 3 + 0] = (float)lit.X;
                rgb[pi * 3 + 1] = (float)lit.Y;
                rgb[pi * 3 + 2] = (float)lit.Z;
                covered[pi] = true;
            }
        });

        return (rgb, covered);
    }

    private static Vec3 ShadeTexel(
        Vec3 pos, Vec3 normal, int faceId,
        List<OmniLight> lights, OcclusionGrid grid, BakeSettings cfg)
    {
        // Start with flat ambient so unlit areas read as dim, not black.
        Vec3 acc = new(cfg.Ambient, cfg.Ambient, cfg.Ambient);

        foreach (var light in lights)
        {
            Vec3 toLight = light.Position - pos;
            double dist = toLight.Length;
            if (dist < 1e-4) continue;
            Vec3 dir = toLight / dist;

            double ndotl = normal.Dot(dir);
            if (ndotl <= 0) continue;   // facing away

            double range = light.RawRadius * cfg.RangeScale;
            if (range <= 0) range = cfg.RangeScale;
            if (dist > range) continue;

            // Inverse-ish falloff with a smooth cutoff at range (matches the
            // soft look engines use, avoids a hard circle edge).
            double atten = 1.0 - (dist / range);
            atten = atten * atten;     // quadratic-ish falloff
            double energy = light.RawEnergy * cfg.EnergyScale;

            // Shadow test (hard or soft depending on samples).
            double vis = Visibility(pos, light.Position, faceId, grid, cfg);
            if (vis <= 0) continue;

            double contrib = ndotl * atten * energy * vis;
            acc = acc + new Vec3(light.Color.X, light.Color.Y, light.Color.Z) * contrib;
        }

        // Clamp to a sane HDR-ish ceiling; the loader can tonemap if it wants.
        return new Vec3(Math.Min(acc.X, 4), Math.Min(acc.Y, 4), Math.Min(acc.Z, 4));
    }

    private static double Visibility(Vec3 pos, Vec3 lightPos, int faceId, OcclusionGrid grid, BakeSettings cfg)
    {
        if (cfg.ShadowSamples <= 1)
            return grid.Occluded(pos, lightPos, faceId) ? 0.0 : 1.0;

        // Soft shadows: jitter the light position within a small sphere and average.
        int hits = 0;
        var rng = new Random(HashPos(pos));   // deterministic per texel -> stable bakes
        for (int i = 0; i < cfg.ShadowSamples; i++)
        {
            Vec3 jitter = new(
                (rng.NextDouble() * 2 - 1) * cfg.LightSize,
                (rng.NextDouble() * 2 - 1) * cfg.LightSize,
                (rng.NextDouble() * 2 - 1) * cfg.LightSize);
            if (!grid.Occluded(pos, lightPos + jitter, faceId)) hits++;
        }
        return (double)hits / cfg.ShadowSamples;
    }

    private static int HashPos(Vec3 p)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + (int)(p.X * 13.17);
            h = h * 31 + (int)(p.Y * 7.91);
            h = h * 31 + (int)(p.Z * 3.53);
            return h;
        }
    }
}
