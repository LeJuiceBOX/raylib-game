using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public enum MeshShape { Cube, Sphere, Plane }

    /// <summary>
    /// A renderer driven by the <see cref="RenderPipeline"/> rather than by the
    /// immediate-mode Component.Draw3D() callback. On Load it builds a model and
    /// registers itself; the pipeline draws it (shaded, into the HDR target) each
    /// frame. This is the migration target away from SimpleSphereRenderer etc.
    /// </summary>
    public class MeshRenderer : Component, IRenderable
    {
        // Scene-configurable fields (bound by SceneLoader).
        public MeshShape shape = MeshShape.Cube;
        public Vector3 size = Vector3.One;     // cube extents / plane size.xz; sphere uses size.X as radius
        public Color color = Color.White;

        private Transform _t = null!;
        private Model _model;
        private bool _built;
        private bool _registered;

        public override void Load()
        {
            _t = Owner.GetComponent<Transform>();
            BuildModel();
            TryRegister();
        }

        private unsafe void BuildModel()
        {
            Mesh mesh;
            switch (shape)
            {
                case MeshShape.Sphere:
                    mesh = Raylib.GenMeshSphere(size.X, 24, 24);
                    break;
                case MeshShape.Plane:
                    mesh = Raylib.GenMeshPlane(size.X, size.Z, 1, 1);
                    break;
                case MeshShape.Cube:
                default:
                    mesh = Raylib.GenMeshCube(size.X, size.Y, size.Z);
                    break;
            }

            _model = Raylib.LoadModelFromMesh(mesh);

            // Tint via the albedo material map color (sampled as texture0 * colDiffuse
            // in the world shader; with no texture, raylib supplies a 1x1 white).
            _model.Materials[0].Maps[(int)MaterialMapIndex.Albedo].Color = color;

            _built = true;
        }

        /// <summary>
        /// Bind the pipeline's shared world shader to this model's material.
        /// Done lazily because the pipeline may finish Init after components load.
        /// </summary>
        private unsafe void EnsureShader(RenderPipeline pipeline)
        {
            _model.Materials[0].Shader = pipeline.WorldShader;
        }

        private void TryRegister()
        {
            if (_registered) return;
            Game.Pipeline?.Register(this);
            _registered = Game.Pipeline != null;
        }

        public override void Update(float dt)
        {
            // In case the pipeline came up after this component's Load().
            if (!_registered) TryRegister();
        }

        // IRenderable: pipeline calls this inside BeginMode3D, shader+lights set.
        public unsafe void Render(RenderPipeline pipeline)
        {
            if (!_built) return;
            EnsureShader(pipeline);

            // System.Numerics.Matrix4x4 is row-major; raylib's Matrix (what DrawMesh
            // uploads to matModel/mvp) is column-major. DrawMesh does NOT transpose
            // internally (DrawModelEx does), so we transpose here — otherwise every
            // vertex is sheared into stretched garbage.
            Matrix4x4 transform = Matrix4x4.Transpose(
                Matrix4x4.CreateScale(_t.scale) *
                Matrix4x4.CreateFromQuaternion(NormalizedRotation(_t.rotation)) *
                Matrix4x4.CreateTranslation(_t.position));

            Raylib.DrawMesh(_model.Meshes[0], _model.Materials[0], transform);
        }

        private static Quaternion NormalizedRotation(Quaternion q)
        {
            // Transform defaults rotation to Quaternion.Zero (all zeros), which is
            // not a valid rotation — fall back to identity so the matrix is sane.
            if (q == default || q.LengthSquared() < 1e-6f) return Quaternion.Identity;
            return Quaternion.Normalize(q);
        }

        public void Dispose()
        {
            if (_registered) { Game.Pipeline?.Unregister(this); _registered = false; }
            if (_built) { Raylib.UnloadModel(_model); _built = false; }
        }
    }
}