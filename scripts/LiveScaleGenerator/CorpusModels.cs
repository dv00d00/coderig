using System.Text.Json.Serialization;

namespace LiveScaleGenerator;

internal sealed record GeneratorManifest(int SchemaVersion, Dictionary<string, PresetDefinition> Presets);

internal sealed record PresetDefinition(
    int ProjectCount,
    int FilesPerProject,
    int OversizedProjectExtraFiles,
    int MethodsPerFile,
    int CallsPerMethod,
    int VocabularyCardinality,
    TopologyDefinition Topology,
    int OptionalGeneratedFilesPerProject
);

internal sealed record TopologyDefinition(
    int CoreHubIndex,
    int RuntimeIndex,
    int[] OversizedProjectIndices,
    int DiamondStride,
    int OversizedCoverageNumerator,
    int OversizedCoverageDenominator
);

internal sealed record CorpusManifest(
    int SchemaVersion,
    string Preset,
    ulong Seed,
    CorpusDimensions Dimensions,
    IReadOnlyList<ProjectManifest> Projects,
    string Solution,
    string EditTrace,
    string EditTraceSha256,
    string HashAlgorithm,
    bool IncludesGeneratedCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorpusSha256 = null
);

internal sealed record CorpusDimensions(
    int ProjectCount,
    int FilesPerProject,
    int OversizedProjectExtraFiles,
    int MethodsPerFile,
    int CallsPerMethod,
    int VocabularyCardinality,
    TopologyDefinition Topology,
    int OptionalGeneratedFilesPerProject,
    int TotalCSharpFiles,
    long EstimatedReferenceCount,
    TopologySummary TopologySummary
);

internal sealed record TopologySummary(
    int MaximumTransitiveDependents,
    int MedianTransitiveDependents,
    int ProjectsWithAtLeastHalfOfProjectsDependingOnThem,
    int MedianAffectedFileCount,
    int FeaturesPullingOversizedProjects
);

internal sealed record ProjectManifest(
    string Name,
    string Role,
    string ProjectPath,
    int CSharpFileCount,
    IReadOnlyList<string> ProjectReferences
);

internal sealed record EditTrace(
    int SchemaVersion,
    ulong Seed,
    string ReplayPolicy,
    IReadOnlyList<EditStep> Edits,
    IReadOnlyList<QuerySeed> QuerySeeds
);

internal sealed record EditStep(string Id, string Kind, string Scenario, string TargetClass, IReadOnlyList<FileMutation> Mutations);

internal sealed record FileMutation(
    string Project,
    string File,
    string Operation,
    string Marker,
    string Replacement,
    string ReverseMarker,
    string ReverseReplacement
);

internal sealed record QuerySeed(string Id, string EditId, string DirtyFile, string QueryFile, string Relation, string Pattern);

internal readonly record struct ProjectPlan(int Index, string Name, string Role, int FileCount, int[] Dependencies);

internal readonly record struct GenerationOptions(string Preset, string OutputDirectory, ulong Seed, bool IncludeGenerated);

internal readonly record struct GenerationSummary(
    string Preset,
    int ProjectCount,
    int CSharpFileCount,
    string CorpusSha256,
    string EditTraceSha256
);
