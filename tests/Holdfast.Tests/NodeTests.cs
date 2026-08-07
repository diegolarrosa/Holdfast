using Xunit;

namespace Holdfast.Tests
{
    /// <summary>
    /// Parent is split across both link words, with the boundary at bit 20.
    /// It is the most fragile part of the layout, so it gets tested hard.
    /// </summary>
    public class NodeTests
    {
        [Fact]
        public void TheFourTreeFieldsAreIndependent()
        {
            long[] values = { 0, 1, 2, 0xFFFFF, 0x100000, 0x3FFFFF,
                              1L << 41, Layout.AddressMask, Layout.TreeNull };

            foreach (long left in values)
            {
                foreach (long right in values)
                {
                    foreach (long parent in values)
                    {
                        foreach (bool color in new[] { false, true })
                        {
                            var node = new Node<long>();
                            node.Left = left;
                            node.Right = right;
                            node.Parent = parent;
                            node.Color = color;

                            Assert.True(node.Left == left,
                                $"Left broken: {node.Left:X} != {left:X} (right={right:X} parent={parent:X} color={color})");
                            Assert.True(node.Right == right,
                                $"Right broken: {node.Right:X} != {right:X} (left={left:X} parent={parent:X} color={color})");
                            Assert.True(node.Parent == parent,
                                $"Parent broken: {node.Parent:X} != {parent:X} (left={left:X} right={right:X} color={color})");
                            Assert.True(node.Color == color,
                                $"Color broken (left={left:X} right={right:X} parent={parent:X})");
                        }
                    }
                }
            }
        }

        [Fact]
        public void ClearingOneFieldLeavesTheOthersAlone()
        {
            var node = new Node<long>();

            node.Left = Layout.AddressMask;
            node.Right = Layout.AddressMask;
            node.Parent = Layout.AddressMask;
            node.Color = true;
            Assert.True(node.Left == Layout.AddressMask && node.Right == Layout.AddressMask
                     && node.Parent == Layout.AddressMask && node.Color, "all ones");

            node.Left = 0;
            Assert.True(node.Right == Layout.AddressMask && node.Parent == Layout.AddressMask && node.Color,
                "clearing Left touched another field");

            node.Parent = 0;
            Assert.True(node.Right == Layout.AddressMask && node.Left == 0 && node.Color,
                "clearing Parent touched another field");

            node.Color = false;
            Assert.True(node.Right == Layout.AddressMask && node.Parent == 0,
                "clearing Color touched another field");

            node.Value = long.MinValue;
            node.Left = 12345; node.Right = 67890; node.Parent = 111213; node.Color = true;
            Assert.True(node.Value == long.MinValue, "the payload was corrupted");
        }

        [Fact]
        public void ParentSurvivesEveryBitPosition()
        {
            for (int bit = 0; bit < Layout.AddressBits; bit++)
            {
                long value = 1L << bit;
                var node = new Node<long>();
                node.Left = Layout.AddressMask;
                node.Right = Layout.AddressMask;
                node.Color = true;
                node.Parent = value;

                Assert.True(node.Parent == value, $"Parent bit {bit}: read {node.Parent:X}, wrote {value:X}");
                Assert.True(node.Left == Layout.AddressMask, $"Parent bit {bit} clobbered Left");
                Assert.True(node.Right == Layout.AddressMask, $"Parent bit {bit} clobbered Right");
                Assert.True(node.Color, $"Parent bit {bit} clobbered Color");
            }
        }
    }
}
