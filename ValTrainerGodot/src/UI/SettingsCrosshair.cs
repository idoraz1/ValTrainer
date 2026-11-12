using Godot;
using ValTrainer.Audio;
using ValTrainer.Core;
using ValTrainer.Valorant;

namespace ValTrainer.UI;

/// <summary>
/// Settings → GENERAL → CROSSHAIR, the switcher's parts (<see cref="CrosshairList"/>): where the crosshairs come from,
/// SAVE CURRENT, and the SAVED CROSSHAIRS list (click a name to rename it; USE, COPY, move up / down, DELETE with a second
/// click). Everything here is ValTrainer's settings.json only; VALORANT's files are read, never written.
/// </summary>
public partial class SettingsScreen
{
    /// <summary>The note under the Crosshair selector: where the crosshair comes from (e.DisplayTag: e.Tag stays
    /// English in CrosshairList, a self-test reads it).</summary>
    static string CrosshairNote(CrosshairEntry e) => e.Source switch
    {
        CrosshairSource.Valorant => e.DisplayTag + (St.CrosshairChoice == null && e.ActiveInValorant && CrosshairList.All.Count > 1 ? " · " + L.T("follows VALORANT") : ""),
        CrosshairSource.Custom => L.T(CrosshairCode.IsFinderResult(St.FinderCrosshairCode) ? "Crosshair Finder result" : "Custom code") + " · " + L.T("ValTrainer only"),
        _ => e.DisplayTag + " · " + L.T("ValTrainer only"),
    };

    /// <summary>"4 profiles from VALORANT (saved on this PC 2026-11-05) · 2 saved codes · switch in drills with , / ."
    /// (refreshed live).</summary>
    Control CrosshairInfoRow()
    {
        float k = K;
        var lbl = Lbl("", UiTheme.Body, 13, UiTheme.Dim);
        lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lbl.MouseFilter = MouseFilterEnum.Pass;
        lbl.TooltipText = L.T("VALORANT's profiles are read from this PC (VALORANT writes them when it runs here). Switching changes ValTrainer only, never VALORANT. The keys work in every drill (Settings → KEYBINDS).");
        void Refresh()
        {
            var vp = App.Valorant;
            var list = CrosshairList.All;
            int nv = list.Count(e => e.Source == CrosshairSource.Valorant && e.Index >= 0);
            int ns = list.Count(e => e.Source == CrosshairSource.Saved);
            var acc = App.Accounts.FirstOrDefault(a => a.Id == vp.AccountId);
            string from = !vp.Found ? L.T("VALORANT not found: its default crosshair")
                : nv > 0 && acc != null ? L.P(nv, $"{nv} profile from VALORANT (saved on this PC {acc.LastModified:yyyy-MM-dd})", $"{nv} profiles from VALORANT (saved on this PC {acc.LastModified:yyyy-MM-dd})")
                : nv > 0 ? L.P(nv, $"{nv} profile from VALORANT", $"{nv} profiles from VALORANT")
                : L.T("VALORANT keeps this account's profiles online: 1 crosshair on this PC");
            var b = vp.Binds;
            string keys = b.IsBound(GameAction.CrosshairPrev) || b.IsBound(GameAction.CrosshairNext)
                ? " · " + L.F($"switch in drills with {b.Short(GameAction.CrosshairPrev)} / {b.Short(GameAction.CrosshairNext)}") : "";
            string t = from + " · " + L.P(ns, $"{ns} saved code", $"{ns} saved codes") + keys; // a Godot Label: translated here
            if (lbl.Text != t) lbl.Text = t;
        }
        Refresh();
        refreshers.Add(Refresh);
        return Margins(lbl, 12 * k, 0, 4 * k, 2 * k);
    }

