`merge_vi` uses the accepted self-authored aggregate-selection helper copied from
`app/development/merge-helper-handoff.json`. `provenance.json` preserves the
original helper hash, authoring files, NI-only static callee, ABI, measured
development fixture, and remaining unknowns. The copied helper has the same
SHA-256 and contains a constant true duplication input. It does not expose a
destructive move mode, clipboard operations, or Cleanup.

The public transaction accepts two distinct app-owned VI artifacts with matching
successful generation reports. Inputs are fresh graph builds or fresh recipes
from the explicitly allowed operation set. Imported/copy ancestry, containers
beyond the bounded Array/Cluster cases below, connector authoring, FP decorations
and multiple panes are outside this contract and rejected.
`complete_replacement=false` remains explicit.

The source is copied to a unique scratch VI before native selection. The target
is also a unique copy. Both originals, both generation reports and both authoring
plans are pinned against disk writes and checked again. The native worker checks
loaded paths, exact unique/disjoint labels, scalar control classes/directions/type
descriptors, target original UIDs, one-to-one source FP membership, root/nested
diagram object classes and wire counts, broken wires and compiled idle state.
It saves only the new target and recursively checks its actual saved `_CalleesPaths`
against registered NI vi.lib or app-owned VI/type hash/report snapshots.
Helper callees are separate metadata and never substitute for saved target callees.

The later bounded container extension accepts typed rank1/2 DBL/Boolean/String
Arrays and1..32-field mixed scalar/nested Clusters. Array values record CLR type,
rank, every axis length/lower bound and ordered typed elements; Cluster values
record ordered recursive typed members. No multidimensional SAFEARRAY is passed
directly to JSON equality. Cluster-element Arrays and Arrays inside Cluster
fields are still rejected. `tests/test_merge_containers.py` prepares a single
nonempty2x3 DBL matrix and mixed/nested Cluster echo fixture, with both container
classes also retained in the original target. Its native/fresh acceptance is
separate from the earlier scalar fixture and must be explicitly recorded.

The first bounded container product fixture passed18 public native checks and12
fresh-client checks. Its measured scope is precisely a nonempty2x3 DBL Array,
a mixed Double/String/Boolean Cluster with a nested Double/String Cluster, their
two typed echo indicators, and both classes already present in the target.
Evidence is indexed by `development/merge-container-handoff.json`. These results
do not certify every Array element type, arbitrary nested control container,
decoration, pane layout, or connector migration.

Neither input nor a generic merged output is run. `tests/test_merge_product.py`
defaults to an offline plan/fixture export. With exclusive NI ownership,
`--native` builds via public MCP and runs only its known finite For(N=5)/Add
merged fixture. `--reopen-report` uses a fresh process to recheck that fixture.
`tests/test-merge-offline.ps1` has a fake text-only backend and records no NI claim.
