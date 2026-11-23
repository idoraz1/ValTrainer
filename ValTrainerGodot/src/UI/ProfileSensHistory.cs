using Godot;
using ValTrainer.Analysis;
using ValTrainer.Core;

namespace ValTrainer.UI;

/// <summary>
/// Coach → Sensitivity → "Your sens history" (Discord suggestion): which sens of the player's own run history they score
/// best with, drill by drill (<see cref="SensHistory"/>). A tab of the sensitivity panel, so the screen keeps its layout.
/// COMPARE BY switches the buckets to the runs' DPI, polling rate or full setup (sens + DPI + polling rate; also a
/// Discord suggestion). Runs without the value are one "unknown" row that is never compared.
/// </summary>
public partial class ProfileScreen
{
    /// <summary>The sensitivity panel shows the history tab (static: survives a rebuild and reopening the screen).</summary>
    static bool showSensHistory = Main.I?.Dev == true && Core.CmdLine.Has("--sens-history");

    /// <summary>What the history compares (dev "--sens-history-by dpi|polling|setup" for screenshots).</summary>
    static SensHistory.By historyBy = (Main.I?.Dev == true ? Core.CmdLine.DevAfter("--sens-history-by") : null) switch
    {
        "dpi" => SensHistory.By.Dpi,
        "polling" => SensHistory.By.Polling,
        "setup" => SensHistory.By.Setup,
        _ => SensHistory.By.Sens,
    };

    const int HistoryRows = 4;

    /// <summary>RECOMMENDATION | YOUR SENS HISTORY tabs on top of the sensitivity panel.</summary>
    Control SensTabs(bool history)
    {
        float k = K;
        var row = HBox(8 * k);
        VButton Tab(string label, bool on, bool toHistory, float w)
        {
            var b = new VButton { Label = label, Kind = VButton.Look.Select, Selected = on, K = k, FontPx = 14, CustomMinimumSize = new Vector2(w * k, 34 * k) };
            b.Pressed += () =>
            {
                if (showSensHistory == toHistory) return;
                showSensHistory = toHistory;
                Rebuild();
            };
            return b;
        }
        row.AddChild(Tab(L.N("RECOMMENDATION"), !history, false, 190));
        var h = Tab(L.N("YOUR SENS HISTORY"), history, true, 210);
        h.TooltipText = L.T("Which of the sensitivities you've played you score best with, drill by drill");
        row.AddChild(h);
        return row;
    }

    /// <summary>COMPARE BY: SENS | DPI | POLLING | FULL SETUP.</summary>
    Control HistoryByRow()
    {
        float k = K;
        var row = HBox(6 * k);
        var cap = Lbl(L.T("COMPARE BY"), UiTheme.HudWide, 11, UiTheme.Dim);
        cap.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(cap);
        row.AddChild(Spacer(4 * k, 0));
        void Opt(string label, SensHistory.By by, float w, string tip)
        {
            var b = new VButton { Label = label, Kind = VButton.Look.Select, Selected = historyBy == by, K = k, FontPx = 12, CustomMinimumSize = new Vector2(w * k, 28 * k) };
            b.TooltipText = tip;
            b.Pressed += () =>
            {
                if (historyBy == by) return;
                historyBy = by;
                Rebuild();
            };
            row.AddChild(b);
        }
        Opt(L.N("SENS"), SensHistory.By.Sens, 80, L.T("Group your runs by sensitivity (in cm/360, so a DPI change with the same cm/360 is the same sens)"));
        Opt(L.N("DPI"), SensHistory.By.Dpi, 70, L.T("Group your runs by the mouse DPI they were played at"));
        Opt(L.N("POLLING RATE"), SensHistory.By.Polling, 130, L.T("Group your runs by the mouse polling rate set in ValTrainer when they were played"));
        Opt(L.N("FULL SETUP"), SensHistory.By.Setup, 120, L.T("Group your runs by sens + DPI + polling rate together"));
        return row;
    }

