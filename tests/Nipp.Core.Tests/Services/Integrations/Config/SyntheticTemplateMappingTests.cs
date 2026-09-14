using System.Text.Json.Nodes;
using Nipp.Core.Services.Integrations.Catalog;
using Nipp.Core.Services.Integrations.Config;
using Nipp.Core.Services.Integrations.Mapping;

namespace Nipp.Core.Tests.Services.Integrations.Config;

/// <summary>
/// Was die Mapping-Maschine an Antwortformen beherrschen muss — geprüft gegen
/// die <b>synthetischen</b> Anbietervorlagen (ADR-040).
///
/// <para><b>Wovon diese Datei übrig ist.</b> Bis zum 11.09.2026 standen diese
/// Aussagen in <c>ExpectedResponseTests</c> und liefen gegen die Vorlagen zweier
/// echter Systeme. Die Datei prüfte dreierlei: dass die Vorlage zur vereinbarten
/// Antwortform passt, dass die Mapping-Maschine diese Formen beherrscht, und —
/// als Wächter — dass die <b>echten</b> Antworten sich nicht geändert haben.
/// Mit dem öffentlichen Repo gehen die Systemnamen, die Adressen und die
/// mitgeschnittenen Antworten weg. <b>Das Mittelstück muss bleiben</b>, und
/// hier ist es.</para>
///
/// <para><b>Was dabei verloren geht und wodurch es ersetzt ist:</b> der
/// Regressionswächter gegen die echten Endpunkte. Weicht einer künftig ab,
/// merkt es der Testabruf in den Einstellungen — also ein Mensch, nicht die
/// Pipeline. Das ist der Preis, und er steht in ADR-040.</para>
///
/// <para>Geprüft werden die Formen, an denen es beim Anbinden tatsächlich hakte:
/// verschachtelte Arrays, ein Filter auf ein Kennzeichen (<c>is_primary</c>),
/// <c>join()</c> über eine Liste, ein Bereich (<c>[1:3]</c>), Datumsformate,
/// <c>itemsPath</c> für eine Trefferliste und die Kürzung mit <c>…</c>.</para>
/// </summary>
public sealed class SyntheticTemplateMappingTests
{
    /// <summary>Lädt eine synthetische Vorlage und mappt eine Antwort damit.</summary>
    private static MappingResult Mappe(string vorlage, string antwort, bool suche = false)
    {
        Assert.True(
            ConnectorTemplateReader.TryRead(TestTemplates.Read(vorlage), out var template, out var fehler),
            fehler);

        var quelle = template!.Source;

        var mapping = suche
            ? quelle.SearchContacts!.ToMapping()
            : quelle.LookupByPhone!.ToMapping();

        Assert.True(MappingEngine.TryCompile(mapping, out var compiled, out var befunde),
            string.Join(" | ", befunde));

        var body = JsonNode.Parse(antwort);

        return suche
            ? MappingEngine.MapItems(compiled, body, limit: 10)[0]
            : MappingEngine.Map(compiled, body);
    }

    private static string Text(MappingResult result, string feld) =>
        result.Fields.TryGetValue(feld, out var value) ? value.AsText() : string.Empty;

    // --- Ein Kontakt mit Kunden und Zeiteinträgen ---

    private const string KontorByPhone = """
        {
          "id": 9566,
          "name": "Hans Muster",
          "salutation": "Herr",
          "contact_type_name": "Technischer Ansprechpartner",
          "fixnet_number": "0713142250",
          "mobile_number": "0791234567",
          "mail": "hans.muster@example.ch",
          "context_md": "Zuständig für die Standorte Ost.",
          "customers": [
            { "id": 412, "name": "Muster AG", "is_primary": true },
            { "id": 588, "name": "Muster Immobilien AG", "is_primary": false }
          ],
          "recent_time_entries": [
            {
              "id": 154600, "date": "2026-09-04", "hours": "2.50",
              "project_name": "Migration Telefonie", "customer_name": "Muster AG",
              "technician_name": "Anna Beispiel",
              "description": "Anlage konfiguriert, Test mit Kunde"
            },
            {
              "id": 154500, "date": "2026-08-28", "hours": "1.00",
              "project_name": "Wartung", "customer_name": "Muster AG",
              "technician_name": "Anna Beispiel", "description": "Updates"
            },
            {
              "id": 154400, "date": "2026-08-14", "hours": "3.25",
              "project_name": "Netzwerk", "customer_name": "Muster Immobilien AG",
              "technician_name": "Beat Beispiel", "description": "Switch getauscht"
            }
          ]
        }
        """;

