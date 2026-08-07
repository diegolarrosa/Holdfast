using System.Runtime.CompilerServices;

namespace Holdfast
{
    /// <summary>
    /// Every bit-level constant in the library lives here. Address arithmetic
    /// is written once, in <see cref="Arena{T}"/>, and derived from these.
    /// </summary>
    public static class Layout
    {
        /// <summary>Bits used for the offset within a block.</summary>
        public const int OffsetBits = 20;

        /// <summary>Nodes per block: 1,048,576.</summary>
        public const int BlockSize = 1 << OffsetBits;

        /// <summary>Mask that isolates the offset part of an address.</summary>
        public const long OffsetMask = BlockSize - 1;

        /// <summary>
        /// Usable width of an address. The tree imposes it: Left, Right and
        /// Parent are 42-bit fields packed into the node's two link words. No
        /// address may exceed this or the tree could not reference it.
        /// </summary>
        public const int AddressBits = 42;

        /// <summary>Mask that isolates a full address: 2^42 - 1.</summary>
        public const long AddressMask = (1L << AddressBits) - 1;

        /// <summary>
        /// Maximum addressable blocks: 4,194,304, or about 4.4e12 nodes.
        /// </summary>
        public const int MaxBlocks = 1 << (AddressBits - OffsetBits);

        /// <summary>The null address in list mode.</summary>
        public const long ListNull = -1L;

        /// <summary>
        /// The null address in tree mode: 2^42-1. Distinct from
        /// <see cref="ListNull"/> on purpose — the two modes never mix, and the
        /// handle type keeps them apart at compile time.
        /// </summary>
        public const long TreeNull = AddressMask;

        // Field layout inside a node's two 64-bit link words:
        //
        //   word 0 : [63..42] parent high (22 bits)   [41..0] left  (42 bits)
        //   word 1 : [63] unused  [62] color
        //            [61..42] parent low (20 bits)    [41..0] right (42 bits)

        internal const int ParentShift = 42;
        internal const int ParentLowBits = 20;
        internal const long ParentLowMask = (1L << ParentLowBits) - 1;
        internal const long ParentHighMask = (1L << (AddressBits - ParentLowBits)) - 1;
        internal const long ColorBit = 1L << 62;

        /// <summary>Packs a block index and an in-block offset into an address.</summary>
        /// <param name="block">Zero-based block index, below <see cref="MaxBlocks"/>.</param>
        /// <param name="offset">Offset within the block, below <see cref="BlockSize"/>.</param>
        /// <returns>The packed address.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Address(int block, int offset) => ((long)block << OffsetBits) | (uint)offset;

        /// <summary>Extracts the block index from an address.</summary>
        /// <param name="address">A packed address.</param>
        /// <returns>The zero-based block index.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Block(long address) => (int)(address >> OffsetBits);

        /// <summary>Extracts the in-block offset from an address.</summary>
        /// <param name="address">A packed address.</param>
        /// <returns>The offset within its block.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Offset(long address) => (int)(address & OffsetMask);
    }
}