    Control SensHistoryPanel(SensHistory h)
    {
        float k = K;
        var panel = new VPanel { Title = "SENSITIVITY", Caption = HistoryCaption(h), K = k };
        var v = VBox(10 * k);
        panel.AddChild(v);
        v.AddChild(SensTabs(true));
        v.AddChild(HistoryByRow());

        var (head, col) = HistoryHeadline(h);
        v.AddChild(Lbl(head, UiTheme.Display, 24, col));
        if (HistoryText(h) is { Length: > 0 } text) v.AddChild(Para(text, UiTheme.Body, 15, new Color(UiTheme.Text, 0.85f)));

        var rows = HistoryRowsOf(h);
        if (rows.Count >= 2)
        {
            v.AddChild(new DrawBox { CustomMinimumSize = new Vector2(0, (24 + 40 * rows.Count) * k), OnDraw = d => DrawHistory(d, h, rows) });
            int hidden = h.Buckets.Count - rows.Count;
            if (hidden > 0)
                v.AddChild(Para(h.Grouping == SensHistory.By.Sens
                    ? L.P(hidden, $"+{hidden} more sens with only a few runs", $"+{hidden} more sens with only a few runs")
                    : L.P(hidden, $"+{hidden} more setup with only a few runs", $"+{hidden} more setups with only a few runs"), UiTheme.Body, 12.5f, UiTheme.Faint));
        }

        var row = HBox(12 * k);
        v.AddChild(row);
        if (h.Grouping == SensHistory.By.Sens && h.Result is SensHistory.Verdict.OneSens or SensHistory.Verdict.NoRuns or SensHistory.Verdict.NotEnough)
        {
            var finder = Btn("RUN SENS FINDER", VButton.Look.Secondary, () => CoachData.StartDrill("sensfinder"), 220, 44, 18);
            finder.TooltipText = L.T("Blind A/B test of sensitivities with flicks + tracking (about 15 minutes)");
            finder.Disabled = !CoachData.HasDrill("sensfinder");
            row.AddChild(finder);
        }
        string noteText = L.F($"Each drill is compared only with your own runs of the same drill, difficulty and gun. A sens needs {SensHistory.MinRuns} runs over {SensHistory.MinDrills} drills before it's judged.");
        if (h.Grouping != SensHistory.By.Sens)
            noteText = L.F($"Each drill is compared only with your own runs of the same drill, difficulty and gun. A setup needs {SensHistory.MinRuns} runs over {SensHistory.MinDrills} drills before it's judged; runs without the value aren't compared.");
        var note = Para(noteText, UiTheme.Body, 12.5f, UiTheme.Faint);
        note.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        row.AddChild(note);
        return panel;
    }

    static string SensText(SensHistory.Bucket b) => b.Sens.ToString("0.000", Inv);

    static string HzText(int hz) => hz.ToString(Inv) + " Hz";

    /// <summary>The bucket's name in sentences: "0.350", "800 DPI", "1000 Hz", "0.350 · 800 DPI · 1000 Hz".</summary>
    static string Name(SensHistory h, SensHistory.Bucket b) => h.Grouping switch
    {
        SensHistory.By.Dpi => L.F($"{b.Dpi} DPI"),
        SensHistory.By.Polling => HzText(b.PollingHz),
        SensHistory.By.Setup => SensText(b) + " · " + L.F($"{b.Dpi} DPI") + " · " + HzText(b.PollingHz),
        _ => SensText(b),
    };

    /// <summary>The big label of a table row (a setup shows its sens; DPI and polling rate go in the line under it).</summary>
    static string RowName(SensHistory h, SensHistory.Bucket b) =>
        b.Unknown ? L.T("UNKNOWN") : h.Grouping == SensHistory.By.Setup ? SensText(b) : Name(h, b).ToUpperInvariant();

    /// <summary>"0.350" or "0.300–0.400" (a DPI or polling bucket mixes the sens played at it).</summary>
    static string SensRange(SensHistory.Bucket b) =>
        MathF.Abs(b.SensMax - b.SensMin) < 0.0005f ? b.SensMin.ToString("0.000", Inv) : b.SensMin.ToString("0.000", Inv) + "–" + b.SensMax.ToString("0.000", Inv);

