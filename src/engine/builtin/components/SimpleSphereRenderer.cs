

using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public class SimpleSphereRenderer : Component
    {
        public float radius = 1f;
        public Color color = Color.White;

        private Transform _ownerTransform = null!;

        public override void Load()
        {
            _ownerTransform = Owner.GetComponent<Transform>();
        }

        public override void Draw3D()
        {
            Raylib.DrawSphere(_ownerTransform.position,radius,color);
        }
    }
}