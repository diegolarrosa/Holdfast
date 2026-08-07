using System.Diagnostics;

namespace Holdfast
{
    /// <summary>
    /// In-order traversal of an arena-backed tree using an explicit stack.
    /// </summary>
    /// <remarks>
    /// The trees also walk in order through parent links, which works from any
    /// node with no prior state and is right for jumping around. This one is
    /// faster when walking everything: it only touches nodes that are already
    /// hot, instead of re-reading ancestors on the way up.
    /// <para>
    /// The stack costs O(log n), not O(n): a red-black tree is at most
    /// 2*log2(n+1) deep, so with 42-bit addresses the hard ceiling is 84
    /// levels. One 768-byte allocation per traversal.
    /// </para>
    /// </remarks>
    /// <typeparam name="TN">
    /// The node payload type: <c>T</c> for <see cref="RedBlackSet{T, TComp}"/>,
    /// <c>ArenaList&lt;T&gt;</c> for <see cref="RedBlackTree{T, TComp}"/>.
    /// </typeparam>
    public readonly struct InOrder<TN> where TN : struct
    {
        /// <summary>2*log2(2^42) = 84, rounded up.</summary>
        public const int MaxDepth = 96;

        private readonly Handle<TN, TreeMode> root;

        /// <summary>Creates a traversal rooted at the given node.</summary>
        /// <param name="root">The root of the tree to walk, or the null handle for an empty tree.</param>
        public InOrder(Handle<TN, TreeMode> root) => this.root = root;

        /// <summary>Each foreach builds its own enumerator with its own stack.</summary>
        public Enumerator GetEnumerator() => new Enumerator(root);

        /// <summary>Walks the tree in order, keeping the pending left spine on a stack.</summary>
        public struct Enumerator
        {
            private readonly Handle<TN, TreeMode>[] stack;
            private int top;
            private Handle<TN, TreeMode> current;

            internal Enumerator(Handle<TN, TreeMode> root)
            {
                stack = new Handle<TN, TreeMode>[MaxDepth];
                top = 0;
                current = Handle<TN, TreeMode>.Null;
                PushLeftSpine(root);
            }

            private void PushLeftSpine(Handle<TN, TreeMode> node)
            {
                while (!node.IsNull)
                {
                    Debug.Assert(top < MaxDepth,
                        "stack overflow: the tree is deeper than a valid red-black tree allows");
                    stack[top++] = node;
                    node = Arena<TN>.Left(node);
                }
            }

            /// <summary>The node at the current position.</summary>
            public readonly Handle<TN, TreeMode> Current => current;

            /// <summary>Advances to the next node in order.</summary>
            /// <returns>False once every node has been visited.</returns>
            public bool MoveNext()
            {
                if (top == 0) return false;
                current = stack[--top];
                PushLeftSpine(Arena<TN>.Right(current));
                return true;
            }
        }
    }
}