    /// <summary>SAVE CURRENT: a copy of the crosshair in use goes to the saved list (keeps a VALORANT profile across
    /// accounts, or after VALORANT moves the account's profiles online).</summary>
    Control SaveCurrentRow()
    {
        float k = K;
        var row = HBox(12 * k);
        row.CustomMinimumSize = new Vector2(0, 50 * k);
        var txt = new DrawBox
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            OnDraw = d =>
            {
                float cy = d.Size.Y / 2;
                int ls = UiTheme.Fs(14, k);
                Gfx.TextFit(d, UiTheme.Body, "Keep the crosshair in use", 10 * k, Gfx.Mid(cy - 9 * k, ls), ls, UiTheme.Dim, d.Size.X - 10 * k);
                Gfx.TextFit(d, UiTheme.Body, "A copy in your list stays when you switch accounts", 10 * k, Gfx.Mid(cy + 10 * k, ls), ls, UiTheme.Text, d.Size.X - 10 * k);
            },
        };
        row.AddChild(txt);
        VButton? save = null;
        save = Btn("SAVE CURRENT", VButton.Look.Secondary, () =>
        {
            CrosshairList.SaveCurrent(out var msg);
            save!.Label = msg;
            save.QueueRedraw();
            GetTree().CreateTimer(2.5).Timeout += () => { if (IsInstanceValid(save)) { save.Label = "SAVE CURRENT"; save.QueueRedraw(); } };
            Changed();
        }, 150, 36, 16);
        save.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        save.TooltipText = L.T("Saves the crosshair ValTrainer draws now to SAVED CROSSHAIRS (named after it). VALORANT isn't changed.");
        refreshers.Add(() => save.Disabled = CrosshairList.CurrentEntry.Source == CrosshairSource.Saved);
        row.AddChild(save);
        return row;
    }

    /// <summary>The saved codes, one row each, in cycle order. Rebuilt when the list or the pick changes.</summary>
    Control SavedCrosshairsList()
    {
        float k = K;
        var v = VBox(4 * k);
        v.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var head = new SectionLabel { Text = "SAVED CROSSHAIRS", K = k };
        v.AddChild(head);
        var rows = VBox(4 * k);
        rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        v.AddChild(rows);
        string built = "";
        void Refresh()
        {
            var sv = St.SavedCrosshairs;
            int n = sv?.Count ?? 0;
            head.Note = n >= CrosshairList.MaxSaved ? L.F($"{n}/{CrosshairList.MaxSaved} · max {CrosshairList.MaxSaved}") : $"{n}/{CrosshairList.MaxSaved}";
            head.QueueRedraw();
            var cur = CrosshairList.CurrentEntry;
            string key = $"{CrosshairList.Revision}|{n}|{cur.Source}|{cur.Index}|{K}";
            if (key == built && rows.GetChildCount() > 0) return;
            built = key;
            foreach (var c in rows.GetChildren()) { rows.RemoveChild(c); c.QueueFree(); }
            if (n == 0)
            {
                var none = Lbl("None yet: PASTE a code or SAVE CURRENT. Saved codes come after VALORANT's profiles when you switch.", UiTheme.Body, 13, UiTheme.Faint);
                none.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                none.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                rows.AddChild(Margins(none, 12 * k, 0, 4 * k, 4 * k));
                return;
            }
            for (int i = 0; i < n; i++) rows.AddChild(SavedRow(i, n, cur.Source == CrosshairSource.Saved && cur.Index == i));
        }
        Refresh();
        refreshers.Add(Refresh);
        return v;
    }

    Control SavedRow(int i, int count, bool inUse)
    {
        float k = K;
        var sc = St.SavedCrosshairs![i];
        var row = HBox(6 * k);
        row.CustomMinimumSize = new Vector2(0, 40 * k);
        var mark = new ColorRect { Color = inUse ? UiTheme.Accent : new Color(UiTheme.Text, 0.12f), CustomMinimumSize = new Vector2(3 * k, 0), MouseFilter = MouseFilterEnum.Ignore };
        row.AddChild(mark);
        // the name: click and type to rename (Enter or clicking away keeps it, Esc puts it back)
        var name = new LineEdit
        {
            Text = sc.Name, MaxLength = CrosshairList.MaxName, SelectAllOnFocus = true, CustomMinimumSize = new Vector2(0, 34 * k),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter,
            TooltipText = L.F($"Click to rename · {sc.Code}"),
        };
        StyleEdit(name, k, 15);
        if (inUse) name.AddThemeColorOverride("font_color", UiTheme.Accent);
        int idx = i;
        bool cancel = false;
        name.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                cancel = true;
                name.Text = sc.Name;
                name.ReleaseFocus();
                name.AcceptEvent();
            }
        };
        name.TextSubmitted += _ => name.ReleaseFocus();
        name.FocusExited += () =>
        {
            if (cancel) { cancel = false; return; }
            // the list may have changed meanwhile (another row's DELETE / move while this box still had focus)
            if (St.SavedCrosshairs is not { } l || idx >= l.Count || !ReferenceEquals(l[idx], sc)) return;
            if (CrosshairList.CleanName(name.Text) is not { } nm) { name.Text = sc.Name; return; }
            if (nm == sc.Name) return;
            CrosshairList.Rename(idx, nm);
            Callable.From(RefreshAll).CallDeferred(); // rebuilds the rows: not from inside this one's signal
        };
        row.AddChild(name);

        var use = Btn(inUse ? "IN USE" : "USE", VButton.Look.Secondary, () =>
        {
            if (CrosshairList.All.FirstOrDefault(e => e.Source == CrosshairSource.Saved && e.Index == idx) is { } en)
            {
                CrosshairList.Select(en.Position);
                Sfx.I?.Play("kill1", 0.35f);
                Callable.From(Changed).CallDeferred();
            }
        }, 66, 34, 14);
        use.Disabled = inUse;
        use.TooltipText = L.T("Use this crosshair in ValTrainer");
        VButton? copy = null;
        copy = Btn("COPY", VButton.Look.Secondary, () =>
        {
            DisplayServer.ClipboardSet(sc.Code);
            copy!.Label = "COPIED";
            copy.QueueRedraw();
            GetTree().CreateTimer(2.0).Timeout += () => { if (IsInstanceValid(copy)) { copy.Label = "COPY"; copy.QueueRedraw(); } };
        }, 70, 34, 14);
        copy.TooltipText = L.T("Copy the code, then paste it in VALORANT: Settings → Crosshair → Import Profile Code");
        var up = new ListArrow { Up = true, K = k, CustomMinimumSize = new Vector2(30 * k, 34 * k), Disabled = idx == 0, TooltipText = L.T("Earlier in the cycle") };
        up.Pressed += () => { CrosshairList.Move(idx, -1); Callable.From(RefreshAll).CallDeferred(); };
        var down = new ListArrow { Up = false, K = k, CustomMinimumSize = new Vector2(30 * k, 34 * k), Disabled = idx == count - 1, TooltipText = L.T("Later in the cycle") };
        down.Pressed += () => { CrosshairList.Move(idx, 1); Callable.From(RefreshAll).CallDeferred(); };
        VButton? del = null;
        ulong armedAt = 0;
        del = Btn("DELETE", VButton.Look.Secondary, () =>
        {
            ulong now = Time.GetTicksMsec();
            if (armedAt == 0 || now - armedAt > 4000)
            {
                armedAt = now;
                del!.Label = "SURE?";
                del.QueueRedraw();
                GetTree().CreateTimer(4.0).Timeout += () =>
                {
                    if (!IsInstanceValid(del) || armedAt == 0 || Time.GetTicksMsec() - armedAt < 3900) return;
                    armedAt = 0; del.Label = "DELETE"; del.QueueRedraw();
                };
                return;
            }
            armedAt = 0;
            CrosshairList.Remove(idx);
            Callable.From(Changed).CallDeferred();
        }, 78, 34, 14);
        del.TooltipText = L.T("Delete from ValTrainer (click twice)");
        foreach (var b in new BaseButton[] { use, copy, up, down, del })
        {
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            row.AddChild(b);
        }
        return row;
    }
}

