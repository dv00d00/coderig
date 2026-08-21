using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Rig.Tests")]
[assembly: InternalsVisibleTo("Rig.Benchmarks")]
// `rig watch` (Rig.Cli) hosts the resident index: it drives the internal ResidentIndex /
// AnalyzeRetainingWorkspaceAsync seams, which stay internal until the resident surface stabilises.
[assembly: InternalsVisibleTo("Rig.Cli")]
