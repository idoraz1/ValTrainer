using Godot;
using ValTrainer.Core;
using ValTrainer.Game;
using ValTrainer.UI;

namespace ValTrainer.Modes;

/// <summary>
/// "Compare 2 sens" (suggested on Discord): the player enters two VALORANT sens they like (shoot a value to type it, or
/// shoot − / +), then plays the Sens Finder's blind rounds between just those two: 8 rounds ABBA BAAB (or BAAB ABBA),
/// the labels A / B drawn at random, the same flick, micro-adjustment and strafe sets for both. Scored exactly like a
/// search step (<see cref="SfStats.Compare"/>); the verdict is <see cref="SfStats.CompareVerdict"/>: |t| ≥ 2 a clear
/// win, 1–2 a slight edge, below 1 no clear difference. The result shows the winner, the margin and confidence, the
/// per-task split (flicks, micro-adjustments, tracking), eDPI and cm/360 of both, and the midpoint (geometric mean).
/// The two values are remembered (AppSettings.SensCompare1 / SensCompare2). The normal Sens Finder flow is unchanged.
/// </summary>
public sealed partial class SensFinderMode
{
    bool compareMode, cmpDev;
    readonly float[] cmpSens = new float[2];
    int cmpEdit = -1;          // the value being typed (-1 = none)
    string cmpBuf = "";
    readonly HashSet<Godot.Key> cmpHeld = new();
    // result
    SfStage? cmpStage;
    int cmpWin = -1, cmpStrength;
    double cmpT;
    float cmpMid;

    /// <summary>A sens far outside this (at the player's DPI) is almost certainly a typo or a wrong DPI.</summary>
    const float CmpMinCm = 8f, CmpMaxCm = 200f;
    /// <summary>Under 5% apart: usually too close to tell; 75% or more apart: the best sens may well be in between.</summary>
    const float CmpCloseRatio = 1.05f, CmpFarRatio = 1.75f;

    const int KStep0 = 2, KBack = 10, KGo = 11, KReset = 12, KCount = 13;
    static readonly float[] StepVals = { -0.05f, -0.01f, 0.01f, 0.05f };
    static readonly string[] StepNames = { "−0.05", "−0.01", "+0.01", "+0.05" };

    static float R3(float s) => MathF.Round(s * 1000f) / 1000f;
    static string S3(float s) => s.ToString("0.000", Inv);
    static string Sig(double t) => t.ToString("+0.0;-0.0", Inv) + "σ";

    /// <summary>The last two values (or the current sens and +20%); dev: --sfcompare A,B and --sfcompare-type TEXT.</summary>
    void SetupCompare(bool dev)
    {
        var s = Main.I.Settings;
        float a = s.SensCompare1, b = s.SensCompare2;
        if (!(a >= SensInput.Min && a <= SensInput.Max && b >= SensInput.Min && b <= SensInput.Max) || Mathf.Abs(a - b) < 0.0005f)
            (a, b) = CompareDefaults();
        cmpSens[0] = a; cmpSens[1] = b;
        if (dev && ArgAfter("--sfcompare") is { } v)
        {
            var parts = v.Split(',', ';');
            if (parts.Length == 2 && SensInput.TryParse(parts[0], out var x) && SensInput.TryParse(parts[1], out var y))
            {
                cmpSens[0] = x; cmpSens[1] = y; cmpDev = true;
            }
            else GD.Print($"[sf] --sfcompare needs two sens, e.g. 0.30,0.45 (got '{v}')");
        }
        if (dev && ArgAfter("--sfcompare-type") is { } typed) { cmpEdit = 1; cmpBuf = typed; }
    }

    (float, float) CompareDefaults() => (R3(current), R3(Mathf.Min(SensInput.Max, current * 1.2f)));

    void OpenCompare()
    {
        cardEye ??= G.View.Eye;
        phase = Ph.CompareSetup;
        phaseT = 0;
        GD.Print($"[sf] compare setup: sens1={cmpSens[0]:0.000} sens2={cmpSens[1]:0.000}");
    }

    // =====================================================================================
    // Value entry
    // =====================================================================================

    /// <summary>Compare card i as (x, y, w, h), like <see cref="SetupLayout"/>: the two values, their − / + steppers,
    /// then BACK, START and RESET.</summary>
    static (float X, float Y, float W, float H) CmpLayout(int i)
    {
        if (i < KStep0) return ((i == 0 ? -1 : 1) * 0.36f, 0.06f, 0.62f, 0.40f);
        if (i < KBack)
        {
            int f = (i - KStep0) / 4, j = (i - KStep0) % 4;
            return ((f == 0 ? -1 : 1) * 0.36f + (j - 1.5f) * 0.155f, -0.245f, 0.145f, 0.11f);
        }
        return i switch
        {
            KBack => (-0.52f, -0.50f, 0.30f, 0.11f),
            KGo => (0f, -0.50f, 0.50f, 0.11f),
            _ => (0.52f, -0.50f, 0.30f, 0.11f),
        };
    }

