
using System.Drawing;
using System.Xml.Serialization;

namespace PhrawgEngine
{
    public class Workspace
    {
        public List<Entity> entities = [];

        public T AddEntity<T>() where T : Entity, new()
        {
            var ent = new T();
            entities.Add(ent);
            ent.LoadEntity();
            return ent;
        }

        public void LoadSceneFromFile()
        {
            UnloadWorkspace();
        }

        public void UpdateWorkspace(float dt)
        {
            foreach(Entity ent in entities) { ent.UpdateEntity(dt); }
        }

        public void Draw2DWorkspace()
        {
            foreach(Entity ent in entities) { ent.Draw2DEntity(); }
        }

        public void Draw3DWorkspace()
        {
            foreach(Entity ent in entities) { ent.Draw3DEntity(); }
        }

        public void UnloadWorkspace()
        {
            
        }

        public Entity? FindEntity(string name)
        {
            foreach (Entity ent in entities)
            {
                if (ent.name == name) { return ent; }
            }
            return null;
        }
    }
}