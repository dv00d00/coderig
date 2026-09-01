using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Rig.Cli;
using Rig.Cli.Caching;
using Rig.Cli.CommandLine;
using Rig.Cli.Services;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class AnnotateResidentTransportTests
{
    [Test]
    public async Task Resident_solution_cache_loads_once_for_different_file_requests()
    {
        var storeDirectory = Directory.CreateTempSubdirectory("rig-filefx-cache-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(storeDirectory, StoreLayout.DbFileName), Guid.NewGuid().ToString("N"));
            var rulesHash = Guid.NewGuid().ToString("N");
            var loads = 0;

            var forFirstFile = await WarmStore.ResidentFileEffectsAsync(
                storeDirectory,
                rulesHash,
                () => Task.FromResult(new CacheSentinel(++loads, "First.cs"))
            );
            var forSecondFile = await WarmStore.ResidentFileEffectsAsync(
                storeDirectory,
                rulesHash,
                () => Task.FromResult(new CacheSentinel(++loads, "Second.cs"))
            );

            loads.ShouldBe(1);
            ReferenceEquals(forFirstFile, forSecondFile).ShouldBeTrue();
            forSecondFile.FactoryRequest.ShouldBe("First.cs");
        }
        finally
        {
            try
            {
                Directory.Delete(storeDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public void Marker_lease_deletes_only_the_marker_it_published()
    {
        var root = Directory.CreateTempSubdirectory("rig-serve-marker-").FullName;
        try
        {
            var path = Path.Combine(StoreLayout.RigDir(root), AnnotateResidentTransport.MarkerFileName);
            using (ServeMarkerLease.Publish(root, 5049, "http://localhost:5049")) { }
            File.Exists(path).ShouldBeFalse();

            using var lease = ServeMarkerLease.Publish(root, 5050, "http://localhost:5050");
            var own = AnnotateResidentTransport.ReadMarker(path).ShouldNotBeNull();
            var replacement = own with { Port = 5051, Url = "http://localhost:5051", StartedUtc = own.StartedUtc.AddSeconds(1) };
            File.WriteAllText(path, JsonSerializer.Serialize(replacement, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            lease.Dispose();

            AnnotateResidentTransport.ReadMarker(path).ShouldBe(replacement);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Test]
    public async Task Explicit_and_discovered_hosts_match_cold_stdout_and_disclose_transport()
    {
        await using var fixture = await AnnotateFixture.CreateAsync();
        await fixture.StartHostAsync();

        var cold = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--cold");
        var explicitHost = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--host", fixture.HostUrl, "--time");
        fixture.PublishMarker();
        var discovered = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv");

        explicitHost.Exit.ShouldBe(0, explicitHost.Err);
        discovered.Exit.ShouldBe(0, discovered.Err);
        explicitHost.Out.ShouldBe(cold.Out);
        discovered.Out.ShouldBe(cold.Out);
        explicitHost.Err.ShouldContain("transport: rig serve");
        explicitHost.Err.ShouldContain("transport");
        discovered.Err.ShouldContain("transport: rig serve");

        var coldEmpty = await fixture.RunAsync("annotate", fixture.EmptyFilePath, "--format", "tsv", "--cold");
        var residentEmpty = await fixture.RunAsync("annotate", fixture.EmptyFilePath, "--format", "tsv", "--host", fixture.HostUrl);
        residentEmpty.Exit.ShouldBe(0, residentEmpty.Err);
        residentEmpty.Out.ShouldBe(coldEmpty.Out);
        residentEmpty.Err.ShouldContain("transport: rig serve");
    }

    [Test]
    public async Task Explicit_store_is_forwarded_to_meta_and_file_effects()
    {
        await using var fixture = await AnnotateFixture.CreateAsync(withDifferentLatestStore: true);
        await fixture.StartHostAsync();

        var cold = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--store", fixture.EffectStoreId, "--cold");
        var resident = await fixture.RunAsync(
            "annotate",
            fixture.FilePath,
            "--format",
            "tsv",
            "--store",
            fixture.EffectStoreId,
            "--host",
            fixture.HostUrl
        );

        resident.Exit.ShouldBe(0, resident.Err);
        resident.Out.ShouldBe(cold.Out);
        resident.Err.ShouldContain("transport: rig serve");
        resident.Err.ShouldNotContain("falling back to cold");
    }

    [Test]
    public async Task Mismatched_host_is_refused_then_cold_output_is_used()
    {
        await using var fixture = await AnnotateFixture.CreateAsync();
        await using var other = await AnnotateFixture.CreateAsync();
        await other.StartHostAsync();

        var cold = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--cold");
        var result = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--host", other.HostUrl);

        result.Exit.ShouldBe(0, result.Err);
        result.Out.ShouldBe(cold.Out);
        result.Err.ShouldContain("different working directory");
        result.Err.ShouldContain("transport: cold");
    }

    [Test]
    public async Task Legacy_meta_payload_is_malformed_for_annotate_and_falls_back_cold()
    {
        await using var fixture = await AnnotateFixture.CreateAsync();
        await fixture.StartLegacyHostAsync();

        var cold = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--cold");
        var result = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--host", fixture.HostUrl);

        result.Exit.ShouldBe(0, result.Err);
        result.Out.ShouldBe(cold.Out);
        result.Err.ShouldContain("resident annotate unavailable");
        result.Err.ShouldContain("transport: cold");
    }

    [Test]
    public async Task Unreachable_and_stale_markers_fall_back_and_stale_marker_is_removed()
    {
        await using var fixture = await AnnotateFixture.CreateAsync();
        var cold = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--cold");

        var unreachable = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--host", "http://localhost:1");
        unreachable.Out.ShouldBe(cold.Out);
        unreachable.Err.ShouldContain("falling back to cold");

        var markerPath = Path.Combine(StoreLayout.RigDir(fixture.Root), AnnotateResidentTransport.MarkerFileName);
        var marker = new ServeMarker(
            Port: 1,
            Url: "http://localhost:1",
            Pid: int.MaxValue,
            WorkingDirectory: AnnotateResidentTransport.CanonicalPath(fixture.Root),
            StartedUtc: DateTimeOffset.UtcNow
        );
        File.WriteAllText(markerPath, JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var stale = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv");

        stale.Out.ShouldBe(cold.Out);
        stale.Err.ShouldContain("no longer running");
        File.Exists(markerPath).ShouldBeFalse();
    }

    [Test]
    public async Task Cold_never_contacts_host_and_effectless_method_diagnosis_survives_resident_DTO()
    {
        await using var fixture = await AnnotateFixture.CreateAsync();
        var forcedCold = await fixture.RunAsync("annotate", fixture.FilePath, "--format", "tsv", "--host", "http://localhost:1", "--cold");

        forcedCold.Exit.ShouldBe(0, forcedCold.Err);
        forcedCold.Err.ShouldContain("transport: cold (--cold)");
        forcedCold.Err.ShouldNotContain("resident annotate unavailable");

        await fixture.StartHostAsync();
        var coldQuiet = await fixture.RunAsync("annotate", fixture.FilePath, "--method", "Quiet", "--format", "tsv", "--cold");
        var residentQuiet = await fixture.RunAsync(
            "annotate",
            fixture.FilePath,
            "--method",
            "Quiet",
            "--format",
            "tsv",
            "--host",
            fixture.HostUrl
        );

        residentQuiet.Exit.ShouldBe(coldQuiet.Exit);
        residentQuiet.Out.ShouldBe(coldQuiet.Out);
        residentQuiet.Err.ShouldContain("Quiet");
        residentQuiet.Err.ShouldContain("no effects in this store");
        residentQuiet.Err.ShouldContain("transport: rig serve");
    }

    private sealed class AnnotateFixture : IAsyncDisposable
    {
        private const string EffectfulId = "M:Demo.Work.Effectful";
        private const string QuietId = "M:Demo.Work.Quiet";
        private WebApplication? _app;
        private ServeMarkerLease? _marker;

        private AnnotateFixture(string root, string filePath, string effectStoreId)
        {
            Root = root;
            FilePath = filePath;
            EffectStoreId = effectStoreId;
        }

        public string Root { get; }

        public string FilePath { get; }

        public string EmptyFilePath => Path.Combine(Root, "NoMethods.cs");

        public string EffectStoreId { get; }

        public string HostUrl => _app!.Urls.Single();

        public static async Task<AnnotateFixture> CreateAsync(bool withDifferentLatestStore = false)
        {
            var root = Directory.CreateTempSubdirectory("rig-annotate-resident-").FullName;
            var filePath = Path.Combine(root, "Work.cs");
            File.WriteAllText(
                filePath,
                """
                using System.IO;

                namespace Demo;

                public sealed class Work
                {
                    public void Effectful()
                    {
                        File.WriteAllText("out.txt", "value");
                    }

                    public void Quiet()
                    {
                        var answer = 42;
                    }
                }
                """
            );
            File.WriteAllText(path: Path.Combine(root, "NoMethods.cs"), contents: "namespace Demo; public sealed class NoMethods {}\n");
            Git(root, "init", "-q");
            Git(root, "add", ".");
            Git(root, "-c", "user.email=rig@test", "-c", "user.name=rig", "commit", "-q", "-m", "fixture");
            var commit = Git(root, "rev-parse", "HEAD");
            var effectStoreId = commit[..12];
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(root, "Demo.slnx"),
                SourceFiles:
                [
                    new SourceFileInfo("Demo", filePath, "indexed", "high", "project", "", ""),
                    new SourceFileInfo("Demo", Path.Combine(root, "NoMethods.cs"), "indexed", "high", "project", "", ""),
                ],
                DiRegistrations: [],
                Symbols:
                [
                    Symbol(EffectfulId, "Effectful", filePath, line: 7, endLine: 10),
                    Symbol(QuietId, "Quiet", filePath, line: 12, endLine: 15),
                ],
                References:
                [
                    new ReferenceFact(
                        TargetSymbolId: "M:System.IO.File.WriteAllText(System.String,System.String)",
                        RefKind: RefKinds.Invocation,
                        EnclosingSymbolId: EffectfulId,
                        TargetAssembly: "System.Private.CoreLib",
                        TargetInSource: false,
                        FilePath: filePath,
                        Line: 9
                    ),
                ],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );
            await WriteStoreAsync(root, effectStoreId, result, commit);
            StoreLayout.WriteLatestPointer(root, effectStoreId);

            if (withDifferentLatestStore)
            {
                var otherFile = Path.Combine(root, "Other.cs");
                File.WriteAllText(otherFile, "namespace Demo; public sealed class Other {}\n");
                var other = new AnalysisResult(
                    SolutionPath: Path.Combine(root, "Other.slnx"),
                    SourceFiles: [new SourceFileInfo("Other", otherFile, "indexed", "high", "project", "", "")],
                    DiRegistrations: []
                );
                const string otherStoreId = "other-store";
                await WriteStoreAsync(root, otherStoreId, other, commit);
                StoreLayout.WriteLatestPointer(root, otherStoreId);
            }

            return new AnnotateFixture(root, filePath, effectStoreId);
        }

        public async Task StartHostAsync()
        {
            for (var attempt = 1; ; attempt++)
            {
                _app = RigWebHost.Build(Root, FreePort());
                try
                {
                    await _app.StartAsync();
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    await _app.DisposeAsync();
                }
            }
        }

        public async Task StartLegacyHostAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://localhost:{FreePort()}");
            _app = builder.Build();
            _app.MapGet("/api/meta", () => Results.Json(new { derivationVersion = "legacy" }));
            await _app.StartAsync();
        }

        public void PublishMarker()
        {
            var uri = new Uri(HostUrl);
            _marker = ServeMarkerLease.Publish(Root, uri.Port, HostUrl);
        }

        public async Task<(int Exit, string Out, string Err)> RunAsync(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var exit = await CliApplication.RunAsync(args, output, error, Root);
            return (exit, output.ToString(), error.ToString());
        }

        public async ValueTask DisposeAsync()
        {
            _marker?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }

            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static async Task WriteStoreAsync(string root, string storeId, AnalysisResult result, string commit)
        {
            var storeDirectory = StoreLayout.NewStoreDir(root, storeId);
            await using var context = new RigDbContext(Path.Combine(storeDirectory, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(context, result, provenance: new GitProvenance(commit, "main", Dirty: false));
        }

        private static SymbolFact Symbol(string id, string name, string filePath, int line, int endLine) =>
            new(
                SymbolId: id,
                Kind: SymbolKinds.Method,
                Name: name,
                Namespace: "Demo",
                ContainingSymbolId: "T:Demo.Work",
                Modifiers: "public",
                TypeKind: "",
                Signature: $"void {name}()",
                FilePath: filePath,
                Line: line,
                EndLine: endLine,
                DefiningAssembly: "Demo",
                IsOverride: false
            );

        private static string Git(string workingDirectory, params string[] args)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in args)
            {
                psi.ArgumentList.Add(argument);
            }

            using var process = Process.Start(psi).ShouldNotBeNull();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {stdout}{stderr}");
            return stdout.Trim();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed record CacheSentinel(int LoadNumber, string FactoryRequest);
}
