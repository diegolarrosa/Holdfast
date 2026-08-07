namespace Holdfast
{
    /// <summary>
    /// A node's mode. The same storage is interpreted two incompatible ways:
    /// in list mode the two link words are raw forward and backward links; in
    /// tree mode those same 128 bits hold Left, Right, Parent and Color packed
    /// together. The mode travels in the handle's type, never at run time, so
    /// passing a list handle to a tree operation does not compile.
    /// </summary>
    public interface IHandleMode
    {
        /// <summary>The value meaning "no address" in this mode.</summary>
        static abstract long Null { get; }

        /// <summary>Mode tag, used only by debug-build assertions.</summary>
        static abstract byte Code { get; }

        /// <summary>Prefix for ToString, so addresses are readable in a debugger.</summary>
        static abstract string Prefix { get; }
    }

    /// <summary>A freshly allocated node that has not been given a mode yet.</summary>
    public readonly struct RawMode : IHandleMode
    {
        /// <summary>Null for an unassigned node, shared with list mode.</summary>
        public static long Null => Layout.ListNull;

        /// <summary>Mode tag for debug-build assertions.</summary>
        public static byte Code => 1;

        /// <summary>Debugger prefix for addresses in this mode.</summary>
        public static string Prefix => "raw";
    }

    /// <summary>A node linked into an <see cref="ArenaList{T}"/>.</summary>
    public readonly struct ListMode : IHandleMode
    {
        /// <summary>Null in list mode: -1.</summary>
        public static long Null => Layout.ListNull;

        /// <summary>Mode tag for debug-build assertions.</summary>
        public static byte Code => 2;

        /// <summary>Debugger prefix for addresses in this mode.</summary>
        public static string Prefix => "L";
    }

    /// <summary>A node linked into one of the red-black trees.</summary>
    public readonly struct TreeMode : IHandleMode
    {
        /// <summary>Null in tree mode: 2^42-1, the all-ones address.</summary>
        public static long Null => Layout.TreeNull;

        /// <summary>Mode tag for debug-build assertions.</summary>
        public static byte Code => 3;

        /// <summary>Debugger prefix for addresses in this mode.</summary>
        public static string Prefix => "T";
    }
}
