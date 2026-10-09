using System.Runtime.InteropServices;
using Godot;
using Mutex = System.Threading.Mutex;

namespace ValTrainer.Core;

/// <summary>
/// One ValTrainer per Windows user: the first copy holds a named mutex for as long as it runs. A second launch
/// (double-clicking the shortcut again, a pinned taskbar icon …) asks the running copy to come to the front through a
/// named event and quits before it loads anything. Dev and automated runs (--dev) neither claim nor check it, so tests
/// can run next to a normal copy (dev: <c>--single-instance</c> turns it on to test it). Any error here lets the app
/// start normally: this must never stop ValTrainer opening.
/// <para>The updater is unaffected: Setup waits for the old copy to exit before installing, and the relaunched copy
/// starts after that (the portable swap's helper also waits for the exit).</para>
/// </summary>
public static class SingleInstance
{
    // Same GUID as the installer's AppId (installer/ValTrainer.iss). "Local\" = this Windows session only.
    const string MutexName = @"Local\ValTrainer-8BC1A72E-9D6C-4649-AE20-032F14D7A1F5";
    const string ShowName = MutexName + "-show";

    static Mutex? mutex;
    static EventWaitHandle? show;
    static volatile bool showRequested;
    static DisplayServer.WindowMode lastMode = DisplayServer.WindowMode.Windowed;

    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
    const int AsfwAny = -1;

    /// <summary>True when this is the only ValTrainer (it now holds the mutex). False when another copy is already
    /// running: it was asked to come to the front, and this copy should quit.</summary>
    public static bool Claim()
    {
        try
        {
            mutex = new Mutex(true, MutexName, out bool created);
            if (!created)
            {
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; } // the previous copy crashed: the mutex is ours now
                if (!owned)
                {
                    mutex.Dispose();
                    mutex = null;
                    BringOtherToFront();
                    return false;
                }
            }
            show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
            new Thread(() =>
            {
                while (true)
                {
                    try { show.WaitOne(); } catch { return; }
                    showRequested = true;
                }
            }) { IsBackground = true, Name = "ValTrainer single instance" }.Start();
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Single-instance check failed ({Log.Describe(e)}); starting anyway");
            return true;
        }
    }

    /// <summary>Second copy: let the running one take the foreground (Windows only allows that when the foreground
    /// process permits it) and signal it.</summary>
    static void BringOtherToFront()
    {
        try { AllowSetForegroundWindow(AsfwAny); } catch { /* not Windows: nothing to allow */ }
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowName);
            ev.Set();
        }
        catch { /* the other copy is still starting or just quitting: nothing to bring forward */ }
    }

    /// <summary>Main thread, every frame (cheap): restores and focuses the window when a second launch asked for it.</summary>
    public static void Poll()
    {
        if (show == null) return; // not holding the lock (dev runs, or the check failed)
        var mode = DisplayServer.WindowGetMode();
        if (mode != DisplayServer.WindowMode.Minimized) lastMode = mode;
        if (!showRequested) return;
        showRequested = false;
        if (mode == DisplayServer.WindowMode.Minimized) DisplayServer.WindowSetMode(lastMode);
        DisplayServer.WindowMoveToForeground();
        Log.Info("Another launch of ValTrainer: brought this window to the front");
    }
}
