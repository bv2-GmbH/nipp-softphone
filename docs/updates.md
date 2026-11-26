# Releases und Updates

Wie aus dem Quellcode ein Setup wird, wie es an die Arbeitsplätze kommt und was
zu tun ist, wenn eine Fassung sich als kaputt herausstellt.

Entschieden in **ADR-038** (Velopack statt MSIX) und **ADR-039** (Update über
GitHub, Kanäle, Prüfung beim Start). Der Plan dahinter: `docs/plans/RELEASE-PLAN.md`.

---

## Kurzfassung

```powershell
# Bauen, ohne zu veröffentlichen
.\build\Release-Nipp.ps1 -Version 0.9.0 -Channel stable

# Bauen und nach GitHub laden (Token in GITHUB_TOKEN)
.\build\Release-Nipp.ps1 -Version 0.9.0-beta.1 -Channel beta -Publish
```

Das Ergebnis liegt unter `dist\releases\`: `nipp-win-<kanal>-Setup.exe`, das
Voll-Paket, ein Delta-Paket und `releases.win-<kanal>.json` — der Feed, den die
installierten Arbeitsplätze lesen. Das Delta entsteht nur, wenn das vorherige
Release vorher aus GitHub geholt werden konnte (siehe «Delta-Pakete»).

Derselbe Weg läuft in GitHub Actions (`.github/workflows/release.yml`, von Hand
angestossen mit Version und Kanal). Zwei Wege zum Release wären zwei
Gelegenheiten, sie auseinanderlaufen zu lassen — deshalb ruft der Workflow
dasselbe Skript auf.

---

## Warum nicht MSIX

MSIX ist gebaut und bleibt es (`Pack-Nipp.ps1`, `docs/packaging.md`), wird aber
nicht ausgeliefert. Zwei Gründe:

1. **T110 ist offen** — packaged bekommt keine Toasts. Ohne Toast ist §8.6 nicht
   erfüllt, und weil nipp im Infobereich lebt, gäbe ein eingehender Anruf bei
   geschlossenem Fenster kein Zeichen.
2. **Ohne echtes Zertifikat lässt sich ein MSIX gar nicht installieren.** Bei
   einer Setup.exe ist ein fehlendes Zertifikat eine hässliche Warnung, bei
   MSIX ein hartes Nein.

Sobald T110 gelöst und AP9.2 beschafft ist, ist MSIX für Intune-Umgebungen
(§16.3) wieder die bessere Wahl.

---

## Was der Installer anlegt

```
%LocalAppData%\nipp\
    Nipp.App.exe          <- Stub, bleibt über alle Updates gleich
    Update.exe
    current\              <- der Inhalt wird beim Update ersetzt
        Nipp.App.exe
        liblinphone.dll, …
