

using System.Numerics;
using Raylib_cs;

namespace PhrawgEngine
{
    public class SimplePlaneRenderer : Component
    {
        public Vector2 size = Vector2.One;
        public Color color = Color.White;

        public override void Draw3D()
        {
            Raylib.DrawPlane(Owner.transform.position,size,color);
        }
    }
}