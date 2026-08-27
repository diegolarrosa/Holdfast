using System;
using System.Collections.Generic;
using System.IO;

namespace Holdfast
{
    /// <summary>
    /// A set of arenas and named roots, written to a stream and read back.
    /// </summary>
    /// <remarks>
    /// Nothing in an arena is a pointer: an address is a block index and an
    /// offset, so the blocks mean the same thing wherever they are loaded. A
    /// snapshot is that property with a surface: it copies the blocks out and
    /// copies them back, and every handle that was valid before is valid after.
    /// There is no traversal, no graph walk and no fixup pass.
    /// <para>
    /// It holds several arenas because one is rarely enough. A
    /// <see cref="RedBlackTree{T, TComp}"/> keeps its nodes in
    /// <c>Arena&lt;ArenaList&lt;T&gt;&gt;</c> and its values in
    /// <c>Arena&lt;T&gt;</c>; saving one without the other produces a tree whose
    /// value lists point at nothing. <see cref="AddTree"/> adds both.
    /// </para>
    /// <para>
    /// Writing:
    /// </para>
    /// <code>
    /// var snapshot = Snapshot.Create();
    /// snapshot.AddSet("index", index);
    /// using var file = File.Create("checkpoint.hf");
    /// snapshot.SaveTo(file);
    /// </code>
    /// <para>
    /// Reading, in another process:
    /// </para>
    /// <code>
    /// using var file = File.OpenRead("checkpoint.hf");
    /// var snapshot = Snapshot.LoadFrom(file);
    /// snapshot.Activate();
    /// var index = snapshot.GetSet&lt;long, LongComparer&gt;("index");
    /// </code>
    /// <para>
    /// <see cref="Activate"/> replaces the state of every static arena the file
    /// covers, which INVALIDATES every handle into those arenas that the process
    /// was already holding. Nothing here is thread-safe.
    /// </para>
    /// </remarks>
    public sealed class Snapshot
    {
        private readonly List<ArenaSection> sections = new List<ArenaSection>();
        private readonly Dictionary<string, int> sectionByType = new Dictionary<string, int>(StringComparer.Ordinal);

        private readonly List<RootEntry> roots = new List<RootEntry>();
        private readonly Dictionary<string, int> rootByName = new Dictionary<string, int>(StringComparer.Ordinal);

        private bool fromStream;
        private bool activated;

        private Snapshot() { }

        /// <summary>An empty snapshot, ready to have arenas and roots added.</summary>
        /// <returns>A new snapshot.</returns>
        public static Snapshot Create() => new Snapshot();

        /// <summary>The names of the roots, in the order they were added.</summary>
        public IReadOnlyList<string> RootNames
        {
            get
            {
                var names = new string[roots.Count];
                for (int i = 0; i < roots.Count; i++) names[i] = roots[i].Name;
                return names;
            }
        }

        /// <summary>How many arenas the snapshot covers.</summary>
        public int ArenaCount => sections.Count;

        // =====================================================================
        //  Type registry
        // =====================================================================

        private static readonly object registryLock = new object();
        private static readonly Dictionary<string, Func<ArenaSection>> registry =
            new Dictionary<string, Func<ArenaSection>>(StringComparer.Ordinal);

        /// <summary>
        /// Declares that snapshots in this process may contain arenas of
        /// <typeparamref name="T"/>, so loading one does not have to find the
        /// type by name.
        /// </summary>
        /// <remarks>
        /// Optional. Without it <see cref="LoadFrom(Stream)"/> resolves the
        /// recorded type name through reflection, which works for anything the
        /// process can already see. Register when the payload lives in an
        /// assembly that is not loaded yet, or when you would rather not rely on
        /// a name in a file at all.
        /// </remarks>
        /// <typeparam name="T">The payload type.</typeparam>
        public static void Register<T>() where T : struct
        {
            string key = TypeNames.Normalize(TypeNames.Of(typeof(T)));
            lock (registryLock)
            {
                registry[key] = static () => new TypedArenaSection<T>();
            }
        }

        /// <summary>
        /// Empties the registry, so the next load has to find its types by
        /// name. Exists so the tests can exercise the path a fresh process
        /// takes without starting one.
        /// </summary>
        internal static void ClearRegistry()
        {
            lock (registryLock)
            {
                registry.Clear();
            }
        }

        // =====================================================================
        //  Building a snapshot
        // =====================================================================

