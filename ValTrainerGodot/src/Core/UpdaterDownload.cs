using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ValTrainer.Core;

public static partial class Updater
{
    /// <summary>
    /// One background download of a release file (own low-priority thread, never touches Godot): fetches the release's
    /// SHA256SUMS.txt, downloads the file to "&lt;name&gt;.part" — resuming a partial file with an HTTP Range request —
    /// then checks size and SHA-256 and renames it. A portable zip is then unpacked to "ValTrainer-&lt;version&gt;.exe".
    /// Network trouble (offline, timeouts, HTTP errors, connection drops) keeps the partial file for the next try; a
    /// file that doesn't match its published hash is deleted.
    /// </summary>
    sealed class Job
    {
        public enum Result { None, Ok, BadFile, Transient, Cancelled }

        public readonly string Version, Url, SumsUrl, Asset, Dir, AppVersion;
        public readonly long ExpectedSize;
        public readonly bool Portable;
        readonly long throttleBps;
        readonly CancellationTokenSource cts = new();

        public long Received, Total;
        public volatile int ProgressRev;
        public int SeenRev;
        public Result Outcome;
        public string? Error, ReadyFile, ReadySha256;
        volatile bool done;
        public bool Done => done;

        static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

        public Job(string version, string url, long size, string sumsUrl, string asset, bool portable, string dir, string appVersion, long throttleBps)
        {
            Version = version; Url = url; ExpectedSize = size; SumsUrl = sumsUrl; Asset = asset; Portable = portable; Dir = dir;
            AppVersion = appVersion; this.throttleBps = throttleBps;
        }

        public void Start() => new Thread(Run) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "ValTrainer update download" }.Start();

        public void Cancel() { try { cts.Cancel(); } catch { } }

        void Run()
        {
            try { Work(); }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { Outcome = Result.Cancelled; }
            catch (BadFileException e) { Outcome = Result.BadFile; Error = e.Message; }
            catch (OperationCanceledException) { Outcome = Result.Transient; Error = "no answer from the download server (offline?)"; }
            catch (HttpRequestException e) { Outcome = Result.Transient; Error = e.StatusCode is { } c ? $"HTTP {(int)c}" : $"can't reach the download server ({Log.Describe(e)})"; }
            catch (IOException e) { Outcome = Result.Transient; Error = Log.Describe(e); }
            catch (Exception e) { Log.Exception("Updater: download", e); Outcome = Result.Transient; Error = Log.Describe(e); }
            finally { done = true; }
        }

        HttpClient Client(TimeSpan timeout)
        {
            var h = new SocketsHttpHandler
            {
                AllowAutoRedirect = true, // github.com/…/releases/download/… → GitHub's download host (https only)
                MaxAutomaticRedirections = 5,
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AutomaticDecompression = DecompressionMethods.None,
            };
            var http = new HttpClient(h) { Timeout = timeout };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ValTrainer/{AppVersion} (updater)");
            return http;
        }

