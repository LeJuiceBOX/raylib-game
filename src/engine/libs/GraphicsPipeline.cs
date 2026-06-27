using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public sealed class DirectionalLight
    {
        public Vector3 Direction = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f));
        public Vector3 Color = new(1.1f, 1.05f, 0.95f);
        public Vector3 Ambient = new(0.10f, 0.11f, 0.14f);
    }

    public interface IRenderable
    {
        void Render(RenderPipeline pipeline);
    }

    public sealed class RenderPipeline
    {
        public DirectionalLight Light { get; } = new();
        public float Exposure = 1.0f;

        private readonly List<IRenderable> _renderables = new();

        private RenderTexture2D _hdr;
        private int _width, _height;
        private bool _ready;

        private Shader _world;
        private int _locViewPos, _locLightDir, _locLightColor, _locAmbient, _locSpec, _locShine;

        private Shader _tonemap;
        private int _locExposure;

        private Shader _lightmap;
        private int _locLmAmbient, _locLmLightmapTex;

        public Shader WorldShader => _world;
        public Shader LightmapShader => _lightmap;
        public int LightmapTexLoc => _locLmLightmapTex;

        private const string ShaderDir = "src/engine/builtin/shaders/";

        public void Init(int width, int height)
        {
            _width = width;
            _height = height;

            _hdr = Raylib.LoadRenderTexture(width, height);
            Raylib.SetTextureFilter(_hdr.Texture, TextureFilter.Bilinear);

            _world = Raylib.LoadShader(ShaderDir + "world.vert", ShaderDir + "world.frag");
            _locViewPos    = Raylib.GetShaderLocation(_world, "viewPos");
            _locLightDir   = Raylib.GetShaderLocation(_world, "dirLightDir");
            _locLightColor = Raylib.GetShaderLocation(_world, "dirLightColor");
            _locAmbient    = Raylib.GetShaderLocation(_world, "ambientColor");
            _locSpec       = Raylib.GetShaderLocation(_world, "specStrength");
            _locShine      = Raylib.GetShaderLocation(_world, "shininess");

            _tonemap = Raylib.LoadShader(null, ShaderDir + "tonemap.frag");
            _locExposure = Raylib.GetShaderLocation(_tonemap, "exposure");

            _lightmap = Raylib.LoadShader(ShaderDir + "world_lightmap.vert",
                                          ShaderDir + "world_lightmap.frag");
            _locLmAmbient     = Raylib.GetShaderLocation(_lightmap, "ambientColor");
            _locLmLightmapTex = Raylib.GetShaderLocation(_lightmap, "lightmap");
            unsafe
            {
                // SHADER_LOC_VERTEX_TEXCOORD02 == 2 in raylib's enum; explicitly set it
                // so DrawMesh binds mesh.TexCoords2 to vertexTexCoord2.
                _lightmap.Locs[2] = Raylib.GetShaderLocationAttrib(_lightmap, "vertexTexCoord2");

                // Route the "lightmap" sampler through material map slot 1 (Metalness).
                // DrawMesh iterates material.maps[] and for each slot i with a valid
                // texture it calls: rlActiveTextureSlot(i) + rlSetUniformSampler(locs[15+i], i).
                // Setting locs[16] (MAP_METALNESS = 15+1) to the "lightmap" location means
                // DrawMesh automatically binds maps[1].Texture to GL_TEXTURE1 and sets
                // the lightmap sampler to unit 1, without any manual SetShaderValueTexture call.
                _lightmap.Locs[16] = _locLmLightmapTex;
            }

            _ready = true;
        }

        public void Register(IRenderable r)
        {
            if (!_renderables.Contains(r)) _renderables.Add(r);
        }

        public void Unregister(IRenderable r) => _renderables.Remove(r);

        private void UploadFrameUniforms(Camera3D cam)
        {
            Vector3 dir = Vector3.Normalize(Light.Direction);
            Raylib.SetShaderValue(_world, _locViewPos,    cam.Position, ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(_world, _locLightDir,   dir,          ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(_world, _locLightColor, Light.Color,  ShaderUniformDataType.Vec3);
            Raylib.SetShaderValue(_world, _locAmbient,    Light.Ambient,ShaderUniformDataType.Vec3);

            float spec = 0.25f, shine = 32f;
            Raylib.SetShaderValue(_world, _locSpec,  spec,  ShaderUniformDataType.Float);
            Raylib.SetShaderValue(_world, _locShine, shine, ShaderUniformDataType.Float);

            Raylib.SetShaderValue(_lightmap, _locLmAmbient, Light.Ambient, ShaderUniformDataType.Vec3);
        }

        public void RenderFrame(Camera3D cam)
        {
            if (!_ready) return;

            UploadFrameUniforms(cam);

            Raylib.BeginTextureMode(_hdr);
                Raylib.ClearBackground(Color.Black);
                Raylib.BeginMode3D(cam);
                    foreach (var r in _renderables) r.Render(this);
                Raylib.EndMode3D();
            Raylib.EndTextureMode();

            Raylib.SetShaderValue(_tonemap, _locExposure, Exposure, ShaderUniformDataType.Float);
            Raylib.BeginShaderMode(_tonemap);
                var src = new Rectangle(0, 0, _width, -_height);
                Raylib.DrawTextureRec(_hdr.Texture, src, Vector2.Zero, Color.White);
            Raylib.EndShaderMode();
        }

        public void Resize(int width, int height)
        {
            if (!_ready || (width == _width && height == _height)) return;
            Raylib.UnloadRenderTexture(_hdr);
            _width = width; _height = height;
            _hdr = Raylib.LoadRenderTexture(width, height);
            Raylib.SetTextureFilter(_hdr.Texture, TextureFilter.Bilinear);
        }

        public void Shutdown()
        {
            if (!_ready) return;
            Raylib.UnloadShader(_world);
            Raylib.UnloadShader(_tonemap);
            Raylib.UnloadShader(_lightmap);
            Raylib.UnloadRenderTexture(_hdr);
            _ready = false;
        }
    }
}