```

**Keine Adminrechte**, keine Voraussetzungen auf dem Zielrechner: .NET 8 und das
Windows App SDK sind mit im Paket (self-contained). Das kostet Grösse — rund
345 MB im Installordner, das Setup deutlich weniger — und spart dafür die
Fehlerklasse „startet nicht und sagt nicht warum".

**Verknüpfungen** entstehen auf dem Desktop und im Startmenü.

**Benutzerdaten liegen ausserhalb** und überleben jedes Update:

| Was | Wo |
|---|---|
| Einstellungen | `%APPDATA%\nipp\settings.json` |
| Anrufliste, Protokolle, Aufnahmen | `%LOCALAPPDATA%\bv2\nipp\` — **seit ADR-079 unter `bv2\`**, und das ist der Grund: Velopack installiert mit `packId nipp` nach `%LOCALAPPDATA%\nipp`, also genau dorthin, wo die Daten vorher lagen. Ein Setup fand dann sein eigenes Verzeichnis vor und fragte, ob es überschreiben solle |
| Geheimnisse (SIP, Integrationen, Update-Token) | DPAPI-Ablage des Benutzers |
| Auslieferungszustand | `%PROGRAMDATA%\bv2\nipp\nipp-factory.xml` |

**Autostart und die Protokolle `tel:`, `sip:`, `callto:`** zeigen auf den
**Stub**, nicht in den `current`-Ordner. Sonst zeigte der Eintrag während eines
Updates auf eine Datei, die gerade ersetzt wird (`WindowsIntegration.ResolveExecutablePath`,
geprüft in `ExecutablePathTests`, am Gerät T122).

---

## Die zwei Kanäle

| Kanal | Feed | Für wen |
|---|---|---|
| **stable** | `releases.win-stable.json` | alle Arbeitsplätze |
| **beta** | `releases.win-beta.json` | wer Neues früher sehen will |

Sie sind **getrennte Feeds**. Eine installierte Fassung sucht in dem Kanal
weiter, in dem sie steht; gewechselt wird in den Einstellungen unter
„Aktualisierung".

**Der Rückweg von beta nach stable ist ein Downgrade** — auf eine niedrigere
Versionsnummer. Er ist ausdrücklich erlaubt (`AllowVersionDowngrade`), sonst
wäre beta eine Einbahnstrasse, aus der man nur durch Neuinstallation
herauskäme.

**Beta-Releases tragen auf GitHub das Vorabversions-Kennzeichen** (`--pre`), und
die App sucht im Beta-Kanal ausdrücklich danach. Wer ein Beta-Release ohne
dieses Kennzeichen hochlädt, baut einen Feed, den niemand findet.

---

## Der Ablauf beim Benutzer

1. **Beim Start wird nachgesehen** — zuletzt, nachdem Anmeldung und Fenster
   stehen, auf einem eigenen Thread. Ein Softphone, dessen Start von GitHub
   abhängt, ist kaputt gebaut.
2. **Gefundenes steht als ruhige Zeile** in den Einstellungen. Kein Dialog,
   kein Toast — der Toast ist bei nipp die Anrufmeldung.
3. **Geladen wird auf Knopfdruck.** Nichts wird still heruntergeladen.
4. **Angewandt wird auf Knopfdruck**, und **nie, solange ein Gespräch läuft**.
   Der Knopf ist dann abgeblendet, mit dem Grund daneben.
5. **Fehler sind still**: Protokollzeile, Zustand „nicht möglich", der nächste
   Start versucht es wieder. Kein Netz ist der Normalfall, nicht die Ausnahme.

Abschalten lässt sich die Prüfung in den Einstellungen. „Aus" heisst: nicht von
selbst — der Knopf „Jetzt prüfen" fragt trotzdem.

---

## Das Token — und wann es nicht mehr gebraucht wird

**Seit dem 14.09.2026 ist das Repo öffentlich**
(`github.com/bv2-GmbH/nipp-softphone`), und ein Release-Asset dort ist ohne
Anmeldung abrufbar (HTTP 200, am selben Tag geprüft). **Die installierte App
braucht für den Abruf kein Token mehr** — ab 0.9.6 nicht, und eine frisch
installierte Fassung kommt ohne eines aus. Die Update-Prüfung ist auch ohne
Token nicht still aus.

**Was ein Token weiterhin tut:**

- **Hochladen** braucht Schreibrecht auf Releases. Im Workflow
  (`.github/workflows/release.yml`) ist das das `GITHUB_TOKEN` des Laufs, von
  Hand `GITHUB_TOKEN` oder `-Token` bei `Release-Nipp.ps1 -Publish`. Es gehört
  nicht in den Quelltext und nicht an einen Arbeitsplatz.
- **Lesen** geht ohne. Das Skript reicht ein vorhandenes Token beim Holen des
  vorherigen Releases trotzdem durch (siehe «Delta-Pakete»), weil es gegen das
  Abruflimit für anonyme Anfragen hilft — nötig ist es dafür nicht.

**Ab 0.9.18 gibt es kein Update-Token mehr (ADR-083, 26.11.2026).** Dominic
hat am 26.11.2026 bestätigt, dass kein Arbeitsplatz mehr vor 0.9.6 steht;
damit zieht niemand mehr die Brücke 0.9.5 aus dem archivierten, privaten Repo
`bv2-GmbH/nipp` (`docs/plans/OEFFENTLICH-PLAN.md`, Ö6). Seither:

- **nipp sendet nie ein Token** an GitHub, auch wenn noch eines gespeichert ist.
- **Ein gespeichertes Token wird beim Start gelöscht**, einmalig, mit einer
  Protokollzeile ohne den Wert.
- **`update.token` in einer `nipp-factory.xml` oder einem Profil wird
  angenommen und ignoriert** — kein Fehler, eine Protokollzeile «wird nicht mehr
  gebraucht». `nippprov pruefen` weist darauf hin; der Eintrag kann bei
  Gelegenheit aus den Dateien raus.

```xml
<!-- was in der Provisionierung zu Updates bleibt -->
<set path="update.channel" value="stable" />
<set path="update.check-on-start" value="true" />
```

**Die Reihenfolge ist zwingend:** erst muss 0.9.18 auf den Arbeitsplätzen
angekommen sein, **dann** wird das Token auf GitHub widerrufen. Fassungen bis
0.9.17 schicken ein gespeichertes Token noch mit — ist es widerrufen,
antwortet GitHub auch für ein öffentliches Repo mit 401, und die
Update-Prüfung bleibt still stehen. Prüfzeile T345.

**So ist es gelaufen (26.11.2026):** 0.9.18 erschienen, alle Arbeitsplätze
aktualisiert, T345 bestanden — **danach das PAT widerrufen.** Ein Update-Token
gibt es seither nicht mehr. `update.token` in alten Factory-Dateien schadet
nicht und kann bei Gelegenheit raus.

**Keine Abkürzung gilt weiter:** Code privat und ein zweites Repo für die
Pakete öffentlich wäre die Binärdatei an jeden und der Quelltext an niemanden —
genau der Fall, für den es die AGPL gibt. Deshalb liegt beides offen am selben
Ort (`docs/licensing.md`).

---

## Delta-Pakete

Ein Release trägt neben dem Voll-Paket ein Delta-Paket (rund 1,7 MB statt
148 MB), und die installierten Arbeitsplätze laden bevorzugt dieses.

**Velopack bildet ein Delta nur, wenn das vorherige Release beim Paketieren im
Ausgabeordner liegt.** Auf der Entwicklungsmaschine blieb `dist\releases` von
Lauf zu Lauf stehen; 0.9.15 und 0.9.16 hatten deshalb ein Delta. In GitHub
Actions beginnt jeder Lauf mit einem leeren Ordner, und 0.9.17 — das erste
Release aus dem Workflow — erschien ohne. Seither holt `Release-Nipp.ps1`
**vor** `vpk pack` das vorherige Release mit `vpk download github` — aus
demselben Repo, in das auch hochgeladen wird (`-UploadRepo`), im selben Kanal.
Scheitert der Abruf (erstes Release eines Kanals, kein Netz), baut das Skript
mit einer Warnung ohne Delta weiter: ein fehlendes Delta kostet Download-
Volumen, kein Update.

## Ein Release veröffentlichen

1. **Version festlegen.** Dreiteilig nach SemVer (`0.9.0`), für beta mit Suffix
   (`0.9.0-beta.1`). Anders als bei MSIX **nicht** vierteilig.
2. **Erst beta.** Auf mindestens einem echten Arbeitsplatz laufen lassen, nicht
   nur starten — ein Gespräch führen, einen Anruf annehmen, die Anrufliste
   ansehen.
3. **Dann stable**, mit derselben Versionsnummer ohne Suffix oder der nächsten.
4. **Der Release-Text nennt den Tag** und was sich geändert hat.

Die Lizenzfrage ist entschieden (AGPLv3, `docs/licensing.md`); ein Release
muss deshalb nicht mehr als intern markiert werden. Er ist öffentlich, sobald
er veröffentlicht ist — der Release-Text ist entsprechend zu schreiben.

---

## Wenn ein Release kaputt ist

Ein Softphone, das nicht mehr startet, ist ein Betriebsausfall. Der Rückweg:

1. **Kein Zurückziehen.** Ein gelöschtes Release hilft niemandem, der es schon
   installiert hat.
2. **Ein neues Release mit höherer Versionsnummer, das den alten Stand trägt.**
   Also: den letzten guten Commit auschecken, Version anheben (`0.9.2` nach
   einem kaputten `0.9.1`), veröffentlichen. Die Arbeitsplätze holen es beim
   nächsten Start.
3. **Wer nicht warten kann**, installiert das alte Setup neu — die
   Benutzerdaten liegen ausserhalb und bleiben.

Deshalb steht in der Reihenfolge oben „erst beta". Sie ist die einzige
Sicherung dagegen, dass eine kaputte Fassung alle Arbeitsplätze auf einmal
erreicht.

---

## Was noch offen ist

- **AP9.2, das Code-Signing-Zertifikat.** Ohne es bleibt das Setup unsigniert,
  und Windows zeigt beim ersten Start „Unbekannter Herausgeber" (T130). Intern
  ist das einmal wegzuklicken; für eine Abgabe ausser Haus ist es der Blocker.
  Beschaffung dauert Wochen.
- **Die Abnahme am Gerät**: T120 bis T133 in `docs/test-matrix.md`. Nichts
  davon ist geprüft.
- **GitHub-API-Grenze**: unauthentifiziert 60 Anfragen je Stunde und IP. Mit
  Token deutlich mehr. Bei zehn Arbeitsplätzen und einer Prüfung je Start
  unkritisch; bei hundert neu zu bewerten.
