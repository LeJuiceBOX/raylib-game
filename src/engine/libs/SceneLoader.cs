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

            // Add the entity WITHOUT loading yet, so we can fully construct it
            // (name, properties, components, component properties) before a single
            // LoadEntity() pass runs Load() in the correct order.
            MethodInfo addEntity = typeof(Workspace)
                .GetMethods()
                .First(m => m.Name == nameof(Workspace.AddEntity)
                            && m.IsGenericMethodDefinition
                            && m.GetParameters().Length == 1)
                .MakeGenericMethod(entType);
            Entity ent = (Entity)addEntity.Invoke(workspace, new object[] { false })!;

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

            // Everything is configured — now run Load() exactly once, in order:
            // all components first, then the entity (matches LoadEntity's contract).
            ent.LoadEntity();

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
                // Select the AddComponent<T>(bool load) overload and defer Load()
                // until properties are applied (done by the entity's LoadEntity pass).
                MethodInfo addComp = typeof(Entity)
                    .GetMethods()
                    .First(m => m.Name == nameof(Entity.AddComponent)
                                && m.IsGenericMethodDefinition
                                && m.GetParameters().Length == 1)
                    .MakeGenericMethod(compType);
                component = addComp.Invoke(ent, new object[] { false })!;
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
            if (targetType == typeof(Vector2))
            {
                if (el.ValueKind != JsonValueKind.Array) return false;
                float[] a = el.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                if (a.Length < 2) return false;
                value = new Vector2(a[0], a[1]);
                return true;
            }
            if (targetType == typeof(Raylib_cs.Color))
            {
                if (el.ValueKind != JsonValueKind.Array) return false;
                int[] a = el.EnumerateArray().Select(x => x.GetInt32()).ToArray();
                if (a.Length < 3) return false;
                byte alpha = a.Length >= 4 ? (byte)a[3] : (byte)255;
                value = new Raylib_cs.Color((byte)a[0], (byte)a[1], (byte)a[2], alpha);
                return true;
            }
            if (targetType.IsEnum)
            {
                if (el.ValueKind == JsonValueKind.String
                    && Enum.TryParse(targetType, el.GetString(), ignoreCase: true, out object? ev))
                { value = ev; return true; }
                if (el.ValueKind == JsonValueKind.Number)
                { value = Enum.ToObject(targetType, el.GetInt32()); return true; }
                return false;
            }
            if (targetType == typeof(float)) { value = el.GetSingle(); return true; }
            if (targetType == typeof(int))   { value = el.GetInt32();  return true; }
            if (targetType == typeof(string)){ value = el.GetString(); return true; }
            if (targetType == typeof(bool))  { value = el.GetBoolean();return true; }

            return false;
        }
    }
}