    [Fact]
    public void Kontaktangaben_kommen_an()
    {
        var ergebnis = Mappe("musterkontor", KontorByPhone);

        Assert.Equal("Hans Muster", Text(ergebnis, "contactName"));
        Assert.Equal("Technischer Ansprechpartner", Text(ergebnis, "contactType"));
        Assert.Equal("hans.muster@example.ch", Text(ergebnis, "email"));
        Assert.Equal("Zuständig für die Standorte Ost.", Text(ergebnis, "notiz"));
    }

    /// <summary>
    /// Ein Kontakt kann mehreren Kunden zugeordnet sein. Auf der Karte steht
    /// der Hauptkunde, darunter der Hinweis auf die übrigen — <b>der Filter auf
    /// <c>is_primary</c></b> ist die Form, um die es hier geht.
    /// </summary>
    [Fact]
    public void Die_Kunden_werden_mit_vollem_Namen_aufgeloest()
    {
        var ergebnis = Mappe("musterkontor", KontorByPhone);

        Assert.Equal("Muster AG", Text(ergebnis, "primaryCustomer"));
        Assert.Equal("Muster AG", Text(ergebnis, "company"));
        Assert.Equal("2", Text(ergebnis, "customerCount"));
        Assert.Equal("und 1 weitere", Text(ergebnis, "weitereKunden"));
    }

    [Fact]
    public void Ohne_Hauptkunden_gelten_alle_Kunden()
    {
        const string OhneHauptkunde = """
            {
              "name": "Hans Muster",
              "customers": [ { "id": 1, "name": "Erste AG", "is_primary": false },
                             { "id": 2, "name": "Zweite AG", "is_primary": false } ],
              "recent_time_entries": []
            }
            """;

        var ergebnis = Mappe("musterkontor", OhneHauptkunde);

        Assert.Equal("Erste AG, Zweite AG", Text(ergebnis, "company"));
    }

    /// <summary>
    /// Die Frage, die der jüngste Eintrag beantwortet: woran haben wir zuletzt
    /// gearbeitet? Datum, Projekt und wer — mehr hat auf einer Zeile nicht
    /// Platz. Hier hängt zusätzlich das <b>Datumsformat</b> dran.
    /// </summary>
    [Fact]
    public void Der_juengste_Eintrag_wird_zu_einer_Zeile()
    {
        var ergebnis = Mappe("musterkontor", KontorByPhone);

        Assert.Equal("Anlage konfiguriert, Test mit Kunde", Text(ergebnis, "letzteArbeit"));
        Assert.Equal("04.09. · Migration Telefonie · Anna Beispiel", Text(ergebnis, "letzteArbeitZeile"));
    }

    /// <summary>Ein Bereich aus einem Array — <c>[1:3]</c> — und <c>join()</c>.</summary>
    [Fact]
    public void Die_beiden_vorherigen_Eintraege_stehen_als_zweite_Zeile()
    {
        var ergebnis = Mappe("musterkontor", KontorByPhone);

        Assert.Equal("Wartung · Netzwerk", Text(ergebnis, "vorherigeZeile"));
    }

    /// <summary>
    /// Ein Kontakt ohne Einträge ist der Normalfall bei einem neuen Kunden —
    /// die Zeile verschwindet dann, statt leer dazustehen.
    /// </summary>
    [Fact]
    public void Ohne_Eintraege_bleiben_die_Zeilen_leer()
    {
        const string Ohne = """
            { "name": "Neu Kunde", "customers": [], "recent_time_entries": [] }
            """;

        var ergebnis = Mappe("musterkontor", Ohne);

        Assert.Empty(Text(ergebnis, "letzteArbeitZeile"));
        Assert.Empty(Text(ergebnis, "vorherigeZeile"));
        Assert.Equal("Neu Kunde", Text(ergebnis, "contactName"));
    }

