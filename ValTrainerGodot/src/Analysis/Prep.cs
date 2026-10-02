using ValTrainer.Core;
using ValTrainer.Game;

namespace ValTrainer.Analysis;

/// <summary>
/// Pre-processed run: per-frame unwrapped crosshair angles and cumulative hand rotation (from raw mouse counts, so
/// recoil and camera kick never contaminate it), plus the 240 Hz resampled hand path with a 7 Hz zero-phase
/// low-pass used for submovement segmentation (spec conventions).
/// </summary>
sealed class Prep
{
    public readonly RunTelemetry T;
    public readonly int N;
    public readonly float[] Ft, Yaw, Pitch;   // frame time, crosshair yaw (unwrapped), pitch
    public readonly float[] Hx, Hy;           // cumulative hand rotation (deg; +x right, +y up)
    public readonly float[] Kd;               // deg of view rotation per count, per frame
    public readonly float Sens;
    public readonly int Dpi;

    // 240 Hz grid
    public readonly double G0;
    public readonly int Gn;
    public readonly float[] Gx, Gy;           // raw resampled hand path
    public readonly float[] Vx, Vy, Sp;       // 7 Hz smoothed hand velocity (deg/s) and speed

    public Prep(RunTelemetry t)
    {
        T = t;
        var fr = t.Frames;
        N = fr.Count;
        Sens = t.Sens > 0 ? t.Sens : 0.4f;
        Dpi = t.Dpi > 0 ? t.Dpi : 800;
        float zoom = Enum.TryParse<WeaponKind>(t.Weapon, out var wk) ? Weapons.Get(wk)?.Zoom ?? 1f : 1f;
        float zoomMult = float.IsFinite(t.ZoomSensMult) && t.ZoomSensMult > 0 ? t.ZoomSensMult : 1f;
        Ft = new float[N]; Yaw = new float[N]; Pitch = new float[N]; Hx = new float[N]; Hy = new float[N]; Kd = new float[N];
        float prevRaw = 0;
        for (int i = 0; i < N; i++)
        {
            var f = fr[i];
            Ft[i] = f.T;
            float k = PlayerView.DegPerCount * Sens * (f.Has(FrameFlags.Ads) && zoom > 1f ? zoomMult / zoom : 1f);
            Kd[i] = k;
            // Angles and hand path are cumulative: one non-finite sample (corrupt file) must not poison the rest of the run.
            float yaw = float.IsFinite(f.Yaw) ? f.Yaw : i > 0 ? prevRaw : 0f;
            if (i == 0)
            {
                // The first frame also carries any motion made during the countdown: never count it as a movement.
                Yaw[0] = yaw; prevRaw = yaw;
            }
            else
            {
                // Wrap to [−180, 180] without a loop: a corrupt yaw like 1e30 made "while (d > 180) d -= 360" spin forever.
                float d = yaw - prevRaw;
                d = float.IsFinite(d) ? d - 360f * MathF.Round(d / 360f) : 0f;
                Yaw[i] = Yaw[i - 1] + d;
                prevRaw = yaw;
                Hx[i] = Hx[i - 1] + (float.IsFinite(f.Dx) ? f.Dx * k : 0f);
                Hy[i] = Hy[i - 1] - (float.IsFinite(f.Dy) ? f.Dy * k : 0f);
            }
            Pitch[i] = float.IsFinite(f.Pitch) ? f.Pitch : i > 0 ? Pitch[i - 1] : 0f;
        }
        if (N < 2) { Gx = Gy = Vx = Vy = Sp = Array.Empty<float>(); return; }
        G0 = Ft[0];
        Gn = Math.Max(2, (int)((Ft[N - 1] - Ft[0]) * Dsp.Fs) + 1);
        Gx = Dsp.Resample(Ft, Hx, G0, Gn);
        Gy = Dsp.Resample(Ft, Hy, G0, Gn);
        Vx = Dsp.Deriv(Dsp.LowPass(Gx, 7));
        Vy = Dsp.Deriv(Dsp.LowPass(Gy, 7));
        Sp = new float[Gn];
        for (int i = 0; i < Gn; i++) Sp[i] = MathF.Sqrt(Vx[i] * Vx[i] + Vy[i] * Vy[i]);
    }

    public int Gi(double t) => Math.Clamp((int)Math.Round((t - G0) * Dsp.Fs), 0, Math.Max(0, Gn - 1));
    public double Gt(int i) => G0 + i * Dsp.Dt;

    /// <summary>Index of the last frame with time ≤ t (0 if none).</summary>
    public int FrameAt(double t)
    {
        int lo = 0, hi = N - 1;
        if (N == 0) return 0;
        if (t < Ft[0]) return 0;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Ft[mid] <= t + 1e-6) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Raw hand speed (deg/s): mean over the last <paramref name="nf"/> frames ending at frame fi.</summary>
    public float RawSpeed(int fi, int nf = 3)
    {
        if (fi <= 0) return 0;
        int a = Math.Max(1, fi - nf + 1);
        double dist = 0;
        for (int i = a; i <= fi; i++) dist += Math.Sqrt(Sq(Hx[i] - Hx[i - 1]) + Sq(Hy[i] - Hy[i - 1]));
        double dt = Ft[fi] - Ft[a - 1];
        return dt > 1e-5 ? (float)(dist / dt) : 0;
    }

    /// <summary>Crosshair (unwrapped yaw, pitch) at time t, interpolated between frames.</summary>
    public (float Yaw, float Pitch) CrossAt(double t)
    {
        int i = FrameAt(t);
        if (i >= N - 1 || t <= Ft[i]) return (Yaw[i], Pitch[i]);
        float f = (float)((t - Ft[i]) / Math.Max(1e-6, Ft[i + 1] - Ft[i]));
        return (Yaw[i] + (Yaw[i + 1] - Yaw[i]) * f, Pitch[i] + (Pitch[i + 1] - Pitch[i]) * f);
    }

    /// <summary>Cumulative hand path length (deg) between two times (frame resolution).</summary>
    public float PathLength(double from, double to)
    {
        int a = FrameAt(from), b = FrameAt(to);
        double s = 0;
        for (int i = a + 1; i <= b; i++) s += Math.Sqrt(Sq(Hx[i] - Hx[i - 1]) + Sq(Hy[i] - Hy[i - 1]));
        return (float)s;
    }

    /// <summary>Hand speed (deg/s) → hand speed on the mouse pad (cm/s).</summary>
    public float DegToCm(float degPerSec) => degPerSec / (PlayerView.DegPerCount * Sens) / Dpi * 2.54f;

    static double Sq(double x) => x * x;
}
