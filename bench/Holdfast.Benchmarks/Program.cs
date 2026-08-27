using System;
using System.Collections.Generic;
using System.Diagnostics;
using Holdfast;

namespace Holdfast.Benchmarks
{
    /// <summary>The comparer the benchmarks measure with. A struct, so the JIT inlines it.</summary>
    public readonly struct LongComparer : IArenaComparer<long>
    {
        public int Compare(in long a, in long b) => a.CompareTo(b);
        public int CompareDescendant(in long a, in long b) => a.CompareTo(b);
    }

    /// <summary>
    /// The same comparer behind an interface. Because the field is typed as
    /// IComparer&lt;long&gt;, the JIT cannot inline the comparison. This is the
    /// control for measuring what devirtualization is worth: the tree is the
    /// exact same code, only the way it compares changes.
    /// </summary>
    public readonly struct BoxedLongComparer : IArenaComparer<long>
    {
        private readonly IComparer<long> inner;
        public BoxedLongComparer(IComparer<long> comparer) => inner = comparer;
        public int Compare(in long a, in long b) => inner.Compare(a, b);
        public int CompareDescendant(in long a, in long b) => inner.Compare(a, b);
    }

    /// <summary>
    ///   dotnet run -c Release --project bench/Holdfast.Benchmarks
    ///   dotnet run -c Release --project bench/Holdfast.Benchmarks -- 10000000
    ///
    /// Numbers from a Debug build mean nothing: without optimization there is
    /// no inlining, single-field structs are not unwrapped, the debug mode
    /// assertions run and bounds checks stay in. The program says so.
    ///
    /// Method: first run discarded (JIT and warmup), several repetitions, best
    /// time reported — the least noisy estimator for a microbenchmark, since
    /// system interference can only add. This does not replace BenchmarkDotNet,
    /// which also measures variance and isolates the process; it is here to
    /// give the order of magnitude and to catch regressions.
    /// </summary>
    public static class Program
    {
        private static long checksum;
        private static int reps;
        private static bool warmup;

        public static int Main(string[] args)
        {
            int n = args.Length > 0 && int.TryParse(args[0], out int parsed) ? parsed : 1_000_000;

            // Repetitions have to scale down or a 50M run takes hours: the
            // delete row alone is minutes per repetition.
            reps = n <= 1_000_000 ? 5 : n <= 10_000_000 ? 3 : 1;
            warmup = n <= 10_000_000;

#if DEBUG
            Console.WriteLine();
            Console.WriteLine("  *** DEBUG BUILD — these numbers are meaningless. ***");
            Console.WriteLine("  *** Use: dotnet run -c Release -- <n>            ***");
            Console.WriteLine();
#endif
            Console.WriteLine($"Holdfast benchmarks — {n:N0} keys");
            Console.WriteLine($".NET {Environment.Version} | {(Environment.Is64BitProcess ? "x64" : "x86")} | " +
                              $"{Environment.ProcessorCount} cores | server GC: {System.Runtime.GCSettings.IsServerGC}");
            Console.WriteLine($"{reps} repetition(s), best time reported" + (warmup ? ", first run discarded." : ", no warmup run."));
            Console.WriteLine();

            long[] keys = RandomKeys(n, 20260809);

            // dotnet run -c Release --project bench/Holdfast.Benchmarks -- 10000000 checkpoint
            if (args.Length > 1 && string.Equals(args[1], "checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                Checkpoint(keys);
                return 0;
            }

            Insertion(keys);
            Lookup(keys);
            Traversal(keys);
            Deletion(keys);
            Devirtualization(keys);
            Memory(keys);

            Console.WriteLine();
            Console.WriteLine($"(checksum: {checksum})");
            return 0;
        }

        // =====================================================================
        //  Checkpoint
        // =====================================================================

        /// <summary>
        /// The comparison that matters for a checkpoint is not MB/s, which is
        /// your disk's number and not the library's. It is loading against
        /// rebuilding: loading is a sequential read with no per-node work,
        /// rebuilding is n log n comparisons over cold cache lines.
        /// </summary>
        /// <remarks>
        /// Single run, no repetitions and no warmup on purpose: the second read
        /// of a file just written comes out of the page cache and measures RAM.
        /// Even the first one is warm here, because this process wrote it. For a
        /// number that means anything, run it, drop the cache or reboot, and run
        /// the load half again.
        /// </remarks>
        private static void Checkpoint(long[] keys)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "holdfast-checkpoint-bench.hf");

            try
            {
                Arena<long>.Reset();

                var clock = Stopwatch.StartNew();
                var set = RedBlackSet<long, LongComparer>.Create();
                foreach (long key in keys) set.Insert(key);
                clock.Stop();
                double build = clock.Elapsed.TotalSeconds;

                clock.Restart();
                var snapshot = Snapshot.Create();
                snapshot.AddSet("index", set);
                using (var file = new System.IO.FileStream(
                    path, System.IO.FileMode.Create, System.IO.FileAccess.Write,
                    System.IO.FileShare.None, 1 << 20))
                {
                    snapshot.SaveTo(file);
                }
                clock.Stop();
                double save = clock.Elapsed.TotalSeconds;

                double megabytes = new System.IO.FileInfo(path).Length / (1024.0 * 1024.0);

                Arena<long>.Reset();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);

