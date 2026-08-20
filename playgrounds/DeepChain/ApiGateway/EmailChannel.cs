using System;
using Business;
using Domain;

namespace ApiGateway;

// The INHERITED-implementation shape (fact-merge scale gate): EmailChannel declares INotifier but the
// implementation is the inherited Business.ChannelBase.Notify — the impl dispatch edge
// (INotifier.Notify -> ChannelBase.Notify) is emitted HERE, at EmailChannel's declaration, while BOTH
// of its endpoints are declared in OTHER projects' files. Per-file re-extraction of either endpoint's
// file can never re-emit this edge.
public sealed class EmailChannel : ChannelBase, INotifier { }

// Method-group delegate bind whose bound target lives in another project's file: assigning
// channel.Notify (a method group) to the _send delegate field emits a delegate_bind dispatch edge at
// THIS file, with the target endpoint (ChannelBase.Notify) declared in Business.
public sealed class NotificationRelay
{
    private readonly Func<string, string> _send;

    public NotificationRelay(ChannelBase channel) => _send = channel.Notify;

    public string Relay(string message) => _send(message);
}
