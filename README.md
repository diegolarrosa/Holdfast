# Holdfast

Arena allocator with typed handles and intrusive collections for .NET.

> A *holdfast* is the clamp that pins work to the bench, and the root-like
> organ an alga anchors itself with. Both hold on and don't let go — which is
> the one guarantee this library is built around: **a handle stays valid until
> you free the node**. Nothing ever moves.

Holdfast stores every node in fixed-size blocks of plain structs. There are no
per-node objects, so the garbage collector never walks the data — it only sees
a handful of large, reference-free arrays. Nodes are addressed by 42-bit
handles that stay valid for the entire lifetime of the node, and handles carry
their mode in the type, so a list handle cannot be passed to a tree operation.

## What it's for

Holdfast is built for one shape of problem: **long-running, single-threaded
work with a very large working set**. Four properties follow from that, and
they matter more than throughput on small inputs:

**It scales past where the BCL stops.** Every collection in the base class
library is `int`-indexed — `SortedSet<T>.Count` is an `int`, and a .NET array
cannot hold more than about 2.1 billion elements no matter how it is
configured. That is a hard ceiling in the type signature, not a memory limit.
Holdfast counts in `long` and addresses in 42 bits: 4,398,046,511,103 nodes,
roughly 105 TB for a 24-byte node. The same code, unchanged, runs from
thousands to trillions.

**Half the memory per key.** 24 bytes against 48 for `SortedSet<long>`. Exact,
in every run, in every configuration. A BCL node is a class: object header, two
references, the value, the color. An arena node is flat bytes.

**Predictable timing.** Holdfast's time depends on DRAM latency, which does not
change. A GC-backed collection's time depends on heap state, fragmentation and
memory pressure, which do. Across repeated identical runs at ten million keys,
`SortedSet` insertion varied by 23% while Holdfast varied by under 2%. For a
computation that runs for days, being able to predict when it finishes is worth
as much as finishing sooner.