    (Vector3 C, Vector3 R, Vector3 U) CmpQuad(int i) => CardQuad(CmpLayout(i));

    void UpdateCompareSetup(bool pressed)
    {
        cardEye ??= G.View.Eye;
        TypeKeys();
        if (!pressed || phaseT < 0.4f) return;
        int i = CardHit(G.View.Eye, G.View.Forward, KCount, CmpQuad);
        if (i < 0) return;
        if (i < KStep0)
        {
            CommitEdit();
            cmpEdit = i;
            cmpBuf = "";
            G.Sound("head", 0.5f, 1.2f);
            return;
        }
        CommitEdit();
        if (i < KBack)
        {
            int f = (i - KStep0) / 4;
            cmpSens[f] = Mathf.Clamp(R3(cmpSens[f] + StepVals[(i - KStep0) % 4]), SensInput.Min, SensInput.Max);
            G.Sound("tick", 0.3f, 1.2f);
        }
        else if (i == KBack) { phase = Ph.Intro; phaseT = 0; G.Sound("tick", 0.3f, 1.2f); }
        else if (i == KReset) { (cmpSens[0], cmpSens[1]) = CompareDefaults(); G.Sound("tick", 0.3f, 1.2f); }
        else BeginCompare();
    }

    static readonly Godot.Key[] TypeKeyList = Enumerable.Range(0, 10).SelectMany(d => new[] { Godot.Key.Key0 + d, Godot.Key.Kp0 + d })
        .Concat(new[] { Godot.Key.Period, Godot.Key.Comma, Godot.Key.KpPeriod, Godot.Key.Backspace, Godot.Key.Delete, Godot.Key.Enter, Godot.Key.KpEnter, Godot.Key.Tab })
        .ToArray();

    /// <summary>Typing into the selected value: digits and . or , (max 6 characters), Backspace, Delete, Enter, Tab.</summary>
    void TypeKeys()
    {
        foreach (var key in TypeKeyList)
        {
            bool down = Input.IsKeyPressed(key);
            bool edge = down && !cmpHeld.Contains(key);
            if (down) cmpHeld.Add(key); else cmpHeld.Remove(key);
            if (!edge || cmpEdit < 0) continue;
            if (key >= Godot.Key.Key0 && key <= Godot.Key.Key9) Type((char)('0' + (key - Godot.Key.Key0)));
            else if (key >= Godot.Key.Kp0 && key <= Godot.Key.Kp9) Type((char)('0' + (key - Godot.Key.Kp0)));
            else if (key is Godot.Key.Period or Godot.Key.Comma or Godot.Key.KpPeriod) Type('.');
            else if (key == Godot.Key.Backspace) { if (cmpBuf.Length > 0) cmpBuf = cmpBuf[..^1]; }
            else if (key == Godot.Key.Delete) cmpBuf = "";
            else if (key is Godot.Key.Enter or Godot.Key.KpEnter) { CommitEdit(); G.Sound("tick", 0.3f, 1.2f); }
            else if (key == Godot.Key.Tab) { int next = 1 - cmpEdit; CommitEdit(); cmpEdit = next; cmpBuf = ""; }
        }
    }

    void Type(char ch)
    {
        if (cmpBuf.Length >= 6) return;
        if (ch == '.') { if (cmpBuf.Contains('.')) return; if (cmpBuf.Length == 0) cmpBuf = "0"; }
        cmpBuf += ch;
        G.Sound("tick", 0.15f, 1.5f);
    }

    /// <summary>Ends typing: a valid number replaces the value, anything else keeps the old one (with a hint).</summary>
    void CommitEdit()
    {
        if (cmpEdit < 0) return;
        if (cmpBuf.Length > 0)
        {
            if (SensInput.TryParse(cmpBuf, out var v)) cmpSens[cmpEdit] = v;
            else Notice(SensInput.Hint);
        }
        cmpEdit = -1;
        cmpBuf = "";
    }

    /// <summary>A value as it stands now (the typed text when it is a valid number).</summary>
    float CmpVal(int f) => f == cmpEdit && SensInput.TryParse(cmpBuf, out var v) ? v : cmpSens[f];

