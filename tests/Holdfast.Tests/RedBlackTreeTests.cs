using System;
using System.Collections.Generic;
using Xunit;

namespace Holdfast.Tests
{
    public class RedBlackTreeTests
    {
        private static RedBlackTree<long, LongComparer> NewTree() => RedBlackTree<long, LongComparer>.Create();

        private static void ResetArenas()
        {
            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();
        }

        [Fact]
        public void InsertsAndFinds()
        {
            ResetArenas();
            var tree = NewTree();

            Assert.True(tree.IsEmpty, "a fresh tree should be empty");

            foreach (long value in new long[] { 50, 25, 75, 10, 30, 60, 90 }) tree.Insert(value);
            tree.Validate();

            Assert.Equal(7L, tree.NodeCount);
            Assert.Equal(7L, tree.ValueCount);

            foreach (long value in new long[] { 50, 25, 75, 10, 30, 60, 90 })
            {
                Assert.True(tree.Contains(value), $"did not find {value}");
            }
            foreach (long value in new long[] { 0, 15, 51, 100 })
            {
                Assert.True(!tree.Contains(value), $"found {value}, which was never inserted");
            }

            tree.Clear();
            ResetArenas();
        }

        [Fact]
        public void DuplicateValuesAccumulateInOneNode()
        {
            ResetArenas();
            var tree = NewTree();

            tree.Insert(10);
            tree.Insert(20);
            tree.Insert(10);
            tree.Insert(10);
            tree.Validate();

            Assert.True(tree.NodeCount == 2, $"there should be 2 distinct nodes, there are {tree.NodeCount}");
            Assert.True(tree.ValueCount == 4, $"there should be 4 values, there are {tree.ValueCount}");

            var node = tree.Find(10);
            Assert.True(!node.IsNull, "did not find the node for 10");
            Assert.True(tree.ValuesOf(node).Count == 3, $"the node for 10 holds {tree.ValuesOf(node).Count} values");

            // Removing one value leaves the node standing.
            Assert.True(tree.Remove(10), "Remove(10) returned false");
            tree.Validate();
            Assert.True(tree.NodeCount == 2 && tree.ValueCount == 3,
                $"after removing one: {tree.NodeCount} nodes, {tree.ValueCount} values");

            tree.Remove(10);
            tree.Remove(10);
            tree.Validate();
            Assert.True(tree.NodeCount == 1, $"the node should go once its values run out; {tree.NodeCount} remain");
            Assert.True(!tree.Contains(10), "10 should be gone");

            tree.Clear();
            ResetArenas();
        }

        [Fact]
        public void WalksInOrder()
        {
            ResetArenas();
            var tree = NewTree();

            long[] input = { 50, 25, 75, 10, 30, 60, 90, 5, 15 };
            foreach (long value in input) tree.Insert(value);

            var seen = new List<long>();
            foreach (var node in tree) seen.Add(tree.ValuesOf(node).FirstValue);

            var expected = new List<long>(input);
            expected.Sort();

            Assert.True(SameSequence(seen, expected), $"order: {string.Join(",", seen)}");
            Assert.True(tree.ValuesOf(tree.First).FirstValue == 5, "the first should be 5");
            Assert.True(tree.ValuesOf(tree.Last).FirstValue == 90, "the last should be 90");

            tree.Clear();
            ResetArenas();
        }

        [Fact]
        public void SuccessorAndPredecessorCoverTheWholeTree()
        {
            ResetArenas();
            var tree = NewTree();
            for (long value = 0; value < 100; value++) tree.Insert(value * 3);

            long expected = 0;
            for (var node = tree.First; !node.IsNull; node = RedBlackTree<long, LongComparer>.Successor(node))
            {
                Assert.True(tree.ValuesOf(node).FirstValue == expected,
                    $"successor: expected {expected}, got {tree.ValuesOf(node).FirstValue}");
                expected += 3;
            }
            Assert.True(expected == 300, $"the forward walk ended at {expected}");

            expected = 297;
            for (var node = tree.Last; !node.IsNull; node = RedBlackTree<long, LongComparer>.Predecessor(node))
            {
                Assert.True(tree.ValuesOf(node).FirstValue == expected,
                    $"predecessor: expected {expected}, got {tree.ValuesOf(node).FirstValue}");
                expected -= 3;
            }
            Assert.True(expected == -3, $"the backward walk ended at {expected}");

            tree.Clear();
            ResetArenas();
        }

