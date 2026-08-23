using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis;
using Rig.Analysis.Inventory;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Analysis;

public sealed class DtbFailedBuildCacheAdmissionTests
{
    private static readonly MetadataReference[] FrameworkReferences = ((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string) ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    [Test]
    public void Legacy_sidecar_without_admission_bit_is_a_miss()
    {
        using var fixture = new CacheFixture();
        fixture.WriteLegacySidecar("current", BuildInfo());

        var decision = BuildCacheDecision.Decide(currentFingerprint: "current", stored: fixture.Cache.Load(fixture.ProjectPath));

        decision.ShouldBeOfType<BuildCacheDecision.Miss>().Fingerprint.ShouldBe("current");
    }

    [Test]
    public void Admitted_sidecar_without_candidate_identity_is_legacy_and_misses()
    {
        var legacy = new StoredBuild(Fingerprint: "current", Info: BuildInfo(), Admitted: true);

        BuildCacheDecision.Decide("current", legacy).ShouldBeOfType<BuildCacheDecision.Miss>();
    }

    [Test]
    public void Unadmitted_candidate_is_a_miss()
    {
        using var fixture = new CacheFixture();
        fixture.Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: true);

        var decision = BuildCacheDecision.Decide(currentFingerprint: "current", stored: fixture.Cache.Load(fixture.ProjectPath));

        decision.ShouldBeOfType<BuildCacheDecision.Miss>();
    }

    [Test]
    public void Failed_matching_sidecar_is_a_miss()
    {
        var failed = new StoredBuild(Fingerprint: "current", Info: BuildInfo(), Admitted: false);

        var decision = BuildCacheDecision.Decide(currentFingerprint: "current", stored: failed);

        decision.ShouldBeOfType<BuildCacheDecision.Miss>().Fingerprint.ShouldBe("current");
    }

    [Test]
    public void Roslyn_promotion_makes_a_candidate_hittable()
    {
        using var fixture = new CacheFixture();
        var candidateId = fixture
            .Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: true)
            .ShouldNotBeNull();

        SolutionSourceLoader
            .ApplyCompilationCacheAdmission(fixture.Cache, fixture.ProjectPath, candidateId, compilationSucceeded: true)
            .ShouldBeTrue();