        /// <summary>
        /// Includes the current state of <c>Arena&lt;T&gt;</c>. Adding the same
        /// arena twice does nothing the second time.
        /// </summary>
        /// <typeparam name="T">The arena's payload type.</typeparam>
        /// <returns>This snapshot, so calls can be chained.</returns>
        public Snapshot AddArena<T>() where T : struct
        {
            RequireBuildable();
            Register<T>();

            string key = TypeNames.Normalize(TypeNames.Of(typeof(T)));
            if (sectionByType.ContainsKey(key)) return this;

            var section = new TypedArenaSection<T>();
            section.CaptureFromArena();

            sectionByType.Add(key, sections.Count);
            sections.Add(section);
            return this;
        }

        /// <summary>
        /// Records a bare handle under a name, and includes its arena.
        /// </summary>
        /// <typeparam name="T">The arena's payload type.</typeparam>
        /// <typeparam name="TMode">The handle's mode.</typeparam>
        /// <param name="name">The name to store it under.</param>
        /// <param name="handle">The handle to record.</param>
        /// <returns>This snapshot, so calls can be chained.</returns>
        public Snapshot AddHandle<T, TMode>(string name, Handle<T, TMode> handle)
            where T : struct
            where TMode : struct, IHandleMode
        {
            AddArena<T>();
            AddRoot(name, RootKind.Handle, typeof(T), handle.Raw, 0, 0);
            return this;
        }

        /// <summary>
        /// Records a list under a name, and includes the arena its nodes live
        /// in.
        /// </summary>
        /// <typeparam name="T">The list's element type.</typeparam>
        /// <param name="name">The name to store it under.</param>
        /// <param name="list">The list to record.</param>
        /// <returns>This snapshot, so calls can be chained.</returns>
        public Snapshot AddList<T>(string name, in ArenaList<T> list) where T : struct
        {
            AddArena<T>();
            list.CaptureState(out long head, out long tail, out long count);
            AddRoot(name, RootKind.List, typeof(T), head, tail, count);
            return this;
        }

        /// <summary>
        /// Records a set under a name, and includes the arena its nodes live
        /// in.
        /// </summary>
        /// <typeparam name="T">The set's key type.</typeparam>
        /// <typeparam name="TComp">The comparer type. Not stored: comparers are a compile-time choice and are supplied again on load.</typeparam>
        /// <param name="name">The name to store it under.</param>
        /// <param name="set">The set to record.</param>
        /// <returns>This snapshot, so calls can be chained.</returns>
        public Snapshot AddSet<T, TComp>(string name, in RedBlackSet<T, TComp> set)
            where T : struct
            where TComp : struct, IArenaComparer<T>
        {
            AddArena<T>();
            set.CaptureState(out long root, out long count);
            AddRoot(name, RootKind.Set, typeof(T), root, count, 0);
            return this;
        }

        /// <summary>
        /// Records a tree under a name, and includes BOTH arenas it spans: the
        /// nodes in <c>Arena&lt;ArenaList&lt;T&gt;&gt;</c> and the values in
        /// <c>Arena&lt;T&gt;</c>.
        /// </summary>
        /// <typeparam name="T">The tree's value type.</typeparam>
        /// <typeparam name="TComp">The comparer type. Not stored.</typeparam>
        /// <param name="name">The name to store it under.</param>
        /// <param name="tree">The tree to record.</param>
        /// <returns>This snapshot, so calls can be chained.</returns>
        public Snapshot AddTree<T, TComp>(string name, in RedBlackTree<T, TComp> tree)
            where T : struct
            where TComp : struct, IArenaComparer<T>
        {
            AddArena<ArenaList<T>>();
            AddArena<T>();

            tree.CaptureState(out long root, out long nodes, out long values);
            AddRoot(name, RootKind.Tree, typeof(ArenaList<T>), root, nodes, values);
            return this;
        }

        private void AddRoot(string name, RootKind kind, Type arenaType, long a, long b, long c)
        {
            RequireBuildable();
            if (name is null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0) throw new ArgumentException("a root needs a name.", nameof(name));
            if (rootByName.ContainsKey(name))
                throw new ArgumentException($"the snapshot already has a root named '{name}'.", nameof(name));

            var entry = new RootEntry
            {
                Name = name,
                Kind = kind,
                PayloadTypeName = TypeNames.Of(arenaType),
                A = a,
                B = b,
                C = c,
            };

            rootByName.Add(name, roots.Count);
            roots.Add(entry);
        }

