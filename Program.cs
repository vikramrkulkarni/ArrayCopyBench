using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

namespace ArrayCopyBench
{
    /// <summary>
    /// BenchmarkDotNet benchmark for Array.Copy of an object[] — the 3-arg
    /// Run (must be Release, no debugger attached):
    ///     dotnet run -c Release
    /// Quick smoke test (fewer iterations):
    ///     dotnet run -c Release -- --job short
    /// Length sweep: add values to the [Params] attribute, e.g. [Params(1, 4, 16, 64)].
    /// </summary>
    [GcServer(true)]                 // match the server: Server GC ...
    [GcConcurrent(true)]             // ... concurrent (same as the csproj / product)
    [MemoryDiagnoser(false)]         // report allocations (expected: 0 B for a copy)
    public class ArrayCopyBenchmark
    {
        // Elements copied per operation (the "_startColumnIndex" in ConstructRow).
        // Add values to sweep in a single run, e.g. [Params(1, 2, 4, 8, 16, 32, 64)].
        [Params(10, 100, 500)]
        public int Length;

        // Destination is wider than the copy region, mirroring RowData vs the key prefix.
        private const int Offset = 0;

        private object[] _src;
        private object[] _dst;

        [GlobalSetup]
        public void Setup()
        {
            // Boxed ints -> heap references, so copies exercise real GC write barriers
            // (the same reason object[] copies are more expensive than blittable copies).
            _src = new object[Length];
            for (int i = 0; i < Length; i++)
            {
                _src[i] = i;
            }
            _dst = new object[Offset + Length];
        }

        // 3-arg Array.Copy — matches ConstructRow line 307 (copies Length elements to dst[0..Length]).
        [Benchmark]
        public object ArrayCopy()
        {
            Array.Copy(_src, _dst, Length);
            return _dst[Length - 1]; // returned so BenchmarkDotNet cannot eliminate the copy as dead code
        }
    }

    public static class Program
    {
        public static void Main(string[] args) =>
            BenchmarkRunner.Run<ArrayCopyBenchmark>(null, args);
    }
}
