using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Context;
using Nipp.Core.Services.Integrations.Phone;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Telephony.Model;

namespace Nipp.Core.Tests.Services.Integrations.Context;

/// <summary>
/// Wie der Gesprächspartner heisst (ADR-043).
///
/// <para><b>Diese Tests gibt es wegen einer Asymmetrie, die niemand
/// beabsichtigt hatte.</b> Die Auflösung lief richtungsneutral, aber der
/// einzige richtungsneutrale <i>Name</i> im System — <c>CallInfo.DisplayLabel</c>
/// — schaute nie auf Kontakte. Bei eingehenden Anrufen kaschierte das der
/// Anzeigename der Anlage; bei ausgehenden stand die blosse Nummer da.</para>
///
/// <para>Zwei Regeln standen bis dahin nur als Kommentar im Code und sind
/// hier bewacht: <b>ein Name ist nie eine Nummer</b>, und <b>eine Zeile ist
/// nie leer</b>.</para>
/// </summary>
public sealed class CallPartyResolverTests : IDisposable
{
    private readonly string _verzeichnis = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Ein Kontaktspeicher mit den angegebenen Team-Nebenstellen.
    ///
    /// <b>Der Pfad kommt aus dem temporären Verzeichnis</b> — kein Test greift
    /// auf die Einstellungen des angemeldeten Benutzers (<c>TestIsolationTests</c>).
    /// </summary>
    private ContactStore Kontakte(params TeamExtension[] team)
    {
        var einstellungen = new SettingsService(
            new SecretStore(NullLogger<SecretStore>.Instance, Path.Combine(_verzeichnis, "secrets.dat")),
            NullLogger<SettingsService>.Instance,
            Path.Combine(_verzeichnis, "settings.json"));

        einstellungen.Save(new NippSettings
        {
            Contacts = new ContactSettings { Groups = ["Team"], Team = [.. team] },
        });

        var store = new ContactStore([], einstellungen, NullLogger<ContactStore>.Instance);

        store.ReloadTeam();

        return store;
    }

    private static CallInfo Anruf(
        CallDirection direction = CallDirection.Outgoing,
        string number = "201",
        string? displayName = null) =>
        new(
            Handle: CallHandle.New(),
            RemoteNumber: number,
            RemoteDisplayName: displayName,
            Direction: direction,
            Status: CallStatus.Connected,
            StatusMessage: null,
            StartedAt: DateTimeOffset.UtcNow,
            ConnectedAt: DateTimeOffset.UtcNow,
            IsMuted: false,
            IsRecording: false,
            Codec: "PCMU",
            Encryption: MediaEncryptionMode.None);

    /// <summary>Eine Attrappe des Kontextdienstes — drei Zeilen, das ist der Zweck der Schnittstelle.</summary>
    private sealed class Quellen : ICallContextSnapshots
    {
        private readonly Dictionary<CallHandle, ContextSnapshot> _stand = [];

        public ContextSnapshot? SnapshotFor(CallHandle handle) =>
            _stand.TryGetValue(handle, out var s) ? s : null;

        public event EventHandler<ContextSnapshot>? ContextChanged;

        /// <summary>Eine Quelle hat geantwortet.</summary>
        public void Melde(CallHandle handle, string feld, string wert)
        {
            var schnappschuss = new ContextSnapshot(
                handle,
                PhoneNumberKey.None,
                new Dictionary<string, ContextFragment>(StringComparer.Ordinal)
                {
                    ["crm"] = new ContextFragment(
                        "crm",
                        "Das CRM",
                        SourceState.Success,
                        new Dictionary<string, ContextValue>(StringComparer.Ordinal)
                        {
                            [feld] = ContextValue.FromText(wert),
                        },
                        Priority: 0),
                });

            _stand[handle] = schnappschuss;
            ContextChanged?.Invoke(this, schnappschuss);
        }
    }

    /// <summary>
    /// <b>Der Test, wegen dem es diesen Dienst gibt.</b> Er wird rot, sobald
    /// jemand die Auflösung wieder an die Richtung des Anrufs bindet.
    /// </summary>
    [Fact]
    public void Ein_ausgehender_Anruf_an_eine_bekannte_Nummer_zeigt_den_Namen()
    {
        var auf = new CallPartyResolver(new ClipResolver(Kontakte(new TeamExtension("Anna", "201"))));

        var anruf = Anruf(CallDirection.Outgoing, "201");

        Assert.Equal("Anna", auf.NameOf(anruf));
        Assert.Equal("Anna", auf.Describe(anruf));
    }

    /// <summary>
    /// <b>Eine Nummer ist kein Name.</b> Die Regel stand bisher nur als
    /// Kommentar im Code — und genau darunter wurde sie verletzt.
    /// </summary>
    [Fact]
    public void Eine_unbekannte_Nummer_wird_nicht_zum_Titel()
    {
        var auf = new CallPartyResolver(new ClipResolver(Kontakte()));

        var anruf = Anruf(CallDirection.Outgoing, "0791234567");

        Assert.Null(auf.NameOf(anruf));

        // Die Zeile bleibt trotzdem gefuellt — ohne Nummer kann niemand
        // zurueckrufen.
        Assert.NotEmpty(auf.Describe(anruf));
    }

    /// <summary>Ohne Nummer und ohne Namen steht trotzdem etwas da.</summary>
    [Fact]
    public void Ohne_alles_bleibt_die_Zeile_gefuellt()
    {
        var auf = new CallPartyResolver(new ClipResolver(Kontakte()));

        Assert.Equal("Unbekannt", auf.Describe(Anruf(number: "")));
    }