        private void RequireBuildable()
        {
            if (fromStream)
                throw new InvalidOperationException(
                    "this snapshot was read from a stream; build a new one with Snapshot.Create() to write.");
        }

        // =====================================================================
        //  Writing
        // =====================================================================

        /// <summary>Writes the snapshot with the default options.</summary>
        /// <param name="stream">Where to write. Written sequentially; never seeks.</param>
        public void SaveTo(Stream stream) => SaveTo(stream, SnapshotOptions.Default);

        /// <summary>Writes the snapshot.</summary>
        /// <param name="stream">Where to write. Written sequentially; never seeks.</param>
        /// <param name="options">What to do while writing.</param>
        public void SaveTo(Stream stream, SnapshotOptions options)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (!stream.CanWrite) throw new ArgumentException("the stream is not writable.", nameof(stream));
            RequireBuildable();

            foreach (RootEntry root in roots)
            {
                string key = TypeNames.Normalize(root.PayloadTypeName);
                if (!sectionByType.ContainsKey(key))
                    throw new InvalidOperationException(
                        $"root '{root.Name}' points into an arena of {root.PayloadTypeName}, which the snapshot does not include.");
            }

            byte flags = options.ScrubFreeNodes ? SnapshotFormat.FlagScrubbed : (byte)0;

            stream.Write(SnapshotFormat.Magic);
            SnapshotFormat.WriteInt32(stream, SnapshotFormat.Version);
            SnapshotFormat.WriteByte(stream, SnapshotFormat.HostByteOrder);
            SnapshotFormat.WriteByte(stream, flags);
            SnapshotFormat.WriteByte(stream, 0);
            SnapshotFormat.WriteByte(stream, 0);
            SnapshotFormat.WriteInt32(stream, Layout.OffsetBits);
            SnapshotFormat.WriteInt32(stream, Layout.AddressBits);
            SnapshotFormat.WriteInt32(stream, sections.Count);
            SnapshotFormat.WriteInt32(stream, roots.Count);

            foreach (RootEntry root in roots) root.Write(stream);

            foreach (ArenaSection section in sections)
            {
                section.WriteDescriptor(stream);
                section.WritePayload(stream, options.ScrubFreeNodes);
            }

            stream.Flush();
        }

        // =====================================================================
        //  Reading
        // =====================================================================

        /// <summary>
        /// Reads a snapshot. The arenas are held in the returned object until
        /// <see cref="Activate"/> installs them.
        /// </summary>
        /// <param name="stream">The stream to read. Read sequentially; never seeks.</param>
        /// <returns>The snapshot the stream held.</returns>
        public static Snapshot LoadFrom(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("the stream is not readable.", nameof(stream));

            var snapshot = new Snapshot { fromStream = true };

            Span<byte> magic = stackalloc byte[8];
            SnapshotFormat.ReadExactly(stream, magic);
            if (!magic.SequenceEqual(SnapshotFormat.Magic))
                throw new SnapshotFormatException("this stream does not start with the Holdfast magic; it is not a snapshot.");

            int version = SnapshotFormat.ReadInt32(stream);
            if (version != SnapshotFormat.Version)
                throw new SnapshotFormatException(
                    $"the snapshot is format version {version}; this build reads version {SnapshotFormat.Version}.");

            byte byteOrder = SnapshotFormat.ReadByte(stream);
            if (byteOrder != SnapshotFormat.HostByteOrder)
                throw new SnapshotFormatException(
                    "the snapshot was written on a machine of the opposite byte order. The blocks are a verbatim copy " +
                    "of memory, so they cannot be swapped without knowing the layout of the payload type.");

            _ = SnapshotFormat.ReadByte(stream);  // flags: informational, both settings load the same
            _ = SnapshotFormat.ReadByte(stream);
            _ = SnapshotFormat.ReadByte(stream);

            int offsetBits = SnapshotFormat.ReadInt32(stream);
            int addressBits = SnapshotFormat.ReadInt32(stream);
            if (offsetBits != Layout.OffsetBits || addressBits != Layout.AddressBits)
                throw new SnapshotFormatException(
                    $"the snapshot was written with {offsetBits}/{addressBits} offset/address bits; " +
                    $"this build uses {Layout.OffsetBits}/{Layout.AddressBits}. Every address in the file means something else.");

            int arenaCount = SnapshotFormat.ReadInt32(stream);
            int rootCount = SnapshotFormat.ReadInt32(stream);

            if (arenaCount < 0 || rootCount < 0)
                throw new SnapshotFormatException("the snapshot header holds a negative count. The file is corrupt.");

            for (int i = 0; i < rootCount; i++)
            {
                RootEntry entry = RootEntry.Read(stream);
                if (snapshot.rootByName.ContainsKey(entry.Name))
                    throw new SnapshotFormatException($"the snapshot holds two roots named '{entry.Name}'.");

                snapshot.rootByName.Add(entry.Name, snapshot.roots.Count);
                snapshot.roots.Add(entry);
            }

            for (int i = 0; i < arenaCount; i++)
            {
                string typeName = SnapshotFormat.ReadString(stream);
                ArenaSection section = CreateSection(typeName);

                section.ReadDescriptorTail(stream, typeName);
                section.ValidateDescriptor();
                section.ReadPayload(stream);

                string key = TypeNames.Normalize(typeName);
                if (snapshot.sectionByType.ContainsKey(key))
                    throw new SnapshotFormatException($"the snapshot holds two arenas of {typeName}.");

                snapshot.sectionByType.Add(key, snapshot.sections.Count);
                snapshot.sections.Add(section);
            }

            snapshot.ValidateRoots();
            return snapshot;
        }

