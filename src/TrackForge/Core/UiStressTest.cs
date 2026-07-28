using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TrackForge.Core;

/// <summary>
/// Handle-leak detector. Builds the real main window and cycles through every page
/// repeatedly, sampling USER and GDI handle counts.
///
/// Exists because a burst of "Error creating window handle" (Win32 1158) crashes
/// showed up while switching tabs. A process is capped at 10,000 USER handles, so a
/// per-switch leak of even a few dozen becomes a crash during normal use.
/// </summary>
public static class UiStressTest
{
    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

    private const uint GdiObjects = 0;
    private const uint UserObjects = 1;

    private static (uint user, uint gdi) Sample()
    {
        var handle = Process.GetCurrentProcess().Handle;
        return (GetGuiResources(handle, UserObjects), GetGuiResources(handle, GdiObjects));
    }

    public static int Run(int cycles = 60)
    {
        Console.WriteLine("TrackForge UI stress test");
        Console.WriteLine(new string('-', 70));

        var before = Sample();
        Console.WriteLine($"baseline           USER {before.user,6}   GDI {before.gdi,6}");

        var form = new UI.MainForm();
        form.WindowState = FormWindowState.Minimized;
        form.ShowInTaskbar = false;
        form.Show();
        Application.DoEvents();

        var afterCreate = Sample();
        Console.WriteLine($"window built       USER {afterCreate.user,6}   GDI {afterCreate.gdi,6}" +
                          $"   (+{afterCreate.user - before.user} USER)");
        Console.WriteLine();

        uint previousUser = afterCreate.user;
        uint afterWarmup = 0;
        uint gdiAfterWarmup = 0;

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            for (int page = 0; page < 4; page++)
            {
                form.ShowPageForTesting(page);
                Application.DoEvents();
            }

            // Page switching alone was never the problem. Exercise what actually
            // gets used: filtering and re-rendering the library list, changing the
            // selection, and loading artwork into a picture box.
            form.StressLibraryForTesting();
            Application.DoEvents();

            var now = Sample();
            long delta = (long)now.user - previousUser;
            previousUser = now.user;

            // Pages build their controls the first time they're shown. That one-off
            // cost is not a leak, so steady state is measured from cycle 2 onward.
            if (cycle == 1) { afterWarmup = now.user; gdiAfterWarmup = now.gdi; }

            if (cycle % 10 == 0 || cycle <= 3)
                Console.WriteLine($"cycle {cycle,4}         USER {now.user,6}   GDI {now.gdi,6}" +
                                  $"   ({(delta >= 0 ? "+" : "")}{delta} since last)");
        }

        var after = Sample();
        Console.WriteLine();
        Console.WriteLine(new string('-', 70));

        long warmup = (long)afterWarmup - afterCreate.user;
        long steady = (long)after.user - afterWarmup;
        long gdiSteady = (long)after.gdi - gdiAfterWarmup;
        double perCycle = cycles > 1 ? (double)steady / (cycles - 1) : 0;
        double gdiPerCycle = cycles > 1 ? (double)gdiSteady / (cycles - 1) : 0;

        Console.WriteLine($"{cycles} cycles ({cycles * 4} page switches + library churn)");
        Console.WriteLine($"first-show cost   +{warmup} USER (one-off, pages build lazily)");
        Console.WriteLine($"USER steady       {afterWarmup} -> {after.user}   " +
                          $"net {(steady >= 0 ? "+" : "")}{steady} over {cycles - 1} cycles " +
                          $"({perCycle:0.00} per cycle)");
        Console.WriteLine($"GDI steady        {gdiAfterWarmup} -> {after.gdi}   " +
                          $"net {(gdiSteady >= 0 ? "+" : "")}{gdiSteady} " +
                          $"({gdiPerCycle:0.00} per cycle)");

        // Both caps are 10,000 per process. Anything that grows per cycle gets there
        // during a normal session, and the symptom is a hang, not an exception.
        bool userLeak = perCycle > 0.5;
        bool gdiLeak = gdiPerCycle > 0.5;

        if (userLeak) Console.WriteLine($"\nFAIL  leaking {perCycle:0.0} USER handles per cycle");
        if (gdiLeak) Console.WriteLine($"FAIL  leaking {gdiPerCycle:0.0} GDI objects per cycle");
        if (!userLeak && !gdiLeak) Console.WriteLine("\nPASS  USER and GDI handle counts are both stable");

        bool leaking = userLeak || gdiLeak;

        form.Close();
        form.Dispose();
        return leaking ? 1 : 0;
    }
}
