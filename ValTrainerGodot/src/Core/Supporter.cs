using System.Security.Cryptography;

namespace ValTrainer.Core;

/// <summary>
/// Supporter codes: a thank-you for donating. A valid code unlocks every skin, level and variant, now and in future
/// versions (<see cref="ValTrainer.Progression.Progress.IsUnlocked"/>). The code is an ECDSA P-256 signature over the supporter's name,
/// made by the maintainer with tools/supporter/supporter_code.py (the private key never leaves the maintainer's PC) and
/// checked here OFFLINE with the public key below: no server, no account, no network request. Entered in Settings →
/// About → Supporter code and kept in <see cref="AppSettings.SupporterCode"/>.
/// <para>Layout (before base32): version 1 | name length | name (UTF-8, ≤ 32 bytes) | days since 2026-01-01 (2, little
/// endian) | 4 random bytes | signature r‖s (64 bytes). Shown as "VT-" + unpadded RFC 4648 base32 in groups of 5; spaces,
/// dashes and case are ignored.</para>
/// </summary>
public static class Supporter
{
    /// <summary>The maintainer's public key (SubjectPublicKeyInfo, base64): codes made by hand (supporter_code.py).</summary>
    const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcopNNVgB08TMwPb36fGKMzRxT7J2LTxNQAm/hpUD+VUYgTyBSDW10SAEgTs1tBvztczdTOLtDf8R4Hw158B3SA==";

    /// <summary>The donation webhook's key (tools/supporter/server: Ko-fi / Buy Me a Coffee → a code by email). Its own key
    /// so the maintainer's stays offline; a future version can drop it alone if it is ever exposed.</summary>
    const string ServerKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE/7Pt9/Icd3nuVSJAkCPo17IUfmMXM71zEi8shhAniZ6iVxAxhearxv0J3sQEFE1yzyS+FtO5DjPoSmbgd58Jow==";

    /// <summary>The automated tests' key: accepted only in --dev runs (its codes unlock nothing in a normal run).</summary>
    const string DevTestKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEJ95BWqLdara/67R83yUKJ6Z0YbeYo6G0+aaymVF6IPmhALH0k5WA/re+L/NfkiyxaaaxGI9TmpwJ/MyYYdpHJw==";

    /// <summary>A code made with <see cref="DevTestKey"/> for "Test Supporter" (the culture test uses it).</summary>
    internal const string DevTestCode = "VT-AEHFI-ZLTOQ-QFG5L-QOBXX-E5DFO-I5ACQ-XSJVL-JJAIO-5X6W3-526GK-56LFM-5NFYF-O2T2L-LGY6T-6YQPK-PA5V2-LGLFI-KBQ7I-364H6-XIO2N-BREGU-K7X2I-7IS4B-YQEF4-CZRHB-MN6ZL-7ALUC-P5U";

    public sealed record Info(string Name, DateOnly Issued);

    static string? checkedCode;
    static Info? current;

    /// <summary>The supporter behind the saved code, or null (no code, or not valid).</summary>
    public static Info? Current
    {
        get
        {
            string? code = Main.I?.Settings?.SupporterCode;
            if (code != checkedCode) { checkedCode = code; current = Check(code); }
            return current;
        }
    }

    /// <summary>GET MY CODE (Settings → About): the donation webhook's claim endpoint (tools/supporter/server). The only
    /// request this feature makes, and only when the player presses the button.</summary>
    public const string ClaimUrl = "https://ktnsibbyxctiktqmjnxk.supabase.co/functions/v1/supporter-webhook?claim";

    /// <summary>Asks the claim endpoint for the code of a donation made with <paramref name="email"/>. Returns the code
    /// (already checked with <see cref="Check"/>) or an error key: "not_found", "rate_limited", "bad_email", "network",
    /// "invalid" (the server answered something that isn't a valid code). Never throws.</summary>
    public static async Task<(string? Code, string? Error)> Claim(string email)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ValTrainer/{AppInfo.Version}");
            var body = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(new { email = email.Trim() }),
                System.Text.Encoding.UTF8, "application/json");
            using var resp = await http.PostAsync(ClaimUrl, body).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (null, "network");
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == System.Text.Json.JsonValueKind.True
                && root.TryGetProperty("code", out var c) && c.GetString() is { } code)
                return Check(code) != null ? (code, null) : (null, "invalid");
            return (null, root.TryGetProperty("error", out var e) ? e.GetString() ?? "not_found" : "not_found");
        }
        catch (Exception) { return (null, "network"); }
    }

    /// <summary>A valid supporter code is saved: every skin is unlocked.</summary>
    public static bool Active => Current != null;

    /// <summary>The supporter in <paramref name="code"/>, or null when it isn't a valid code.</summary>
    public static Info? Check(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || Decode(code) is not { } raw || raw.Length < 2 + 1 + 2 + 4 + 64 || raw[0] != 1) return null;
        int n = raw[1];
        int payloadLen = 2 + n + 2 + 4;
        if (n < 1 || n > 32 || raw.Length != payloadLen + 64) return null;
        var payload = raw.AsSpan(0, payloadLen);
        var sig = raw.AsSpan(payloadLen, 64);
        bool ok = Verify(PublicKey, payload, sig) || Verify(ServerKey, payload, sig) || (CmdLine.Dev && Verify(DevTestKey, payload, sig));
        if (!ok) return null;
        try
        {
            string name = new System.Text.UTF8Encoding(false, true).GetString(payload.Slice(2, n));
            int days = payload[2 + n] | payload[3 + n] << 8;
            return new Info(name, new DateOnly(2026, 1, 1).AddDays(days));
        }
        catch (System.Text.DecoderFallbackException) { return null; }
    }

    static bool Verify(string keyB64, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> sig)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(keyB64), out _);
            return ec.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException) { return false; }
    }

    /// <summary>"VT-ABCDE-…" → bytes (RFC 4648 base32, no padding); null if it has other characters.</summary>
    static byte[]? Decode(string code)
    {
        var s = new System.Text.StringBuilder();
        foreach (char ch in code.ToUpperInvariant())
            if (ch is not (' ' or '-' or '\t' or '\r' or '\n')) s.Append(ch);
        string t = s.ToString();
        if (t.StartsWith("VT")) t = t[2..];
        const string Abc = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var outp = new List<byte>(t.Length * 5 / 8);
        int buf = 0, bits = 0;
        foreach (char ch in t)
        {
            int v = Abc.IndexOf(ch);
            if (v < 0) return null;
            buf = buf << 5 | v; bits += 5;
            if (bits >= 8) { bits -= 8; outp.Add((byte)(buf >> bits & 0xFF)); }
        }
        return outp.ToArray();
    }
}
