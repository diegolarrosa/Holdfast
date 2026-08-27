using System;
using System.IO;

namespace Holdfast
{
    /// <summary>
    /// One arena inside a snapshot: a descriptor plus the verbatim bytes of its
    /// blocks.
    /// </summary>
    /// <remarks>
    /// Non-generic on purpose. A snapshot holds several arenas of different
    /// payload types — a <see cref="RedBlackTree{T, TComp}"/> alone spans two —
    /// so the container needs to hold them in one list. The generic work
    /// happens in <see cref="TypedArenaSection{T}"/>.
    /// </remarks>
    internal abstract class ArenaSection
    {
        internal string TypeName = string.Empty;
        internal int NodeSize;
        internal int BlockSize;
        internal long BlockCount;
        internal long FreeHead;
        internal long Allocated;
        internal long Available;
        internal long TotalAllocations;

        /// <summary>The closed payload type this section stores.</summary>
        internal abstract Type PayloadType { get; }

        /// <summary>Highest address this section can hold, exclusive.</summary>
        internal long Capacity => BlockCount * BlockSize;

        /// <summary>Reads the descriptor fields from the live arena.</summary>
        internal abstract void CaptureFromArena();

        /// <summary>Writes the block bytes.</summary>
        internal abstract void WritePayload(Stream stream, bool scrubFreeNodes);

        /// <summary>Reads the block bytes into fresh blocks.</summary>
        internal abstract void ReadPayload(Stream stream);

        /// <summary>Makes what was read the state of the static arena.</summary>
        internal abstract void Activate();

        /// <summary>
        /// Checks the descriptor against the running build before a single
        /// payload byte is read.
        /// </summary>
        internal abstract void ValidateDescriptor();

        internal void WriteDescriptor(Stream stream)
        {
            SnapshotFormat.WriteString(stream, TypeName);
            SnapshotFormat.WriteInt32(stream, NodeSize);
            SnapshotFormat.WriteInt32(stream, BlockSize);
            SnapshotFormat.WriteInt64(stream, BlockCount);
            SnapshotFormat.WriteInt64(stream, FreeHead);
            SnapshotFormat.WriteInt64(stream, Allocated);
            SnapshotFormat.WriteInt64(stream, Available);
            SnapshotFormat.WriteInt64(stream, TotalAllocations);
        }

        internal void ReadDescriptorTail(Stream stream, string typeName)
        {
            TypeName = typeName;
            NodeSize = SnapshotFormat.ReadInt32(stream);
            BlockSize = SnapshotFormat.ReadInt32(stream);
            BlockCount = SnapshotFormat.ReadInt64(stream);
            FreeHead = SnapshotFormat.ReadInt64(stream);
            Allocated = SnapshotFormat.ReadInt64(stream);
            Available = SnapshotFormat.ReadInt64(stream);
            TotalAllocations = SnapshotFormat.ReadInt64(stream);
        }
    }
}
