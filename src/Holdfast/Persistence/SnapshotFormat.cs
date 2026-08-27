using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Holdfast
{
    /// <summary>
    /// The on-disk format: magic, version, and the scalar primitives every
    /// section is written with.
    /// </summary>
    /// <remarks>
    /// Scalars are little-endian regardless of the machine, so a header can
    /// always be read. The node blocks are NOT: they are a verbatim copy of
    /// memory, which is the whole point. The header records the writer's byte
    /// order and the reader refuses a mismatch rather than swapping bytes it
    /// cannot interpret — it does not know where the fields of
    /// <c>T</c> begin.
    /// </remarks>
    internal static class SnapshotFormat
    {
        /// <summary>File magic: eight bytes, no BOM, no ambiguity.</summary>
        internal static ReadOnlySpan<byte> Magic => "HOLDFAST"u8;

        /// <summary>Format version. Bumped on any change that moves a byte.</summary>
        internal const int Version = 1;

        internal const byte LittleEndian = 0;
        internal const byte BigEndian = 1;

        /// <summary>Set when the payload bytes of free nodes were zeroed.</summary>
        internal const byte FlagScrubbed = 1 << 0;

        /// <summary>The byte order of the machine doing the writing.</summary>
        internal static byte HostByteOrder =>
            BitConverter.IsLittleEndian ? LittleEndian : BigEndian;

        /// <summary>Longest name accepted for a root, in UTF-8 bytes.</summary>
        internal const int MaxStringBytes = 4096;

        // -------------------------------------------------------------------
        //  Scalars
        // -------------------------------------------------------------------

        internal static void WriteByte(Stream stream, byte value) => stream.WriteByte(value);

        internal static byte ReadByte(Stream stream)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new SnapshotFormatException("the stream ends in the middle of the snapshot.");
            return (byte)value;
        }

        internal static void WriteInt32(Stream stream, int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        internal static int ReadInt32(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        internal static void WriteInt64(Stream stream, long value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        internal static long ReadInt64(Stream stream)
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            ReadExactly(stream, buffer);
            return BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }

        internal static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > MaxStringBytes)
                throw new ArgumentException(
                    $"the name is {bytes.Length} bytes; the limit is {MaxStringBytes}.", nameof(value));

            WriteInt32(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        internal static string ReadString(Stream stream)
        {
            int length = ReadInt32(stream);
            if (length < 0 || length > MaxStringBytes)
                throw new SnapshotFormatException(
                    $"a string in the snapshot claims to be {length} bytes; the limit is {MaxStringBytes}. The file is corrupt or is not a snapshot.");

            byte[] bytes = new byte[length];
            ReadExactly(stream, bytes.AsSpan());
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Fills the buffer or throws. A short read on a snapshot is not a
        /// partial result to work with, it is a truncated file.
        /// </summary>
        internal static void ReadExactly(Stream stream, Span<byte> buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer.Slice(read));
                if (n <= 0)
                    throw new SnapshotFormatException(
                        $"the stream ends {buffer.Length - read} bytes early. The snapshot is truncated.");
                read += n;
            }
        }
    }
}