    /// <summary>Was nur das Fremdsystem weiss, erreicht die Kopfzeile.</summary>
    [Fact]
    public void Der_Name_einer_externen_Quelle_erreicht_den_Titel()
    {
        var quellen = new Quellen();
        var auf = new CallPartyResolver(new ClipResolver(Kontakte()), quellen);

        var anruf = Anruf(CallDirection.Outgoing, "0791234567");

        quellen.Melde(anruf.Handle, "contactName", "Beat Muster");

        Assert.Equal("Beat Muster", auf.NameOf(anruf));
    }

    /// <summary>
    /// Der lokale Kontakt gewinnt — <b>Telefonieren hängt von keiner Quelle
    /// ab</b> (§21.2), und wer im eigenen Adressbuch steht, soll so heissen,
    /// wie er dort steht.
    /// </summary>
    [Fact]
    public void Der_lokale_Kontakt_schlaegt_die_externe_Quelle()
    {
        var quellen = new Quellen();
        var auf = new CallPartyResolver(
            new ClipResolver(Kontakte(new TeamExtension("Anna", "201"))),
            quellen);

        var anruf = Anruf(CallDirection.Outgoing, "201");

        quellen.Melde(anruf.Handle, "contactName", "Nebenstelle 201");

        Assert.Equal("Anna", auf.NameOf(anruf));
    }

    /// <summary>
    /// Manche Anlagen schicken als Anzeigenamen die Nummer noch einmal. Dann
    /// stünde sie als „Name" im Kopf.
    /// </summary>
    [Fact]
    public void Der_Anzeigename_aus_der_Signalisierung_zaehlt_nur_wenn_er_keine_Nummer_ist()
    {
        var auf = new CallPartyResolver(new ClipResolver(Kontakte()));

        Assert.Null(auf.NameOf(Anruf(number: "0791234567", displayName: "079 123 45 67")));
        Assert.Equal("Empfang", auf.NameOf(Anruf(number: "0791234567", displayName: "Empfang")));
    }

    /// <summary>
    /// <b>Einmal je Gespräch.</b> Die Auflösung läuft über alle Kontakte, auf
    /// dem Thread, der alle 20 ms <c>Core.Iterate()</c> bedient — und diese
    /// Zeile wird bei jedem Zustandswechsel neu aufgebaut.
    /// </summary>
    [Fact]
    public void Dieselbe_Nummer_wird_je_Gespraech_nur_einmal_aufgeloest()
    {
        var kontakte = Kontakte(new TeamExtension("Anna", "201"));
        var auf = new CallPartyResolver(new ClipResolver(kontakte));

        var anruf = Anruf(CallDirection.Outgoing, "201");

        Assert.Equal("Anna", auf.NameOf(anruf));

        // Der Speicher wird leergeraeumt. Haette der Auflöser nicht gemerkt,
        // was er fand, stuende hier ab jetzt nichts mehr.
        kontakte.ReplaceTeam([]);

        Assert.Equal("Anna", auf.NameOf(anruf));

        // Ein anderes Gespraech an dieselbe Nummer fragt neu — sonst zeigte
        // der naechste Anruf einen Namen aus einem Adressbuch, das sich
        // inzwischen geaendert haben kann.
        Assert.Null(auf.NameOf(Anruf(CallDirection.Outgoing, "201")));
    }

    /// <summary>
    /// Die Antwort des Fremdsystems kommt nach dem letzten Zustandswechsel —
    /// ohne diese Meldung bliebe in Leiste, Liste und Infobereich die Nummer
    /// stehen.
    /// </summary>
    [Fact]
    public void Eine_spaete_Antwort_meldet_sich()
    {
        var quellen = new Quellen();
        var auf = new CallPartyResolver(new ClipResolver(Kontakte()), quellen);

        var anruf = Anruf(CallDirection.Outgoing, "0791234567");

        // Erst fragen — der Auflöser meldet nur zu Gespraechen, nach denen
        // jemand gefragt hat.
        Assert.Null(auf.NameOf(anruf));

        var gemeldet = new List<CallHandle>();
        auf.PartyChanged += (_, h) => gemeldet.Add(h);

        quellen.Melde(anruf.Handle, "contactName", "Beat Muster");

        Assert.Equal([anruf.Handle], gemeldet);

        // Nur bei echter Aenderung: jede Quellenantwort veroeffentlicht einen
        // Schnappschuss, und ohne die Pruefung zeichnete die Oberflaeche vier
        // Mal je Anruf neu.
        quellen.Melde(anruf.Handle, "contactName", "Beat Muster");

        Assert.Single(gemeldet);
    }

    /// <summary>
    /// Kennt der lokale Kontakt den Namen schon, ändert eine spätere Antwort
    /// nichts — und meldet auch nichts.
    /// </summary>
    [Fact]
    public void Ein_lokaler_Treffer_laesst_sich_von_der_Quelle_nicht_umstossen()
    {
        var quellen = new Quellen();
        var auf = new CallPartyResolver(
            new ClipResolver(Kontakte(new TeamExtension("Anna", "201"))),
            quellen);

        var anruf = Anruf(CallDirection.Outgoing, "201");

        Assert.Equal("Anna", auf.NameOf(anruf));

        var gemeldet = 0;
        auf.PartyChanged += (_, _) => gemeldet++;

        quellen.Melde(anruf.Handle, "contactName", "Nebenstelle 201");

        Assert.Equal(0, gemeldet);
        Assert.Equal("Anna", auf.NameOf(anruf));
    }

    public void Dispose()
    {
        if (Directory.Exists(_verzeichnis))
        {
            Directory.Delete(_verzeichnis, recursive: true);
        }
    }
}
