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

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            for (int page = 0; page < 4; page++)
            {
                form.ShowPageForTesting(page);
                Application.DoEvents();
            }

            var now = Sample();
            long delta = (long)now.user - previousUser;
            previousUser = now.user;

            // Pages build their controls the first time they're shown. That one-off
            // cost is not a leak, so steady state is measured from cycle 2 onward.
            if (cycle == 1) afterWarmup = now.user;

            if (cycle % 10 == 0 || cycle <= 3)
                Console.WriteLine($"cycle {cycle,4}         USER {now.user,6}   GDI {now.gdi,6}" +
                                  $"   ({(delta >= 0 ? "+" : "")}{delta} since last)");
        }

        var after = Sample();
        Console.WriteLine();
        Console.WriteLine(new string('-', 70));

        long warmup = (long)afterWarmup - afterCreate.user;
        long steady = (long)after.user - afterWarmup;
        double perCycle = cycles > 1 ? (double)steady / (cycles - 1) : 0;

        Console.WriteLine($"{cycles} cycles ({cycles * 4} page switches)");
        Console.WriteLine($"first-show cost   +{warmup} USER (one-off, pages build lazily)");
        Console.WriteLine($"steady state      {afterWarmup} -> {after.user}   " +
                          $"net {(steady >= 0 ? "+" : "")}{steady} over {cycles - 1} cycles " +
                          $"({perCycle:0.00} per cycle)");

        // Anything that grows per cycle compounds into the 10,000 cap during a session.
        bool leaking = perCycle > 0.5;
        Console.WriteLine(leaking
            ? $"\nFAIL  leaking about {perCycle:0.0} USER handles per cycle"
            : "\nPASS  handle count is stable across page switches");

        form.Close();
        form.Dispose();
        return leaking ? 1 : 0;
    }
}
