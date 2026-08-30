using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Psi.CSharp;

namespace RiderBackendEffectSpike;

[ZoneMarker]
public sealed class ZoneMarker : IRequire<ILanguageCSharpZone>;
