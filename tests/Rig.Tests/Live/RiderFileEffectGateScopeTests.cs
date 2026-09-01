using Rig.Cli.Live;
using Rig.Domain.Data;
using Shouldly;

namespace Rig.Tests.Live;

// The file-effects unavailability gate used to be a WHOLE-SOLUTION check: any compile error anywhere, or any
// unreconciled project, answered `stale` with empty rows for EVERY file. Measured on MedDBase that meant 2
// broken files out of 11,976 (in MedDBase.PACS and a payment-gateway data tier) blanked the Rider plugin
// across all 227 projects, permanently — a couple of non-compiling files is the steady state of a monorepo.
// These tests pin the scoped replacement: a file is blocked by ITS OWN errors and ITS OWN projects' failures,
// and by nothing else.
public sealed class RiderFileEffectGateScopeTests
{
    private static readonly string RepoRoot = Path.GetFullPath(OperatingSystem.IsWindows() ? @"C:\repo" : "/repo");
    private static readonly string CleanFile = Path.Combine(RepoRoot, "Pages", "Main.cs");
    private static readonly string BrokenFile = Path.Combine(RepoRoot, "Pacs", "Startup.cs");

    private static IReadOnlySet<string> Projects(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

    [Test]
    public void A_clean_file_is_served_although_another_project_holds_the_only_broken_file()
    {
        var health = new CompilationHealth(
            [new FileCompileHealth(BrokenFile, 1, "CS0117", "'ProcessNames' does not contain a definition for 'Outbound'")],
            PartialProjects: [],
            UnlocatedErrorCount: 0
        );

        RiderFileEffectResponder
            .UnavailableForFile(CleanFile, Projects("MedDBase.Pages"), unreconciledProjectNames: [], health: health)
            .ShouldBeNull();
    }

    [Test]
    public void The_broken_file_itself_is_blocked_with_a_file_scoped_reason_naming_its_diagnostics()
    {
        var health = new CompilationHealth(
            [new FileCompileHealth(BrokenFile, 2, "CS0117,CS0246", "'ProcessNames' does not contain a definition for 'Outbound'")],
            PartialProjects: [],
            UnlocatedErrorCount: 0
        );

        var unavailable = RiderFileEffectResponder.UnavailableForFile(
            BrokenFile,
            Projects("MedDBase.PACS"),
            unreconciledProjectNames: [],
            health: health
        );

        unavailable.ShouldNotBeNull();
        unavailable!.Code.ShouldBe(RiderFileEffectResponder.ReasonFileCompileErrors);
        unavailable.Scope.ShouldBe(RiderFileEffectResponder.ScopeFile);
        unavailable.Text.ShouldContain("2 compile error(s)");
        unavailable.Text.ShouldContain("CS0117,CS0246");
        unavailable.Text.ShouldContain("Outbound");
    }

    [Test]
    public void A_clean_file_is_served_although_an_unrelated_project_is_unreconciled()
    {
        RiderFileEffectResponder
            .UnavailableForFile(
                CleanFile,
                Projects("MedDBase.Pages"),
                unreconciledProjectNames: ["MedDBase.PACS", "MedDBase.DataServer"],
                health: CompilationHealth.Empty
            )
            .ShouldBeNull();
    }

    [Test]
    public void An_unreconciled_project_that_declares_the_file_blocks_it_at_HOST_scope()
    {
        var unavailable = RiderFileEffectResponder.UnavailableForFile(
            CleanFile,
            Projects("MedDBase.Pages"),
            unreconciledProjectNames: ["MedDBase.PACS", "MedDBase.Pages"],
            health: CompilationHealth.Empty
        );

        unavailable.ShouldNotBeNull();
        unavailable!.Code.ShouldBe(RiderFileEffectResponder.ReasonProjectUnreconciled);
        // HOST, not FILE: it clears itself within one reconcile, so it belongs to the status widget rather
        // than a Problems row that appears and vanishes on every save.
        unavailable.Scope.ShouldBe(RiderFileEffectResponder.ScopeHost);
        unavailable.Text.ShouldContain("MedDBase.Pages");
        unavailable.Text.ShouldNotContain("MedDBase.PACS");
    }

    [Test]
    public void A_partial_project_blocks_only_the_files_it_declares()
    {
        var health = new CompilationHealth(
            Files: [],
            [new ProjectCompileFailure("MedDBase.PACS", ProjectCompileFailure.NoCompilation)],
            UnlocatedErrorCount: 0
        );

        RiderFileEffectResponder
            .UnavailableForFile(CleanFile, Projects("MedDBase.Pages"), unreconciledProjectNames: [], health: health)
            .ShouldBeNull();

        var blocked = RiderFileEffectResponder.UnavailableForFile(
            BrokenFile,
            Projects("MedDBase.PACS"),
            unreconciledProjectNames: [],
            health: health
        );

        blocked.ShouldNotBeNull();
        blocked!.Code.ShouldBe(RiderFileEffectResponder.ReasonProjectPartial);
        blocked.Scope.ShouldBe(RiderFileEffectResponder.ScopeFile);
        blocked.Text.ShouldContain(ProjectCompileFailure.NoCompilation);
    }

    // A file is matched by NORMALIZED path, not by string equality: the client sends whatever Rider's PSI
    // reports and the health rows carry whatever Roslyn reported, so `\.\` and casing differences must not
    // make a broken file look clean.
    [Test]
    public void A_broken_file_is_recognised_through_path_normalisation()
    {
        var health = new CompilationHealth(
            [new FileCompileHealth(BrokenFile, 1, "CS0117", "boom")],
            PartialProjects: [],
            UnlocatedErrorCount: 0
        );
        var awkward = Path.Combine(RepoRoot, "Pacs", ".", "Startup.cs");

        RiderFileEffectResponder
            .UnavailableForFile(awkward, Projects("MedDBase.PACS"), unreconciledProjectNames: [], health: health)
            .ShouldNotBeNull();
    }
}
