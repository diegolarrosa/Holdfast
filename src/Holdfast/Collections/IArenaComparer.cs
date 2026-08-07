namespace Holdfast
{
    /// <summary>
    /// Ordering for the arena-backed trees.
    /// </summary>
    /// <remarks>
    /// Implement this on a <c>struct</c> and pass it as the tree's second type
    /// argument. Because the constraint is <c>struct</c>, the JIT specializes
    /// the tree for that comparer and inlines the comparison instead of
    /// dispatching through an interface.
    /// <para>
    /// The comparer is stored by value, so it may carry state.
    /// </para>
    /// </remarks>
    public interface IArenaComparer<T> where T : struct
    {
        /// <summary>
        /// Total order over values. <paramref name="a"/> is the incoming value,
        /// <paramref name="b"/> the one already in the tree.
        /// </summary>
        int Compare(in T a, in T b);

        /// <summary>
        /// Compares only the part of the value that identifies a node, for
        /// descendant lookups. Return <see cref="Compare"/> if unused.
        /// </summary>
        int CompareDescendant(in T a, in T b);
    }
}
