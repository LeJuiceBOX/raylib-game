namespace MapLightBaker;

/// <summary>
/// One face of a brush as authored in the .map: a plane plus its texture name.
/// The actual polygon (winding) is computed later by intersecting brush planes.
/// </summary>
public sealed class BrushFace
{
    public required Vec3 P0, P1, P2;     // the three defining points
    public required Plane Plane;
    public required string Texture;

    // Filled in once the winding is computed.
    public List<Vec3> Winding = new();
}

public sealed class Brush
{
    public List<BrushFace> Faces = new();
}

/// <summary>
/// A point/omni light parsed from a light_omni entity.
/// light_radius / light_energy come straight from the map; we translate them
/// into a world-space range and intensity at bake time using user-set scales,
/// because the raw Godot values are tiny relative to Quake-unit geometry.
/// </summary>
public sealed class OmniLight
{
    public required Vec3 Position;
    public double RawRadius;      // light_radius from the map
    public double RawEnergy;      // light_energy from the map
    public Vec3 Color = new(1, 1, 1);
}

/// <summary>A fully resolved triangle ready for rasterization / ray tests.</summary>
public struct Triangle
{
    public Vec3 A, B, C;
    public Vec3 Normal;
    public int FaceId;           // index into the flat face list (for UV lookup)
}
