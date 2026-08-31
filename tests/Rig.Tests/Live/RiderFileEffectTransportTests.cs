using System.Collections.Immutable;
using System.IO.Pipes;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Rig.Analysis.Inventory;
using Rig.Cli.Live;
using Rig.Domain.Data;
using Rig.Domain.Functions;
using Shouldly;
using static Rig.Cli.Live.LiveQueryTransport;

namespace Rig.Tests.Live;

public sealed class RiderFileEffectTransportTests
{
    // The responder normalises a requested path through Path.GetFullPath before joining it to the read
    // model, so a POSIX-shaped literal is rewritten to "C:\repo\…" on Windows and matches nothing. The
    // fixture therefore builds platform-native absolute paths, for which that normalisation is identity.
    private static readonly string RepoRoot = Path.GetFullPath(OperatingSystem.IsWindows() ? @"C:\repo" : "/repo");
    private static readonly string EffectFile = Path.Combine(RepoRoot, "Effectful.cs");
    private static readonly string CleanFile = Path.Combine(RepoRoot, "Clean.cs");
    private static readonly string OwnerFile = Path.Combine(RepoRoot, "Owners.cs");

    [Test]
    public async Task Typed_round_trip_routes_file_effects_and_echoes_client_correlation()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rider-file-effects-").FullName;
        try
        {
            var served = 0;
            await using var server = LiveQueryServer.Start(
                directory,
                (_, _) => Task.FromResult(LiveServeResult.Declined("rendered callback must not run")),
                new StringWriter(),
                serveFileEffects: (request, _) =>
                {
                    Interlocked.Increment(ref served);
                    return Task.FromResult(
                        new RiderFileEffectResponse(
                            Protocol,
                            StatusOk,
                            request.RequestId,
                            request.FilePath,
                            request.ClientSnapshotToken,
                            GraphGeneration: 42,
                            RiderFileEffectResponder.SourceExact,
                            [new RiderFileEffectMethod("M:Fixture.Query", "sql", 1)],
                            [new RiderFileEffectCallSite("M:Fixture.Query", "M:Fixture.Read", 17, "sql", 0)],
                            Reason: ""
                        )
                    );
                }
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
            var request = Request(directory, "request-17", "psi-99", EffectFile);

            var response = await AskFileEffectsAsync(server.PipeName, request);

            Volatile.Read(ref served).ShouldBe(1);
            response.Status.ShouldBe(StatusOk);
            response.RequestId.ShouldBe("request-17");
            response.ClientSnapshotToken.ShouldBe("psi-99");
            response.FilePath.ShouldBe(EffectFile);
            response.GraphGeneration.ShouldBe(42);
            response.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceExact);
            response.Methods.ShouldBe([new RiderFileEffectMethod("M:Fixture.Query", "sql", 1)]);
            response.CallSites.ShouldBe([new RiderFileEffectCallSite("M:Fixture.Query", "M:Fixture.Read", 17, "sql", 0)]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task Typed_protocol_and_directory_guards_decline_without_reaching_the_responder()
    {
        var hostDirectory = Directory.CreateTempSubdirectory("rig-rider-file-effects-host-").FullName;
        var otherDirectory = Directory.CreateTempSubdirectory("rig-rider-file-effects-other-").FullName;
        try
        {
            var served = 0;
            await using var server = LiveQueryServer.Start(
                hostDirectory,
                (_, _) => Task.FromResult(LiveServeResult.Declined("rendered callback must not run")),
                new StringWriter(),
                serveFileEffects: (request, _) =>
                {
                    Interlocked.Increment(ref served);
                    return Task.FromResult(RiderFileEffectResponder.Declined(request, "unexpected"));
                }
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();

            var wrongProtocolRequest = Request(hostDirectory, "protocol-id", "protocol-token", EffectFile) with { Protocol = Protocol + 1 };
            var wrongProtocol = await AskFileEffectsAsync(server.PipeName, wrongProtocolRequest);
            wrongProtocol.Protocol.ShouldBe(Protocol);
            wrongProtocol.Status.ShouldBe(StatusDeclined);
            wrongProtocol.RequestId.ShouldBe("protocol-id");
            wrongProtocol.ClientSnapshotToken.ShouldBe("protocol-token");
            wrongProtocol.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceStale);
            wrongProtocol.Methods.ShouldBeEmpty();
            wrongProtocol.CallSites.ShouldBeEmpty();
            wrongProtocol.Reason.ShouldContain("protocol mismatch");

            var wrongDirectoryRequest = Request(otherDirectory, "directory-id", "directory-token", CleanFile);
            var wrongDirectory = await AskFileEffectsAsync(server.PipeName, wrongDirectoryRequest);
            wrongDirectory.Status.ShouldBe(StatusDeclined);
            wrongDirectory.RequestId.ShouldBe("directory-id");
            wrongDirectory.ClientSnapshotToken.ShouldBe("directory-token");
            wrongDirectory.FilePath.ShouldBe(CleanFile);
            wrongDirectory.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceStale);
            wrongDirectory.Methods.ShouldBeEmpty();
            wrongDirectory.Reason.ShouldContain("is watching");
            Volatile.Read(ref served).ShouldBe(0);
        }
        finally
        {
            Directory.Delete(hostDirectory, recursive: true);
            Directory.Delete(otherDirectory, recursive: true);
        }
    }

    [Test]
    public async Task Existing_rendered_query_response_keeps_its_original_wire_shape()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rendered-query-shape-").FullName;
        try
        {
            var result = LiveServeResult.Answered(0, "stdout\n", "stderr\n", "live: exact");
            await using var server = LiveQueryServer.Start(
                directory,
                (_, _) => Task.FromResult(result),
                new StringWriter(),
                serveFileEffects: (_, _) => throw new InvalidOperationException("typed callback must not run")
            );
            (await server.WaitUntilReadyAsync(TimeSpan.FromSeconds(10))).ShouldBeTrue();
            var request = new LiveQueryRequest(Protocol, LiveQueryVerbs.Reaches, directory, "{}");

            var actualFrame = await AskRawAsync(server.PipeName, JsonSerializer.SerializeToUtf8Bytes(request, Json));
            var expected = new LiveQueryResponse(Protocol, StatusOk, 0, "stdout\n", "stderr\n", "live: exact", "");

            actualFrame.ShouldBe(JsonSerializer.SerializeToUtf8Bytes(expected, Json));
            LiveQueryVerbs.Routable.ShouldNotContain(RiderFileEffectResponder.Verb);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Responder_distinguishes_effectful_clean_unindexed_ambiguous_and_stale_files()
    {
        var index = SqlIndex();
        var builds = 0;
        FileEffectReadModelIndex Build()
        {
            builds++;
            return index;
        }

        var effectfulRequest = Request(RepoRoot, "effectful-id", "effectful-token", EffectFile);
        var effectful = RiderFileEffectResponder.Respond(
            effectfulRequest,
            new RiderFileEffectCapture(19, ["project:A"], StaleReason: null, Build)
        );
        effectful.Status.ShouldBe(StatusOk);
        effectful.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceExact);
        effectful.GraphGeneration.ShouldBe(19);
        effectful.RequestId.ShouldBe("effectful-id");
        effectful.ClientSnapshotToken.ShouldBe("effectful-token");
        effectful
            .Methods.Select(method => (method.SymbolId, method.Family, method.NearestDepth))
            .ShouldBe([("M:File.Command", "sql", 1), ("M:File.Ef", "sql", 1)]);
        effectful
            .CallSites.Select(callSite =>
                (callSite.EnclosingSymbolId, callSite.TargetSymbolId, callSite.Line, callSite.Family, callSite.NearestDepth)
            )
            .ShouldBe([("M:File.Command", "M:CommandOwner", 20, "sql", 0), ("M:File.Ef", "M:EfOwner", 10, "sql", 0)]);
        RiderFileEffectResponder
            .SqlSelector.Predicates.Select(predicate => predicate.Provider)
            .ShouldBe(["efcore", "db_connection", "db_reader", "db_command", "db_transaction", "yessql"]);

        var clean = RiderFileEffectResponder.Respond(
            Request(RepoRoot, "clean-id", "clean-token", CleanFile),
            new RiderFileEffectCapture(19, ["project:A"], StaleReason: null, Build)
        );
        clean.Status.ShouldBe(StatusOk);
        clean.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceExact);
        clean.Methods.ShouldBeEmpty();
        clean.CallSites.ShouldBeEmpty();
        clean.Reason.ShouldBe("");

        var builtBeforeFailures = builds;
        var unindexed = RiderFileEffectResponder.Respond(
            Request(RepoRoot, "unindexed-id", "unindexed-token", Path.Combine(RepoRoot, "Missing.cs")),
            new RiderFileEffectCapture(20, [], StaleReason: null, Build)
        );
        unindexed.Status.ShouldBe(StatusOk);
        unindexed.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceUnindexed);
        unindexed.Methods.ShouldBeEmpty();
        unindexed.CallSites.ShouldBeEmpty();
        unindexed.Reason.ShouldNotBeEmpty();

