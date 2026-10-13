The private inspection helpers support merge content certificates. Their exact
bytes, native ABI, source hashes and finite probe are recorded in
`inspection-provenance.json` and `development/merge-generalization-helper-native.json`.
They are independent private copies; the original helper files remain unchanged.

* OwnMergePanelInventory uses actual NI class Pnl with AllObjs[] 6348803,
  Decos[] 6348802 and Panes[] 6348806. Its typed arrays retain NI refnums and
  native error chain. Product merge accepts one pane, no FP decorations, and
  requires AllObjs to equal the complete top-level Control inventory.
* OwnMergeObjectOwner uses actual NI class GObj and Owner 6327806. The source's
  numeric reference cast is retained and directly wired to the property. A
  ControlTerminal's Owner is its Diagram, not its FP control; Diagram bounds
  can expand after merge, while its UID/class and wire terminal identities stay.
* OwnMergeWireTerms is a private copy of our old independent Wire.Terms[]
  reader. The terminal_ids type input is the U32 TypeCast specifier with fixed
  native default 0. It is not a required wire reference. Each returned endpoint
  is then checked for Path/UID, owner, native type, direction and diagram.

The authoring script retains partial copies and records native errors. Removing
old output indicators can leave NI dangling wire fragments; only those broken
fragments in newly owned helper copies are deleted. No Cleanup is invoked.
All three final helpers are compiled idle and have an empty callee list. The
aggregate merge helper separately uses only the NI LVDeltaXY32Cluster.ctl.

Native GObject.Position uses global diagram coordinates for nested For bodies.
The observed nested objects move by the same dx/dy as their containing loop;
parent/frame containment and relative geometry are also preserved by mapping.
Targets and original helper source files are never Run during authoring probes.
