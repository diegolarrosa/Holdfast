using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Holdfast
{
    /// <summary>
    /// A doubly linked list whose nodes live in <see cref="Arena{T}"/>.
    /// </summary>
    /// <remarks>
    /// The list is a struct holding its own head, tail and count — there is no
    /// ambient "current list" and nothing to open or close around a batch of
    /// edits.
    /// <para>
    /// Nesting works: <c>ArenaList&lt;ArenaList&lt;T&gt;&gt;</c> is a list of
    /// lists, and an inner list is mutated in place through
    /// <see cref="ValueOf"/>, which returns a <c>ref</c>.
    /// </para>
    /// </remarks>
    public struct ArenaList<T> where T : struct
    {
        private Handle<T, ListMode> head;
        private Handle<T, ListMode> tail;
        private long count;

        // ---------------------------------------------------------------------
        //  Creation
        // ---------------------------------------------------------------------

        /// <summary>
        /// An empty list. Always start from this or from <see cref="Reserve"/>.
        /// </summary>
        /// <remarks>
        /// <c>default(ArenaList&lt;T&gt;)</c> is NOT a valid empty list: the null
        /// address is -1, not 0, so a zeroed list points at node 0. Debug builds
        /// assert on it.
        /// </remarks>
        public static ArenaList<T> Empty
        {
            get
            {
                ArenaList<T> list;
                list.head = Handle<T, ListMode>.Null;
                list.tail = Handle<T, ListMode>.Null;
                list.count = 0;
                return list;
            }
        }

        /// <summary>
        /// Allocates a node in the arena of lists and returns it already
        /// initialized as an empty list. The safe way to create the outer list
        /// of a list of lists.
        /// </summary>
        public static Handle<ArenaList<T>, ListMode> Reserve()
        {
            var handle = Arena<ArenaList<T>>.AsList(Arena<ArenaList<T>>.Allocate());
            Arena<ArenaList<T>>.Value(handle) = Empty;
            return handle;
        }

        [Conditional("DEBUG")]
        private readonly void CheckInitialized()
        {
            // A genuinely empty list has head = -1. A zeroed one does not.
            Debug.Assert(!(head.Raw == 0 && tail.Raw == 0 && count == 0),
                "uninitialized ArenaList<T>: use ArenaList<T>.Empty or ArenaList<T>.Reserve()");
        }

        // ---------------------------------------------------------------------
        //  Inspection
        // ---------------------------------------------------------------------

        /// <summary>The first node, or the null handle when the list is empty.</summary>
        public readonly Handle<T, ListMode> First => head;
        /// <summary>The last node, or the null handle when the list is empty.</summary>
        public readonly Handle<T, ListMode> Last => tail;
        /// <summary>How many nodes the list holds.</summary>
        public readonly long Count => count;
        /// <summary>True when the list holds no nodes.</summary>
        public readonly bool IsEmpty => count == 0;

        /// <summary>The node after this one, or the null handle at the end.</summary>
        /// <param name="node">The node to step from.</param>
        /// <returns>The following node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, ListMode> Next(Handle<T, ListMode> node) => Arena<T>.Next(node);

        /// <summary>The node before this one, or the null handle at the start.</summary>
        /// <param name="node">The node to step from.</param>
        /// <returns>The preceding node, or the null handle.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Handle<T, ListMode> Previous(Handle<T, ListMode> node) => Arena<T>.Previous(node);

        /// <summary>A node's payload by reference: readable and writable in place.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T ValueOf(Handle<T, ListMode> node) => ref Arena<T>.Value(node);

        /// <summary>
        /// The first node's payload, by reference. Throws if the list is empty.
        /// </summary>
        public ref T FirstValue
        {
            get
            {
                if (count == 0) throw new InvalidOperationException("the list is empty");
                return ref Arena<T>.Value(head);
            }
        }

        /// <summary>
        /// The last node's payload, by reference. Throws if the list is empty.
        /// </summary>
        public ref T LastValue
        {
            get
            {
                if (count == 0) throw new InvalidOperationException("the list is empty");
                return ref Arena<T>.Value(tail);
            }
        }

        // ---------------------------------------------------------------------
        //  Insertion
        // ---------------------------------------------------------------------

        private Handle<T, ListMode> NewNode(in T value)
        {
            var node = Arena<T>.AsList(Arena<T>.Allocate());
            Arena<T>.Value(node) = value;
            return node;
        }

        /// <summary>Allocates a node holding <paramref name="value"/> and puts it at the front.</summary>
        /// <param name="value">The payload to store.</param>
        /// <returns>A handle to the new node.</returns>
        public Handle<T, ListMode> AddFirst(T value)
        {
            CheckInitialized();
            var node = NewNode(value);
            head = Arena<T>.AddFirst(head, node);
            if (tail.IsNull) tail = head;
            count++;
            return node;
        }

        /// <summary>Allocates a node holding <paramref name="value"/> and puts it at the back.</summary>
        /// <param name="value">The payload to store.</param>
        /// <returns>A handle to the new node.</returns>
        public Handle<T, ListMode> AddLast(T value)
        {
            CheckInitialized();
            var node = NewNode(value);
            tail = Arena<T>.AddLast(tail, node);
            if (head.IsNull) head = tail;
            count++;
            return node;
        }

        /// <summary>Inserts after a node that already belongs to this list.</summary>
        /// <param name="node">The node to insert behind.</param>
        /// <param name="value">The payload to store.</param>
        /// <returns>A handle to the new node.</returns>
        public Handle<T, ListMode> AddAfter(Handle<T, ListMode> node, T value)
        {
            CheckInitialized();
            Debug.Assert(!node.IsNull, "AddAfter with a null node");
            var inserted = NewNode(value);
            Arena<T>.AddAfter(node, inserted);
            if (node == tail) tail = inserted;
            count++;
            return inserted;
        }

        // ---------------------------------------------------------------------
        //  Removal
        // ---------------------------------------------------------------------

        /// <summary>Removes and frees the first node. Throws if the list is empty.</summary>
        public void RemoveFirst()
        {
            if (count == 0) throw new InvalidOperationException("the list is empty");
            var removed = head;
            head = Arena<T>.RemoveFirst(head);
            if (removed == tail) tail = Handle<T, ListMode>.Null;
            Arena<T>.Free(removed);
            count--;
        }

        /// <summary>Removes and frees the last node. Throws if the list is empty.</summary>
        public void RemoveLast()
        {
            if (count == 0) throw new InvalidOperationException("the list is empty");
            var removed = tail;
            tail = Arena<T>.RemoveLast(tail);
            if (removed == head) head = Handle<T, ListMode>.Null;
            Arena<T>.Free(removed);
            count--;
        }

        /// <summary>
        /// Removes a node from THIS list. Membership cannot be checked without
        /// walking the list; passing a node from another one corrupts both.
        /// </summary>
        public void Remove(Handle<T, ListMode> node)
        {
            if (count == 0) throw new InvalidOperationException("the list is empty");
            if (node == head) { RemoveFirst(); return; }
            if (node == tail) { RemoveLast(); return; }
            Arena<T>.Unlink(node);
            Arena<T>.Free(node);
            count--;
        }

        /// <summary>Frees every node. Walks once instead of relinking each step.</summary>
        public void Clear()
        {
            var node = head;
            while (!node.IsNull)
            {
                var following = Arena<T>.Next(node);
                Arena<T>.Free(node);
                node = following;
            }
            head = Handle<T, ListMode>.Null;
            tail = Handle<T, ListMode>.Null;
            count = 0;
        }

        // ---------------------------------------------------------------------
        //  Traversal
        //
        //      foreach (var node in list)           -> handles
        //      foreach (ref var v in list.Values)   -> payloads by reference
        //
        //  Both capture the next node before yielding the current one, so the
        //  current node may be removed mid-traversal.
        // ---------------------------------------------------------------------

        /// <summary>Enumerates node handles from first to last.</summary>
        /// <returns>An enumerator over the list's node handles.</returns>
        public readonly NodeEnumerator GetEnumerator() => new NodeEnumerator(head);

        /// <summary>
        /// Enumerates payloads by reference, so <c>foreach (ref var v in list.Values)</c>
        /// can write to them in place.
        /// </summary>
        public readonly ValueEnumerator Values => new ValueEnumerator(head);

        /// <summary>Walks the list forwards, yielding node handles.</summary>
        public struct NodeEnumerator
        {
            private Handle<T, ListMode> current;
            private Handle<T, ListMode> following;

            internal NodeEnumerator(Handle<T, ListMode> from)
            {
                current = Handle<T, ListMode>.Null;
                following = from;
            }

            /// <summary>The node at the current position.</summary>
            public readonly Handle<T, ListMode> Current => current;

            /// <summary>Advances to the next node.</summary>
            /// <returns>False once the end of the list is reached.</returns>
            public bool MoveNext()
            {
                if (following.IsNull) return false;
                current = following;
                following = Arena<T>.Next(current);
                return true;
            }
        }

        /// <summary>Walks the list forwards, yielding payloads by reference.</summary>
        public struct ValueEnumerator
        {
            private Handle<T, ListMode> current;
            private Handle<T, ListMode> following;

            internal ValueEnumerator(Handle<T, ListMode> from)
            {
                current = Handle<T, ListMode>.Null;
                following = from;
            }

            /// <summary>Returns itself, so the type can be used directly in foreach.</summary>
            /// <returns>This enumerator.</returns>
            public readonly ValueEnumerator GetEnumerator() => this;

            /// <summary>The payload at the current position, by reference.</summary>
            public ref T Current => ref Arena<T>.Value(current);

            /// <summary>Advances to the next node.</summary>
            /// <returns>False once the end of the list is reached.</returns>
            public bool MoveNext()
            {
                if (following.IsNull) return false;
                current = following;
                following = Arena<T>.Next(current);
                return true;
            }
        }

        // ---------------------------------------------------------------------
        //  Diagnostics
        // ---------------------------------------------------------------------

        /// <summary>
        /// Walks both directions and checks they agree with each other and with
        /// the count. A broken backward link is invisible to a forward-only walk.
        /// </summary>
        public readonly void Validate()
        {
            if (count == 0)
            {
                if (!head.IsNull || !tail.IsNull)
                    throw new InvalidOperationException($"empty list with head={head} tail={tail}");
                return;
            }

            if (!Arena<T>.Previous(head).IsNull)
                throw new InvalidOperationException($"the first node {head} has a predecessor");
            if (!Arena<T>.Next(tail).IsNull)
                throw new InvalidOperationException($"the last node {tail} has a successor");

            long seen = 0;
            var preceding = Handle<T, ListMode>.Null;
            for (var node = head; !node.IsNull; node = Arena<T>.Next(node))
            {
                if (Arena<T>.Previous(node) != preceding)
                    throw new InvalidOperationException(
                        $"{node}: predecessor reads {Arena<T>.Previous(node)}, expected {preceding}");
                preceding = node;
                if (++seen > count)
                    throw new InvalidOperationException($"more than {count} nodes: cycle?");
            }

            if (seen != count)
                throw new InvalidOperationException($"walked {seen} nodes but the count reports {count}");
            if (preceding != tail)
                throw new InvalidOperationException($"the walk ended at {preceding}, not at {tail}");
        }
    }
}