    static string DpiRange(SensHistory.Bucket b) =>
        b.DpiMax <= 0 ? "" : b.DpiMin == b.DpiMax ? L.F($"{b.DpiMin} DPI") : L.F($"{b.DpiMin}–{b.DpiMax} DPI");

    /// <summary>The small line under a row's name.</summary>
    static string RowSub(SensHistory h, SensHistory.Bucket b, int dpiNow)
    {
        if (b.Unknown)
            return h.Grouping switch
            {
                SensHistory.By.Dpi => L.P(b.AllRuns, $"{b.AllRuns} run from before DPI was saved", $"{b.AllRuns} runs from before DPI was saved"),
                SensHistory.By.Polling => L.P(b.AllRuns, $"{b.AllRuns} run without a polling rate", $"{b.AllRuns} runs without a polling rate"),
                _ => L.P(b.AllRuns, $"{b.AllRuns} run without a DPI or polling rate", $"{b.AllRuns} runs without a DPI or polling rate"),
            };
        switch (h.Grouping)
        {
            case SensHistory.By.Dpi: return L.F($"sens {SensRange(b)}");
            case SensHistory.By.Polling: return L.F($"sens {SensRange(b)}") + (DpiRange(b) is { Length: > 0 } dr ? " · " + dr : "");
            case SensHistory.By.Setup: return L.F($"{b.Dpi} DPI") + " · " + HzText(b.PollingHz) + " · " + L.F($"{b.Cm360:0.0} cm/360");
            default: return L.F($"{b.Cm360:0.0} cm/360") + (b.Dpi != dpiNow ? " · " + L.F($"{b.Dpi} DPI") : "");
        }
    }

    static string HistoryCaption(SensHistory h)
    {
        int known = h.Buckets.Count(b => !b.Unknown);
        return h.Result switch
        {
            SensHistory.Verdict.NoRuns => L.T("no runs yet"),
            SensHistory.Verdict.NoneKnown => L.T("nothing saved to compare yet"),
            SensHistory.Verdict.OneSens => h.Grouping == SensHistory.By.Sens ? L.T("one sens so far") : L.T("one setup so far"),
            _ => h.Grouping == SensHistory.By.Sens
                ? L.P(h.ComparedRuns, $"{h.Buckets.Count} sens · {h.ComparedRuns} run compared", $"{h.Buckets.Count} sens · {h.ComparedRuns} runs compared")
                : L.P(h.ComparedRuns, $"{known} setups · {h.ComparedRuns} run compared", $"{known} setups · {h.ComparedRuns} runs compared"),
        };
    }

    static (string Text, Color Color) HistoryHeadline(SensHistory h) => h.Result switch
    {
        SensHistory.Verdict.Best => (L.F($"YOU SCORE BEST AT {Name(h, h.Top!).ToUpperInvariant()}"), UiTheme.Good),
        SensHistory.Verdict.NoClearDifference => (L.T("NO CLEAR DIFFERENCE YET"), UiTheme.Text),
        SensHistory.Verdict.NotEnough => (L.T("NOT ENOUGH RUNS TO COMPARE YET"), UiTheme.Warn),
        SensHistory.Verdict.OneSens => (h.Grouping switch
        {
            SensHistory.By.Dpi => L.T("ONE DPI SO FAR"),
            SensHistory.By.Polling => L.T("ONE POLLING RATE SO FAR"),
            SensHistory.By.Setup => L.T("ONE SETUP SO FAR"),
            _ => L.T("ONE SENS SO FAR"),
        }, UiTheme.Text),
        SensHistory.Verdict.NoneKnown => (h.Grouping == SensHistory.By.Dpi ? L.T("NO DPI SAVED WITH YOUR RUNS YET") : L.T("NO POLLING RATE SAVED WITH YOUR RUNS YET"), UiTheme.Text),
        _ => (L.T("NO RUNS YET"), UiTheme.Faint),
    };

