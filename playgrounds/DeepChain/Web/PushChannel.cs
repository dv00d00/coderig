using Business;

namespace Web;

// Override chain across projects (fact-merge scale gate): ChannelBase.Notify is virtual in Business;
// the override here means the (override) dispatch edge is emitted at THIS file while its SOURCE
// endpoint is declared in Business/ChannelBase.cs. ChannelBase reaches Web only transitively
// (Web -> ApiGateway -> Business), the same transitive-reference shape HomePage already exercises.
public sealed class PushChannel : ChannelBase
{
    public override string Notify(string message) => $"push: {message}";
}
