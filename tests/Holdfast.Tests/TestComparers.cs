namespace Holdfast.Tests
{
    /// <summary>A struct comparer, so the JIT specializes the tree for it.</summary>
    public readonly struct LongComparer : IArenaComparer<long>
    {
        public int Compare(in long a, in long b) => a.CompareTo(b);
        public int CompareDescendant(in long a, in long b) => a.CompareTo(b);
    }
}
