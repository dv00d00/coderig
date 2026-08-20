namespace Domain;

// Cross-project dispatch scaffolding for the fact-merge scale gate (ResidentIndexScaleTests).
// INotifier is implemented by TWO classes in DIFFERENT projects: Business.SmsChannel (directly) and
// ApiGateway.EmailChannel (via the INHERITED Business.ChannelBase.Notify) — so its impl dispatch
// edges span three projects, which is exactly the sparseness the 7-project/103-line original
// playground lacked.
public interface INotifier
{
    string Notify(string message);
}
