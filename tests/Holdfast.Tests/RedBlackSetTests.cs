using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace Holdfast.Tests
{
    public class RedBlackSetTests
    {
        private readonly ITestOutputHelper output;

        public RedBlackSetTests(ITestOutputHelper output) => this.output = output;

        private static RedBlackSet<long, LongComparer> NewSet() => RedBlackSet<long, LongComparer>.Create();

        private static void ResetArenas()
        {
            Arena<long>.Reset();
            Arena<ArenaList<long>>.Reset();
        }

        [Fact]
        public void InsertsAndFinds()
        {
            Arena<long>.Reset();
            var set = NewSet();

            Assert.True(set.IsEmpty, "a fresh set should be empty");

            foreach (long value in new long[] { 50, 25, 75, 10, 30, 60, 90 }) set.Insert(value);
            set.Validate();

            Assert.Equal(7L, set.Count);

            foreach (long value in new long[] { 50, 25, 75, 10, 30, 60, 90 })
            {
                Assert.True(set.Contains(value), $"did not find {value}");
            }
            foreach (long value in new long[] { 0, 15, 51, 100 })
            {
                Assert.True(!set.Contains(value), $"found {value}, which was never inserted");
            }

            set.Clear();
            Arena<long>.Reset();
        }

        [Fact]
        public void InsertingAnExistingKeyReplacesInstead()
        {
            Arena<long>.Reset();
            var set = NewSet();

            set.Insert(10, out bool firstWasNew);
            Assert.True(firstWasNew, "the first insertion should be an addition");

            set.Insert(20);
            var node = set.Insert(10, out bool secondWasNew);
            Assert.True(!secondWasNew, "inserting an existing key is not an addition");
            set.Validate();

            Assert.Equal(2L, set.Count);
            Assert.Equal(10L, set.ValueOf(node));
            Assert.True(Arena<long>.Allocated == 2,
                $"{Arena<long>.Allocated} nodes allocated: an upsert must not create one");

            set.Clear();
            Arena<long>.Reset();
        }

        [Fact]
        public void WalksInOrder()
        {
            Arena<long>.Reset();
            var set = NewSet();

            long[] input = { 50, 25, 75, 10, 30, 60, 90, 5, 15 };
            foreach (long value in input) set.Insert(value);

            var seen = new List<long>();
            foreach (var node in set) seen.Add(set.ValueOf(node));

            var expected = new List<long>(input);
            expected.Sort();

            Assert.True(SameSequence(seen, expected), $"order: {string.Join(",", seen)}");
            Assert.True(set.ValueOf(set.First) == 5, "the first should be 5");
            Assert.True(set.ValueOf(set.Last) == 90, "the last should be 90");

            set.Clear();
            Arena<long>.Reset();
        }

        [Fact]
        public void BothTraversalsProduceTheSameSequence()
        {
            Arena<long>.Reset();
            var set = NewSet();
            var random = new Random(777);
            for (int i = 0; i < 5000; i++) set.Insert(random.Next(100_000));
            set.Validate();

            var viaParents = new List<long>();
            foreach (var node in set) viaParents.Add(set.ValueOf(node));

            var viaStack = new List<long>();
            foreach (var node in set.InOrder) viaStack.Add(set.ValueOf(node));

            Assert.True(viaParents.Count == set.Count, $"parent walk visited {viaParents.Count} of {set.Count}");
            Assert.True(SameSequence(viaParents, viaStack), "the two traversals disagree");

            // An empty tree must not break either of them.
            set.Clear();
            int count = 0;
            foreach (var unused in set.InOrder) count++;
            Assert.True(count == 0, $"traversing an empty tree yielded {count} nodes");

            Arena<long>.Reset();
        }

        [Fact]
        public void SuccessorAndPredecessorCoverTheWholeTree()
        {
            Arena<long>.Reset();
            var set = NewSet();
            for (long value = 0; value < 100; value++) set.Insert(value * 3);

            long expected = 0;
            for (var node = set.First; !node.IsNull; node = RedBlackSet<long, LongComparer>.Successor(node))
            {
                Assert.True(set.ValueOf(node) == expected,
                    $"successor: expected {expected}, got {set.ValueOf(node)}");
                expected += 3;
            }
            Assert.True(expected == 300, $"the forward walk ended at {expected}");

            expected = 297;
            for (var node = set.Last; !node.IsNull; node = RedBlackSet<long, LongComparer>.Predecessor(node))
            {
                Assert.True(set.ValueOf(node) == expected,
                    $"predecessor: expected {expected}, got {set.ValueOf(node)}");
                expected -= 3;
            }
            Assert.True(expected == -3, $"the backward walk ended at {expected}");

            set.Clear();
            Arena<long>.Reset();
        }

        [Fact]
        public void RemovesLeavesSingleChildrenAndTwoChildren()
        {
            Arena<long>.Reset();
            var set = NewSet();
            foreach (long value in new long[] { 50, 25, 75, 10, 30 }) set.Insert(value);

            Assert.True(set.Remove(10), "did not remove the leaf");
            set.Validate();
            Assert.True(set.Count == 4 && !set.Contains(10), "10 is still there");

            // Rebuild the two-children case around 25.
            set.Insert(10);
            set.Insert(28);
            set.Validate();

            Assert.True(set.Remove(25), "did not remove the node with two children");
            set.Validate();

            Assert.True(!set.Contains(25), "25 is still there");
            foreach (long value in new long[] { 10, 28, 30, 50, 75 })
            {
                Assert.True(set.Contains(value), $"{value} disappeared when 25 was removed");
            }

            Assert.True(Arena<long>.Allocated == 5,
                $"{Arena<long>.Allocated} nodes allocated, expected 5 (leak or double free?)");

            set.Clear();
            Arena<long>.Reset();
        }

        [Fact]
        public void ClearLeavesNothingAllocated()
        {
            Arena<long>.Reset();
            var set = NewSet();

            var random = new Random(4321);
            for (int i = 0; i < 5000; i++) set.Insert(random.Next(10_000));
            set.Validate();

            set.Clear();

            Assert.True(set.IsEmpty && set.Count == 0, "the tree is not empty");
            Assert.True(Arena<long>.Allocated == 0, $"{Arena<long>.Allocated} nodes were not freed");
            Arena<long>.ValidateIntegrity();

            Arena<long>.Reset();
        }

        [Fact]
        public void BulkInsertionKeepsTheInvariants()
        {
            Arena<long>.Reset();
            var set = NewSet();

            // Ascending order is the worst case for an unbalanced tree.
            for (long value = 0; value < 20_000; value++) set.Insert(value);
            set.Validate();
            Assert.Equal(20_000L, set.Count);
            set.Clear();

            // And descending.
            for (long value = 20_000; value > 0; value--) set.Insert(value);
            set.Validate();
            Assert.Equal(20_000L, set.Count);
            set.Clear();

            Arena<long>.Reset();
        }

        [Fact]
        public void MatchesSortedSetOverRandomOperations()
        {
            const int seed = 20260808;
            var random = new Random(seed);

            Arena<long>.Reset();
            var set = NewSet();
            var reference = new SortedSet<long>();

            for (int step = 0; step < 40_000; step++)
            {
                long key = random.Next(500);

                if (random.Next(100) < 55)
                {
                    set.Insert(key, out bool wasNew);
                    bool expectedNew = reference.Add(key);
                    Assert.True(wasNew == expectedNew,
                        $"step {step}: Insert({key}) reported wasNew={wasNew}, expected {expectedNew}");
                }
                else
                {
                    bool removed = set.Remove(key);
                    bool expectedRemoved = reference.Remove(key);
                    Assert.True(removed == expectedRemoved,
                        $"step {step}: Remove({key}) returned {removed}, expected {expectedRemoved}");
                }

                if (step % 1000 == 0) Compare(ref set, reference, step);
            }

            Compare(ref set, reference, -1);

            set.Clear();
            Assert.True(Arena<long>.Allocated == 0, $"{Arena<long>.Allocated} nodes were not freed");
            Arena<long>.Reset();
        }

        /// <summary>
        /// The reason this variant exists: the same number of distinct keys
        /// costs far fewer arena nodes than the list-backed tree.
        /// </summary>
        [Fact]
        public void UsesFarLessMemoryThanTheListBackedTree()
        {
            const int keys = 50_000;

            ResetArenas();
            var set = NewSet();
            for (long value = 0; value < keys; value++) set.Insert(value);

            long setNodes = Arena<long>.Allocated;
            long setBytes = setNodes * 24;

            set.Clear();
            Arena<long>.Reset();

            var tree = RedBlackTree<long, LongComparer>.Create();
            for (long value = 0; value < keys; value++) tree.Insert(value);

            long valueNodes = Arena<long>.Allocated;
            long treeNodes = Arena<ArenaList<long>>.Allocated;
            long treeBytes = valueNodes * 24 + treeNodes * 40;   // ArenaList<long> is 24 + 16

            Assert.True(setNodes == keys, $"the set allocated {setNodes} nodes for {keys} keys");
            Assert.True(valueNodes == keys && treeNodes == keys,
                $"the list-backed tree allocated {valueNodes} values and {treeNodes} tree nodes");

            output.WriteLine($"{keys:N0} keys — set: {setBytes / 1024:N0} KB | " +
                             $"list-backed: {treeBytes / 1024:N0} KB | " +
                             $"factor {(double)treeBytes / setBytes:F2}x");

            tree.Clear();
            ResetArenas();
        }

        // =====================================================================

        private static void Compare(ref RedBlackSet<long, LongComparer> set, SortedSet<long> reference, int step)
        {
            string where = step < 0 ? "at the end" : $"at step {step}";

            set.Validate();
            Assert.True(set.Count == reference.Count, $"count {set.Count} != {reference.Count} {where}");
            Assert.True(Arena<long>.Allocated == reference.Count,
                $"allocated {Arena<long>.Allocated} != {reference.Count} {where}");

            var keys = new List<long>();
            foreach (var node in set) keys.Add(set.ValueOf(node));

            int index = 0;
            foreach (long key in reference)
            {
                Assert.True(keys[index] == key, $"key {index}: {keys[index]} != {key} {where}");
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
