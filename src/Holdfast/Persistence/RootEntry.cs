using System.IO;

namespace Holdfast
{
    /// <summary>
    /// A named entry point back into the arenas: a handle, or the two or three
    /// words that make up a collection's state.
    /// </summary>
    /// <remarks>
    /// The nodes are in the arena sections. Without a root there is no way in:
    /// the addresses are all there, but which one is the root of the index is
    /// not something the bytes can say.
    /// </remarks>
    internal sealed class RootEntry
    {
        internal string Name = string.Empty;
        internal RootKind Kind;
        internal string PayloadTypeName = string.Empty;

        /// <summary>Set root, list head, or the handle itself.</summary>
        internal long A;

        /// <summary>Set count, list tail, or tree node count.</summary>
        internal long B;

        /// <summary>List count or tree value count. Unused otherwise.</summary>
        internal long C;

        internal void Write(Stream stream)
        {
            SnapshotFormat.WriteString(stream, Name);
            SnapshotFormat.WriteByte(stream, (byte)Kind);
            SnapshotFormat.WriteString(stream, PayloadTypeName);
            SnapshotFormat.WriteInt64(stream, A);
            SnapshotFormat.WriteInt64(stream, B);
            SnapshotFormat.WriteInt64(stream, C);
        }

        internal static RootEntry Read(Stream stream)
        {
            var entry = new RootEntry();
            entry.Name = SnapshotFormat.ReadString(stream);

            byte kind = SnapshotFormat.ReadByte(stream);
            if (kind is < (byte)RootKind.Handle or > (byte)RootKind.Tree)
                throw new SnapshotFormatException(
                    $"root '{entry.Name}' has kind {kind}, which this build does not know.");

            entry.Kind = (RootKind)kind;
            entry.PayloadTypeName = SnapshotFormat.ReadString(stream);
            entry.A = SnapshotFormat.ReadInt64(stream);
            entry.B = SnapshotFormat.ReadInt64(stream);
            entry.C = SnapshotFormat.ReadInt64(stream);
            return entry;
        }
    }
}
