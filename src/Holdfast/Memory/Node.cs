using System.Runtime.CompilerServices;

namespace Holdfast
{
    /// <summary>
    /// One slot of an <see cref="Arena{T}"/>: a payload plus two 64-bit link
    /// words.
    /// </summary>
    /// <remarks>
    /// The link words are interpreted differently depending on the node's mode.
    /// In list mode <see cref="Next"/> and <see cref="Previous"/> are raw
    /// addresses. In tree mode the same bits carry <see cref="Left"/>,
    /// <see cref="Right"/>, <see cref="Parent"/> and <see cref="Color"/>, so a
    /// tree node costs the same as a list node.
    /// <para>
    /// Reading a node through the wrong view yields garbage that looks like a
    /// valid address. That is exactly what the mode in
    /// <see cref="Handle{T, TMode}"/> exists to prevent.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The payload type stored in the node.</typeparam>
    public struct Node<T> where T : struct
    {
        /// <summary>The user payload. Independent of the node's mode.</summary>
        public T Value;

        /// <summary>Link word 0. List mode: next. Tree mode: left + parent high.</summary>
        public long Next;

        /// <summary>Link word 1. List mode: previous. Tree mode: right + parent low + color.</summary>
        public long Previous;

        /// <summary>
        /// Red-black color, packed into bit 62 of the second link word.
        /// True is red, false is black. Only meaningful in tree mode.
        /// </summary>
        public bool Color
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => (Previous & Layout.ColorBit) != 0;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Previous = value ? Previous | Layout.ColorBit : Previous & ~Layout.ColorBit;
        }

        /// <summary>
        /// The parent address, split across both words: the low 20 bits live in
        /// word 1 and the high 22 in word 0. Only meaningful in tree mode.
        /// </summary>
        public long Parent
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get
            {
                long low = (long)(((ulong)Previous >> Layout.ParentShift) & (ulong)Layout.ParentLowMask);
                long high = (long)(((ulong)Next >> Layout.ParentShift) & (ulong)Layout.ParentHighMask);
                return low | (high << Layout.ParentLowBits);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                Previous = (Previous & ~(Layout.ParentLowMask << Layout.ParentShift))
                         | ((value & Layout.ParentLowMask) << Layout.ParentShift);
                Next = (long)(((ulong)Next & ~((ulong)Layout.ParentHighMask << Layout.ParentShift))
                     | ((((ulong)value >> Layout.ParentLowBits) & (ulong)Layout.ParentHighMask) << Layout.ParentShift));
            }
        }

        /// <summary>
        /// The left child address, in the low 42 bits of word 0. Only
        /// meaningful in tree mode.
        /// </summary>
        public long Left
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => Next & Layout.AddressMask;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Next = (Next & ~Layout.AddressMask) | (value & Layout.AddressMask);
        }

        /// <summary>
        /// The right child address, in the low 42 bits of word 1. Only
        /// meaningful in tree mode.
        /// </summary>
        public long Right
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            readonly get => Previous & Layout.AddressMask;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Previous = (Previous & ~Layout.AddressMask) | (value & Layout.AddressMask);
        }
    }
}
