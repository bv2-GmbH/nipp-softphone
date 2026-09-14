using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.History;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;
using NSubstitute;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// Das breite Layout (ADR-047) und der Detailbereich in der Zeile (ADR-048).
///
/// <para><b>Warum das hier und nicht in der Seite geprüft wird.</b>
/// <c>Nipp.App</c> hat kein Testprojekt, und an der einen Entscheidung «ist das
/// Fenster breit genug» hängt die halbe Hauptansicht: wo die Nebenstellen
/// stehen, was die Umschaltleiste umschaltet, wie breit die Inhaltsspalte ist.
/// Eine Regel, die nur im XAML steht, ist eine ungeprüfte Regel.</para>
/// </summary>
public sealed class ShellLayoutTests
{
    [Fact]
    public void Ein_schmales_Fenster_bleibt_schmal()
    {
        var model = Create();

        model.ApplyWidth(400);

        Assert.Equal(ShellLayout.Narrow, model.Layout);
        Assert.True(model.ShowTeamInLeftColumn);
        Assert.False(model.ShowTiles);
    }

    [Fact]
    public void Ab_der_Schwelle_stehen_zwei_Spalten()
    {
        var model = Create();

        model.ApplyWidth(ShellViewModel.WideThreshold);

        Assert.Equal(ShellLayout.Wide, model.Layout);
        Assert.True(model.ShowTiles);

        // Die Nebenstellen stehen dann rechts als Kacheln. Zweimal dieselbe
        // Liste in einem Fenster wäre die Doppelanzeige, wegen der ADR-046 die
        // beiden Suchfelder zusammengelegt hat.
        Assert.False(model.ShowTeamInLeftColumn);
    }

    [Fact]
    public void Knapp_unter_der_Schwelle_bleibt_es_bei_einer_Spalte()
    {
        var model = Create();

        model.ApplyWidth(ShellViewModel.WideThreshold - 1);

        Assert.Equal(ShellLayout.Narrow, model.Layout);
    }

    /// <summary>
    /// <b>Die Hysterese ist kein Feinschliff.</b> Wer das Fenster genau an der
    /// Schwelle zieht, baut ohne sie bei jedem Pixel die halbe Seite neu auf —
    /// auf dem Thread, der alle 20 ms <c>Core.Iterate()</c> bedient.
    /// </summary>
    [Fact]
    public void Unter_der_Schwelle_bleibt_es_breit_bis_zur_Hysterese()
    {
        var model = Create();

        model.ApplyWidth(ShellViewModel.WideThreshold);
        Assert.Equal(ShellLayout.Wide, model.Layout);

        // Ein Pixel unter der Schwelle: noch kein Grund umzubauen.
        model.ApplyWidth(ShellViewModel.WideThreshold - 1);
        Assert.Equal(ShellLayout.Wide, model.Layout);

        // Genau auf der Hysterese ebenfalls nicht — erst darunter.
        model.ApplyWidth(ShellViewModel.WideThreshold - ShellViewModel.WideHysteresis);
        Assert.Equal(ShellLayout.Wide, model.Layout);

        model.ApplyWidth(ShellViewModel.WideThreshold - ShellViewModel.WideHysteresis - 1);
        Assert.Equal(ShellLayout.Narrow, model.Layout);
    }

