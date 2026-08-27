namespace Holdfast
{
    /// <summary>
    /// The complete state of an <see cref="Arena{T}"/>: its blocks, its free
    /// list and its counters, detached from the static storage.
    /// </summary>
    /// <remarks>
    /// <see cref="Arena{T}"/> keeps its state in static fields so the hot path
    /// costs nothing to reach it. This type is that same state as an object, so
    /// it can be handed around, written to a stream, and swapped back in.
    /// <para>
    /// <see cref="Arena{T}.Capture"/> does NOT copy the blocks: the state and
    /// the live arena share them, so a captured state keeps changing while the
    /// arena it came from is still the active one. Use
    /// <see cref="Arena{T}.Swap"/> when you need a state that stands still.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The payload type stored in the arena.</typeparam>
    public sealed class ArenaState<T> where T : struct
    {
        internal Node<T>[][] Blocks;
        internal int BlockCountValue;
        internal long Free;
        internal long AllocatedValue;
        internal long AvailableValue;
        internal long TotalAllocationsValue;

#if DEBUG
        internal byte[][] States;
#endif

        internal ArenaState(
            Node<T>[][] blocks,
            int blockCount,
            long free,
            long allocated,
            long available,
            long totalAllocations
#if DEBUG
            , byte[][] states
#endif
            )
        {
            Blocks = blocks;
            BlockCountValue = blockCount;
            Free = free;
            AllocatedValue = allocated;
            AvailableValue = available;
            TotalAllocationsValue = totalAllocations;
#if DEBUG
            States = states;
#endif
        }

        /// <summary>A state with no blocks: what a fresh arena looks like.</summary>
        public static ArenaState<T> Empty =>
            new ArenaState<T>(
                new Node<T>[16][],
                0,
                Layout.ListNull,
                0,
                0,
                0
#if DEBUG
                , new byte[16][]
#endif
                );

        /// <summary>Number of blocks this state holds.</summary>
        public long BlockCount => BlockCountValue;

        /// <summary>Nodes handed out and not yet freed.</summary>
        public long Allocated => AllocatedValue;

        /// <summary>Nodes sitting on the free list.</summary>
        public long Available => AvailableValue;

        /// <summary>Running total of allocations in this state.</summary>
        public long TotalAllocations => TotalAllocationsValue;
    }
}
