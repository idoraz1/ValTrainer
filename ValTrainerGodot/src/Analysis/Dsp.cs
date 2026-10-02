namespace ValTrainer.Analysis;

/// <summary>
/// Small signal-processing toolkit for the coach: uniform resampling, zero-phase Butterworth filters
/// (cascaded biquads, bilinear transform with pre-warping) and robust statistics.
/// </summary>
static class Dsp
{
    public const double Fs = 240.0;
    public const double Dt = 1.0 / Fs;

    readonly record struct Sec(double B0, double B1, double B2, double A1, double A2);

    /// <summary>Butterworth low-pass as second-order sections (+ one first-order section for odd orders).</summary>
    static Sec[] ButterLow(int order, double fc, double fs)
    {
        double k = Math.Tan(Math.PI * Math.Min(fc, fs * 0.45) / fs), k2 = k * k;
        var s = new List<Sec>();
        for (int i = 0; i < order / 2; i++)
        {
            double q = 1.0 / (2.0 * Math.Sin(Math.PI * (2 * i + 1) / (2.0 * order)));
            double norm = 1.0 / (1.0 + k / q + k2);
            double b0 = k2 * norm;
            s.Add(new Sec(b0, 2 * b0, b0, 2 * (k2 - 1) * norm, (1 - k / q + k2) * norm));
        }
        if (order % 2 == 1)
        {
            double b = k / (1 + k);
            s.Add(new Sec(b, b, 0, (k - 1) / (k + 1), 0));
        }
        return s.ToArray();
    }

    static readonly Dictionary<(int, double), Sec[]> cache = new();

    static Sec[] Low(int order, double fc)
    {
        lock (cache)
        {
            if (!cache.TryGetValue((order, fc), out var s)) cache[(order, fc)] = s = ButterLow(order, fc, Fs);
            return s;
        }
    }

    static void Pass(double[] x, Sec[] secs)
    {
        foreach (var c in secs)
        {
            // Direct form II transposed, initialised to the steady state of the first sample (DC gain 1).
            double u = x[0], z1 = u - c.B0 * u, z2 = c.B2 * u - c.A2 * u;
            for (int i = 0; i < x.Length; i++)
            {
                double xi = x[i], y = c.B0 * xi + z1;
                z1 = c.B1 * xi - c.A1 * y + z2;
                z2 = c.B2 * xi - c.A2 * y;
                x[i] = y;
            }
        }
    }

    /// <summary>Zero-phase low-pass (forward + backward), odd-reflection padded at both ends.</summary>
    public static float[] LowPass(float[] x, double fc, int order = 5)
    {
        int n = x.Length;
        if (n < 4) return (float[])x.Clone();
        var secs = Low(order, fc);
        int pad = Math.Min(n - 1, (int)(3 * Fs / fc));
        var w = new double[n + 2 * pad];
        for (int i = 0; i < pad; i++) w[i] = 2 * x[0] - x[pad - i];
        for (int i = 0; i < n; i++) w[pad + i] = x[i];
        for (int i = 0; i < pad; i++) w[pad + n + i] = 2 * x[n - 1] - x[n - 2 - i];
        Pass(w, secs);
        Array.Reverse(w);
        Pass(w, secs);
        Array.Reverse(w);
        var r = new float[n];
        for (int i = 0; i < n; i++) r[i] = (float)w[pad + i];
        return r;
    }

    /// <summary>Central-difference derivative of a uniformly sampled signal (units per second).</summary>
    public static float[] Deriv(float[] x)
    {
        int n = x.Length;
        var d = new float[n];
        if (n < 2) return d;
        for (int i = 1; i < n - 1; i++) d[i] = (float)((x[i + 1] - x[i - 1]) * Fs / 2);
        d[0] = (float)((x[1] - x[0]) * Fs);
        d[n - 1] = (float)((x[n - 1] - x[n - 2]) * Fs);
        return d;
    }

    /// <summary>Linear resample of (t, x) (t ascending) onto t0 + i·Dt, i &lt; n.</summary>
    public static float[] Resample(float[] t, float[] x, double t0, int n)
    {
        var r = new float[n];
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            double tg = t0 + i * Dt;
            while (j < t.Length - 2 && t[j + 1] < tg) j++;
            if (t.Length == 1) { r[i] = x[0]; continue; }
            double ta = t[j], tb = t[j + 1];
            double f = tb > ta ? (tg - ta) / (tb - ta) : 0;
            f = Math.Clamp(f, 0, 1);
            r[i] = (float)(x[j] + (x[j + 1] - x[j]) * f);
        }
        return r;
    }

    // ---------------- statistics ----------------

    public static float Median(IEnumerable<float> v)
    {
        var a = v.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToArray();
        if (a.Length == 0) return float.NaN;
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2f;
    }

    public static float Mean(IEnumerable<float> v)
    {
        double s = 0; int n = 0;
        foreach (var x in v) if (!float.IsNaN(x)) { s += x; n++; }
        return n == 0 ? float.NaN : (float)(s / n);
    }

    public static float Sd(IEnumerable<float> v)
    {
        var a = v.Where(x => !float.IsNaN(x)).ToArray();
        if (a.Length < 2) return float.NaN;
        double m = a.Average();
        return (float)Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / (a.Length - 1));
    }

    /// <summary>Least-squares line y = a + b·x. Returns false when x has (almost) no spread.</summary>
    public static bool Fit(IReadOnlyList<float> x, IReadOnlyList<float> y, out float a, out float b)
    {
        a = b = 0;
        int n = x.Count;
        if (n < 5) return false;
        double mx = x.Average(), my = y.Average(), sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++) { sxx += (x[i] - mx) * (x[i] - mx); sxy += (x[i] - mx) * (y[i] - my); }
        if (sxx / n < 0.05) return false;
        b = (float)(sxy / sxx);
        a = (float)(my - b * mx);
        return true;
    }

    /// <summary>Standard normal CDF (Abramowitz–Stegun 7.1.26).</summary>
    public static double Phi(double z)
    {
        double t = 1 / (1 + 0.3275911 * Math.Abs(z) / Math.Sqrt(2));
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z / 2);
        return z >= 0 ? 0.5 * (1 + y) : 0.5 * (1 - y);
    }
}