/// <summary>A small ▲ / ▼ button (saved crosshairs: move up / down), drawn like a secondary <see cref="VButton"/>.</summary>
public partial class ListArrow : BaseButton
{
    public bool Up;
    public float K = 1f;
    float hover;

    public ListArrow()
    {
        FocusMode = FocusModeEnum.None;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready() => Pressed += UiTheme.ClickSound;

    public override void _Process(double delta)
    {
        float t = IsHovered() && !Disabled ? 1f : 0f;
        if (hover != t) { hover = Mathf.MoveToward(hover, t, (float)delta * 8f); QueueRedraw(); }
    }

    public override void _Draw()
    {
        float a = Disabled ? 0.3f : 1f;
        var r = new Rect2(Vector2.Zero, Size);
        var fill = new Color(0.06f, 0.1f, 0.14f, 0.78f).Lerp(new Color(0.93f, 0.91f, 0.88f, 0.16f), hover);
        DrawRect(r, new Color(fill, fill.A * a));
        DrawRect(r, new Color(UiTheme.Text, (0.2f + 0.5f * hover) * a), false, 1f);
        var c = r.GetCenter();
        float w = 10 * K, h = 5 * K * (Up ? -1 : 1);
        DrawPolyline(new[] { new Vector2(c.X - w / 2, c.Y - h / 2), new Vector2(c.X, c.Y + h / 2), new Vector2(c.X + w / 2, c.Y - h / 2) },
            new Color(UiTheme.Text, a), Mathf.Max(1.5f, 2f * K), true);
    }
}
