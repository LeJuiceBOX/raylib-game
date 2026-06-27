using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    /// <summary>
    /// A single directional light + flat ambient. This is the HL2-era model:
    /// one dominant directional source, ambient floor so shadows aren't black.
    /// </summary>
    public sealed class DirectionalLight
    {
        /// <summary>Direction the light travels (will be normalized).</summary>
        public Vector3 Direction = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f));

        /// <summary>Linear RGB * intensity. >1 channels are fine (HDR).</summary>
        public Vector3 Color = new(1.1f, 1.05f, 0.95f);

        /// <summary>Flat ambient added everywhere, linear RGB.</summary>
        public Vector3 Ambient = new(0.10f, 0.11f, 0.14f);
    }

    /// <summary>
    /// Anything the pipeline can draw. Renderer components implement this and
    /// register with <see cref="RenderPipeline"/> instead of issuing their own
    /// immediate draw calls in Component.Draw3D().
    /// </summary>
    public interface IRenderable
    {
        /// <summary>
        /// Issue the actual mesh draw. The pipeline has already bound the HDR
        /// target, started 3D mode, and set the shared world shader + lights.
        /// </summary>
        void Render(RenderPipeline pipeline);
    }

    /// <summary>
    /// Owns the HDR pipeline: a float render target, the shared world shader,
    /// the tonemap composite pass, the scene light, and the renderer registry.
    ///
    /// Frame shape:
    ///   1. bind HDR target, clear
    ///   2. BeginMode3D(camera)  -> draw every registered IRenderable (shaded)
    ///   3. EndMode3D, unbind
    ///   4. fullscreen tonemap pass: HDR target -> backbuffer
    /// </summary>
    public sealed class RenderPipeline
    {
        public DirectionalLight Light { get; } = new();
        public float Exposure = 1.0f;

        private readonly List<IRenderable> _renderables = new();

        private RenderTexture2D _hdr;
        private int _width, _height;
        private bool _ready;

        // World shader + cached uniform locations.
        private Shader _world;
        private int _locViewPos, _locLightDir, _locLightColor, _locAmbient, _locSpec, _locShine;

        // Tonemap shader.
        private Shader _tonemap;
        private int _locExposure;

        // World lightmap shader (baked albedo*lightmap path) + its locations.
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

            // HDR float color target. UncompressedR16G16B16A16 keeps linear HDR
            // headroom; the tonemap pass compresses it to the 8-bit backbuffer.
            _hdr = Raylib.LoadRenderTexture(width, height);
            // Upgrade the color attachment to a float format if available.
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
            // Tell raylib where the second UV channel attribute lives so it binds
            // mesh.texcoords2 to vertexTexCoord2. Index 11 == SHADER_LOC_VERTEX_TEXCOORD02
            // in raylib's RL_DEFAULT shader location layout; using the int avoids
            // any enum-casing differences across raylib-cs versions.
            unsafe
            {
                _lightmap.Locs[11] = Raylib.GetShaderLocationAttrib(_lightmap, "vertexTexCoord2");
            }

            _ready = true;
        }

        public void Register(IRenderable r)
        {
            if (!_renderables.Contains(r)) _renderables.Add(r);
        }

        public void Unregister(IRenderable r) => _renderables.Remove(r);

        /// <summary>Push current light + camera uniforms into the world shader.</summary>
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

            // Lightmap path shares the ambient floor.
            Raylib.SetShaderValue(_lightmap, _locLmAmbient, Light.Ambient, ShaderUniformDataType.Vec3);
        }

        /// <summary>
        /// Render the 3D scene into HDR and composite to the backbuffer.
        /// Call this between Raylib.BeginDrawing()/EndDrawing(), where the old
        /// BeginMode3D block used to be.
        /// </summary>
        public void RenderFrame(Camera3D cam)
        {
            if (!_ready) return;

            UploadFrameUniforms(cam);

            // --- 1+2: scene into HDR target ---
            Raylib.BeginTextureMode(_hdr);
                Raylib.ClearBackground(Color.Black);
                Raylib.BeginMode3D(cam);
                    foreach (var r in _renderables) r.Render(this);
                Raylib.EndMode3D();
            Raylib.EndTextureMode();

            // --- 3: tonemap composite to backbuffer ---
            Raylib.SetShaderValue(_tonemap, _locExposure, Exposure, ShaderUniformDataType.Float);
            Raylib.BeginShaderMode(_tonemap);
                // RenderTextures are y-flipped vs screen, so source height is negative.
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