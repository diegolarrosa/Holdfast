using System.Collections.Generic;
using Xunit;

namespace Holdfast.Tests
{
    public class ArenaTests
    {
        // Tests that cross a block boundary allocate ~1M nodes of 24 bytes,
        // so roughly 25 MB per block. Nothing dramatic.

        [Fact]
        public void CountersAndIntegrityAddUp()
        {
            Arena<long>.Reset();
            Arena<long>.ValidateIntegrity();

            var handles = new List<Handle<long, RawMode>>();
            for (int i = 0; i < 1000; i++) handles.Add(Arena<long>.Allocate());

            Assert.Equal(1000L, Arena<long>.Allocated);
            Assert.Equal(1000L, Arena<long>.TotalAllocations);
            Arena<long>.ValidateIntegrity();

            foreach (var handle in handles) Arena<long>.Free(handle);

            Assert.Equal(0L, Arena<long>.Allocated);
            Assert.True(Arena<long>.TotalAllocations == 1000,
                "the running total must not go down when nodes are freed");
            Arena<long>.ValidateIntegrity();

            Arena<long>.Reset();
        }

        [Fact]
        public void AddressesAreNeverHandedOutTwice()
        {
            Arena<long>.Reset();

            var seen = new HashSet<Handle<long, RawMode>>();
            for (int i = 0; i < 200_000; i++)
            {
                Assert.True(seen.Add(Arena<long>.Allocate()), $"duplicate address at allocation {i}");
            }

            Arena<long>.Reset();
        }

        [Fact]
        public void OneBlockYieldsExactlyBlockSizeNodes()
        {
            // An earlier version lost element 0 of every block, so it asked for
            // a second block one allocation too early.
            Arena<long>.Reset();

            for (int i = 0; i < Layout.BlockSize; i++) Arena<long>.Allocate();

            Assert.True(Arena<long>.BlockCount == 1,
                $"one block should yield {Layout.BlockSize:N0} nodes, but there are already {Arena<long>.BlockCount} blocks");
            Assert.Equal(0L, Arena<long>.Available);
            Arena<long>.ValidateIntegrity();

            Arena<long>.Allocate();
            Assert.True(Arena<long>.BlockCount == 2, "the next node should have forced a new block");

            Arena<long>.Reset();
        }

        [Fact]
        public void PayloadsSurviveAcrossABlockBoundary()
        {
            // Writes and reads back a distinct value in every node on both
            // sides of the boundary. Broken two-level indexing shows up here.
            Arena<long>.Reset();
            const int count = Layout.BlockSize + 5000;

            var handles = new Handle<long, ListMode>[count];
            for (int i = 0; i < count; i++)
            {
                handles[i] = Arena<long>.AsList(Arena<long>.Allocate());
                Arena<long>.Value(handles[i]) = i * 2654435761L;
            }

            Assert.True(Arena<long>.BlockCount == 2, $"expected 2 blocks, found {Arena<long>.BlockCount}");

            for (int i = 0; i < count; i++)
            {
                Assert.True(Arena<long>.Value(handles[i]) == i * 2654435761L,
                    $"node {i} ({handles[i]}) returned {Arena<long>.Value(handles[i])}");
            }

            Arena<long>.Reset();
        }

        [Fact]
        public void RecyclingDoesNotConsumeNewBlocks()
        {
            Arena<long>.Reset();

            var handles = new List<Handle<long, RawMode>>();
            for (int i = 0; i < 50_000; i++) handles.Add(Arena<long>.Allocate());

            long blocksBefore = Arena<long>.BlockCount;
            foreach (var handle in handles) Arena<long>.Free(handle);
            Arena<long>.ValidateIntegrity();

            for (int i = 0; i < 50_000; i++) Arena<long>.Allocate();

            Assert.True(Arena<long>.BlockCount == blocksBefore,
                $"reallocating the same count asked for new blocks: {blocksBefore} -> {Arena<long>.BlockCount}");
            Arena<long>.ValidateIntegrity();

            Arena<long>.Reset();
        }

        // ---------------------------------------------------------------------
        //  Raw linking primitives. ArenaList<T> is built from these.
        // ---------------------------------------------------------------------

