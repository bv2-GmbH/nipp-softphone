using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Telephony.Model;
using Nipp.Core.ViewModels;

namespace Nipp.Core.Tests.ViewModels;

/// <summary>
/// <b>Befund vom 06.09.2026:</b> ein Kontakt aus dem CRM mit Festnetz
/// <b>und</b> Mobilnummer zeigte nur die geschäftliche — und ein Klick wählte
/// auch nur die. Beide Nummern standen im Modell; verloren gingen sie in
/// <see cref="ContactRow"/>, das ausschliesslich
/// <see cref="Contact.PrimaryNumber"/> weitergab, also <c>Numbers[0]</c>.
/// </summary>
public sealed class ContactRowTests
{
    [Fact]
    public void Beide_Nummern_eines_Kontakts_sind_waehlbar()
    {
        var row = Zeile(
            new ContactNumber("0713142250", ContactNumberKind.Business),
            new ContactNumber("0791234567", ContactNumberKind.Mobile));

        Assert.Equal(2, row.Choices.Count);
        Assert.Equal(["0713142250", "0791234567"], row.Choices.Select(static c => c.Number));
        Assert.Equal(["Geschäftlich", "Mobil"], row.Choices.Select(static c => c.KindLabel));
    }

    /// <summary>
    /// Die Reihenfolge des Kontakts bleibt erhalten: die erste ist die, die
    /// ein Klick ohne Rückfrage wählt.
    /// </summary>
    [Fact]
    public void Die_erste_Nummer_bleibt_die_erste()
    {
        var row = Zeile(
            new ContactNumber("0713142250", ContactNumberKind.Business),
            new ContactNumber("0791234567", ContactNumberKind.Mobile));

        Assert.Equal("0713142250", row.Number);
        Assert.Equal("0713142250", row.Choices[0].Number);
    }

    [Fact]
    public void Bei_einer_einzigen_Nummer_wird_nicht_gefragt()
    {
        var row = Zeile(new ContactNumber("0713142250", ContactNumberKind.Business));

        Assert.False(row.HasMultipleNumbers);
        Assert.Empty(row.MoreNumbersHint);
    }

    [Fact]
    public void Ein_Kontakt_ohne_Nummer_hat_keine_Auswahl()
    {
        var row = Zeile();

        Assert.Empty(row.Choices);
        Assert.False(row.HasMultipleNumbers);
    }

    /// <summary>
    /// Leere Einträge kommen vor: das CRM liefert <c>mobile_number: ""</c>
    /// für einen Kontakt ohne Mobilnummer. Eine leere Zeile im Auswahlmenü
    /// wäre nicht wählbar und trotzdem anklickbar.
    /// </summary>
    [Fact]
    public void Eine_leere_Nummer_erscheint_nicht_in_der_Auswahl()
    {
        var row = Zeile(
            new ContactNumber("0713142250", ContactNumberKind.Business),
            new ContactNumber("", ContactNumberKind.Mobile),
            new ContactNumber("   ", ContactNumberKind.Home));

        Assert.Single(row.Choices);
        Assert.False(row.HasMultipleNumbers);
    }

    [Theory]
    [InlineData(2, "+1 Nummer")]
    [InlineData(3, "+2 Nummern")]
    public void Der_Hinweis_zaehlt_die_uebrigen_Nummern(int anzahl, string erwartet)
    {
        var nummern = Enumerable
            .Range(1, anzahl)
            .Select(i => new ContactNumber($"07131422{i:D2}", ContactNumberKind.Business))
            .ToArray();

        Assert.Equal(erwartet, Zeile(nummern).MoreNumbersHint);
    }

    [Fact]
    public void Der_Hinweis_steht_hinten_in_der_zweiten_Zeile()
    {
        var row = Zeile(
            new ContactNumber("0713142250", ContactNumberKind.Business),
            new ContactNumber("0791234567", ContactNumberKind.Mobile));

        Assert.StartsWith("071 314 22 50", row.SubLabel, StringComparison.Ordinal);
        Assert.EndsWith("+1 Nummer", row.SubLabel, StringComparison.Ordinal);
        Assert.Contains("4net AG", row.SubLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Ohne_weitere_Nummern_bleibt_die_zweite_Zeile_wie_bisher()
    {
        var row = Zeile(new ContactNumber("0713142250", ContactNumberKind.Business));

        Assert.Equal("071 314 22 50 · 4net AG", row.SubLabel);
    }

    private static ContactRow Zeile(params ContactNumber[] numbers) =>
        new(
            new Contact(
                Id: "c1",
                DisplayName: "Hans Muster",
                Numbers: numbers,
                Source: ContactSourceKind.External,
                Company: "4net AG"),
            PresenceStatus.Unknown);

    /// <summary>
    /// <b>Die Präsenz gehört in den Namen</b> (ADR-046).
    ///
    /// <para>Sie steht sichtbar in derselben Zeile, aber der Name auf dem
    /// umschliessenden Raster gilt für die ganze Zeile — was sonst noch darin
    /// steht, wird beim Durchgehen der Liste nicht mehr vorgelesen. §8.4
    /// verlangt Farbe <b>und</b> Text; sichtbar war beides da, für die
    /// Sprachausgabe galt es nicht.</para>
    /// </summary>
    [Fact]
    public void Der_Name_fuer_die_Sprachausgabe_nennt_die_Praesenz()
    {
        var zeile = new ContactRow(
            new Contact(
                "team:0:201",
                "Anna",
                [new ContactNumber("201", ContactNumberKind.Business)],
                ContactSourceKind.Team,
                SipAddress: "sip:201@pbx.example.ch"),
            PresenceStatus.OnCall);

        Assert.True(zeile.HasPresence);
        Assert.Contains(zeile.PresenceText, zeile.AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith("Anna", zeile.AccessibleName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ohne Präsenz bleibt der Name, was er war — ein Outlook-Kontakt bekommt
    /// keine Lampe (§14.8) und darf sich auch keine anhören.
    /// </summary>
    [Fact]
    public void Ohne_Praesenz_bleibt_der_Name_kurz()
    {
        var zeile = new ContactRow(
            new Contact(
                "outlook:1",
                "Bruno",
                [new ContactNumber("+41791234567", ContactNumberKind.Mobile)],
                ContactSourceKind.Outlook),
            PresenceStatus.Unknown);

        Assert.False(zeile.HasPresence);
        Assert.DoesNotContain("unbekannt", zeile.AccessibleName, StringComparison.Ordinal);
    }
}
