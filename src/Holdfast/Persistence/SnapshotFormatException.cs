using System;

namespace Holdfast
{
    /// <summary>
    /// Thrown when a stream is not a Holdfast snapshot, or is one that this
    /// build cannot load.
    /// </summary>
    /// <remarks>
    /// The message always names the specific mismatch — magic, format version,
    /// endianness, layout constants, node size or payload type — rather than
    /// reporting a generic parse failure. A snapshot is raw memory: loading one
    /// that does not match would not fail loudly, it would succeed and hand back
    /// garbage that looks like a valid tree.
    /// </remarks>
    public sealed class SnapshotFormatException : Exception
    {
        /// <summary>Creates the exception with a message.</summary>
        /// <param name="message">What did not match.</param>
        public SnapshotFormatException(string message) : base(message) { }

        /// <summary>Creates the exception with a message and an inner cause.</summary>
        /// <param name="message">What did not match.</param>
        /// <param name="inner">The underlying failure.</param>
        public SnapshotFormatException(string message, Exception inner) : base(message, inner) { }
    }
}
