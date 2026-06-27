

namespace PhrawgEngine
{
    public abstract class Component()
    {

        public Entity Owner = null!;

        public virtual void Load() {}
        public virtual void Update(float dt) {}
        public virtual void Draw2D() {}
        public virtual void Draw3D() {}

    }
}