    /// <summary>
    /// Eine Grössenmeldung kommt bei jedem Pixel. Meldete <c>ApplyWidth</c>
    /// jedes Mal, zeichnete die Seite sich so oft neu — <c>Refresh</c> hängt an
    /// <c>PropertyChanged</c>.
    /// </summary>
    [Fact]
    public void Ohne_Wechsel_wird_nichts_gemeldet()
    {
        var model = Create();
        var meldungen = 0;

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ShellViewModel.Layout))
            {
                meldungen++;
            }
        };

        model.ApplyWidth(400);
        model.ApplyWidth(500);
        model.ApplyWidth(700);
        model.ApplyWidth(959);

        Assert.Equal(0, meldungen);

        model.ApplyWidth(1200);
        model.ApplyWidth(1400);
        model.ApplyWidth(1900);

        Assert.Equal(1, meldungen);
    }

    /// <summary>
    /// Beim ersten Messen steht eine Seite manchmal auf 0. Darauf hin das
    /// Layout zu wechseln hiesse, es gleich danach wieder zu wechseln.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Eine_unbrauchbare_Breite_aendert_nichts(double breite)
    {
        var model = Create();

        model.ApplyWidth(1200);
        model.ApplyWidth(breite);

        Assert.Equal(ShellLayout.Wide, model.Layout);
    }

    /// <summary>
    /// Eine offene Nebenstelle steht im breiten Layout nicht mehr in der linken
    /// Spalte — ihre Zeile gibt es dort nicht mehr, und der Bereich zeigte auf
    /// eine Zeile, die niemand sieht.
    /// </summary>
    [Fact]
    public void Der_Wechsel_nach_breit_schliesst_eine_offene_Nebenstelle()
    {
        var model = Create();
        var zeile = Zeile("Anna Muster", ContactSourceKind.Team);

        model.ToggleContactDetails(zeile);
        Assert.True(zeile.IsDetailExpanded);

        model.ApplyWidth(1200);

        Assert.Null(model.ExpandedContact);
        Assert.False(zeile.IsDetailExpanded);
    }

    /// <summary>
    /// Ein Outlook-Kontakt bleibt offen: seine Liste wechselt nur den Platz,
    /// nicht die Form.
    /// </summary>
    [Fact]
    public void Der_Wechsel_nach_breit_laesst_einen_Outlook_Kontakt_offen()
    {
        var model = Create();
        var zeile = Zeile("Hans Beispiel", ContactSourceKind.Outlook);

        model.ToggleContactDetails(zeile);
        model.ApplyWidth(1200);

        Assert.Same(zeile, model.ExpandedContact);
        Assert.True(zeile.IsDetailExpanded);
    }

    // ---- ADR-048: der Detailbereich klappt in der Zeile auf ----

    /// <summary>
    /// <b>Die Invariante steht im Setter</b>, nicht bei den Aufrufern: «es ist
    /// höchstens eine Zeile offen» ist damit eine Eigenschaft der Eigenschaft
    /// und kein Vertrag, an den sich drei Stellen erinnern müssen.
    /// </summary>
    [Fact]
    public void Es_ist_hoechstens_eine_Zeile_offen()
    {
        var model = Create();
        var erste = Zeile("Anna Muster", ContactSourceKind.Outlook);
        var zweite = Zeile("Beat Beispiel", ContactSourceKind.Outlook);

        model.ToggleContactDetails(erste);
        model.ToggleContactDetails(zweite);

        Assert.False(erste.IsDetailExpanded);
        Assert.True(zweite.IsDetailExpanded);
        Assert.Same(zweite, model.ExpandedContact);
    }


    [Fact]
    public void Dieselbe_Zeile_noch_einmal_schliesst_sie()
    {
        var model = Create();
        var zeile = Zeile("Anna Muster", ContactSourceKind.Outlook);

        model.ToggleContactDetails(zeile);
        model.ToggleContactDetails(zeile);

        Assert.False(zeile.IsDetailExpanded);
        Assert.Null(model.ExpandedContact);
        Assert.False(model.HasContactDetails);
    }

    [Fact]
    public void Schliessen_nimmt_die_Fahne_von_der_Zeile()
    {
        var model = Create();
        var zeile = Zeile("Anna Muster", ContactSourceKind.Outlook);

        model.ToggleContactDetails(zeile);
        model.CloseContactDetails();

        Assert.False(zeile.IsDetailExpanded);
    }

    /// <summary>
    /// Eine Zeile mit ausgeklapptem Innenleben ist als Ziehziel weder zu
    /// treffen noch zu erklären (ADR-042).
    /// </summary>
    [Fact]
    public void Der_Sortiermodus_schliesst_den_Bereich()
    {
        var model = Create();
        var zeile = Zeile("Anna Muster", ContactSourceKind.Team);

        model.ToggleContactDetails(zeile);
        model.IsTeamReorderMode = true;

        Assert.Null(model.ExpandedContact);
        Assert.False(zeile.IsDetailExpanded);
    }

    // ---- ADR-047: was auf einer Kachel Platz hat ----

    [Fact]
    public void Eine_Kachel_zeigt_zwei_Nummern_und_nennt_den_Rest()
    {
        var row = Zeile(
            "Anna Muster",
            ContactSourceKind.Team,
            new ContactNumber("21", ContactNumberKind.Business),
            new ContactNumber("0791234567", ContactNumberKind.Mobile),
            new ContactNumber("0711112233", ContactNumberKind.Home));

        Assert.Equal(["21", "0791234567"], row.TileChoices.Select(static c => c.Number));
        Assert.True(row.HasHiddenChoices);
        Assert.Equal("+1 Nummer", row.HiddenChoicesHint);
        Assert.Equal(["0711112233"], row.HiddenChoices.Select(static c => c.Number));
    }

    /// <summary>Der Regelfall seit ADR-041: Nebenstelle und Handy.</summary>
    [Fact]
    public void Zwei_Nummern_passen_ganz_auf_die_Kachel()
    {
        var row = Zeile(
            "Anna Muster",
            ContactSourceKind.Team,
            new ContactNumber("21", ContactNumberKind.Business),
            new ContactNumber("0791234567", ContactNumberKind.Mobile));

        Assert.Equal(2, row.TileChoices.Count);
        Assert.False(row.HasHiddenChoices);
        Assert.Empty(row.HiddenChoicesHint);
    }

    /// <summary>
    /// Ein Raster hat keine Zeilenreihenfolge, an der man sich entlanghangelt:
    /// wer mit den Pfeiltasten hineinspringt, muss hören, in welchem Abschnitt
    /// er gelandet ist.
    /// </summary>
    [Fact]
    public void Die_Kachel_nennt_der_Sprachausgabe_ihre_Gruppe()
    {
        var kontakt = new Contact(
            "team:anna",
            "Anna Muster",
            [new ContactNumber("21", ContactNumberKind.Business)],
            ContactSourceKind.Team,
            SipAddress: "sip:21@example.test",
            Group: "Support");

        var row = new ContactRow(kontakt, PresenceStatus.Available);

        Assert.Contains("Anna Muster", row.TileAccessibleName, StringComparison.Ordinal);
        Assert.Contains("Support", row.TileAccessibleName, StringComparison.Ordinal);
        Assert.Contains(row.PresenceText, row.TileAccessibleName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Die Gruppe steht im Kopf über der Kachel; dasselbe Wort zweimal
    /// übereinander erklärt nichts (ADR-044). Sichtbar trägt die Zeile deshalb
    /// die Firma — und nichts, wo es keine gibt.
    /// </summary>
    [Fact]
    public void Ohne_Firma_bleibt_die_zweite_Kachelzeile_leer()
    {
        var row = Zeile("Anna Muster", ContactSourceKind.Team);

        Assert.Empty(row.TileContext);
    }

    private static ContactRow Zeile(
        string name,
        ContactSourceKind quelle,
        params ContactNumber[] nummern)
    {
        var kontakt = new Contact(
            $"{quelle}:{name}",
            name,
            nummern.Length > 0
                ? nummern
                : [new ContactNumber("0713142250", ContactNumberKind.Business)],
            quelle,
            SipAddress: quelle == ContactSourceKind.Team ? $"sip:21@example.test" : null);

        return new ContactRow(kontakt, PresenceStatus.Available);
    }

    /// <summary>
    /// Ein <see cref="ShellViewModel"/> ohne Netz, ohne SDK und ohne Quellen.
    ///
    /// <para><b>Der Pfad kommt aus dem temporären Verzeichnis</b> — kein Test
    /// darf auf die Einstellungen des angemeldeten Benutzers greifen
    /// (<c>TestIsolationTests</c>). Ein <c>dotnet test</c> hat hier schon
    /// einmal das SIP-Konto samt Passwort gelöscht.</para>
    /// </summary>
    private static ShellViewModel Create()
    {
        var verzeichnis = Path.Combine(
            Path.GetTempPath(),
            "nipp-tests",
            Guid.NewGuid().ToString("N"));

        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(verzeichnis, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(verzeichnis, "settings.json"));

        var sip = Substitute.For<ISipService>();

        sip.Accounts.Returns([]);
        sip.ActiveCalls.Returns([]);

        var kontakte = new ContactStore([], einstellungen, NullLogger<ContactStore>.Instance);

        return new ShellViewModel(
            sip,
            new CallHistoryStore(
                NullLogger<CallHistoryStore>.Instance,
                Path.Combine(verzeichnis, "history.db")),
            kontakte,
            new CallPartyResolver(new ClipResolver(kontakte)),
            new BlfService(sip, kontakte, einstellungen, NullLogger<BlfService>.Instance),
            einstellungen,
            new PolicyService(),
            NullLogger<ShellViewModel>.Instance);
    }
}
