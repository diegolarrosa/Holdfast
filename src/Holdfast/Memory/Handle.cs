using System;
using System.Runtime.CompilerServices;

namespace Holdfast
{
    /// <summary>
    /// A typed address into <see cref="Arena{T}"/>.
    /// </summary>
    /// <remarks>
    /// A readonly struct with a single <see cref="long"/> field: the JIT
    /// unwraps it completely, so the generated code is identical to passing the
    /// raw long. Everything it buys is at compile time.
    /// <para>
    /// There is deliberately no implicit conversion to <see cref="long"/>.
    /// Adding one would silently discard every guarantee the type provides.
    /// </para>
    /// <para>
    /// A handle stays valid until its node is freed. Blocks are never copied or
    /// moved, so growing the arena does not invalidate anything.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The payload type stored in the arena.</typeparam>
    /// <typeparam name="TMode">The node's mode: <see cref="RawMode"/>, <see cref="ListMode"/> or <see cref="TreeMode"/>.</typeparam>
    public readonly struct Handle<T, TMode> : IEquatable<Handle<T, TMode>>
        where T : struct
        where TMode : struct, IHandleMode
    {
        private readonly long _value;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Handle(long value) => _value = value;

        internal long Raw
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _value;
        }

        /// <summary>
        /// The null handle for this mode. Note that the underlying value
        /// differs per mode — -1 for lists, 2^42-1 for trees — which is why
        /// handles of different modes must never be compared.
        /// </summary>
        public static Handle<T, TMode> Null
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new Handle<T, TMode>(TMode.Null);
        }

        /// <summary>True when this handle refers to no node.</summary>
        public bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _value == TMode.Null;
        }

        /// <summary>Compares two handles of the same payload type and mode.</summary>
        /// <param name="other">The handle to compare with.</param>
        /// <returns>True when both refer to the same node.</returns>
        public bool Equals(Handle<T, TMode> other) => _value == other._value;

        /// <summary>Compares this handle with an arbitrary object.</summary>
        /// <param name="obj">The object to compare with.</param>
        /// <returns>True when it is a handle of the same type referring to the same node.</returns>
        public override bool Equals(object? obj) => obj is Handle<T, TMode> h && _value == h._value;

        /// <summary>Hash code derived from the underlying address.</summary>
        /// <returns>A hash code suitable for dictionaries and sets.</returns>
        public override int GetHashCode() => _value.GetHashCode();

        /// <summary>Tests whether two handles refer to the same node.</summary>
        /// <param name="a">First handle.</param>
        /// <param name="b">Second handle.</param>
        /// <returns>True when both refer to the same node.</returns>
        public static bool operator ==(Handle<T, TMode> a, Handle<T, TMode> b) => a._value == b._value;

        /// <summary>Tests whether two handles refer to different nodes.</summary>
        /// <param name="a">First handle.</param>
        /// <param name="b">Second handle.</param>
        /// <returns>True when they refer to different nodes.</returns>
        public static bool operator !=(Handle<T, TMode> a, Handle<T, TMode> b) => a._value != b._value;

        /// <summary>
        /// Renders as <c>T[3:874512]</c> — block and offset. Far easier to read
        /// in a debugger than the packed integer.
        /// </summary>
        /// <returns>A readable representation of the address.</returns>
        public override string ToString() =>
            _value == TMode.Null
                ? TMode.Prefix + "(null)"
                : $"{TMode.Prefix}[{Layout.Block(_value)}:{Layout.Offset(_value)}]";
    }
}
