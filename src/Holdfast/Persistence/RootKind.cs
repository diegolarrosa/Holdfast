namespace Holdfast
{
    /// <summary>
    /// What a named root in a snapshot refers to.
    /// </summary>
    /// <remarks>
    /// The arena file holds nodes; a root is the entry point back into them.
    /// The kind is recorded so that asking for a set by a name that was stored
    /// as a list fails with a message instead of reinterpreting the addresses.
    /// </remarks>
    public enum RootKind : byte
    {
        /// <summary>No root. Never written to a file.</summary>
        None = 0,

        /// <summary>A bare <see cref="Handle{T, TMode}"/>.</summary>
        Handle = 1,

        /// <summary>An <see cref="ArenaList{T}"/>: head, tail and count.</summary>
        List = 2,

        /// <summary>A <see cref="RedBlackSet{T, TComp}"/>: root and count.</summary>
        Set = 3,

        /// <summary>A <see cref="RedBlackTree{T, TComp}"/>: root, nodes and values.</summary>
        Tree = 4,
    }
}
