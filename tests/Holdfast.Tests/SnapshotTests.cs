using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Holdfast.Tests
{
    /// <summary>
    /// El README de la 0.1.0 decia que la arena es reubicable y que un
    /// checkpoint "cuesta una escritura a disco". Estos tests son lo que hace
    /// que esa frase sea verificable y no una propiedad de diseno sin
    /// superficie.
    /// </summary>
    public class SnapshotTests
    {
        // ---------------------------------------------------------------------
        //  Ida y vuelta
        // ---------------------------------------------------------------------

        [Fact]
        public void EmptyArenaRoundTrips()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();

            byte[] bytes = Save(s => s.AddSet("index", set));

            Arena<long>.Reset();
            Snapshot loaded = Load(bytes);
            loaded.Activate();

            var back = loaded.GetSet<long, LongComparer>("index");

            Assert.Equal(0L, back.Count);
            Assert.True(back.IsEmpty, "un set vacio tiene que volver vacio");
            Assert.Equal(0L, Arena<long>.BlockCount);
            Arena<long>.ValidateIntegrity();

            Arena<long>.Reset();
        }

        [Fact]
        public void SetSurvivesTheRoundTrip()
        {
            const int N = 100_000;

            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();

            var random = new Random(20260827);
            var expected = new SortedSet<long>();
            for (int i = 0; i < N; i++)
            {
                long key = random.NextInt64();
                if (expected.Add(key)) set.Insert(key);
            }

            long countBefore = set.Count;
            long allocatedBefore = Arena<long>.Allocated;

            byte[] bytes = Save(s => s.AddSet("index", set));

            Arena<long>.Reset();
            Assert.Equal(0L, Arena<long>.Allocated);

            Snapshot loaded = Load(bytes);
            loaded.Activate();
            var back = loaded.GetSet<long, LongComparer>("index");

            Assert.Equal(countBefore, back.Count);
            Assert.Equal(allocatedBefore, Arena<long>.Allocated);

            back.Validate();
            Arena<long>.ValidateIntegrity();

            var walked = new List<long>();
            foreach (SetNode<long> node in back.InOrder) walked.Add(back.ValueOf(node));

            Assert.Equal(expected.Count, walked.Count);
            Assert.Equal(new List<long>(expected), walked);

            // No alcanza con que se lea: tiene que seguir siendo un arbol que
            // acepta operaciones.
            back.Insert(long.MaxValue);
            back.Remove(long.MaxValue);
            back.Validate();
            Assert.Equal(countBefore, back.Count);

            Arena<long>.Reset();
        }

        [Fact]
        public void HandlesStayValidAcrossTheFile()
        {
            Arena<long>.Reset();

            Handle<long, RawMode> kept = Arena<long>.Allocate();
            Arena<long>.Value(kept) = 987654321L;
            string rendered = kept.ToString();

            var set = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 5000; i++) set.Insert(i);

            byte[] bytes = Save(s =>
            {
                s.AddSet("index", set);
                s.AddHandle("kept", kept);
            });

            Arena<long>.Reset();
            Snapshot loaded = Load(bytes);
            loaded.Activate();

            Handle<long, RawMode> again = loaded.GetHandle<long, RawMode>("kept");

            // Esta es la propiedad de la que depende todo lo demas: el handle no
            // se traduce ni se arregla, es el mismo numero.
            Assert.Equal(kept, again);
            Assert.Equal(rendered, again.ToString());
            Assert.Equal(987654321L, Arena<long>.Value(again));

            Arena<long>.Reset();
        }

        [Fact]
        public void FragmentedFreeListComesBackIntact()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();

            for (int i = 0; i < 60_000; i++) set.Insert(i);
            for (int i = 0; i < 60_000; i += 2) set.Remove(i);

            long allocated = Arena<long>.Allocated;
            long available = Arena<long>.Available;
            long total = Arena<long>.TotalAllocations;
            long count = set.Count;

            byte[] bytes = Save(s => s.AddSet("index", set));

            Arena<long>.Reset();
            Snapshot loaded = Load(bytes);
            loaded.Activate();
            var back = loaded.GetSet<long, LongComparer>("index");

            Assert.Equal(allocated, Arena<long>.Allocated);
            Assert.Equal(available, Arena<long>.Available);
            Assert.Equal(total, Arena<long>.TotalAllocations);
            Assert.Equal(count, back.Count);

            Arena<long>.ValidateIntegrity();
            back.Validate();

            // La lista libre tiene que ser usable, no solo contable.
            for (int i = 0; i < 60_000; i += 2) back.Insert(i);

            back.Validate();
            Arena<long>.ValidateIntegrity();
            Assert.Equal(60_000L, back.Count);
            Assert.Equal(60_000L, Arena<long>.Allocated);

            Arena<long>.Reset();
        }

        [Fact]
        public void TreeSpansTwoArenasAndBothAreSaved()
        {
            Arena<PairKey>.Reset();
            Arena<ArenaList<PairKey>>.Reset();

            var tree = RedBlackTree<PairKey, PairKeyComparer>.Create();
            for (int i = 0; i < 20_000; i++)
                tree.Insert(new PairKey { Key = i % 5_000, Tag = i });

            long nodes = tree.NodeCount;
            long values = tree.ValueCount;
            Assert.Equal(5_000L, nodes);
            Assert.Equal(20_000L, values);

            byte[] bytes = Save(s => s.AddTree("tree", tree));

            Arena<PairKey>.Reset();
            Arena<ArenaList<PairKey>>.Reset();

            Snapshot loaded = Load(bytes);

            // AddTree agrega las dos arenas. Si agregara una sola, lo que sigue
            // pasaria igual hasta que se recorre una lista de valores.
            Assert.Equal(2, loaded.ArenaCount);

            loaded.Activate();
            var back = loaded.GetTree<PairKey, PairKeyComparer>("tree");

            Assert.Equal(nodes, back.NodeCount);
            Assert.Equal(values, back.ValueCount);

            back.Validate();
            Arena<PairKey>.ValidateIntegrity();
            Arena<ArenaList<PairKey>>.ValidateIntegrity();

            long walkedNodes = 0;
            long seenValues = 0;
            foreach (TreeNode<PairKey> node in back.InOrder)
            {
                walkedNodes++;
                seenValues += back.ValuesOf(node).Count;
            }

            Assert.Equal(nodes, walkedNodes);
            Assert.Equal(values, seenValues);

            Arena<PairKey>.Reset();
            Arena<ArenaList<PairKey>>.Reset();
        }

        [Fact]
        public void ListRoundTrips()
        {
            Arena<long>.Reset();

            ArenaList<long> list = ArenaList<long>.Empty;
            for (int i = 0; i < 1000; i++) list.AddLast(i);

            byte[] bytes = Save(s => s.AddList("queue", list));

            Arena<long>.Reset();
            Snapshot loaded = Load(bytes);
            loaded.Activate();
            var back = loaded.GetList<long>("queue");

            Assert.Equal(1000L, back.Count);

            long expected = 0;
            foreach (long value in back.Values) Assert.Equal(expected++, value);
            Assert.Equal(1000L, expected);

            Arena<long>.Reset();
        }

        [Fact]
        public void ArenaOfSeveralBlocksRoundTrips()
        {
            // Un bloque son 1.048.576 nodos. Por debajo de eso todo vive en el
            // bloque 0, el indice de bloque de toda direccion es cero, y un bug
            // en la aritmetica de bloques no se ve. Por eso este test tiene que
            // ser grande: es el unico que cruza a los bloques 1 y 2.
            const int N = 1_200_000;
            string path = Path.Combine(Path.GetTempPath(), $"holdfast-blocks-{Guid.NewGuid():N}.hf");

            try
            {
                Arena<long>.Reset();
                var set = RedBlackSet<long, LongComparer>.Create();
                for (long i = 0; i < N; i++) set.Insert(i);

                Assert.True(Arena<long>.BlockCount >= 2,
                    $"el test no cruzo un bloque: {Arena<long>.BlockCount}");

                Snapshot snapshot = Snapshot.Create();
                snapshot.AddSet("index", set);
                using (FileStream file = File.Create(path)) snapshot.SaveTo(file);

                Arena<long>.Reset();
                Snapshot loaded;
                using (FileStream file = File.OpenRead(path)) loaded = Snapshot.LoadFrom(file);

                loaded.Activate();
                var back = loaded.GetSet<long, LongComparer>("index");

                back.Validate();
                Arena<long>.ValidateIntegrity();
                Assert.Equal((long)N, back.Count);
                Assert.True(back.Contains(0), "falta la clave del bloque 0");
                Assert.True(back.Contains(N - 1), "falta la clave del ultimo bloque");

                long walked = 0;
                long previous = long.MinValue;
                foreach (SetNode<long> node in back.InOrder)
                {
                    long value = back.ValueOf(node);
                    Assert.True(value > previous, $"el orden se rompio en {value}");
                    previous = value;
                    walked++;
                }

                Assert.Equal((long)N, walked);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                Arena<long>.Reset();
            }
        }

        // ---------------------------------------------------------------------
        //  Propiedades del archivo
        // ---------------------------------------------------------------------

        [Fact]
        public void TwoWritesOfTheSameArenaAreByteIdentical()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();

            var random = new Random(7);
            for (int i = 0; i < 30_000; i++) set.Insert(random.NextInt64(0, 1_000_000));
            for (int i = 0; i < 10_000; i++) set.Remove(random.NextInt64(0, 1_000_000));

            byte[] first = Save(s => s.AddSet("index", set));
            byte[] second = Save(s => s.AddSet("index", set));

            Assert.Equal(first.Length, second.Length);
            Assert.Equal(first, second);

            Arena<long>.Reset();
        }

        [Fact]
        public void FreedPayloadsDoNotReachTheFile()
        {
            const long Pattern = unchecked((long)0xDEADBEEFCAFEF00DUL);

            Arena<long>.Reset();

            var handles = new List<Handle<long, RawMode>>();
            for (int i = 0; i < 1000; i++)
            {
                Handle<long, RawMode> handle = Arena<long>.Allocate();
                Arena<long>.Value(handle) = Pattern;
                handles.Add(handle);
            }
            foreach (Handle<long, RawMode> handle in handles) Arena<long>.Free(handle);

            byte[] scrubbed = Save(s => s.AddArena<long>(), new SnapshotOptions { ScrubFreeNodes = true });
            Assert.True(IndexOf(scrubbed, Pattern) < 0,
                "el payload de un nodo liberado quedo en el archivo");

            // Y el barrido no puede romper la lista libre que recorre.
            Arena<long>.ValidateIntegrity();
            Handle<long, RawMode> reused = Arena<long>.Allocate();
            Assert.Equal(0L, Arena<long>.Value(reused));
            Arena<long>.ValidateIntegrity();

            Arena<long>.Reset();
        }

        [Fact]
        public void FileSizeIsReservedCapacityNotLiveKeys()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            set.Insert(1);

            byte[] bytes = Save(s => s.AddSet("index", set));

            // Una clave reserva un bloque entero, y el archivo es el bloque
            // entero. Esta declarado en el README y este test lo fija.
            long block = (long)Layout.BlockSize * 24;  // Node<long> son 24 bytes
            Assert.True(bytes.Length > block, $"el archivo mide {bytes.Length}, menos que un bloque");
            Assert.True(bytes.Length - block < 1024, $"la cabecera mide {bytes.Length - block} bytes");

            Arena<long>.Reset();
        }

        [Fact]
        public void ACapturedArenaIsWrittenEvenAfterItIsSwappedOut()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 3000; i++) set.Insert(i);

            Snapshot snapshot = Snapshot.Create();
            snapshot.AddSet("index", set);

            // La arena que el snapshot capturo ya no es la activa.
            Arena<long>.Swap(ArenaState<long>.Empty);
            var decoy = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 10; i++) decoy.Insert(i + 500_000);

            byte[] bytes;
            using (var stream = new MemoryStream())
            {
                snapshot.SaveTo(stream);
                bytes = stream.ToArray();
            }

            Arena<long>.Restore(ArenaState<long>.Empty);
            Snapshot loaded = Load(bytes);
            loaded.Activate();
            var back = loaded.GetSet<long, LongComparer>("index");

            back.Validate();
            Assert.Equal(3000L, back.Count);

            Arena<long>.Reset();
        }

        // ---------------------------------------------------------------------
        //  Resolucion de tipos
        // ---------------------------------------------------------------------

        [Fact]
        public void LoadsWithAnEmptyRegistry()
        {
            // Un proceso que reanuda un computo no llamo a AddArena todavia:
            // arranca con un path y nada mas. Vaciar el registro deja el mismo
            // camino que ese proceso toma, el de resolver el tipo por nombre.
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 10_000; i++) set.Insert(i);

            byte[] bytes = Save(s => s.AddSet("index", set));

            Arena<long>.Reset();
            Snapshot.ClearRegistry();

            Snapshot loaded = Load(bytes);
            loaded.Activate();
            var back = loaded.GetSet<long, LongComparer>("index");

            back.Validate();
            Assert.Equal(10_000L, back.Count);

            Arena<long>.Reset();
        }

        // ---------------------------------------------------------------------
        //  Lo que tiene que rechazar
        // ---------------------------------------------------------------------

        [Fact]
        public void RefusesAPayloadHoldingReferences()
        {
            // Un snapshot es una copia de memoria: una referencia se escribiria
            // como una direccion de este proceso y no significaria nada en otro.
            Assert.Throws<NotSupportedException>(
                () => Save(s => s.AddArena<WithReference>()));
        }

        [Fact]
        public void RefusesAStreamThatIsNotASnapshot()
        {
            byte[] junk = new byte[512];
            new Random(1).NextBytes(junk);

            Assert.Throws<SnapshotFormatException>(
                () => Snapshot.LoadFrom(new MemoryStream(junk)));
        }

        [Fact]
        public void RefusesATruncatedFile()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 5000; i++) set.Insert(i);

            byte[] bytes = Save(s => s.AddSet("index", set));

            Assert.Throws<SnapshotFormatException>(
                () => Snapshot.LoadFrom(new MemoryStream(bytes, 0, bytes.Length / 2)));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesAnUnknownFormatVersion()
        {
            Arena<long>.Reset();
            byte[] bytes = Save(s => s.AddArena<long>());

            bytes[8] = 99;  // el int32 de version arranca justo despues del magic

            Assert.Throws<SnapshotFormatException>(
                () => Snapshot.LoadFrom(new MemoryStream(bytes)));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesARootReadAsTheWrongKind()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            set.Insert(1);

            byte[] bytes = Save(s => s.AddSet("index", set));

            Arena<long>.Reset();
            Snapshot loaded = Load(bytes);
            loaded.Activate();

            Assert.Throws<InvalidOperationException>(() => loaded.GetList<long>("index"));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesARootReadAsTheWrongPayloadType()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            set.Insert(1);

            byte[] bytes = Save(s => s.AddSet("index", set));

            Arena<long>.Reset();
            Snapshot loaded = Load(bytes);
            loaded.Activate();

            Assert.Throws<InvalidOperationException>(
                () => loaded.GetSet<PairKey, PairKeyComparer>("index"));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesARootPointingOutsideItsArena()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 100; i++) set.Insert(i);

            byte[] bytes = Save(s => s.AddSet("index", set));

            set.CaptureState(out long rootAddress, out _);
            int at = IndexOf(bytes, rootAddress, limit: 400);
            Assert.True(at >= 0, "no se encontro la direccion raiz en la cabecera");

            byte[] tampered = (byte[])bytes.Clone();
            BitConverter.GetBytes(1L << 40).CopyTo(tampered, at);

            Assert.Throws<SnapshotFormatException>(
                () => Snapshot.LoadFrom(new MemoryStream(tampered)));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesARootReadBeforeActivate()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            set.Insert(1);

            byte[] bytes = Save(s => s.AddSet("index", set));
            Snapshot loaded = Load(bytes);

            Assert.Throws<InvalidOperationException>(
                () => loaded.GetSet<long, LongComparer>("index"));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesTwoRootsWithTheSameName()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();

            Snapshot snapshot = Snapshot.Create();
            snapshot.AddSet("index", set);

            Assert.Throws<ArgumentException>("name", () => snapshot.AddSet("index", set));

            Arena<long>.Reset();
        }

        [Fact]
        public void RefusesAnUnknownRootName()
        {
            Arena<long>.Reset();
            var set = RedBlackSet<long, LongComparer>.Create();
            byte[] bytes = Save(s => s.AddSet("index", set));

            Snapshot loaded = Load(bytes);
            loaded.Activate();

            Assert.Throws<KeyNotFoundException>(
                () => loaded.GetSet<long, LongComparer>("no-existe"));

            Arena<long>.Reset();
        }

        // ---------------------------------------------------------------------
        //  Plomeria
        // ---------------------------------------------------------------------

        /// <summary>Un payload con una referencia, para el test que lo rechaza.</summary>
        public struct WithReference
        {
            public long Key;
            public string? Label;
        }

        private static byte[] Save(Action<Snapshot> build) => Save(build, SnapshotOptions.Default);

        private static byte[] Save(Action<Snapshot> build, SnapshotOptions options)
        {
            Snapshot snapshot = Snapshot.Create();
            build(snapshot);

            using var stream = new MemoryStream();
            snapshot.SaveTo(stream, options);
            return stream.ToArray();
        }

        private static Snapshot Load(byte[] bytes) => Snapshot.LoadFrom(new MemoryStream(bytes));

        private static int IndexOf(byte[] haystack, long needle) => IndexOf(haystack, needle, haystack.Length);

        private static int IndexOf(byte[] haystack, long needle, int limit)
        {
            byte[] pattern = BitConverter.GetBytes(needle);
            int end = Math.Min(limit, haystack.Length) - pattern.Length;

            for (int i = 0; i <= end; i++)
            {
                bool hit = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (haystack[i + j] != pattern[j]) { hit = false; break; }
                }
                if (hit) return i;
            }
            return -1;
        }
    }
}
