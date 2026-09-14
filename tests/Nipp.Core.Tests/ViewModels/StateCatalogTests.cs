using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Die beiden Wortkataloge (ADR-044).
///
/// <para><b>Wogegen diese Tests stehen.</b> Der Zustand eines Anrufs stand
/// dreimal im Code, jedes Mal mit eigenem Wortlaut, und der eines Kontos
/// viermal. Die Kopien fielen niemandem auf, weil kein Benutzer zwei davon
/// gleichzeitig sieht — er sieht sie nacheinander. Ein Test, der beide Enden
/// derselben Aufzählung prüft, fängt die nächste Kopie: sobald jemand einen
/// Zustand wieder vor Ort ausschreibt, bleibt der Katalog hier zurück, und
/// spätestens die Vollständigkeitsprüfung meldet es.</para>
/// </summary>
public sealed class StateCatalogTests
{
    [Fact]
    public void Jeder_Anrufzustand_hat_ein_Wort()
    {
        foreach (var status in Enum.GetValues<CallStatus>())
        {
            var wort = CallStateCatalog.Of(status);

            Assert.False(string.IsNullOrWhiteSpace(wort));
            Assert.NotEqual(status.ToString(), wort);
        }
    }

    [Fact]
    public void Die_Gegenseite_heisst_ueberall_gleich()
    {
        // „Gegenstelle" und „Gegenseite" standen nebeneinander im Code. Die
        // sichtbaren XAML-Texte sagen „die Gegenseite muss davon wissen" —
        // das ist das Wort, auf das sich alles andere ausrichtet.
        Assert.Equal("von der Gegenseite gehalten", CallStateCatalog.Of(CallStatus.RemoteOnHold));
        Assert.DoesNotContain("Gegenstelle", CallStateCatalog.Of(CallStatus.RemoteOnHold), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CallStatus.Ended)]
    [InlineData(CallStatus.Failed)]
    public void Die_Kopfzeile_schweigt_nach_dem_Ende(CallStatus status) =>
        Assert.Equal(string.Empty, CallStateCatalog.Caption(status));

    [Fact]
    public void Ohne_Gespraech_bleibt_die_Kopfzeile_leer() =>
        Assert.Equal(string.Empty, CallStateCatalog.Caption(null));

    [Fact]
    public void Nur_ein_verbundenes_Gespraech_laeuft_weiter()
    {
        // Der Satz erklärt den Weg zurück zur Wähltastatur. In jedem anderen
        // Zustand war er falsch, und dort stand er eine Zeit lang trotzdem.
        Assert.Equal("Gespräch läuft weiter", CallStateCatalog.Caption(CallStatus.Connected));

        foreach (var status in Enum.GetValues<CallStatus>())
        {
            if (status is CallStatus.Connected or CallStatus.Ended or CallStatus.Failed)
            {
                continue;
            }

            Assert.Equal(CallStateCatalog.Of(status), CallStateCatalog.Caption(status));
        }
    }

    [Fact]
    public void Jeder_Kontozustand_hat_ein_Wort()
    {
        foreach (var status in Enum.GetValues<RegistrationStatus>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AccountStateCatalog.Of(status)));
        }
    }

    [Fact]
    public void Das_Wort_heisst_Anmeldung_und_nicht_Registrierung()
    {
        // Wer eine Fehlermeldung an den Support weitergab, suchte danach
        // vergeblich nach dem Wort „Registrierung" in der Oberfläche.
        foreach (var status in Enum.GetValues<RegistrationStatus>())
        {
            Assert.DoesNotContain("egistrier", AccountStateCatalog.Line(status), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(RegistrationStatus.Registered)]
    [InlineData(RegistrationStatus.InProgress)]
    public void Ohne_Problem_keine_Folge(RegistrationStatus status)
    {
        Assert.Equal(string.Empty, AccountStateCatalog.Consequence(status));
        Assert.Equal(AccountStateCatalog.Of(status), AccountStateCatalog.Line(status));
    }

    [Fact]
    public void Wer_nicht_angemeldet_ist_erfaehrt_die_Folge()
    {
        // Die Lampe allein sagt nicht, was sie bedeutet (§8.4).
        var zeile = AccountStateCatalog.Line(RegistrationStatus.Unregistered);

        Assert.Contains("Anrufe sind nicht möglich", zeile, StringComparison.Ordinal);
        Assert.StartsWith(AccountStateCatalog.Of(RegistrationStatus.Unregistered), zeile, StringComparison.Ordinal);
    }
}
