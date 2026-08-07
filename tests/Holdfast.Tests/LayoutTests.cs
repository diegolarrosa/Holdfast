using Xunit;

namespace Holdfast.Tests
{
    /// <summary>
    /// Address packing is tested as arithmetic, not by exercising the arena.
    /// A truncated mask only shows up past a block index that would take
    /// tens of gigabytes to reach in practice — but takes microseconds here.
    /// </summary>
    public class LayoutTests
    {
        [Fact]
        public void AddressRoundTripsAcrossTheWholeRange()
        {
            int[] blocks = { 0, 1, 2, 1023, 2046, 2047, 2048, 2049, 4095,
                             65535, 1_000_000, Layout.MaxBlocks - 1 };
            int[] offsets = { 0, 1, 2, 12345, Layout.BlockSize / 2,
                              Layout.BlockSize - 2, Layout.BlockSize - 1 };

            foreach (int block in blocks)
            {
                foreach (int offset in offsets)
                {
                    long address = Layout.Address(block, offset);
                    Assert.True(Layout.Block(address) == block,
                        $"block: expected {block}, got {Layout.Block(address)} (address={address})");
                    Assert.True(Layout.Offset(address) == offset,
                        $"offset: expected {offset}, got {Layout.Offset(address)} (address={address})");
                }
            }
        }

        [Fact]
        public void HighestAddressFillsExactlyTheAddressBits()
        {
            long highest = Layout.Address(Layout.MaxBlocks - 1, Layout.BlockSize - 1);

            Assert.True(highest == Layout.AddressMask,
                $"the highest address should fill exactly {Layout.AddressBits} bits: " +
                $"{highest:X} vs {Layout.AddressMask:X}");
            Assert.True(highest <= Layout.TreeNull,
                "the highest address must not exceed the tree's null value");
        }
    }
}