**Serializable at any point.** Because everything is an offset rather than a
pointer, the arena is position-independent: write the blocks to disk, load them
in another process or on another machine, and every handle still resolves. No
pointer fixup, no traversal, no serialization code. Checkpointing a multi-day
computation costs a file write. See [Checkpoints](#checkpoints).

Where that tends to matter: search and solver state spaces, graph and index
structures that outlive a request, simulations, in-memory analytical stores, and
any computation long enough that it has to be able to stop and resume.

If your data fits comfortably in memory and lives for the length of a request,
the BCL collections are the right answer and are extremely well tuned. Holdfast
starts paying off where they start hurting.

## At a glance

```csharp
using Holdfast;

var tree = RedBlackSet<long, LongComparer>.Create();

tree.Insert(42);
tree.Insert(17);

if (tree.Contains(42))
    Console.WriteLine("found");

foreach (var node in tree.InOrder)      // stack-based traversal
    Console.WriteLine(tree.ValueOf(node));

tree.Clear();                           // iterative, no recursion

public readonly struct LongComparer : IArenaComparer<long>
{
    public int Compare(in long a, in long b) => a.CompareTo(b);
    public int CompareDescendant(in long a, in long b) => a.CompareTo(b);
}
```

The comparer is a generic type parameter constrained to `struct`, so the JIT
inlines the comparison instead of dispatching through an interface.

## What's in the box

| Type | What it is |
|---|---|
| `Arena<T>` | Block-allocated storage with a free list. Blocks are 1,048,576 nodes and are never copied or moved. |
| `Handle<T, TMode>` | A 42-bit address in a single-field readonly struct. Zero runtime cost; the mode lives in the type. |
| `ArenaList<T>` | Doubly linked list. Nest it — `ArenaList<ArenaList<T>>` is a list of lists. |
| `RedBlackSet<T, TComp>` | Red-black tree, one value per key. 24 bytes/key for `long`. |
| `RedBlackTree<T, TComp>` | Red-black tree where each key holds an `ArenaList<T>` of equal values. |
| `Snapshot` | Writes a set of arenas and named roots to a stream, and reads them back. |
| `ArenaState<T>` | An arena's state as an object, so it can be swapped out and put back. |

## Checkpoints

```csharp
// Stop.
var snapshot = Snapshot.Create();
snapshot.AddSet("index", index);

using (var file = File.Create("checkpoint.hf"))
    snapshot.SaveTo(file);
```

```csharp
// Resume, in a process that has built nothing.
Snapshot snapshot;
using (var file = File.OpenRead("checkpoint.hf"))
    snapshot = Snapshot.LoadFrom(file);

snapshot.Activate();
var index = snapshot.GetSet<long, LongComparer>("index");
```

The file is the blocks, verbatim, plus a header. Loading is a read into fresh
blocks — there is no graph walk and nothing is rewritten, so a `Handle` you
wrote down before the save is the same value after the load and still points at
the same node.

`AddSet`, `AddList`, `AddTree` and `AddHandle` each pull in the arenas they
need. That matters most for `RedBlackTree<T, TComp>`, which spans two: its nodes
live in `Arena<ArenaList<T>>` and its values in `Arena<T>`. Saving one without
the other produces a tree whose value lists point at nothing, so `AddTree` adds
both.

`Activate` replaces the state of every static arena the file covers, which
invalidates every handle the process was already holding into those arenas. It
leaves arenas the file does not mention alone.

**Where it loses.**

- **The file is the reserved capacity, not the live keys.** A block is 1,048,576
  nodes, so an arena of `long` holding one key writes 24 MB. At 50 million keys
  it is about 1.2 GB, which is the working set anyway. At a thousand keys it is
  still 24 MB. There is no compaction, because compaction moves nodes and moving
  nodes is what handle stability rules out.
- **Every checkpoint is the whole arena.** No incremental or differential
  writes, and no compression. A checkpoint costs a sequential write of the
  reserved blocks.
- **`T` must hold no references.** A snapshot is a copy of memory: a reference
  would be written as an address of the writing process. An arena of a payload
  with a reference field is refused at save time, not at load time.
- **Same byte order, same layout.** The header records the writer's byte order
  and the reader refuses a mismatch rather than swapping bytes whose meaning it
  does not know. The payload type is fingerprinted by its name and the size of
  its node, which catches a changed type but not two fields of the same size
  swapped around. If you reorder fields, treat old checkpoints as unreadable.
- **The comparer is not in the file.** It is a compile-time choice and may carry
  state, so you supply it again on load. It has to order keys the same way the
  saved one did; nothing checks that.
- **This is a process image, not a database.** `SaveTo` flushes the stream; it
  does not `fsync`, and there is no journal and no transaction. A crash partway
  through a write leaves a truncated file — the loader rejects it, but the
  checkpoint is gone. If a checkpoint has to survive a crash, write it to a
  temporary path, flush it to disk yourself, and rename over the previous one.
- **Free nodes are scrubbed by default.** Blocks are allocated uninitialized, so
  the payload of a never-used slot holds whatever those pages held before.
  Zeroing it keeps that out of the file and makes two snapshots of the same
  arena byte-identical. It costs one walk of the free list;
  `SnapshotOptions.ScrubFreeNodes` turns it off.

## Benchmarks

Speed is not the reason to use Holdfast, but it does not cost anything either.

.NET 8.0.25, x64, 4 cores, 50,000,000 random keys. Lower is better;
`SortedSet<long>` is the 1.00x baseline.

| Operation | Workstation GC | Server GC |
|---|---|---|
| Insert | **0.63x** | 0.97x |
| Lookup | 0.93x | 0.93x |
| Insert + remove | **0.65x** | 0.83x |
| In-order traversal | 0.98x | 0.98x |
| Memory | **1144 MB vs 2289 MB** | **1144 MB vs 2289 MB** |

The advantage grows with the working set, because that is what it is for. At ten
million keys under workstation GC the same numbers are 0.86x insert, 0.91x
lookup, 0.80x insert+remove. At one million the difference is small.

Three caveats, because a benchmark table without them is marketing:

**Server GC changes who wins at insertion.** It makes allocating an object per
node much cheaper, which is exactly what a GC-backed collection spends its
insertion time on, and it does nothing for Holdfast. Below roughly fifty million
keys `SortedSet` inserts faster under server GC — 1.1x to 1.2x in our runs at
one, five and ten million. Every other operation stays ahead in both modes, and
memory is unaffected by the setting.

**Ratios vary between runs.** The same insertion benchmark at ten million keys
has produced anything from 0.65x to 0.86x, driven almost entirely by how the
baseline behaves on the day. Holdfast's own times varied by under 2% across the
same runs — see "predictable timing" above.

**The insert row includes growing the arena.** Every repetition allocates the
blocks from scratch. In a long-running process the arena is built once and
reused, so real insertion cost is lower than this row suggests. The comparison
is still fair — the baseline builds from empty too — but it is not the number
that matters for a process that runs for days.

Run them yourself, in both modes:

```
$env:DOTNET_gcServer=0
dotnet run -c Release --project bench/Holdfast.Benchmarks -- 50000000

$env:DOTNET_gcServer=1
dotnet run -c Release --project bench/Holdfast.Benchmarks -- 50000000
```

## Design notes

**Handles are stable.** This is the property the library is named after, and
everything else is downstream of it. It is why Holdfast is a red-black tree and
not a B-tree: a B-tree searches faster but relocates keys on split and merge,
which breaks any algorithm that holds references to nodes across operations.

**Memory is reference-free.** When `T` contains no references, blocks are
allocated on the Pinned Object Heap without zero-initialization. The GC never
scans them.

**Growth does not invalidate.** Adding a block appends to an array of block
references; the blocks themselves are never copied. Handles and `ref`s into the
arena survive it.

**Debug builds check more.** Mode confusion and use-after-free are caught by
assertions that compile away entirely in Release.

## Limits

- 42-bit addresses: 4,398,046,511,103 nodes, about 105 TB for a 24-byte node.
- Not thread-safe. No locking anywhere.
- `Arena<T>` is static per closed type `T`; two independent arenas of the same
  `T` cannot be live at once. `Arena<T>.Swap` exchanges one state for another,
  so several can exist and take turns, but only one is addressable at a time.
- `default(ArenaList<T>)` is not a valid empty list — use `ArenaList<T>.Empty`.
- The workload is bound by memory latency, not by instruction count. There is
  little headroom left in micro-optimization.

## Testing

68 tests. The trees are fuzzed against `SortedDictionary` and `SortedSet` over
tens of thousands of random operations, with the red-black invariants, the
parent links, the in-order ordering and the arena's allocation counters checked
throughout. Arena leaks and double frees are caught by asserting the exact
allocated-node count after each scenario.

Checkpoints are tested by saving, wiping the arena, loading, and then checking
the red-black invariants and the arena counters on what came back — including an
arena large enough to cross a block boundary, a free list left fragmented on
purpose, and a tree whose values live in a second arena. A truncated file, a
foreign stream, an unknown format version, a payload holding a reference and a
root pointing outside its arena are each expected to be refused.

```
dotnet test
```

## License

MIT. See [LICENSE](LICENSE).

The red-black case analysis derives from the MIT-licensed "Red-black tree (C)"
article on LiteratePrograms.org — see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