        private static ArenaSection CreateSection(string typeName)
        {
            string key = TypeNames.Normalize(typeName);

            Func<ArenaSection>? factory;
            lock (registryLock)
            {
                registry.TryGetValue(key, out factory);
            }
            if (factory is not null) return factory();

            Type? type = TypeNames.Resolve(typeName)
                ?? throw new SnapshotFormatException(
                    $"the snapshot holds an arena of '{typeName}', which this process cannot find. " +
                    "Call Snapshot.Register<T>() for it before loading, or make sure its assembly is loaded.");

            if (!type.IsValueType || type.ContainsGenericParameters)
                throw new SnapshotFormatException(
                    $"the snapshot names '{typeName}' as an arena payload, but it is not a closed value type.");

            try
            {
                var section = (ArenaSection?)Activator.CreateInstance(
                    typeof(TypedArenaSection<>).MakeGenericType(type));

                return section ?? throw new SnapshotFormatException($"could not build a section for '{typeName}'.");
            }
            catch (Exception e) when (e is ArgumentException or TypeLoadException or MissingMethodException)
            {
                throw new SnapshotFormatException($"could not build a section for '{typeName}'.", e);
            }
        }

        /// <summary>
        /// Checks that every root address falls inside the arena it belongs to.
        /// A corrupt root would otherwise be read as a perfectly ordinary node.
        /// </summary>
        private void ValidateRoots()
        {
            foreach (RootEntry root in roots)
            {
                string key = TypeNames.Normalize(root.PayloadTypeName);
                if (!sectionByType.TryGetValue(key, out int index))
                    throw new SnapshotFormatException(
                        $"root '{root.Name}' points into an arena of {root.PayloadTypeName}, which is not in the file.");

                long capacity = sections[index].Capacity;

                CheckAddress(root, root.A, capacity);
                if (root.Kind == RootKind.List) CheckAddress(root, root.B, capacity);

                long count = root.Kind switch
                {
                    RootKind.Set => root.B,
                    RootKind.List => root.C,
                    RootKind.Tree => root.B,
                    _ => 0,
                };

                if (count < 0 || count > capacity)
                    throw new SnapshotFormatException(
                        $"root '{root.Name}' claims {count} nodes; the arena holds {capacity}.");
            }
        }

        private static void CheckAddress(RootEntry root, long address, long capacity)
        {
            if (address == Layout.ListNull || address == Layout.TreeNull) return;
            if (address >= 0 && address < capacity) return;

            throw new SnapshotFormatException(
                $"root '{root.Name}' points at address {address}, which is outside the {capacity} nodes of its arena.");
        }

        // =====================================================================
        //  Installing and reading back
        // =====================================================================

        /// <summary>
        /// Makes every arena in the snapshot the state of its static
        /// <see cref="Arena{T}"/>.
        /// </summary>
        /// <remarks>
        /// This INVALIDATES every handle the process holds into those arenas.
        /// It does not touch arenas the snapshot does not mention.
        /// </remarks>
        public void Activate()
        {
            foreach (ArenaSection section in sections) section.Activate();
            activated = true;
        }

