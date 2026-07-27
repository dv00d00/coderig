using System.CommandLine;

namespace Rig.Cli.CommandLine;

// A registered-but-disabled command: it appears in `--help` marked DISABLED, and invoking it exits non-zero
// with the REASON and a WORKAROUND on stderr.
//
// Why register a stub at all instead of dropping the command: an unregistered name fails with
// System.CommandLine's "'<name>' was not matched", which reads like a typo or a broken install. A command
// that USED to exist is still referenced by older docs, older skill copies, and muscle memory, so that error
// actively misleads — it says "you got the name wrong" when the truth is "this is switched off, and here is
// what to use instead". Disclosing the reason costs one small type and turns a dead end into a redirect.
//
// This is the same disclosure principle the effect filter follows (EffectDerivation.IntrinsicProviders): a
// suppressed capability must teach its own escape hatch rather than fail opaquely.
internal static class DisabledCommand
{
    internal static Command Build(string name, string reason, string workaround, TextWriter error)
    {
        // The DISABLED marker belongs in the description so it shows in the command list, where someone
        // deciding what to run will actually see it — not only after they have already tried it.
        var cmd = new Command(name: name, description: $"[DISABLED] {reason}");

        // Accept-and-ignore any arguments so an old invocation still reaches THIS message rather than dying
        // first on an unrecognized option it used to support.
        var ignored = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore, Description = "Ignored." };
        cmd.Arguments.Add(ignored);

        cmd.SetAction(_ =>
        {
            error.WriteLine(reason);
            error.WriteLine(workaround);
            return 2;
        });
        return cmd;
    }
}