    /// <summary>The plain-language verdict under the headline (thin data said plainly, like the rest of the coach).</summary>
    static string HistoryText(SensHistory h)
    {
        bool bySens = h.Grouping == SensHistory.By.Sens;
        // a DPI or polling bucket mixes every sens played at it: say so where a difference is shown
        string mixNote = h.Grouping switch
        {
            SensHistory.By.Dpi => " " + L.T("Each DPI includes every sens you played at it (your cm/360 changes with the DPI); FULL SETUP keeps them apart."),
            SensHistory.By.Polling => " " + L.T("Each polling rate includes every sens and DPI you played at it; FULL SETUP keeps them apart."),
            _ => "",
        };
        switch (h.Result)
        {
            case SensHistory.Verdict.NoRuns:
                return L.T("Every run remembers the sens you played it at. Play some drills and this compares your sensitivities.");
            case SensHistory.Verdict.NoneKnown:
                return h.Grouping == SensHistory.By.Dpi
                    ? L.T("Your runs are from before ValTrainer saved the DPI (1.22). Runs you play now remember it, so this fills in as you play.")
                    : L.T("None of your runs has a polling rate yet: ValTrainer can't read it from your mouse. Set yours in the ESC menu or Settings → General (as set in your mouse software) and your next runs remember it.");
            case SensHistory.Verdict.OneSens:
            {
                var b = h.Buckets.First(x => !x.Unknown);
                string n = Name(h, b);
                if (bySens)
                    return L.P(b.AllRuns, $"Your {b.AllRuns} run is at {SensText(b)}, so there's nothing to compare yet.",
                               $"All your {b.AllRuns} runs are at {SensText(b)}, so there's nothing to compare yet.")
                           + " " + L.T("Play some runs at another sens and this shows which one you score best with, drill by drill. Not sure what to try? The Sens Finder tests sensitivities blind.");
                string s = L.P(b.AllRuns, $"Your {b.AllRuns} run with this saved is at {n}, so there's nothing to compare yet.",
                    $"All your {b.AllRuns} runs with this saved are at {n}, so there's nothing to compare yet.");
                return s + " " + h.Grouping switch
                {
                    SensHistory.By.Dpi => L.T("Change your DPI for some runs (ESC menu or Settings) and this shows which one you score best with, drill by drill."),
                    SensHistory.By.Polling => L.T("Change your polling rate in your mouse software for some runs, set it in ValTrainer too (ESC menu or Settings), and this shows which one you score best with."),
                    _ => L.T("Play some runs with another sens, DPI or polling rate and this shows which setup you score best with, drill by drill."),
                };
            }
            case SensHistory.Verdict.NotEnough:
            {
                var parts = new List<string>();
                // the sens the player has to play more at; one that has plenty only waits for the others to cover its drills
                var lacking = h.Buckets.Where(b => !b.Unknown && !b.Enough && b.OwnShortfall).ToList();
                if (lacking.Count == 0) lacking = h.Buckets.Where(b => !b.Unknown && !b.Enough).ToList();
                foreach (var b in lacking.OrderByDescending(b => b.Current).ThenByDescending(b => b.AllRuns).Take(2))
                {
                    int need = Math.Max(b.NeedRuns, b.NeedDrills);
                    string line = bySens
                        ? L.P(need, $"Not enough runs at {SensText(b)} yet: play {need} more drill at that sens.",
                            $"Not enough runs at {SensText(b)} yet: play {need} more drills at that sens.")
                        : L.P(need, $"Not enough runs at {Name(h, b)} yet: play {need} more drill with it.",
                            $"Not enough runs at {Name(h, b)} yet: play {need} more drills with it.");
                    if (b.NeedDrills > 0)
                        line += " " + L.P(b.NeedDrills, $"Make it at least {b.NeedDrills} more different drill.", $"Make it at least {b.NeedDrills} more different drills.");
                    parts.Add(line);
                }
                parts.Add(bySens ? L.T("Only drills you've also played at another sens count, at the same difficulty and with the same gun.")
                    : L.T("Only drills you've also played with another setup count, at the same difficulty and with the same gun."));
                return string.Join(" ", parts);
            }
            case SensHistory.Verdict.NoClearDifference:
            {
                var a = h.Top!; var b = h.RunnerUp!;
                string an = Name(h, a), bn = Name(h, b);
                float d = (a.Rel - b.Rel) * 100;
                return (d >= 1
                        ? L.F($"{an} scores {d:0}% higher than {bn} on average, but that's within your normal run-to-run variation, so it could be luck.")
                        : L.F($"{an} and {bn} score about the same."))
                       + " " + L.T("Play the one that feels better, or keep mixing them to sharpen the picture.") + mixNote;
            }
            case SensHistory.Verdict.Best:
            {
                var a = h.Top!; var b = h.RunnerUp!;
                string an = Name(h, a), bn = Name(h, b);
                var s = new List<string>
                {
                    L.F($"At {an} you score {a.Rel * 100:+0;-0;0}% vs your average over {a.Runs} runs in {a.Drills} drills: clearly better than {bn}, not just luck (95% confidence)."),
                };
                if (MetricParts(a) is { Count: > 0 } mp)
                    s.Add(bySens ? L.F($"At that sens: {string.Join(", ", mp)}.") : L.F($"With that setup: {string.Join(", ", mp)}."));
                if (h.TopIsLatest) s.Add(L.F($"You played {an} most recently, so part of the gain may simply be practice."));
                if (a.Current) s.Add(bySens ? L.T("That's your current sens: keep it.") : L.T("That's your current setup: keep it."));
                else
                    s.Add(h.Grouping switch
                    {
                        SensHistory.By.Sens => L.F($"You're not on it now. To switch, set {SensText(a)} in VALORANT (Settings → General → Mouse)."),
                        SensHistory.By.Setup => L.F($"You're not on it now. To switch: sens {SensText(a)} in VALORANT, {a.Dpi} DPI and {HzText(a.PollingHz)} in your mouse software, and the same DPI and polling rate in ValTrainer (ESC menu)."),
                        _ => L.F($"You're not on it now. To switch, set {an} in your mouse software and in ValTrainer (ESC menu or Settings)."),
                    });
                return string.Join(" ", s) + mixNote;
            }
        }
        return "";
    }

