using System;
using System.Collections.Generic;
using Xunit;

namespace Holdfast.Tests
{
    public class ArenaListTests
    {
        [Fact]
        public void AddsAndRemovesAtBothEnds()
        {
            Arena<long>.Reset();
            var list = ArenaList<long>.Empty;

            Assert.True(list.IsEmpty, "a fresh list should be empty");
            Assert.True(list.First.IsNull && list.Last.IsNull, "non-null handles on an empty list");

            list.AddLast(10);
            list.AddLast(20);
            list.AddFirst(5);
            list.Validate();

            Assert.Equal(3L, list.Count);
            Assert.Equal(5L, list.FirstValue);
            Assert.Equal(20L, list.LastValue);

            list.RemoveFirst();
            list.Validate();
            Assert.True(list.FirstValue == 10, "after RemoveFirst the head should be 10");

            list.RemoveLast();
            list.Validate();
            Assert.True(list.Count == 1 && list.FirstValue == 10 && list.LastValue == 10,
                "with a single element, first and last are the same node");

            list.RemoveLast();
            list.Validate();
            Assert.True(list.IsEmpty && list.First.IsNull && list.Last.IsNull,
                "emptying the list must return both handles to null");

            Arena<long>.Reset();
        }

        [Fact]
        public void RemovesFromTheMiddle()
        {
            Arena<long>.Reset();
            var list = ArenaList<long>.Empty;

            list.AddLast(1);
            var middle = list.AddLast(2);
            list.AddLast(3);

            list.Remove(middle);
            list.Validate();

            Assert.Equal(2L, list.Count);
            Assert.True(list.FirstValue == 1 && list.LastValue == 3, "the ends should remain");

            Arena<long>.Reset();
        }

        [Fact]
        public void AddAfterMovesTheTailOnlyWhenItShould()
        {
            Arena<long>.Reset();
            var list = ArenaList<long>.Empty;

            var first = list.AddLast(1);
            list.AddAfter(first, 2);            // lands at the end
            list.Validate();
            Assert.True(list.LastValue == 2, "inserting after the last node must move the tail");

            list.AddAfter(first, 99);           // lands in the middle
            list.Validate();
            Assert.True(list.Count == 3 && list.LastValue == 2, "the tail should not have moved");

            var values = new List<long>();
            foreach (ref var value in list.Values) values.Add(value);
            Assert.True(values.Count == 3 && values[0] == 1 && values[1] == 99 && values[2] == 2,
                $"unexpected order: {string.Join(",", values)}");

            Arena<long>.Reset();
        }

        [Fact]
        public void TraversalsReadAndWriteInPlace()
        {
            Arena<long>.Reset();
            var list = ArenaList<long>.Empty;
            for (int i = 0; i < 10; i++) list.AddLast(i);

            long total = 0;
            foreach (var node in list) total += ArenaList<long>.ValueOf(node);
            Assert.True(total == 45, $"sum over handles = {total}");

            foreach (ref var value in list.Values) value *= 2;

            total = 0;
            foreach (ref var value in list.Values) total += value;
            Assert.True(total == 90, $"sum after doubling = {total}");

            Arena<long>.Reset();
        }

        [Fact]
        public void ClearReturnsEveryNode()
        {
            Arena<long>.Reset();
            var list = ArenaList<long>.Empty;
            for (int i = 0; i < 1000; i++) list.AddLast(i);

            Assert.Equal(1000L, Arena<long>.Allocated);

            list.Clear();
            list.Validate();

            Assert.True(list.IsEmpty, "Clear should leave the list empty");
            Assert.True(Arena<long>.Allocated == 0, $"{Arena<long>.Allocated} nodes were not freed");
            Arena<long>.ValidateIntegrity();

            Arena<long>.Reset();
        }

        [Fact]
        public void RemovingFromAnEmptyListThrows()
        {
            Arena<long>.Reset();

            var a = ArenaList<long>.Empty;
            Assert.Throws<InvalidOperationException>(() => a.RemoveFirst());

            var b = ArenaList<long>.Empty;
            Assert.Throws<InvalidOperationException>(() => b.RemoveLast());

            var c = ArenaList<long>.Empty;
            Assert.Throws<InvalidOperationException>(() => { long _ = c.FirstValue; });

            Arena<long>.Reset();
        }

