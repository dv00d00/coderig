using System.Diagnostics;
using Rig.Cli;
using Rig.Cli.Rendering;
using Shouldly;

namespace Rig.Tests.Cli;

// `rig show` renders a symbol's declaration SOURCE. The load-bearing property is not the pretty gutter — it
// is that the rendered text is attributable to the store's OWN commit. A rig store is commit-scoped but its
// symbol_facts carry absolute paths + frozen line numbers, so reading the working tree after the tree has
// moved (or after a local edit) renders the WRONG lines under the right line numbers. These tests pin the
// three resolution paths (working tree / git blob / refuse) against REAL temp git repositories.
public sealed class ShowCommandTests
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

    // Path 1 — clean store, its commit IS head, and the file is unmodified: the working tree is byte-for-byte
    // the indexed revision, so it is read directly and carries no provenance marker.
    [Test]
    public void A_clean_store_at_head_reads_the_working_tree()
    {
        using var repo = GitFixture.Create(Declaration);
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 8);

        snippet.Origin.ShouldBe(SourceOrigin.WorkingTree);
        snippet.Lines.Select(l => l.Text).ShouldBe(["    public int Answer()", "    {", "        return 42;", "    }"]);
        renderer.OriginMarker(snippet).ShouldBeEmpty();
    }

    // Path 2 — the correctness case this feature exists for. HEAD still equals the store's commit, but the
    // file has an uncommitted edit that shifts every line: reading disk would render `Console.WriteLine`
    // under line 5. The resolver must go to the indexed blob instead, and say so.
    [Test]
    public void A_locally_edited_file_is_read_from_the_indexed_commit_not_from_disk()
    {
        using var repo = GitFixture.Create(Declaration);
        File.WriteAllText(repo.FilePath, "// a local edit that shifts every line\n\n" + Declaration);
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 8);

        snippet.Origin.ShouldBe(SourceOrigin.GitBlob);
        snippet.Lines.Select(l => l.Text).ShouldBe(["    public int Answer()", "    {", "        return 42;", "    }"]);
        renderer.OriginMarker(snippet).ShouldStartWith(" (from git ");
    }

    // Path 2 — the working tree has moved on to a later commit; the store still describes the older one.
    [Test]
    public void A_store_behind_head_reads_its_own_revision()
    {
        using var repo = GitFixture.Create(Declaration);
        var indexed = repo.Head;
        repo.CommitAll("// a later commit\n\n" + Declaration, "move head on");

        var renderer = new SourceRenderer(storeCommit: indexed, storeDirty: false);
        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 5);

        repo.Head.ShouldNotBe(indexed);
        snippet.Origin.ShouldBe(SourceOrigin.GitBlob);
        snippet.Lines.ShouldHaveSingleItem().Text.ShouldBe("    public int Answer()");
        renderer.OriginMarker(snippet).ShouldContain(indexed[..12]);
    }

    // A store indexed from a DIRTY tree matches no commit exactly, so the working tree is never trusted even
    // at the same HEAD — and the marker discloses that even the blob may differ from what was indexed.
    [Test]
    public void A_dirty_store_never_reads_the_working_tree()
    {
        using var repo = GitFixture.Create(Declaration);
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: true);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 5);

        snippet.Origin.ShouldBe(SourceOrigin.GitBlob);
        renderer.OriginMarker(snippet).ShouldContain("DIRTY");
    }

    // Path 3 — refuse. The file is right there on disk, but nothing ties it to the store's revision.
    [Test]
    public void A_file_outside_a_git_work_tree_is_refused()
    {
        using var scratch = new ScratchDirectory();
        var file = Path.Combine(scratch.Path, "Widget.cs");
        File.WriteAllText(file, Declaration);
        var renderer = new SourceRenderer(storeCommit: new string('a', 40), storeDirty: false);

        var snippet = renderer.Resolve(file, startLine: 5, endLine: 8);

        snippet.Origin.ShouldBe(SourceOrigin.Unavailable);
        snippet.HasText.ShouldBeFalse();
        snippet.Reason.ShouldNotBeNull().ShouldContain("not inside a git work tree");
    }

    // Path 3 — a store with no provenance (pre-stamping index, or a non-git source) can attribute nothing.
    [Test]
    public void A_store_with_no_commit_is_refused()
    {
        using var repo = GitFixture.Create(Declaration);
        var renderer = new SourceRenderer(storeCommit: null, storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 8);

        snippet.Origin.ShouldBe(SourceOrigin.Unavailable);
        snippet.Reason.ShouldNotBeNull().ShouldContain("no source commit");
    }

    // Path 3 — the commit is not in this work tree (a store copied from another machine / a pruned branch).
    [Test]
    public void An_unknown_commit_is_refused()
    {
        using var repo = GitFixture.Create(Declaration);
        var renderer = new SourceRenderer(storeCommit: new string('a', 40), storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 8);

        snippet.Origin.ShouldBe(SourceOrigin.Unavailable);
        snippet.Reason.ShouldNotBeNull().ShouldContain("git could not read");
    }

    // Store and source disagreeing about the file's LENGTH is itself evidence the lines cannot be trusted;
    // clamping silently would render an arbitrary tail as if it were the declaration.
    [Test]
    public void A_line_past_the_end_of_the_revision_is_refused()
    {
        using var repo = GitFixture.Create(Declaration);
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 900, endLine: 950);

        snippet.Origin.ShouldBe(SourceOrigin.Unavailable);
        snippet.Reason.ShouldNotBeNull().ShouldContain("past the end of the file");
    }

    [Test]
    public void Context_lines_widen_the_range_symmetrically()
    {
        using var repo = GitFixture.Create(Declaration);
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 5, endLine: 8, context: 2);

        snippet.Lines[0].Number.ShouldBe(3);
        snippet.Lines[^1].Number.ShouldBe(9);
    }

    // The absurd-output guard: a 1,600-line class must not be dumped whole.
    [Test]
    public void An_oversized_declaration_is_truncated_with_an_explicit_marker()
    {
        using var repo = GitFixture.Create(string.Join('\n', Enumerable.Range(1, 100).Select(i => $"// line {i}")));
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: false);

        var snippet = renderer.Resolve(repo.FilePath, startLine: 1, endLine: 100, maxLines: 10);

        snippet.Lines.Count.ShouldBe(10);
        snippet.TruncatedCount.ShouldBe(90);

        var output = new StringWriter();
        SourceRenderer.Render(output, snippet, "  ");
        output.ToString().ShouldContain("… truncated 90 lines");
    }

    // The rendered shape: a right-aligned line-number gutter so the numbers line up and are quotable.
    [Test]
    public void Render_writes_a_right_aligned_line_number_gutter()
    {
        using var repo = GitFixture.Create(string.Join('\n', Enumerable.Range(1, 12).Select(i => $"// line {i}")));
        var renderer = new SourceRenderer(storeCommit: repo.Head, storeDirty: false);
        var output = new StringWriter();

        SourceRenderer.Render(output, renderer.Resolve(repo.FilePath, startLine: 9, endLine: 10), "  ");

        output.ToString().ShouldBe("   9 | // line 9" + Environment.NewLine + "  10 | // line 10" + Environment.NewLine);
    }

    // A refusal still renders — the caller has printed file:line, and this states, in one line, why no source
    // followed. Never a silent omission and never unattributed text.
    [Test]
    public void Render_prints_a_one_line_reason_when_source_is_unavailable()
    {
        var output = new StringWriter();

        SourceRenderer.Render(output, SourceSnippet.Unavailable("no source commit"), "  ");

        output.ToString().ShouldBe("  (source unavailable: no source commit)" + Environment.NewLine);
    }

    // The command is wired into the CLI surface with its documented flags.
    [Test]
    public async Task Show_is_registered_with_its_flags()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(["show", "--help"], output, error);

        exitCode.ShouldBe(0);
        var help = output.ToString();
        help.ShouldContain("Print the source of a matched symbol's declaration.");
        help.ShouldContain("--context");
        help.ShouldContain("--limit");
        help.ShouldContain("--store");
    }

    [Test]
    public async Task Show_is_listed_in_the_root_help()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        await CliApplication.RunAsync([], output, error);

        output.ToString().ShouldContain("show");
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public ScratchDirectory() => Path = Directory.CreateTempSubdirectory("rig-show-").FullName;

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // A throwaway git work tree with one committed file — the only way to exercise the resolution order for
    // real (the whole point is what git says, not what a mock says).
    private sealed class GitFixture : IDisposable
    {
        private readonly ScratchDirectory _scratch = new();

        private GitFixture() { }

        public string FilePath => System.IO.Path.Combine(_scratch.Path, "Widget.cs");

        public string Head => Git(_scratch.Path, "rev-parse", "HEAD");

        public static GitFixture Create(string content)
        {
            var fixture = new GitFixture();
            Git(fixture._scratch.Path, "init", "-q");
            fixture.CommitAll(content, "initial");
            return fixture;
        }

        public void CommitAll(string content, string message)
        {
            File.WriteAllText(FilePath, content);
            Git(_scratch.Path, "add", "-A");
            Git(_scratch.Path, "-c", "user.email=rig@test", "-c", "user.name=rig", "commit", "-q", "-m", message);
        }

        public void Dispose() => _scratch.Dispose();

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
