using Rig.Cli.CommandLine;
using static Rig.Cli.Graph.TraversalGraphLoader;

namespace Rig.Cli.Web;

internal static class WebCompilationHealth
{
    internal static async Task<CompilationHealthNotice.StoreSnapshot> LoadAsync(string workingDirectory, string? store)
    {
        await using var context = await OpenReadContextGatedAsync(new WorkspaceLocation(workingDirectory, store));
        return await CompilationHealthNotice.LoadStoreAsync(context);
    }

    internal static CompileErrorsDto ToDto(CompilationHealthNotice.StoreSnapshot snapshot) =>
        new(
            Files: snapshot.Health.Files.Count,
            Total: snapshot.Health.TotalErrorCount,
            Projects: snapshot
                .Health.PartialProjects.Select(project => new CompileProjectDto(project.ProjectName, project.Reason))
                .ToArray()
        );

    internal static string BindingHealth(CompilationHealthNotice.StoreSnapshot snapshot, string? file) =>
        snapshot.HasCompileError(file) ? "compile_error" : "ok";
}