    // --- Die Trefferliste einer Suche: itemsPath ---

    [Fact]
    public void Die_Suche_verarbeitet_eine_Trefferliste()
    {
        const string Treffer = """
            {
              "count": 1, "next": null, "previous": null,
              "results": [
                {
                  "id": 9566, "salutation": "", "name": "Muster AG",
                  "contact_type": null, "contact_type_name": null,
                  "fixnet_number": "0713142250", "mobile_number": "",
                  "mail": "", "primary_customer": null,
                  "primary_customer_name": "Muster Holding",
                  "customer_ids": [], "customer_names": [],
                  "context_md": "", "context_updated_at": null
                }
              ]
            }
            """;

        var ergebnis = Mappe("musterkontor", Treffer, suche: true);

        Assert.Equal("Muster AG", Text(ergebnis, "displayName"));
        Assert.Equal("Muster Holding", Text(ergebnis, "company"));
        Assert.Equal("0713142250", Text(ergebnis, "phoneBusiness"));
        Assert.Equal("9566", Text(ergebnis, "externalId"));

        // Leere Felder bleiben leer, nicht "null" oder "".
        Assert.Empty(Text(ergebnis, "phoneMobile"));
    }

    // --- Ein verschachteltes Objekt mit Listen darin ---

    private const string JournalProfile = """
        {
          "phone_number": "+41791234567",
          "display_name": "Hans Muster",
          "total_calls": 6,
          "last_call": "2026-09-04T14:56:05Z",
          "common_categories": ["Terminvereinbarung", "Support", "Rückruf"],
          "common_topics": ["Glasfaser", "Migration", "Rückruf", "Termin", "Umzug"],
          "sentiment": "neutral",
          "sentiment_score": 0.1,
          "sentiment_direction": "declining",
          "last_call_detail": {
            "call_id": "2789bc85",
            "timestamp": "2026-09-04T14:56:05Z",
            "direction": "inbound",
            "duration_seconds": 61,
            "category": "Rückruf",
            "sentiment": "neutral",
            "summary": "Der Anrufer möchte einen Sachverhalt besprechen. Rückruf vereinbart.",
            "summary_short": "Rückruf zur Anlage vereinbart.",
            "outcome": "Rückruf wurde vereinbart und akzeptiert.",
            "todos": ["Rückruf am Montag", "Unterlagen bereitstellen"]
          },
          "notes": ""
        }
        """;

    [Fact]
    public void Die_Zusammenfassung_des_letzten_Gespraechs_kommt_an()
    {
        var ergebnis = Mappe("gespraechsjournal", JournalProfile);

        Assert.Equal("Rückruf zur Anlage vereinbart.", Text(ergebnis, "letzteZusammenfassung"));
        Assert.Equal("Rückruf wurde vereinbart und akzeptiert.", Text(ergebnis, "letztesErgebnis"));
        Assert.Equal("Rückruf", Text(ergebnis, "letzteKategorie"));
        Assert.Equal("inbound", Text(ergebnis, "letzteRichtung"));
    }

    /// <summary>
    /// „wird schlechter" ist die Information, die zählt, bevor man abnimmt —
    /// nicht ein Verlauf aus sechs Punkten.
    /// </summary>
    [Fact]
    public void Eine_fallende_Stimmung_wird_benannt()
    {
        var ergebnis = Mappe("gespraechsjournal", JournalProfile);

        Assert.Equal("neutral — Tendenz fallend", Text(ergebnis, "stimmung"));
    }

    [Fact]
    public void Eine_stabile_Stimmung_bleibt_knapp()
    {
        var stabil = JournalProfile.Replace("\"declining\"", "\"stable\"", StringComparison.Ordinal);

        var ergebnis = Mappe("gespraechsjournal", stabil);

        Assert.Equal("neutral", Text(ergebnis, "stimmung"));
    }

