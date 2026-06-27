using System.Globalization;
using System.Numerics;

namespace PhrawgEngine
{
    /// <summary>
    /// In-engine loader for Valve-220 .map files (the TrenchBroom "Format: Valve"
    /// variant). This is the runtime counterpart to the offline baker's parser:
    /// it recovers brush face polygons (windings) and, crucially, computes the
    /// TRUE albedo UVs from each face's Valve texture axes — the baker's JSON only
    /// carries the lightmap UVs, so albedo UVs have to be derived here.
    ///
    /// Coordinate space: we keep the map's native Quake axes (Z-up) unchanged so
    /// world positions match the baker's JSON exactly. The renderer draws in that
    /// space; the FreeCam flies through it the same way.
    /// </summary>
    public static class MapData
    {
        /// <summary>
        /// Default assumed texture dimensions for albedo UV scaling, until real
        /// material images are loaded and their true sizes are known. Valve UVs
        /// are texel-space, so this only affects how many times a texture tiles.
        /// </summary>
        public const float DefaultTexSize = 64f;

        // Faces whose textures are non-rendering (tool/clip/sky). Mirrors the
        // baker's IsSkipped so face sets line up. NOTE: __TB_empty is the default
        // TrenchBroom texture and is a REAL visible surface here, so it is not
        // skipped — skipping it would drop the whole map.
        private static readonly string[] SkipSubstrings =
            { "glass", "trigger", "clip", "skip", "nodraw", "sky", "origin" };

        public static bool IsSkipped(string texture)
        {
            string t = texture.ToLowerInvariant();
            foreach (var s in SkipSubstrings)
                if (t.Contains(s)) return true;
            return false;
        }

        public sealed class Face
        {
            public string Texture = "";
            public Vector3 Normal;
            public List<Vector3> Winding = new();   // world-space polygon (CCW around Normal)

            // Valve texture axes for albedo UVs.
            public Vector3 UAxis;   public float UOffset; public float UScale = 1f;
            public Vector3 VAxis;   public float VOffset; public float VScale = 1f;

            /// <summary>Albedo UV for a winding vertex, in texture (tiling) space.</summary>
            public Vector2 AlbedoUV(Vector3 p, float texW, float texH)
            {
                float u = (Vector3.Dot(p, UAxis) / UScale + UOffset) / texW;
                float v = (Vector3.Dot(p, VAxis) / VScale + VOffset) / texH;
                return new Vector2(u, v);
            }
        }

        public sealed class OmniLight
        {
            public Vector3 Position;
            public Vector3 Color = Vector3.One;
        }

        public sealed class Result
        {
            public List<Face> Faces = new();         // render faces, in baker face-id order
            public List<OmniLight> Lights = new();
        }

        public static Result Load(string path)
        {
            string[] lines = File.ReadAllLines(path);
            var result = new Result();

            int i = 0, n = lines.Length;
            while (i < n)
            {
                string line = Strip(lines[i]);
                if (line == "{") i = ParseEntity(lines, i + 1, result);
                else i++;
            }
            return result;
        }

        private static int ParseEntity(string[] lines, int i, Result result)
        {
            int n = lines.Length;
            var kv = new Dictionary<string, string>();
            var brushFaces = new List<List<RawFace>>();

            while (i < n)
            {
                string line = Strip(lines[i]);
                if (line == "}") { i++; break; }
                if (line == "{")
                {
                    var (faces, next) = ParseBrush(lines, i + 1);
                    if (faces.Count >= 4) brushFaces.Add(faces);
                    i = next;
                    continue;
                }
                if (line.StartsWith('"'))
                {
                    var (k, v) = ParseKeyValue(line);
                    if (k != null) kv[k] = v!;
                }
                i++;
            }

            // Build windings per brush, emit faces in file order (matches baker's
            // faceId walk: every brush in order, every face, skipped ones dropped).
            foreach (var faces in brushFaces)
            {
                BuildWindings(faces);
                foreach (var rf in faces)
                {
                    if (rf.Winding.Count < 3) continue;        // degenerate, baker drops too
                    if (IsSkipped(rf.Texture)) continue;       // non-render face
                    result.Faces.Add(rf.ToFace());
                }
            }

            if (kv.TryGetValue("classname", out var cls) && cls == "light_omni"
                && kv.TryGetValue("origin", out var os) && TryVec3(os, out var origin))
            {
                var light = new OmniLight { Position = origin };
                if (kv.TryGetValue("light_color", out var c) && TryVec3(c, out var col))
                    light.Color = col / 255f;
                result.Lights.Add(light);
            }

            return i;
        }

        // --- Brush face parsing (Valve-220) ---

        private sealed class RawFace
        {
            public Vector3 P0, P1, P2;
            public Vector3 Normal;
            public float PlaneD;
            public string Texture = "";
            public Vector3 UAxis; public float UOffset; public float UScale = 1f;
            public Vector3 VAxis; public float VOffset; public float VScale = 1f;
            public List<Vector3> Winding = new();

            public Face ToFace() => new()
            {
                Texture = Texture, Normal = Normal, Winding = Winding,
                UAxis = UAxis, UOffset = UOffset, UScale = UScale,
                VAxis = VAxis, VOffset = VOffset, VScale = VScale,
            };
        }

        private static (List<RawFace>, int) ParseBrush(string[] lines, int i)
        {
            int n = lines.Length;
            var faces = new List<RawFace>();
            while (i < n)
            {
                string line = Strip(lines[i]);
                if (line == "}") { i++; break; }
                if (line.Length == 0) { i++; continue; }
                var f = ParseFaceLine(line);
                if (f != null) faces.Add(f);
                i++;
            }
            return (faces, i);
        }

