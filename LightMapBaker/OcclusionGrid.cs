namespace MapLightBaker;

/// <summary>
/// Occlusion structure for shadow rays. A uniform grid over all world triangles.
/// For a map this size a grid is overkill-fast and far simpler than a BVH.
/// We only ever ask "is this segment blocked?", so we early-out on first hit.
/// </summary>
public sealed class OcclusionGrid
{
    private readonly List<Triangle> _tris;
    private readonly Vec3 _min;
    private readonly double _cell;
    private readonly int _nx, _ny, _nz;
    private readonly List<int>[] _cells;

    public OcclusionGrid(List<Triangle> tris, double cellSize)
    {
        _tris = tris;
        _cell = cellSize;

        var min = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vec3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var t in tris)
        {
            Accum(ref min, ref max, t.A);
            Accum(ref min, ref max, t.B);
            Accum(ref min, ref max, t.C);
        }
        // Pad bounds a touch.
        _min = min - new Vec3(1, 1, 1);
        var span = (max - min) + new Vec3(2, 2, 2);

        _nx = Math.Max(1, (int)Math.Ceiling(span.X / _cell));
        _ny = Math.Max(1, (int)Math.Ceiling(span.Y / _cell));
        _nz = Math.Max(1, (int)Math.Ceiling(span.Z / _cell));
        _cells = new List<int>[_nx * _ny * _nz];

        // Insert each triangle into every cell its AABB overlaps.
        for (int ti = 0; ti < tris.Count; ti++)
        {
            var t = tris[ti];
            var tmin = Min(t.A, Min(t.B, t.C));
            var tmax = Max(t.A, Max(t.B, t.C));
            var (ix0, iy0, iz0) = CellOf(tmin);
            var (ix1, iy1, iz1) = CellOf(tmax);
            for (int z = iz0; z <= iz1; z++)
            for (int y = iy0; y <= iy1; y++)
            for (int x = ix0; x <= ix1; x++)
            {
                int idx = Index(x, y, z);
                (_cells[idx] ??= new List<int>()).Add(ti);
            }
        }
    }

    /// <summary>
    /// Returns true if the open segment from a to b is blocked by any triangle.
    /// ignoreFace lets the sampled face exclude itself to avoid self-shadow acne.
    /// </summary>
    public bool Occluded(Vec3 a, Vec3 b, int ignoreFace)
    {
        Vec3 dir = b - a;
        double maxT = dir.Length;
        if (maxT < 1e-6) return false;
        Vec3 d = dir / maxT;

        // Walk the grid (3D DDA) and test triangles in visited cells.
        var (ix, iy, iz) = CellOf(a);

        int stepX = d.X > 0 ? 1 : -1;
        int stepY = d.Y > 0 ? 1 : -1;
        int stepZ = d.Z > 0 ? 1 : -1;

        double tMaxX = BoundT(a.X, d.X, ix, stepX, _min.X);
        double tMaxY = BoundT(a.Y, d.Y, iy, stepY, _min.Y);
        double tMaxZ = BoundT(a.Z, d.Z, iz, stepZ, _min.Z);

        double tDeltaX = d.X != 0 ? Math.Abs(_cell / d.X) : double.MaxValue;
        double tDeltaY = d.Y != 0 ? Math.Abs(_cell / d.Y) : double.MaxValue;
        double tDeltaZ = d.Z != 0 ? Math.Abs(_cell / d.Z) : double.MaxValue;

        double traveled = 0;
        while (true)
        {
            if (ix >= 0 && ix < _nx && iy >= 0 && iy < _ny && iz >= 0 && iz < _nz)
            {
                var cell = _cells[Index(ix, iy, iz)];
                if (cell != null)
                {
                    foreach (int ti in cell)
                    {
                        var tri = _tris[ti];
                        if (tri.FaceId == ignoreFace) continue;
                        if (RayTriangle(a, d, tri, out double hit) && hit > 1e-3 && hit < maxT - 1e-3)
                            return true;
                    }
                }
            }

            // Advance to next cell.
            if (tMaxX < tMaxY && tMaxX < tMaxZ) { ix += stepX; traveled = tMaxX; tMaxX += tDeltaX; }
            else if (tMaxY < tMaxZ)            { iy += stepY; traveled = tMaxY; tMaxY += tDeltaY; }
            else                               { iz += stepZ; traveled = tMaxZ; tMaxZ += tDeltaZ; }

            if (traveled > maxT) break;
            if (ix < 0 || ix >= _nx || iy < 0 || iy >= _ny || iz < 0 || iz >= _nz)
            {
                // Left the grid; only stop if we can't re-enter (segments are short
                // relative to the map, so leaving means done).
                if (traveled > maxT) break;
                if ((ix < 0 && stepX <= 0) || (ix >= _nx && stepX >= 0) ||
                    (iy < 0 && stepY <= 0) || (iy >= _ny && stepY >= 0) ||
                    (iz < 0 && stepZ <= 0) || (iz >= _nz && stepZ >= 0))
                    break;
            }
        }
        return false;
    }

    // Möller–Trumbore.
    private static bool RayTriangle(Vec3 o, Vec3 d, in Triangle tri, out double t)
    {
        t = 0;
        Vec3 e1 = tri.B - tri.A;
        Vec3 e2 = tri.C - tri.A;
        Vec3 p = d.Cross(e2);
        double det = e1.Dot(p);
        if (det > -1e-9 && det < 1e-9) return false;   // parallel
        double inv = 1.0 / det;
        Vec3 tv = o - tri.A;
        double u = tv.Dot(p) * inv;
        if (u < -1e-6 || u > 1 + 1e-6) return false;
        Vec3 q = tv.Cross(e1);
        double v = d.Dot(q) * inv;
        if (v < -1e-6 || u + v > 1 + 1e-6) return false;
        t = e2.Dot(q) * inv;
        return t > 0;
    }

    private double BoundT(double origin, double dir, int cellIdx, int step, double gridMin)
    {
        if (dir == 0) return double.MaxValue;
        double cellMinWorld = gridMin + (cellIdx + (step > 0 ? 1 : 0)) * _cell;
        return (cellMinWorld - origin) / dir;
    }

    private (int, int, int) CellOf(Vec3 p)
    {
        int x = (int)Math.Floor((p.X - _min.X) / _cell);
        int y = (int)Math.Floor((p.Y - _min.Y) / _cell);
        int z = (int)Math.Floor((p.Z - _min.Z) / _cell);
        return (Clamp(x, _nx), Clamp(y, _ny), Clamp(z, _nz));
    }

    private static int Clamp(int v, int n) => v < 0 ? 0 : (v >= n ? n - 1 : v);
    private int Index(int x, int y, int z) => (z * _ny + y) * _nx + x;

    private static void Accum(ref Vec3 min, ref Vec3 max, Vec3 p)
    {
        min = Min(min, p); max = Max(max, p);
    }
    private static Vec3 Min(Vec3 a, Vec3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    private static Vec3 Max(Vec3 a, Vec3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
}
