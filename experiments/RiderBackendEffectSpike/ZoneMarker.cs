using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Psi.CSharp;

namespace CodeRig.Rider;

[ZoneMarker]
public sealed class ZoneMarker : IRequire<ILanguageCSharpZone>;