        /// <summary>Recovers a handle stored under a name.</summary>
        /// <typeparam name="T">The arena's payload type.</typeparam>
        /// <typeparam name="TMode">The mode to read it as.</typeparam>
        /// <param name="name">The name it was stored under.</param>
        /// <returns>The handle.</returns>
        public Handle<T, TMode> GetHandle<T, TMode>(string name)
            where T : struct
            where TMode : struct, IHandleMode
        {
            RootEntry root = Root(name, RootKind.Handle, typeof(T));
            return new Handle<T, TMode>(root.A);
        }

        /// <summary>Recovers a list stored under a name.</summary>
        /// <typeparam name="T">The list's element type.</typeparam>
        /// <param name="name">The name it was stored under.</param>
        /// <returns>The list, in the same state it was saved in.</returns>
        public ArenaList<T> GetList<T>(string name) where T : struct
        {
            RootEntry root = Root(name, RootKind.List, typeof(T));
            return ArenaList<T>.FromState(root.A, root.B, root.C);
        }

        /// <summary>Recovers a set stored under a name, with a default comparer.</summary>
        /// <typeparam name="T">The set's key type.</typeparam>
        /// <typeparam name="TComp">The comparer type.</typeparam>
        /// <param name="name">The name it was stored under.</param>
        /// <returns>The set, in the same state it was saved in.</returns>
        public RedBlackSet<T, TComp> GetSet<T, TComp>(string name)
            where T : struct
            where TComp : struct, IArenaComparer<T>
            => GetSet<T, TComp>(name, default(TComp));

        /// <summary>Recovers a set stored under a name.</summary>
        /// <typeparam name="T">The set's key type.</typeparam>
        /// <typeparam name="TComp">The comparer type.</typeparam>
        /// <param name="name">The name it was stored under.</param>
        /// <param name="comparer">The comparer to give the set. It must order keys the same way the saved one did.</param>
        /// <returns>The set, in the same state it was saved in.</returns>
        public RedBlackSet<T, TComp> GetSet<T, TComp>(string name, TComp comparer)
            where T : struct
            where TComp : struct, IArenaComparer<T>
        {
            RootEntry root = Root(name, RootKind.Set, typeof(T));
            return RedBlackSet<T, TComp>.FromState(root.A, root.B, comparer);
        }

        /// <summary>Recovers a tree stored under a name, with a default comparer.</summary>
        /// <typeparam name="T">The tree's value type.</typeparam>
        /// <typeparam name="TComp">The comparer type.</typeparam>
        /// <param name="name">The name it was stored under.</param>
        /// <returns>The tree, in the same state it was saved in.</returns>
        public RedBlackTree<T, TComp> GetTree<T, TComp>(string name)
            where T : struct
            where TComp : struct, IArenaComparer<T>
            => GetTree<T, TComp>(name, default(TComp));

        /// <summary>Recovers a tree stored under a name.</summary>
        /// <typeparam name="T">The tree's value type.</typeparam>
        /// <typeparam name="TComp">The comparer type.</typeparam>
        /// <param name="name">The name it was stored under.</param>
        /// <param name="comparer">The comparer to give the tree. It must order keys the same way the saved one did.</param>
        /// <returns>The tree, in the same state it was saved in.</returns>
        public RedBlackTree<T, TComp> GetTree<T, TComp>(string name, TComp comparer)
            where T : struct
            where TComp : struct, IArenaComparer<T>
        {
            RootEntry root = Root(name, RootKind.Tree, typeof(ArenaList<T>));
            return RedBlackTree<T, TComp>.FromState(root.A, root.B, root.C, comparer);
        }

        private RootEntry Root(string name, RootKind kind, Type arenaType)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            if (fromStream && !activated)
                throw new InvalidOperationException(
                    "call Activate() before reading roots: a root is an address, and it means nothing until " +
                    "the snapshot's blocks are the arena's blocks.");

            if (!rootByName.TryGetValue(name, out int index))
                throw new KeyNotFoundException($"the snapshot has no root named '{name}'.");

            RootEntry root = roots[index];

            if (root.Kind != kind)
                throw new InvalidOperationException(
                    $"root '{name}' was stored as a {root.Kind}, not as a {kind}.");

            string expected = TypeNames.Normalize(TypeNames.Of(arenaType));
            string stored = TypeNames.Normalize(root.PayloadTypeName);

            if (!string.Equals(expected, stored, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"root '{name}' holds nodes of {stored}, and it is being read as {expected}.");

            return root;
        }
    }
}