        private static RawFace? ParseFaceLine(string line)
        {
            // ( x y z ) ( x y z ) ( x y z ) TEX [ Ux Uy Uz Uo ] [ Vx Vy Vz Vo ] rot us vs
            var pts = new Vector3[3];
            int idx = 0;
            for (int p = 0; p < 3; p++)
            {
                int open = line.IndexOf('(', idx);
                int close = line.IndexOf(')', open + 1);
                if (open < 0 || close < 0) return null;
                if (!TryVec3(line.Substring(open + 1, close - open - 1), out pts[p])) return null;
                idx = close + 1;
            }

            string rest = line.Substring(idx).Trim();
            int sp = rest.IndexOf(' ');
            if (sp < 0) return null;
            string tex = rest.Substring(0, sp);
            rest = rest.Substring(sp + 1);

            // Two bracket groups for the texture axes.
            if (!ParseBracket(ref rest, out Vector4 u)) return null;
            if (!ParseBracket(ref rest, out Vector4 v)) return null;

            // remaining: rot uscale vscale
            var tail = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            float uscale = tail.Length >= 2 ? ParseF(tail[1]) : 1f;
            float vscale = tail.Length >= 3 ? ParseF(tail[2]) : 1f;

            // Plane normal matches the baker EXACTLY: (p2-p0) x (p1-p0).
            Vector3 nrm = Vector3.Normalize(Vector3.Cross(pts[2] - pts[0], pts[1] - pts[0]));

            return new RawFace
            {
                P0 = pts[0], P1 = pts[1], P2 = pts[2],
                Normal = nrm, PlaneD = Vector3.Dot(nrm, pts[0]),
                Texture = tex,
                UAxis = new Vector3(u.X, u.Y, u.Z), UOffset = u.W, UScale = uscale == 0 ? 1f : uscale,
                VAxis = new Vector3(v.X, v.Y, v.Z), VOffset = v.W, VScale = vscale == 0 ? 1f : vscale,
            };
        }

        private static bool ParseBracket(ref string s, out Vector4 vec)
        {
            vec = default;
            int open = s.IndexOf('[');
            int close = s.IndexOf(']', open + 1);
            if (open < 0 || close < 0) return false;
            var nums = s.Substring(open + 1, close - open - 1)
                        .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (nums.Length < 4) return false;
            vec = new Vector4(ParseF(nums[0]), ParseF(nums[1]), ParseF(nums[2]), ParseF(nums[3]));
            s = s.Substring(close + 1);
            return true;
        }

        // --- Winding via plane clipping (mirrors baker's BrushGeometry) ---

        private const float Huge = 1_000_000f;
        private const float Eps = 1e-3f;

        private static void BuildWindings(List<RawFace> faces)
        {
            foreach (var face in faces)
            {
                var poly = BasePolygon(face.Normal, face.PlaneD);
                foreach (var other in faces)
                {
                    if (ReferenceEquals(other, face)) continue;
                    poly = ClipToBack(poly, other.Normal, other.PlaneD);
                    if (poly.Count == 0) break;
                }
                face.Winding = poly;
            }
        }

        private static List<Vector3> BasePolygon(Vector3 n, float d)
        {
            Vector3 up = MathF.Abs(n.Z) < 0.9f ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
            Vector3 u = Vector3.Normalize(Vector3.Cross(up, n));
            Vector3 v = Vector3.Normalize(Vector3.Cross(n, u));
            Vector3 origin = n * d;
            return new List<Vector3>
            {
                origin - u * Huge - v * Huge,
                origin - u * Huge + v * Huge,
                origin + u * Huge + v * Huge,
                origin + u * Huge - v * Huge,
            };
        }

        private static List<Vector3> ClipToBack(List<Vector3> poly, Vector3 n, float d)
        {
            var result = new List<Vector3>(poly.Count + 4);
            int count = poly.Count;
            if (count == 0) return result;
            for (int i = 0; i < count; i++)
            {
                Vector3 cur = poly[i];
                Vector3 nxt = poly[(i + 1) % count];
                float dc = Vector3.Dot(n, cur) - d;
                float dn = Vector3.Dot(n, nxt) - d;
                bool curIn = dc <= Eps;
                bool nxtIn = dn <= Eps;
                if (curIn) result.Add(cur);
                if (curIn != nxtIn)
                {
                    float t = dc / (dc - dn);
                    result.Add(cur + (nxt - cur) * t);
                }
            }
            return result;
        }

        // --- text helpers ---

        private static (string?, string?) ParseKeyValue(string line)
        {
            int q1 = line.IndexOf('"');
            int q2 = line.IndexOf('"', q1 + 1);
            int q3 = line.IndexOf('"', q2 + 1);
            int q4 = line.IndexOf('"', q3 + 1);
            if (q1 < 0 || q2 < 0 || q3 < 0 || q4 < 0) return (null, null);
            return (line.Substring(q1 + 1, q2 - q1 - 1), line.Substring(q3 + 1, q4 - q3 - 1));
        }

        private static bool TryVec3(string s, out Vector3 v)
        {
            v = default;
            var p = s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) return false;
            v = new Vector3(ParseF(p[0]), ParseF(p[1]), ParseF(p[2]));
            return true;
        }

        private static float ParseF(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        private static string Strip(string line)
        {
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
}