    /// <summary>Why the comparison can't start (null = it can).</summary>
    string? CmpError()
    {
        for (int f = 0; f < 2; f++)
        {
            float cm = Cm(CmpVal(f));
            if (!(cm >= CmpMinCm && cm <= CmpMaxCm))
                return L.F($"Sens {f + 1} is {cm:0.0} cm/360 at {dpi} DPI: pick a sens between {CmpMinCm:0} and {CmpMaxCm:0} cm/360 (or check your DPI in Settings).");
        }
        if (Mathf.Abs(CmpVal(0) - CmpVal(1)) < 0.0005f) return L.N("The two sens are the same: pick two different values.");
        return null;
    }

    /// <summary>Allowed, but worth a word (null = nothing to say).</summary>
    string? CmpWarning()
    {
        float a = CmpVal(0), b = CmpVal(1);
        float ratio = Mathf.Max(a, b) / Mathf.Max(1e-6f, Mathf.Min(a, b)), pct = (ratio - 1f) * 100f;
        if (ratio < CmpCloseRatio) return L.F($"Only {pct:0.0}% apart: values this close often come out as no clear difference.");
        if (ratio >= CmpFarRatio) return L.F($"{pct:0}% apart, very far: the test still picks one, but your best sens may be in between. The full Sens Finder searches that range.");
        return null;
    }

    void BeginCompare()
    {
        CommitEdit();
        if (CmpError() is { } e) { Notice(e); G.Sound("fail", 0.4f); GD.Print($"[sf] compare refused: {e}"); return; }
        G.Sound("head", 0.8f);
        compareMode = true;
        var s = Main.I.Settings;
        s.SensCompare1 = cmpSens[0];
        s.SensCompare2 = cmpSens[1];
        s.Save();
        var st = new SfStage { Compare = true, Step = -1, Centre = SfStats.Midpoint(cmpSens[0], cmpSens[1]), HalfWidth = 1f };
        st.Sens[0] = cmpSens[0];
        st.Sens[1] = cmpSens[1];
        SetupStage(st);
    }

    // =====================================================================================
    // Result
    // =====================================================================================

    void FinishCompare(SfStage st)
    {
        G.TrialSens = null;
        Event("sens_set", Main.I.Sens, -1);
        flickT.Visible = trackT.Visible = microT.Visible = false;
        cmpStage = st;
        var r = st.Result!;
        cmpT = r.T;
        (cmpWin, cmpStrength) = SfStats.CompareVerdict(cmpT);
        confidence = aborted ? (cmpStrength > 0 ? "Low" : "") : SfStats.CompareConfidence(cmpT);
        cmpMid = SfStats.Midpoint(st.Sens[0], st.Sens[1]);
        // not "sens_result": a comparison isn't a Sens Finder recommendation for the coach's sens advice
        Event("sens_compare", cmpWin >= 0 ? st.Sens[cmpWin] : 0f, (float)cmpT);
        string verdict = cmpStrength == 2 ? "clear" : cmpStrength == 1 ? "edge" : "none";
        string micro = r.HasMicro ? $"{r.TMicro:+0.00;-0.00}" : "n/a";
        GD.Print($"[sf] COMPARE RESULT sens1={st.Sens[0]:0.000} ({Cm(st.Sens[0]):0.0}cm) sens2={st.Sens[1]:0.000} ({Cm(st.Sens[1]):0.0}cm) "
            + $"t={cmpT:+0.00;-0.00} d={r.D:+0.000;-0.000} verdict={verdict} winner={(cmpWin >= 0 ? st.Sens[cmpWin].ToString("0.000", Inv) : "none")} "
            + $"confidence={(confidence == "" ? "none" : confidence)} flick_t={r.TFlick:+0.00;-0.00} micro_t={micro} track_t={r.TTrack:+0.00;-0.00} "
            + $"mid={cmpMid:0.000} ({Cm(cmpMid):0.0}cm) aborted={aborted}");
        phase = Ph.Summary;
        phaseT = 0;
    }

    /// <summary>One task's verdict: the better condition (|t| ≥ 1, like the finder's per-part split) or -1.</summary>
    static int TaskWin(double t) => Math.Abs(t) >= SfStats.EdgeT ? (t > 0 ? 0 : 1) : -1;

    (float MtMs, float HitPct) FlickNums(int cond)
    {
        var bs = cmpStage!.Blocks.Where(b => b.Cond == cond).ToList();
        var mts = bs.SelectMany(b => b.FlickMt).ToList();
        int h = bs.Sum(b => b.Hits), a = bs.Sum(b => b.Attempts);
        return (mts.Count > 0 ? Median(mts) * 1000f : 0f, a > 0 ? 100f * h / a : 0f);
    }