    [Fact]
    public void Der_Verlauf_wird_zu_einem_Satz()
    {
        var ergebnis = Mappe("gespraechsjournal", JournalProfile);

        Assert.Equal("6 Anrufe, zuletzt 04.09.2026", Text(ergebnis, "verlauf"));
    }

    [Fact]
    public void Beim_ersten_Anruf_steht_das_auch_so_da()
    {
        var erster = JournalProfile.Replace("\"total_calls\": 6", "\"total_calls\": 1", StringComparison.Ordinal);

        var ergebnis = Mappe("gespraechsjournal", erster);

        Assert.Equal("erster Anruf", Text(ergebnis, "verlauf"));
    }

    [Fact]
    public void Offene_Aufgaben_stehen_auf_einer_Zeile()
    {
        var ergebnis = Mappe("gespraechsjournal", JournalProfile);

        Assert.Equal(
            "Rückruf am Montag · Unterlagen bereitstellen",
            Text(ergebnis, "offeneAufgabenZeile"));
    }

    [Fact]
    public void Die_Themen_bleiben_kurz()
    {
        var ergebnis = Mappe("gespraechsjournal", JournalProfile);

        Assert.Equal("Glasfaser, Migration, Rückruf, Termin, Umzug", Text(ergebnis, "themen"));
        Assert.Equal(5, Text(ergebnis, "themen").Split(", ").Length);
    }

    /// <summary>
    /// Fehlt das verschachtelte Objekt ganz, bleiben die zugehörigen Felder
    /// leer — und der Rest der Karte steht trotzdem. <b>Ein fehlendes Feld ist
    /// nicht dasselbe wie ein leeres Objekt</b>, und beides darf die übrigen
    /// Zeilen nicht mitnehmen.
    /// </summary>
    [Fact]
    public void Ohne_letztes_Gespraech_bleibt_der_Rest_brauchbar()
    {
        const string OhneDetail = """
            {
              "phone_number": "+41791234567", "display_name": "Hans Muster",
              "total_calls": 6, "last_call": "2026-09-04T14:56:05Z",
              "common_categories": ["Support"], "common_topics": ["Glasfaser"],
              "sentiment": "neutral", "notes": ""
            }
            """;

        var ergebnis = Mappe("gespraechsjournal", OhneDetail);

        Assert.Equal("Hans Muster", Text(ergebnis, "displayName"));
        Assert.Equal("6 Anrufe, zuletzt 04.09.2026", Text(ergebnis, "verlauf"));
        Assert.Empty(Text(ergebnis, "letzteZusammenfassung"));
    }

    /// <summary>
    /// <b>Der Befund, der bleibt, auch wenn das System ihn nicht mehr trägt:</b>
    /// ein Feld namens „kurze Zusammenfassung" ist manchmal nicht kurz
    /// geschrieben, sondern die lange Fassung abgeschnitten — mitten im Wort,
    /// mit einem <c>…</c> am Ende. Brauchbar bleibt es für eine Zeile in der
    /// Anrufliste; wer eine Karte darauf baut, sollte es wissen.
    /// </summary>
    [Fact]
    public void Eine_gekuerzte_Zusammenfassung_bleibt_erkennbar()
    {
        var lang = new string('x', 200);

        var antwort = JournalProfile
            .Replace(
                "\"summary\": \"Der Anrufer möchte einen Sachverhalt besprechen. Rückruf vereinbart.\"",
                $"\"summary\": \"{lang}\"",
                StringComparison.Ordinal)
            .Replace(
                "\"summary_short\": \"Rückruf zur Anlage vereinbart.\"",
                $"\"summary_short\": \"{lang[..120]}…\"",
                StringComparison.Ordinal);

        var ergebnis = Mappe("gespraechsjournal", antwort);

        var kurz = Text(ergebnis, "letzteZusammenfassung");
        var voll = Text(ergebnis, "letzteZusammenfassungLang");

        Assert.EndsWith("…", kurz, StringComparison.Ordinal);
        Assert.StartsWith(kurz[..^1], voll, StringComparison.Ordinal);
        Assert.True(kurz.Length < voll.Length);
    }
}
