namespace PhrawgEngine
{
    public class Entity
    {
        static private int s_objectCount = 0;

        public int id = s_objectCount;
        public string name = "Entity_" + s_objectCount.ToString();

        private List<Component> components = [];

        public Transform transform;
        
        public Entity()
        {
            s_objectCount++;
            transform = AddComponent<Transform>();
        }


        public T AddComponent<T>() where T : Component, new()
        {
            return AddComponent<T>(load: true);
        }

        /// <summary>
        /// Add a component, optionally deferring Load(). The scene loader adds
        /// components with load:false so it can apply JSON properties first, then
        /// triggers a single Load() pass via LoadEntity(). Runtime callers use the
        /// default overload and get immediate Load().
        /// </summary>
        public T AddComponent<T>(bool load) where T : Component, new()
        {
            var c = new T { Owner = this };
            components.Add(c);
            if (load) c.Load();
            return c;
        }
    // Component Handling
        public void RemoveComponent<T>() where T : Component
        {
            for (int i = components.Count - 1; i >= 0; i--)
            {
                if (components[i] is T)
                {
                    components.RemoveAt(i);
                }
            }
        }

        public T GetComponent<T>() where T : Component
        {
            foreach (Component c in components)
            {
                if (c is T match) { return match; }
            }
            throw new InvalidOperationException($"Component of type {typeof(T).Name} not found on entity '{name}'.");
        }

        public bool HasComponent<T>() where T : Component
        {
            foreach (Component c in components)
            {
                if (c is T) { return true; }
            }
            return false;
        }

    // PhrawgEngine Gameloop
        public void LoadEntity()
        {
            foreach (Component c in components) { c.Load(); }
            Load();
        }

        public void UpdateEntity(float dt) // keep in mind we are updating components before the entity itself.
        {
            foreach (Component c in components) { c.Update(dt); }
            Update(dt);
        }

        public void Draw2DEntity()
        {
            foreach (Component c in components) { c.Draw2D(); }
            Draw2D();
        }

        public void Draw3DEntity()
        {
            foreach (Component c in components) { c.Draw3D(); }
            Draw3D();
        }
    // These methods are for overriding only.
        public virtual void Load() {}
        public virtual void Update(float dt) {}
        public virtual void Draw2D() {}
        public virtual void Draw3D() {}

    }
}