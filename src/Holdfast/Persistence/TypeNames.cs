using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Holdfast
{
    /// <summary>
    /// Writes and resolves the payload type names recorded in a snapshot.
    /// </summary>
    /// <remarks>
    /// Names are stored assembly-qualified so a payload type from the user's
    /// own assembly can be found again. They are compared, and resolved, with
    /// the version, culture and public key token stripped: a snapshot taken
    /// with version 1.2 of an assembly still loads against 1.3, which is the
    /// point of a checkpoint that outlives the process that wrote it.
    /// <para>
    /// Resolution only ever produces a <see cref="Type"/> that is then used as
    /// a generic argument. Nothing from the file is constructed or invoked.
    /// </para>
    /// </remarks>
    internal static class TypeNames
    {
        internal static string Of(Type type) =>
            type.AssemblyQualifiedName ?? type.FullName ?? type.Name;

        /// <summary>
        /// Drops the version, culture and key token from every assembly
        /// reference in a type name, including the ones nested inside generic
        /// arguments. What is left is the part that identifies the type.
        /// </summary>
        internal static string Normalize(string name)
        {
            var result = new StringBuilder(name.Length);
            int i = 0;

            while (i < name.Length)
            {
                char c = name[i];

                if (c == ',' && IsNoiseAt(name, i + 1))
                {
                    // Skip ", Version=1.2.3.4" and friends, up to the next
                    // separator that is not part of the value.
                    i++;
                    while (i < name.Length && name[i] != ',' && name[i] != ']') i++;
                    continue;
                }

                result.Append(c);
                i++;
            }

            return result.ToString();
        }

        private static bool IsNoiseAt(string name, int start)
        {
            while (start < name.Length && name[start] == ' ') start++;

            return name.AsSpan(start).StartsWith("Version=", StringComparison.Ordinal)
                || name.AsSpan(start).StartsWith("Culture=", StringComparison.Ordinal)
                || name.AsSpan(start).StartsWith("PublicKeyToken=", StringComparison.Ordinal);
        }

        /// <summary>The type, or null when it cannot be found.</summary>
        internal static Type? Resolve(string name)
        {
            try
            {
                return Type.GetType(name, ResolveAssembly, ResolveType, throwOnError: false);
            }
            catch (Exception e) when (e is FileLoadException or BadImageFormatException or TypeLoadException)
            {
                return null;
            }
        }

        private static Assembly? ResolveAssembly(AssemblyName request)
        {
            string? simple = request.Name;
            if (simple is null) return null;

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, simple, StringComparison.OrdinalIgnoreCase))
                    return loaded;
            }

            try
            {
                // By simple name only: the version in the file is the version
                // that wrote it, not a requirement on the one that reads it.
                return Assembly.Load(new AssemblyName(simple));
            }
            catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                return null;
            }
        }

        private static Type? ResolveType(Assembly? assembly, string typeName, bool ignoreCase) =>
            assembly is null
                ? Type.GetType(typeName, throwOnError: false, ignoreCase: ignoreCase)
                : assembly.GetType(typeName, throwOnError: false, ignoreCase: ignoreCase);
    }
}
