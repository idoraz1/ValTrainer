using Godot;

namespace ValTrainer.Game.Weapon;

/// <summary>Curl of one finger: spread (deg, + = toward the thumb) and the three joint flexions (deg).</summary>
public sealed record Finger(float Spread, float C0, float C1, float C2);

/// <summary>
/// Pose of a procedural gloved hand. Hand space (right hand): origin = wrist, -Z = fingers,
/// +Y = back of the hand, -X = thumb side. Thumb: direction + nail-side vector + three flexions.
/// </summary>
public sealed record HandShape(
    Finger Index, Finger Middle, Finger Ring, Finger Little,
    Vector3 ThumbDir, Vector3 ThumbUp, Vector3 ThumbCurl, float ForearmLen = 0.5f);

/// <summary>
/// Where a hand sits on the gun (gun space: grip origin, barrel -Z, +Y up, right-handed layout):
/// wrist position, finger direction, back-of-hand direction and the forearm direction (toward the elbow).
/// </summary>
public sealed record HandPlacement(Vector3 Wrist, Vector3 Fingers, Vector3 Back, Vector3 Elbow, HandShape Shape)
{
    public Transform3D Xform => new(VmMesh.Look(Fingers, Back), Wrist);
    /// <summary>Forearm direction in hand space.</summary>
    public Vector3 ElbowLocal => (Xform.Basis.Inverse() * Elbow).Normalized();
}

/// <summary>
/// Builds a low-poly tactical glove + sleeve as one baked ArrayMesh (4 surfaces: glove, glove pads,
/// sleeve, sleeve accent). <c>mirror</c> builds a left hand.
/// </summary>
public static class VmHands
{
    static Transform3D T(Vector3 p) => new(Basis.Identity, p);
    static Transform3D T(float x, float y, float z) => new(Basis.Identity, new Vector3(x, y, z));
    static Transform3D RX(float deg) => new(new Basis(Vector3.Right, Mathf.DegToRad(deg)), Vector3.Zero);
    static Transform3D RY(float deg) => new(new Basis(Vector3.Up, Mathf.DegToRad(deg)), Vector3.Zero);

