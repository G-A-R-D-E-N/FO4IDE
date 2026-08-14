// Several tests exercise MutagenLoader's process-global mod registries (LooseMods / EditableMods).
// Running test classes in parallel lets one class's created plugins leak into another's assertions
// (e.g. a "No plugins loaded" check). Serialize the assembly so global-state tests are deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
