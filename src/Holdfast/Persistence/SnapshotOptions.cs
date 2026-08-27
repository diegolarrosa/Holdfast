namespace Holdfast
{
    /// <summary>
    /// What to do while writing a snapshot.
    /// </summary>
    public sealed class SnapshotOptions
    {
        /// <summary>The defaults. Do not mutate; make your own instance instead.</summary>
        public static SnapshotOptions Default { get; } = new SnapshotOptions();

        /// <summary>
        /// Zero the payload bytes of nodes on the free list before writing.
        /// On by default.
        /// </summary>
        /// <remarks>
        /// Blocks are allocated uninitialized, so the payload of a slot that
        /// was never used holds whatever the pages held before. Scrubbing keeps
        /// that out of the file and makes two snapshots of the same arena
        /// byte-identical. It costs one walk of the free list, which is the
        /// part of the arena that is not being written for its contents anyway.
        /// <para>
        /// Turn it off only when the free list is enormous and the file is
        /// going somewhere you already trust.
        /// </para>
        /// </remarks>
        public bool ScrubFreeNodes { get; set; } = true;
    }
}
