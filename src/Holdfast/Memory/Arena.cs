using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Holdfast
{
    /// <summary>
    /// Block-allocated storage for nodes of payload type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Nodes live in fixed blocks of <see cref="Layout.BlockSize"/> entries.
    /// Growing appends a block; existing blocks are never copied or moved, so
    /// handles and <c>ref</c>s into the arena survive growth. Freed nodes go on
    /// an intrusive free list and are reused before a new block is taken.
    /// <para>
    /// Storage is static per closed generic type: <c>Arena&lt;long&gt;</c> is one
    /// arena for the whole process. Nothing here is thread-safe.
    /// </para>
    /// </remarks>
    public static class Arena<T> where T : struct
    {
        // A flat jagged array rather than List<T[]>: one less indirection on
        // the hot path.
        private static Node<T>[][] blocks = new Node<T>[16][];
        private static int blockCount;

        /// <summary>Head of the free list.</summary>
        private static long free = Layout.ListNull;

        /// <summary>Nodes currently handed out and not yet freed.</summary>
        public static long Allocated;
        /// <summary>Nodes sitting on the free list, ready to be reused.</summary>
        public static long Available;
        /// <summary>Running total of allocations. Never goes down.</summary>
        public static long TotalAllocations;

        /// <summary>
        /// When the node holds no references the block can go on the Pinned
        /// Object Heap, which keeps it out of the Large Object Heap and its
        /// compaction policy. Resolved once per closed type.
        /// </summary>
        private static readonly bool referenceFree =
            !RuntimeHelpers.IsReferenceOrContainsReferences<Node<T>>();

#if DEBUG
        // 0 = freed, 1 = raw, 2 = list, 3 = tree. Debug only: catches
        // use-after-free and mode confusion at run time.
        private static byte[][] states = new byte[16][];
#endif

        /// <summary>Number of blocks the arena has grown to.</summary>
        public static long BlockCount => blockCount;

        /// <summary>Megabytes reserved, counting whole blocks.</summary>
        public static long ReservedMegabytes =>
            (long)Unsafe.SizeOf<Node<T>>() * blockCount * Layout.BlockSize / (1L << 20);

        // ---------------------------------------------------------------------
        //  Access — the only address arithmetic in the library
        // ---------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ref Node<T> Ref(long address)
        {
            Debug.Assert(address >= 0, "negative address (unchecked null?)");
            Debug.Assert(Layout.Block(address) < blockCount, "block out of range");

            ref Node<T>[] block = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(blocks), (nint)Layout.Block(address));
            return ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(block), (nint)Layout.Offset(address));
        }

        [Conditional("DEBUG")]
        private static void CheckMode(long address, byte expected)
        {
#if DEBUG
            byte actual = states[Layout.Block(address)][Layout.Offset(address)];
            Debug.Assert(actual != 0, $"use of freed node at {address}");
            Debug.Assert(actual == expected, $"wrong mode at {address}: is {actual}, expected {expected}");
#endif
        }

        [Conditional("DEBUG")]
        internal static void MarkMode(long address, byte mode)
        {
#if DEBUG
            states[Layout.Block(address)][Layout.Offset(address)] = mode;
#endif
        }

        // ---------------------------------------------------------------------
        //  Blocks
        // ---------------------------------------------------------------------

        private static void AddBlock()
        {
            if (blockCount >= Layout.MaxBlocks)
                throw new InvalidOperationException(
                    $"arena exhausted: {Layout.MaxBlocks} blocks is the maximum addressable with {Layout.AddressBits} bits.");

            if (blockCount == blocks.Length)
            {
                Array.Resize(ref blocks, blocks.Length * 2);
#if DEBUG
                Array.Resize(ref states, blocks.Length);
#endif
            }

            // Uninitialized: the loop below writes both link words of every
            // entry, so the runtime's zeroing would be wasted work — tens of
            // megabytes per block.
            var block = GC.AllocateUninitializedArray<Node<T>>(Layout.BlockSize, pinned: referenceFree);

            long baseAddress = Layout.Address(blockCount, 0);

            for (int i = 0; i < Layout.BlockSize - 1; i++)
            {
                block[i].Next = baseAddress + i + 1;
                block[i].Previous = Layout.ListNull;
            }
            block[Layout.BlockSize - 1].Next = free;          // chain onto the existing free list
            block[Layout.BlockSize - 1].Previous = Layout.ListNull;

            blocks[blockCount] = block;
#if DEBUG
            states[blockCount] = new byte[Layout.BlockSize];
#endif
            blockCount++;

            free = baseAddress;
            Available += Layout.BlockSize;
        }

        // ---------------------------------------------------------------------
        //  Allocation
        // ---------------------------------------------------------------------

        internal static long AllocateRaw()
        {
            if (free < 0) AddBlock();

            long address = free;
            ref Node<T> node = ref Ref(address);
            free = node.Next;

            node.Next = Layout.ListNull;
            node.Previous = 0L;
            node.Value = default;

            MarkMode(address, RawMode.Code);

            Allocated++;
            Available--;
            TotalAllocations++;
            return address;
        }

        internal static void FreeRaw(long address)
        {
            MarkMode(address, 0);
            Ref(address).Next = free;
            free = address;
            Allocated--;
            Available++;
        }

        /// <summary>
        /// Takes a node off the free list, growing the arena if it is empty.
        /// The node has no mode yet — pass it to <see cref="AsList"/> or
        /// <see cref="AsTree"/> before linking it into anything.
        /// </summary>
        /// <returns>A handle to a zeroed node.</returns>
        public static Handle<T, RawMode> Allocate() => new Handle<T, RawMode>(AllocateRaw());

        /// <summary>Gives a freshly allocated node list mode, with null links.</summary>
        public static Handle<T, ListMode> AsList(Handle<T, RawMode> handle)
        {
            ref Node<T> node = ref Ref(handle.Raw);
            node.Next = Layout.ListNull;
            node.Previous = Layout.ListNull;
            MarkMode(handle.Raw, ListMode.Code);
            return new Handle<T, ListMode>(handle.Raw);
        }

        /// <summary>Gives a freshly allocated node tree mode: null links, black.</summary>
        public static Handle<T, TreeMode> AsTree(Handle<T, RawMode> handle)
        {
            ref Node<T> node = ref Ref(handle.Raw);
            node.Next = 0L;
            node.Previous = 0L;
            node.Left = Layout.TreeNull;
            node.Right = Layout.TreeNull;
            node.Parent = Layout.TreeNull;
            node.Color = false;
            MarkMode(handle.Raw, TreeMode.Code);
            return new Handle<T, TreeMode>(handle.Raw);
        }

        /// <summary>
        /// Returns a node to the free list. Unlink it from its structure first;
        /// this does not touch its links.
        /// </summary>
        /// <typeparam name="TMode">The node's current mode.</typeparam>
        /// <param name="handle">The node to free.</param>
        public static void Free<TMode>(Handle<T, TMode> handle)
            where TMode : struct, IHandleMode
            => FreeRaw(handle.Raw);

        /// <summary>The payload by reference: no copy, writable in place.</summary>
        /// <typeparam name="TMode">The node's current mode.</typeparam>
        /// <param name="handle">The node to read from.</param>
        /// <returns>A reference to the payload stored in the node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T Value<TMode>(Handle<T, TMode> handle)
            where TMode : struct, IHandleMode
        {
            CheckMode(handle.Raw, TMode.Code);
            return ref Ref(handle.Raw).Value;
        }

        // ---------------------------------------------------------------------
        //  List view
        // ---------------------------------------------------------------------

        /// <summary>The next node in the list, or null at the end.</summary>
        /// <param name="handle">The node to read from.</param>
        /// <returns>The linked node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, ListMode> Next(Handle<T, ListMode> handle)
        {
            CheckMode(handle.Raw, ListMode.Code);
            return new Handle<T, ListMode>(Ref(handle.Raw).Next);
        }

        /// <summary>The previous node in the list, or null at the start.</summary>
        /// <param name="handle">The node to read from.</param>
        /// <returns>The linked node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, ListMode> Previous(Handle<T, ListMode> handle)
        {
            CheckMode(handle.Raw, ListMode.Code);
            return new Handle<T, ListMode>(Ref(handle.Raw).Previous);
        }

        // ---------------------------------------------------------------------
        //  Tree view
        // ---------------------------------------------------------------------

        /// <summary>The left child, or null.</summary>
        /// <param name="handle">The node to read from.</param>
        /// <returns>The linked node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, TreeMode> Left(Handle<T, TreeMode> handle)
        {
            CheckMode(handle.Raw, TreeMode.Code);
            return new Handle<T, TreeMode>(Ref(handle.Raw).Left);
        }

        /// <summary>The right child, or null.</summary>
        /// <param name="handle">The node to read from.</param>
        /// <returns>The linked node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, TreeMode> Right(Handle<T, TreeMode> handle)
        {
            CheckMode(handle.Raw, TreeMode.Code);
            return new Handle<T, TreeMode>(Ref(handle.Raw).Right);
        }

        /// <summary>The parent node, or null at the root.</summary>
        /// <param name="handle">The node to read from.</param>
        /// <returns>The linked node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, TreeMode> Parent(Handle<T, TreeMode> handle)
        {
            CheckMode(handle.Raw, TreeMode.Code);
            return new Handle<T, TreeMode>(Ref(handle.Raw).Parent);
        }

        /// <summary>Reads a tree node's color. True is red, false is black.</summary>
        /// <param name="handle">The node to read.</param>
        /// <returns>True when the node is red.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Color(Handle<T, TreeMode> handle) => Ref(handle.Raw).Color;

        /// <summary>Sets a tree node's left child.</summary>
        /// <param name="handle">The node to modify.</param>
        /// <param name="value">The new left child, or the null handle.</param>
        public static void SetLeft(Handle<T, TreeMode> handle, Handle<T, TreeMode> value) => Ref(handle.Raw).Left = value.Raw;
        /// <summary>Sets a tree node's right child.</summary>
        /// <param name="handle">The node to modify.</param>
        /// <param name="value">The new right child, or the null handle.</param>
        public static void SetRight(Handle<T, TreeMode> handle, Handle<T, TreeMode> value) => Ref(handle.Raw).Right = value.Raw;
        /// <summary>Sets a tree node's parent.</summary>
        /// <param name="handle">The node to modify.</param>
        /// <param name="value">The new parent, or the null handle.</param>
        public static void SetParent(Handle<T, TreeMode> handle, Handle<T, TreeMode> value) => Ref(handle.Raw).Parent = value.Raw;
        /// <summary>Sets a tree node's color. True is red, false is black.</summary>
        /// <param name="handle">The node to modify.</param>
        /// <param name="color">True for red, false for black.</param>
        public static void SetColor(Handle<T, TreeMode> handle, bool color) => Ref(handle.Raw).Color = color;

        // ---------------------------------------------------------------------
        //  Raw list linking. ArenaList<T> is the supported API; these are the
        //  primitives it is built from.
        // ---------------------------------------------------------------------

        internal static long LinkFirstRaw(long head, long node)
        {
            ref Node<T> n = ref Ref(node);
            n.Next = head;
            n.Previous = Layout.ListNull;
            if (head >= 0) Ref(head).Previous = node;
            return node;
        }

        internal static long LinkLastRaw(long tail, long node)
        {
            ref Node<T> n = ref Ref(node);
            n.Previous = tail;
            n.Next = Layout.ListNull;
            if (tail >= 0) Ref(tail).Next = node;
            return node;
        }

        internal static long LinkAfterRaw(long after, long node)
        {
            long following = Ref(after).Next;
            ref Node<T> n = ref Ref(node);
            n.Previous = after;
            n.Next = following;
            Ref(after).Next = node;
            if (following >= 0) Ref(following).Previous = node;
            return node;
        }

        internal static long UnlinkFirstRaw(long head)
        {
            long following = Ref(head).Next;
            if (following >= 0) Ref(following).Previous = Layout.ListNull;
            return following;
        }

        internal static long UnlinkLastRaw(long tail)
        {
            long preceding = Ref(tail).Previous;
            if (preceding >= 0) Ref(preceding).Next = Layout.ListNull;
            return preceding;
        }

        internal static long UnlinkRaw(long node)
        {
            ref Node<T> n = ref Ref(node);
            long following = n.Next;
            if (following >= 0)
            {
                long preceding = n.Previous;
                if (preceding >= 0)
                {
                    Ref(following).Previous = preceding;
                    Ref(preceding).Next = following;
                }
                else
                {
                    return UnlinkFirstRaw(node);
                }
            }
            else
            {
                return UnlinkLastRaw(node);
            }
            return following;
        }

        /// <summary>Links a node at the front of a list.</summary>
        /// <param name="head">The current first node, or the null handle for an empty list.</param>
        /// <param name="node">The node to link in.</param>
        /// <returns>The new first node.</returns>
        public static Handle<T, ListMode> AddFirst(Handle<T, ListMode> head, Handle<T, ListMode> node)
            => new Handle<T, ListMode>(LinkFirstRaw(head.Raw, node.Raw));

        /// <summary>Links a node at the back of a list.</summary>
        /// <param name="tail">The current last node, or the null handle for an empty list.</param>
        /// <param name="node">The node to link in.</param>
        /// <returns>The new last node.</returns>
        public static Handle<T, ListMode> AddLast(Handle<T, ListMode> tail, Handle<T, ListMode> node)
            => new Handle<T, ListMode>(LinkLastRaw(tail.Raw, node.Raw));

        /// <summary>Links a node immediately after another.</summary>
        /// <param name="after">The node to insert behind.</param>
        /// <param name="node">The node to link in.</param>
        /// <returns>The node that was linked in.</returns>
        public static Handle<T, ListMode> AddAfter(Handle<T, ListMode> after, Handle<T, ListMode> node)
            => new Handle<T, ListMode>(LinkAfterRaw(after.Raw, node.Raw));

        /// <summary>Unlinks the first node of a list. Does not free it.</summary>
        /// <param name="head">The current first node.</param>
        /// <returns>The new first node, or the null handle if the list is now empty.</returns>
        public static Handle<T, ListMode> RemoveFirst(Handle<T, ListMode> head)
            => new Handle<T, ListMode>(UnlinkFirstRaw(head.Raw));

        /// <summary>Unlinks the last node of a list. Does not free it.</summary>
        /// <param name="tail">The current last node.</param>
        /// <returns>The new last node, or the null handle if the list is now empty.</returns>
        public static Handle<T, ListMode> RemoveLast(Handle<T, ListMode> tail)
            => new Handle<T, ListMode>(UnlinkLastRaw(tail.Raw));

        /// <summary>Unlinks a node from the middle of a list. Does not free it.</summary>
        /// <param name="node">The node to unlink.</param>
        /// <returns>The node that followed it, or the null handle.</returns>
        public static Handle<T, ListMode> Unlink(Handle<T, ListMode> node)
            => new Handle<T, ListMode>(UnlinkRaw(node.Raw));

        // ---------------------------------------------------------------------
        //  Lifetime and diagnostics
        // ---------------------------------------------------------------------

        /// <summary>
        /// Drops every block and returns to the initial state. This INVALIDATES
        /// every handle issued so far. Intended for tests and for restarting a
        /// long computation without tearing down the process.
        /// </summary>
        public static void Reset()
        {
            blocks = new Node<T>[16][];
            blockCount = 0;
            free = Layout.ListNull;
            Allocated = 0;
            Available = 0;
            TotalAllocations = 0;
#if DEBUG
            states = new byte[16][];
#endif
        }

        /// <summary>
        /// Checks that the counters add up to the reserved capacity and that
        /// the free list is acyclic. Throws with a specific message otherwise.
        /// </summary>
        public static void ValidateIntegrity()
        {
            long capacity = (long)blockCount * Layout.BlockSize;

            if (Allocated + Available != capacity)
                throw new InvalidOperationException(
                    $"inconsistent counters: {Allocated} allocated + {Available} available != {capacity} capacity");

            long count = 0;
            for (long address = free; address >= 0; address = Ref(address).Next)
            {
                if (++count > capacity)
                    throw new InvalidOperationException("cycle in the free list");
            }

            if (count != Available)
                throw new InvalidOperationException(
                    $"the free list holds {count} nodes but Available reports {Available}");
        }
    }
}
