using Godot;

namespace ValTrainer.Game.Weapon;

/// <summary>
/// Tiny CPU mesh builder for the procedural viewmodel parts (gloved hands, sleeves, shells).
/// Primitives are appended with an arbitrary transform (normals use the inverse-transpose, so
/// non-uniform scale is fine) and baked into one ArrayMesh surface per material.
/// </summary>
public sealed class VmMesh
{
    readonly List<Vector3> v = new();
    readonly List<Vector3> n = new();
    readonly List<int> idx = new();

    public bool Empty => v.Count == 0;

    /// <summary>Appends raw arrays (vertices, normals, indices) transformed by <paramref name="t"/>.</summary>
    public void Add(Vector3[] verts, Vector3[] norms, int[] ind, Transform3D t)
    {
        int b = v.Count;
        var nb = t.Basis.Inverse().Transposed();
        for (int i = 0; i < verts.Length; i++)
        {
            v.Add(t * verts[i]);
            n.Add((nb * norms[i]).Normalized());
        }
        // Godot front faces: (b-a)x(c-a) points INTO the surface. Fix the winding per triangle so
        // mirrored (det<0) transforms and our own generators always come out right.
        for (int i = 0; i + 2 < ind.Length; i += 3)
        {
            int a = ind[i] + b, c1 = ind[i + 1] + b, c2 = ind[i + 2] + b;
            var fn = (v[c1] - v[a]).Cross(v[c2] - v[a]);
            var avg = n[a] + n[c1] + n[c2];
            if (fn.Dot(avg) > 0) (c1, c2) = (c2, c1);
            idx.Add(a); idx.Add(c1); idx.Add(c2);
        }
    }

    public void Add(Mesh prim, Transform3D t)
    {
        var arr = prim.SurfaceGetArrays(0);
        Add(arr[(int)Mesh.ArrayType.Vertex].AsVector3Array(), arr[(int)Mesh.ArrayType.Normal].AsVector3Array(),
            arr[(int)Mesh.ArrayType.Index].AsInt32Array(), t);
    }

    public void AddTo(ArrayMesh mesh, Material mat)
    {
        if (Empty) return;
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = n.ToArray();
        arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
    }

    // ---------------- primitives (cached arrays) ----------------

    static readonly Dictionary<string, (Vector3[] V, Vector3[] N, int[] I)> cache = new();

    /// <summary>Capsule along local -Z from z=0 to z=-len (caps included in len), radius r.</summary>
    public void Capsule(float r, float len, Transform3D t)
    {
        len = Mathf.Max(len, 2 * r + 0.0005f);
        string key = $"cap{r:0.0000}_{len:0.0000}";
        if (!cache.TryGetValue(key, out var a))
        {
            var cm = new CapsuleMesh { Radius = r, Height = len, RadialSegments = 12, Rings = 3 };
            var arr = cm.GetMeshArrays();
            a = (arr[(int)Mesh.ArrayType.Vertex].AsVector3Array(), arr[(int)Mesh.ArrayType.Normal].AsVector3Array(), arr[(int)Mesh.ArrayType.Index].AsInt32Array());
            cache[key] = a;
        }
        // capsule mesh is centred, along Y -> rotate to -Z and shift so it starts at the origin
        var local = new Transform3D(new Basis(Vector3.Right, -Mathf.Pi / 2), new Vector3(0, 0, -len / 2));
        Add(a.V, a.N, a.I, t * local);
    }

