using System.Reflection;
using System.Text.Json;
using System.Numerics;

namespace PhrawgEngine
{
    public static class SceneLoader
    {
        public static void LoadSceneFromFile(string path)
        {
            string json = File.ReadAllText(path);
            using JsonDocument doc = JsonDocument.Parse(json);

            foreach (JsonElement entJson in doc.RootElement.EnumerateArray())
            {
                Entity ent = SpawnEntity(entJson, Game.Workspace);
            }
        }

        private static Entity SpawnEntity(JsonElement entJson, Workspace workspace)
        {
            // Resolve the entity type by full name; default to Entity.
            Type entType = ResolveType(entJson, typeof(Entity));

            // Workspace.AddEntity<T>() is generic + new(), so invoke via reflection.
            MethodInfo addEntity = typeof(Workspace)
                .GetMethod(nameof(Workspace.AddEntity))!
                .MakeGenericMethod(entType);
            Entity ent = (Entity)addEntity.Invoke(workspace, null)!;

            if (entJson.TryGetProperty("Name", out JsonElement nameEl)
                && nameEl.ValueKind == JsonValueKind.String)
            {
                ent.name = nameEl.GetString()!;
            }

            ApplyProperties(ent, entJson);

            if (entJson.TryGetProperty("Components", out JsonElement comps))
            {
                foreach (JsonElement compJson in comps.EnumerateArray())
                {
                    AddOrConfigureComponent(ent, compJson);
                }
            }

            return ent;
        }

        private static void AddOrConfigureComponent(Entity ent, JsonElement compJson)
        {
            Type compType = ResolveType(compJson, null)
                ?? throw new InvalidOperationException("Component missing valid 'Type'.");

            object component;

            // Entity ctor already adds a Transform — reuse it instead of duplicating.
            if (compType == typeof(Transform))
            {
                component = ent.transform;
            }
            else
            {
                MethodInfo addComp = typeof(Entity)
                    .GetMethod(nameof(Entity.AddComponent))!
                    .MakeGenericMethod(compType);
                component = addComp.Invoke(ent, null)!;
            }

            ApplyProperties(component, compJson);
        }

        // Resolve "Type" string (e.g. "PhrawgEngine.SimpleSphereRenderer") to a Type.
        private static Type? ResolveType(JsonElement json, Type? fallback)
        {
            if (json.TryGetProperty("Type", out JsonElement typeEl)
                && typeEl.ValueKind == JsonValueKind.String)
            {
                string typeName = typeEl.GetString()!;
                Type? t = Type.GetType(typeName)
                    ?? typeof(Entity).Assembly.GetType(typeName);
                if (t != null) return t;
            }
            return fallback;
        }

        // Bind "Properties" onto fields/properties (case-insensitive).
        private static void ApplyProperties(object target, JsonElement json)
        {
            if (!json.TryGetProperty("Properties", out JsonElement props)
                || props.ValueKind != JsonValueKind.Object)
                return;

            Type t = target.GetType();
            foreach (JsonProperty p in props.EnumerateObject())
            {
                MemberInfo? member =
                    (MemberInfo?)t.GetField(p.Name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? t.GetProperty(p.Name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (member == null) continue;

                Type memberType = member is FieldInfo f ? f.FieldType
                                                        : ((PropertyInfo)member).PropertyType;

                if (!TryConvert(p.Value, memberType, out object? value)) continue;

                if (member is FieldInfo fi) fi.SetValue(target, value);
                else ((PropertyInfo)member).SetValue(target, value);
            }
        }

        private static bool TryConvert(JsonElement el, Type targetType, out object? value)
        {
            value = null;

            if (targetType == typeof(Vector3))
            {
                if (el.ValueKind != JsonValueKind.Array) return false;
                float[] a = el.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                if (a.Length < 3) return false;
                value = new Vector3(a[0], a[1], a[2]);
                return true;
            }
            if (targetType == typeof(float)) { value = el.GetSingle(); return true; }
            if (targetType == typeof(int))   { value = el.GetInt32();  return true; }
            if (targetType == typeof(string)){ value = el.GetString(); return true; }
            if (targetType == typeof(bool))  { value = el.GetBoolean();return true; }

            return false;
        }
    }
}