    static List<string> MetricParts(SensHistory.Bucket b)
    {
        var m = new List<string>();
        if (!float.IsNaN(b.AccPts) && MathF.Abs(b.AccPts) >= 0.5f) m.Add(L.F($"accuracy {b.AccPts:+0.0;-0.0} points"));
        if (!float.IsNaN(b.KillRel) && MathF.Abs(b.KillRel) >= 0.01f)
            m.Add(b.KillRel < 0 ? L.F($"kills {-b.KillRel * 100:0}% faster") : L.F($"kills {b.KillRel * 100:0}% slower"));
        return m;
    }

    /// <summary>The rows of the table: the buckets with the most runs (the top one and the current one always), in
    /// value order, then the unknown bucket (if any) as an extra last row.</summary>
    static List<SensHistory.Bucket> HistoryRowsOf(SensHistory h)
    {
        var known = h.Buckets.Where(b => !b.Unknown).ToList();
        if (known.Count < 2) return new();
        var rows = known.Take(HistoryRows).ToList();
        foreach (var must in new[] { h.Top, known.FirstOrDefault(b => b.Current) })
            if (must != null && !rows.Contains(must)) rows[^1] = must;
        rows = h.Grouping switch
        {
            SensHistory.By.Dpi => rows.Distinct().OrderBy(b => b.Dpi).ToList(),
            SensHistory.By.Polling => rows.Distinct().OrderBy(b => b.PollingHz).ToList(),
            SensHistory.By.Setup => rows.Distinct().OrderBy(b => b.Cm360).ThenBy(b => b.Dpi).ThenBy(b => b.PollingHz).ToList(),
            _ => rows.Distinct().OrderBy(b => b.Sens * b.Dpi).ToList(), // low → high sens (eDPI)
        };
        if (h.Buckets.FirstOrDefault(b => b.Unknown) is { } unk) rows.Add(unk);
        return rows;
    }

