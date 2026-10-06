using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace DD2Estate.CoreTests
{
    /// <summary>
    /// Runs every public static void method of the *Tests classes. PASS / FAIL / SKIP per test; the exit code is
    /// the number of failures. Arguments: [DD1 install dir] [--only text] (the dir also comes from DD1_DIR).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string only = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--only" && i + 1 < args.Length) only = args[++i];
                else Dd1.Root = args[i];
            }
            Console.WriteLine("DD1 install: " + (Dd1.Available ? Dd1.Root : Dd1.Root + " (NOT FOUND: tests that read it are skipped)"));

            var tests = typeof(Program).Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Tests", StringComparison.Ordinal))
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
                    .OrderBy(m => m.MetadataToken))
                .Where(m => only == null || (m.DeclaringType.Name + "." + m.Name).IndexOf(only, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            int passed = 0, failed = 0, skipped = 0;
            foreach (var test in tests)
            {
                var name = test.DeclaringType.Name + "." + test.Name;
                var watch = Stopwatch.StartNew();
                try
                {
                    test.Invoke(null, null);
                    passed++;
                    Console.WriteLine("PASS  " + name + "  (" + watch.ElapsedMilliseconds + " ms)");
                }
                catch (TargetInvocationException e) when (e.InnerException is SkipException)
                {
                    skipped++;
                    Console.WriteLine("SKIP  " + name + "  " + e.InnerException.Message);
                }
                catch (TargetInvocationException e)
                {
                    failed++;
                    Console.WriteLine("FAIL  " + name);
                    Console.WriteLine("      " + e.InnerException.ToString().Replace("\n", "\n      "));
                }
            }
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed, " + skipped + " skipped");
            return failed;
        }
    }
}
