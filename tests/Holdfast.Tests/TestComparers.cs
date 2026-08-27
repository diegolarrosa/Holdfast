namespace Holdfast.Tests
{
    /// <summary>A struct comparer, so the JIT specializes the tree for it.</summary>
    public readonly struct LongComparer : IArenaComparer<long>
    {
        public int Compare(in long a, in long b) => a.CompareTo(b);
        public int CompareDescendant(in long a, in long b) => a.CompareTo(b);
    }

    /// <summary>Ordena PairKey solo por Key, para que los Tag se apilen en la lista del nodo.</summary>
    public readonly struct PairKeyComparer : IArenaComparer<PairKey>
    {
        public int Compare(in PairKey a, in PairKey b) => a.Key.CompareTo(b.Key);
        public int CompareDescendant(in PairKey a, in PairKey b) => a.Key.CompareTo(b.Key);
    }
}
