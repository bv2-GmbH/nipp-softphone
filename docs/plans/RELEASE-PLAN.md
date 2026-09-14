# Installer, Updates und die Lizenz

**Angelegt am 07.09.2026 abends.** Auftrag von Dominic: ein Installer, damit nipp
weitergegeben werden kann; Updates über GitHub mit den Kanälen **stable** und
**beta**; beim Start wird nach Updates gesucht. Dazu die Frage, ob das
Public-Stellen des Repos die Lizenzfrage erledigt.

Bezug: NIPP-BUILD.md §16 Punkte 1 bis 4, M8 (§12), `docs/licensing.md`,
`docs/packaging.md`, ADR-008.

---

## Umsetzungsstand

**R0 bis R8 sind gebaut** (07.09.2026 abends). 934 Komponententests und 20
Architekturtests grün, Build ohne Warnungen. **Am Gerät ist nichts davon
abgenommen** — R9 ist der Teil, der zählt.

| Paket | Inhalt | Stand |
|---|---|---|
| R0 | Machbarkeit: self-contained unpackaged bauen und starten | **gebaut**, mit Befund (siehe unten) |
| R1 | Lizenzhygiene intern: Hinweise, Fremdlizenzen, „intern" markiert | **gebaut** |
| R2 | Eigener Einstiegspunkt, Velopack eingehängt | **gebaut** |
| R3 | Setup bauen (`vpk`), DLL-Vollständigkeit weiterhin geprüft | **gebaut** — Setup.exe 144,8 MB |
| R4 | Pfade: Autostart, `tel:`, Deinstallation, Benutzerdaten | **gebaut** |
| R5 | Update-Dienst im Kern, Kanalwahl, Prüfung beim Start | **gebaut** |
| R6 | Oberfläche: „Version / Update prüfen" (§9) | **gebaut** |
| R7 | Der Weg zu GitHub: Release-Skript und CI | **gebaut und gelaufen** — der erste CI-Lauf fand sofort etwas, das hier niemand sehen konnte (siehe unten). Der Upload braucht noch ein Token |
| R8 | Dokumentation, ADRs, CLAUDE.md | **gebaut** |
| R9 | Abnahme am Gerät (T120 bis T134) | **offen — nichts geprüft** |
| **R10** | **Öffentlichmachen: AGPLv3, History-Prüfung, Repo auf public** | **zurückgestellt** |

### Der Befund aus R0, und er ist der wichtigste dieses Plans

Beim ersten `dotnet publish` lagen **229 MB .NET- und WindowsAppSDK-Laufzeit im
Ausgabeverzeichnis und keine einzige Linphone-DLL.**

`build/Linphone.Sdk.targets` kopierte die native Kette an zwei Stellen: an
`Build` nach `$(OutDir)` und für MSIX ins Paketlayout. **`publish` sammelt seine
eigene Dateiliste und kannte beides nicht.** Zwei Wege waren gebaut, der dritte
fehlte — und nichts hat es gemeldet: der Build war grün, das Verzeichnis sah
vollständig aus. Aufgefallen wäre es erst auf dem Zielrechner, mit der Meldung
aus §14.2, die nicht sagt, welche Datei fehlt.

Behoben mit einem dritten Target (`AddLinphoneToPublish`, über
`ResolvedFileToPublish`). **Und `Release-Nipp.ps1` prüft das fertige Verzeichnis
danach noch einmal nach** — sechs Kernbibliotheken, 8 von 8 Grammatiken, das
WASAPI-Plugin. Ein Target, das lautlos nichts tut, sieht wie ein Erfolg aus.

### Was der erste CI-Lauf gefunden hat