        void Work()
        {
            Directory.CreateDirectory(Dir);
            var ct = cts.Token;
            string expected = FetchHash(ct);
            string final = Path.Combine(Dir, Asset), part = final + ".part";

            // already downloaded by an earlier run (e.g. update.json was lost)?
            if (!(File.Exists(final) && Sha256(final, ct) == expected))
            {
                TryDelete(final);
                for (int round = 0; ; round++)
                {
                    try { if (Download(part, ct)) break; }
                    catch (BadFileException) { TryDelete(part); throw; }
                    if (round >= 1) throw new IOException("the download server didn't resume the partial download");
                }
                long len = new FileInfo(part).Length;
                if (ExpectedSize > 0 && len != ExpectedSize)
                {
                    TryDelete(part);
                    throw new BadFileException($"{Asset} has {len} bytes instead of {ExpectedSize}");
                }
                string got = Sha256(part, ct);
                if (got != expected)
                {
                    TryDelete(part);
                    throw new BadFileException($"{Asset} doesn't match SHA256SUMS.txt (got {got[..12]}…, expected {expected[..12]}…)");
                }
                File.Move(part, final, overwrite: true);
            }
            if (!Portable)
            {
                if (!LooksLikeExe(final)) { TryDelete(final); throw new BadFileException($"{Asset} isn't a Windows program"); }
                ReadyFile = Asset;
                ReadySha256 = expected;
                Outcome = Result.Ok;
                return;
            }
            // portable: take ValTrainer.exe out of the verified zip
            string exeName = $"ValTrainer-{Version}.exe", exe = Path.Combine(Dir, exeName), tmp = exe + ".tmp";
            try
            {
                using (var zip = ZipFile.OpenRead(final))
                {
                    var entry = zip.Entries
                        .Where(e => e.Name.Equals("ValTrainer.exe", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(e => e.FullName.Length).FirstOrDefault()
                        ?? throw new BadFileException($"{Asset} has no ValTrainer.exe");
                    entry.ExtractToFile(tmp, overwrite: true);
                }
                if (!LooksLikeExe(tmp)) throw new BadFileException($"ValTrainer.exe in {Asset} isn't a Windows program");
                ReadySha256 = Sha256(tmp, ct);
                File.Move(tmp, exe, overwrite: true);
            }
            catch (InvalidDataException e)
            {
                TryDelete(tmp); TryDelete(final);
                throw new BadFileException($"{Asset} is damaged ({Log.Describe(e)})");
            }
            catch { TryDelete(tmp); throw; }
            TryDelete(final);
            ReadyFile = exeName;
            Outcome = Result.Ok;
        }

        /// <summary>The asset's SHA-256 from the release's SHA256SUMS.txt ("&lt;hex&gt;  &lt;name&gt;" lines).</summary>
        string FetchHash(CancellationToken ct)
        {
            using var http = Client(TimeSpan.FromSeconds(20));
            using var resp = http.GetAsync(SumsUrl, HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            if (resp.Content.Headers.ContentLength > 1 << 20) throw new BadFileException("SHA256SUMS.txt is too big");
            string text = resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            foreach (var line in text.Split('\n'))
            {
                var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
                if (m.Success && m.Groups[2].Value.Trim().Equals(Asset, StringComparison.OrdinalIgnoreCase))
                    return m.Groups[1].Value.ToLowerInvariant();
            }
            throw new BadFileException($"SHA256SUMS.txt has no line for {Asset}");
        }

        /// <summary>Downloads (the rest of) the file into <paramref name="part"/>. False when the server ignored or refused
        /// the range of a partial file (the partial file is then dropped and the caller starts over once).</summary>
        bool Download(string part, CancellationToken ct)
        {
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (ExpectedSize > 0 && have > ExpectedSize) { TryDelete(part); have = 0; }
            if (ExpectedSize > 0 && have == ExpectedSize) return true; // complete: verify it
            using var http = Client(TimeSpan.FromSeconds(30)); // until the response headers; the body has its own read timeout
            using var req = new HttpRequestMessage(HttpMethod.Get, Url);
            req.Headers.Accept.ParseAdd("application/octet-stream");
            if (have > 0) req.Headers.Range = new RangeHeaderValue(have, null);
            using var resp = http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult();
            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { TryDelete(part); return false; }
            resp.EnsureSuccessStatusCode();
            bool resume = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent && resp.Content.Headers.ContentRange?.From == have;
            if (have > 0 && !resume)
            {
                if (resp.StatusCode == HttpStatusCode.PartialContent) { TryDelete(part); return false; } // a range we didn't ask for
                Log.Info($"Updater: the server sent {Asset} from the start (no resume)");
                have = 0;
            }
            else if (resume) Log.Info($"Updater: resuming {Asset} at {have / 1048576.0:0.0} MB");
            long len = resp.Content.Headers.ContentLength ?? -1;
            Interlocked.Exchange(ref Total, ExpectedSize > 0 ? ExpectedSize : len >= 0 ? have + len : 0);
            Interlocked.Exchange(ref Received, have);
            ProgressRev++;

            using var fs = new FileStream(part, resume ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            using var body = resp.Content.ReadAsStreamAsync(ct).GetAwaiter().GetResult();
            var buf = new byte[1 << 16];
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long session = 0, lastTick = 0;
            while (true)
            {
                int n;
                using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    read.CancelAfter(ReadTimeout);
                    n = body.ReadAsync(buf, read.Token).AsTask().GetAwaiter().GetResult();
                }
                if (n == 0) break;
                fs.Write(buf, 0, n);
                session += n;
                long got = Interlocked.Add(ref Received, n);
                if (ExpectedSize > 0 && got > ExpectedSize) throw new BadFileException($"{Asset} is bigger than the release says");
                if (sw.ElapsedMilliseconds - lastTick >= 150) { lastTick = sw.ElapsedMilliseconds; ProgressRev++; }
                if (throttleBps > 0)
                {
                    long wait = session * 1000 / throttleBps - sw.ElapsedMilliseconds; // dev: --update-throttle
                    if (wait > 0) Thread.Sleep((int)Math.Min(wait, 1000));
                }
            }
            fs.Flush(true);
            ProgressRev++;
            if (ExpectedSize > 0 && Interlocked.Read(ref Received) < ExpectedSize) throw new IOException("the connection closed before the download finished");
            return true;
        }
    }

    sealed class BadFileException : Exception
    {
        public BadFileException(string msg) : base(msg) { }
    }

    /// <summary>
    /// RESTART TO UPDATE, off the main thread: re-checks the downloaded file's SHA-256 (it sat on disk since it was
    /// verified) and, for a portable copy, copies the new exe next to the running one ("ValTrainer.exe.new", same drive,
    /// so the swap is two renames) and checks the copy too.
    /// </summary>
    sealed class ApplyJob
    {
        public readonly string File, Sha256Hex;
        readonly string? exePath;
        public string? StagedExe, Error;
        public bool Ok;
        volatile bool done;
        public bool Done => done;

        public ApplyJob(string file, string sha, string? exePath) { File = file; Sha256Hex = sha; this.exePath = exePath; }

        public void Start() => new Thread(Run) { IsBackground = true, Name = "ValTrainer update apply" }.Start();

        void Run()
        {
            try
            {
                if (!System.IO.File.Exists(File)) { Error = $"{Path.GetFileName(File)} is missing"; return; }
                if (Sha256(File, default) != Sha256Hex) { Error = $"{Path.GetFileName(File)} changed since it was downloaded (SHA-256 mismatch)"; return; }
                if (exePath != null)
                {
                    string tmp = exePath + ".new";
                    System.IO.File.Copy(File, tmp, overwrite: true);
                    if (Sha256(tmp, default) != Sha256Hex) { TryDelete(tmp); Error = "copying the new ValTrainer.exe failed (SHA-256 mismatch)"; return; }
                    StagedExe = tmp;
                }
                Ok = true;
            }
            catch (Exception e) { Log.Exception("Updater: checking the downloaded file", e); Error = Log.Describe(e); }
            finally { done = true; }
        }
    }

    static string Sha256(string path, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var sha = SHA256.Create();
        var buf = new byte[1 << 20];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.TransformBlock(buf, 0, n, null, 0);
        }
        sha.TransformFinalBlock(buf, 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    static bool LooksLikeExe(string path)
    {
        try
        {
            using var fs = System.IO.File.OpenRead(path);
            return fs.Length > 1024 && fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
        }
        catch { return false; }
    }
}
