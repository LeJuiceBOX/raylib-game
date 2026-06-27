using System.Diagnostics;
using System.Globalization;

namespace MapLightBaker;

internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        if (args.Length < 1)
        {
            Console.WriteLine(
                "Usage: maplightbaker <map.map> [options]\n" +
                "  --out <dir>            output directory (default: alongside the map)\n" +
                "  --texels <f>          texels per world unit (default 0.25 = 4 units/texel)\n" +
                "  --atlas <int>         atlas width in pixels (default 1024)\n" +
                "  --range-scale <f>     world reach per light_radius unit (default 200)\n" +
                "  --energy-scale <f>    brightness per light_energy unit (default 4)\n" +
                "  --ambient <f>         flat ambient floor 0..1 (default 0.08)\n" +
                "  --shadow-samples <n>  >1 = soft shadows (default 1 = hard)\n" +
                "  --light-size <f>      soft-shadow light radius (default 8)\n");
            return 1;
        }

        string mapPath = args[0];
        if (!File.Exists(mapPath))
        {
            Console.Error.WriteLine($"Map not found: {mapPath}");
            return 1;
        }

        var cfg = new BakeSettings();
        string outDir = Path.GetDirectoryName(Path.GetFullPath(mapPath)) ?? ".";

        for (int i = 1; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--out": outDir = args[++i]; break;
                case "--texels": cfg.TexelsPerUnit = D(args[++i]); break;
                case "--atlas": cfg.AtlasWidth = int.Parse(args[++i]); break;
                case "--range-scale": cfg.RangeScale = D(args[++i]); break;
                case "--energy-scale": cfg.EnergyScale = D(args[++i]); break;
                case "--ambient": cfg.Ambient = D(args[++i]); break;
                case "--shadow-samples": cfg.ShadowSamples = int.Parse(args[++i]); break;
                case "--light-size": cfg.LightSize = D(args[++i]); break;
            }
        }
        Directory.CreateDirectory(outDir);

        var sw = Stopwatch.StartNew();

        // 1. Parse.
        Console.WriteLine($"Parsing {mapPath} ...");
        var (brushes, lights) = MapParser.Parse(mapPath);
        Console.WriteLine($"  {brushes.Count} brushes, {lights.Count} omni lights");

        if (lights.Count == 0)
            Console.WriteLine("  WARNING: no light_omni entities found; output will be ambient-only.");

        // 2. Build face windings.
        foreach (var b in brushes) BrushGeometry.BuildWindings(b);

        // Flatten faces, skipping degenerate ones and common non-lit textures.
        var faces = new List<(BrushFace face, int id)>();
        int id = 0;
        foreach (var b in brushes)
            foreach (var f in b.Faces)
            {
                if (f.Winding.Count >= 3 && !IsSkipped(f.Texture))
                    faces.Add((f, id));
                id++;
            }
        Console.WriteLine($"  {faces.Count} lit faces");

        // 3. Triangulate for the occlusion structure (fan triangulation per face).
        var tris = new List<Triangle>();
        foreach (var (f, fid) in faces)
        {
            var w = f.Winding;
            for (int t = 1; t + 1 < w.Count; t++)
                tris.Add(new Triangle { A = w[0], B = w[t], C = w[t + 1], Normal = f.Plane.N, FaceId = fid });
        }
        // Also add skipped faces as occluders? No — glass/triggers shouldn't cast.
        Console.WriteLine($"  {tris.Count} triangles for occlusion");

        var grid = new OcclusionGrid(tris, cellSize: 64);

        // 4. Lightmap layout.
        var (rects, atlasW, atlasH) = LightmapLayout.Build(faces, cfg.TexelsPerUnit, cfg.AtlasWidth, cfg.Padding);
        Console.WriteLine($"  atlas {atlasW}x{atlasH}, {rects.Count} face rects");

        // 5. Bake.
        Console.WriteLine("Baking ...");
        var (rgb, covered) = Baker.Bake(rects, grid, lights, atlasW, atlasH, cfg);

        // 6. Dilate + write.
        Output.Dilate(rgb, covered, atlasW, atlasH, iterations: cfg.Padding + 2);

        string pngPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(mapPath) + "_lightmap.png");
        string jsonPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(mapPath) + "_lightmap.json");
        Output.WritePng(pngPath, rgb, atlasW, atlasH);
        Output.WriteManifest(jsonPath, rects, faces, atlasW, atlasH);

        sw.Stop();
        Console.WriteLine($"Done in {sw.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine($"  {pngPath}");
        Console.WriteLine($"  {jsonPath}");
        return 0;
    }

    // Textures that shouldn't receive/own lightmap texels (transparent, tool, trigger).
    private static bool IsSkipped(string tex)
    {
        string t = tex.ToLowerInvariant();
        return t.Contains("glass") || t.Contains("trigger") || t.Contains("skip") ||
               t.Contains("clip") || t.Contains("nodraw") || t.Contains("sky") || t.Contains("origin");
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