                clock.Restart();
                Snapshot loaded;
                using (var file = new System.IO.FileStream(
                    path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                    System.IO.FileShare.Read, 1 << 20))
                {
                    loaded = Snapshot.LoadFrom(file);
                }
                loaded.Activate();
                clock.Stop();
                double load = clock.Elapsed.TotalSeconds;

                var back = loaded.GetSet<long, LongComparer>("index");
                checksum += back.Count;

                Heading("Checkpoint");
                Console.WriteLine($"  {"build by insertion",-28} {build * 1000,10:N0} {"",10} {"1.00x",10}");
                Console.WriteLine($"  {"save",-28} {save * 1000,10:N0} {megabytes / save,9:N0}M {save / build,9:F3}x");
                Console.WriteLine($"  {"load",-28} {load * 1000,10:N0} {megabytes / load,9:N0}M {load / build,9:F3}x");
                Console.WriteLine();
                Console.WriteLine($"  file: {megabytes:N0} MB for {back.Count:N0} keys " +
                                  $"({Arena<long>.BlockCount} block(s) of {Layout.BlockSize:N0} nodes reserved)");
                Console.WriteLine($"  loading instead of rebuilding: {build / load:F1}x faster");
                Console.WriteLine();
                Console.WriteLine("  The load figure is warm: this process wrote the file. Drop the page");
                Console.WriteLine("  cache or reboot and run it again for a number off the disk.");
            }
            finally
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                Arena<long>.Reset();
            }
        }

        // =====================================================================
        //  Measurement
        // =====================================================================

        private static double Time(Action action)
        {
            if (warmup) action();

            double best = double.MaxValue;
            for (int i = 0; i < reps; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                var clock = Stopwatch.StartNew();
                action();
                clock.Stop();
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds);
            }
            return best;
        }

        private static void Heading(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"  {title}");
            Console.WriteLine($"  {new string('-', 62)}");
            Console.WriteLine($"  {"",-28} {"ms",10} {"ns/op",10} {"relative",10}");
        }

        private static void Row(string label, double ms, int n, double baseline)
        {
            double nsPerOp = ms * 1_000_000.0 / n;
            string relative = baseline <= 0 ? "—" : $"{ms / baseline:F2}x";
            Console.WriteLine($"  {label,-28} {ms,10:F1} {nsPerOp,10:F1} {relative,10}");
        }

        private static long[] RandomKeys(int n, int seed)
        {
            var random = new Random(seed);
            var keys = new long[n];
            for (int i = 0; i < n; i++)
                keys[i] = ((long)random.Next() << 20) | (uint)random.Next(1 << 20);
            return keys;
        }

        // =====================================================================
        //  Insertion
        // =====================================================================

        private static void Insertion(long[] keys)
        {
            int n = keys.Length;
            Heading("Insertion (random keys)");

            double baseline = Time(() =>
            {
                var set = new SortedSet<long>();
                foreach (long k in keys) set.Add(k);
                checksum += set.Count;
            });
            Row("SortedSet<long>", baseline, n, baseline);

            Row("SortedDictionary<long,long>", Time(() =>
            {
                var map = new SortedDictionary<long, long>();
                foreach (long k in keys) map[k] = k;
                checksum += map.Count;
            }), n, baseline);

            Row("RedBlackSet<long>", Time(() =>
            {
                Arena<long>.Reset();
                var set = RedBlackSet<long, LongComparer>.Create();
                foreach (long k in keys) set.Insert(k);
                checksum += set.Count;
                set.Clear();
            }), n, baseline);

            Row("RedBlackTree<long>", Time(() =>
            {
                Arena<long>.Reset();
                Arena<ArenaList<long>>.Reset();
                var tree = RedBlackTree<long, LongComparer>.Create();
                foreach (long k in keys) tree.Insert(k);
                checksum += tree.NodeCount;
                tree.Clear();
            }), n, baseline);

            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();
        }

        // =====================================================================
        //  Lookup
        // =====================================================================

        private static void Lookup(long[] keys)
        {
            int n = keys.Length;
            Heading("Lookup (every key, all present)");

            var reference = new SortedSet<long>();
            foreach (long k in keys) reference.Add(k);

            double baseline = Time(() =>
            {
                long found = 0;
                foreach (long k in keys) if (reference.Contains(k)) found++;
                checksum += found;
            });
            Row("SortedSet<long>", baseline, n, baseline);

            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            foreach (long k in keys) set.Insert(k);

            Row("RedBlackSet<long>", Time(() =>
            {
                long found = 0;
                foreach (long k in keys) if (set.Contains(k)) found++;
                checksum += found;
            }), n, baseline);

            set.Clear();
            Arena<long>.Reset();
        }

        // =====================================================================
        //  Traversal
        // =====================================================================

        private static void Traversal(long[] keys)
        {
            int n = keys.Length;
            Heading("In-order traversal");

            var reference = new SortedSet<long>();
            foreach (long k in keys) reference.Add(k);

            double baseline = Time(() =>
            {
                long total = 0;
                foreach (long k in reference) total += k;
                checksum += total;
            });
            Row("SortedSet<long>", baseline, n, baseline);

            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            foreach (long k in keys) set.Insert(k);

            Row("RedBlackSet (parent links)", Time(() =>
            {
                long total = 0;
                foreach (var node in set) total += set.ValueOf(node);
                checksum += total;
            }), n, baseline);

            Row("RedBlackSet (explicit stack)", Time(() =>
            {
                long total = 0;
                foreach (var node in set.InOrder) total += set.ValueOf(node);
                checksum += total;
            }), n, baseline);

            set.Clear();
            Arena<long>.Reset();
        }

        // =====================================================================
        //  Deletion
        // =====================================================================

        private static void Deletion(long[] keys)
        {
            int n = keys.Length;
            Heading("Insert then remove every key");

            double baseline = Time(() =>
            {
                var set = new SortedSet<long>();
                foreach (long k in keys) set.Add(k);
                foreach (long k in keys) set.Remove(k);
                checksum += set.Count;
            });
            Row("SortedSet<long>", baseline, n, baseline);

            Row("RedBlackSet<long>", Time(() =>
            {
                Arena<long>.Reset();
                var set = RedBlackSet<long, LongComparer>.Create();
                foreach (long k in keys) set.Insert(k);
                foreach (long k in keys) set.Remove(k);
                checksum += set.Count;
            }), n, baseline);

            Arena<long>.Reset();
        }

        // =====================================================================
        //  Devirtualization
        // =====================================================================

        private static void Devirtualization(long[] keys)
        {
            int n = keys.Length;
            Heading("Comparer: inlined struct vs interface field");

            double baseline = Time(() =>
            {
                Arena<long>.Reset();
                var set = RedBlackSet<long, BoxedLongComparer>.Create(
                    new BoxedLongComparer(Comparer<long>.Default));
                foreach (long k in keys) set.Insert(k);
                checksum += set.Count;
                set.Clear();
            });
            Row("interface (virtual call)", baseline, n, baseline);

            Row("struct (inlined)", Time(() =>
            {
                Arena<long>.Reset();
                var set = RedBlackSet<long, LongComparer>.Create();
                foreach (long k in keys) set.Insert(k);
                checksum += set.Count;
                set.Clear();
            }), n, baseline);

            Console.WriteLine();
            Console.WriteLine("    Note: .NET 8 has dynamic PGO and may speculatively devirtualize the");
            Console.WriteLine("    interface when it only ever sees one concrete type. A small gap means");
            Console.WriteLine("    that — not that the virtual comparer is free. With several comparers");
            Console.WriteLine("    in play the gap widens. This workload is also memory-latency bound,");
            Console.WriteLine("    so instructions saved hide in the shadow of a cache miss.");

            Arena<long>.Reset();
        }

        // =====================================================================
        //  Memory
        // =====================================================================

        private static void Memory(long[] keys)
        {
            Console.WriteLine();
            Console.WriteLine("  Memory per key");
            Console.WriteLine($"  {new string('-', 62)}");
            Console.WriteLine($"  {"",-28} {"MB",10} {"bytes/key",14}");

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long before = GC.GetTotalMemory(true);

            var reference = new SortedSet<long>();
            foreach (long k in keys) reference.Add(k);

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long managedBytes = GC.GetTotalMemory(true) - before;
            long referenceCount = reference.Count;
            Console.WriteLine($"  {"SortedSet<long>",-28} {managedBytes / 1048576.0,10:F1} {(double)managedBytes / referenceCount,14:F1}");
            reference = null!;

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            foreach (long k in keys) set.Insert(k);

            long inUse = Arena<long>.Allocated * 24;
            long reserved = Arena<long>.BlockCount * (long)Layout.BlockSize * 24;
            Console.WriteLine($"  {"RedBlackSet (in use)",-28} {inUse / 1048576.0,10:F1} {(double)inUse / set.Count,14:F1}");
            Console.WriteLine($"  {"RedBlackSet (reserved)",-28} {reserved / 1048576.0,10:F1} {(double)reserved / set.Count,14:F1}");

            set.Clear();
            Arena<long>.Reset();

            Console.WriteLine();
            Console.WriteLine("    A SortedSet<long> node is a class: object header, two references,");
            Console.WriteLine("    the value and the color. An arena node is 24 flat bytes, with no");
            Console.WriteLine("    header and no pointers for the GC to walk. 'Reserved' counts whole");
            Console.WriteLine("    blocks — the arena grows one million nodes at a time.");
        }
    }
}
