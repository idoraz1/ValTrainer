using System.IO.Compression;
using System.Text.Json;
using Godot;
using ValTrainer.Maps;

namespace ValTrainer.Game.Replay;

/// <summary>
/// The .vtr encoding (version 1). Layout:
/// <code>
/// int32 magic "VTT2" · int32 version · string headerJson (BinaryWriter length-prefixed UTF-8) · int32 compressed size
/// ZLibStream (exactly that many bytes):
///   float duration
///   player:   varuint n, then columns (each a zigzag-delta varint stream of quantised ints unless noted):
///             T (0.1 ms) · Eye x,y,z (mm) · VisualZoom-1 (1/1000) · AdsBlend, Reload, Blind (1/255) · bits (raw bytes) ·
///             Focus x,y,z (cm)
///   entities: varuint count; each: u16 id · u8 kind · u8 variant · f32 spawnT · f32 endT · varuint n · columns
///             Bot:    T · Feet x,y,z (cm) · Vel x,y,z (cm/s) · Facing (u16 turn, wrapped delta) · AimPitch (mrad) · bits
///             Target: T · Pos x,y,z (mm) · Radius (mm) · bits
///   events:   varuint count; each: f32 T · u8 kind · u16 ent · u8 parts (1 = P, 2 = N, 4 = Arg) · [P f32×3] [N f32×3] [Arg zigzag]
///   int32 end marker "END!"
/// </code>
/// Columns are delta-coded from the previous QUANTISED value, so the error never accumulates (≤ half a unit).
/// Load never throws (returns null) and touches no Godot nodes, so it runs on worker threads.
/// </summary>
public static partial class ReplayFile
{
    const int EndMarker = 0x21444E45; // "END!"
    const int MaxCount = 20_000_000;   // sanity limit against corrupt counts
    const float Tick = 1e-4f;          // time unit: 0.1 ms

    static readonly JsonSerializerOptions JsonOpts = new() { IncludeFields = true };

    // ------------------------------------------------------------------ support table

    static readonly HashSet<string> Full = new(StringComparer.OrdinalIgnoreCase)
    {
        "gridshot", "flick", "spider", "tracking", "microshot", "popup", "strafebots", "peek", "operator", "spray_vandal",
        "spray_phantom", "spray_transfer", "longtaps", "switch", "counterstrafe", "peekduel", "jumppeek", "jigglepeek",
        "peek_mix", "siteclear", "deathmatch",
    };
    static readonly HashSet<string> None = new(StringComparer.OrdinalIgnoreCase)
    {
        "reaction", "sensfinder", "xhairfinder", "crosshairfinder",
    };

    static partial void SupportCore(string modeKey, ref ReplaySupport result)
    {
        var key = modeKey ?? "";
        int c = key.IndexOf(':');
        var b = c > 0 ? key[..c] : key;
        // Partial: flashmap, postplant, retake, anchor, sound, the agent drills (flashpeek / recon / smokeexec / mobility /
        // chamber) and any unknown 3D drill.
        result = Full.Contains(b) ? ReplaySupport.Full : None.Contains(b) ? ReplaySupport.None : ReplaySupport.Partial;
    }

    // ------------------------------------------------------------------ map hash

    /// <summary>FNV-1a over the boxes' exact float bits, in order. The replay compares it with the map it rebuilds.</summary>
    public static uint MapHashOf(IEnumerable<Box> boxes)
    {
        uint h = 2166136261;
        void Mix(float f)
        {
            uint v = (uint)BitConverter.SingleToInt32Bits(f);
            for (int k = 0; k < 4; k++) { h ^= (v >> (8 * k)) & 0xff; h *= 16777619; }
        }
        foreach (var b in boxes) { Mix(b.Min.X); Mix(b.Min.Y); Mix(b.Min.Z); Mix(b.Max.X); Mix(b.Max.Y); Mix(b.Max.Z); }
        return h;
    }

    /// <summary>Hash of a map's solid boxes (<see cref="MapSpot.Boxes"/> bounds), as recorded in <see cref="ReplayHeader.MapHash"/>.</summary>
    public static uint MapHashOf(MapSpot map) => MapHashOf(map.Boxes.Select(b => b.Bounds));