        [Fact]
        public void RemovesLeavesAndSingleChildren()
        {
            ResetArenas();
            var tree = NewTree();
            foreach (long value in new long[] { 50, 25, 75, 10 }) tree.Insert(value);

            Assert.True(tree.Remove(10), "did not remove the leaf");
            tree.Validate();
            Assert.True(tree.NodeCount == 3 && !tree.Contains(10), "10 is still there");

            Assert.True(tree.Remove(75), "did not remove 75");
            tree.Validate();
            Assert.True(tree.NodeCount == 2 && !tree.Contains(75), "75 is still there");

            Assert.True(!tree.Remove(999), "removed something that did not exist");

            tree.Clear();
            ResetArenas();
        }

        // =====================================================================
        //  Regressions
        //
        //  Removing a node with two children copies the predecessor's value
        //  list up and then releases the predecessor. If the predecessor is
        //  released while it still owns that list, the surviving node's values
        //  are freed underneath it. If the node's own list is overwritten
        //  before being released, those values leak. Both are invisible except
        //  by counting allocated nodes.
        // =====================================================================

        [Fact]
        public void RemovingANodeWithTwoChildrenNeitherLeaksNorDoubleFrees()
        {
            ResetArenas();
            var tree = NewTree();

            // 25 ends up with two children (10 and 30); its predecessor is 10.
            foreach (long value in new long[] { 50, 25, 75, 10, 30 }) tree.Insert(value);
            Assert.True(Arena<long>.Allocated == 5, $"allocated before = {Arena<long>.Allocated}");

            Assert.True(tree.Remove(25), "did not remove 25");
            tree.Validate();

            Assert.True(tree.NodeCount == 4, $"{tree.NodeCount} nodes remain");
            Assert.True(tree.ValueCount == 4, $"{tree.ValueCount} values remain");
            Assert.True(!tree.Contains(25), "25 is still there");

            // The survivors must still be readable: if the predecessor lost its
            // list, this is where it breaks.
            foreach (long value in new long[] { 10, 30, 50, 75 })
            {
                var node = tree.Find(value);
                Assert.True(!node.IsNull, $"{value} disappeared");
                Assert.True(tree.ValuesOf(node).Count == 1, $"the node for {value} holds {tree.ValuesOf(node).Count} values");
                Assert.True(tree.ValuesOf(node).FirstValue == value, $"the node for {value} holds {tree.ValuesOf(node).FirstValue}");
            }

            Assert.True(Arena<long>.Allocated == 4,
                $"allocated after = {Arena<long>.Allocated}, expected 4 (leak or double free?)");

            tree.Clear();
            ResetArenas();
        }

        [Fact]
        public void RemovingATwoChildNodeCarriesTheWholePredecessorList()
        {
            ResetArenas();
            var tree = NewTree();

            foreach (long value in new long[] { 50, 25, 75, 10, 30 }) tree.Insert(value);
            tree.Insert(10);   // the predecessor of 25 now holds several values
            tree.Insert(10);
            tree.Insert(25);   // and so does 25
            Assert.True(Arena<long>.Allocated == 8, $"allocated before = {Arena<long>.Allocated}");

            // Removes one value from 25; the node stays.
            tree.Remove(25);
            tree.Validate();
            Assert.True(tree.NodeCount == 5 && tree.ValueCount == 7,
                $"{tree.NodeCount} nodes, {tree.ValueCount} values");

            // Now the node disappears, with two children and a predecessor
            // carrying a list of its own.
            tree.Remove(25);
            tree.Validate();

            Assert.True(tree.NodeCount == 4, $"{tree.NodeCount} nodes remain");
            Assert.True(tree.ValueCount == 6, $"{tree.ValueCount} values remain");

            var ten = tree.Find(10);
            Assert.True(!ten.IsNull, "10 disappeared");
            Assert.True(tree.ValuesOf(ten).Count == 3,
                $"the node for 10 holds {tree.ValuesOf(ten).Count} values, expected 3");
            foreach (ref var value in tree.ValuesOf(ten).Values)
            {
                Assert.True(value == 10, $"the node for 10 contains a {value}");
            }

            Assert.True(Arena<long>.Allocated == 6,
                $"allocated after = {Arena<long>.Allocated}, expected 6");

            tree.Clear();
            ResetArenas();
        }

