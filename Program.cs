using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ArrayCopyBench
{
    /// <summary>
    /// Standalone benchmark comparing Array.Copy vs a manual for-loop for copying the
    /// leading elements of one object[] into another. Mirrors the hot-path copy in
    /// MergeTableWithBaseAndTransDeltaOperator.ConstructRow (the key-component copy).
    ///
    /// Usage:
    ///   dotnet run -c Release -- [--length N] [--iterations M] [--runs R] [--offset O]
    ///
    ///   --length      Number of elements copied per operation (the "_startColumnIndex").
    ///                 This is the variable that matters most. Default: 4
    ///   --iterations  How many copies per timed run (the tight loop). Default: 50000000
    ///   --runs        How many timed runs per method (best + median reported). Default: 7
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

            Console.WriteLine("=== Array.Copy vs manual loop vs Span<object>.CopyTo (object[]) ===");
            Console.WriteLine($"Runtime            : .NET {Environment.Version}");
            Console.WriteLine($"OS                 : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            Console.WriteLine($"Arch               : {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"GC mode            : {(System.Runtime.GCSettings.IsServerGC ? "Server" : "Workstation")}");
            Console.WriteLine($"length (copy count): {length}");
            Console.WriteLine($"iterations / run   : {iterations:N0}");
            Console.WriteLine($"runs / method      : {runs}");
            Console.WriteLine($"dest offset        : {offset}");
            Console.WriteLine();

            // Warm up both methods so tiered JIT promotes them before timing.
            Warmup(src, dst, offset, length);

            var arrayCopyMs = new double[runs];
            var manualMs = new double[runs];
            var spanMs = new double[runs];
            long sinkTotal = 0;

            // Interleave the methods run-by-run so any thermal / scheduler drift
            // affects them roughly equally instead of biasing one.
            for (int r = 0; r < runs; r++)
            {
                arrayCopyMs[r] = TimeArrayCopy(src, dst, offset, length, iterations, out long s1);
                manualMs[r] = TimeManualLoop(src, dst, offset, length, iterations, out long s2);
                spanMs[r] = TimeSpanCopyTo(src, dst, offset, length, iterations, out long s3);
                sinkTotal += s1 + s2 + s3;

                Console.WriteLine(
                    $"run {r + 1,2}:  Array.Copy = {arrayCopyMs[r],9:F2} ms   manual = {manualMs[r],9:F2} ms   Span.CopyTo = {spanMs[r],9:F2} ms");
            }

            double acBest = Min(arrayCopyMs);
            double mlBest = Min(manualMs);
            double spBest = Min(spanMs);
            double acMed = Median(arrayCopyMs);
            double mlMed = Median(manualMs);
            double spMed = Median(spanMs);

            double nsPerCopyAc = acBest * 1_000_000.0 / iterations;
            double nsPerCopyMl = mlBest * 1_000_000.0 / iterations;
            double nsPerCopySp = spBest * 1_000_000.0 / iterations;

            Console.WriteLine();
            Console.WriteLine("--- results (lower is better) ---");
            Console.WriteLine($"Array.Copy      best = {acBest,9:F2} ms   median = {acMed,9:F2} ms   ({nsPerCopyAc:F3} ns/copy)");
            Console.WriteLine($"manual loop     best = {mlBest,9:F2} ms   median = {mlMed,9:F2} ms   ({nsPerCopyMl:F3} ns/copy)");
            Console.WriteLine($"Span.CopyTo     best = {spBest,9:F2} ms   median = {spMed,9:F2} ms   ({nsPerCopySp:F3} ns/copy)");
            Console.WriteLine();

            // Primary comparison requested: Span<object>.CopyTo vs Array.Copy.
            Report("Span.CopyTo", spBest, "Array.Copy", acBest, runs);
            Report("manual loop", mlBest, "Array.Copy", acBest, runs);

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
                ManualCopy(src, dst, offset, length);
                SpanCopy(src, dst, offset, length);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double TimeArrayCopy(object[] src, object[] dst, int offset, int length, long iterations, out long sink)
        {
            var sw = Stopwatch.StartNew();
            for (long it = 0; it < iterations; it++)
            {
                Array.Copy(src, 0, dst, offset, length);
            }
            sw.Stop();
            sink = dst[offset + length - 1] is int v ? v : 0;
            return sw.Elapsed.TotalMilliseconds;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double TimeManualLoop(object[] src, object[] dst, int offset, int length, long iterations, out long sink)
        {
            var sw = Stopwatch.StartNew();
            for (long it = 0; it < iterations; it++)
            {
                ManualCopy(src, dst, offset, length);
            }
            sw.Stop();
            sink = dst[offset + length - 1] is int v ? v : 0;
            return sw.Elapsed.TotalMilliseconds;
        }

        // Same shape as the shipped code: hoisted locals, simple index loop.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ManualCopy(object[] src, object[] dst, int offset, int length)
        {
            for (int i = 0; i < length; i++)
            {
                dst[offset + i] = src[i];
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static double TimeSpanCopyTo(object[] src, object[] dst, int offset, int length, long iterations, out long sink)
        {
            var sw = Stopwatch.StartNew();
            for (long it = 0; it < iterations; it++)
            {
                SpanCopy(src, dst, offset, length);
            }
            sw.Stop();
            sink = dst[offset + length - 1] is int v ? v : 0;
            return sw.Elapsed.TotalMilliseconds;
        }

        // Span<object>.CopyTo: for reference-type spans this routes through
        // Buffer.BulkMoveWithWriteBarrier (memmove + bulk card-marking), like Array.Copy,
        // but without Array.Copy's runtime-helper argument/type validation layer.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SpanCopy(object[] src, object[] dst, int offset, int length)
        {
            src.AsSpan(0, length).CopyTo(dst.AsSpan(offset, length));
        }

        private static void Report(string a, double aMs, string b, double bMs, int runs)
        {
            double ratio = bMs / aMs;
            if (ratio >= 1.0)
                Console.WriteLine($"=> {a} is {ratio:F2}x FASTER than {b} (best-of-{runs})");
            else
                Console.WriteLine($"=> {a} is {1.0 / ratio:F2}x SLOWER than {b} (best-of-{runs})");
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
            Console.WriteLine("  --runs       / -r  Timed runs per method         (default 7)");
            Console.WriteLine("  --offset     / -o  Destination start offset       (default 0)");
        }
    }
}
