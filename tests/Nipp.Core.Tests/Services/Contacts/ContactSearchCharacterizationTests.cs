using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Contacts;

/// <summary>
/// Das Suchverhalten der Kontaktliste, <b>so wie es heute ist</b> (§8.4).
///
/// <b>Warum es diese Tests gibt.</b> Die Suche zieht mit §21 in ein
/// gemeinsames Framework um, in dem lokale Kontakte und externe Quellen
/// dieselbe Schnittstelle bedienen. Ein Umbau ohne Netz darunter ist die
/// beste Gelegenheit, das Verhalten unbemerkt zu verändern: eine Suche, die
/// plötzlich Gross- und Kleinschreibung unterscheidet oder Nummern mit
/// Trennzeichen nicht mehr findet, fällt erst beim Kunden auf, und niemand
/// verbindet sie mit dem Umbau.
///
/// Diese Tests beschreiben deshalb <b>den Ist-Zustand</b>, nicht den
/// Wunschzustand. Sie sind vor der Migration entstanden und müssen sie
/// unverändert überleben. Wenn einer davon rot wird, ist entweder die
/// Migration schief gegangen — oder jemand hat das Verhalten bewusst
/// geändert und muss den Test mit einer Begründung anpassen.
/// </summary>
public sealed class ContactSearchCharacterizationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private sealed class Quelle(ContactSourceKind kind, params Contact[] contacts) : IContactSource
    {
        public ContactSourceKind Kind => kind;

        public bool IsAvailable => true;

        public Task<IReadOnlyList<Contact>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Contact>>(contacts);
    }

    private static Contact Kontakt(
        string id,
        string name,
        string nummer,
        ContactSourceKind kind = ContactSourceKind.Outlook,
        string? firma = null) =>
        new(id, name, [new ContactNumber(nummer, ContactNumberKind.Business)], kind, Company: firma);

    private SettingsService Einstellungen() =>
        new(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_directory, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_directory, "settings.json"));

    /// <summary>Ein Speicher mit einem kleinen, aber vielfältigen Adressbuch.</summary>
    private async Task<ContactStore> GeladenerSpeicherAsync()
    {
        var einstellungen = Einstellungen();
        einstellungen.Load();

        var quelle = new Quelle(
            ContactSourceKind.Outlook,
            Kontakt("o:1", "Hans Muster", "044 512 84 30", firma: "Muster AG"),
            Kontakt("o:2", "Anna Beispiel", "+41791234567", firma: "Beispiel GmbH"),
            Kontakt("o:3", "Peter Meier", "0791112233", firma: "Muster AG"),
            Kontakt("o:4", "Zoe Zünd", "0041446667788"));

        var team = new Quelle(
            ContactSourceKind.Team,
            Kontakt("team:0:151", "Support", "151", ContactSourceKind.Team));

        var store = new ContactStore(
            [team, quelle],
            einstellungen,
            NullLogger<ContactStore>.Instance);

        await store.RefreshAsync(force: true);

        return store;
    }

    private static IReadOnlyList<string> Namen(IEnumerable<Contact> treffer) =>
        [.. treffer.Select(static c => c.DisplayName)];

    // --- Suche über den Namen ---

    [Fact]
    public async Task Ein_Teil_des_Namens_genuegt()
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Equal(["Hans Muster"], Namen(store.Search("Muster").Where(static c => c.DisplayName.StartsWith('H'))));
        Assert.Contains("Hans Muster", Namen(store.Search("Hans")));
    }

    [Theory]
    [InlineData("hans")]
    [InlineData("HANS")]
    [InlineData("HaNs")]
    public async Task Die_Namenssuche_unterscheidet_nicht_zwischen_gross_und_klein(string eingabe)
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Contains("Hans Muster", Namen(store.Search(eingabe)));
    }

    [Fact]
    public async Task Auch_die_Mitte_des_Namens_trifft()
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Contains("Anna Beispiel", Namen(store.Search("nna")));
    }

    // --- Suche über die Firma ---

    /// <summary>
    /// Die Firma zählt mit. Zwei Personen derselben Firma erscheinen beide —
    /// genau dafür ist es da.
    /// </summary>
    [Fact]
    public async Task Die_Firma_wird_mitdurchsucht()
    {
        var store = await GeladenerSpeicherAsync();

        var treffer = Namen(store.Search("Muster AG"));

        Assert.Contains("Hans Muster", treffer);
        Assert.Contains("Peter Meier", treffer);
    }

    // --- Suche über die Nummer ---

    /// <summary>
    /// <b>Der wichtigste Fall.</b> Wer <c>0445128430</c> tippt, findet auch
    /// <c>044 512 84 30</c> — verglichen wird über die Ziffern, ohne
    /// Trennzeichen. Ein Umbau, der das verliert, macht die Suche im Alltag
    /// unbrauchbar.
    /// </summary>
    [Theory]
    [InlineData("0445128430")]
    [InlineData("044 512 84 30")]
    [InlineData("512")]
    public async Task Trennzeichen_in_der_Nummer_werden_ueberlesen(string eingabe)
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Contains("Hans Muster", Namen(store.Search(eingabe)));
    }

    /// <summary>
    /// Gesucht wird nach einem <b>Teilstück</b> der Ziffern, an beliebiger
    /// Stelle. <c>791234</c> findet <c>+41791234567</c>.
    /// </summary>
    [Fact]
    public async Task Ein_Ausschnitt_der_Nummer_genuegt()
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Contains("Anna Beispiel", Namen(store.Search("791234")));
    }

    /// <summary>
    /// Das Länderpräfix wird <b>nicht</b> normalisiert: <c>0791234567</c>
    /// findet <c>+41791234567</c> heute nicht, weil über die rohen Ziffern
    /// verglichen wird und <c>0791234567</c> kein Teilstück von
    /// <c>41791234567</c> ist.
    ///
    /// <b>Das ist eine Schwäche, kein Wunsch</b> — aber sie ist der Ist-Zustand,
    /// und dieser Test hält sie fest, damit die Migration sie nicht
    /// versehentlich verändert. Wer sie beheben will, tut das als eigene
    /// Änderung mit eigenem Test.
    /// </summary>
    [Fact]
    public async Task Die_nationale_Schreibweise_findet_die_internationale_heute_nicht()
    {
        var store = await GeladenerSpeicherAsync();

        Assert.DoesNotContain("Anna Beispiel", Namen(store.Search("0791234567")));
    }

    // --- Randfälle ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Eine_leere_Eingabe_liefert_alles(string? eingabe)
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Equal(store.Contacts.Count, store.Search(eingabe).Count);
    }

    [Fact]
    public async Task Was_nirgends_vorkommt_liefert_nichts()
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Empty(store.Search("Rumpelstilzchen"));
    }

    /// <summary>
    /// Team-Nebenstellen werden mitdurchsucht und behalten ihren Platz vor
    /// den Outlook-Kontakten (§8.4: beide Quellen getrennt sichtbar).
    /// </summary>
    [Fact]
    public async Task Das_Team_wird_mitdurchsucht_und_steht_vorne()
    {
        var store = await GeladenerSpeicherAsync();

        var alle = store.Search(null);

        Assert.Equal(ContactSourceKind.Team, alle[0].Source);
        Assert.Contains("Support", Namen(store.Search("Sup")));
        Assert.Contains("Support", Namen(store.Search("151")));
    }

    /// <summary>
    /// Umlaute werden ohne Sonderbehandlung verglichen: <c>Zünd</c> findet
    /// sich unter <c>zü</c>, aber nicht unter <c>zu</c>. Auch das ist
    /// Ist-Zustand.
    /// </summary>
    [Fact]
    public async Task Umlaute_werden_nicht_umgeschrieben()
    {
        var store = await GeladenerSpeicherAsync();

        Assert.Contains("Zoe Zünd", Namen(store.Search("zü")));
        Assert.DoesNotContain("Zoe Zünd", Namen(store.Search("zund")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