        [Fact]
        public void ClearLeavesNothingAllocated()
        {
            ResetArenas();
            var tree = NewTree();

            var random = new Random(1234);
            for (int i = 0; i < 5000; i++) tree.Insert(random.Next(1000));
            tree.Validate();

            tree.Clear();

            Assert.True(tree.IsEmpty && tree.NodeCount == 0 && tree.ValueCount == 0, "the tree is not empty");
            Assert.True(Arena<long>.Allocated == 0, $"{Arena<long>.Allocated} values were not freed");
            Assert.True(Arena<ArenaList<long>>.Allocated == 0, $"{Arena<ArenaList<long>>.Allocated} nodes were not freed");

            Arena<long>.ValidateIntegrity();
            Arena<ArenaList<long>>.ValidateIntegrity();
            ResetArenas();
        }

        [Fact]
        public void BulkInsertionKeepsTheInvariants()
        {
            ResetArenas();
            var tree = NewTree();

            for (long value = 0; value < 20_000; value++) tree.Insert(value);
            tree.Validate();
            Assert.Equal(20_000L, tree.NodeCount);
            tree.Clear();

            for (long value = 20_000; value > 0; value--) tree.Insert(value);
            tree.Validate();
            Assert.Equal(20_000L, tree.NodeCount);
            tree.Clear();

            ResetArenas();
        }

        [Fact]
        public void MatchesSortedDictionaryOverRandomOperations()
        {
            const int seed = 20260807;
            var random = new Random(seed);

            ResetArenas();
            var tree = NewTree();
            var reference = new SortedDictionary<long, int>();   // key -> how many times

            for (int step = 0; step < 30_000; step++)
            {
                // Small key range on purpose: forces duplicate keys and nodes
                // with several values, which is where the bug lived.
                long key = random.Next(300);

                if (random.Next(100) < 60)
                {
                    tree.Insert(key);
                    reference.TryGetValue(key, out int count);
                    reference[key] = count + 1;
                }
                else
                {
                    bool removed = tree.Remove(key);
                    bool expected = reference.TryGetValue(key, out int count);

                    Assert.True(removed == expected,
                        $"step {step}: Remove({key}) returned {removed}, expected {expected}");

                    if (expected)
                    {
                        if (count == 1) reference.Remove(key);
                        else reference[key] = count - 1;
                    }
                }

                if (step % 1000 == 0) Compare(ref tree, reference, step);
            }

            Compare(ref tree, reference, -1);

            tree.Clear();
            Assert.True(Arena<long>.Allocated == 0, $"{Arena<long>.Allocated} values were not freed");
            Assert.True(Arena<ArenaList<long>>.Allocated == 0, $"{Arena<ArenaList<long>>.Allocated} nodes were not freed");
            ResetArenas();
        }

        // =====================================================================

        private static void Compare(ref RedBlackTree<long, LongComparer> tree,
                                    SortedDictionary<long, int> reference, int step)
        {
            string where = step < 0 ? "at the end" : $"at step {step}";

            tree.Validate();

            Assert.True(tree.NodeCount == reference.Count, $"nodes {tree.NodeCount} != {reference.Count} {where}");

            long referenceTotal = 0;
            foreach (var pair in reference) referenceTotal += pair.Value;
            Assert.True(tree.ValueCount == referenceTotal, $"values {tree.ValueCount} != {referenceTotal} {where}");

            var keys = new List<long>();
            var counts = new List<int>();
            foreach (var node in tree)
            {
                keys.Add(tree.ValuesOf(node).FirstValue);
                counts.Add((int)tree.ValuesOf(node).Count);
            }

            int index = 0;
            foreach (var pair in reference)
            {
                Assert.True(keys[index] == pair.Key, $"key {index}: {keys[index]} != {pair.Key} {where}");
                Assert.True(counts[index] == pair.Value,
                    $"key {pair.Key} holds {counts[index]} values, expected {pair.Value} {where}");
                index++;
            }
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