        [Fact]
        public void AddAfterLinksInBothDirections()
        {
            // An earlier version never wrote the predecessor's forward link, so
            // the node was inserted but nothing pointed at it.
            Arena<long>.Reset();

            var a = NewListNode(1);
            var c = NewListNode(3);
            Arena<long>.AddAfter(a, c);          // a <-> c

            var b = NewListNode(2);
            Arena<long>.AddAfter(a, b);          // a <-> b <-> c

            Assert.True(Arena<long>.Next(a) == b, $"Next(a) = {Arena<long>.Next(a)}, expected {b}");
            Assert.True(Arena<long>.Previous(b) == a, "Previous(b) != a");
            Assert.True(Arena<long>.Next(b) == c, "Next(b) != c");
            Assert.True(Arena<long>.Previous(c) == b, "Previous(c) != b");
            Assert.True(Arena<long>.Next(c).IsNull, "c should be last");
            Assert.True(Arena<long>.Previous(a).IsNull, "a should be first");

            Arena<long>.Reset();
        }

        [Fact]
        public void UnlinkFromTheMiddleJoinsTheNeighbours()
        {
            Arena<long>.Reset();

            var a = NewListNode(1);
            var b = NewListNode(2);
            var c = NewListNode(3);
            Arena<long>.AddAfter(a, b);
            Arena<long>.AddAfter(b, c);

            Arena<long>.Unlink(b);

            Assert.True(Arena<long>.Next(a) == c, "a should now link straight to c");
            Assert.True(Arena<long>.Previous(c) == a, "c should now link back to a");

            Arena<long>.Reset();
        }

        [Fact]
        public void RawLinkingMatchesLinkedList()
        {
            const int seed = 20260805;
            var random = new System.Random(seed);
            Arena<long>.Reset();

            var reference = new LinkedList<long>();
            var head = Handle<long, ListMode>.Null;
            var tail = Handle<long, ListMode>.Null;
            long count = 0;

            for (int step = 0; step < 50_000; step++)
            {
                int op = count == 0 ? random.Next(2) : random.Next(4);

                switch (op)
                {
                    case 0:
                    {
                        long value = random.Next();
                        var node = NewListNode(value);
                        head = Arena<long>.AddFirst(head, node);
                        if (tail.IsNull) tail = head;
                        count++;
                        reference.AddFirst(value);
                        break;
                    }
                    case 1:
                    {
                        long value = random.Next();
                        var node = NewListNode(value);
                        tail = Arena<long>.AddLast(tail, node);
                        if (head.IsNull) head = tail;
                        count++;
                        reference.AddLast(value);
                        break;
                    }
                    case 2:
                    {
                        var removed = head;
                        head = Arena<long>.RemoveFirst(head);
                        if (removed == tail) tail = Handle<long, ListMode>.Null;
                        Arena<long>.Free(removed);
                        count--;
                        reference.RemoveFirst();
                        break;
                    }
                    default:
                    {
                        var removed = tail;
                        tail = Arena<long>.RemoveLast(tail);
                        if (removed == head) head = Handle<long, ListMode>.Null;
                        Arena<long>.Free(removed);
                        count--;
                        reference.RemoveLast();
                        break;
                    }
                }

                if (step % 500 == 0) CompareBothDirections(head, tail, count, reference, step);
            }

            CompareBothDirections(head, tail, count, reference, -1);
            Arena<long>.ValidateIntegrity();
            Arena<long>.Reset();
        }

        // ---------------------------------------------------------------------

        private static Handle<long, ListMode> NewListNode(long value)
        {
            var node = Arena<long>.AsList(Arena<long>.Allocate());
            Arena<long>.Value(node) = value;
            return node;
        }

        /// <summary>
        /// Walks both ways. Comparing only forwards lets a broken backward link
        /// through unnoticed.
        /// </summary>
        private static void CompareBothDirections(
            Handle<long, ListMode> head,
            Handle<long, ListMode> tail,
            long count,
            LinkedList<long> reference,
            int step)
        {
            string where = step < 0 ? "at the end" : $"at step {step}";

            Assert.True(count == reference.Count, $"count {count} != {reference.Count} {where}");

            var forward = new List<long>();
            for (var node = head; !node.IsNull; node = Arena<long>.Next(node))
            {
                forward.Add(Arena<long>.Value(node));
                Assert.True(forward.Count <= reference.Count + 1, $"list longer than the reference {where}");
            }

            var backward = new List<long>();
            for (var node = tail; !node.IsNull; node = Arena<long>.Previous(node))
            {
                backward.Add(Arena<long>.Value(node));
            }
            backward.Reverse();

            var expected = new List<long>(reference);
            Assert.True(SameSequence(forward, expected), $"forward walk differs {where}");
            Assert.True(SameSequence(backward, expected), $"backward walk differs {where}");
        }

        private static bool SameSequence(List<long> a, List<long> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