    float TrackPct(int cond)
    {
        var bs = cmpStage!.Blocks.Where(b => b.Cond == cond).ToList();
        float tot = bs.Sum(b => b.TrackTotal);
        return tot > 0 ? 100f * bs.Sum(b => b.TrackOn) / tot : 0f;
    }

    string CmpTitle()
    {
        if (aborted) return L.N("STOPPED EARLY: TAKE A BREAK AND RUN IT AGAIN");
        var st = cmpStage!;
        if (cmpStrength == 2) return L.F($"SENS {cmpWin + 1} WINS: {S3(st.Sens[cmpWin])}");
        if (cmpStrength == 1) return L.F($"SLIGHT EDGE TO {S3(st.Sens[cmpWin])}: NOT A CLEAR WIN");
        return L.F($"NO CLEAR DIFFERENCE BETWEEN {S3(st.Sens[0])} AND {S3(st.Sens[1])}");
    }

    IEnumerable<(string Label, string Value)> CompareResultLines()
    {
        var st = cmpStage;
        if (st?.Result is not { } r) yield break;
        if (aborted) yield return (L.N("Result"), L.N("aborted (fatigue)"));
        else yield return (L.N("Result"), cmpStrength == 2 ? L.F($"{S3(st.Sens[cmpWin])} wins") : cmpStrength == 1 ? L.F($"slight edge: {S3(st.Sens[cmpWin])}") : L.N("no clear difference"));
        yield return (L.N("Confidence"), (confidence == "" ? L.T("none") : L.C("confidence", confidence)) + " · " + Sig(cmpT));
        for (int f = 0; f < 2; f++) yield return (L.F($"Sens {f + 1}"), L.F($"{S3(st.Sens[f])} · {Cm(st.Sens[f]):0.0} cm"));
        yield return (L.N("Midpoint"), L.F($"{S3(cmpMid)} · {Cm(cmpMid):0.0} cm"));
        string Task(double t)
        {
            int w = TaskWin(t);
            return w >= 0 ? L.F($"better at {S3(st.Sens[w])}") : L.N("no clear winner");
        }
        yield return (L.N("Flicks"), Task(r.TFlick));
        if (r.HasMicro) yield return (L.N("Micro-adjust"), Task(r.TMicro));
        yield return (L.N("Tracking"), Task(r.TTrack));
        yield return (L.N("Run again"), L.N("another day"));
        yield return (L.N("In VALORANT"), L.N("set it yourself"));
    }

    /// <summary>The winner after a clear win; otherwise the midpoint. ("Keep my sens" follows in ResultButtons.)</summary>
    IEnumerable<(string Label, Action Act)> CompareUseButtons()
    {
        if (aborted || cmpStage == null) yield break;
        float v = cmpStrength == 2 ? cmpStage.Sens[cmpWin] : cmpMid;
        string label = cmpStrength == 2 ? L.F($"Use {S3(v)} in ValTrainer") : L.F($"Use the midpoint {S3(v)} in ValTrainer");
        yield return (label, () =>
        {
            var s = Main.I.Settings;
            s.UseSensOverride = true;
            s.SensOverride = v;
            s.Save();
        });
    }

    // =====================================================================================
    // Drawing: the entry screen
    // =====================================================================================

