using System;
using System.Globalization;

namespace Citymaker.Tests
{
    /// <summary>
    /// Wall-clock budgets for the timing tests. The numbers in the tests are the design budgets on a
    /// quiet developer machine (ARCHITECTURE: &lt; 250 ms for 5 ha on one core); shared CI runners and
    /// parallel worktree builds are several times slower and made those asserts flaky.
    ///
    /// Scale (first that applies):
    ///   CITYMAKER_TIMING_SCALE=&lt;factor&gt;   explicit factor (e.g. 0 or "off" disables the budgets)
    ///   CI / CITYMAKER_PARALLEL set        ×4 (CI runners, several test runs at once)
    ///   another testhost process running    ×3 (parallel worktrees on one machine)
    ///   ≤ 2 logical processors              ×2
    ///   otherwise                           ×1
    /// Timing classes still run in TimingCollection (sequential, after the parallel tests).
    /// </summary>
    public static class TimingBudget
    {
        public static readonly double Scale = ReadScale();

        /// <summary>The budget in ms for a design budget of <paramref name="designMs"/>.</summary>
        public static long Ms(double designMs) =>
            Scale <= 0 ? long.MaxValue : (long)Math.Ceiling(designMs * Scale);

        private static double ReadScale()
        {
            var env = Environment.GetEnvironmentVariable("CITYMAKER_TIMING_SCALE");
            if (!string.IsNullOrWhiteSpace(env))
            {
                if (string.Equals(env.Trim(), "off", StringComparison.OrdinalIgnoreCase)) return 0;
                if (double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 0) return f;
            }
            if (Set("CI") || Set("CITYMAKER_PARALLEL") || Set("TF_BUILD") || Set("GITHUB_ACTIONS")) return 4;
            // Another test run on this machine (parallel worktrees / builders) competes for the cores.
            if (OtherTestRuns() > 0) return 3;
            if (Environment.ProcessorCount <= 2) return 2;
            return 1;
        }

        private static int OtherTestRuns()
        {
            try
            {
                int self = System.Diagnostics.Process.GetCurrentProcess().Id, n = 0;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName("testhost")) { if (p.Id != self) n++; p.Dispose(); }
                return n;
            }
            catch (Exception) { return 0; }
        }

        private static bool Set(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(v) && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) && v != "0";
        }
    }
}