        [Fact]
        public void MatchesLinkedListOverRandomOperations()
        {
            const int seed = 20260806;
            var random = new Random(seed);

            Arena<long>.Reset();
            var list = ArenaList<long>.Empty;
            var reference = new LinkedList<long>();

            for (int step = 0; step < 50_000; step++)
            {
                int op = list.IsEmpty ? random.Next(2) : random.Next(5);

                switch (op)
                {
                    case 0:
                    {
                        long value = random.Next(1000);
                        list.AddFirst(value);
                        reference.AddFirst(value);
                        break;
                    }
                    case 1:
                    {
                        long value = random.Next(1000);
                        list.AddLast(value);
                        reference.AddLast(value);
                        break;
                    }
                    case 2:
                        list.RemoveFirst();
                        reference.RemoveFirst();
                        break;

                    case 3:
                        list.RemoveLast();
                        reference.RemoveLast();
                        break;

                    default:
                    {
                        // Remove the k-th node, to exercise the middle branch.
                        int k = random.Next((int)list.Count);

                        var node = list.First;
                        for (int i = 0; i < k; i++) node = ArenaList<long>.Next(node);
                        Assert.True(!node.IsNull, $"the list ran out of nodes before {k}");

                        var referenceNode = reference.First;
                        for (int i = 0; i < k && referenceNode != null; i++) referenceNode = referenceNode.Next;
                        if (referenceNode is null)
                        {
                            Assert.Fail($"the reference ran out of nodes before {k}");
                            return;
                        }

                        list.Remove(node);
                        reference.Remove(referenceNode);
                        break;
                    }
                }

                if (step % 500 == 0) Compare(list, reference, step);
            }

            Compare(list, reference, -1);
            list.Clear();
            Arena<long>.ValidateIntegrity();
            Arena<long>.Reset();
        }

        // =====================================================================
        //  Lists of lists
        // =====================================================================

        [Fact]
        public void InnerListsAreMutatedThroughRef()
        {
            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();

            var outer = ArenaList<ArenaList<long>>.Empty;

            for (int i = 0; i < 3; i++)
            {
                var node = outer.AddLast(ArenaList<long>.Empty);
                // ValueOf returns a ref, so the inner list is mutated in place —
                // no copy out, no store back.
                ref var inner = ref ArenaList<ArenaList<long>>.ValueOf(node);
                for (int j = 0; j <= i; j++) inner.AddLast(i * 100 + j);
            }

            outer.Validate();
            Assert.Equal(3L, outer.Count);

            int index = 0;
            foreach (var node in outer)
            {
                ref var inner = ref ArenaList<ArenaList<long>>.ValueOf(node);
                inner.Validate();
                Assert.True(inner.Count == index + 1,
                    $"inner list {index} holds {inner.Count} values, expected {index + 1}");
                Assert.True(inner.FirstValue == index * 100,
                    $"first value of inner list {index} = {inner.FirstValue}");
                index++;
            }

            foreach (var node in outer) ArenaList<ArenaList<long>>.ValueOf(node).Clear();
            outer.Clear();

            Assert.True(Arena<long>.Allocated == 0, $"{Arena<long>.Allocated} inner nodes were not freed");
            Assert.True(Arena<ArenaList<long>>.Allocated == 0, $"{Arena<ArenaList<long>>.Allocated} outer nodes were not freed");

            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();
        }

        [Fact]
        public void OperatingOnOneListNeverTouchesAnother()
        {
            // Regression. An earlier design had one API taking an explicit list
            // and another operating on an ambient "current list"; the head and
            // tail branches of the explicit one called the ambient ones, so
            // removing from one list modified a different one.
            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();

            var outer = ArenaList<ArenaList<long>>.Empty;
            var nodeA = outer.AddLast(ArenaList<long>.Empty);
            var nodeB = outer.AddLast(ArenaList<long>.Empty);

            ref var a = ref ArenaList<ArenaList<long>>.ValueOf(nodeA);
            a.AddLast(1); a.AddLast(2); a.AddLast(3);

            ref var b = ref ArenaList<ArenaList<long>>.ValueOf(nodeB);
            b.AddLast(10); b.AddLast(20); b.AddLast(30);

            ArenaList<ArenaList<long>>.ValueOf(nodeA).RemoveFirst();

            ref var a2 = ref ArenaList<ArenaList<long>>.ValueOf(nodeA);
            ref var b2 = ref ArenaList<ArenaList<long>>.ValueOf(nodeB);
            a2.Validate();
            b2.Validate();

            Assert.True(a2.Count == 2 && a2.FirstValue == 2, $"A holds {a2.Count}, first {a2.FirstValue}");
            Assert.True(b2.Count == 3 && b2.FirstValue == 10, $"B was modified: {b2.Count} values, first {b2.FirstValue}");

            ArenaList<ArenaList<long>>.ValueOf(nodeB).RemoveLast();

            ref var a3 = ref ArenaList<ArenaList<long>>.ValueOf(nodeA);
            ref var b3 = ref ArenaList<ArenaList<long>>.ValueOf(nodeB);
            Assert.True(a3.Count == 2, $"A was modified while operating on B: {a3.Count}");
            Assert.True(b3.Count == 2 && b3.LastValue == 20, $"B holds {b3.Count}, last {b3.LastValue}");

            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();
        }

        // =====================================================================

        private static void Compare(ArenaList<long> list, LinkedList<long> reference, int step)
        {
            string where = step < 0 ? "at the end" : $"at step {step}";

            list.Validate();
            Assert.True(list.Count == reference.Count, $"count {list.Count} != {reference.Count} {where}");

            var forward = new List<long>();
            foreach (ref var value in list.Values) forward.Add(value);

            var backward = new List<long>();
            for (var node = list.Last; !node.IsNull; node = ArenaList<long>.Previous(node))
            {
                backward.Add(ArenaList<long>.ValueOf(node));
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
