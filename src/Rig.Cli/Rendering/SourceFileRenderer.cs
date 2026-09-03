using Rig.Domain.Data;

namespace Rig.Cli.Rendering;

internal static class SourceFileRenderer
{
    public static void RenderSkipped(IReadOnlyList<SourceFileInfo> sourceFiles, TextWriter output)
    {
        output.WriteLine("Skipped Files");
        foreach (var sourceFile in sourceFiles)
        {
            output.WriteLine($"  {Path.GetFileName(sourceFile.FilePath)}");
            output.WriteLine(
                $"    project={sourceFile.ProjectName} conf={sourceFile.Confidence} basis={sourceFile.Basis} reason={sourceFile.Reason}"
            );
            output.WriteLine($"    path={sourceFile.FilePath}");
        }
    }

    public static void RenderCompileErrors(IReadOnlyList<SourceFileInfo> sourceFiles, TextWriter output, bool tsv)
    {
        if (tsv)
        {
            output.WriteLine("project\tfile\terror_count\tcodes\tfirst_message");
            foreach (var sourceFile in sourceFiles)
            {
                output.WriteLine(
                    $"{TsvCell.Clean(sourceFile.ProjectName)}\t{TsvCell.Clean(sourceFile.FilePath)}\t{sourceFile.CompileErrorCount}"
                        + $"\t{TsvCell.Clean(sourceFile.CompileErrorCodes)}\t{TsvCell.Clean(sourceFile.CompileErrorFirst)}"
                );
            }

            return;
        }

        output.WriteLine("Files With Compile Errors");
        foreach (var sourceFile in sourceFiles)
        {
            output.WriteLine($"  {Path.GetFileName(sourceFile.FilePath)}  ~compile-error");
            output.WriteLine(
                $"    project={sourceFile.ProjectName} errors={sourceFile.CompileErrorCount} codes={sourceFile.CompileErrorCodes}"
            );
            output.WriteLine($"    first={sourceFile.CompileErrorFirst}");
            output.WriteLine($"    path={sourceFile.FilePath}");
        }
    }
}
