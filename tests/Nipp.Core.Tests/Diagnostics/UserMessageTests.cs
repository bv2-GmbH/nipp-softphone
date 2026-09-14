using Nipp.Core.Diagnostics;

namespace Nipp.Core.Tests.Diagnostics;

/// <summary>
/// W1.3 und Befund C7: eine Fehlermeldung sagt zuerst, was zu tun ist.
///
/// <para><b>Der Befund.</b> An mehreren Stellen war die Meldung des Systems
/// die ganze Aussage — «Ausgabe fehlgeschlagen» als Titel und
/// <c>ex.Message</c> als Text. Was dort steht, ist auf Englisch, nennt einen
/// .NET-Typ und sagt nie, was der Benutzer tun soll.</para>
/// </summary>
public sealed class UserMessageTests
{
    [Fact]
    public void Der_eigene_Satz_steht_vorn()
    {
        var text = UserMessage.WithCause(
            "Die Datei liess sich nicht schreiben. Einen anderen Ordner wählen.",
            new UnauthorizedAccessException("Access to the path is denied."));

        Assert.StartsWith("Die Datei liess sich nicht schreiben.", text, StringComparison.Ordinal);
        Assert.Contains("Technische Ursache:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Ausnahme_ohne_Meldung_haengt_nichts_an()
    {
        // «Technische Ursache:» ohne Ursache dahinter wäre schlechter als
        // nichts — es sieht aus, als fehle etwas.
        var text = UserMessage.WithCause("Das ging nicht.", new LeereAusnahme());

        Assert.Equal("Das ging nicht.", text);
    }

    [Fact]
    public void Ein_Pfad_mit_Rufnummer_wird_maskiert()
    {
        // §21.2: was der Benutzer auf dem Bildschirm sieht, kopiert er in ein
        // Support-Ticket. Ein Aufnahmepfad trägt die Rufnummer im Dateinamen.
        var text = UserMessage.WithCause(
            "Die Aufnahme liess sich nicht öffnen.",
            new IOException("Datei 2026-09-13_101500_0791234567.wav ist gesperrt"));

        Assert.DoesNotContain("0791234567", text, StringComparison.Ordinal);
    }

    private sealed class LeereAusnahme : Exception
    {
        public override string Message => string.Empty;
    }
}
