using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Rig.Cli.CommandLine;
using Rig.Cli.Web;
using Rig.Domain.Data;
using Rig.Storage.Queries;
using Rig.Storage.Storage;
using Shouldly;

namespace Rig.Tests.Cli;

// `/api/source` — the web slice of `rig show`. These drive the REAL in-process host (RigWebHost.Build, the
// same one `rig serve` runs) over HTTP against a real materialized store whose one symbol points into a real
// temp git repo, so the three provenance paths (working tree / git blob / refusal) and the error convention
// are exercised end-to-end rather than mocked.
//
// The load-bearing properties pinned here:
//   1. The endpoint is keyed by SYMBOL ID ONLY — it never accepts a file path. `rig serve` is an HTTP server;
//      an endpoint that renders any path handed to it is an arbitrary-file-read primitive.
//   2. Provenance survives the JSON hop: a git-blob read carries its commit, a refusal carries its reason.
//   3. A bad/unknown id is a 400 with the message, not a 500.
//
// Assertions are written against the ACTUAL wire output of a run against the real MedDBase store, e.g.
//   {"symbolId":"M:…SetSiteSettings(…)","file":"C:\\Git\\…\\Master_HealthcodeServiceImpl.cs","line":1604,
//    "endLine":1609,"origin":"worktree","commit":null,"truncatedCount":0,"reason":null,
//    "lines":[{"number":1604,"text":"    public void SetSiteSettings(…"}, …],"storeDirty":false}
// — hence the camelCase property names and the "worktree"/"git"/"unavailable" origin words below.
public sealed class SourceEndpointTests
{
    private const string Declaration = """
        namespace Demo;

        public sealed class Widget
        {
            public int Answer()
            {
                return 42;
            }
        }
        """;

    // The DocID the fixture store indexes for Widget.Answer — the endpoint's ONLY input.
    private const string AnswerId = "M:Demo.Widget.Answer";

    // Path 1 — clean store, its commit IS head, file unmodified: the working tree provably IS the indexed
    // revision, so it is read directly and reports no commit (nothing to disclose).
    [Test]
    public async Task An_indexed_symbol_returns_its_declaration_from_the_working_tree()
    {
        using var fixture = await Fixture.CreateAsync();

        using var doc = await fixture.GetJsonAsync($"/api/source?id={Uri.EscapeDataString(AnswerId)}");
        var root = doc.RootElement;

        root.GetProperty("symbolId").GetString().ShouldBe(AnswerId);
        root.GetProperty("file").GetString().ShouldBe(fixture.FilePath);
        root.GetProperty("line").GetInt32().ShouldBe(5);
        root.GetProperty("endLine").GetInt32().ShouldBe(8);
        root.GetProperty("origin").GetString().ShouldBe("worktree");
        root.GetProperty("commit").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("truncatedCount").GetInt32().ShouldBe(0);
        root.GetProperty("storeDirty").GetBoolean().ShouldBeFalse();

        // The gutter arrives as DATA — a file line number per line, not pre-padded text — so the client can
        // right-align it. Same slice `rig show` renders for lines 5-8.
        var lines = root.GetProperty("lines").EnumerateArray().ToList();
        lines.Select(l => l.GetProperty("number").GetInt32()).ShouldBe([5, 6, 7, 8]);
        lines.Select(l => l.GetProperty("text").GetString()).ShouldBe(["    public int Answer()", "    {", "        return 42;", "    }"]);
    }

    // Path 2 — the correctness case the whole feature exists for. HEAD still equals the store's commit, but
    // the file has an uncommitted edit that shifts every line: serving disk would put `// a local edit` under
    // line 5. The endpoint must serve the indexed blob AND disclose the commit, so the UI can say
    // "(from git <shortsha>)" — the reader is not looking at their working tree.
    [Test]
    public async Task A_locally_edited_file_is_served_from_the_indexed_commit_and_discloses_it()
    {
        using var fixture = await Fixture.CreateAsync();
        File.WriteAllText(fixture.FilePath, "// a local edit that shifts every line\n\n" + Declaration);

        using var doc = await fixture.GetJsonAsync($"/api/source?id={Uri.EscapeDataString(AnswerId)}");
        var root = doc.RootElement;

        root.GetProperty("origin").GetString().ShouldBe("git");
        // The SHORT (12-char) sha, matching what `rig runs` shows and what the CLI's marker renders.
        root.GetProperty("commit").GetString().ShouldBe(fixture.Head[..12]);
        root.GetProperty("lines")
            .EnumerateArray()
            .Select(l => l.GetProperty("text").GetString())
            .ShouldBe(["    public int Answer()", "    {", "        return 42;", "    }"]);
    }

