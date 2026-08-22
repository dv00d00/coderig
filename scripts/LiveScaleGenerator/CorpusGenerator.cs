using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiveScaleGenerator;

internal static class CorpusGenerator
{
    private const string MarkerFileName = ".live-scale-corpus";
    private const string MarkerContent = "LiveScaleGenerator schema 1\n";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static GenerationSummary Generate(GenerationOptions options)
    {
        var definition = LoadPreset(options.Preset);
        Validate(definition);
        ResetOutput(options.OutputDirectory);

        var plans = CreateProjectPlans(definition);
        var rng = new SplitMix64(options.Seed);

        WriteRelative(options.OutputDirectory, MarkerFileName, MarkerContent);
        WriteProjects(options.OutputDirectory, plans, definition, options.IncludeGenerated, ref rng);
        WriteSolution(options.OutputDirectory, plans);

        var trace = CreateEditTrace(options.Seed, plans, ref rng);
        var traceText = Serialize(trace);
        WriteRelative(options.OutputDirectory, "edit-trace.json", traceText);
        var traceHash = Sha256Hex(Utf8NoBom.GetBytes(traceText));

        var totalFiles =
            plans.Sum(p => p.FileCount) + (options.IncludeGenerated ? definition.OptionalGeneratedFilesPerProject * plans.Count : 0);
        var dimensions = new CorpusDimensions(
            definition.ProjectCount,
            definition.FilesPerProject,
            definition.OversizedProjectExtraFiles,
            definition.MethodsPerFile,
            definition.CallsPerMethod,
            definition.VocabularyCardinality,
            definition.Topology,
            definition.OptionalGeneratedFilesPerProject,
            totalFiles,
            (long)totalFiles * definition.MethodsPerFile * (definition.CallsPerMethod + 5L),
            SummarizeTopology(plans)
        );
        var projectManifests = plans
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => new ProjectManifest(
                p.Name,
                p.Role,
                $"{p.Name}/{p.Name}.csproj",
                p.FileCount + (options.IncludeGenerated ? definition.OptionalGeneratedFilesPerProject : 0),
                p.Dependencies.Select(index => plans[index].Name).Order(StringComparer.Ordinal).ToArray()
            ))
            .ToArray();
        var manifest = new CorpusManifest(
            SchemaVersion: 1,
            Preset: options.Preset,
            Seed: options.Seed,
            Dimensions: dimensions,
            Projects: projectManifests,
            Solution: "LiveScale.slnx",
            EditTrace: "edit-trace.json",
            EditTraceSha256: traceHash,
            HashAlgorithm: "SHA-256 over ordinal relative-forward-slash-path + NUL + lowercase-content-SHA-256 + LF; corpus-manifest.json is hashed without corpusSha256",
            IncludesGeneratedCode: options.IncludeGenerated
        );
        var provisionalManifest = Serialize(manifest);
        WriteRelative(options.OutputDirectory, "corpus-manifest.json", provisionalManifest);
        var corpusHash = ComputeCorpusHash(options.OutputDirectory, provisionalManifest);
        WriteRelative(options.OutputDirectory, "corpus-manifest.json", Serialize(manifest with { CorpusSha256 = corpusHash }));

