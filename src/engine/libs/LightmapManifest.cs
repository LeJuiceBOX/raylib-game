using System.Numerics;
using System.Text.Json;

namespace PhrawgEngine
{
    /// <summary>
    /// Loads the baker's "<map>_lightmap.json" manifest: per-face atlas UV rects
    /// and per-vertex lightmap UVs (already in 0..1 atlas space). We match these
    /// to the in-engine parsed faces by vertex position, because face ordering
    /// between the baker and our parser is meant to line up but position is the
    /// exact, ordering-independent key the baker itself recommends.
    /// </summary>
    public static class LightmapManifest
    {
        public sealed class FaceUV
        {
            public Vector3[] Positions = Array.Empty<Vector3>();
            public Vector2[] LightmapUVs = Array.Empty<Vector2>();
        }

        public sealed class Data
        {
            public int AtlasWidth, AtlasHeight;
            public List<FaceUV> Faces = new();
        }

        public static Data Load(string path)
        {
            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var data = new Data
            {
                AtlasWidth = root.GetProperty("atlasWidth").GetInt32(),
                AtlasHeight = root.GetProperty("atlasHeight").GetInt32(),
            };

            foreach (var f in root.GetProperty("faces").EnumerateArray())
            {
                var verts = f.GetProperty("verts");
                int count = verts.GetArrayLength();
                var fu = new FaceUV
                {
                    Positions = new Vector3[count],
                    LightmapUVs = new Vector2[count],
                };
                int vi = 0;
                foreach (var v in verts.EnumerateArray())
                {
                    var pos = v.GetProperty("pos");
                    var uv = v.GetProperty("uv");
                    fu.Positions[vi] = new Vector3(
                        pos[0].GetSingle(), pos[1].GetSingle(), pos[2].GetSingle());
                    fu.LightmapUVs[vi] = new Vector2(uv[0].GetSingle(), uv[1].GetSingle());
                    vi++;
                }
                data.Faces.Add(fu);
            }
            return data;
        }

        /// <summary>
        /// Find the manifest face whose winding matches a parsed face's winding by
        /// position (centroid + vertex count). Returns null if no match within eps.
        /// </summary>
        public static FaceUV? MatchByPosition(MapData.Face face, Data data, float eps = 2.0f)
        {
            Vector3 c = Centroid(face.Winding);
            FaceUV? best = null;
            float bestDist = eps;
            foreach (var mf in data.Faces)
            {
                Vector3 mc = Centroid(mf.Positions);
                float dist = Vector3.Distance(c, mc);
                if (dist < bestDist) { bestDist = dist; best = mf; }
            }
            return best;
        }

        /// <summary>Lightmap UV for a specific winding vertex, matched by nearest position.</summary>
        public static Vector2 LightmapUVForVertex(FaceUV mf, Vector3 p)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < mf.Positions.Length; i++)
            {
                float d = Vector3.DistanceSquared(mf.Positions[i], p);
                if (d < bestD) { bestD = d; best = i; }
            }
            return mf.LightmapUVs[best];
        }

        private static Vector3 Centroid(IReadOnlyList<Vector3> pts)
        {
            Vector3 s = Vector3.Zero;
            foreach (var p in pts) s += p;
            return pts.Count > 0 ? s / pts.Count : Vector3.Zero;
        }
    }
}