        var stored = fixture.Cache.Load(fixture.ProjectPath);
        stored.ShouldNotBeNull().Admitted.ShouldBeTrue();
        BuildCacheDecision.Decide("current", stored).ShouldBeOfType<BuildCacheDecision.Hit>();
    }

    [Test]
    public void Roslyn_rejection_removes_a_candidate()
    {
        using var fixture = new CacheFixture();
        var candidateId = fixture.Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: true);

        SolutionSourceLoader.ApplyCompilationCacheAdmission(fixture.Cache, fixture.ProjectPath, candidateId, compilationSucceeded: false);

        fixture.Cache.Load(fixture.ProjectPath).ShouldBeNull();
        Directory.GetFiles(fixture.CacheDirectory, "*.json").ShouldBeEmpty();
    }

    [Test]
    public void No_compilation_verdict_evicts_an_admitted_hit()
    {
        using var fixture = new CacheFixture();
        var candidateId = fixture
            .Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: true)
            .ShouldNotBeNull();
        fixture.Cache.PromoteCandidate(fixture.ProjectPath, candidateId).ShouldBeTrue();

        SolutionSourceLoader.ApplyCompilationCacheAdmission(
            fixture.Cache,
            fixture.ProjectPath,
            candidateId: null,
            compilationSucceeded: false
        );

        fixture.Cache.Load(fixture.ProjectPath).ShouldBeNull();
    }

    [Test]
    public void Clean_compilation_confirms_an_admitted_hit_without_a_run_token()
    {
        using var fixture = new CacheFixture();
        var candidateId = fixture
            .Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: true)
            .ShouldNotBeNull();
        fixture.Cache.PromoteCandidate(fixture.ProjectPath, candidateId).ShouldBeTrue();

        SolutionSourceLoader
            .ApplyCompilationCacheAdmission(fixture.Cache, fixture.ProjectPath, candidateId: null, compilationSucceeded: true)
            .ShouldBeFalse();

        BuildCacheDecision.Decide("current", fixture.Cache.Load(fixture.ProjectPath)).ShouldBeOfType<BuildCacheDecision.Hit>();
    }

    [Test]
    public void Failed_Buildalyzer_result_is_rejected_immediately()
    {
        using var fixture = new CacheFixture();
        var oldCandidateId = fixture
            .Cache.StoreCandidate(fixture.ProjectPath, "old", BuildInfo(), buildalyzerSucceeded: true)
            .ShouldNotBeNull();
        fixture.Cache.PromoteCandidate(fixture.ProjectPath, oldCandidateId).ShouldBeTrue();

        fixture.Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: false);

        fixture.Cache.Load(fixture.ProjectPath).ShouldBeNull();
        Directory.GetFiles(fixture.CacheDirectory, "*.json").ShouldBeEmpty();
    }

    [Test]
    public void Wrong_candidate_token_cannot_promote_a_sidecar()
    {
        using var fixture = new CacheFixture();
        fixture.Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo(), buildalyzerSucceeded: true);

        fixture.Cache.PromoteCandidate(fixture.ProjectPath, "another-writers-token").ShouldBeFalse();

        var stored = fixture.Cache.Load(fixture.ProjectPath).ShouldNotBeNull();
        stored.Admitted.ShouldBeFalse();
        BuildCacheDecision.Decide("current", stored).ShouldBeOfType<BuildCacheDecision.Miss>();
    }

    [Test]
    public void Same_fingerprint_writers_cannot_cross_admit_each_others_payload()
    {
        using var fixture = new CacheFixture();
        var firstToken = fixture
            .Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo("first.dll"), buildalyzerSucceeded: true)
            .ShouldNotBeNull();
        var secondToken = fixture
            .Cache.StoreCandidate(fixture.ProjectPath, "current", BuildInfo("second.dll"), buildalyzerSucceeded: true)
            .ShouldNotBeNull();

        fixture.Cache.PromoteCandidate(fixture.ProjectPath, firstToken).ShouldBeFalse();
        fixture.Cache.PromoteCandidate(fixture.ProjectPath, secondToken).ShouldBeTrue();

        var admitted = fixture.Cache.Load(fixture.ProjectPath).ShouldNotBeNull();
        admitted.Admitted.ShouldBeTrue();
        admitted.Info.References.ShouldBe(["second.dll"]);
    }

    [Test]
    public async Task Concurrent_candidate_writes_leave_parseable_complete_json()
    {
        using var fixture = new CacheFixture();

        await Task.WhenAll(
            Enumerable
                .Range(0, 32)
                .Select(i =>
                    Task.Run(() =>
                        fixture.Cache.StoreCandidate(
                            fixture.ProjectPath,
                            "current",
                            BuildInfo($"dependency-{i}.dll"),
                            buildalyzerSucceeded: true
                        )
                    )
                )
        );

        var stored = fixture.Cache.Load(fixture.ProjectPath).ShouldNotBeNull();
        stored.Admitted.ShouldBeFalse();
        stored.CandidateId.ShouldNotBeNullOrWhiteSpace();
        stored.Info.References.Count.ShouldBe(1);
        Directory.GetFiles(fixture.CacheDirectory, "*.tmp").ShouldBeEmpty();
    }

    [Test]
    public async Task Read_sources_callback_reports_a_clean_base_compilation()
    {
        var verdict = await ObserveCompilationVerdictAsync("public sealed class Clean { }");

        verdict.Succeeded.ShouldBeTrue();
    }

    [Test]
    public async Task Read_sources_callback_reports_a_base_compilation_error()
    {
        var verdict = await ObserveCompilationVerdictAsync("public sealed class Broken { MissingType Value; }");

        verdict.Succeeded.ShouldBeFalse();
    }

    [Test]
    public void Cache_summary_discloses_failed_build_rejections()
    {
        SolutionSourceLoader
            .FormatBuildCacheSummary(hits: 1, misses: 2, rejectedBuilds: 1, projects: 3)
            .ShouldBe("build cache: 1 hit(s), 2 miss(es), 1 failed Buildalyzer result(s) rejected of 3 project(s)");
    }

    private static ProjectBuildInfo BuildInfo(string reference = "/packages/Dependency.dll") =>
        new(
            ProjectFilePath: "/repo/App/App.csproj",
            References: [reference],
            ProjectReferences: [],
            SourceFiles: ["/repo/App/Program.cs"],
            AnalyzerReferences: [],
            PreprocessorSymbols: [],
            Properties: new Dictionary<string, string>(StringComparer.Ordinal)
        );

    private static async Task<(string ProjectFilePath, bool Succeeded)> ObserveCompilationVerdictAsync(string source)
    {
        using var workspace = new AdhocWorkspace();
        var root = Path.Combine(Path.GetTempPath(), $"rig-compilation-callback-{Guid.NewGuid():N}");
        var solutionPath = Path.Combine(root, "Fixture.sln");
        var projectPath = Path.GetFullPath(Path.Combine(root, "Fixture.csproj"));
        var documentPath = Path.GetFullPath(Path.Combine(root, "Fixture.cs"));
        var projectId = ProjectId.CreateNewId("Fixture");
        var documentId = DocumentId.CreateNewId(projectId, "Fixture.cs");
        var solution = workspace
            .CurrentSolution.AddProject(
                ProjectInfo.Create(
                    projectId,
                    VersionStamp.Create(),
                    name: "Fixture",
                    assemblyName: "Fixture",
                    language: LanguageNames.CSharp,
                    filePath: projectPath,
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                    parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                    metadataReferences: FrameworkReferences
                )
            )
            .AddDocument(documentId, "Fixture.cs", SourceText.From(source), filePath: documentPath);
        var verdicts = new List<(string ProjectFilePath, bool Succeeded)>();

        await SolutionSourceLoader.ReadSolutionSourcesAsync(
            solution,
            solutionPath,
            new Rig.Domain.Data.RuleSet(),
            models => models.Select(_ => new SourceExtractionResult([], [], [], [], [], [])).ToArray(),
            CancellationToken.None,
            parallelism: 1,
            onProjectCompilation: (path, succeeded) => verdicts.Add((Path.GetFullPath(path), succeeded))
        );

        verdicts.Count.ShouldBe(1);
        verdicts[0].ProjectFilePath.ShouldBe(projectPath);
        return verdicts[0];
    }

    private sealed class CacheFixture : IDisposable
    {
        public CacheFixture()
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), $"rig-cache-admission-{Guid.NewGuid():N}");
            ProjectPath = Path.Combine(CacheDirectory, "src", "App", "App.csproj");
            Cache = new BuildResultCache(CacheDirectory);
        }

        public string CacheDirectory { get; }
        public string ProjectPath { get; }
        public BuildResultCache Cache { get; }

        public void WriteLegacySidecar(string fingerprint, ProjectBuildInfo info)
        {
            Cache.StoreCandidate(ProjectPath, fingerprint, info, buildalyzerSucceeded: true);
            var sidecar = Directory.GetFiles(CacheDirectory, "*.json").Single();
            File.WriteAllText(sidecar, JsonSerializer.Serialize(new { Fingerprint = fingerprint, Info = info }));
        }

        public void Dispose()
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, recursive: true);
            }
        }
    }
}