    /// <summary>Tapered tube along +Z from z0 to z1 (radii r0, r1), elliptical by sx/sy.</summary>
    public void Tube(float z0, float r0, float z1, float r1, float sx, float sy, Transform3D t, bool caps = true, int seg = 14)
    {
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var ind = new List<int>();
        float slope = (r0 - r1) / Mathf.Max(0.0001f, z1 - z0);
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.Tau;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            var nn = new Vector3(c / sx, s / sy, slope).Normalized();
            verts.Add(new Vector3(c * r0 * sx, s * r0 * sy, z0)); norms.Add(nn);
            verts.Add(new Vector3(c * r1 * sx, s * r1 * sy, z1)); norms.Add(nn);
        }
        for (int i = 0; i < seg; i++)
        {
            int k = i * 2;
            ind.Add(k); ind.Add(k + 1); ind.Add(k + 2);
            ind.Add(k + 1); ind.Add(k + 3); ind.Add(k + 2);
        }
        if (caps)
        {
            foreach (var (z, r, dir) in new[] { (z0, r0, -1f), (z1, r1, 1f) })
            {
                int c0 = verts.Count;
                verts.Add(new Vector3(0, 0, z)); norms.Add(new Vector3(0, 0, dir));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i / (float)seg * Mathf.Tau;
                    verts.Add(new Vector3(Mathf.Cos(a) * r * sx, Mathf.Sin(a) * r * sy, z)); norms.Add(new Vector3(0, 0, dir));
                }
                for (int i = 0; i < seg; i++) { ind.Add(c0); ind.Add(c0 + 1 + i); ind.Add(c0 + 2 + i); }
            }
        }
        Add(verts.ToArray(), norms.ToArray(), ind.ToArray(), t);
    }

    /// <summary>Superellipsoid ("rounded box") with half extents <paramref name="h"/>; e≈0.3 boxy, 1 = ellipsoid.</summary>
    public void RoundBox(Vector3 h, float e, Transform3D t, int lat = 10, int lon = 16)
    {
        string key = $"rb{e:0.000}_{lat}_{lon}";
        if (!cache.TryGetValue(key, out var a))
        {
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var ind = new List<int>();
            static float sp(float x, float p) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), p);
            for (int i = 0; i <= lat; i++)
            {
                float u = -Mathf.Pi / 2 + Mathf.Pi * i / lat;
                for (int j = 0; j <= lon; j++)
                {
                    float w = -Mathf.Pi + Mathf.Tau * j / lon;
                    var p = new Vector3(sp(Mathf.Cos(u), e) * sp(Mathf.Cos(w), e), sp(Mathf.Sin(u), e), sp(Mathf.Cos(u), e) * sp(Mathf.Sin(w), e));
                    // implicit gradient of |x|^(2/e)+|y|^(2/e)+|z|^(2/e)
                    float q = 2f / e - 1f;
                    var g = new Vector3(sp(p.X, q), sp(p.Y, q), sp(p.Z, q));
                    if (g.LengthSquared() < 1e-12f) g = p;
                    verts.Add(p); norms.Add(g.Normalized());
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int k = i * (lon + 1) + j;
                    ind.Add(k); ind.Add(k + lon + 1); ind.Add(k + 1);
                    ind.Add(k + 1); ind.Add(k + lon + 1); ind.Add(k + lon + 2);
                }
            a = (verts.ToArray(), norms.ToArray(), ind.ToArray());
            cache[key] = a;
        }
        Add(a.V, a.N, a.I, t * Transform3D.Identity.Scaled(h));
    }

    // ---------------- transform helpers ----------------

    /// <summary>Mirror conjugation across the YZ plane (x → -x). Keeps a proper rotation, so
    /// mirrored parts stay correctly lit and wound.</summary>
    public static Transform3D Mirror(Transform3D t)
    {
        var b = t.Basis;
        var m = new Basis(
            new Vector3(b.X.X, -b.X.Y, -b.X.Z),
            new Vector3(-b.Y.X, b.Y.Y, b.Y.Z),
            new Vector3(-b.Z.X, b.Z.Y, b.Z.Z));
        return new Transform3D(m, new Vector3(-t.Origin.X, t.Origin.Y, t.Origin.Z));
    }

    /// <summary>Basis whose -Z points along <paramref name="forward"/> and +Y towards <paramref name="up"/>.</summary>
    public static Basis Look(Vector3 forward, Vector3 up)
    {
        var z = -forward.Normalized();
        var x = up.Cross(z);
        if (x.LengthSquared() < 1e-8f) x = Vector3.Right;
        x = x.Normalized();
        var y = z.Cross(x);
        return new Basis(x, y, z);
    }

    public static Basis Euler(float pitchDeg, float yawDeg, float rollDeg) =>
        Basis.FromEuler(new Vector3(Mathf.DegToRad(pitchDeg), Mathf.DegToRad(yawDeg), Mathf.DegToRad(rollDeg)), EulerOrder.Yxz);
}
