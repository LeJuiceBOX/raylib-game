using System.Globalization;

namespace MapLightBaker;

/// <summary>
/// Minimal double-precision 3D vector. Doubles matter here: brush-plane
/// intersection accumulates error fast in float, and a few ULPs can drop
/// or duplicate a vertex.
/// </summary>
public readonly struct Vec3
{
    public readonly double X, Y, Z;

    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);

    public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;

    public Vec3 Cross(Vec3 b) => new(
        Y * b.Z - Z * b.Y,
        Z * b.X - X * b.Z,
        X * b.Y - Y * b.X);

    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double LengthSq => X * X + Y * Y + Z * Z;

    public Vec3 Normalized
    {
        get
        {
            double l = Length;
            return l > 1e-12 ? this / l : new Vec3(0, 0, 0);
        }
    }

    public override string ToString() =>
        string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
}

/// <summary>
/// Plane stored as outward normal N and distance D such that N·p = D for points
/// on the plane. In the Valve .map format each face line gives three points in
/// CLOCKWISE order when viewed from the front, so the normal is (p1-p0)x(p2-p0).
/// </summary>
public readonly struct Plane
{
    public readonly Vec3 N;
    public readonly double D;

    public Plane(Vec3 n, double d) { N = n; D = d; }

    public static Plane FromPoints(Vec3 p0, Vec3 p1, Vec3 p2)
    {
        // Valve/Quake winding: normal points toward the side the three points
        // were authored from. Cross product order chosen to match qbsp convention.
        Vec3 n = (p2 - p0).Cross(p1 - p0).Normalized;
        double d = n.Dot(p0);
        return new Plane(n, d);
    }

    /// <summary>Signed distance from a point to the plane (positive = front side).</summary>
    public double Distance(Vec3 p) => N.Dot(p) - D;
}