    // ------------------------------------------------------------------ save

    static partial void SaveCore(ReplayRecording r, string path, ref bool done)
    {
        // Written to a temp file and moved into place, so a reader never sees half a sidecar.
        var tmp = path + ".tmp";
        try
        {
            using (var fs = File.Create(tmp)) Write(fs, r);
            File.Move(tmp, path, overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        done = true;
    }

    /// <summary>Encodes a recording to bytes in memory (tests / size reports).</summary>
    public static byte[] Encode(ReplayRecording r)
    {
        using var ms = new MemoryStream();
        Write(ms, r);
        return ms.ToArray();
    }

    static void Write(Stream fs, ReplayRecording r)
    {
        var body = new MemoryStream();
        using (var z = new ZLibStream(body, CompressionLevel.Optimal, leaveOpen: true))
        using (var w = new BinaryWriter(new BufferedStream(z, 1 << 16), System.Text.Encoding.UTF8)) // disposing it finishes the zlib stream
            WriteBody(w, r);
        using var bw = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: true);
        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(JsonSerializer.Serialize(r.Header, JsonOpts));
        bw.Write((int)body.Length); // the compressed size: a truncated file is rejected before inflating
        bw.Write(body.GetBuffer(), 0, (int)body.Length);
    }

    static void WriteBody(BinaryWriter w, ReplayRecording r)
    {
        w.Write(r.Duration);

        var p = r.Player;
        Var(w, (ulong)p.Count);
        Col(w, p, s => Q(s.T, 1f / Tick));
        Col(w, p, s => Q(s.Eye.X, 1000)); Col(w, p, s => Q(s.Eye.Y, 1000)); Col(w, p, s => Q(s.Eye.Z, 1000));
        Col(w, p, s => Q(s.VisualZoom - 1f, 1000));
        Col(w, p, s => Q(s.AdsBlend, 255)); Col(w, p, s => Q(s.Reload, 255)); Col(w, p, s => Q(s.Blind, 255));
        foreach (var s in p) w.Write((byte)s.Bits);
        Col(w, p, s => Q(s.Focus.X, 100)); Col(w, p, s => Q(s.Focus.Y, 100)); Col(w, p, s => Q(s.Focus.Z, 100));

        Var(w, (ulong)r.Entities.Count);
        foreach (var e in r.Entities)
        {
            w.Write(e.Id); w.Write((byte)e.Kind); w.Write(e.Variant); w.Write(e.SpawnT); w.Write(e.EndT);
            if (e.Kind == EntKind.Bot)
            {
                var l = e.Bot;
                Var(w, (ulong)l.Count);
                Col(w, l, s => Q(s.T, 1f / Tick));
                Col(w, l, s => Q(s.Feet.X, 100)); Col(w, l, s => Q(s.Feet.Y, 100)); Col(w, l, s => Q(s.Feet.Z, 100));
                Col(w, l, s => Q(s.Vel.X, 100)); Col(w, l, s => Q(s.Vel.Y, 100)); Col(w, l, s => Q(s.Vel.Z, 100));
                long prev = 0;
                foreach (var s in l)
                {
                    long q = FacingQ(s.Facing);
                    long d = (short)(ushort)(q - prev); // shortest way round
                    Var(w, Zig(d));
                    prev = q;
                }
                Col(w, l, s => Q(s.AimPitch, 1000));
                foreach (var s in l) w.Write((byte)s.Bits);
            }
            else
            {
                var l = e.Tgt;
                Var(w, (ulong)l.Count);
                Col(w, l, s => Q(s.T, 1f / Tick));
                Col(w, l, s => Q(s.Pos.X, 1000)); Col(w, l, s => Q(s.Pos.Y, 1000)); Col(w, l, s => Q(s.Pos.Z, 1000));
                Col(w, l, s => Q(s.Radius, 1000));
                foreach (var s in l) w.Write((byte)s.Bits);
            }
        }

        Var(w, (ulong)r.Events.Count);
        foreach (var ev in r.Events)
        {
            w.Write(ev.T); w.Write((byte)ev.Kind); w.Write(ev.Ent);
            byte parts = (byte)((ev.P != Vector3.Zero ? 1 : 0) | (ev.N != Vector3.Zero ? 2 : 0) | (ev.Arg != 0 ? 4 : 0));
            w.Write(parts);
            if ((parts & 1) != 0) { w.Write(ev.P.X); w.Write(ev.P.Y); w.Write(ev.P.Z); }
            if ((parts & 2) != 0) { w.Write(ev.N.X); w.Write(ev.N.Y); w.Write(ev.N.Z); }
            if ((parts & 4) != 0) Var(w, Zig(ev.Arg));
        }
        w.Write(EndMarker);
    }

    static long FacingQ(float rad)
    {
        if (!float.IsFinite(rad)) rad = 0;
        double turns = rad / (2 * Math.PI);
        turns -= Math.Floor(turns);
        return (long)Math.Round(turns * 65536) & 0xffff;
    }

    static long Q(float v, float scale)
    {
        if (!float.IsFinite(v)) v = 0;
        double d = Math.Clamp((double)v * scale, -4e12, 4e12);
        return (long)Math.Round(d);
    }

    static void Col<T>(BinaryWriter w, List<T> l, Func<T, long> q)
    {
        long prev = 0;
        foreach (var s in l) { long v = q(s); Var(w, Zig(v - prev)); prev = v; }
    }

    static ulong Zig(long v) => (ulong)((v << 1) ^ (v >> 63));
    static long UnZig(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);

    static void Var(BinaryWriter w, ulong v)
    {
        while (v >= 0x80) { w.Write((byte)(v | 0x80)); v >>= 7; }
        w.Write((byte)v);
    }

    // ------------------------------------------------------------------ load

    static partial void LoadCore(string path, ref ReplayRecording? result)
    {
        result = null;
        if (!File.Exists(path)) return;
        using var fs = File.OpenRead(path);
        result = Read(fs);
    }

    /// <summary>Decodes a .vtr stream; null for anything malformed. Never throws.</summary>
    public static ReplayRecording? Read(Stream s)
    {
        try
        {
            using var br = new BinaryReader(s, System.Text.Encoding.UTF8, leaveOpen: true);
            if (br.ReadInt32() != Magic) return null;
            if (br.ReadInt32() != Version) return null;
            var header = JsonSerializer.Deserialize<ReplayHeader>(br.ReadString(), JsonOpts);
            if (header == null) return null;
            header.ExtraSolids ??= Array.Empty<float[]>();
            header.Mode ??= ""; header.MapKey ??= "range"; header.Weapon ??= ""; header.EnemyColor ??= "";
            header.TargetStyle ??= ""; header.AppVersion ??= "";
            int len = br.ReadInt32();
            if (len <= 0 || len > 256 << 20 || (s.CanSeek && len > s.Length - s.Position)) return null;
            var packed = br.ReadBytes(len);
            if (packed.Length != len) return null; // truncated
            using var z = new ZLibStream(new MemoryStream(packed), CompressionMode.Decompress);
            using var r = new BinaryReader(new BufferedStream(z, 1 << 16), System.Text.Encoding.UTF8);
            var rec = new ReplayRecording { Header = header };
            ReadBody(r, rec);
            return rec;
        }
        catch { return null; }
    }

    static void ReadBody(BinaryReader r, ReplayRecording rec)
    {
        rec.Duration = r.ReadSingle();

        int n = Count(r);
        var t = Cols(r, n);
        var ex = Cols(r, n); var ey = Cols(r, n); var ez = Cols(r, n);
        var zoom = Cols(r, n); var ads = Cols(r, n); var rel = Cols(r, n); var bl = Cols(r, n);
        var bits = r.ReadBytes(n);
        if (bits.Length != n) throw new EndOfStreamException();
        var fx = Cols(r, n); var fy = Cols(r, n); var fz = Cols(r, n);
        rec.Player.Capacity = n;
        for (int i = 0; i < n; i++)
            rec.Player.Add(new PlayerSample
            {
                T = t[i] * Tick,
                Eye = new Vector3(ex[i] / 1000f, ey[i] / 1000f, ez[i] / 1000f),
                VisualZoom = 1f + zoom[i] / 1000f,
                AdsBlend = ads[i] / 255f, Reload = rel[i] / 255f, Blind = bl[i] / 255f,
                Bits = (PBits)bits[i],
                Focus = new Vector3(fx[i] / 100f, fy[i] / 100f, fz[i] / 100f),
            });

        int ne = Count(r);
        for (int k = 0; k < ne; k++)
        {
            var e = new EntityTrack
            {
                Id = r.ReadUInt16(), Kind = (EntKind)r.ReadByte(), Variant = r.ReadByte(), SpawnT = r.ReadSingle(), EndT = r.ReadSingle(),
            };
            int m = Count(r);
            if (e.Kind == EntKind.Bot)
            {
                var bt = Cols(r, m);
                var px = Cols(r, m); var py = Cols(r, m); var pz = Cols(r, m);
                var vx = Cols(r, m); var vy = Cols(r, m); var vz = Cols(r, m);
                var face = new long[m];
                long prev = 0;
                for (int i = 0; i < m; i++) { prev = (prev + UnZig(ReadVar(r))) & 0xffff; face[i] = prev; }
                var pitch = Cols(r, m);
                var bb = r.ReadBytes(m);
                if (bb.Length != m) throw new EndOfStreamException();
                e.Bot.Capacity = m;
                for (int i = 0; i < m; i++)
                {
                    float f = (float)(face[i] / 65536.0 * 2 * Math.PI);
                    if (f > MathF.PI) f -= 2 * MathF.PI;
                    e.Bot.Add(new BotSample
                    {
                        T = bt[i] * Tick,
                        Feet = new Vector3(px[i] / 100f, py[i] / 100f, pz[i] / 100f),
                        Vel = new Vector3(vx[i] / 100f, vy[i] / 100f, vz[i] / 100f),
                        Facing = f, AimPitch = pitch[i] / 1000f, Bits = (BBits)bb[i],
                    });
                }
            }
            else if (e.Kind == EntKind.Target)
            {
                var tt = Cols(r, m);
                var px = Cols(r, m); var py = Cols(r, m); var pz = Cols(r, m);
                var rad = Cols(r, m);
                var tb = r.ReadBytes(m);
                if (tb.Length != m) throw new EndOfStreamException();
                e.Tgt.Capacity = m;
                for (int i = 0; i < m; i++)
                    e.Tgt.Add(new TargetSample
                    {
                        T = tt[i] * Tick, Pos = new Vector3(px[i] / 1000f, py[i] / 1000f, pz[i] / 1000f), Radius = rad[i] / 1000f, Bits = (TBits)tb[i],
                    });
            }
            else throw new InvalidDataException("unknown entity kind");
            rec.Entities.Add(e);
        }

        int nv = Count(r);
        for (int k = 0; k < nv; k++)
        {
            float et = r.ReadSingle();
            var kind = (RxKind)r.ReadByte();
            ushort ent = r.ReadUInt16();
            byte parts = r.ReadByte();
            var p = (parts & 1) != 0 ? new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) : Vector3.Zero;
            var nn = (parts & 2) != 0 ? new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) : Vector3.Zero;
            int arg = (parts & 4) != 0 ? (int)UnZig(ReadVar(r)) : 0;
            rec.Events.Add(new ReplayEvent(et, kind, ent, p, nn, arg));
        }
        if (r.ReadInt32() != EndMarker) throw new InvalidDataException("no end marker");
    }

    static int Count(BinaryReader r)
    {
        ulong n = ReadVar(r);
        if (n > MaxCount) throw new InvalidDataException("count");
        return (int)n;
    }

    static long[] Cols(BinaryReader r, int n)
    {
        var a = new long[n];
        long prev = 0;
        for (int i = 0; i < n; i++) { prev += UnZig(ReadVar(r)); a[i] = prev; }
        return a;
    }

    static ulong ReadVar(BinaryReader r)
    {
        ulong v = 0;
        for (int shift = 0; shift < 70; shift += 7)
        {
            byte b = r.ReadByte();
            v |= (ulong)(b & 0x7f) << shift;
            if (b < 0x80) return v;
        }
        throw new InvalidDataException("varint");
    }
}