    // Path 3 — refuse. The store's commit is not in this work tree, so nothing attributes the stored lines to
    // a revision: no text, but the location and a one-line reason still come back (the UI renders the reason
    // instead of code — never a silently empty panel).
    [Test]
    public async Task An_unattributable_location_returns_the_reason_instead_of_code()
    {
        using var fixture = await Fixture.CreateAsync(storeCommit: new string('a', 40));

        using var doc = await fixture.GetJsonAsync($"/api/source?id={Uri.EscapeDataString(AnswerId)}");
        var root = doc.RootElement;

        root.GetProperty("origin").GetString().ShouldBe("unavailable");
        root.GetProperty("lines").GetArrayLength().ShouldBe(0);
        root.GetProperty("reason").GetString().ShouldNotBeNull().ShouldContain("git could not read");
        // The location is never lost — the client still shows file:line above the refusal.
        root.GetProperty("file").GetString().ShouldBe(fixture.FilePath);
        root.GetProperty("line").GetInt32().ShouldBe(5);
    }

    // `?context=N` pads the declaration range either side, mirroring `rig show --context`.
    [Test]
    public async Task Context_widens_the_returned_range()
    {
        using var fixture = await Fixture.CreateAsync();

        using var doc = await fixture.GetJsonAsync($"/api/source?context=2&id={Uri.EscapeDataString(AnswerId)}");

        doc.RootElement.GetProperty("lines")
            .EnumerateArray()
            .Select(l => l.GetProperty("number").GetInt32())
            .ShouldBe([3, 4, 5, 6, 7, 8, 9]);
    }

