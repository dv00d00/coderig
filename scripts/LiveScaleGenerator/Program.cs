using LiveScaleGenerator;

try
{
    var options = CommandLine.Parse(args);
    var summary = CorpusGenerator.Generate(options);
    Console.WriteLine($"Generated LiveScale preset: {summary.Preset}");
    Console.WriteLine($"Projects: {summary.ProjectCount}");
    Console.WriteLine($"C# files: {summary.CSharpFileCount}");
    Console.WriteLine($"Corpus SHA-256: {summary.CorpusSha256}");
    Console.WriteLine($"Edit trace SHA-256: {summary.EditTraceSha256}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("Usage: LiveScaleGenerator --preset smoke|scale|stress --output <dir> --seed <ulong> [--include-generated]");
    return 1;
}

internal static class CommandLine
{
    public static GenerationOptions Parse(string[] args)
    {
        string? preset = null;
        string? output = null;
        ulong? seed = null;
        var includeGenerated = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--preset":
                    preset = Value(args, ref i, "--preset");
                    break;
                case "--output":
                    output = Value(args, ref i, "--output");
                    break;
                case "--seed":
                    var raw = Value(args, ref i, "--seed");
                    if (
                        !ulong.TryParse(
                            raw,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var parsed
                        )
                    )
                    {
                        throw new ArgumentException($"Invalid unsigned 64-bit seed: {raw}");
                    }
                    seed = parsed;
                    break;
                case "--include-generated":
                    includeGenerated = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[i]}");
            }
        }

        if (preset is not ("smoke" or "scale" or "stress"))
        {
            throw new ArgumentException("--preset must be smoke, scale, or stress.");
        }
        if (string.IsNullOrWhiteSpace(output))
        {
            throw new ArgumentException("--output is required.");
        }
        if (seed is null)
        {
            throw new ArgumentException("--seed is required.");
        }

        return new GenerationOptions(preset, Path.GetFullPath(output), seed.Value, includeGenerated);
    }

    private static string Value(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }
        return args[index];
    }
}