        return new GenerationSummary(options.Preset, plans.Count, totalFiles, corpusHash, traceHash);
    }

    private static PresetDefinition LoadPreset(string preset)
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("The checked-in LiveScale manifest was not copied beside the generator.", manifestPath);
        }

        var manifest =
            JsonSerializer.Deserialize<GeneratorManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidDataException("LiveScale manifest is empty.");
        return manifest.Presets.TryGetValue(preset, out var definition)
            ? definition
            : throw new InvalidDataException($"Preset '{preset}' is absent from the LiveScale manifest.");
    }

    private static void Validate(PresetDefinition definition)
    {
        if (definition.ProjectCount < 4 || definition.FilesPerProject < 3 || definition.MethodsPerFile < 2)
        {
            throw new InvalidDataException("A preset requires at least 4 projects, 3 files/project, and 2 methods/file.");
        }
        if (definition.CallsPerMethod < 1 || definition.VocabularyCardinality < 2)
        {
            throw new InvalidDataException("A preset requires calls and a non-trivial string vocabulary.");
        }
        if (definition.Topology.OversizedProjectIndices.Length != 2)
        {
            throw new InvalidDataException("Exactly two oversized projects are required.");
        }
        if (
            definition.Topology.DiamondStride < 4
            || definition.Topology.OversizedCoverageDenominator < 1
            || definition.Topology.OversizedCoverageNumerator < 1
            || definition.Topology.OversizedCoverageNumerator >= definition.Topology.OversizedCoverageDenominator
        )
        {
            throw new InvalidDataException("Topology requires bounded diamonds and a proper oversized-project coverage ratio.");
        }
        var indices = new[] { definition.Topology.CoreHubIndex, definition.Topology.RuntimeIndex }
            .Concat(definition.Topology.OversizedProjectIndices)
            .ToArray();
        if (indices.Any(index => index < 0 || index >= definition.ProjectCount) || indices.Distinct().Count() != indices.Length)
        {
            throw new InvalidDataException("Topology role indices must be distinct and within the project range.");
        }
    }

    private static IReadOnlyList<ProjectPlan> CreateProjectPlans(PresetDefinition definition)
    {
        var topology = definition.Topology;
        var specialIndices = new HashSet<int>(topology.OversizedProjectIndices) { topology.CoreHubIndex, topology.RuntimeIndex };
        var featureIndices = Enumerable.Range(0, definition.ProjectCount).Where(index => !specialIndices.Contains(index)).ToArray();
        var plans = new List<ProjectPlan>(definition.ProjectCount);
        for (var index = 0; index < definition.ProjectCount; index++)
        {
            var role = ProjectRole(index, topology);
            var name = ProjectName(index, topology);
            var extra = topology.OversizedProjectIndices.Contains(index) ? definition.OversizedProjectExtraFiles : 0;
            plans.Add(
                new ProjectPlan(index, name, role, definition.FilesPerProject + extra, Dependencies(index, featureIndices, topology))
            );
        }
        return plans;
    }

    private static int[] Dependencies(int index, int[] featureIndices, TopologyDefinition topology)
    {
        if (index == topology.CoreHubIndex)
        {
            return [];
        }

        var dependencies = new SortedSet<int> { topology.CoreHubIndex };
        if (index == topology.RuntimeIndex)
        {
            return dependencies.ToArray();
        }

        dependencies.Add(topology.RuntimeIndex);
        var oversizedSlot = Array.IndexOf(topology.OversizedProjectIndices, index);
        if (oversizedSlot >= 0)
        {
            for (var ordinal = 0; ordinal < featureIndices.Length; ordinal++)
            {
                // Four fifths of low-level features flow into one oversized project. The remainder
                // preserve genuine leaves and prevent the synthetic corpus from becoming one giant fan-in.
                if (
                    ordinal % topology.OversizedCoverageDenominator < topology.OversizedCoverageNumerator
                    && ordinal % topology.OversizedProjectIndices.Length == oversizedSlot
                )
                {
                    dependencies.Add(featureIndices[ordinal]);
                }
            }
            return dependencies.ToArray();
        }

        var position = Array.IndexOf(featureIndices, index);
        var clusterOffset = position % topology.DiamondStride;
        if (clusterOffset == 1)
        {
            dependencies.Add(featureIndices[position - 1]);
        }
        else if (clusterOffset == 2)
        {
            dependencies.Add(featureIndices[position - 2]);
        }
        else if (clusterOffset == 3)
        {
            dependencies.Add(featureIndices[position - 2]);
            dependencies.Add(featureIndices[position - 1]);
        }
        return dependencies.ToArray();
    }

    private static string ProjectRole(int index, TopologyDefinition topology)
    {
        if (index == topology.CoreHubIndex)
        {
            return "core-contracts-hub";
        }
        if (index == topology.RuntimeIndex)
        {
            return "runtime";
        }
        var oversizedSlot = Array.IndexOf(topology.OversizedProjectIndices, index);
        return oversizedSlot switch
        {
            0 => "oversized-pages",
            1 => "oversized-data-access",
            _ => "feature",
        };
    }

    private static string ProjectName(int index, TopologyDefinition topology) =>
        ProjectRole(index, topology) switch
        {
            "core-contracts-hub" => "Core.Contracts",
            "runtime" => "Core.Runtime",
            "oversized-pages" => "Pages",
            "oversized-data-access" => "DataAccessTier",
            _ => $"Feature{index:000}",
        };

    private static void WriteProjects(
        string root,
        IReadOnlyList<ProjectPlan> plans,
        PresetDefinition definition,
        bool includeGenerated,
        ref SplitMix64 rng
    )
    {
        foreach (var project in plans.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            WriteRelative(root, $"{project.Name}/{project.Name}.csproj", ProjectFile(project, plans));
            for (var fileIndex = 0; fileIndex < project.FileCount; fileIndex++)
            {
                var source = SourceFile(project, fileIndex, plans, definition, ref rng);
                WriteRelative(root, $"{project.Name}/File{fileIndex:0000}.cs", source);
            }

            if (includeGenerated)
            {
                for (var generatedIndex = 0; generatedIndex < definition.OptionalGeneratedFilesPerProject; generatedIndex++)
                {
                    WriteRelative(
                        root,
                        $"{project.Name}/Generated/Generated{generatedIndex:0000}.g.cs",
                        GeneratedSource(project, generatedIndex)
                    );
                }
            }
        }
    }

    private static string ProjectFile(ProjectPlan project, IReadOnlyList<ProjectPlan> plans)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        builder.AppendLine("  <PropertyGroup>");
        builder.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        builder.AppendLine("    <ImplicitUsings>enable</ImplicitUsings>");
        builder.AppendLine("    <Nullable>enable</Nullable>");
        builder.AppendLine("    <Deterministic>true</Deterministic>");
        builder.AppendLine("  </PropertyGroup>");
        if (project.Dependencies.Length > 0)
        {
            builder.AppendLine("  <ItemGroup>");
            foreach (var dependency in project.Dependencies.Select(index => plans[index]).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                builder.AppendLine($"    <ProjectReference Include=\"../{dependency.Name}/{dependency.Name}.csproj\" />");
            }
            builder.AppendLine("  </ItemGroup>");
        }
        builder.AppendLine("</Project>");
        return builder.ToString();
    }

    private static string SourceFile(
        ProjectPlan project,
        int fileIndex,
        IReadOnlyList<ProjectPlan> plans,
        PresetDefinition definition,
        ref SplitMix64 rng
    )
    {
        var namespaceName = $"LiveScale.Project{project.Index:000}";
        var typeName = $"Type{project.Index:000}_{fileIndex:0000}";
        var bodyMarker = BodyMarker(project.Index, fileIndex);
        var vocabulary = rng.Next(definition.VocabularyCardinality);
        var builder = new StringBuilder();
        builder.AppendLine("// Deterministic LiveScale corpus source.");
        builder.AppendLine($"namespace {namespaceName};");
        builder.AppendLine();
        builder.AppendLine($"public static class {typeName}");
        builder.AppendLine("{");
        builder.AppendLine($"    private const int StableMarker = {bodyMarker};");
        builder.AppendLine($"    private const string SharedText = \"vocabulary-{vocabulary:0000}\";");
        builder.AppendLine($"    private const string UniqueText = \"project-{project.Index:000}-file-{fileIndex:0000}\";");
        builder.AppendLine("    // surface-edit-marker");

        for (var methodIndex = 0; methodIndex < definition.MethodsPerFile; methodIndex++)
        {
            builder.AppendLine($"    public static int Method{methodIndex:0000}(int value)");
            builder.AppendLine("    {");
            builder.AppendLine(
                methodIndex == 0
                    ? $"        var total = value + StableMarker + {bodyMarker}; // body-edit-marker"
                    : $"        var total = value + StableMarker + {methodIndex};"
            );
            for (var callIndex = 0; callIndex < definition.CallsPerMethod; callIndex++)
            {
                var target = CallTarget(project, fileIndex, methodIndex, callIndex, plans, definition);
                builder.AppendLine($"        total += {target}(value + {callIndex + 1});");
            }
            builder.AppendLine("        return total + SharedText.Length + UniqueText.Length;");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");

        AppendSemanticScenario(builder, project, fileIndex, definition.Topology);
        return builder.ToString();
    }

    private static string CallTarget(
        ProjectPlan project,
        int fileIndex,
        int methodIndex,
        int callIndex,
        IReadOnlyList<ProjectPlan> plans,
        PresetDefinition definition
    )
    {
        ProjectPlan targetProject;
        if (callIndex == 0 && project.Dependencies.Length > 0)
        {
            var callableDependencies = project.Role.StartsWith("oversized-", StringComparison.Ordinal)
                ? project.Dependencies.Where(index => plans[index].Role is "core-contracts-hub" or "runtime").ToArray()
                : project.Dependencies;
            targetProject = plans[callableDependencies[(fileIndex + methodIndex) % callableDependencies.Length]];
        }
        else
        {
            targetProject = project;
        }

        var targetFile = (fileIndex + methodIndex + callIndex + 1) % targetProject.FileCount;
        var targetMethod = (methodIndex + callIndex + 1) % definition.MethodsPerFile;
        return $"global::LiveScale.Project{targetProject.Index:000}.Type{targetProject.Index:000}_{targetFile:0000}.Method{targetMethod:0000}";
    }

    private static void AppendSemanticScenario(StringBuilder builder, ProjectPlan project, int fileIndex, TopologyDefinition topology)
    {
        if (project.Index == topology.CoreHubIndex && fileIndex == 0)
        {
            builder.AppendLine();
            builder.AppendLine("public interface IWorker");
            builder.AppendLine("{");
            builder.AppendLine("    int Execute(int value);");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("public abstract class WorkerBase : IWorker");
            builder.AppendLine("{");
            builder.AppendLine("    public virtual int Execute(int value) => value + 1;");
            builder.AppendLine("    public virtual string Describe() => \"worker\";");
            builder.AppendLine("}");
        }
        else if (project.Index == topology.RuntimeIndex && fileIndex == 0)
        {
            builder.AppendLine();
            builder.AppendLine($"public class IntermediateWorker : global::LiveScale.Project{topology.CoreHubIndex:000}.WorkerBase");
            builder.AppendLine("{");
            builder.AppendLine("    public override int Execute(int value) => value + 2;");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("public sealed class FinalWorker : IntermediateWorker");
            builder.AppendLine("{");
            builder.AppendLine("    public override int Execute(int value) => value + 3;");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("public sealed class InheritedWorker : IntermediateWorker;");
        }
        else if (project.Index == topology.OversizedProjectIndices[0] && fileIndex == 0)
        {
            builder.AppendLine();
            builder.AppendLine("public static class SemanticEntry");
            builder.AppendLine("{");
            builder.AppendLine("    public static async global::System.Threading.Tasks.Task<int> RunAsync(string path)");
            builder.AppendLine("    {");
            builder.AppendLine(
                $"        global::LiveScale.Project{topology.CoreHubIndex:000}.IWorker worker = new global::LiveScale.Project{topology.RuntimeIndex:000}.InheritedWorker();"
            );
            builder.AppendLine("        global::System.Func<int, int> methodGroup = worker.Execute;");
            builder.AppendLine("        global::System.Func<int, int> lambda = value => methodGroup(value) + 1;");
            builder.AppendLine("        await global::System.Threading.Tasks.Task.Yield();");
            builder.AppendLine("        return global::System.IO.File.Exists(path) ? lambda(7) : 0;");
            builder.AppendLine("    }");
            builder.AppendLine("}");
        }
    }

    private static string GeneratedSource(ProjectPlan project, int index) =>
        "// <auto-generated />\n"
        + $"namespace LiveScale.Project{project.Index:000}.Generated;\n\n"
        + $"internal static class Generated{index:0000} {{ public static int Value => {index}; }}\n";

    private static void WriteSolution(string root, IReadOnlyList<ProjectPlan> plans)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<Solution>");
        foreach (var project in plans.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            builder.AppendLine($"  <Project Path=\"{project.Name}/{project.Name}.csproj\" />");
        }
        builder.AppendLine("</Solution>");
        WriteRelative(root, "LiveScale.slnx", builder.ToString());
    }

    private static EditTrace CreateEditTrace(ulong seed, IReadOnlyList<ProjectPlan> plans, ref SplitMix64 rng)
    {
        var transitiveDependents = TransitiveDependents(plans);
        var hub = plans.OrderByDescending(p => transitiveDependents[p.Index].Count).ThenBy(p => p.Name, StringComparer.Ordinal).First();
        var leaf = plans
            .Where(p => p.Role == "feature" && transitiveDependents[p.Index].Count == 0)
            .DefaultIfEmpty(plans.Where(p => transitiveDependents[p.Index].Count == 0).OrderBy(p => p.Name, StringComparer.Ordinal).First())
            .OrderByDescending(p => p.Index)
            .First();
        var medianDependents = Median(plans.Where(p => p.Role == "feature").Select(p => transitiveDependents[p.Index].Count));
        var medium = plans
            .Where(p => p.Role == "feature" && p.Index != leaf.Index && transitiveDependents[p.Index].Count > 0)
            .OrderBy(p => Math.Abs(transitiveDependents[p.Index].Count - Math.Max(1, medianDependents)))
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .First();
        var hot = plans.Single(p => p.Role == "oversized-pages");
        var classes = new[] { ("hub", hub), ("medium", medium), ("leaf", leaf) };
        var edits = new List<EditStep>(20);

        for (var index = 0; index < 12; index++)
        {
            var target = index is 0 or 3 or 6 or 9 ? hot : classes[index % classes.Length].Item2;
            var targetClass = index is 0 or 3 or 6 or 9 ? "hot" : classes[index % classes.Length].Item1;
            var file = index is 0 or 3 or 6 or 9 ? 0 : rng.Next(target.FileCount);
            edits.Add(new EditStep($"edit-{index + 1:00}", "body", "single-save", targetClass, [BodyMutation(target, file, index)]));
        }

        for (var index = 0; index < 4; index++)
        {
            var target = classes[index % classes.Length];
            edits.Add(
                new EditStep(
                    $"edit-{index + 13:00}",
                    "surface",
                    "public-api-change",
                    target.Item1,
                    [SurfaceMutation(target.Item2, rng.Next(target.Item2.FileCount), index)]
                )
            );
        }

        for (var index = 0; index < 4; index++)
        {
            var mutationList = new List<FileMutation>(classes.Length);
            for (var offset = 0; offset < classes.Length; offset++)
            {
                var target = classes[offset];
                mutationList.Add(BodyMutation(target.Item2, rng.Next(target.Item2.FileCount), 100 + index * 10 + offset));
            }
            var mutations = mutationList.OrderBy(m => m.File, StringComparer.Ordinal).ToArray();
            edits.Add(new EditStep($"edit-{index + 17:00}", "batch", index % 2 == 0 ? "save-all" : "branch-switch", "mixed", mutations));
        }

        var intersectingEdit = edits.Single(edit => edit.Id == "edit-01");
        var intersectingMutation = intersectingEdit.Mutations.Single();
        var hotDependencies = TransitiveDependencies(hot, plans);
        var disjointEdit = edits
            .Where(edit => edit.Kind == "body")
            .First(edit =>
            {
                var target = plans.Single(plan => plan.Name == edit.Mutations.Single().Project);
                return target.Index != hot.Index && !hotDependencies.Contains(target.Index);
            });
        var disjointMutation = disjointEdit.Mutations.Single();
        var queryPattern = $"M:LiveScale.Project{hot.Index:000}.SemanticEntry.RunAsync(System.String)";
        var queryFile = $"{hot.Name}/File0000.cs";
        var querySeeds = new[]
        {
            new QuerySeed(
                "query-intersects-hot-body",
                intersectingEdit.Id,
                intersectingMutation.File,
                queryFile,
                "intersects",
                queryPattern
            ),
            new QuerySeed("query-disjoint-low-level-body", disjointEdit.Id, disjointMutation.File, queryFile, "disjoint", queryPattern),
        };
        return new EditTrace(1, seed, "apply-then-revert-each-step", edits, querySeeds);
    }

    private static FileMutation BodyMutation(ProjectPlan project, int fileIndex, int delta)
    {
        var marker = $"        var total = value + StableMarker + {BodyMarker(project.Index, fileIndex)}; // body-edit-marker";
        var replacement =
            $"        var total = value + StableMarker + {BodyMarker(project.Index, fileIndex) + delta + 1}; // body-edit-marker";
        return new FileMutation(
            project.Name,
            $"{project.Name}/File{fileIndex:0000}.cs",
            "change-local-expression",
            marker,
            replacement,
            replacement,
            marker
        );
    }

    private static FileMutation SurfaceMutation(ProjectPlan project, int fileIndex, int suffix)
    {
        const string Marker = "    // surface-edit-marker";
        var replacement = $"    public static int AddedSurface{suffix:00}(int value) => value + {suffix + 1};\n{Marker}";
        return new FileMutation(
            project.Name,
            $"{project.Name}/File{fileIndex:0000}.cs",
            "add-member",
            Marker,
            replacement,
            replacement,
            Marker
        );
    }

    private static int BodyMarker(int projectIndex, int fileIndex) => projectIndex * 100_000 + fileIndex;

    private static TopologySummary SummarizeTopology(IReadOnlyList<ProjectPlan> plans)
    {
        var dependents = TransitiveDependents(plans);
        var counts = plans.Select(plan => dependents[plan.Index].Count).ToArray();
        var affectedFiles = plans.Select(plan => plan.FileCount + dependents[plan.Index].Sum(index => plans[index].FileCount)).ToArray();
        var oversized = plans
            .Where(plan => plan.Role.StartsWith("oversized-", StringComparison.Ordinal))
            .Select(plan => plan.Index)
            .ToHashSet();
        var featuresPullingOversized = plans.Count(plan => plan.Role == "feature" && dependents[plan.Index].Overlaps(oversized));
        return new TopologySummary(
            counts.Max(),
            Median(counts),
            counts.Count(count => count >= plans.Count / 2),
            Median(affectedFiles),
            featuresPullingOversized
        );
    }

    private static Dictionary<int, HashSet<int>> TransitiveDependents(IReadOnlyList<ProjectPlan> plans)
    {
        var result = plans.ToDictionary(plan => plan.Index, _ => new HashSet<int>());
        foreach (var candidate in plans)
        {
            foreach (var dependency in TransitiveDependencies(candidate, plans))
            {
                result[dependency].Add(candidate.Index);
            }
        }
        return result;
    }

    private static HashSet<int> TransitiveDependencies(ProjectPlan project, IReadOnlyList<ProjectPlan> plans)
    {
        var result = new HashSet<int>();
        var pending = new Stack<int>(project.Dependencies);
        while (pending.TryPop(out var dependency))
        {
            if (!result.Add(dependency))
            {
                continue;
            }
            foreach (var ancestor in plans[dependency].Dependencies)
            {
                pending.Push(ancestor);
            }
        }
        return result;
    }

    private static int Median(IEnumerable<int> values)
    {
        var ordered = values.Order().ToArray();
        return ordered[ordered.Length / 2];
    }

    private static string ComputeCorpusHash(string root, string provisionalManifest)
    {
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in EnumerateCorpusFiles(root))
        {
            var relative = RelativePath(root, path);
            var contentHash =
                relative == "corpus-manifest.json"
                    ? Sha256Hex(Utf8NoBom.GetBytes(provisionalManifest))
                    : Sha256Hex(File.ReadAllBytes(path));
            var row = Utf8NoBom.GetBytes(relative + "\0" + contentHash + "\n");
            aggregate.AppendData(row);
        }
        return Convert.ToHexStringLower(aggregate.GetHashAndReset());
    }

    private static IEnumerable<string> EnumerateCorpusFiles(string root) =>
        Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !RelativePath(root, path).Split('/').Any(segment => segment is "bin" or "obj" or ".rig"))
            .OrderBy(path => RelativePath(root, path), StringComparer.Ordinal);

    private static string RelativePath(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string Serialize<T>(T value) => NormalizeLf(JsonSerializer.Serialize(value, JsonOptions)) + "\n";

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void WriteRelative(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, NormalizeLf(content), Utf8NoBom);
    }

    private static string NormalizeLf(string content) => content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static void ResetOutput(string root)
    {
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            return;
        }

        var rootInfo = new DirectoryInfo(root);
        if (rootInfo.LinkTarget is not null || rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException($"Refusing to replace symlinked generator directory: {root}");
        }

        var entries = Directory.EnumerateFileSystemEntries(root).ToArray();
        if (entries.Length == 0)
        {
            return;
        }
        var markerPath = Path.Combine(root, MarkerFileName);
        var marker = new FileInfo(markerPath);
        if (
            !marker.Exists
            || marker.LinkTarget is not null
            || marker.Attributes.HasFlag(FileAttributes.ReparsePoint)
            || !File.ReadAllBytes(markerPath).AsSpan().SequenceEqual(Utf8NoBom.GetBytes(MarkerContent))
        )
        {
            throw new InvalidOperationException($"Refusing to replace non-generator directory: {root}");
        }

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }
}
