using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    /// <summary>
    /// Loads a Valve-220 .map plus its baked lightmap (atlas PNG + manifest JSON)
    /// and renders the static world as lit geometry through the pipeline.
    ///
    /// Pipeline role: implements IRenderable and registers itself. It draws with
    /// the pipeline's lightmap shader (albedo * baked light), not the dynamic
    /// Blinn-Phong world shader. Geometry is grouped into one mesh per texture so
    /// each can later bind its own material; for now every texture gets a
    /// generated checkerboard so the UV layout (and the bake) is clearly visible.
    ///
    /// Coordinate space is the map's native Quake Z-up — positions are used as-is
    /// so they line up exactly with the baker's JSON.
    /// </summary>
    public class WorldRenderer : Entity, IRenderable
    {
        // Scene-configurable: base path without extension. The renderer appends
        // ".map", "_lightmap.json", "_lightmap.png".
        public string mapPath = "src/engine/builtin/maps/demo";

        private readonly List<Model> _models = new();      // one per texture group
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private Texture2D _checker;
        private Texture2D _lightmapTex;
        private bool _built;
        private bool _registered;

        // Where material textures live; the face's texture name (e.g.
        // "horror/stone_brick_02") is resolved under here with common extensions.
        private const string TextureDir = "src/engine/builtin/textures/";

        public override void Load()
        {
            BuildWorld();
            Game.Pipeline?.Register(this);
            _registered = Game.Pipeline != null;
        }

        private unsafe void BuildWorld()
        {
            string mapFile  = mapPath + ".map";
            string jsonFile = mapPath + "_lightmap.json";
            string pngFile  = mapPath + "_lightmap.png";

            if (!File.Exists(mapFile))
            {
                Console.WriteLine($"[WorldRenderer] map not found: {mapFile}");
                return;
            }

            MapData.Result map = MapData.Load(mapFile);

            LightmapManifest.Data? lm = null;
            if (File.Exists(jsonFile)) lm = LightmapManifest.Load(jsonFile);
            else Console.WriteLine($"[WorldRenderer] lightmap manifest not found: {jsonFile}");

            // Albedo placeholder + lightmap atlas.
            _checker = GenChecker(64, 8);
            if (File.Exists(pngFile))
            {
                Image img = Raylib.LoadImage(pngFile);
                _lightmapTex = Raylib.LoadTextureFromImage(img);
                Raylib.SetTextureFilter(_lightmapTex, TextureFilter.Bilinear);
                Raylib.UnloadImage(img);
            }
            else
            {
                Console.WriteLine($"[WorldRenderer] lightmap png not found: {pngFile}");
                _lightmapTex = _checker; // harmless fallback
            }

            // Group faces by texture so each becomes one mesh/model.
            var groups = new Dictionary<string, List<MapData.Face>>();
            foreach (var f in map.Faces)
            {
                if (!groups.TryGetValue(f.Texture, out var list))
                    groups[f.Texture] = list = new List<MapData.Face>();
                list.Add(f);
            }

            foreach (var (tex, faces) in groups)
            {
                Model? model = BuildGroupModel(tex, faces, lm);
                if (model.HasValue) _models.Add(model.Value);
            }

            _built = _models.Count > 0;
            Console.WriteLine($"[WorldRenderer] loaded {map.Faces.Count} faces, " +
                              $"{groups.Count} texture groups, {_models.Count} models.");

            HandleBrushEntities(map);
        }

        /// <summary>
        /// Hook for non-worldspawn brush entities (doors, platforms, triggers).
        /// Intentionally empty for now — these will become their own dynamic
        /// renderers/movers rather than baked static geometry.
        /// </summary>
        private void HandleBrushEntities(MapData.Result map)
        {
            // TODO: split brush entities (func_door, func_rotating, etc.) out of
            // the static world and give them their own movable renderers + light
            // probes. Left empty deliberately.
        }

        private unsafe Model? BuildGroupModel(string texture, List<MapData.Face> faces, LightmapManifest.Data? lm)
        {
            // Triangulate every face as a fan; collect interleaved vertex arrays.
            var positions = new List<Vector3>();
            var albedoUVs = new List<Vector2>();
            var lightUVs  = new List<Vector2>();
            var normals   = new List<Vector3>();

            foreach (var face in faces)
            {
                LightmapManifest.FaceUV? mf =
                    lm != null ? LightmapManifest.MatchByPosition(face, lm) : null;

                int n = face.Winding.Count;
                for (int i = 1; i < n - 1; i++)
                {
                    // The Z-up -> Y-up conversion (x, z, -y) is a reflection, which
                    // inverts triangle winding. Reverse the fan order so front faces
                    // stay front-facing (otherwise walls render see-through).
                    AddVertex(face, face.Winding[0], mf, positions, albedoUVs, lightUVs, normals);
                    AddVertex(face, face.Winding[i + 1], mf, positions, albedoUVs, lightUVs, normals);
                    AddVertex(face, face.Winding[i], mf, positions, albedoUVs, lightUVs, normals);
                }
            }

            int vcount = positions.Count;
            if (vcount == 0) return null;

            // Build the mesh by allocating native arrays directly. We avoid the
            // higher-level Mesh.Alloc* helpers because their availability varies
            // across raylib-cs versions; raw float* fields + MemAlloc are stable.
            var mesh = new Mesh
            {
                VertexCount = vcount,
                TriangleCount = vcount / 3,
            };

            uint fStride3 = (uint)(vcount * 3 * sizeof(float));
            uint fStride2 = (uint)(vcount * 2 * sizeof(float));

            mesh.Vertices   = (float*)Raylib.MemAlloc(fStride3);
            mesh.Normals    = (float*)Raylib.MemAlloc(fStride3);
            mesh.TexCoords  = (float*)Raylib.MemAlloc(fStride2);
            mesh.TexCoords2 = (float*)Raylib.MemAlloc(fStride2);

            for (int i = 0; i < vcount; i++)
            {
                mesh.Vertices[i * 3 + 0] = positions[i].X;
                mesh.Vertices[i * 3 + 1] = positions[i].Y;
                mesh.Vertices[i * 3 + 2] = positions[i].Z;

                mesh.Normals[i * 3 + 0] = normals[i].X;
                mesh.Normals[i * 3 + 1] = normals[i].Y;
                mesh.Normals[i * 3 + 2] = normals[i].Z;

                mesh.TexCoords[i * 2 + 0] = albedoUVs[i].X;
                mesh.TexCoords[i * 2 + 1] = albedoUVs[i].Y;

                mesh.TexCoords2[i * 2 + 0] = lightUVs[i].X;
                mesh.TexCoords2[i * 2 + 1] = lightUVs[i].Y;
            }

            Raylib.UploadMesh(ref mesh, false);
            Model model = Raylib.LoadModelFromMesh(mesh);

            // Resolve the material texture (falls back to the checkerboard when the
            // image file isn't present), bind it to map 0 (texture0 in the shader).
            model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = ResolveAlbedo(texture);
            return model;
        }

        /// <summary>Quake Z-up world coords -> raylib Y-up: (x, y, z) -> (x, z, -y).</summary>
        private static Vector3 ToYUp(Vector3 q) => new(q.X, q.Z, -q.Y);

        private static void AddVertex(
            MapData.Face face, Vector3 p, LightmapManifest.FaceUV? mf,
            List<Vector3> pos, List<Vector2> alb, List<Vector2> light, List<Vector3> nrm)
        {
            // UVs and lightmap matching use the ORIGINAL Quake-space position,
            // because the Valve texture axes and the baker's JSON are both authored
            // in that space. Only the emitted vertex position/normal are flipped
            // to raylib's Y-up so the level isn't rendered sideways.
            pos.Add(ToYUp(p));
            nrm.Add(ToYUp(face.Normal));
            alb.Add(face.AlbedoUV(p, MapData.DefaultTexSize, MapData.DefaultTexSize));
            light.Add(mf != null ? LightmapManifest.LightmapUVForVertex(mf, p) : Vector2.Zero);
        }

        // IRenderable: draw all texture-group models with the lightmap shader.
        public unsafe void Render(RenderPipeline pipeline)
        {
            if (!_built) return;

            Shader sh = pipeline.LightmapShader;
            foreach (var model in _models)
            {
                // Apply the lightmap shader and bind the atlas to its sampler.
                model.Materials[0].Shader = sh;
                Raylib.SetShaderValueTexture(sh, pipeline.LightmapTexLoc, _lightmapTex);
                Raylib.DrawModel(model, Vector3.Zero, 1f, Color.White);
            }
        }

        /// <summary>
        /// Resolve a face texture name (e.g. "horror/stone_brick_02") to a loaded
        /// texture under TextureDir, trying common extensions. Falls back to the
        /// generated checkerboard if no file is found. Results are cached.
        /// </summary>
        private Texture2D ResolveAlbedo(string texture)
        {
            if (_textureCache.TryGetValue(texture, out var cached)) return cached;

            Texture2D result = _checker;
            string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".tga" };
            foreach (var ext in exts)
            {
                string path = TextureDir + texture + ext;
                if (File.Exists(path))
                {
                    Image img = Raylib.LoadImage(path);
                    Texture2D t = Raylib.LoadTextureFromImage(img);
                    Raylib.UnloadImage(img);
                    Raylib.SetTextureFilter(t, TextureFilter.Bilinear);
                    Raylib.SetTextureWrap(t, TextureWrap.Repeat);
                    result = t;
                    Console.WriteLine($"[WorldRenderer] texture: {path}");
                    break;
                }
            }
            if (result.Id == _checker.Id)
                Console.WriteLine($"[WorldRenderer] texture not found for '{texture}', using checker.");

            _textureCache[texture] = result;
            return result;
        }

        /// <summary>Simple two-tone checkerboard so UVs/bake read clearly.</summary>
        private static Texture2D GenChecker(int size, int checks)
        {
            Image img = Raylib.GenImageChecked(size, size, size / checks, size / checks,
                                               new Color(200, 200, 200, 255),
                                               new Color(120, 120, 120, 255));
            Texture2D t = Raylib.LoadTextureFromImage(img);
            Raylib.SetTextureFilter(t, TextureFilter.Point);
            Raylib.SetTextureWrap(t, TextureWrap.Repeat);
            Raylib.UnloadImage(img);
            return t;
        }
    }
}