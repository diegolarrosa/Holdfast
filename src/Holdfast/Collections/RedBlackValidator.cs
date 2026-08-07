using System;

namespace Holdfast
{
    /// <summary>
    /// Checks the red-black invariants of a tree in the arena.
    /// </summary>
    /// <remarks>
    /// Works purely on <c>Handle&lt;T, TreeMode&gt;</c> and knows nothing about
    /// how the tree above it is written, so it validates any structure that
    /// uses the arena's tree view.
    /// </remarks>
    public static class RedBlackValidator<T> where T : struct
    {
        /// <summary>
        /// Verifies the five properties plus parent-link coherence and returns
        /// the node count. Throws with a specific message on the first failure.
        /// </summary>
        /// <param name="root">The root of the tree to check, or the null handle.</param>
        /// <returns>The number of nodes visited.</returns>
        public static long Validate(Handle<T, TreeMode> root)
        {
            if (root.IsNull) return 0;

            if (Arena<T>.Color(root))
                throw new InvalidOperationException("property 2: the root must be black");

            if (!Arena<T>.Parent(root).IsNull)
                throw new InvalidOperationException($"the root {root} has a parent: {Arena<T>.Parent(root)}");

            int blackHeight = -1;
            return Walk(root, 0, ref blackHeight);
        }

        private static long Walk(Handle<T, TreeMode> node, int blacksSoFar, ref int blackHeight)
        {
            if (node.IsNull)
            {
                // Property 5: every path to a null leaf crosses the same number
                // of black nodes.
                if (blackHeight == -1) blackHeight = blacksSoFar;
                else if (blackHeight != blacksSoFar)
                    throw new InvalidOperationException(
                        $"property 5: black height {blacksSoFar}, expected {blackHeight}");
                return 0;
            }

            bool red = Arena<T>.Color(node);
            var left = Arena<T>.Left(node);
            var right = Arena<T>.Right(node);

            // Property 4: a red node cannot have red children.
            if (red)
            {
                if (!left.IsNull && Arena<T>.Color(left))
                    throw new InvalidOperationException($"property 4: red {node} with red left child {left}");
                if (!right.IsNull && Arena<T>.Color(right))
                    throw new InvalidOperationException($"property 4: red {node} with red right child {right}");
            }

            if (!left.IsNull && Arena<T>.Parent(left) != node)
                throw new InvalidOperationException($"{left} is a child of {node} but its parent reads {Arena<T>.Parent(left)}");
            if (!right.IsNull && Arena<T>.Parent(right) != node)
                throw new InvalidOperationException($"{right} is a child of {node} but its parent reads {Arena<T>.Parent(right)}");

            int blacks = blacksSoFar + (red ? 0 : 1);
            return 1 + Walk(left, blacks, ref blackHeight)
                     + Walk(right, blacks, ref blackHeight);
        }
    }
}
