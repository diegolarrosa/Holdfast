using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Holdfast
{
    /// <summary>
    /// The typed half of <see cref="ArenaSection"/>: everything that needs to
    /// know what <typeparamref name="T"/> is.
    /// </summary>
    /// <typeparam name="T">The arena's payload type.</typeparam>
    internal sealed class TypedArenaSection<T> : ArenaSection where T : struct
    {
        private ArenaState<T>? captured;
        private Node<T>[][]? loaded;

        internal override Type PayloadType => typeof(T);

        // -------------------------------------------------------------------
        //  Saving
        // -------------------------------------------------------------------

        internal override void CaptureFromArena()
        {
            RequireBlittable();

            captured = Arena<T>.Capture();

            TypeName = TypeNames.Of(typeof(T));
            NodeSize = Unsafe.SizeOf<Node<T>>();
            BlockSize = Layout.BlockSize;
            BlockCount = captured.BlockCountValue;
            FreeHead = captured.Free;
            Allocated = captured.AllocatedValue;
            Available = captured.AvailableValue;
            TotalAllocations = captured.TotalAllocationsValue;
        }

        internal override void WritePayload(Stream stream, bool scrubFreeNodes)
        {
            ArenaState<T> state = captured
                ?? throw new InvalidOperationException("the section was never captured from an arena.");

            if (scrubFreeNodes) Scrub(state);

            for (int i = 0; i < BlockCount; i++)
            {
                Node<T>[] block = state.Blocks[i];
                stream.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<Node<T>>(block)));
            }
        }

        /// <summary>
        /// Zeroes the payload of every node on the free list, then puts the
        /// link back.
        /// </summary>
        /// <remarks>
        /// Blocks are allocated uninitialized — the arena writes the two link
        /// words of every slot and leaves the payload alone, because a slot on
        /// the free list has no payload worth writing. That is free at run time
        /// and not free on disk: it would put whatever those pages held before
        /// into the file. Zeroing them also makes two snapshots of the same
        /// arena byte-identical, which is what lets you diff or hash one.
        /// </remarks>
        private void Scrub(ArenaState<T> state)
        {
            long address = state.Free;

            while (address >= 0)
            {
                ref Node<T> node = ref state.Blocks[Layout.Block(address)][Layout.Offset(address)];
                long next = node.Next;

                node = default;
                node.Next = next;
                node.Previous = Layout.ListNull;

                address = next;
            }
        }

        // -------------------------------------------------------------------
        //  Loading
        // -------------------------------------------------------------------

        internal override void ValidateDescriptor()
        {
            RequireBlittable();

            int expectedNodeSize = Unsafe.SizeOf<Node<T>>();
            if (NodeSize != expectedNodeSize)
                throw new SnapshotFormatException(
                    $"the snapshot stores {typeof(T)} in nodes of {NodeSize} bytes; this build uses {expectedNodeSize}. " +
                    "The payload type changed since the snapshot was written.");

            if (BlockSize != Layout.BlockSize)
                throw new SnapshotFormatException(
                    $"the snapshot uses blocks of {BlockSize} nodes; this build uses {Layout.BlockSize}.");

            if (BlockCount < 0 || BlockCount > Layout.MaxBlocks)
                throw new SnapshotFormatException(
                    $"the snapshot claims {BlockCount} blocks; the addressable maximum is {Layout.MaxBlocks}.");

            long capacity = Capacity;

            if (Allocated < 0 || Available < 0 || Allocated + Available != capacity)
                throw new SnapshotFormatException(
                    $"the counters of {typeof(T)} do not add up: {Allocated} allocated + {Available} available != {capacity} capacity.");

            if (FreeHead != Layout.ListNull && (FreeHead < 0 || FreeHead >= capacity))
                throw new SnapshotFormatException(
                    $"the free list of {typeof(T)} starts at {FreeHead}, which is outside the {capacity} nodes in the file.");
        }

        internal override void ReadPayload(Stream stream)
        {
            int capacity = 16;
            while (capacity < BlockCount) capacity *= 2;

            var blocks = new Node<T>[capacity][];

            for (int i = 0; i < BlockCount; i++)
            {
                // Uninitialized: the read below writes every byte.
                var block = GC.AllocateUninitializedArray<Node<T>>(
                    Layout.BlockSize, pinned: Arena<T>.ReferenceFree);

                SnapshotFormat.ReadExactly(stream, MemoryMarshal.AsBytes(new Span<Node<T>>(block)));
                blocks[i] = block;
            }

            loaded = blocks;
        }

        internal override void Activate()
        {
            Node<T>[][] blocks = loaded
                ?? throw new InvalidOperationException("the section was never read from a stream.");

            Arena<T>.InstallBlocks(
                blocks, (int)BlockCount, FreeHead, Allocated, Available, TotalAllocations);
        }

        // -------------------------------------------------------------------

        /// <summary>
        /// A snapshot is a copy of memory. A node holding a reference would
        /// write a pointer into the file, and a pointer means nothing to the
        /// process that reads it back.
        /// </summary>
        private static void RequireBlittable()
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<Node<T>>())
                throw new NotSupportedException(
                    $"{typeof(T)} holds references, so an arena of it cannot be snapshotted. " +
                    "A snapshot is a verbatim copy of the blocks: references would be written as addresses " +
                    "of this process and would be meaningless anywhere else. Use a payload of unmanaged fields " +
                    "and keep anything by reference in a side table.");
        }
    }
}
