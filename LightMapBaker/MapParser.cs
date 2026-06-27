using System.Globalization;

namespace MapLightBaker;

/// <summary>
/// Parser for the Valve 220 .map text format (the one TrenchBroom emits with
/// "// Format: Valve"). We only need three things out of it:
///   1. brush faces (plane points + texture) from every entity that has brushes
///   2. light_omni entities (origin / radius / energy)
/// Everything else (texture axes, scales, other entity keys) is skipped.
/// </summary>
public static class MapParser
{
    public static (List<Brush> brushes, List<OmniLight> lights) Parse(string path)
    {
        var brushes = new List<Brush>();
        var lights = new List<OmniLight>();

        string[] lines = File.ReadAllLines(path);
        int i = 0;
        int n = lines.Length;

        // Top level is a sequence of entity blocks: { ... }
        while (i < n)
        {
            string line = Strip(lines[i]);
            if (line == "{")
            {
                i = ParseEntity(lines, i + 1, brushes, lights);
            }
            else
            {
                i++;
            }
        }

        return (brushes, lights);
    }

    /// <summary>
    /// Parse one entity block. Returns the index just past the entity's closing brace.
    /// An entity is key/value pairs and zero or more brush sub-blocks.
    /// </summary>
    private static int ParseEntity(string[] lines, int i, List<Brush> brushes, List<OmniLight> lights)
    {
        int n = lines.Length;
        var kv = new Dictionary<string, string>();
        var entityBrushes = new List<Brush>();

        while (i < n)
        {
            string line = Strip(lines[i]);

            if (line == "}")
            {
                i++;            // consume closing brace
                break;
            }
            if (line == "{")
            {
                // A brush sub-block.
                var (brush, next) = ParseBrush(lines, i + 1);
                if (brush.Faces.Count >= 4)   // a valid convex brush needs >= 4 planes
                    entityBrushes.Add(brush);
                i = next;
                continue;
            }
            if (line.StartsWith('"'))
            {
                var (key, val) = ParseKeyValue(line);
                if (key != null) kv[key] = val!;
            }
            i++;
        }

        // Brushes from any entity contribute to the static world geometry.
        // (worldspawn and func_group/func_detail are all bakeable static geometry.)
        brushes.AddRange(entityBrushes);

        // Light entities.
        if (kv.TryGetValue("classname", out var cls) && cls == "light_omni")
        {
            if (kv.TryGetValue("origin", out var originStr) &&
                TryParseVec3(originStr, out var origin))
            {
                var light = new OmniLight { Position = origin };
                if (kv.TryGetValue("light_radius", out var r) &&
                    double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rv))
                    light.RawRadius = rv;
                if (kv.TryGetValue("light_energy", out var e) &&
                    double.TryParse(e, NumberStyles.Float, CultureInfo.InvariantCulture, out var ev))
                    light.RawEnergy = ev;
                if (kv.TryGetValue("light_color", out var c) && TryParseVec3(c, out var col))
                    light.Color = new Vec3(col.X / 255.0, col.Y / 255.0, col.Z / 255.0);
                lights.Add(light);
            }
        }

        return i;
    }

    /// <summary>Parse a brush block. Returns the brush and the index past its closing brace.</summary>
    private static (Brush brush, int next) ParseBrush(string[] lines, int i)
    {
        int n = lines.Length;
        var brush = new Brush();

        while (i < n)
        {
            string line = Strip(lines[i]);
            if (line == "}") { i++; break; }
            if (line.Length == 0) { i++; continue; }

            var face = ParseFaceLine(line);
            if (face != null) brush.Faces.Add(face);
            i++;
        }
        return (brush, i);
    }

    /// <summary>
    /// A Valve-220 face line:
    /// ( x y z ) ( x y z ) ( x y z ) TEXTURE [ ax ay az ao ] [ bx by bz bo ] rot sx sy
    /// We only need the three points and the texture name.
    /// </summary>
    private static BrushFace? ParseFaceLine(string line)
    {
        // Pull the three parenthesized points.
        var pts = new List<Vec3>(3);
        int idx = 0;
        for (int p = 0; p < 3; p++)
        {
            int open = line.IndexOf('(', idx);
            int close = line.IndexOf(')', open + 1);
            if (open < 0 || close < 0) return null;
            string inner = line.Substring(open + 1, close - open - 1).Trim();
            if (!TryParseVec3(inner, out var v)) return null;
            pts.Add(v);
            idx = close + 1;
        }

        // The texture name is the first whitespace-delimited token after the
        // third ')'. The remainder (texture axes etc.) we ignore.
        string rest = line.Substring(idx).Trim();
        int sp = rest.IndexOf(' ');
        string tex = sp < 0 ? rest : rest.Substring(0, sp);

        return new BrushFace
        {
            P0 = pts[0],
            P1 = pts[1],
            P2 = pts[2],
            Plane = Plane.FromPoints(pts[0], pts[1], pts[2]),
            Texture = tex,
        };
    }

    private static (string? key, string? val) ParseKeyValue(string line)
    {
        // "key" "value"
        int q1 = line.IndexOf('"');
        int q2 = line.IndexOf('"', q1 + 1);
        int q3 = line.IndexOf('"', q2 + 1);
        int q4 = line.IndexOf('"', q3 + 1);
        if (q1 < 0 || q2 < 0 || q3 < 0 || q4 < 0) return (null, null);
        string key = line.Substring(q1 + 1, q2 - q1 - 1);
        string val = line.Substring(q3 + 1, q4 - q3 - 1);
        return (key, val);
    }

    private static bool TryParseVec3(string s, out Vec3 v)
    {
        v = default;
        var parts = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
        v = new Vec3(x, y, z);
        return true;
    }

    /// <summary>Remove a trailing // comment and surrounding whitespace.</summary>
    private static string Strip(string line)
    {
        // Only strip // when it's not inside a quoted string. Map comments are
        // always at line start or after geometry, never inside quotes here, but
        // be safe about quoted values that might contain slashes.
        bool inQuote = false;
        for (int k = 0; k < line.Length - 1; k++)
        {
            if (line[k] == '"') inQuote = !inQuote;
            if (!inQuote && line[k] == '/' && line[k + 1] == '/')
                return line.Substring(0, k).Trim();
        }
        return line.Trim();
    }
}