    void DrawCompareSetup(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.6f);
        float cx = size.X / 2, pw = Mathf.Min(size.X - 40 * k, 1240 * k), x = cx - pw / 2 + 36 * k, w = pw - 72 * k;
        var top = new Rect2(cx - pw / 2, 66 * k, pw, 190 * k);
        Gfx.Plate(c, top, 20 * k, new Color(0.06f, 0.1f, 0.14f, 0.96f), new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(top.Position, new Vector2(60 * k, 3 * k)), UiTheme.Teal);
        float y = top.Position.Y + 52 * k;
        Gfx.Text(c, UiTheme.Display, "COMPARE 2 SENS", x, y, UiTheme.Fs(44, k), UiTheme.Text);
        Gfx.TextR(c, UiTheme.HudWide, L.F($"BLIND A/B TEST · ABOUT {Mathf.Max(1, Mathf.RoundToInt(cfg.CompareMinutes))} MINUTES"), top.End.X - 36 * k, y - 8 * k, UiTheme.Fs(14, k), UiTheme.Dim);
        y += 32 * k;
        Gfx.TextFit(c, UiTheme.Body, L.N("Enter two VALORANT sens you like. You play both blind, as A and B, and the test tells you which one you aim better with."),
            x, y, UiTheme.Fs(18, k), UiTheme.Text, w);
        y += 28 * k;
        Gfx.TextFit(c, UiTheme.Body, L.F($"Your current sens is {S3(current)} ({Cm(current):0.0} cm/360 at {dpi} DPI)."), x, y, UiTheme.Fs(16, k), UiTheme.Dim, w);
        y += 26 * k;
        string? err = CmpError(), warn = CmpWarning();
        float a = CmpVal(0), b = CmpVal(1);
        float pct = (Mathf.Max(a, b) / Mathf.Max(1e-6f, Mathf.Min(a, b)) - 1f) * 100f;
        string status = Now < noticeUntil ? notice : err ?? warn ?? L.F($"Ready: {pct:0.0}% apart. {cfg.CompareBlocks} rounds, half with each sens, in a random order.");
        Gfx.TextFit(c, UiTheme.Body, status, x, y, UiTheme.Fs(16, k), Now < noticeUntil || err != null ? UiTheme.Accent : warn != null ? UiTheme.Warn : UiTheme.Good, w);
        y += 30 * k;
        Gfx.Text(c, UiTheme.HudWide, "SHOOT A SENS TO TYPE IT, OR SHOOT − / + · THEN SHOOT START", x, y, UiTheme.Fs(15, k), UiTheme.Teal);

