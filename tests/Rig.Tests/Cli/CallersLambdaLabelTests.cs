using Rig.Cli.Commands;
using Shouldly;

namespace Rig.Tests.Cli;

public sealed class CallersLambdaLabelTests
{
    [Test]
    public void Method_and_contained_lambda_render_as_distinct_human_node_labels()
    {
        CallersCommand.HumanNodeLabel("M:App.Type.Method(System.String)").ShouldBe("Type.Method");
        CallersCommand.HumanNodeLabel("M:App.Type.Method(System.String)~λ0").ShouldBe("Type.Method~λ0");
        CallersCommand.HumanNodeLabel("M:App.Type.Method~λ0").ShouldBe("Type.Method~λ0");
    }

    [Test]
    public void Multiple_lambda_caller_depth_rows_stay_distinguishable()
    {
        var rows = new[]
        {
            $"d4  {CallersCommand.HumanNodeLabel("M:App.WatchCommand.Build(System.String)~λ0")}",
            $"d5  {CallersCommand.HumanNodeLabel("M:App.WatchCommand.Build(System.String)~λ1")}",
        };

        rows.ShouldBe(["d4  WatchCommand.Build~λ0", "d5  WatchCommand.Build~λ1"]);
        rows.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
    }
}
