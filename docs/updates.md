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
installierten Arbeitsplätze lesen.

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
| Anrufliste | `%LOCALAPPDATA%\nipp\history.db` |
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

## Das Token, solange das Repo privat ist

Das Repo ist privat (`docs/licensing.md`), und ein Release-Asset dort ist ohne
Anmeldung nicht abrufbar. Die installierte App braucht deshalb ein Token.

**Es steht nicht im Quelltext.** Es kommt über die Provisionierung an den
Arbeitsplatz und liegt über DPAPI im `SecretStore` — derselbe Weg wie das
SIP-Passwort:

```xml
<!-- in nipp-factory.xml oder im Kundenprofil -->
<set path="update.token" value="github_pat_…" />
<set path="update.channel" value="stable" />
<set path="update.check-on-start" value="true" />
```

`update.token` wird **nur aus einer vertrauenswürdigen Quelle** übernommen —
also aus der mitgelieferten Datei, nicht aus einem Profil, das über das Netz
kommt. Wer das Token aus der Ferne setzen könnte, bestimmte damit, aus welchem
Repo dieser Arbeitsplatz seine nächste Fassung bezieht.

**Anforderungen an das Token:** fein granuliert, `Contents: read`, **nur dieses
eine Repo**. Fehlt es, ist die Update-Prüfung still aus.

**Für zehn interne Arbeitsplätze ist das tragbar. Für eine Kundenverteilung
nicht** — ein Token, das auf jedem Rechner liegt, ist ein Token, das jeder
auslesen kann. Bis dahin ist entweder das Repo öffentlich (`docs/plans/RELEASE-PLAN.md`
R10, dann fällt das Token ersatzlos weg) oder der Feed liegt auf einem
bv2-Webserver.

**Keine Abkürzung:** Code privat und ein zweites Repo `nipp-releases` öffentlich
für die Pakete wäre die Binärdatei an jeden und der Quelltext an niemanden —
genau der Fall, für den es die AGPL gibt. Entweder beides zu oder beides offen.

---

## Ein Release veröffentlichen

1. **Version festlegen.** Dreiteilig nach SemVer (`0.9.0`), für beta mit Suffix
   (`0.9.0-beta.1`). Anders als bei MSIX **nicht** vierteilig.
2. **Erst beta.** Auf mindestens einem echten Arbeitsplatz laufen lassen, nicht
   nur starten — ein Gespräch führen, einen Anruf annehmen, die Anrufliste
   ansehen.
3. **Dann stable**, mit derselben Versionsnummer ohne Suffix oder der nächsten.
4. **Der Release-Text nennt den Tag** und was sich geändert hat.

Solange die Lizenzfrage nicht entschieden ist, ist **jeder Release ausdrücklich
als intern zu markieren** (§12, M8). Der Satz gehört in den Release-Text.

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