    /// <param name="elbow">forearm direction in (un-mirrored) hand space.</param>
    public static ArrayMesh Build(HandShape s, bool mirror, Vector3 elbow, Material glove, Material pad, Material sleeve, Material accent)
    {
        var mg = new VmMesh(); var mp = new VmMesh(); var ms = new VmMesh(); var ma = new VmMesh();
        Transform3D X(Transform3D t) => mirror ? VmMesh.Mirror(t) : t;

        // Palm block + thenar (thumb muscle) + back-of-hand armour pad + knuckle guard.
        mg.RoundBox(new Vector3(0.041f, 0.0145f, 0.047f), 0.55f, X(T(0.001f, 0f, -0.05f)));
        mg.RoundBox(new Vector3(0.017f, 0.0125f, 0.026f), 0.85f, X(T(-0.025f, -0.006f, -0.032f) * RY(12)));
        mp.RoundBox(new Vector3(0.031f, 0.0055f, 0.026f), 0.35f, X(T(0.003f, 0.0128f, -0.058f)));
        mp.RoundBox(new Vector3(0.04f, 0.0075f, 0.0095f), 0.45f, X(T(0.0f, 0.0085f, -0.09f)));

        // Fingers: base (knuckle) positions, segment lengths and radii.
        (Finger f, Vector3 b, float[] len, float[] rad)[] fingers =
        {
            (s.Index,  new(-0.0285f, 0.0f,   -0.092f), new[] { 0.040f, 0.025f, 0.021f }, new[] { 0.0098f, 0.0093f, 0.0088f }),
            (s.Middle, new(-0.0095f, 0.001f, -0.095f), new[] { 0.044f, 0.028f, 0.022f }, new[] { 0.0102f, 0.0096f, 0.009f }),
            (s.Ring,   new(0.0095f,  0.0f,   -0.092f), new[] { 0.041f, 0.026f, 0.021f }, new[] { 0.0097f, 0.0092f, 0.0087f }),
            (s.Little, new(0.0275f, -0.001f, -0.085f), new[] { 0.032f, 0.020f, 0.018f }, new[] { 0.0087f, 0.0082f, 0.0078f }),
        };
        foreach (var (f, b, len, rad) in fingers)
        {
            var t = T(b) * RY(f.Spread) * RX(-f.C0);
            float[] curls = { f.C1, f.C2, 0f };
            for (int i = 0; i < 3; i++)
            {
                mg.Capsule(rad[i], len[i] + 2 * rad[i], X(t * T(0, 0, rad[i])));
                t = t * T(0, 0, -len[i]) * RX(-curls[i]);
            }
        }

        // Thumb (metacarpal + 2 phalanges).
        {
            var t = T(-0.026f, -0.008f, -0.022f) * new Transform3D(VmMesh.Look(s.ThumbDir, s.ThumbUp), Vector3.Zero);
            float[] len = { 0.036f, 0.027f, 0.023f };
            float[] rad = { 0.0128f, 0.0116f, 0.0106f };
            float[] curl = { s.ThumbCurl.X, s.ThumbCurl.Y, s.ThumbCurl.Z };
            t = t * RX(-curl[0]);
            for (int i = 0; i < 3; i++)
            {
                mg.Capsule(rad[i], len[i] + 2 * rad[i], X(t * T(0, 0, rad[i])));
                if (i < 2) t = t * T(0, 0, -len[i]) * RX(-curl[i + 1]);
            }
        }

        // Wrist joint (bridges the palm and the bent forearm), then glove cuff + strap and the sleeve,
        // all following the forearm direction so no cap faces the camera when the wrist is bent.
        mg.RoundBox(new Vector3(0.033f, 0.023f, 0.03f), 0.8f, X(T(0, -0.001f, -0.004f)));
        var fa = new Transform3D(VmMesh.Look(-elbow, Mathf.Abs(elbow.Y) > 0.95f ? Vector3.Forward : Vector3.Up), Vector3.Zero);
        mg.Tube(0.0f, 0.0285f, 0.068f, 0.031f, 1.18f, 0.86f, X(fa), caps: false, seg: 16);
        mp.Tube(0.026f, 0.0315f, 0.048f, 0.032f, 1.18f, 0.87f, X(fa), caps: false, seg: 16);
        ms.Tube(0.062f, 0.034f, 0.062f + s.ForearmLen, 0.054f, 1.1f, 0.94f, X(fa), caps: false, seg: 16);
        ma.Tube(0.058f, 0.0365f, 0.084f, 0.0375f, 1.1f, 0.94f, X(fa), caps: true, seg: 16);

        var mesh = new ArrayMesh();
        mg.AddTo(mesh, glove);
        mp.AddTo(mesh, pad);
        ms.AddTo(mesh, sleeve);
        ma.AddTo(mesh, accent);
        return mesh;
    }

    /// <summary>Simple spare magazine (for the reload) — straight or curved box mag along -Y.</summary>
    public static ArrayMesh Magazine(Vector3 half, float curveDeg, Material body, Material plate)
    {
        var mb = new VmMesh(); var mp = new VmMesh();
        int n = curveDeg > 0.1f ? 3 : 1;
        var t = Transform3D.Identity;
        float seg = half.Y * 2 / n;
        for (int i = 0; i < n; i++)
        {
            mb.RoundBox(new Vector3(half.X, seg / 2 + 0.002f, half.Z), 0.3f, t * T(0, -seg / 2, 0));
            t = t * T(0, -seg, 0) * RX(curveDeg / n);
        }
        mp.RoundBox(new Vector3(half.X * 1.15f, 0.004f, half.Z * 1.08f), 0.3f, t * T(0, -0.002f, 0));
        var mesh = new ArrayMesh();
        mb.AddTo(mesh, body);
        mp.AddTo(mesh, plate);
        return mesh;
    }

    /// <summary>Brass shell casing along -Z (length len, radius r).</summary>
    public static ArrayMesh Shell(float r, float len, Material brass, Material primer)
    {
        var mb = new VmMesh(); var mp = new VmMesh();
        mb.Tube(0f, r, -len * 0.78f, r * 0.97f, 1, 1, Transform3D.Identity, caps: false, seg: 10);
        mb.Tube(-len * 0.78f, r * 0.97f, -len, r * 0.62f, 1, 1, Transform3D.Identity, caps: true, seg: 10);
        mp.Tube(0.0015f, r * 1.04f, 0f, r * 1.04f, 1, 1, Transform3D.Identity, caps: true, seg: 10);
        var mesh = new ArrayMesh();
        mb.AddTo(mesh, brass);
        mp.AddTo(mesh, primer);
        return mesh;
    }
}
