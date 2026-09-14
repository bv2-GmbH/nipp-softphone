using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Search;
using Nipp.Core.Services.Integrations.Secrets;
using Nipp.Core.Services.Settings;

namespace Nipp.Core.Tests.Services.Integrations.Search;

/// <summary>
/// Die Suche über mehrere Quellen (§21.1).
///
/// <b>Der wichtigste Test dieser Datei ist der Wettlauf</b>, der im Auftrag
/// ausdrücklich genannt ist: Suche „Hans" startet Anfrage A, Suche „Hansi"
/// startet B, und A kommt nach B zurück. Ohne Generationszähler überschriebe
/// das alte Ergebnis das neue, und in der Liste stünden Treffer zu einem Text,
/// der nicht mehr im Feld steht. Das ist der Fehler, der beim Kunden auftritt
/// und im Entwicklungsbetrieb nie — dort ist das Netz zu schnell.
/// </summary>
public sealed class ContactSearchServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _zeit = new();

    /// <summary>Eine Quelle, die der Test steuert.</summary>
    private sealed class Quelle(
        string id,
        bool lokal,
        Func<ContactQuery, CancellationToken, Task<ContactSearchPage>> antwort)
        : IContactSearchProvider
    {
        public string SourceId => id;

        public string DisplayName => id.ToUpperInvariant();

        public ContactSearchTraits Traits { get; init; } = new(lokal);

        public int Aufrufe { get; private set; }

        public Task<ContactSearchPage> SearchAsync(ContactQuery query, CancellationToken cancellationToken)
        {
            Aufrufe++;
            return antwort(query, cancellationToken);
        }
    }

    /// <summary>
    /// Ein Kontakt für die Tests.
    ///
    /// <b>Die Nummer wird aus dem Namen abgeleitet</b>, damit verschiedene
    /// Personen verschiedene Nummern haben. Gäben alle dieselbe, führte der
    /// Merger sie zusammen — und ein Test über die Fortlaufigkeit der Suche
    /// prüfte in Wahrheit das Zusammenführen. Wer das prüfen will, gibt die
    /// Nummer ausdrücklich mit.
    /// </summary>
    private static Contact Kontakt(string name, string quelle, string? nummer = null) =>
        new(
            $"{quelle}:{name}",
            name,
            [new ContactNumber(nummer ?? AbgeleiteteNummer(name), ContactNumberKind.Business)],
            quelle == "lokal" ? ContactSourceKind.Outlook : ContactSourceKind.External,
            SourceId: quelle);

    private static string AbgeleiteteNummer(string name) =>
        $"07912{Math.Abs(name.Sum(static c => c) % 100000):D5}";

    private static Quelle Liefert(string id, bool lokal, params string[] namen) =>
        new(id, lokal, (_, _) => Task.FromResult(new ContactSearchPage(
            id,
            namen.Length == 0 ? SearchState.Empty : SearchState.Success,
            [.. namen.Select(n => Kontakt(n, id))])));

    private IntegrationConfigStore Konfiguration(ContactSearchSettings? suche = null)
    {
        var geheimnisse = new IntegrationSecrets(new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat")));

        var store = new IntegrationConfigStore(
            new IntegrationConfigValidator(geheimnisse),
            NullLogger<IntegrationConfigStore>.Instance,
            Path.Combine(_directory, "integrations.json"));

        store.Save(new IntegrationConfig
        {
            ContactSearch = suche ?? new ContactSearchSettings { DebounceMs = 300, MinQueryLength = 2 },
        });

        return store;
    }

    /// <summary>
    /// Eine feste Quellenliste. Die echte Registry baut sie aus der
    /// Konfiguration; für diese Tests ist die Herkunft ohne Belang, geprüft
    /// wird das Zusammenspiel.
    /// </summary>
    private sealed class FesteRegistry(IEnumerable<IContactSearchProvider> quellen)
        : ISearchProviderRegistry
    {
        public IReadOnlyList<IContactSearchProvider> SearchProviders { get; } = [.. quellen];
    }

    private ContactSearchService Dienst(
        IEnumerable<IContactSearchProvider> quellen,
        ContactSearchSettings? suche = null) =>
        new(
            new FesteRegistry(quellen),
            new ContactMerger(),
            Konfiguration(suche),
            NullLogger<ContactSearchService>.Instance,
            _zeit);

    /// <summary>Sammelt, was der Dienst meldet.</summary>
    private sealed class Mitschrift
    {
        public List<ContactSearchSnapshot> Staende { get; } = [];

        public ContactSearchSnapshot Letzter => Staende[^1];

        public IReadOnlyList<string> LetzteNamen =>
            [.. Letzter.Contacts.Select(static c => c.DisplayName)];
    }

    private static Mitschrift Beobachte(ContactSearchService dienst)
    {
        var mitschrift = new Mitschrift();
        dienst.ResultsChanged += (_, stand) => mitschrift.Staende.Add(stand);

        return mitschrift;
    }

    /// <summary>
    /// Lässt die Fortsetzungen laufen. Der Dienst arbeitet ohne
    /// <c>SynchronizationContext</c>, also auf dem Threadpool — ein kurzes
    /// Nachgeben genügt, damit die Aufgaben durchlaufen.
    /// </summary>
    private static async Task AbwartenAsync()
    {
        for (var i = 0; i < 20; i++)
        {
            await Task.Yield();
            await Task.Delay(1);
        }
    }

    // --- Lokale Suche ---

    [Fact]
    public async Task Lokale_Treffer_erscheinen_ohne_Wartezeit()
    {
        using var dienst = Dienst([Liefert("lokal", lokal: true, "Hans Muster")]);
        var mitschrift = Beobachte(dienst);

        dienst.Search("Hans");
        await AbwartenAsync();

        // Ohne dass die Uhr bewegt wurde: das Debounce gilt nur für Quellen,
        // die über das Netz gehen.
        Assert.Contains("Hans Muster", mitschrift.LetzteNamen);
        Assert.True(mitschrift.Letzter.IsComplete);
    }

    [Fact]
    public async Task Eine_zu_kurze_Eingabe_fragt_niemanden()
    {
        var quelle = Liefert("lokal", lokal: true, "Hans");
        using var dienst = Dienst([quelle]);
        var mitschrift = Beobachte(dienst);

        dienst.Search("H");
        await AbwartenAsync();

        Assert.Equal(0, quelle.Aufrufe);
        Assert.Empty(mitschrift.Letzter.Contacts);
    }

    [Fact]
    public async Task Eine_geleerte_Eingabe_raeumt_die_Liste()
    {
        using var dienst = Dienst([Liefert("lokal", lokal: true, "Hans Muster")]);
        var mitschrift = Beobachte(dienst);

        dienst.Search("Hans");
        await AbwartenAsync();
        Assert.NotEmpty(mitschrift.Letzter.Contacts);

        dienst.Search(string.Empty);
        await AbwartenAsync();

        Assert.Empty(mitschrift.Letzter.Contacts);
    }

    // --- Debounce ---

    /// <summary>
    /// Ohne Debounce stellte jeder Tastendruck eine Anfrage an jedes fremde
    /// System. „Hans Muster" wären elf Anfragen je Quelle.
    /// </summary>
    [Fact]
    public async Task Vor_dem_Debounce_wird_keine_fremde_Quelle_gefragt()
    {
        var fern = Liefert("crm", lokal: false, "Hans aus dem CRM");
        using var dienst = Dienst([fern]);

        dienst.Search("Hans");
        await AbwartenAsync();

        Assert.Equal(0, fern.Aufrufe);

        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        Assert.Equal(1, fern.Aufrufe);
    }

    [Fact]
    public async Task Schnelles_Tippen_ergibt_genau_eine_Anfrage()
    {
        var fern = Liefert("crm", lokal: false, "Hans");
        using var dienst = Dienst([fern]);

        foreach (var text in new[] { "Ha", "Han", "Hans", "Hansi" })
        {
            dienst.Search(text);
            _zeit.Advance(TimeSpan.FromMilliseconds(50));
            await AbwartenAsync();
        }

        Assert.Equal(0, fern.Aufrufe);

        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        Assert.Equal(1, fern.Aufrufe);
    }

    // --- Der Wettlauf aus dem Auftrag ---

    /// <summary>
    /// <b>Der Fall, um den es geht.</b> Anfrage A („Hans") ist langsam,
    /// Anfrage B („Hansi") schnell. A kommt nach B zurück und darf die
    /// Ergebnisse von B nicht überschreiben.
    /// </summary>
    [Fact]
    public async Task Eine_ueberholte_Antwort_ueberschreibt_die_neuere_nicht()
    {
        var langsam = new TaskCompletionSource<ContactSearchPage>();
        var schnell = new TaskCompletionSource<ContactSearchPage>();
        var aufrufe = 0;

        var quelle = new Quelle("crm", lokal: false, (query, _) =>
        {
            aufrufe++;
            return query.Text == "Hans" ? langsam.Task : schnell.Task;
        });

        using var dienst = Dienst([quelle]);
        var mitschrift = Beobachte(dienst);

        // Erste Suche, bis über das Debounce hinaus.
        dienst.Search("Hans");
        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();
        Assert.Equal(1, aufrufe);

        // Zweite Suche, ebenfalls abgeschickt.
        dienst.Search("Hansi");
        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();
        Assert.Equal(2, aufrufe);

        // Jetzt antwortet die zweite zuerst.
        schnell.SetResult(new ContactSearchPage(
            "crm", SearchState.Success, [Kontakt("Hansi Neu", "crm")]));
        await AbwartenAsync();

        Assert.Contains("Hansi Neu", mitschrift.LetzteNamen);

        // Und danach die erste, überholte.
        langsam.SetResult(new ContactSearchPage(
            "crm", SearchState.Success, [Kontakt("Hans Alt", "crm")]));
        await AbwartenAsync();

        // Die alte Antwort darf nichts verändert haben.
        Assert.Contains("Hansi Neu", mitschrift.LetzteNamen);
        Assert.DoesNotContain("Hans Alt", mitschrift.LetzteNamen);
        Assert.Equal("Hansi", mitschrift.Letzter.Query);
    }

    /// <summary>
    /// Eine überholte Anfrage wird abgebrochen, statt zu Ende zu laufen — sie
    /// kostet sonst Bandbreite und Last beim Kunden.
    /// </summary>
    [Fact]
    public async Task Eine_ueberholte_Anfrage_wird_abgebrochen()
    {
        var abgebrochen = false;

        var quelle = new Quelle("crm", lokal: false, async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
                abgebrochen = true;
                throw;
            }

            return ContactSearchPage.Empty("crm");
        });

        using var dienst = Dienst([quelle]);

        dienst.Search("Hans");
        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        dienst.Search("Hansi");
        await AbwartenAsync();

        Assert.True(abgebrochen, "Die überholte Anfrage lief weiter.");
    }

    // --- Mehrere Quellen ---

    /// <summary>
    /// §21.2: eine langsame Quelle hält keine schnelle auf. Die Treffer
    /// erscheinen fortlaufend.
    /// </summary>
    [Fact]
    public async Task Eine_langsame_Quelle_haelt_die_schnellen_nicht_auf()
    {
        var langsam = new TaskCompletionSource<ContactSearchPage>();

        var quellen = new IContactSearchProvider[]
        {
            Liefert("lokal", lokal: true, "Aus Outlook"),
            Liefert("crm", lokal: false, "Aus dem CRM"),
            new Quelle("erp", lokal: false, (_, _) => langsam.Task),
        };

        using var dienst = Dienst(quellen);
        var mitschrift = Beobachte(dienst);

        dienst.Search("Muster");
        await AbwartenAsync();

        // Lokal sofort, noch vor dem Debounce.
        Assert.Contains("Aus Outlook", mitschrift.LetzteNamen);

        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        // CRM ist da, ERP fehlt noch — und die Suche gilt als unfertig.
        Assert.Contains("Aus dem CRM", mitschrift.LetzteNamen);
        Assert.False(mitschrift.Letzter.IsComplete);

        langsam.SetResult(new ContactSearchPage(
            "erp", SearchState.Success, [Kontakt("Aus dem ERP", "erp")]));
        await AbwartenAsync();

        Assert.Contains("Aus dem ERP", mitschrift.LetzteNamen);
        Assert.True(mitschrift.Letzter.IsComplete);
    }

    [Fact]
    public async Task Der_Zustand_jeder_Quelle_ist_sichtbar()
    {
        var quellen = new IContactSearchProvider[]
        {
            Liefert("lokal", lokal: true, "Hans"),
            Liefert("crm", lokal: false),
            new Quelle("erp", lokal: false, (_, _) => throw new InvalidOperationException("kaputt")),
        };

        using var dienst = Dienst(quellen);
        var mitschrift = Beobachte(dienst);

        dienst.Search("Hans");
        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        var zustaende = mitschrift.Letzter.Sources.ToDictionary(
            static s => s.SourceId,
            static s => s.State,
            StringComparer.Ordinal);

        Assert.Equal(SearchState.Success, zustaende["lokal"]);
        Assert.Equal(SearchState.Empty, zustaende["crm"]);
        Assert.Equal(SearchState.Error, zustaende["erp"]);
    }

    /// <summary>
    /// Eine Quelle, die wirft, darf die Suche nicht mitnehmen — die anderen
    /// Treffer bleiben stehen (§21.2).
    /// </summary>
    [Fact]
    public async Task Eine_kaputte_Quelle_kostet_nur_sich_selbst()
    {
        var quellen = new IContactSearchProvider[]
        {
            Liefert("lokal", lokal: true, "Hans Muster"),
            new Quelle("crm", lokal: false, (_, _) => throw new HttpRequestException("weg")),
        };

        using var dienst = Dienst(quellen);
        var mitschrift = Beobachte(dienst);

        dienst.Search("Hans");
        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        Assert.Contains("Hans Muster", mitschrift.LetzteNamen);
        Assert.Contains(
            mitschrift.Letzter.Sources,
            s => s.SourceId == "crm" && s.Message is { Length: > 0 });
    }

    [Fact]
    public async Task Eine_Quelle_die_zu_lange_braucht_laeuft_in_ihre_Zeitgrenze()
    {
        var quelle = new Quelle(
            "crm",
            lokal: false,
            async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return ContactSearchPage.Empty("crm");
            })
        {
            Traits = new ContactSearchTraits(IsLocal: false, Timeout: TimeSpan.FromMilliseconds(50)),
        };

        using var dienst = Dienst([quelle]);
        var mitschrift = Beobachte(dienst);

        dienst.Search("Hans");
        _zeit.Advance(TimeSpan.FromMilliseconds(300));
        await AbwartenAsync();

        Assert.Contains(
            mitschrift.Letzter.Sources,
            s => s.SourceId == "crm" && s.State == SearchState.Timeout);
    }

    [Fact]
    public void Ob_es_ueberhaupt_ferne_Quellen_gibt_ist_abfragbar()
    {
        using var nurLokal = Dienst([Liefert("lokal", lokal: true)]);
        Assert.False(nurLokal.HasRemoteSources);

        using var mitFern = Dienst([Liefert("lokal", lokal: true), Liefert("crm", lokal: false)]);
        Assert.True(mitFern.HasRemoteSources);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