Genau das, wofür er da ist: **einen Bau aus einem frischen Klon.** Er brach mit
zehn Compilerfehlern ab, weil `src\Nipp.Core\Services\Integrations\Secrets\`
von der `.gitignore` verschluckt wurde — die Regel `secrets/` war für
Zugangsdaten gedacht. `IntegrationSecrets.cs` lag hier auf der Platte und war
**nie im Repo**.

Der Fehler war lokal unsichtbar und hätte auf jeder anderen Maschine sofort
zugeschlagen. Behoben (`/secrets/`), die Datei ist eingecheckt, und
`RepositoryCompletenessTests` vergleicht jede gebaute Quelldatei mit
`git ls-files`.

### Gemessen am 07.09.2026

| | |
|---|---|
| publish, self-contained | 345 MB, 325 DLLs, 8/8 Grammatiken |
| `nipp-win-stable-Setup.exe` | **144,8 MB** |
| Voll-Paket / Portable | je 141 MB |
| Bauzeit `vpk pack` | 20 s (auf ARM64, emuliert) |
| Velopack | 1.2.0, MIT, als lokales Tool in `.config/dotnet-tools.json` |

`vpk` bestätigt beim Packen selbst: *„Verified VelopackApp.Run() in
'System.Void Nipp.App.Program::Main()'"* — der Hook sitzt an der richtigen
Stelle.

---

## Was vorab entschieden ist

Vier Weichen, am 07.09.2026 von Dominic gestellt:

| Frage | Entscheidung |
|---|---|
| Installer und Update-Rahmen | **Velopack** (MIT), nicht MSIX, nicht Inno Setup |
| Laufzeit | **Alles mitliefern** — .NET 8 und Windows App SDK self-contained |
| Updateablauf | **Fragen, bevor geladen wird.** Kein stiller Download |
| Git-History | **Prüfen, dann übernehmen** — kein frischer Initial-Commit |
| **Öffentlichkeit** | **Das Repo bleibt vorerst privat.** Erst intern testen; R10 später |

**Nachtrag vom selben Abend:** Dominic will zuerst intern testen. Damit bleibt
`docs/licensing.md` in seinem Stand vom 04.09.2026 — „nipp bleibt vorerst
intern" —, und Teil A dieses Plans beschreibt, was **beim Öffentlichmachen**
gilt, nicht was jetzt zu tun ist. Was sich dadurch am Bau ändert, steht in R1,
R7 und R10; es ist wenig, wenn man es von Anfang an so baut.

---

# Teil A — Die Lizenzfrage

## Solange nipp bv2 nicht verlässt, wird gar nichts ausgelöst

Copyleft greift bei der **Weitergabe**. Interne Installationen bei bv2 sind keine
— das ist genau die Grundlage der Entscheidung vom 04.09.2026, und sie gilt
unverändert. Installer, beide Kanäle, Update-Prüfung und die Abnahme am Gerät
lassen sich vollständig bauen und testen, ohne eine einzige Lizenzfrage zu
berühren.

**Zwei Grenzen dieser Aussage**, beide schon in `docs/licensing.md`: „intern"
schliesst Auftragnehmer und Tochtergesellschaften nicht automatisch ein — wer ein
Setup bekommt, gehört vorher geprüft. Und: **ein öffentliches Release-Repo mit
der Setup.exe wäre Weitergabe an die Welt.** Der Quelltext darf zu bleiben, die
Binärdatei muss es dann auch. Siehe R7.

Der Rest dieses Teils gilt ab dem Tag, an dem nipp das Haus verlässt.

## Kurzantwort auf die ursprüngliche Frage

**Im Grundsatz ja, aber „das Repo auf public stellen" ist nur die halbe
Handlung — und für sich genommen wirkungslos.**

Ein öffentliches Repository ohne Lizenzdatei steht unter „alle Rechte
vorbehalten". Wer nipp dann ausliefert, verteilt weiterhin ein Werk, das gegen
das linphone-sdk gelinkt ist, **ohne** die Bedingungen zu erfüllen, unter denen
das erlaubt ist. Sichtbarkeit ist keine Lizenz. Was die AGPLv3 verlangt, ist
nicht Öffentlichkeit, sondern ein **Angebot an jeden Empfänger der Binärdatei**,
den vollständigen zugehörigen Quelltext zu bekommen — samt der Erlaubnis, ihn zu
ändern und weiterzugeben.

Das öffentliche Repo ist der bequemste Weg, dieses Angebot zu erfüllen. Es
ersetzt aber weder die Lizenzdatei noch die Zuordnung „welcher Quellstand gehört
zu diesem Installer".

## Was dazugehört, damit es trägt

| Handlung | Warum |
|---|---|
| `LICENSE` mit dem vollständigen **AGPL-3.0**-Text im Wurzelverzeichnis | Ohne sie ist der Code trotz Sichtbarkeit unlizenziert |
| Lizenzkopf oder ein `NOTICE` mit Urheber und Lizenz | GPLv3 §5: das Werk muss die Bedingungen nennen, unter denen es steht |
| **Jeder Release trägt einen Tag**, und der Installer nennt ihn | GPLv3 §6: der Quelltext muss zur **ausgelieferten Fassung** passen, nicht zu `main` von heute |
| `THIRD-PARTY-NOTICES.md` mit linphone-sdk (AGPLv3), Velopack, Serilog, den MS-Paketen und json-everything | Fremde Lizenztexte sind mitzuliefern |
| Ein **Spiegel des SDK-Quelltextes** (5.5.18) oder wenigstens Tag und Bezugsquelle im Release | Der zugehörige Quelltext des kombinierten Werks schliesst das SDK ein. `download.linphone.org` räumt irgendwann auf; ein Link, der ins Leere zeigt, erfüllt nichts |
| Die Bauanleitung bleibt im Repo (`docs/sdk-setup.md`, `build.ps1`, `build/`) | „Corresponding Source" umfasst ausdrücklich die Skripte, mit denen gebaut und installiert wird |
| Im Info-Bereich der App: Version, Lizenz, Link auf das Repo | Der Empfänger muss das Angebot **finden**, ohne danach zu fragen |

## Was der Schritt kostet

Das ist keine Formalie, sondern eine kaufmännische Entscheidung — sie gehört
bewusst getragen, nicht nebenbei mitgenommen:

- **Alles wird öffentlich, was mitgeliefert wird**: die Provisionierungslogik,
  die Connector-Vorlagen unter `docs/integrations/`, damit auch die Endpunkte
  `crm.bv2.ch` und `journal.bv2.ch` samt ihrer Feldstruktur. Zugangsdaten
  sind es nicht — die liegen in DPAPI und in Kundenprofilen, und die sind Daten,
  keine Software. Aber wie die bv2-Systeme innen aussehen, steht dann im Netz.
- **Jeder darf nipp weitergeben, ändern und verkaufen.** Die AGPL verbietet
  kommerzielle Nutzung nicht. Ein Mitbewerber darf nipp forken; er muss seine
  Änderungen nur ebenfalls offenlegen.
- **Copyleft wandert mit.** Eine kundenspezifische Erweiterung, die mit nipp
  gelinkt wird, steht ebenfalls unter AGPLv3 und ist dem Kunden im Quelltext
  anzubieten. Wer später ein proprietäres Modul plant, ist genau hier wieder bei
  der kommerziellen Lizenz von Belledonne.
- **Zurück geht es nicht.** Eine einmal unter AGPL veröffentlichte Fassung bleibt
  unter AGPL. bv2 kann künftige Fassungen umlizenzieren (das Urheberrecht bleibt
  bei bv2), die veröffentlichte nicht mehr einfangen.

## Was AGPL **nicht** löst

Zwei Dinge, die gern mit der Lizenzfrage verwechselt werden:

- **§13, die Netzwerk-Klausel, greift hier nicht** — und das ist die gute
  Nachricht. Sie zielt auf Software, mit der **Dritte über ein Netz**
  interagieren. Ein Softphone auf dem Arbeitsplatz ist das nicht; der Benutzer
  sitzt davor. Käme später eine Server- oder Mandantenkomponente dazu, wäre sie
  neu zu prüfen.
- **Das Code-Signing-Zertifikat bleibt offen (AP9.2)** — **und das ist seit
  dem 14.09.2026 eine Entscheidung, kein Versäumnis.** Ohne es zeigt Windows
  beim Setup „Unbekannter Herausgeber", SmartScreen blockiert den ersten Start,
  und beim Kunden sieht das nach Schadsoftware aus. **Die Lizenz erlaubt die
  Weitergabe, das Zertifikat ermöglicht sie.**

  **Für die zehn internen Arbeitsplätze bleibt es unsigniert.** Dort ist die
  Meldung einmal wegzuklicken, und der Aufwand steht in keinem Verhältnis. Vor
  einer Abgabe ausser Haus ist es weiterhin der einzige echte Blocker.

  **Die Wege, geprüft am 14.09.2026** — damit die Recherche nicht zweimal
  gemacht wird:

  | Weg | Kosten | Aufwand | SmartScreen |
  |---|---|---|---|
  | **SignPath Foundation** (für quelloffene Projekte) | kostenlos | Build muss über ein CI-System laufen, nicht lokal | wie OV: Reputation muss wachsen |
  | **Azure Trusted Signing** | ~10 USD/Monat | gering, **kein Hardware-Token** | wie OV |
  | **OV-Zertifikat** | 200–400 CHF/Jahr | Hardware-Token muss beim Bauen stecken | Reputation muss wachsen |
  | **EV-Zertifikat** | 400–700 CHF/Jahr | Hardware-Token | **sofort vertraut** |

  **Let's Encrypt geht nicht** und wird es nie: deren Zertifikate tragen
  `serverAuth`, Authenticode verlangt `codeSigning` aus einem Root im
  Microsoft-Programm für Code Signing. Dazu verlangt Code Signing eine
  Organisations- statt einer Domainprüfung und seit Juni 2023 einen privaten
  Schlüssel in zertifizierter Hardware — beides verträgt sich nicht mit einem
  vollautomatischen, kostenlosen Dienst.

  **Seit dem Repo-Wechsel ist SignPath neu möglich**: nipp ist quelloffen
  (AGPLv3) und öffentlich. Der Preis wäre ein CI-Build — der T174 (frischer
  Klon, Bauen nach eigener Anleitung) nebenbei bei jedem Commit beantworten
  würde.

Das hier ist keine Rechtsberatung. Der AGPL-Weg ist der von Belledonne
ausdrücklich vorgesehene, und die Punkte oben sind die üblichen; vor der ersten
Abgabe an jemanden ausserhalb bv2 lohnt eine juristische Gegenlese von einer
Stunde.

---

# Teil B — Der Weg zum Installer

## Warum Velopack und nicht MSIX

MSIX ist in `docs/packaging.md` beschrieben und gebaut — und aus zwei Gründen
heute nicht der Weg zur Auslieferung:

1. **T110 ist offen**: packaged bekommt keine Toasts
   (`AppNotificationManager.Register()` scheitert mit `0x80070490`). Ohne Toast
   ist §8.6 nicht erfüllt, und nipp lebt im Infobereich — ein eingehender Anruf
   gäbe kein Zeichen. Der Alltag läuft deshalb unpackaged.
2. **Ohne echtes Zertifikat lässt sich ein MSIX beim Kunden gar nicht
   installieren.** Bei einer Setup.exe ist ein fehlendes Zertifikat eine
   hässliche Warnung; bei MSIX ist es ein hartes Nein.

Velopack passt dagegen auf die Lage: es installiert **ohne Adminrechte** nach
`%LocalAppData%`, nimmt **GitHub Releases** als Feed, führt **Kanäle** als
getrennte Feeds (`releases.win-stable.json`, `releases.win-beta.json`) und baut
**Delta-Pakete**, was bei einer self-contained App mit dreistelliger
Megabyte-Zahl den Unterschied macht. Es ist MIT-lizenziert und damit mit AGPL
verträglich.

**MSIX wird nicht weggeworfen.** `Pack-Nipp.ps1` und das Manifest bleiben; sobald
T110 gelöst und ein Zertifikat da ist, ist MSIX für Intune-Umgebungen (§16.3)
wieder die bessere Wahl. Beide Wege nebeneinander sind vertretbar — aber nur
einer wird ausgeliefert, sonst hat man zwei Update-Wege und keinen davon ganz.

---

## R0 — Machbarkeit zuerst (halber Tag)

**Bevor irgendetwas eingebaut wird**, wird die Annahme geprüft, auf der alles
steht: eine **self-contained, unpackaged** WinUI-3-App mit der nativen
Linphone-Kette lässt sich bauen und startet auf einem Rechner ohne .NET und ohne
Windows App SDK.

```powershell
.\build.ps1 --% publish src\Nipp.App -c Release -r win-x64 --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true
```

Zu prüfen, in dieser Reihenfolge:

1. Der Build läuft durch (Warnungen sind Fehler, §15).
2. Das Ausgabeverzeichnis enthält die Linphone-Kette vollständig — sechs
   Kernbibliotheken und **8 von 8** belr-Grammatiken. Die Prüfung dafür steht
   schon in `build/Pack-Nipp.ps1` und wird übernommen, nicht neu geschrieben.
3. nipp startet aus diesem Verzeichnis, meldet sich an, klingelt, spielt Töne.
4. **Toasts funktionieren weiterhin** — sie sind der Grund für unpackaged.
5. `vpk` läuft auf dieser ARM64-Maschine und erzeugt **win-x64**-Pakete
   (ADR-001: hier ist alles Emulation; das gilt auch für das Werkzeug).

**Abbruchkriterium:** Scheitert 1 oder 2, wird nicht weitergebaut, sondern die
Ursache untersucht. Scheitert 4, ist die Grundlage der ganzen Auslieferungsform
weg, und der Plan geht zurück auf MSIX plus T110.

**Erwartete Grösse:** Installordner 250–350 MB, Setup komprimiert 90–130 MB. Der
heutige Debug-Ordner liegt bei 115 MB **ohne** mitgelieferte Laufzeiten.

---

## R1 — Lizenzhygiene, intern (zwei Stunden)

Das Repo bleibt privat. Trotzdem gehört jetzt erledigt, was ohnehin fällig ist
und später den Schalter zu einer Kleinigkeit macht:

1. **`THIRD-PARTY-NOTICES.md`** aus dem, was `docs/licensing.md` schon führt,
   plus Velopack (MIT). Die Regel „permissiv oder gar nicht" steht dort; die
   Liste ist gepflegt, sie hat nur noch keine Ausgabeform.
2. **Der Info-Bereich der App** nennt Version, Hersteller und die Fremdlizenzen.
   `AppInfo` und `SettingsViewModel.VersionLine` sind da; es fehlen zwei Zeilen.
   Fällt in R6 ohnehin an.
3. **`docs/licensing.md` bestätigen, nicht ändern.** Der Stand vom 04.09.2026
   gilt weiter. Ergänzt wird ein Satz zur Auslieferungsform: **ein
   Velopack-Setup an einen bv2-Arbeitsplatz ist keine Weitergabe, ein
   öffentlich herunterladbares Setup wäre eine.**
4. **Der Release ist ausdrücklich als intern markiert** — im Release-Text auf
   GitHub, im Info-Bereich und in `docs/updates.md`. §12 (M8) verlangt genau
   das, wenn die Lizenzfrage nicht geklärt ist.

**Nicht jetzt:** `LICENSE`, History-Prüfung, SDK-Spiegel, öffentlicher README-Ton.
Das ist R10 und wäre heute Arbeit auf Vorrat — mit dem Nachteil, dass eine
AGPL-Datei im Repo eine Lizenzierung behauptet, die noch nicht entschieden ist.

---

## R2 — Eigener Einstiegspunkt (halber Tag)

Velopack braucht seinen Hook **als Allererstes im Prozess** — vor jeder
WinUI-Initialisierung. Ein Update wird beim Neustart angewandt, und dabei ruft
Velopack die eigene EXE mit Argumenten wie `--veloapp-install` auf; `Run()`
erledigt das und beendet den Prozess. Läuft vorher WinUI an, ist das Fenster
kurz da und die Installation kaputt.

- `<DefineConstants>DISABLE_XAML_GENERATED_MAIN</DefineConstants>` im csproj.
- Neue `src/Nipp.App/Program.cs`:

```csharp
[STAThread]
private static void Main(string[] args)
{
    VelopackApp.Build().Run();          // zuerst. Kehrt bei Hook-Aufrufen nie zurueck.

    WinRT.ComWrappersSupport.InitializeComWrappers();
    Application.Start(_ => { new App(); });
}
```

**Der heikle Punkt ist die Reihenfolge zu Single-Instance.**
`AppInstance.FindOrRegisterForKey("nipp-single-instance")` steht heute in
`App.xaml.cs:163` und leitet Aktivierungen an die laufende Instanz weiter. Das
bleibt, wo es ist — aber ein Velopack-Hook darf **nicht** an eine laufende
Instanz weitergeleitet werden. Deshalb `VelopackApp.Run()` davor, im nackten
`Main`, ohne DI, ohne Serilog, ohne Fenster.

**Merkposten aus der Vergangenheit:** ein blockierender Aufruf im Startpfad sieht
wie ein Erfolg aus (Headset, siehe CLAUDE.md). Der Update-Hook läuft ganz vorn —
wer hier etwas Wartendes einbaut, hat eine App, die nicht startet, und die letzte
Protokollzeile ist eine, die nach Fortschritt aussieht.

---

## R3 — Das Setup bauen (ein Tag)

Neues `build/Release-Nipp.ps1`, daneben bleibt `Pack-Nipp.ps1` für MSIX.

```powershell
.\build\Release-Nipp.ps1 -Version 0.9.0 -Channel stable
```

Was es tut:

1. `publish` wie in R0, in ein sauberes Verzeichnis.
2. **Die Vollständigkeitsprüfung der Linphone-Kette** — wörtlich aus
   `Pack-Nipp.ps1` übernommen. §14.2: eine fehlende native DLL fällt erst beim
   ersten Start auf dem Zielrechner auf, mit einer Meldung, die nicht sagt,
   welche fehlt. Das ist der wertvollste Teil des bestehenden Skripts und darf
   beim Wechsel des Paketformats nicht verlorengehen.
3. `vpk pack` mit `--packId nipp --packVersion <v> --channel win-<kanal>
   --icon Assets\AppIcon.ico --packAuthors "bv2 GmbH" --shortcuts
   Desktop,StartMenuRoot` und, sobald vorhanden, `--signParams`.
4. Ergebnis nach `dist\`: `nipp-win-<kanal>-Setup.exe`, das Voll-Paket, das
   Delta-Paket und `releases.win-<kanal>.json`.

**Version:** dreiteilig (SemVer), anders als bei MSIX. Sie steht in
`Directory.Build.props` (`VersionPrefix`, heute `0.1.0`) und wird von dort ans
Skript gereicht — **eine Quelle**, wie beim Manifest auch. Für den Beta-Kanal ein
Suffix (`0.9.0-beta.3`).

---

## R4 — Pfade, die sich mit dem Update bewegen (halber Tag)

Velopack legt an:

```
%LocalAppData%\nipp\
    Nipp.App.exe          <- Stub, bleibt ueber alle Updates gleich
    Update.exe
    current\              <- der Inhalt wird beim Update ersetzt
        Nipp.App.exe
        ...
