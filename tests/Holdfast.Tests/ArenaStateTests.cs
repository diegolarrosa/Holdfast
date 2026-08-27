using System;
using Xunit;

namespace Holdfast.Tests
{
    /// <summary>
    /// Arena&lt;T&gt; sigue siendo estatica: el estado vive en campos estaticos
    /// para que el camino caliente no pague una indireccion. Lo que agrega
    /// ArenaState es poder sacar ese estado, guardarlo, y volver a ponerlo.
    /// </summary>
    public class ArenaStateTests
    {
        [Fact]
        public void EmptyStateLooksLikeAFreshArena()
        {
            Arena<long>.Reset();
            Arena<long>.Allocate();

            Arena<long>.Restore(ArenaState<long>.Empty);

            Assert.Equal(0L, Arena<long>.Allocated);
            Assert.Equal(0L, Arena<long>.Available);
            Assert.Equal(0L, Arena<long>.TotalAllocations);
            Assert.Equal(0L, Arena<long>.BlockCount);

            Arena<long>.ValidateIntegrity();
            Arena<long>.Reset();
        }

        [Fact]
        public void CaptureReportsTheCurrentCounters()
        {
            Arena<long>.Reset();
            for (int i = 0; i < 10; i++) Arena<long>.Allocate();

            ArenaState<long> state = Arena<long>.Capture();

            Assert.Equal(10L, state.Allocated);
            Assert.Equal(10L, state.TotalAllocations);
            Assert.Equal(Arena<long>.Available, state.Available);
            Assert.Equal(Arena<long>.BlockCount, state.BlockCount);

            Arena<long>.Reset();
        }

        [Fact]
        public void SwapKeepsTwoArenasApart()
        {
            Arena<long>.Reset();

            var first = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 1000; i++) first.Insert(i);

            // Se lleva la arena entera y deja una vacia en su lugar.
            ArenaState<long> firstState = Arena<long>.Swap(ArenaState<long>.Empty);
            Assert.Equal(0L, Arena<long>.Allocated);

            var second = RedBlackSet<long, LongComparer>.Create();
            for (int i = 0; i < 500; i++) second.Insert(i + 10_000);
            second.Validate();
            Assert.Equal(500L, second.Count);

            ArenaState<long> secondState = Arena<long>.Swap(firstState);

            // El primer set nunca se toco: sus handles siguen apuntando a los
            // mismos nodos, que volvieron con el estado.
            Assert.Equal(1000L, Arena<long>.Allocated);
            first.Validate();
            Assert.Equal(1000L, first.Count);
            Assert.True(first.Contains(999), "el primer set perdio una clave al volver");

            Arena<long>.Restore(secondState);
            second.Validate();
            Assert.Equal(500L, second.Count);
            Assert.True(second.Contains(10_499), "el segundo set perdio una clave");

            Arena<long>.Reset();
        }

        [Fact]
        public void RestoreRejectsNull()
        {
            Assert.Throws<ArgumentNullException>("state", () => Arena<long>.Restore(null!));
        }
    }
}
