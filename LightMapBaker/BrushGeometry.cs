namespace MapLightBaker;

/// <summary>
/// Converts a brush (a set of half-space planes) into explicit face polygons.
///
/// Algorithm (the standard Quake/qbsp approach):
///   For each face plane, start with a huge quad lying on that plane, then clip
///   it against every other face plane of the brush. What survives is the convex
///   polygon for that face. Because brushes are convex by construction, this is
///   exact and robust.
/// </summary>
public static class BrushGeometry
{
    private const double Huge = 1_000_000.0;   // map is ~1000 units; this is safely large
    private const double Eps = 1e-4;

    public static void BuildWindings(Brush brush)
    {
        foreach (var face in brush.Faces)
        {
            var poly = BasePolygon(face.Plane);
            foreach (var other in brush.Faces)
            {
                if (ReferenceEquals(other, face)) continue;
                // Clip away everything in front of the *other* face's plane,
                // keeping the back side (the brush interior side).
                poly = ClipToBack(poly, other.Plane);
                if (poly.Count == 0) break;
            }
            face.Winding = poly;
        }
    }

    /// <summary>A large quad centered on the plane, spanning the world.</summary>
    private static List<Vec3> BasePolygon(Plane plane)
    {
        Vec3 n = plane.N;

        // Pick the world axis least aligned with the normal to build a stable basis.
        Vec3 up = Math.Abs(n.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
        Vec3 u = up.Cross(n).Normalized;
        Vec3 v = n.Cross(u).Normalized;

        Vec3 origin = n * plane.D;   // a point on the plane closest to world origin

        return new List<Vec3>
        {
            origin - u * Huge - v * Huge,
            origin - u * Huge + v * Huge,
            origin + u * Huge + v * Huge,
            origin + u * Huge - v * Huge,
        };
    }

    /// <summary>
    /// Sutherland–Hodgman clip: keep the part of the polygon on the BACK side of
    /// the plane (distance <= 0), which is the brush-interior side.
    /// </summary>
    private static List<Vec3> ClipToBack(List<Vec3> poly, Plane plane)
    {
        var result = new List<Vec3>(poly.Count + 4);
        int count = poly.Count;
        if (count == 0) return result;

        for (int i = 0; i < count; i++)
        {
            Vec3 cur = poly[i];
            Vec3 nxt = poly[(i + 1) % count];
            double dc = plane.Distance(cur);
            double dn = plane.Distance(nxt);

            bool curIn = dc <= Eps;
            bool nxtIn = dn <= Eps;

            if (curIn) result.Add(cur);

            // Edge crosses the plane -> add intersection point.
            if (curIn != nxtIn)
            {
                double t = dc / (dc - dn);
                result.Add(cur + (nxt - cur) * t);
            }
        }
        return result;
    }
}
