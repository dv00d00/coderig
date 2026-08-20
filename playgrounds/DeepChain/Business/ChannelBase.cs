namespace Business;

// Dispatch scaffolding for the fact-merge scale gate (ResidentIndexScaleTests). This FILE is the one
// the scale test edits: every shape below produces a dispatch edge that is EMITTED in another file
// (or another project) while one of its endpoints is declared HERE — the exact class of fact that a
// per-file overlay cannot re-emit from this file's own re-extraction, so a merge that drops base
// rows by symbol silently loses them.

// ChannelBase does NOT implement Domain.INotifier itself, but Notify signature-matches
// INotifier.Notify — so ApiGateway.EmailChannel (another project) satisfies the interface with this
// INHERITED method. That impl edge (INotifier.Notify -> ChannelBase.Notify) is emitted at
// EmailChannel's declaration, with BOTH endpoints declared outside EmailChannel's file. The virtual
// Notify is also overridden by Web.PushChannel (override edge emitted in Web) and bound as a method
// group by ApiGateway.NotificationRelay (delegate_bind edge emitted in ApiGateway).
public abstract class ChannelBase
{
    public virtual string Notify(string message) => $"channel: {message}";
}

// Direct, same-file implementer — pairs with ApiGateway.EmailChannel to give INotifier
// implementations in two DIFFERENT projects.
public sealed class SmsChannel : Domain.INotifier
{
    public string Notify(string message) => $"sms: {message}";
}
