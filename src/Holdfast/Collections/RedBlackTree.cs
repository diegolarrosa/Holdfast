using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Holdfast
{
    /// <summary>Handle of a <see cref="RedBlackTree{T, TComp}"/> node.</summary>
    public readonly struct TreeNode<T> where T : struct
    {
        internal readonly Handle<ArenaList<T>, TreeMode> h;
        internal TreeNode(Handle<ArenaList<T>, TreeMode> handle) => h = handle;

        /// <summary>True when this handle refers to no node.</summary>
        public bool IsNull => h.IsNull;
        /// <summary>A handle that refers to no node.</summary>
        public static TreeNode<T> Null => new TreeNode<T>(Handle<ArenaList<T>, TreeMode>.Null);

        /// <summary>Compares two tree node handles.</summary>
        /// <param name="other">The handle to compare with.</param>
        /// <returns>True when both refer to the same node.</returns>
        public bool Equals(TreeNode<T> other) => h == other.h;
        /// <summary>Compares this handle with an arbitrary object.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True when it is a handle referring to the same node.</returns>
        public override bool Equals(object? obj) => obj is TreeNode<T> n && h == n.h;
        /// <summary>Hash code derived from the underlying address.</summary>
        /// <returns>A hash code suitable for dictionaries and sets.</returns>
        public override int GetHashCode() => h.GetHashCode();
        /// <summary>Tests whether two handles refer to the same node.</summary>
        /// <param name="a">First handle.</param>
        /// <param name="b">Second handle.</param>
        /// <returns>True when both refer to the same node.</returns>
        public static bool operator ==(TreeNode<T> a, TreeNode<T> b) => a.h == b.h;
        /// <summary>Tests whether two handles refer to different nodes.</summary>
        /// <param name="a">First handle.</param>
        /// <param name="b">Second handle.</param>
        /// <returns>True when they refer to different nodes.</returns>
        public static bool operator !=(TreeNode<T> a, TreeNode<T> b) => a.h != b.h;
        /// <summary>Renders the underlying address as block and offset.</summary>
        /// <returns>A readable representation of the node's address.</returns>
        public override string ToString() => h.ToString();
    }

    /// <summary>
    /// A red-black tree where each key holds an <see cref="ArenaList{T}"/> of
    /// the values that compare equal to it.
    /// </summary>
    /// <remarks>
    /// Use this when duplicates must be kept individually. If one value per key
    /// is enough, <see cref="RedBlackSet{T, TComp}"/> is markedly cheaper: 24
    /// bytes per key against 64, and one less indirection per comparison.
    /// <para>
    /// Tree nodes live in <c>Arena&lt;ArenaList&lt;T&gt;&gt;</c> and values in
    /// <c>Arena&lt;T&gt;</c>. Because those are separate arenas, a <c>ref</c> to
    /// a node's list stays valid while values are being allocated.
    /// </para>
    /// </remarks>
    public struct RedBlackTree<T, TComp>
        where T : struct
        where TComp : struct, IArenaComparer<T>
    {
        private Handle<ArenaList<T>, TreeMode> root;
        private TComp comparer;
        private long nodes;
        private long values;

        private const bool Red = true;
        private const bool Black = false;

        // =====================================================================
        //  Creation and inspection
        // =====================================================================

        /// <summary>Creates an empty tree using the given comparer instance.</summary>
        /// <param name="comparer">The comparer, stored by value so it may carry state.</param>
        /// <returns>An empty tree.</returns>
        public static RedBlackTree<T, TComp> Create(TComp comparer)
        {
            RedBlackTree<T, TComp> tree;
            tree.root = Handle<ArenaList<T>, TreeMode>.Null;
            tree.comparer = comparer;
            tree.nodes = 0;
            tree.values = 0;
            return tree;
        }

        /// <summary>Creates an empty tree using a default-constructed comparer.</summary>
        /// <returns>An empty tree.</returns>
        public static RedBlackTree<T, TComp> Create() => Create(default(TComp));

        // State for Snapshot. Note that a tree spans two arenas: the nodes live
        // in Arena<ArenaList<T>> and the values in Arena<T>. Both have to be in
        // the file or the tree comes back with dangling value lists.

        internal readonly void CaptureState(out long rootAddress, out long nodeCount, out long valueCount)
        {
            rootAddress = root.Raw;
            nodeCount = nodes;
            valueCount = values;
        }

        internal static RedBlackTree<T, TComp> FromState(
            long rootAddress, long nodeCount, long valueCount, TComp comparer)
        {
            RedBlackTree<T, TComp> tree;
            tree.root = new Handle<ArenaList<T>, TreeMode>(rootAddress);
            tree.comparer = comparer;
            tree.nodes = nodeCount;
            tree.values = valueCount;
            return tree;
        }

        /// <summary>True when the tree holds no keys.</summary>
        public readonly bool IsEmpty => nodes == 0;

        /// <summary>Number of nodes, that is, of distinct keys.</summary>
        public readonly long NodeCount => nodes;

        /// <summary>Total number of values, counting duplicates.</summary>
        public readonly long ValueCount => values;

        /// <summary>The root node, or the null handle when the tree is empty.</summary>
        public readonly TreeNode<T> Root => new TreeNode<T>(root);

        /// <summary>A node's list of values, by reference.</summary>
        /// <param name="node">The node to read from.</param>
        /// <returns>A reference to the node's value list, writable in place.</returns>
        public readonly ref ArenaList<T> ValuesOf(TreeNode<T> node) => ref Arena<ArenaList<T>>.Value(node.h);

        // =====================================================================
        //  Field shorthands
        // =====================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Handle<ArenaList<T>, TreeMode> L(Handle<ArenaList<T>, TreeMode> n) => Arena<ArenaList<T>>.Left(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Handle<ArenaList<T>, TreeMode> R(Handle<ArenaList<T>, TreeMode> n) => Arena<ArenaList<T>>.Right(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Handle<ArenaList<T>, TreeMode> P(Handle<ArenaList<T>, TreeMode> n) => Arena<ArenaList<T>>.Parent(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetL(Handle<ArenaList<T>, TreeMode> n, Handle<ArenaList<T>, TreeMode> v) => Arena<ArenaList<T>>.SetLeft(n, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetR(Handle<ArenaList<T>, TreeMode> n, Handle<ArenaList<T>, TreeMode> v) => Arena<ArenaList<T>>.SetRight(n, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetP(Handle<ArenaList<T>, TreeMode> n, Handle<ArenaList<T>, TreeMode> v) => Arena<ArenaList<T>>.SetParent(n, v);

        /// <summary>Null nodes are black (property 3).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Color(Handle<ArenaList<T>, TreeMode> n) => n.IsNull ? Black : Arena<ArenaList<T>>.Color(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetColor(Handle<ArenaList<T>, TreeMode> n, bool c)
        {
            if (!n.IsNull) Arena<ArenaList<T>>.SetColor(n, c);
        }

        /// <summary>A node's key is the first value in its list.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref T Key(Handle<ArenaList<T>, TreeMode> n)
            => ref ArenaList<T>.ValueOf(Arena<ArenaList<T>>.Value(n).First);

        private static Handle<ArenaList<T>, TreeMode> Grandparent(Handle<ArenaList<T>, TreeMode> n) => P(P(n));

        private static Handle<ArenaList<T>, TreeMode> Sibling(Handle<ArenaList<T>, TreeMode> n)
        {
            var p = P(n);
            return n == L(p) ? R(p) : L(p);
        }

        private static Handle<ArenaList<T>, TreeMode> Uncle(Handle<ArenaList<T>, TreeMode> n) => Sibling(P(n));

        // =====================================================================
        //  Lookup
        // =====================================================================

        /// <summary>Locates the node whose key compares equal to <paramref name="value"/>.</summary>
        /// <param name="value">The value to look for.</param>
        /// <returns>The matching node, or the null handle if there is none.</returns>
        public TreeNode<T> Find(in T value)
        {
            var n = root;
            while (!n.IsNull)
            {
                int c = comparer.Compare(value, Key(n));
                if (c == 0) return new TreeNode<T>(n);
                n = c < 0 ? L(n) : R(n);
            }
            return TreeNode<T>.Null;
        }

        /// <summary>Locates a node using the comparer's descendant comparison.</summary>
        /// <param name="value">The value to look for.</param>
        /// <returns>The matching node, or the null handle if there is none.</returns>
        public TreeNode<T> FindDescendant(in T value)
        {
            var n = root;
            while (!n.IsNull)
            {
                int c = comparer.CompareDescendant(value, Key(n));
                if (c == 0) return new TreeNode<T>(n);
                n = c < 0 ? L(n) : R(n);
            }
            return TreeNode<T>.Null;
        }

        /// <summary>Tests whether a value's key is present.</summary>
        /// <param name="value">The value to look for.</param>
        /// <returns>True when a matching node exists.</returns>
        public bool Contains(in T value) => !Find(value).IsNull;

        // =====================================================================
        //  Insertion
        // =====================================================================

        private Handle<ArenaList<T>, TreeMode> NewNode(in T value)
        {
            var n = Arena<ArenaList<T>>.AsTree(Arena<ArenaList<T>>.Allocate());
            ref var list = ref Arena<ArenaList<T>>.Value(n);
            list = ArenaList<T>.Empty;
            list.AddLast(value);
            Arena<ArenaList<T>>.SetColor(n, Red);
            nodes++;
            return n;
        }

        /// <summary>
        /// Inserts a value. If a node with an equivalent key exists, the value
        /// is appended to that node's list.
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <returns>The node the value was added to.</returns>
        public TreeNode<T> Insert(T value)
        {
            values++;

            if (root.IsNull)
            {
                root = NewNode(value);
                Insert1(root);
                return new TreeNode<T>(root);
            }

            var n = root;
            Handle<ArenaList<T>, TreeMode> inserted;
            while (true)
            {
                int c = comparer.Compare(value, Key(n));
                if (c == 0)
                {
                    Arena<ArenaList<T>>.Value(n).AddLast(value);
                    return new TreeNode<T>(n);
                }
                if (c < 0)
                {
                    if (L(n).IsNull) { inserted = NewNode(value); SetL(n, inserted); break; }
                    n = L(n);
                }
                else
                {
                    if (R(n).IsNull) { inserted = NewNode(value); SetR(n, inserted); break; }
                    n = R(n);
                }
            }

            SetP(inserted, n);
            Insert1(inserted);
            return new TreeNode<T>(inserted);
        }

        private void Insert1(Handle<ArenaList<T>, TreeMode> n)
        {
            if (P(n).IsNull) SetColor(n, Black);
            else Insert2(n);
        }

        private void Insert2(Handle<ArenaList<T>, TreeMode> n)
        {
            if (Color(P(n)) == Black) return;
            Insert3(n);
        }

        private void Insert3(Handle<ArenaList<T>, TreeMode> n)
        {
            var uncle = Uncle(n);
            if (Color(uncle) == Red)
            {
                SetColor(P(n), Black);
                SetColor(uncle, Black);
                var g = Grandparent(n);
                SetColor(g, Red);
                Insert1(g);
            }
            else Insert4(n);
        }

        private void Insert4(Handle<ArenaList<T>, TreeMode> n)
        {
            var g = Grandparent(n);
            if (n == R(P(n)) && P(n) == L(g))
            {
                RotateLeft(P(n));
                n = L(n);
            }
            else if (n == L(P(n)) && P(n) == R(g))
            {
                RotateRight(P(n));
                n = R(n);
            }
            Insert5(n);
        }

        private void Insert5(Handle<ArenaList<T>, TreeMode> n)
        {
            var g = Grandparent(n);
            SetColor(P(n), Black);
            SetColor(g, Red);
            if (n == L(P(n)) && P(n) == L(g)) RotateRight(g);
            else RotateLeft(g);
        }

        // =====================================================================
        //  Rotations
        // =====================================================================

        private void ReplaceNode(Handle<ArenaList<T>, TreeMode> old, Handle<ArenaList<T>, TreeMode> replacement)
        {
            var p = P(old);
            if (p.IsNull) root = replacement;
            else if (old == L(p)) SetL(p, replacement);
            else SetR(p, replacement);

            if (!replacement.IsNull) SetP(replacement, p);
        }

        private void RotateLeft(Handle<ArenaList<T>, TreeMode> n)
        {
            var r = R(n);
            ReplaceNode(n, r);
            SetR(n, L(r));
            if (!L(r).IsNull) SetP(L(r), n);
            SetL(r, n);
            SetP(n, r);
        }

        private void RotateRight(Handle<ArenaList<T>, TreeMode> n)
        {
            var l = L(n);
            ReplaceNode(n, l);
            SetL(n, R(l));
            if (!R(l).IsNull) SetP(R(l), n);
            SetR(l, n);
            SetP(n, l);
        }

        // =====================================================================
        //  Removal
        // =====================================================================

        /// <summary>
        /// Removes one value. If the node held more than one, the first is
        /// dropped and the node stays. Returns false if there was nothing to
        /// remove.
        /// </summary>
        public bool Remove(in T value)
        {
            var node = Find(value);
            if (node.IsNull) return false;

            if (Arena<ArenaList<T>>.Value(node.h).Count > 1)
            {
                Arena<ArenaList<T>>.Value(node.h).RemoveFirst();
                values--;
                return true;
            }
            return RemoveNode(node);
        }

        /// <summary>Removes the whole node, with all of its values.</summary>
        /// <param name="node">The node to remove. The null handle is accepted and ignored.</param>
        /// <returns>False when the handle was null.</returns>
        public bool RemoveNode(TreeNode<T> node)
        {
            if (node.IsNull) return false;
            var n = node.h;

            // This node's values go with it. Release them now, before any list
            // is moved around.
            values -= Arena<ArenaList<T>>.Value(n).Count;
            Arena<ArenaList<T>>.Value(n).Clear();

            if (!L(n).IsNull && !R(n).IsNull)
            {
                // Two children: the node that physically disappears is the
                // predecessor, and its list moves up. The predecessor must be
                // left without a list before it is released, or freeing it
                // would free the values the surviving node just inherited.
                var predecessor = Maximum(L(n));
                var inherited = Arena<ArenaList<T>>.Value(predecessor);
                Arena<ArenaList<T>>.Value(predecessor) = ArenaList<T>.Empty;
                Arena<ArenaList<T>>.Value(n) = inherited;
                n = predecessor;
            }

            Debug.Assert(L(n).IsNull || R(n).IsNull, "the node being unlinked cannot have two children");
            var child = R(n).IsNull ? L(n) : R(n);

            if (Color(n) == Black)
            {
                SetColor(n, Color(child));
                Delete1(n);
            }
            ReplaceNode(n, child);

            if (P(n).IsNull && !child.IsNull) SetColor(child, Black);

            Arena<ArenaList<T>>.Free(n);
            nodes--;
            return true;
        }

        private static Handle<ArenaList<T>, TreeMode> Maximum(Handle<ArenaList<T>, TreeMode> n)
        {
            while (!R(n).IsNull) n = R(n);
            return n;
        }

        private static Handle<ArenaList<T>, TreeMode> Minimum(Handle<ArenaList<T>, TreeMode> n)
        {
            while (!L(n).IsNull) n = L(n);
            return n;
        }

        private void Delete1(Handle<ArenaList<T>, TreeMode> n)
        {
            if (P(n).IsNull) return;
            Delete2(n);
        }

        private void Delete2(Handle<ArenaList<T>, TreeMode> n)
        {
            var s = Sibling(n);
            if (Color(s) == Red)
            {
                SetColor(P(n), Red);
                SetColor(s, Black);
                if (n == L(P(n))) RotateLeft(P(n));
                else RotateRight(P(n));
            }
            Delete3(n);
        }

        private void Delete3(Handle<ArenaList<T>, TreeMode> n)
        {
            var s = Sibling(n);
            if (Color(P(n)) == Black && Color(s) == Black
                && Color(L(s)) == Black && Color(R(s)) == Black)
            {
                SetColor(s, Red);
                Delete1(P(n));
            }
            else Delete4(n);
        }

        private void Delete4(Handle<ArenaList<T>, TreeMode> n)
        {
            var s = Sibling(n);
            if (Color(P(n)) == Red && Color(s) == Black
                && Color(L(s)) == Black && Color(R(s)) == Black)
            {
                SetColor(s, Red);
                SetColor(P(n), Black);
            }
            else Delete5(n);
        }

        private void Delete5(Handle<ArenaList<T>, TreeMode> n)
        {
            var s = Sibling(n);
            if (n == L(P(n)) && Color(s) == Black
                && Color(L(s)) == Red && Color(R(s)) == Black)
            {
                SetColor(s, Red);
                SetColor(L(s), Black);
                RotateRight(s);
            }
            else if (n == R(P(n)) && Color(s) == Black
                && Color(R(s)) == Red && Color(L(s)) == Black)
            {
                SetColor(s, Red);
                SetColor(R(s), Black);
                RotateLeft(s);
            }
            Delete6(n);
        }

        private void Delete6(Handle<ArenaList<T>, TreeMode> n)
        {
            var s = Sibling(n);
            SetColor(s, Color(P(n)));
            SetColor(P(n), Black);
            if (n == L(P(n)))
            {
                SetColor(R(s), Black);
                RotateLeft(P(n));
            }
            else
            {
                SetColor(L(s), Black);
                RotateRight(P(n));
            }
        }

        // =====================================================================
        //  Clearing
        // =====================================================================

        /// <summary>
        /// Frees the whole tree and every value list, without recursion.
        /// </summary>
        public void Clear()
        {
            var n = root;
            while (!n.IsNull)
            {
                if (!L(n).IsNull) { n = L(n); continue; }
                if (!R(n).IsNull) { n = R(n); continue; }

                var done = n;
                n = P(n);

                Arena<ArenaList<T>>.Value(done).Clear();
                Arena<ArenaList<T>>.Free(done);

                if (!n.IsNull)
                {
                    if (done == L(n)) SetL(n, Handle<ArenaList<T>, TreeMode>.Null);
                    else if (done == R(n)) SetR(n, Handle<ArenaList<T>, TreeMode>.Null);
                }
            }
            root = Handle<ArenaList<T>, TreeMode>.Null;
            nodes = 0;
            values = 0;
        }

        // =====================================================================
        //  Traversal
        // =====================================================================

        /// <summary>The node with the smallest key, or the null handle.</summary>
        public readonly TreeNode<T> First => root.IsNull ? TreeNode<T>.Null : new TreeNode<T>(Minimum(root));
        /// <summary>The node with the largest key, or the null handle.</summary>
        public readonly TreeNode<T> Last => root.IsNull ? TreeNode<T>.Null : new TreeNode<T>(Maximum(root));

        /// <summary>Next node in order. Works from any node, with no prior state.</summary>
        /// <param name="node">The node to step forward from.</param>
        /// <returns>The following node, or the null handle at the end.</returns>
        public static TreeNode<T> Successor(TreeNode<T> node)
        {
            var n = node.h;
            if (!R(n).IsNull) return new TreeNode<T>(Minimum(R(n)));
            var p = P(n);
            while (!p.IsNull && n == R(p)) { n = p; p = P(p); }
            return new TreeNode<T>(p);
        }

        /// <summary>Previous node in order. Works from any node, with no prior state.</summary>
        /// <param name="node">The node to step back from.</param>
        /// <returns>The preceding node, or the null handle at the start.</returns>
        public static TreeNode<T> Predecessor(TreeNode<T> node)
        {
            var n = node.h;
            if (!L(n).IsNull) return new TreeNode<T>(Maximum(L(n)));
            var p = P(n);
            while (!p.IsNull && n == L(p)) { n = p; p = P(p); }
            return new TreeNode<T>(p);
        }

        /// <summary>In-order walk through parent links.</summary>
        /// <returns>An enumerator over the tree's nodes, in order.</returns>
        public readonly ParentEnumerator GetEnumerator() => new ParentEnumerator(root);

        /// <summary>Walks the tree in order by following parent links.</summary>
        public struct ParentEnumerator
        {
            private TreeNode<T> current;
            private TreeNode<T> following;

            internal ParentEnumerator(Handle<ArenaList<T>, TreeMode> root)
            {
                current = TreeNode<T>.Null;
                following = root.IsNull ? TreeNode<T>.Null : new TreeNode<T>(Minimum(root));
            }

            /// <summary>The node at the current position.</summary>
            public readonly TreeNode<T> Current => current;

            /// <summary>Advances to the next node in order.</summary>
            /// <returns>False once every node has been visited.</returns>
            public bool MoveNext()
            {
                if (following.IsNull) return false;
                current = following;
                following = Successor(current);
                return true;
            }
        }

        /// <summary>In-order walk with an explicit stack. See <see cref="RedBlackSet{T, TComp}.InOrder"/>.</summary>
        public readonly StackWalk InOrder => new StackWalk(root);

        /// <summary>An in-order traversal backed by an explicit stack.</summary>
        public readonly struct StackWalk
        {
            private readonly Handle<ArenaList<T>, TreeMode> root;
            internal StackWalk(Handle<ArenaList<T>, TreeMode> root) => this.root = root;

            /// <summary>Builds a fresh enumerator, with its own stack.</summary>
            /// <returns>An enumerator over the tree's nodes, in order.</returns>
            public Enumerator GetEnumerator() => new Enumerator(root);

            /// <summary>Walks the tree in order, keeping the pending left spine on a stack.</summary>
            public struct Enumerator
            {
                private InOrder<ArenaList<T>>.Enumerator inner;
                internal Enumerator(Handle<ArenaList<T>, TreeMode> root) => inner = new InOrder<ArenaList<T>>(root).GetEnumerator();
                /// <summary>The node at the current position.</summary>
                public readonly TreeNode<T> Current => new TreeNode<T>(inner.Current);
                /// <summary>Advances to the next node in order.</summary>
            /// <returns>False once every node has been visited.</returns>
            public bool MoveNext() => inner.MoveNext();
            }
        }

        // =====================================================================
        //  Diagnostics
        // =====================================================================

        /// <summary>
        /// Verifies the red-black invariants, every value list, the in-order
        /// ordering and both counters.
        /// </summary>
        public void Validate()
        {
            long n = RedBlackValidator<ArenaList<T>>.Validate(root);

            if (n != nodes)
                throw new InvalidOperationException($"the tree holds {n} nodes but the count reports {nodes}");

            long totalValues = 0, seen = 0;
            var previous = TreeNode<T>.Null;

            foreach (var node in this)
            {
                ref var list = ref Arena<ArenaList<T>>.Value(node.h);
                list.Validate();

                if (list.Count == 0)
                    throw new InvalidOperationException($"node {node} has no values left");

                if (!previous.IsNull && comparer.Compare(Key(node.h), Key(previous.h)) <= 0)
                    throw new InvalidOperationException($"the in-order walk is not increasing at {node}");

                totalValues += list.Count;
                seen++;
                previous = node;
            }

            if (seen != nodes)
                throw new InvalidOperationException($"the walk visited {seen} nodes, the count reports {nodes}");
            if (totalValues != values)
                throw new InvalidOperationException($"there are {totalValues} values but the count reports {values}");
        }
    }
}
