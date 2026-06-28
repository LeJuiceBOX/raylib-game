using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public class WorldRenderer : Entity, IRenderable
    {
        public string mapPath = "src/engine/builtin/maps/gears";

        private readonly List<Model> _models = new();
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private Texture2D _checker;
        private Texture2D _lightmapTex;
        private bool _built;
        private bool _registered;

        private const string TextureDir = "src/engine/builtin/textures/";

        public override void Update(float dt)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.R))
            {
                ApplySettings(mapPath+"/settings.json");
            }
        }

        public override void Load()
        {
            BuildWorld();
            Game.Pipeline?.Register(this);
            _registered = Game.Pipeline != null;
            ApplySettings(mapPath + "/settings.json");
        }

        private void ApplySettings(string settingsPath)
        {
            if (!File.Exists(settingsPath) || Game.Pipeline == null)
            {
                Console.WriteLine($"[WorldRenderer] settings not found or pipeline unavailable: {settingsPath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(settingsPath);
                var settings = Json.Deserialize(json);

                if (settings.TryGetValue("Lighting", out var lightingObj) &&
                    lightingObj is Dictionary<string, object?> lighting)
                {
                    if (lighting.TryGetValue("Exposure", out var exposureObj) &&
                        exposureObj is double exposure)
                    {
                        Game.Pipeline.Exposure = (float)exposure;
                    }

                    if (lighting.TryGetValue("Sun", out var sunObj) &&
                        sunObj is Dictionary<string, object?> sun)
                    {
                        if (sun.TryGetValue("Direction", out var dirObj) &&
                            dirObj is List<object?> dir && dir.Count == 3)
                            Game.Pipeline.Light.Direction = Vector3.Normalize(ToVector3(dir));

                        if (sun.TryGetValue("Color", out var colObj) &&
                            colObj is List<object?> col && col.Count == 3)
                            Game.Pipeline.Light.Color = ToVector3(col);

                        if (sun.TryGetValue("Ambient", out var ambObj) &&
                            ambObj is List<object?> amb && amb.Count == 3)
                            Game.Pipeline.Light.Ambient = ToVector3(amb);
                    }
                }

                Console.WriteLine($"[WorldRenderer] settings applied from {settingsPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WorldRenderer] failed to parse settings: {ex.Message}");
            }
        }

        private static Vector3 ToVector3(List<object?> list)
        {
            float F(object? v) => v is double d ? (float)d : v is long l ? (float)l : 0f;
            return new Vector3(F(list[0]), F(list[1]), F(list[2]));
        }

        private unsafe void BuildWorld()
        {
            string mapFile  = mapPath + "/level.map";
            string jsonFile = mapPath + "/lighting/lightmap_data.json";
            string pngFile  = mapPath + "/lighting/lightmap.png";

            if (!File.Exists(mapFile))
            {
                Console.WriteLine($"[WorldRenderer] map not found: {mapFile}");
                return;
            }

            MapData.Result map = MapData.Load(mapFile);

            LightmapManifest.Data? lm = null;
            if (File.Exists(jsonFile)) lm = LightmapManifest.Load(jsonFile);
            else Console.WriteLine($"[WorldRenderer] lightmap manifest not found: {jsonFile}");

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
                _lightmapTex = _checker;
            }

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

        private void HandleBrushEntities(MapData.Result map) { }

        private unsafe Model? BuildGroupModel(string texture, List<MapData.Face> faces, LightmapManifest.Data? lm)
        {
            var positions = new List<Vector3>();
            var albedoUVs = new List<Vector2>();
            var lightUVs  = new List<Vector2>();
            var normals   = new List<Vector3>();

        int matchedFaces = 0;
        foreach (var face in faces)
        {
            LightmapManifest.FaceUV? mf =
                lm != null ? LightmapManifest.MatchByPosition(face, lm) : null;
            if (mf != null) matchedFaces++;

            int n = face.Winding.Count;
            for (int i = 1; i < n - 1; i++)
            {
                AddVertex(face, face.Winding[0], mf, positions, albedoUVs, lightUVs, normals);
                AddVertex(face, face.Winding[i + 1], mf, positions, albedoUVs, lightUVs, normals);
                AddVertex(face, face.Winding[i], mf, positions, albedoUVs, lightUVs, normals);
            }
        }

        Console.WriteLine($"[WorldRenderer] group '{texture}': {faces.Count} faces, {matchedFaces} lightmap matched");
                    
            int vcount = positions.Count;
            if (vcount == 0) return null;

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

            // Albedo in slot 0 (texture0 in the shader).
            model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Texture = ResolveAlbedo(texture);
            // Lightmap atlas in slot 1 (Metalness map). The pipeline wires
            // locs[MAP_METALNESS] → "lightmap" uniform so DrawMesh binds it automatically.
            model.Materials[0].Maps[(int)MaterialMapIndex.Metalness].Texture = _lightmapTex;
            return model;
        }

        private static Vector3 ToYUp(Vector3 q) => new(q.X, q.Z, -q.Y);

        private static void AddVertex(
            MapData.Face face, Vector3 p, LightmapManifest.FaceUV? mf,
            List<Vector3> pos, List<Vector2> alb, List<Vector2> light, List<Vector3> nrm)
        {
            pos.Add(ToYUp(p));
            nrm.Add(ToYUp(face.Normal));
            alb.Add(face.AlbedoUV(p, MapData.DefaultTexSize, MapData.DefaultTexSize));
            light.Add(mf != null ? LightmapManifest.LightmapUVForVertex(mf, p) : Vector2.Zero);
        }

        public unsafe void Render(RenderPipeline pipeline)
        {
            if (!_built) return;

            Shader sh = pipeline.LightmapShader;
            foreach (var model in _models)
            {
                // Apply the lightmap shader. The lightmap atlas texture is already
                // in Maps[Metalness] (slot 1) from BuildGroupModel; the pipeline
                // wired locs[MAP_METALNESS] → "lightmap", so DrawMesh binds it.
                model.Materials[0].Shader = sh;
                Raylib.DrawModel(model, Vector3.Zero, 1f, Color.White);
            }
        }

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