```

`WindowsIntegration.ExecutablePath` ist heute
`Path.Combine(AppContext.BaseDirectory, "Nipp.App.exe")` und zeigt damit **in den
Ordner, der beim Update ausgetauscht wird**. Autostart (`HKCU\...\Run`) und die
Protokoll-Handler `tel:`, `sip:`, `callto:` schreiben diesen Pfad in die
Registrierung.

- `ExecutablePath` bevorzugt den **Stub** eine Ebene über `current\`, sonst wie
  bisher. Damit funktioniert die unpackaged Entwicklungsfassung unverändert.
- **Benutzerdaten bleiben, wo sie sind**: `%APPDATA%\nipp\settings.json`,
  `%LOCALAPPDATA%\nipp\history.db`, die DPAPI-Geheimnisse und
  `%PROGRAMDATA%\bv2\nipp\nipp-factory.xml`. Keines davon liegt im Installordner,
  also überlebt alles ein Update ohne Zutun. **Das ist zu prüfen, nicht
  anzunehmen** (T124) — die Anrufliste ist die einzige Nutzerdatei, die nipp
  nicht wiederherstellen kann.
- Deinstallation: Autostart- und Protokolleinträge zurückbauen. Velopack ruft
  dafür einen Hook auf; die Rückbaulogik hat `WindowsIntegration` bereits.
  **Nachtrag vom 13.09.2026 (W1.5, Befund E4):** dieser Punkt stand hier als
  erledigt, und der Hook war **nicht angemeldet** — `Program.cs` rief
  `VelopackApp.Build().Run()` ohne Rückruf. Nach dem Deinstallieren zeigten der
  Autostart-Eintrag und die Handler für `tel:`, `sip:`, `sips:` und `callto:`
  weiter auf eine gelöschte EXE. Jetzt über
  `OnBeforeUninstallFastCallback`; **am Gerät abzunehmen als T272.**

---

## R5 — Der Update-Dienst (ein bis zwei Tage)

Neu unter `src/Nipp.Core/Services/Updates/`. **Nicht** unter `Integrations/` —
dort gilt die Grenze „weder SDK noch WinUI", aber ein Update ist keine
Integration, und `IntegrationBoundaryTests` hätte sonst eine Ausnahme zu tragen,
die niemandem erklärt, warum.

```
IUpdateService          CheckAsync(), DownloadAsync(), ApplyAndRestart()
UpdateService           Velopack-UpdateManager mit GithubSource
UpdateChannel           Stable | Beta
UpdateState             Unbekannt | Aktuell | Verfuegbar | Geladen | Fehler
```

**Der Ablauf, wie entschieden — fragen, bevor geladen wird:**

1. Beim Start **nicht sofort**: erst wenn Anmeldung und Oberfläche stehen, mit
   ein paar Sekunden Abstand, auf einem eigenen Thread. Ein Softphone, dessen
   Start von GitHub abhängt, ist kaputt gebaut — dieselbe Regel wie beim
   Provisioning (`docs/provisioning.md`: ein Fehler beim Abruf verhindert den
   Start nicht).
2. Wird eine neuere Fassung gefunden: eine **ruhige Zeile in der Oberfläche**,
   kein Dialog, kein Toast. Ein Toast ist bei nipp die Anrufmeldung; wer ihn für
   ein Update benutzt, verwässert das einzige Zeichen, das sofort Aufmerksamkeit
   verdient.
3. Erst auf Knopfdruck wird geladen, mit Fortschritt.
4. Angewandt wird auf Knopfdruck — und **nie, solange ein Gespräch läuft**. Der
   Knopf ist dann abgeblendet, mit dem Grund daneben. Prüfung über den
   vorhandenen Anrufzustand, nicht über eine eigene Vermutung.
5. Fehler sind still: Protokolleintrag, Zustand `Fehler`, nächster Start
   probiert es wieder. Kein Netz ist der Normalfall, nicht die Ausnahme.

**Kanäle.** Velopack führt sie als getrennte Feeds; die installierte Fassung
sucht standardmässig im eigenen Kanal weiter. Der Wechsel stable → beta ist eine
Einstellung, die den Kanal des `UpdateManager` überschreibt; **beta → stable ist
technisch ein Downgrade** und braucht `AllowVersionDowngrade`. Wer das vergisst,
baut eine Einbahnstrasse: einmal beta, immer beta.

**Protokollierung:** Version, Kanal, Ergebnis. Keine Benutzerdaten — die Regel
aus §21.2 ist hier leicht einzuhalten, weil es nichts Personenbezogenes gibt. Ein
Satz für `docs/`: der Update-Check überträgt an GitHub die IP-Adresse und den
User-Agent, sonst nichts.

**Tests** (Nipp.Core hat ein Testprojekt, `Nipp.App` nicht — deshalb liegt der
Dienst hier): Kanalwahl, Zustandsübergänge, „kein Update während eines
Gesprächs", und dass ein Fehler beim Abruf nichts anderes ändert. Der
Velopack-Zugriff hinter eine schmale Schnittstelle, damit die Tests ohne Netz
laufen.

---

## R6 — Die Oberfläche (halber Tag)

§9 verlangt in der Einstellungstabelle ohnehin eine Zeile
**„Version / Update prüfen — Anzeige + Aktion"**; sie ist bis heute nicht gebaut.
Damit wird sie fällig, nicht zusätzlich.

In `SettingsPage.xaml`, im Info-Bereich:

- Version und Kanal, vorlesbar (`AppInfo.VersionLine` gibt es schon).
- Knopf „Jetzt prüfen", darunter der Zustand im Klartext.
- Kanalwahl **stable / beta**, mit einem Satz dazu, was beta bedeutet.
- Lizenz und Repo-Link (aus R1).

`AutomationProperties.Name` nicht vergessen — die Lehre aus dem Katalog steht in
CLAUDE.md: ein `record` in einer Liste liest sich für die Sprachausgabe als
`ToString()`.

---

## R7 — Der Weg zu GitHub (halber Tag)

**Veröffentlichen** aus dem Release-Skript heraus:

```powershell
vpk upload github --repoUrl https://github.com/bv2-GmbH/nipp-softphone --token $env:GITHUB_TOKEN --channel win-stable --publish --tag v0.9.0
# beta zusaetzlich mit --pre
```

Der Tag ist zugleich die spätere Antwort auf die Lizenzpflicht aus R10: **dieser
Installer gehört zu diesem Quellstand.** Er wird von Anfang an gesetzt, auch
solange das Repo privat ist — nachträglich lässt sich diese Zuordnung nicht
herstellen.

### Das private Repo kostet einen Zugriffsschlüssel

Ein Release-Asset in einem privaten Repo ist ohne Anmeldung nicht abrufbar. Die
installierte App braucht also ein Token, um überhaupt nachzusehen — Velopack
nimmt es am `GithubSource` entgegen.

**Nicht ins Programm einkompilieren.** nipp hat für genau diese Sorte Wert schon
zwei Wege, und beide sind besser: das **Provisioning** (`nipp-factory.xml` oder
das Kundenprofil) bringt den Wert an den Arbeitsplatz, der **`SecretStore`**
legt ihn über DPAPI ab. Ein fein granuliertes Token mit **`Contents: read` auf
genau dieses eine Repo** und nichts sonst; fehlt es, ist die Update-Prüfung
schlicht aus, und nipp telefoniert unverändert weiter.

**Die Feed-Quelle gehört hinter eine Konfigurationsstelle**, nicht in den Code
verstreut. Dann ist der Wechsel — privates Repo mit Token → öffentliches Repo
ohne Token → eigener Webserver bei bv2, falls die Token-Verteilung nervt —
jeweils eine Zeile. Velopack kann alle drei.

**Für zehn interne Arbeitsplätze** ist ein Token im Provisioning tragbar. Für
eine Kundenverteilung ist es das nicht: ein Token, das auf jedem Rechner liegt,
ist ein Token, das jeder auslesen kann. Bis dahin ist entweder R10 erledigt
(öffentliches Repo, kein Token) oder der Feed liegt auf einem bv2-Webserver.

**Kein öffentliches Release-Repo als Abkürzung.** Der naheliegende Trick — Code
privat, ein zweites Repo `nipp-releases` öffentlich für die Pakete — ist genau
der Fall, den die AGPL adressiert: die Binärdatei ginge an jeden, der Quelltext
an niemanden. Entweder beides zu oder beides offen.

**Und ein Nebeneffekt, der mehr wert ist als der Rest dieser Phase:** ein
GitHub-Actions-Workflow läuft auf `windows-latest` — **echter x64-Hardware**.
Damit werden Bau und die 917 Komponententests erstmals auf der Zielarchitektur
gemessen statt unter Emulation (ADR-001). Das ersetzt AP7.8 und T38 nicht (kein
Audiogerät, keine Anlage, keine Laufzeitmessung), verkleinert aber den
ungemessenen Rest spürbar — und kostet, weil das SDK-ZIP ohnehin per Skript
beschafft wird, kaum Einrichtung.

Zwei Workflows: `ci.yml` (Bau und Tests bei jedem Push) und `release.yml`
(manuell, mit Version und Kanal als Eingabe). Das Zertifikat kommt später als
Secret dazu.

Im privaten Repo zählen Actions-Minuten, und **Windows-Runner zählen doppelt**.
Ein voller Lauf mit SDK-Beschaffung und 917 Tests liegt bei wenigen Minuten —
das trägt jeder Plan. Falls es doch klemmt: `ci.yml` nur auf Pull Requests und
auf `main` laufen lassen, nicht bei jedem Push in einen Arbeitszweig.

---

## R8 — Dokumentation (halber Tag)

- **`docs/updates.md`** neu: wie ein Release entsteht, was die Kanäle bedeuten,
  wie ein Kunde wechselt, was bei einem kaputten Release zu tun ist.
- **`docs/packaging.md`** umschreiben: Velopack ist der Auslieferungsweg, MSIX
  der Anhang für Intune. Die Warnung, dass packaged und unpackaged sich das
  Ausgabeverzeichnis gegenseitig abräumen, bleibt — sie gilt unverändert.
- **`docs/licensing.md`** aus R1.
- **`README.md`**, **`CLAUDE.md`** (Meilensteinabschnitt, „Wo was steht").
- **`ABNAHME-ALLTAG.md`**: wie man ein Update am Gerät prüft.

---

## R9 — Abnahme am Gerät

Neue Zeilen für `docs/test-matrix.md`. **Das ist kein Anhang, sondern der Teil,
der zählt** — 89 von 114 Zeilen haben heute kein Ergebnis, und ein Installer ist
genau die Sorte Sache, die im Test grün aussieht und beim Kunden scheitert.

| # | Testfall | Erwartung |
|---|---|---|
| T120 | Installation auf **frischem Windows 11** ohne .NET und ohne App SDK | nipp startet, meldet sich an, klingelt |
| T121 | Erststart nach der Installation | Verknüpfungen da, Symbol im Infobereich richtig, Autostart eingerichtet |
| T122 | `tel:`-Link aus Outlook **nach** einem Update | wählt — der Registrierungspfad zeigt noch auf etwas Vorhandenes |
| T123 | Update stable → stable | Hinweis erscheint, Laden auf Knopfdruck, Neustart, neue Version läuft |
| T124 | Update mit Daten | Einstellungen, **`history.db`**, Geheimnisse und Karten überleben. **Vorher kopieren** |
| T125 | Update **während eines Gesprächs** angeboten | Knopf abgeblendet mit Grund; das Gespräch bleibt unberührt |
| T126 | Kanal auf beta, Prüfung | findet die Beta-Fassung; stable-Nutzer sehen sie nicht |
| T127 | Kanal zurück auf stable | Downgrade gelingt (`AllowVersionDowngrade`) |
| T128 | Start **ohne Netz** | keine Verzögerung, keine Meldung, Protokolleintrag, Telefonie unbeeinträchtigt |
| T129 | Deinstallation | Verknüpfungen, Autostart und Protokoll-Handler weg; Benutzerdaten bleiben (gewollt) |
| T130 | Setup auf einem Rechner **ohne** importiertes Zertifikat | dokumentiert, wie die SmartScreen-Meldung aussieht — bis AP9.2 da ist, ist das der Auslieferungszustand |
| T131 | Toasts nach der Installation | ein eingehender Anruf bei geschlossenem Fenster gibt ein Zeichen (die Voraussetzung, wegen der unpackaged ausgeliefert wird) |
| T132 | Update-Prüfung **ohne** hinterlegtes Token (privates Repo) | still aus: Protokolleintrag, keine Meldung, Telefonie unberührt |
| T133 | Token kommt aus dem Provisioning | frisch eingerichteter Arbeitsplatz findet Updates, ohne dass jemand etwas eintippt |

---

## R10 — Öffentlichmachen (später, ein Tag)

**Zurückgestellt.** Fällig, wenn nipp bv2 verlässt — bei der ersten Abgabe an
einen Kunden, einen Auftragnehmer oder eine Tochtergesellschaft. Dann ist zuerst
`docs/licensing.md` aufzurollen, und dieser Abschnitt ist die Handlungsliste
dazu.

1. **History prüfen, vollständig, vor dem Umschalten.** Über alle Commits, nicht
   nur im Arbeitsbaum: Zugangsdaten, Tokens, echte Rufnummern, Kundennamen,
   interne Hostnamen, `test-trunk.json`. Werkzeug: `git log -p` mit Mustern,
   ergänzt um `gitleaks` oder `trufflehog`. Ein Fund heisst `git-filter-repo` und
   ein neu geschriebener Verlauf — **vorher**, weil danach jeder Klon ihn
   konserviert. Der Arbeitsbaum ist bereits geprüft: die einzigen Treffer sind
   erfundene Testkonstanten in `tests/` (`streng-geheim-4711`).
2. `LICENSE` (AGPL-3.0, unveränderter Text), `NOTICE` mit `© 2026 bv2 GmbH`.
3. `docs/licensing.md` **umschreiben, nicht anhängen**. Eine Statuszeile, die
   „intern" sagt, während ausgeliefert wird, ist schlimmer als keine — dieselbe
   Falle wie bei ADR-004, das drei Tage falsch auf „offen" stand.
4. `README.md` bekommt Lizenzabschnitt und eine Bau- und Beschaffungsanleitung
   für Fremde; bis heute steht dort ein bv2-interner Ton.
5. **SDK-Quelltext sichern**: `linphone-sdk` 5.5.18 als Spiegel oder
   Release-Asset, mit SHA256 wie in `docs/sdk-setup.md`.
6. Repo auf **public**, Token aus dem Update-Dienst entfernen (R7), ADR-040.
7. **Prüfen, wie es von aussen aussieht** — frischer Klon ohne SDK, ohne
   `%LOCALAPPDATA%`-Dateien, nach der eigenen Anleitung gebaut. Wer das nicht
   tut, merkt erst am ersten Fremden, dass eine Datei fehlt, die hier seit
   Monaten herumliegt.

**Was jetzt schon dafür getan wird**, ohne Mehraufwand: jeder Release trägt einen
Tag (R7), die Fremdlizenzen sind gepflegt (R1), und die Feed-Quelle ist eine
Konfigurationsstelle statt verstreuter Code.

---

## Entscheidungen, die als ADR fällig werden

| ADR | Inhalt |
|---|---|
| **ADR-038** | Auslieferung als Velopack-Setup, unpackaged, self-contained. Ergänzt ADR-008, ersetzt es nicht: MSIX bleibt gebaut und wartet auf T110 und das Zertifikat. Beantwortet §16 Punkt 3 (Verteilung) |
| **ADR-039** | Update über GitHub Releases, Kanäle stable/beta, Prüfung beim Start, **fragen statt still laden**, nie während eines Gesprächs. Dazu: privates Repo, Token aus dem Provisioning, Feed-Quelle austauschbar. Beantwortet §16 Punkt 4 — bis heute „nicht entschieden" |
| ~~ADR-040~~ | **Zurückgestellt mit R10.** nipp wird AGPLv3 und öffentlich — die Entscheidung fällt bei der ersten Abgabe ausser Haus, nicht heute. Teil A dieses Plans ist ihre Vorarbeit; `docs/licensing.md` bleibt bis dahin in seinem Stand vom 04.09.2026 |

---

## Risiken

| Risiko | Wirkung | Umgang |
|---|---|---|
| **Kein Code-Signing-Zertifikat** | SmartScreen blockiert den ersten Start beim Kunden. Für eine Abgabe ausser Haus ist das der eigentliche Blocker | AP9.2 sofort anstossen — Wochen Vorlauf. Bis dahin T130 dokumentieren, nicht beschönigen |
| **self-contained + WinUI 3 unpackaged ungeprüft** | Trägt den ganzen Plan | R0 zuerst, mit Abbruchkriterium |
| **Velopack-Hook vor WinUI** | Falsche Reihenfolge heisst: Update installiert nicht, Fenster blitzt auf | R2, und T123 prüft es am Gerät |
| **Registrierungspfade zeigen in `current\`** | Nach dem ersten Update wählt kein `tel:`-Link mehr, und der Autostart startet nichts | R4, T122 |
| **beta ist eine Einbahnstrasse** | Wer einmal wechselt, kommt ohne Neuinstallation nicht zurück | `AllowVersionDowngrade`, T127 |
| **GitHub-API-Limit** | Unauthentifiziert 60 Anfragen je Stunde und IP. Bei zehn Arbeitsplätzen hinter einer NAT-Adresse und einer Prüfung je Start unkritisch; bei hundert nicht mehr | Bei Kundenverteilung neu bewerten, ggf. eigener Feed auf einem Webserver — Velopack kann beides |
| **Ein kaputtes Release erreicht alle** | Ein Softphone, das nicht mehr startet, ist ein Betriebsausfall | Immer erst beta, dann stable. Rückweg über ein Release mit höherer Versionsnummer, das den alten Stand trägt — beschrieben in `docs/updates.md` |
| **History-Fund nach dem Public-Schalter** | Nicht mehr einzufangen | R10 Punkt 1 vor Punkt 6, ohne Ausnahme |
| **Token auf jedem Arbeitsplatz** | Wer den Rechner hat, hat Lesezugriff auf das private Repo | Fein granuliert, nur `Contents: read`, nur dieses Repo, über Provisioning und DPAPI verteilt. Vor einer Kundenverteilung ablösen — R10 oder eigener Feed |
| **Öffentliches Release-Repo als Abkürzung** | Binärdatei an alle, Quelltext an niemanden — der Fall, für den es die AGPL gibt | Entweder beides zu oder beides offen. Steht in R7 |
| **Kein Tag beim internen Release** | Beim späteren Öffentlichmachen ist nicht mehr zuzuordnen, welcher Quellstand ausgeliefert wurde | Tag von Anfang an, auch privat. Kostet nichts und ist nachträglich nicht herstellbar |

---

## Reihenfolge und Aufwand

| | Paket | Aufwand | Hängt an |
|---|---|---|---|
| 1 | R0 Machbarkeit | ½ Tag | — |
| 2 | R1 Lizenzhygiene intern | 2 Stunden | — |
| 3 | R2 Einstiegspunkt | ½ Tag | R0 |
| 4 | R3 Setup bauen | 1 Tag | R2 |
| 5 | R4 Pfade | ½ Tag | R3 |
| 6 | R5 Update-Dienst | 1–2 Tage | R3 |
| 7 | R6 Oberfläche | ½ Tag | R5 |
| 8 | R7 GitHub, privat, mit Token | ½ Tag | R3 |
| 9 | R8 Dokumentation | ½ Tag | alles |
| 10 | R9 Abnahme | ½ Tag am Gerät | alles |
| — | **R10 Öffentlichmachen** | 1 Tag | **zurückgestellt bis zur ersten Abgabe ausser Haus** |

**Summe: vier bis fünf Tage** bis zum internen Rollout, davon ein halber Tag
Abnahme am Gerät.

**Was dadurch nicht mehr blockiert:** Das Code-Signing-Zertifikat (AP9.2) hält
den internen Betrieb nicht auf — auf zehn bv2-Rechnern ist die
SmartScreen-Meldung einmal wegzuklicken, und ein selbstsigniertes Zertifikat im
Speicher „Vertrauenswürdige Herausgeber" räumt sie ganz weg (das Rezept steht in
`docs/packaging.md`). Es bleibt trotzdem der lange Vorlauf: **zusammen mit R10
fällig, und beide gehören angestossen, bevor jemand den ersten Kundentermin
macht** — nicht danach.
