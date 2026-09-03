namespace Rig.Storage.Storage;

public sealed class SourceFileEntity
{
    public string RunId { get; set; } = "";

    public int FileIndex { get; set; }

    public string ProjectName { get; set; } = "";

    public string FilePath { get; set; } = "";

    public string Status { get; set; } = "";

    public string Confidence { get; set; } = "";

    public string Basis { get; set; } = "";

    public string Reason { get; set; } = "";

    public string Evidence { get; set; } = "";

    public int CompileErrorCount { get; set; }

    public string CompileErrorCodes { get; set; } = "";

    public string CompileErrorFirst { get; set; } = "";

    // This file differed from the source repository's HEAD when the run indexed it, so its facts are NOT
    // at the run's SourceCommit. Recorded per file because dirtiness is a per-file property: the run-level
    // RunEntity.SourceDirty says only that SOME file was uncommitted. Set by Writes.SaveAsync from the
    // git-status set the CLI captures at index start and end.
    public bool Dirty { get; set; }
}
