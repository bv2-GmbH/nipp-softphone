using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Integrations.Catalog;

namespace Nipp.Core.Tests.Services.Integrations.Catalog;

/// <summary>
/// Der Import einer Anbietervorlage (ADR-040).
///
/// <para><b>Warum dieser Test der wichtigste des Arbeitspakets ist.</b> Bis
/// hierher schrieben wir die Vorlagen selbst; ab jetzt kommen sie von aussen.
/// Was der Leser <b>ablehnt</b>, ist damit keine Formsache mehr, sondern die
/// eigentliche Zusage: dass in einer Datei, die herumgereicht wird, kein
/// Zugangsschlüssel steckt und dass eine fremde Datei keine eingeschaltete
/// Quelle anlegen kann.</para>
/// </summary>
public sealed class ConnectorImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nipp-tests",
        Guid.NewGuid().ToString("N"));

    private ConnectorLibrary Bibliothek()
    {
        var bibliothek = new ConnectorLibrary(
            NullLogger<ConnectorLibrary>.Instance,
            Path.Combine(_directory, "connectors"));

        bibliothek.Reload();

        return bibliothek;
    }

    private const string Gut = """
        {
          "kind": "nippConnectorTemplate",
          "schemaVersion": 1,
          "id": "fremd",
          "displayName": "Fremdsystem",
          "summary": "Eine Quelle von jemand anderem.",
          "vendor": "Fremd AG",
          "secrets": [ { "ref": "fremd", "label": "API-Token", "hint": "Im Fremdsystem unter Profil." } ],
          "source": {
            "id": "fremd",
            "displayName": "Fremdsystem",
            "type": "http",
            "enabled": false,
            "http": {
              "baseUrl": "https://api.fremd.example/v1",
              "auth": { "type": "bearer", "secretRef": "fremd" }
            },
            "lookupByPhone": {
              "request": { "method": "GET", "path": "/by-phone", "query": { "p": "{{number.e164}}" } },
              "mapping": { "contactName": { "path": "$.name" } }
            }
          }
        }
        """;

    [Fact]
    public void Eine_gute_Vorlage_kommt_herein_und_ueberlebt_den_Neustart()
    {
        var bibliothek = Bibliothek();

        Assert.True(bibliothek.TryImport(Gut, replaceExisting: false, out var vorlage, out var fehler), fehler);
        Assert.Equal("fremd", vorlage!.Id);
        Assert.Equal(ConnectorOrigin.Imported, vorlage.Origin);
        Assert.Equal("Fremd AG", vorlage.Vendor);

        // Eine zweite Bibliothek auf demselben Ordner findet sie wieder.
        Assert.NotNull(Bibliothek().Find("fremd"));
    }

    /// <summary>
    /// <b>Ein Import legt keine Quelle an.</b> Die Vorlage bleibt liegen, und
    /// eingerichtet wird über „Quelle hinzufügen" — sonst stünde im
    /// Zugangsdatenformular wieder das generische „API-Token" statt der
    /// Herkunft, und dieselbe Vorlage wäre für einen zweiten Mandanten nicht
    /// noch einmal anwendbar.
    /// </summary>
    [Fact]
    public void Ein_Import_legt_keine_Quelle_an()
    {
        var bibliothek = Bibliothek();

        Assert.True(bibliothek.TryImport(Gut, replaceExisting: false, out var vorlage, out _));

        // Die Vorlage ist da …
        Assert.NotNull(bibliothek.Find("fremd"));

        // … und sie ist abgeschaltet. Eingeschaltet wird nach dem Testabruf.
        Assert.False(vorlage!.Source.Enabled);
    }

    /// <summary>
    /// <b>Eine Vorlage wird nie eingeschaltet ausgeliefert</b> — auch nicht,
    /// wenn die Datei es behauptet. Der Leser normalisiert darauf.
    /// </summary>
    [Fact]
    public void Eine_eingeschaltete_Vorlage_kommt_abgeschaltet_herein()
    {
        var eingeschaltet = Gut.Replace("\"enabled\": false", "\"enabled\": true", StringComparison.Ordinal);

        Assert.True(Bibliothek().TryImport(eingeschaltet, replaceExisting: false, out var vorlage, out _));
        Assert.False(vorlage!.Source.Enabled);
    }

    /// <summary>
    /// <b>Ein Geheimnis in der Datei ist ein Ablehnungsgrund.</b> Bei eigenen
    /// Dateien war das ein Test; bei fremden muss es der Leser können —
    /// System.Text.Json schluckt ein unbekanntes <c>token</c> sonst still, und
    /// dann läge ein Zugangsschlüssel in einer Datei, die weitergegeben wird.
    /// </summary>
    [Fact]
    public void Eine_Vorlage_mit_einem_Geheimnis_wird_abgelehnt()
    {
        var mitToken = Gut.Replace(
            "\"auth\": { \"type\": \"bearer\", \"secretRef\": \"fremd\" }",
            "\"auth\": { \"type\": \"bearer\", \"secretRef\": \"fremd\", \"token\": \"abc123geheim\" }",
            StringComparison.Ordinal);

        Assert.False(Bibliothek().TryImport(mitToken, replaceExisting: false, out _, out var fehler));
        Assert.Contains("Geheimnis", fehler!, StringComparison.Ordinal);
    }

    [Fact]
    public void Ein_ueberlanger_Verweis_wird_abgelehnt()
    {
        var langerVerweis = Gut.Replace(
            "\"ref\": \"fremd\"",
            $"\"ref\": \"{new string('x', 60)}\"",
            StringComparison.Ordinal);

        Assert.False(Bibliothek().TryImport(langerVerweis, replaceExisting: false, out _, out var fehler));
        Assert.Contains("Verweis", fehler!, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Der wahrscheinlichste Fehlgriff verdient eine eigene Meldung:</b>
    /// jemand wählt seine <c>integrations.json</c> aus.
    /// </summary>
    [Fact]
    public void Eine_nipp_Konfiguration_bekommt_eine_eigene_Meldung()
    {
        const string Konfiguration = """
            { "schemaVersion": 1, "dataSources": [ { "id": "crm", "type": "http" } ] }
            """;

        Assert.False(Bibliothek().TryImport(Konfiguration, replaceExisting: false, out _, out var fehler));
        Assert.Contains("Einlesen", fehler!, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_unbekannte_Fassung_wird_abgelehnt_statt_versucht()
    {
        var neuer = Gut.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 7", StringComparison.Ordinal);

        Assert.False(Bibliothek().TryImport(neuer, replaceExisting: false, out _, out var fehler));
        Assert.Contains("Fassung", fehler!, StringComparison.Ordinal);
    }

    [Fact]
    public void Eine_Datei_ohne_kind_ist_keine_Vorlage()
    {
        var ohneKind = Gut.Replace("\"kind\": \"nippConnectorTemplate\",", string.Empty, StringComparison.Ordinal);

        Assert.False(Bibliothek().TryImport(ohneKind, replaceExisting: false, out _, out var fehler));
        Assert.Contains("kind", fehler!, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>Eine kaputte Datei ändert nichts.</b> Was schon da war, steht
    /// danach unverändert da — ein halb eingelesener Katalog wäre schlimmer
    /// als ein abgelehnter Import.
    /// </summary>
    [Fact]
    public void Eine_kaputte_Datei_aendert_nichts()
    {
        var bibliothek = Bibliothek();

        Assert.True(bibliothek.TryImport(Gut, replaceExisting: false, out _, out _));

        var vorher = bibliothek.Templates.Select(static t => t.Id).ToList();

        Assert.False(bibliothek.TryImport("{ das ist kein json", replaceExisting: false, out _, out var fehler));
        Assert.NotNull(fehler);

        Assert.Equal(vorher, bibliothek.Templates.Select(static t => t.Id));
    }

    [Fact]
    public void Dieselbe_Kennung_zweimal_fragt_nach()
    {
        var bibliothek = Bibliothek();

        Assert.True(bibliothek.TryImport(Gut, replaceExisting: false, out _, out _));
        Assert.False(bibliothek.TryImport(Gut, replaceExisting: false, out _, out var fehler));
        Assert.Contains("schon da", fehler!, StringComparison.Ordinal);

        // Mit ausdrücklicher Zustimmung geht es.
        var neuerName = Gut.Replace("\"Fremdsystem\"", "\"Fremdsystem (neu)\"", StringComparison.Ordinal);

        Assert.True(bibliothek.TryImport(neuerName, replaceExisting: true, out var ersetzt, out _));
        Assert.Equal("Fremdsystem (neu)", ersetzt!.DisplayName);
        Assert.Single(bibliothek.Templates, static t => t.Id == "fremd");
    }

    /// <summary>
    /// Die mitgelieferte Vorlage lässt sich nicht überschreiben: sie ist
    /// eingebettet, eine Datei gleicher Kennung daneben wäre eine zweite
    /// Wahrheit.
    /// </summary>
    [Fact]
    public void Die_mitgelieferte_Vorlage_laesst_sich_nicht_ersetzen()
    {
        var bibliothek = Bibliothek();

        var gefaelscht = Gut.Replace("\"id\": \"fremd\"", "\"id\": \"custom-rest\"", StringComparison.Ordinal);

        Assert.False(bibliothek.TryImport(gefaelscht, replaceExisting: true, out _, out var fehler));
        Assert.Contains("mitgeliefert", fehler!, StringComparison.Ordinal);

        Assert.Equal(ConnectorOrigin.BuiltIn, bibliothek.Find("custom-rest")!.Origin);
    }

    /// <summary>
    /// <b>Die Bibliothek arbeitet nur im übergebenen Ordner.</b>
    /// <c>TestIsolationTests</c> erzwingt den Konstruktorparameter, und der
    /// Grund steht im Projekt fest: ein <c>dotnet test</c> hat hier schon
    /// einmal die Konfiguration des angemeldeten Benutzers angefasst.
    /// </summary>
    [Fact]
    public void Sie_arbeitet_nur_im_uebergebenen_Ordner()
    {
        var ordner = Path.Combine(_directory, "connectors");
        var bibliothek = new ConnectorLibrary(NullLogger<ConnectorLibrary>.Instance, ordner);

        Assert.Equal(ordner, bibliothek.Directory);
        Assert.NotEqual(ConnectorLibrary.DefaultDirectory, bibliothek.Directory);

        bibliothek.TryImport(Gut, replaceExisting: false, out _, out _);

        Assert.True(File.Exists(Path.Combine(ordner, "fremd.json")));
    }

    [Fact]
    public void Eine_importierte_Vorlage_laesst_sich_entfernen()
    {
        var bibliothek = Bibliothek();

        Assert.True(bibliothek.TryImport(Gut, replaceExisting: false, out _, out _));
        Assert.True(bibliothek.Remove("fremd"));
        Assert.Null(bibliothek.Find("fremd"));

        // Und die mitgelieferte nicht.
        Assert.False(bibliothek.Remove("custom-rest"));
        Assert.NotNull(bibliothek.Find("custom-rest"));
    }

    /// <summary>
    /// Eine Datei über der Grössengrenze wird abgelehnt: eine Vorlage
    /// beschreibt eine Schnittstelle, sie liefert keine Daten — und was hier
    /// hereinkommt, hat niemand geprüft.
    /// </summary>
    [Fact]
    public void Eine_zu_grosse_Datei_wird_abgelehnt()
    {
        var gross = Gut.Replace(
            "\"summary\": \"Eine Quelle von jemand anderem.\"",
            $"\"summary\": \"{new string('x', ConnectorTemplateReader.MaxBytes + 10)}\"",
            StringComparison.Ordinal);

        Assert.False(Bibliothek().TryImport(gross, replaceExisting: false, out _, out var fehler));
        Assert.Contains("kB", fehler!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eine unlesbare Datei im Ordner nimmt die anderen nicht mit — sie fehlt
    /// in der Liste und steht im Protokoll. Der Katalog ist Beiwerk; ohne ihn
    /// lässt sich immer noch eine Konfiguration einlesen.
    /// </summary>
    [Fact]
    public void Eine_kaputte_Datei_im_Ordner_nimmt_die_anderen_nicht_mit()
    {
        var ordner = Path.Combine(_directory, "connectors");

        Directory.CreateDirectory(ordner);
        File.WriteAllText(Path.Combine(ordner, "kaputt.json"), "{ nicht wirklich json");
        File.WriteAllText(Path.Combine(ordner, "fremd.json"), Gut);

        var bibliothek = Bibliothek();

        Assert.NotNull(bibliothek.Find("fremd"));
        Assert.NotNull(bibliothek.Find("custom-rest"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
