using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Diagnostics;

namespace Nipp.Core.Tests.Diagnostics;

/// <summary>
/// W1.7 und Befund A7: erwartete Fehlschläge werden einmal sichtbar.
///
/// <para><b>Die Abwägung, um die es geht.</b> An etwa einem Dutzend Stellen
/// liest nipp eine Eigenschaft des SDK, die vor der Verhandlung wirft, und gab
/// bisher stillschweigend einen Ersatzwert zurück. Jede dieser Stellen bei
/// jedem Pump-Durchlauf zu protokollieren wäre Rauschen — der Timer läuft alle
/// 20 ms. Gar nicht zu protokollieren ist die Lücke, die dieses Projekt
/// zweimal teuer bezahlt hat.</para>
/// </summary>
public sealed class QuietFailuresTests : IDisposable
{
    public QuietFailuresTests() => QuietFailures.Reset();

    public void Dispose() => QuietFailures.Reset();

    [Fact]
    public void Der_erste_Fehlschlag_wird_gemeldet()
    {
        Assert.True(QuietFailures.Report(
            NullLogger.Instance, "LiestCodec", new InvalidOperationException("kein Codec")));
    }

    [Fact]
    public void Jeder_weitere_schweigt()
    {
        // Der eigentliche Zweck: im Gespräch entstehen sonst Tausende gleicher
        // Zeilen, und das Protokoll ist für alles andere unbrauchbar.
        QuietFailures.Report(NullLogger.Instance, "LiestCodec", new InvalidOperationException());

        Assert.False(QuietFailures.Report(
            NullLogger.Instance, "LiestCodec", new InvalidOperationException()));
    }

    [Fact]
    public void Eine_andere_Stelle_meldet_trotzdem()
    {
        // Die Gegenprobe. Ein Zähler, der nach dem ersten Fehlschlag alles
        // verschluckt, wäre schlimmer als der Zustand vorher.
        QuietFailures.Report(NullLogger.Instance, "LiestCodec", new InvalidOperationException());

        Assert.True(QuietFailures.Report(
            NullLogger.Instance, "LiestVerschluesselung", new InvalidOperationException()));
    }
}