        var ambiguous = RiderFileEffectResponder.Respond(
            Request(RepoRoot, "ambiguous-id", "ambiguous-token", EffectFile),
            new RiderFileEffectCapture(21, ["project:A", "project:B"], StaleReason: null, Build)
        );
        ambiguous.Status.ShouldBe(StatusOk);
        ambiguous.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceAmbiguous);
        ambiguous.Methods.ShouldBeEmpty();
        ambiguous.CallSites.ShouldBeEmpty();
        ambiguous.Reason.ShouldContain("2 project contexts");

        var stale = RiderFileEffectResponder.Respond(
            Request(RepoRoot, "stale-id", "stale-token", EffectFile),
            new RiderFileEffectCapture(22, ["project:A"], "one project unreconciled", Build)
        );
        stale.Status.ShouldBe(StatusOk);
        stale.SourceStatus.ShouldBe(RiderFileEffectResponder.SourceStale);
        stale.Methods.ShouldBeEmpty();
        stale.CallSites.ShouldBeEmpty();
        stale.Reason.ShouldBe("one project unreconciled");
        builds.ShouldBe(builtBeforeFailures, "non-exact source states must not force the reverse read model");
    }

    [Test]
    public void Indexed_context_detection_distinguishes_one_project_from_a_linked_file()
    {
        var directory = Directory.CreateTempSubdirectory("rig-rider-contexts-").FullName;
        try
        {
            var shared = Path.Combine(directory, "Shared.cs");
            var unique = Path.Combine(directory, "Unique.cs");
            var excluded = Path.Combine(directory, "Excluded.cs");
            using var workspace = new AdhocWorkspace();
            var projectA = ProjectId.CreateNewId("A");
            var projectB = ProjectId.CreateNewId("B");
            var projectC = ProjectId.CreateNewId("C");
            var solution = workspace
                .CurrentSolution.AddProject(Project(projectA, "A", Path.Combine(directory, "A.csproj")))
                .AddProject(Project(projectB, "B", Path.Combine(directory, "B.csproj")))
                .AddProject(Project(projectC, "C", Path.Combine(directory, "C.csproj")))
                .AddDocument(DocumentId.CreateNewId(projectA), "Shared.cs", SourceText.From(""), filePath: shared)
                .AddDocument(DocumentId.CreateNewId(projectB), "Shared.cs", SourceText.From(""), filePath: shared)
                .AddDocument(DocumentId.CreateNewId(projectC), "Unique.cs", SourceText.From(""), filePath: unique)
                .AddDocument(DocumentId.CreateNewId(projectC), "Excluded.cs", SourceText.From(""), filePath: excluded);
            var facts = new AnalysisResult(
                Path.Combine(directory, "Fixture.sln"),
                [
                    Source("A", shared, "indexed"),
                    Source("B", shared, "indexed"),
                    Source("C", unique, "indexed"),
                    Source("C", excluded, "excluded"),
                ],
                []
            );
            var snapshot = new FactSnapshot(
                new FactRevision(7),
                solution,
                facts,
                ImmutableDictionary.Create<string, FileFacts>(StringComparer.OrdinalIgnoreCase),
                DirtySet.Empty,
                SnapshotDelta.Empty
            );

            RiderFileEffectResponder.IndexedProjectContexts(snapshot, shared).Count.ShouldBe(2);
            RiderFileEffectResponder.IndexedProjectContexts(snapshot, unique).Count.ShouldBe(1);
            RiderFileEffectResponder.IndexedProjectContexts(snapshot, excluded).ShouldBeEmpty();
            RiderFileEffectResponder.IndexedProjectContexts(snapshot, Path.Combine(directory, "Missing.cs")).ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static RiderFileEffectRequest Request(string workingDirectory, string requestId, string token, string filePath) =>
        new(Protocol, RiderFileEffectResponder.Verb, workingDirectory, requestId, filePath, token);

    private static ProjectInfo Project(ProjectId id, string name, string filePath) =>
        ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp, filePath: filePath);

    private static SourceFileInfo Source(string project, string filePath, string status) =>
        new(project, filePath, status, "high", "fixture", "fixture", "");

    private static async Task<RiderFileEffectResponse> AskFileEffectsAsync(string pipeName, RiderFileEffectRequest request)
    {
        var frame = await AskRawAsync(pipeName, JsonSerializer.SerializeToUtf8Bytes(request, Json));
        return JsonSerializer.Deserialize<RiderFileEffectResponse>(frame, Json).ShouldNotBeNull();
    }

    private static async Task<byte[]> AskRawAsync(string pipeName, byte[] request)
    {
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10_000);
        await WriteFrameAsync(pipe, request, CancellationToken.None);
        return (await ReadFrameAsync(pipe, CancellationToken.None)).ShouldNotBeNull();
    }

    private static FileEffectReadModelIndex SqlIndex()
    {
        var graph = Graph(
            [
                new CallEdge("M:File.Ef", "M:EfOwner", "invocation", EffectFile, 10),
                new CallEdge("M:File.Command", "M:CommandOwner", "invocation", EffectFile, 20),
                new CallEdge("M:File.Http", "M:HttpOwner", "invocation", EffectFile, 30),
            ],
            "M:File.Clean"
        );
        var symbols = new[]
        {
            Method("M:File.Ef", EffectFile, 10),
            Method("M:File.Command", EffectFile, 20),
            Method("M:File.Http", EffectFile, 30),
            Method("M:File.Clean", CleanFile, 5),
            Method("M:EfOwner", OwnerFile, 10),
            Method("M:CommandOwner", OwnerFile, 20),
            Method("M:HttpOwner", OwnerFile, 30),
        };
        var effects = new[]
        {
            new DerivedEffect("efcore", "read", "db", "M:EfOwner", OwnerFile, 10),
            new DerivedEffect("db_command", "execute", "db", "M:CommandOwner", OwnerFile, 20),
            new DerivedEffect("http", "send", "network", "M:HttpOwner", OwnerFile, 30),
        };
        return FileEffectReadModelIndex.Build(graph, symbols, effects, RiderFileEffectResponder.SqlSelector);
    }

    private static SymbolFact Method(string id, string file, int line) =>
        new(id, SymbolKinds.Method, id, "Fixture", "T:Fixture", "", "", id, file, line, line + 1, "Fixture", false, BodyHash: id);

    private static FactGraphData Graph(IReadOnlyList<CallEdge> edges, params string[] extraMethods)
    {
        var methods = edges
            .SelectMany(edge => new[] { edge.Caller, edge.Callee })
            .Concat(extraMethods)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new MethodRef(id, id, "T:Fixture"))
            .ToArray();
        return new FactGraphData(edges, Array.Empty<ImplementsEdge>(), methods, Array.Empty<BaseEdge>());
    }
}