    // An unknown id is USER error, not a server fault — 400 with the message, matching the other endpoints'
    // convention (verified against the real store: {"title":"Unknown symbol","status":400,
    // "detail":"No indexed symbol with id 'M:Nope.Nope.Nope' in this store."}).
    [Test]
    public async Task An_unknown_id_is_a_400_carrying_the_message()
    {
        using var fixture = await Fixture.CreateAsync();

        var (status, body) = await fixture.GetAsync("/api/source?id=M:Nope.Nope.Nope");

        status.ShouldBe(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().ShouldBe("Unknown symbol");
        doc.RootElement.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("No indexed symbol with id 'M:Nope.Nope.Nope'");
    }

    // THE SECURITY PROPERTY: the endpoint takes a symbol id and nothing else. A request carrying only a file
    // path — the shape an arbitrary-file-read attempt takes — is rejected outright; the path is not even
    // looked at. The only paths this endpoint can ever render are paths already IN the store.
    [Test]
    public async Task A_request_with_a_file_path_but_no_id_is_rejected_without_reading_anything()
    {
        using var fixture = await Fixture.CreateAsync();

        var (status, body) = await fixture.GetAsync("/api/source?file=" + Uri.EscapeDataString(@"C:\Windows\System32\drivers\etc\hosts"));

        status.ShouldBe(HttpStatusCode.BadRequest);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().ShouldBe("Missing 'id'");
        body.ShouldNotContain("hosts"); // the path never reaches the renderer, so it can't come back as text
    }

    // A file on disk that is NOT in the store is unreachable through this endpoint even when its exact path is
    // handed over as the id — id lookup is an equality match on indexed symbol ids, not a path.
    [Test]
    public async Task A_file_path_supplied_as_the_id_resolves_to_nothing()
    {
        using var fixture = await Fixture.CreateAsync();

        var (status, _) = await fixture.GetAsync("/api/source?id=" + Uri.EscapeDataString(fixture.FilePath));

        status.ShouldBe(HttpStatusCode.BadRequest);
    }

    // The wire names the SPA reads (api.js -> components.js SourceBody). Pinned as raw text because a
    // serializer-policy change would silently break every client field read while every typed test still passed.
    [Test]
    public async Task The_response_uses_the_camelCase_names_the_client_reads()
    {
        using var fixture = await Fixture.CreateAsync();

        var (status, body) = await fixture.GetAsync($"/api/source?id={Uri.EscapeDataString(AnswerId)}");

        status.ShouldBe(HttpStatusCode.OK);
        foreach (
            var name in (string[])
                ["symbolId", "file", "line", "endLine", "origin", "commit", "truncatedCount", "reason", "lines", "storeDirty"]
        )
        {
            body.ShouldContain($"\"{name}\":");
        }

        body.ShouldContain("\"number\":");
        body.ShouldContain("\"text\":");
    }

    // A temp git work tree + a materialized one-symbol store pointing into it + the real web host, bound to an
    // ephemeral port. Everything the endpoint touches is real: git, the store, Kestrel.
    private sealed class Fixture : IDisposable
    {
        private readonly string _repo = Directory.CreateTempSubdirectory("rig-srcep-repo-").FullName;
        private readonly string _workingDirectory = Directory.CreateTempSubdirectory("rig-srcep-wd-").FullName;
        private WebApplication _app = null!;
        private HttpClient _client = null!;

        private Fixture() { }

        public string FilePath => Path.Combine(_repo, "Widget.cs");

        public string Head { get; private set; } = "";

        // `storeCommit` overrides the provenance stamped on the run — pass a sha that isn't in the repo to
        // exercise the refusal path.
        public static async Task<Fixture> CreateAsync(string? storeCommit = null)
        {
            var fixture = new Fixture();
            File.WriteAllText(fixture.FilePath, Declaration);
            Git(fixture._repo, "init", "-q");
            Git(fixture._repo, "add", "-A");
            Git(fixture._repo, "-c", "user.email=rig@test", "-c", "user.name=rig", "commit", "-q", "-m", "initial");
            fixture.Head = Git(fixture._repo, "rev-parse", "HEAD");

            var commit = storeCommit ?? fixture.Head;
            await MaterializeStoreAsync(fixture._workingDirectory, fixture.FilePath, commit);

            // RigWebHost binds `http://localhost:<port>`, and Kestrel refuses dynamic (port 0) binding on the
            // `localhost` host name — so pick a free port ourselves. Racy by nature (another process can take
            // it between probe and bind, and these tests run in parallel), hence the retry.
            for (var attempt = 1; ; attempt++)
            {
                fixture._app = RigWebHost.Build(fixture._workingDirectory, FreePort());
                try
                {
                    await fixture._app.StartAsync();
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    await fixture._app.DisposeAsync();
                }
            }

            fixture._client = new HttpClient { BaseAddress = new Uri(fixture._app.Urls.First()) };
            return fixture;
        }

        public async Task<(HttpStatusCode Status, string Body)> GetAsync(string path)
        {
            using var response = await _client.GetAsync(path);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        public async Task<JsonDocument> GetJsonAsync(string path)
        {
            var (status, body) = await GetAsync(path);
            status.ShouldBe(HttpStatusCode.OK, body);
            return JsonDocument.Parse(body);
        }

        public void Dispose()
        {
            _client?.Dispose();
            _app?.StopAsync().GetAwaiter().GetResult();
            (_app as IDisposable)?.Dispose();
            TryDelete(_repo);
            TryDelete(_workingDirectory);
        }

        // One symbol whose stored location is the fixture file's Answer() declaration (lines 5-8), stamped
        // with `commit` — the minimum a source lookup needs, and nothing else.
        private static async Task MaterializeStoreAsync(string workingDirectory, string filePath, string commit)
        {
            var symbol = new SymbolFact(
                SymbolId: AnswerId,
                Kind: "method",
                Name: "Answer",
                Namespace: "Demo",
                ContainingSymbolId: "T:Demo.Widget",
                Modifiers: "public",
                TypeKind: "",
                Signature: "int Answer()",
                FilePath: filePath,
                Line: 5,
                EndLine: 8,
                DefiningAssembly: "Demo",
                IsOverride: false
            );
            var result = new AnalysisResult(
                SolutionPath: Path.Combine(workingDirectory, "Demo.sln"),
                SourceFiles: [],
                DiRegistrations: [],
                Symbols: [symbol],
                References: [],
                TypeRelations: [],
                DispatchFacts: [],
                AllocationFacts: []
            );

            var dir = StoreLayout.NewStoreDir(workingDirectory, commit[..12]);
            await using var context = new RigDbContext(Path.Combine(dir, StoreLayout.DbFileName), pooling: false);
            await Writes.SaveAsync(context, result, provenance: new GitProvenance(Commit: commit, Branch: "main", Dirty: false));
        }

        // A port the OS says is free right now (bind :0 on loopback, read it back, release).
        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, port: 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

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
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = Process.Start(psi).ShouldNotBeNull();
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            proc.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)}: {stdout}{stderr}");
            return stdout.Trim();
        }
    }
}
