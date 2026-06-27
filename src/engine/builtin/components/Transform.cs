
using System.Buffers;
using System.Numerics;

namespace PhrawgEngine
{
    public class Transform : Component
    {

        public Vector3 position = Vector3.Zero;
        public Vector3 localPosition = Vector3.Zero;
        public Vector3 scale = Vector3.One;
        public Quaternion rotation = Quaternion.Zero;
    }
}