using Godot;
using ValTrainer.Game;

namespace ValTrainer.World;

[Flags]
public enum Faces { None = 0, NegX = 1, PosX = 2, NegY = 4, PosY = 8, NegZ = 16, PosZ = 32, Sides = NegX | PosX | NegZ | PosZ, All = 63 }

/// <summary>
/// Merges every box of the world into one mesh per material (a handful of draw calls for the whole map).
/// UVs are world metres (planar per face, so any box size tiles correctly without triplanar cost);
/// UV2 carries the height above the box base for the shader's floor-grime gradient.
/// </summary>
public sealed class MeshBatch
{
    sealed class Part { public readonly SurfaceTool St = new(); public int Verts; }

    readonly Dictionary<(Material, bool), Part> parts = new();

    Part Get(Material m, bool shadow)
    {
        if (!parts.TryGetValue((m, shadow), out var p))
        {
            p = new Part();
            p.St.Begin(Mesh.PrimitiveType.Triangles);
            parts[(m, shadow)] = p;
        }
        return p;
    }

    public void Box(Material m, Box b, Faces faces = Faces.All, Color? color = null, bool shadow = true, bool grime = true) =>
        Box(m, b.Min, b.Max, faces, color, shadow, grime);

    public void Box(Material m, Vector3 min, Vector3 max, Faces faces = Faces.All, Color? color = null, bool shadow = true, bool grime = true)
    {
        if (max.X - min.X < 1e-4f || max.Y - min.Y < 1e-4f || max.Z - min.Z < 1e-4f) return;
        var p = Get(m, shadow);
        var c = color ?? Colors.White;
        for (int axis = 0; axis < 3; axis++)
            for (int s = -1; s <= 1; s += 2)
            {
                var f = (Faces)(1 << (axis * 2 + (s > 0 ? 1 : 0)));
                if ((faces & f) != 0) Face(p, min, max, axis, s, c, grime);
            }
    }

    /// <summary>Upward-facing quad (floor markings), y = height.</summary>
    public void FloorQuad(Material m, float x0, float z0, float x1, float z1, float y, Color? color = null) =>
        Box(m, new Vector3(x0, y - 0.001f, z0), new Vector3(x1, y, z1), Faces.PosY, color, shadow: false, grime: false);

    static readonly int[] Cw = { 0, 1, 2, 0, 2, 3 }, Ccw = { 0, 2, 1, 0, 3, 2 };

    static void Face(Part part, Vector3 min, Vector3 max, int axis, int sign, Color c, bool grime)
    {
        int b = (axis + 1) % 3, k = (axis + 2) % 3;
        var n = Vector3.Zero; n[axis] = sign;
        float plane = sign > 0 ? max[axis] : min[axis];
        Span<Vector3> v = stackalloc Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            var q = Vector3.Zero;
            q[axis] = plane;
            q[b] = i is 1 or 2 ? max[b] : min[b];
            q[k] = i >= 2 ? max[k] : min[k];
            v[i] = q;
        }
        // Godot treats clockwise (seen from the front) as the front face.
        var idx = (v[1] - v[0]).Cross(v[2] - v[0]).Dot(n) > 0 ? Ccw : Cw;
        bool vertical = axis != 1;
        var st = part.St;
        foreach (int i in idx)
        {
            var q = v[i];
            st.SetNormal(n);
            st.SetUV(axis switch
            {
                1 => new Vector2(q.X, q.Z),
                0 => new Vector2(-sign * q.Z, -q.Y),
                _ => new Vector2(sign * q.X, -q.Y),
            });
            st.SetUV2(grime && vertical ? new Vector2(q.Y - min.Y, 1f) : Vector2.Zero);
            st.SetColor(c);
            st.AddVertex(q);
        }
        part.Verts += 6;
    }

    /// <summary>Builds the merged meshes under <paramref name="parent"/>.</summary>
    public void Commit(Node3D parent, string name)
    {
        int i = 0;
        foreach (var ((mat, shadow), p) in parts)
        {
            if (p.Verts == 0) continue;
            p.St.GenerateTangents();
            var mesh = p.St.Commit();
            mesh.SurfaceSetMaterial(0, mat);
            parent.AddChild(new MeshInstance3D
            {
                Name = $"{name}_{i++}",
                Mesh = mesh,
                CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        parts.Clear();
    }
}