    void DrawHistory(DrawBox d, SensHistory h, List<SensHistory.Bucket> rows)
    {
        float k = K, W = d.Size.X;
        int cap = UiTheme.Fs(11, k), big = UiTheme.Fs(22, k), sm = UiTheme.Fs(12, k), num = UiTheme.Fs(17, k);
        float xRuns = W * 0.36f, xDrills = W * 0.45f, bar0 = W * 0.51f, bar1 = W * 0.65f, xPct = W * 0.67f, xAcc = W * 0.83f, xKill = W * 0.95f;
        string col0 = h.Grouping switch { SensHistory.By.Dpi => L.N("DPI"), SensHistory.By.Polling => L.N("POLLING RATE"), SensHistory.By.Setup => L.N("SETUP"), _ => L.N("SENS") };
        Gfx.Text(d, UiTheme.HudWide, col0, 0, Gfx.Mid(8 * k, cap), cap, UiTheme.Dim);
        Gfx.TextC(d, UiTheme.HudWide, "RUNS", xRuns, Gfx.Mid(8 * k, cap), cap, UiTheme.Dim);
        Gfx.TextC(d, UiTheme.HudWide, "DRILLS", xDrills, Gfx.Mid(8 * k, cap), cap, UiTheme.Dim);
        Gfx.Text(d, UiTheme.HudWide, "VS YOUR AVERAGE", bar0, Gfx.Mid(8 * k, cap), cap, UiTheme.Dim);
        Gfx.TextC(d, UiTheme.HudWide, "ACCURACY", xAcc, Gfx.Mid(8 * k, cap), cap, UiTheme.Dim);
        Gfx.TextC(d, UiTheme.HudWide, "KILL TIME", xKill, Gfx.Mid(8 * k, cap), cap, UiTheme.Dim);

        float scale = Mathf.Max(0.10f, rows.Where(b => !float.IsNaN(b.Rel)).Select(b => MathF.Abs(b.Rel)).DefaultIfEmpty(0).Max());
        float mid = (bar0 + bar1) / 2, half = (bar1 - bar0) / 2;
        int dpiNow = Main.I.Settings.Dpi;
        for (int i = 0; i < rows.Count; i++)
        {
            var b = rows[i];
            float top = 24 * k + i * 40 * k, cy = top + 20 * k;
            if (i % 2 == 0) d.DrawRect(new Rect2(-6 * k, top, W + 12 * k, 40 * k), new Color(UiTheme.Text, 0.035f));
            bool best = h.Result == SensHistory.Verdict.Best && ReferenceEquals(b, h.Top);
            float a = b.Enough ? 1f : 0.6f;
            Color tc = new(best ? UiTheme.Good : UiTheme.Text, a);
            string name = RowName(h, b);
            int nameSize = Gfx.RawW(UiTheme.Display, name, big) > xRuns - 70 * k ? UiTheme.Fs(17, k) : big;
            Gfx.TextRaw(d, UiTheme.Display, name, 0, Gfx.Mid(cy - 4 * k, nameSize), nameSize, tc);
            Gfx.TextRaw(d, UiTheme.Body, RowSub(h, b, dpiNow), 0, Gfx.Mid(cy + 12 * k, UiTheme.Fs(10.5f, k)), UiTheme.Fs(10.5f, k), new Color(UiTheme.Dim, a));
            float cx = Gfx.RawW(UiTheme.Display, name, nameSize) + 10 * k;
            if (b.Current) cx += CoachDraw.Chip(d, L.C("sens", "NOW"), cx, cy - 4 * k, k, UiTheme.Text) + 5 * k;
            if (best) CoachDraw.Chip(d, L.C("sens", "BEST"), cx, cy - 4 * k, k, UiTheme.Good, filled: true);

            if (b.Unknown)
            {
                Gfx.TextC(d, UiTheme.Display, b.AllRuns.ToString(Inv), xRuns, Gfx.Mid(cy, num), num, UiTheme.Faint);
                Gfx.TextC(d, UiTheme.Display, b.AllDrills.ToString(Inv), xDrills, Gfx.Mid(cy, num), num, UiTheme.Faint);
                Gfx.Text(d, UiTheme.Body, L.T("not compared: these runs don't have it saved"), bar0, Gfx.Mid(cy, sm), sm, UiTheme.Faint);
                continue;
            }
            var rc = new Color(b.Runs >= SensHistory.MinRuns ? UiTheme.Text : UiTheme.Warn, a);
            var dc = new Color(b.Drills >= SensHistory.MinDrills ? UiTheme.Text : UiTheme.Warn, a);
            Gfx.TextC(d, UiTheme.Display, b.Runs.ToString(Inv), xRuns, Gfx.Mid(cy, num), num, rc);
            Gfx.TextC(d, UiTheme.Display, b.Drills.ToString(Inv), xDrills, Gfx.Mid(cy, num), num, dc);

            if (b.Runs == 0)
            {
                Gfx.Text(d, UiTheme.Body, h.Grouping == SensHistory.By.Sens ? L.T("no runs in drills you also played at another sens")
                    : L.T("no runs in drills you also played with another setup"), bar0, Gfx.Mid(cy, sm), sm, UiTheme.Faint);
                continue;
            }
            // score vs your average: a bar from the centre line
            d.DrawRect(new Rect2(bar0, cy - 1 * k, bar1 - bar0, 2 * k), new Color(UiTheme.Text, 0.08f));
            d.DrawRect(new Rect2(mid - 1 * k, cy - 9 * k, 2 * k, 18 * k), new Color(UiTheme.Text, 0.3f));
            float len = Mathf.Clamp(b.Rel / scale, -1, 1) * half;
            var bc = b.Rel >= 0 ? UiTheme.Good : UiTheme.Accent;
            if (!b.Enough) bc = UiTheme.Faint;
            d.DrawRect(new Rect2(Mathf.Min(mid, mid + len), cy - 5 * k, Mathf.Abs(len), 10 * k), new Color(bc, b.Enough ? 0.85f : 0.6f));
            Gfx.TextRaw(d, UiTheme.Display, Pct(b.Rel), xPct, Gfx.Mid(cy, num), num, b.Enough ? (b.Rel >= 0 ? UiTheme.Good : UiTheme.Text) : new Color(UiTheme.Dim, a));
            if (!b.Enough)
            {
                int need = Math.Max(b.NeedRuns, b.NeedDrills);
                string why = b.OwnShortfall ? L.P(need, $"{need} more run needed", $"{need} more runs needed") : L.T("needs more drills in common");
                Gfx.Text(d, UiTheme.Body, why, bar0, Gfx.Mid(cy + 15 * k, UiTheme.Fs(10.5f, k)), UiTheme.Fs(10.5f, k), b.OwnShortfall ? UiTheme.Warn : UiTheme.Dim);
            }

            string acc = float.IsNaN(b.AccPts) ? "—" : L.F($"{b.AccPts:+0.0;-0.0;0.0} pts");
            Gfx.TextC(d, UiTheme.Body, acc, xAcc, Gfx.Mid(cy, UiTheme.Fs(14, k)), UiTheme.Fs(14, k), MetricColor(b.AccPts, b.Enough, 0.5f));
            string kill = float.IsNaN(b.KillRel) ? "—" : Pct(b.KillRel);
            Gfx.TextC(d, UiTheme.Body, kill, xKill, Gfx.Mid(cy, UiTheme.Fs(14, k)), UiTheme.Fs(14, k), MetricColor(-b.KillRel, b.Enough, 0.01f));
        }
    }

    static string Pct(float rel) => string.Create(Inv, $"{rel * 100:+0;-0;0}%");

    /// <summary>Green when better than average by at least <paramref name="min"/>, dim otherwise (no red: one bad metric isn't a verdict).</summary>
    static Color MetricColor(float better, bool enough, float min) =>
        float.IsNaN(better) ? UiTheme.Faint : !enough ? new Color(UiTheme.Dim, 0.6f) : better >= min ? UiTheme.Good : UiTheme.Dim;
}
