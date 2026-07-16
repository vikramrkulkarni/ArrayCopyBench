using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArrayCopyBench
{
    /// <summary>
    /// Standalone benchmark measuring Array.Copy for copying the leading elements of one
    /// object[] into another. Reports ns per copy.
    ///
    /// Usage:
    ///   dotnet run -c Release -- [--length N] [--iterations M] [--runs R] [--offset O]
    ///
    ///   --length      Number of elements copied per operation (the "_startColumnIndex").
    ///                 This is the variable that matters most. Default: 4
    ///   --iterations  How many copies per timed run (the tight loop). Default: 50000000
    ///   --runs        How many timed runs (best + median reported). Default: 7
    ///   --offset      Destination start offset, so dst is larger than the copy region
    ///                 (matches RowData being wider than the key). Default: 0
    ///
    /// Example:
    ///   dotnet run -c Release -- --length 4 --iterations 50000000 --runs 7
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int length = 4;
            long iterations = 50_000_000;
            int runs = 7;
            int offset = 0;

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--length":
                        case "-l":
                            length = int.Parse(args[++i]);
                            break;
                        case "--iterations":
                        case "-n":
                            iterations = long.Parse(args[++i]);
                            break;
                        case "--runs":
                        case "-r":
                            runs = int.Parse(args[++i]);
                            break;
                        case "--offset":
                        case "-o":
                            offset = int.Parse(args[++i]);
                            break;
                        case "--help":
                        case "-h":
                            PrintUsage();
                            return 0;
                        default:
                            Console.Error.WriteLine($"Unknown argument: {args[i]}");
                            PrintUsage();
                            return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to parse arguments: {ex.Message}");
                PrintUsage();
                return 1;
            }

            if (length <= 0 || iterations <= 0 || runs <= 0 || offset < 0)
            {
                Console.Error.WriteLine("length, iterations and runs must be > 0; offset must be >= 0.");
                return 1;
            }

            // Source: an object[] whose leading `length` slots hold non-null references
            // (boxed values), so element stores exercise real GC write barriers.
            var src = new object[length];
            for (int i = 0; i < length; i++)
            {
                src[i] = i; // boxed int -> heap reference
            }

            // Destination is wider than the copy region, like RowData vs the key prefix.
            var dst = new object[offset + length];

            Console.WriteLine("=== Array.Copy (object[]) ===");
            Console.WriteLine($"Runtime            : .NET {Environment.Version}");
            Console.WriteLine($"OS                 : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            Console.WriteLine($"Arch               : {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"GC mode            : {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");
            Console.WriteLine($"length (copy count): {length}");
            Console.WriteLine($"iterations / run   : {iterations:N0}");
            Console.WriteLine($"runs               : {runs}");
            Console.WriteLine($"dest offset        : {offset}");
            Console.WriteLine();

            // Warm up so tiered JIT promotes the method before timing.
            Warmup(src, dst, offset, length);

            var arrayCopyMs = new double[runs];
            long sinkTotal = 0;

            for (int r = 0; r < runs; r++)
            {
                arrayCopyMs[r] = TimeArrayCopy(src, dst, offset, length, iterations, out long s1);
                sinkTotal += s1;
                Console.WriteLine($"run {r + 1,2}:  Array.Copy = {arrayCopyMs[r],9:F2} ms");
            }

            double acBest = Min(arrayCopyMs);
            double acMed = Median(arrayCopyMs);
            double nsPerCopyAc = acBest * 1_000_000.0 / iterations;

            Console.WriteLine();
            Console.WriteLine("--- results (lower is better) ---");
            Console.WriteLine($"Array.Copy   best = {acBest,9:F2} ms   median = {acMed,9:F2} ms   ({nsPerCopyAc:F3} ns/copy)");

            // Consume the sink so the JIT cannot elide the copies as dead code.
            Console.WriteLine();
            Console.WriteLine($"(checksum sink: {sinkTotal})");
            return 0;
        }

        private static void Warmup(object[] src, object[] dst, int offset, int length)
        {
            for (int i = 0; i < 200_000; i++)
            {
                Array.Copy(src, 0, dst, offset, length);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double TimeArrayCopy(object[] src, object[] dst, int offset, int length, long iterations, out long sink)
        {
            var sw = Stopwatch.StartNew();
            for (long it = 0; it < iterations; it++)
            {
                Array.Copy(src, dst, length);
            }
            sw.Stop();
            sink = dst[offset + length - 1] is int v ? v : 0;
            return sw.Elapsed.TotalMilliseconds;
        }

        private static double Min(double[] xs)
        {
            double m = xs[0];
            for (int i = 1; i < xs.Length; i++)
            {
                if (xs[i] < m) m = xs[i];
            }
            return m;
        }

        private static double Median(double[] xs)
        {
            var copy = (double[])xs.Clone();
            Array.Sort(copy);
            int n = copy.Length;
            return n % 2 == 1 ? copy[n / 2] : (copy[n / 2 - 1] + copy[n / 2]) / 2.0;
        }

        private static void PrintUsage()
        {
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet run -c Release -- [--length N] [--iterations M] [--runs R] [--offset O]");
            Console.WriteLine("  --length     / -l  Elements copied per operation (default 4)");
            Console.WriteLine("  --iterations / -n  Copies per timed run          (default 50000000)");
            Console.WriteLine("  --runs       / -r  Timed runs                    (default 7)");
            Console.WriteLine("  --offset     / -o  Destination start offset       (default 0)");
        }
    }
}