        var cam = c.GetViewport().GetCamera3D();
        if (cam == null) return;
        int hover = CardHit(G.View.Eye, G.View.Forward, KCount, CmpQuad);
        var rects = CardRects(cam, KCount, CmpQuad);
        for (int i = 0; i < KCount; i++)
        {
            if (rects[i] is not { } r) continue;
            bool hov = hover == i;
            if (i < KStep0) DrawValueCard(c, r, k, i, hov);
            else if (i < KBack) DrawButtonCard(c, r, k, L.Raw(StepNames[(i - KStep0) % 4]), null, true, hov, UiTheme.Text);
            else if (i == KBack) DrawButtonCard(c, r, k, L.T("BACK"), L.T("to the Sens Finder"), false, hov, UiTheme.Text);
            else if (i == KReset) DrawButtonCard(c, r, k, L.T("RESET"), L.T("your sens and +20%"), true, hov, UiTheme.Good);
            else DrawButtonCard(c, r, k, "START", null, true, hov, err != null ? UiTheme.Faint : UiTheme.Accent);
        }
    }

    void DrawValueCard(CanvasItem c, Rect2 r, float k, int f, bool hover)
    {
        bool editing = cmpEdit == f;
        CardFrame(c, r, k, editing, hover);
        float pad = 16 * k, x = r.Position.X + pad, w = r.Size.X - 2 * pad, cxr = r.GetCenter().X;
        Gfx.Text(c, UiTheme.HudWide, L.F($"SENS {f + 1}"), x, r.Position.Y + 30 * k, UiTheme.Fs(15, k), editing ? UiTheme.Good : UiTheme.Dim);
        float v = CmpVal(f);
        if (Mathf.Abs(v - current) < 0.0005f) Gfx.TextR(c, UiTheme.HudWide, "YOUR SENS", r.End.X - pad, r.Position.Y + 30 * k, UiTheme.Fs(12, k), UiTheme.Teal);
        // while typing: the text so far and a blinking caret (centred on the text + caret, so it doesn't jump)
        string shown = editing ? cmpBuf + ((int)(RealNow * 2.5f) % 2 == 0 ? "_" : "") : S3(cmpSens[f]);
        string measured = editing ? cmpBuf + "_" : shown;
        var vcol = editing && cmpBuf.Length > 0 && !SensInput.TryParse(cmpBuf, out _) ? UiTheme.Accent : UiTheme.Text;
        int vsz = UiTheme.Fs(84, k);
        Gfx.TextRaw(c, UiTheme.Display, shown, cxr - Gfx.RawW(UiTheme.Display, measured, vsz) / 2, r.Position.Y + r.Size.Y * 0.58f, vsz, vcol);
        Gfx.TextFit(c, UiTheme.Body, L.F($"eDPI {v * dpi:0} · {Cm(v):0.0} cm/360"), x, r.End.Y - 44 * k, UiTheme.Fs(17, k), UiTheme.Text, w, HorizontalAlignment.Center);
        Gfx.TextFit(c, UiTheme.HudWide, editing ? L.T("TYPE A NUMBER · ENTER TO CONFIRM · TAB: THE OTHER ONE") : L.T("SHOOT HERE TO TYPE A VALUE"),
            x - 8 * k, r.End.Y - 16 * k, UiTheme.Fs(12, k), editing ? UiTheme.Good : UiTheme.Faint, w + 16 * k, HorizontalAlignment.Center);
    }

    // =====================================================================================
    // Drawing: the result
    // =====================================================================================

    void DrawCompareSummary(CanvasItem c, Vector2 size, float k)
    {
        Dim(c, size, 0.9f);
        var st = cmpStage;
        if (st?.Result is not { } res) return;
        float pw = Mathf.Min(size.X - 40 * k, 1500 * k), top = 100 * k, ph = Mathf.Max(400 * k, size.Y - 114 * k - top);
        var r = new Rect2(size.X / 2 - pw / 2, top, pw, ph);
        Gfx.Plate(c, r, 24 * k, new Color(0.06f, 0.1f, 0.14f, 0.97f), new Color(UiTheme.Text, 0.14f));
        c.DrawRect(new Rect2(r.Position, new Vector2(60 * k, 3 * k)), UiTheme.Teal);
        float x0 = r.Position.X + 44 * k, x1 = r.End.X - 44 * k, y = r.Position.Y;

        // header
        int hs = UiTheme.Fs(13, k);
        string tagText = aborted ? L.N("ABORTED") : L.N("SENS COMPARISON");
        var tag = new Rect2(x0, y + 30 * k, Gfx.TextW(UiTheme.HudWide, tagText, hs) + 18 * k, 22 * k);
        c.DrawRect(tag, aborted ? UiTheme.Warn : UiTheme.Teal);
        Gfx.Text(c, UiTheme.HudWide, tagText, tag.Position.X + 9 * k, Gfx.Mid(tag.GetCenter().Y, hs), hs, Colors.Black);
        Gfx.TextR(c, UiTheme.Body, L.F($"{st.Order.Length} blind rounds · {dpi} DPI"), x1, Gfx.Mid(tag.GetCenter().Y, UiTheme.Fs(16, k)), UiTheme.Fs(16, k), UiTheme.Dim);
        var tcol = aborted ? UiTheme.Warn : cmpStrength == 2 ? UiTheme.Good : UiTheme.Text;
        Gfx.TextFit(c, UiTheme.Display, CmpTitle(), x0, y + 100 * k, UiTheme.Fs(52, k), tcol, x1 - x0);
        c.DrawRect(new Rect2(x0, y + 122 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));

        float split = x0 + (x1 - x0) * 0.62f, colTop = y + 146 * k, colH = ph - 146 * k - 150 * k;
        DrawCompareLeft(c, new Rect2(x0, colTop, split - x0 - 30 * k, colH), k, st, res);
        DrawCompareRight(c, new Rect2(split + 10 * k, colTop, x1 - split - 10 * k, colH), k, st, res);

        // notes
        float ny = r.End.Y - 116 * k;
        c.DrawRect(new Rect2(x0, ny - 22 * k, x1 - x0, 1), new Color(UiTheme.Text, 0.1f));
        int ns = UiTheme.Fs(18, k);
        string n1 = aborted ? abortWhy
            : cmpStrength == 2 ? L.F($"Your aim was clearly better at {S3(st.Sens[cmpWin])}. The midpoint {S3(cmpMid)} ({Cm(cmpMid):0.0} cm/360) is only worth trying if {S3(st.Sens[cmpWin])} feels uncomfortable.")
            : L.F($"If both feel fine and neither wins clearly, the midpoint {S3(cmpMid)} ({Cm(cmpMid):0.0} cm/360) is a reasonable middle.");
        Gfx.TextFit(c, UiTheme.Body, n1, x0, ny, ns, aborted ? UiTheme.Warn : UiTheme.Text, x1 - x0);
        Gfx.TextFit(c, UiTheme.Body, L.N("Run the comparison again on another day. If both runs pick the same sens, go with it; if they disagree, use the midpoint."),
            x0, ny + 30 * k, ns, UiTheme.Dim, x1 - x0);
        Gfx.TextFit(c, UiTheme.Body, L.N("VALORANT isn't changed automatically: set it yourself in VALORANT › Settings › General › Mouse › Sensitivity: Aim."),
            x0, ny + 60 * k, UiTheme.Fs(16, k), UiTheme.Faint, x1 - x0);
        if (phaseT > 3f)
        {
            float a = 0.55f + 0.45f * Mathf.Sin(RealNow * 4f);
            Gfx.TextC(c, UiTheme.HudWide, "CLICK TO CONTINUE", r.GetCenter().X, r.End.Y - 22 * k, UiTheme.Fs(18, k), new Color(UiTheme.Accent, a));
        }
    }

    /// <summary>The two sens side by side (eDPI, cm/360, the winner), then the per-task table.</summary>
    void DrawCompareLeft(CanvasItem c, Rect2 r, float k, SfStage st, SfComparison res)
    {
        float gap = 18 * k, cw = (r.Size.X - gap) / 2, ch = 128 * k;
        for (int f = 0; f < 2; f++)
        {
            var cr = new Rect2(r.Position.X + f * (cw + gap), r.Position.Y, cw, ch);
            bool win = cmpWin == f && cmpStrength > 0;
            var wc = cmpStrength == 2 ? UiTheme.Good : UiTheme.Warn;
            Gfx.Plate(c, cr, 12 * k, new Color(1, 1, 1, 0.03f), win ? new Color(wc, 0.9f) : new Color(UiTheme.Text, 0.14f));
            if (win) c.DrawRect(new Rect2(cr.Position.X, cr.Position.Y, cr.Size.X, 4 * k), wc);
            float px = cr.Position.X + 18 * k;
            Gfx.Text(c, UiTheme.HudWide, L.F($"SENS {f + 1}"), px, cr.Position.Y + 28 * k, UiTheme.Fs(13, k), UiTheme.Dim);
            if (win) Gfx.TextR(c, UiTheme.HudWide, cmpStrength == 2 ? L.T("WINNER") : L.T("SLIGHT EDGE"), cr.End.X - 18 * k, cr.Position.Y + 28 * k, UiTheme.Fs(13, k), wc);
            Gfx.TextRaw(c, UiTheme.Display, S3(st.Sens[f]), px, cr.Position.Y + 82 * k, UiTheme.Fs(50, k), win ? UiTheme.Text : UiTheme.Dim);
            Gfx.TextFit(c, UiTheme.Body, L.F($"eDPI {st.Sens[f] * dpi:0} · {Cm(st.Sens[f]):0.0} cm/360"), px, cr.Position.Y + 112 * k, UiTheme.Fs(17, k), UiTheme.Text, cr.Size.X - 36 * k);
        }

        // per task: what was measured at each sens, and which one it favoured
        float ty = r.Position.Y + ch + 34 * k, x = r.Position.X;
        float c1 = x + r.Size.X * 0.30f, c2 = x + r.Size.X * 0.53f, c3 = r.End.X - 8 * k;
        int ls = UiTheme.Fs(12, k), vs = UiTheme.Fs(16, k);
        Gfx.Text(c, UiTheme.HudWide, "BY TASK", x + 8 * k, ty, ls, UiTheme.Dim);
        Gfx.Text(c, UiTheme.HudWide, L.F($"SENS 1 · {S3(st.Sens[0])}"), c1, ty, ls, UiTheme.Dim);
        Gfx.Text(c, UiTheme.HudWide, L.F($"SENS 2 · {S3(st.Sens[1])}"), c2, ty, ls, UiTheme.Dim);
        Gfx.TextR(c, UiTheme.HudWide, "BETTER AT", c3, ty, ls, UiTheme.Dim);
        var rows = new List<(string Name, string V0, string V1, double T)>();
        var f0 = FlickNums(0); var f1 = FlickNums(1);
        rows.Add((L.T("FLICKS"), L.F($"{f0.MtMs:0} ms · {f0.HitPct:0}% hits"), L.F($"{f1.MtMs:0} ms · {f1.HitPct:0}% hits"), res.TFlick));
        if (res.HasMicro)
        {
            var m0 = SfStats.MicroStats(st, 0); var m1 = SfStats.MicroStats(st, 1);
            rows.Add((L.T("MICRO-ADJUSTMENTS"), L.F($"settle {m0.SettleMs:0} ms · {m0.HitPct:0}%"), L.F($"settle {m1.SettleMs:0} ms · {m1.HitPct:0}%"), res.TMicro));
        }
        rows.Add((L.T("TRACKING"), L.F($"{TrackPct(0):0}% on target"), L.F($"{TrackPct(1):0}% on target"), res.TTrack));
        float rh = Mathf.Min(54 * k, (r.End.Y - ty - 40 * k) / (rows.Count + 1));
        float yy = ty + 14 * k;
        for (int i = 0; i <= rows.Count; i++)
        {
            bool overall = i == rows.Count;
            float cy = yy + rh * (i + 0.5f);
            if (i % 2 == 0) c.DrawRect(new Rect2(x, cy - rh / 2, r.Size.X, rh), new Color(1, 1, 1, 0.025f));
            if (overall) c.DrawRect(new Rect2(x, cy - rh / 2, r.Size.X, 1), new Color(UiTheme.Text, 0.15f));
            var (name, v0, v1, t) = overall ? (L.T("OVERALL"), "", "", cmpT) : rows[i];
            int w = overall ? (cmpStrength > 0 ? cmpWin : -1) : TaskWin(t);
            Gfx.Text(c, UiTheme.HudWide, name, x + 8 * k, Gfx.Mid(cy, ls), ls, UiTheme.Text);
            if (!overall)
            {
                Gfx.TextFit(c, UiTheme.Body, v0, c1, Gfx.Mid(cy, vs), vs, w == 0 ? UiTheme.Text : UiTheme.Dim, c2 - c1 - 10 * k);
                Gfx.TextFit(c, UiTheme.Body, v1, c2, Gfx.Mid(cy, vs), vs, w == 1 ? UiTheme.Text : UiTheme.Dim, c3 - c2 - 150 * k);
            }
            else Gfx.TextFit(c, UiTheme.Body, L.F($"{Math.Abs(res.D):0.00} SD apart, all tasks weighted"), c1, Gfx.Mid(cy, UiTheme.Fs(14, k)), UiTheme.Fs(14, k), UiTheme.Faint, c3 - c1 - 150 * k);
            string verdict = w >= 0 ? $"{S3(st.Sens[w])}  {Sig(t)}" : L.F($"EVEN  {Sig(t)}");
            Gfx.TextR(c, UiTheme.HudWide, L.Raw(verdict), c3, Gfx.Mid(cy, UiTheme.Fs(14, k)), UiTheme.Fs(14, k), w >= 0 ? UiTheme.Good : UiTheme.Warn);
        }
    }

    /// <summary>Margin, confidence and the midpoint.</summary>
    void DrawCompareRight(CanvasItem c, Rect2 r, float k, SfStage st, SfComparison res)
    {
        float x = r.Position.X, y = r.Position.Y, w = r.Size.X;
        int ls = UiTheme.Fs(12, k), vs = UiTheme.Fs(34, k), ss = UiTheme.Fs(16, k);
        float step = Mathf.Min(112 * k, r.Size.Y / 3);
        void Row(string label, string value, string sub, Color vc)
        {
            Gfx.Text(c, UiTheme.HudWide, label, x, y + 12 * k, ls, UiTheme.Dim);
            Gfx.TextFit(c, UiTheme.Display, value, x, y + 50 * k, vs, vc, w);
            var subs = sub.Split('\n');
            for (int i = 0; i < subs.Length; i++) Gfx.TextFit(c, UiTheme.Body, subs[i], x, y + (74 + 21 * i) * k, ss, UiTheme.Dim, w);
            y += step;
        }
        string margin = cmpStrength > 0 ? L.F($"{Math.Abs(res.D):0.00} SD · {Sig(cmpT)}") : L.F($"WITHIN NOISE · {Sig(cmpT)}");
        string marginSub = cmpStrength > 0
            ? L.F($"sens {cmpWin + 1} scored higher over all tasks") + "\n" + L.N("SD: the spread of your own rounds; σ: the margin over its noise")
            : L.N("both sens scored the same within noise") + "\n" + L.N("SD: the spread of your own rounds; σ: the margin over its noise");
        Row("MARGIN", L.Raw(margin), marginSub, cmpStrength == 2 ? UiTheme.Good : UiTheme.Text);
        string conf = confidence == "" ? L.T("NO CLEAR WINNER") : L.Upper(L.C("confidence", confidence));
        string confSub = confidence switch
        {
            "High" => L.N("3σ or more: very unlikely to be luck"),
            "Medium" => L.N("2σ or more: luck does this about 1 time in 20"),
            "Low" => L.N("1–2σ: could easily be luck"),
            _ => L.N("under 1σ: no real difference showed up"),
        };
        var cc = confidence == "High" ? UiTheme.Good : confidence == "Medium" ? UiTheme.Warn : UiTheme.Accent;
        Row("CONFIDENCE", conf, confSub, confidence == "" ? UiTheme.Text : cc);
        float avg = R3((st.Sens[0] + st.Sens[1]) / 2f);
        Row("MIDPOINT", L.F($"{S3(cmpMid)} · {Cm(cmpMid):0.0} cm"),
            L.F($"eDPI {cmpMid * dpi:0} · the same % step from both") + "\n" + L.F($"(the plain average is {S3(avg)})"), UiTheme.Text);
    }
}
