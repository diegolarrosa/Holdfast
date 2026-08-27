using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Holdfast
{
    /// <summary>Handle of a <see cref="RedBlackSet{T, TComp}"/> node.</summary>
    public readonly struct SetNode<T> where T : struct
    {
        internal readonly Handle<T, TreeMode> h;
        internal SetNode(Handle<T, TreeMode> handle) => h = handle;

        /// <summary>True when this handle refers to no node.</summary>
        public bool IsNull => h.IsNull;
        /// <summary>A handle that refers to no node.</summary>
        public static SetNode<T> Null => new SetNode<T>(Handle<T, TreeMode>.Null);

        /// <summary>Compares two set node handles.</summary>
        /// <param name="other">The handle to compare with.</param>
        /// <returns>True when both refer to the same node.</returns>
        public bool Equals(SetNode<T> other) => h == other.h;
        /// <summary>Compares this handle with an arbitrary object.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True when it is a handle referring to the same node.</returns>
        public override bool Equals(object? obj) => obj is SetNode<T> n && h == n.h;
        /// <summary>Hash code derived from the underlying address.</summary>
        /// <returns>A hash code suitable for dictionaries and sets.</returns>
        public override int GetHashCode() => h.GetHashCode();
        /// <summary>Tests whether two handles refer to the same node.</summary>
        /// <param name="a">First handle.</param>
        /// <param name="b">Second handle.</param>
        /// <returns>True when both refer to the same node.</returns>
        public static bool operator ==(SetNode<T> a, SetNode<T> b) => a.h == b.h;
        /// <summary>Tests whether two handles refer to different nodes.</summary>
        /// <param name="a">First handle.</param>
        /// <param name="b">Second handle.</param>
        /// <returns>True when they refer to different nodes.</returns>
        public static bool operator !=(SetNode<T> a, SetNode<T> b) => a.h != b.h;
        /// <summary>Renders the underlying address as block and offset.</summary>
        /// <returns>A readable representation of the node's address.</returns>
        public override string ToString() => h.ToString();
    }

    /// <summary>
    /// A red-black tree with one value per key: the value lives directly in the
    /// node, with no indirection.
    /// </summary>
    /// <remarks>
    /// Inserting a key that already exists replaces the value.
    /// <para>
    /// Memory is the reason to prefer this over
    /// <see cref="RedBlackTree{T, TComp}"/>. With <c>T = long</c> a key costs 24
    /// bytes here against 64 there, and the comparison during a descent reads
    /// the key straight from the node instead of following a handle into
    /// another arena.
    /// </para>
    /// </remarks>
    public struct RedBlackSet<T, TComp>
        where T : struct
        where TComp : struct, IArenaComparer<T>
    {
        private Handle<T, TreeMode> root;
        private TComp comparer;
        private long count;

        private const bool Red = true;
        private const bool Black = false;

        // =====================================================================
        //  Creation and inspection
        // =====================================================================

        /// <summary>Creates an empty set using the given comparer instance.</summary>
        /// <param name="comparer">The comparer, stored by value so it may carry state.</param>
        /// <returns>An empty set.</returns>
        public static RedBlackSet<T, TComp> Create(TComp comparer)
        {
            RedBlackSet<T, TComp> set;
            set.root = Handle<T, TreeMode>.Null;
            set.comparer = comparer;
            set.count = 0;
            return set;
        }

        /// <summary>Creates an empty set using a default-constructed comparer.</summary>
        /// <returns>An empty set.</returns>
        public static RedBlackSet<T, TComp> Create() => Create(default(TComp));

        // The set's whole state is the root address and the count: everything
        // else is in the arena. These two let Snapshot write it down and put it
        // back without making root public, which would let anyone hand the set
        // a node from another tree.

        internal readonly void CaptureState(out long rootAddress, out long size)
        {
            rootAddress = root.Raw;
            size = count;
        }

        internal static RedBlackSet<T, TComp> FromState(long rootAddress, long size, TComp comparer)
        {
            RedBlackSet<T, TComp> set;
            set.root = new Handle<T, TreeMode>(rootAddress);
            set.comparer = comparer;
            set.count = size;
            return set;
        }

        /// <summary>True when the set holds no keys.</summary>
        public readonly bool IsEmpty => count == 0;
        /// <summary>How many distinct keys the set holds.</summary>
        public readonly long Count => count;
        /// <summary>The root node, or the null handle when the set is empty.</summary>
        public readonly SetNode<T> Root => new SetNode<T>(root);

        /// <summary>A node's value by reference: readable and writable in place.</summary>
        public readonly ref T ValueOf(SetNode<T> node) => ref Arena<T>.Value(node.h);

        // =====================================================================
        //  Field shorthands
        // =====================================================================

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Handle<T, TreeMode> L(Handle<T, TreeMode> n) => Arena<T>.Left(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Handle<T, TreeMode> R(Handle<T, TreeMode> n) => Arena<T>.Right(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Handle<T, TreeMode> P(Handle<T, TreeMode> n) => Arena<T>.Parent(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetL(Handle<T, TreeMode> n, Handle<T, TreeMode> v) => Arena<T>.SetLeft(n, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetR(Handle<T, TreeMode> n, Handle<T, TreeMode> v) => Arena<T>.SetRight(n, v);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetP(Handle<T, TreeMode> n, Handle<T, TreeMode> v) => Arena<T>.SetParent(n, v);

        /// <summary>Null nodes are black (property 3).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Color(Handle<T, TreeMode> n) => n.IsNull ? Black : Arena<T>.Color(n);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetColor(Handle<T, TreeMode> n, bool c)
        {
            if (!n.IsNull) Arena<T>.SetColor(n, c);
        }

        /// <summary>The key is the value itself, with no indirection.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref T Key(Handle<T, TreeMode> n) => ref Arena<T>.Value(n);

        private static Handle<T, TreeMode> Grandparent(Handle<T, TreeMode> n) => P(P(n));

        private static Handle<T, TreeMode> Sibling(Handle<T, TreeMode> n)
        {
            var p = P(n);
            return n == L(p) ? R(p) : L(p);
        }

        private static Handle<T, TreeMode> Uncle(Handle<T, TreeMode> n) => Sibling(P(n));

        // =====================================================================
        //  Lookup
        // =====================================================================

        /// <summary>Locates the node whose key compares equal to <paramref name="value"/>.</summary>
        /// <param name="value">The value to look for.</param>
        /// <returns>The matching node, or the null handle if there is none.</returns>
        public SetNode<T> Find(in T value)
        {
            var n = root;
            while (!n.IsNull)
            {
                int c = comparer.Compare(value, Key(n));
                if (c == 0) return new SetNode<T>(n);
                n = c < 0 ? L(n) : R(n);
            }
            return SetNode<T>.Null;
        }

        /// <summary>Locates a node using the comparer's descendant comparison.</summary>
        /// <param name="value">The value to look for.</param>
        /// <returns>The matching node, or the null handle if there is none.</returns>
        public SetNode<T> FindDescendant(in T value)
        {
            var n = root;
            while (!n.IsNull)
            {
                int c = comparer.CompareDescendant(value, Key(n));
                if (c == 0) return new SetNode<T>(n);
                n = c < 0 ? L(n) : R(n);
            }
            return SetNode<T>.Null;
        }

        /// <summary>Tests whether a value's key is present.</summary>
        /// <param name="value">The value to look for.</param>
        /// <returns>True when a matching node exists.</returns>
        public bool Contains(in T value) => !Find(value).IsNull;

        // =====================================================================
        //  Insertion
        // =====================================================================

        private Handle<T, TreeMode> NewNode(in T value)
        {
            var n = Arena<T>.AsTree(Arena<T>.Allocate());
            Arena<T>.Value(n) = value;
            Arena<T>.SetColor(n, Red);
            count++;
            return n;
        }

        /// <summary>
        /// Inserts or replaces. <paramref name="added"/> reports whether this
        /// was a new key or an existing one being overwritten.
        /// </summary>
        public SetNode<T> Insert(T value, out bool added)
        {
            if (root.IsNull)
            {
                root = NewNode(value);
                Insert1(root);
                added = true;
                return new SetNode<T>(root);
            }

            var n = root;
            Handle<T, TreeMode> inserted;
            while (true)
            {
                int c = comparer.Compare(value, Key(n));
                if (c == 0)
                {
                    Arena<T>.Value(n) = value;
                    added = false;
                    return new SetNode<T>(n);
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
            added = true;
            return new SetNode<T>(inserted);
        }

        /// <summary>Inserts or replaces, discarding whether it was an addition.</summary>
        /// <param name="value">The value to store.</param>
        /// <returns>The node holding the value.</returns>
        public SetNode<T> Insert(T value) => Insert(value, out _);

        private void Insert1(Handle<T, TreeMode> n)
        {
            if (P(n).IsNull) SetColor(n, Black);
            else Insert2(n);
        }

        private void Insert2(Handle<T, TreeMode> n)
        {
            if (Color(P(n)) == Black) return;
            Insert3(n);
        }

        private void Insert3(Handle<T, TreeMode> n)
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

        private void Insert4(Handle<T, TreeMode> n)
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

        private void Insert5(Handle<T, TreeMode> n)
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

        private void ReplaceNode(Handle<T, TreeMode> old, Handle<T, TreeMode> replacement)
        {
            var p = P(old);
            if (p.IsNull) root = replacement;
            else if (old == L(p)) SetL(p, replacement);
            else SetR(p, replacement);

            if (!replacement.IsNull) SetP(replacement, p);
        }

        private void RotateLeft(Handle<T, TreeMode> n)
        {
            var r = R(n);
            ReplaceNode(n, r);
            SetR(n, L(r));
            if (!L(r).IsNull) SetP(L(r), n);
            SetL(r, n);
            SetP(n, r);
        }

        private void RotateRight(Handle<T, TreeMode> n)
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

        /// <summary>Removes the node whose key compares equal to <paramref name="value"/>.</summary>
        /// <param name="value">The value to remove.</param>
        /// <returns>False when no matching node existed.</returns>
        public bool Remove(in T value) => RemoveNode(Find(value));

        /// <summary>Removes a node located earlier by <see cref="Find"/>.</summary>
        /// <param name="node">The node to remove. The null handle is accepted and ignored.</param>
        /// <returns>False when the handle was null.</returns>
        public bool RemoveNode(SetNode<T> node)
        {
            if (node.IsNull) return false;
            var n = node.h;

            if (!L(n).IsNull && !R(n).IsNull)
            {
                // Two children: copy the predecessor's value up and delete the
                // predecessor's position instead. A value owns no memory, so
                // nothing needs freeing here.
                var predecessor = Maximum(L(n));
                Arena<T>.Value(n) = Arena<T>.Value(predecessor);
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

            Arena<T>.Free(n);
            count--;
            return true;
        }

        private static Handle<T, TreeMode> Maximum(Handle<T, TreeMode> n)
        {
            while (!R(n).IsNull) n = R(n);
            return n;
        }

        private static Handle<T, TreeMode> Minimum(Handle<T, TreeMode> n)
        {
            while (!L(n).IsNull) n = L(n);
            return n;
        }

        private void Delete1(Handle<T, TreeMode> n)
        {
            if (P(n).IsNull) return;
            Delete2(n);
        }

        private void Delete2(Handle<T, TreeMode> n)
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

        private void Delete3(Handle<T, TreeMode> n)
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

        private void Delete4(Handle<T, TreeMode> n)
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

        private void Delete5(Handle<T, TreeMode> n)
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

        private void Delete6(Handle<T, TreeMode> n)
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
        /// Frees the whole tree without recursion: descend left, else right,
        /// else climb releasing. A recursive version would blow the stack on a
        /// tree of billions of nodes.
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
                Arena<T>.Free(done);

                if (!n.IsNull)
                {
                    if (done == L(n)) SetL(n, Handle<T, TreeMode>.Null);
                    else if (done == R(n)) SetR(n, Handle<T, TreeMode>.Null);
                }
            }
            root = Handle<T, TreeMode>.Null;
            count = 0;
        }

        // =====================================================================
        //  Traversal
        // =====================================================================

        /// <summary>The node with the smallest key, or the null handle.</summary>
        public readonly SetNode<T> First => root.IsNull ? SetNode<T>.Null : new SetNode<T>(Minimum(root));
        /// <summary>The node with the largest key, or the null handle.</summary>
        public readonly SetNode<T> Last => root.IsNull ? SetNode<T>.Null : new SetNode<T>(Maximum(root));

        /// <summary>Next node in order. Works from any node, with no prior state.</summary>
        public static SetNode<T> Successor(SetNode<T> node)
        {
            var n = node.h;
            if (!R(n).IsNull) return new SetNode<T>(Minimum(R(n)));
            var p = P(n);
            while (!p.IsNull && n == R(p)) { n = p; p = P(p); }
            return new SetNode<T>(p);
        }

        /// <summary>Previous node in order. Works from any node, with no prior state.</summary>
        /// <param name="node">The node to step back from.</param>
        /// <returns>The preceding node, or the null handle at the start.</returns>
        public static SetNode<T> Predecessor(SetNode<T> node)
        {
            var n = node.h;
            if (!L(n).IsNull) return new SetNode<T>(Maximum(L(n)));
            var p = P(n);
            while (!p.IsNull && n == L(p)) { n = p; p = P(p); }
            return new SetNode<T>(p);
        }

        /// <summary>In-order walk through parent links.</summary>
        /// <returns>An enumerator over the tree's nodes, in order.</returns>
        public readonly ParentEnumerator GetEnumerator() => new ParentEnumerator(root);

        /// <summary>Walks the tree in order by following parent links.</summary>
        public struct ParentEnumerator
        {
            private SetNode<T> current;
            private SetNode<T> following;

            internal ParentEnumerator(Handle<T, TreeMode> root)
            {
                current = SetNode<T>.Null;
                following = root.IsNull ? SetNode<T>.Null : new SetNode<T>(Minimum(root));
            }

            /// <summary>The node at the current position.</summary>
            public readonly SetNode<T> Current => current;

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

        /// <summary>
        /// In-order walk with an explicit stack. Faster than the parent-link
        /// walk when traversing everything; costs one small allocation.
        /// </summary>
        public readonly StackWalk InOrder => new StackWalk(root);

        /// <summary>An in-order traversal backed by an explicit stack.</summary>
        public readonly struct StackWalk
        {
            private readonly Handle<T, TreeMode> root;
            internal StackWalk(Handle<T, TreeMode> root) => this.root = root;

            /// <summary>Builds a fresh enumerator, with its own stack.</summary>
            /// <returns>An enumerator over the tree's nodes, in order.</returns>
            public Enumerator GetEnumerator() => new Enumerator(root);

            /// <summary>Walks the tree in order, keeping the pending left spine on a stack.</summary>
            public struct Enumerator
            {
                private InOrder<T>.Enumerator inner;
                internal Enumerator(Handle<T, TreeMode> root) => inner = new InOrder<T>(root).GetEnumerator();
                /// <summary>The node at the current position.</summary>
                public readonly SetNode<T> Current => new SetNode<T>(inner.Current);
                /// <summary>Advances to the next node in order.</summary>
            /// <returns>False once every node has been visited.</returns>
            public bool MoveNext() => inner.MoveNext();
            }
        }

        // =====================================================================
        //  Diagnostics
        // =====================================================================

        /// <summary>
        /// Verifies the red-black invariants, the parent links, that the
        /// in-order walk is strictly increasing, and that the count agrees.
        /// </summary>
        public void Validate()
        {
            long n = RedBlackValidator<T>.Validate(root);

            if (n != count)
                throw new InvalidOperationException($"the tree holds {n} nodes but the count reports {count}");

            long seen = 0;
            var previous = SetNode<T>.Null;

            foreach (var node in this)
            {
                if (!previous.IsNull && comparer.Compare(Key(node.h), Key(previous.h)) <= 0)
                    throw new InvalidOperationException($"the in-order walk is not increasing at {node}");
                seen++;
                previous = node;
            }

            if (seen != count)
                throw new InvalidOperationException($"the walk visited {seen} nodes, the count reports {count}");
        }
    }
}
