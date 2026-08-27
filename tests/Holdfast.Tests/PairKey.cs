namespace Holdfast.Tests
{
    /// <summary>
    /// Una carga util de dos campos, para los tests que necesitan un payload
    /// propio y no un primitivo.
    /// </summary>
    /// <remarks>
    /// El arbol ordena solo por Key, asi que varios PairKey con la misma Key
    /// caen en el mismo nodo y su lista de valores. Eso es lo que hace que un
    /// RedBlackTree ocupe dos arenas, que es el caso que hay que probar al
    /// serializar.
    /// </remarks>
    public struct PairKey
    {
        /// <summary>La parte que ordena.</summary>
        public long Key;

        /// <summary>Distingue duplicados dentro de un mismo nodo.</summary>
        public int Tag;
    }
}
