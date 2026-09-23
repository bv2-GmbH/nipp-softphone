# nipp

Ein Windows-Softphone für SIP-Telefonanlagen — WinUI 3 auf .NET 8, x64, auf
Basis des [Linphone SDK](https://gitlab.linphone.org/BC/public/linphone-sdk)
5.5.18.

**Lizenz: [AGPLv3](LICENSE).** Wer nipp weitergibt oder verändert weitergibt,
gibt den Quelltext mit — auch den des SDK. Warum das so ist und welche
Alternativen geprüft wurden, steht in
[docs/licensing.md](docs/licensing.md).

**Was es ist, und was nicht.** nipp ist bei bv2 entstanden und ersetzt dort den
zugekauften Client, den bisher jeder Arbeitsplatz mitschleppt. Es ist keine
Software von der Stange: die Entscheidungen sind für **diesen einen** Einsatz
getroffen, und jede Abweichung von der Spezifikation steht als ADR in
[docs/decisions.md](docs/decisions.md). Wer nipp anderswo einsetzen will,
findet dort, was zu ändern wäre — und in
[docs/lehren.md](docs/lehren.md), was auf dem Weg dorthin schon Tage gekostet
hat.

**Zustand, offen gesagt.** nipp telefoniert im Alltag, seit dem 07.09.2026 auf
echten Anlagen. Was fehlt: das Setup ist **nicht signiert** (Windows meldet
beim ersten Start «Unbekannter Herausgeber»), ein grosser Teil der Testmatrix
hat noch kein Ergebnis, und entwickelt wird auf ARM64 unter x64-Emulation —
die nichtfunktionalen Ziele sind damit ungemessen (ADR-001). Wer es ernsthaft
einsetzen will, sollte
[docs/plans/REVIEW-2026-09-12.md](docs/plans/REVIEW-2026-09-12.md) lesen: 83
Befunde, ehrlich aufgeschrieben.

## Bauen — die Kurzfassung

Windows 10 oder 11, **.NET 8 SDK für x64**, Windows App SDK. Dann:

```powershell
git clone <dieses Repo>
cd nipp

# Das Linphone SDK liegt NICHT im Repo — 299 MB, einmal herunterladen:
Invoke-WebRequest `
  -Uri 'https://download.linphone.org/releases/windows/sdk/linphone-sdk-win64-5.5.18.zip' `
  -OutFile 'sdk\linphone-sdk-win64-5.5.18.zip'
Expand-Archive -Path 'sdk\linphone-sdk-win64-5.5.18.zip' -DestinationPath 'sdk\extracted'

.uild.ps1 build Nipp.sln -c Debug
.uild.ps1 test Nipp.sln
```

**Immer `.uild.ps1`, nie ein blankes `dotnet`** — das Skript sucht den
x64-Host, der auf einer ARM64-Maschine sonst nicht gefunden wird. Die
ausführliche Fassung samt Prüfsummen steht unter
[Entwicklungsmaschine einrichten](#entwicklungsmaschine-einrichten), die
Fallstricke unter
[Fallstricke, die schon zugeschlagen haben](#fallstricke-die-schon-zugeschlagen-haben).

---

```
┌─────────────────────────────┐
│ ● nipp Testgeraet        ▾  │   Konto mit Status-LED
│ Angemeldet                  │   und der Zustand als Text
├─────────────────────────────┤
│ 044 512 84 30       × ⌨  📞│   Eingabe, Löschen, Tastatur, Wählen
│ wählt +41445128430          │   (leer: 🕘 statt ×, Wahlwiederholung)
├─────────────────────────────┤
│ ⌄ Team (3)               ⇅  │   Kontakte / Anrufe / Einstellungen
│  AM  Anna Meier      ● frei │   (die Wähltastatur ist ab Werk zu —
│  TS  Thomas Suter ● Gespräch│    das ⌨ im Feld holt sie hervor)
│  RB  Reto Bühler    ● frei  │
│ ⌄ Dienste (7)               │   die Gruppen klappen einzeln zu
│  EM  Empfang   ● im Gespräch│   und bleiben so über den Neustart
├─────────────────────────────┤
│ ☑Kontakte  Anrufe  ⚙        │   die aktive Seite ist zu sehen
└─────────────────────────────┘
```

**Ab rund 960 Pixeln Fensterbreite** stehen zwei Spalten (§23, ADR-047). Links
bleibt alles, wie es ist; rechts kommen die Nebenstellen als **Kacheln** dazu,
nach Gruppen gegliedert — mit allem darauf, was die Zeile sonst erst nach einem
Klick zeigt:

```
┌──────────────────────┬─────────────────────────────────────┐
│ ● nipp Testgeraet ▾  │ ⌄ Team (2)                      ⇅   │
│ 044 512 84 30  × ⌨ 📞│ ┌──────────────┐┌──────────────┐    │
│   1    2    3        │ │● Reto Bühler ││● Empfang     │    │
│   4    5    6        │ │  frei        ││  im Gespräch │    │
│   7    8    9        │ │ 📞 21 Intern ││ 📞 10 Intern │    │
│   *    0    #        │ │ 📞 079…Mobil ││              │    │
├──────────────────────┤ └──────────────┘└──────────────┘    │
│ Anrufe               │ ⌄ Dienste (1)                       │
│  ↙ Muster AG   09:12 │ ┌──────────────┐                    │
│  ↗ 044 512…    08:40 │ │● A. Meier    │                    │
│  ✕ Reto B.   gestern │ └──────────────┘                    │
│                      │                                     │
├──────────────────────┤                                     │
│ ☑Kontakte Anrufe ⚙   │                                     │
└──────────────────────┴─────────────────────────────────────┘
```

Die Umschaltleiste wechselt dabei die **linke** Spalte; die Kacheln stehen
unabhängig davon. Deshalb steht sie auch **nur unter der linken Spalte**, und
das Kachelfeld reicht daneben bis an den unteren Rand (ADR-052).

**Die Breite gehört dem Fenster.** Der Inhalt füllt es — schmal wie breit;
im breiten Layout ist die linke Spalte 480 Pixel breit, und die Kacheln nehmen
den ganzen Rest. Bis ADR-052 war die Inhaltsbreite gedeckelt (ADR-046, ADR-047),
und zwischen 480 und 960 Pixeln blieb links und rechts leerer Rand.

Läuft ein Gespräch, während diese Ansicht offen ist, steht oben eine Leiste
„Gespräch mit …" — ein Klick führt hin. Die Gesprächsansicht hat oben eine
Leiste, die beim Scrollen stehen bleibt:

```
┌─────────────────────────────┐
│ ←  Gespräch läuft weiter 🔊 │   zurück, Wiedergabegerät,
│                 [ Auflegen ]│   Auflegen immer erreichbar
├─────────────────────────────┤
│ Reto Bühler                 │   Gegenstelle, Zustand, Dauer
│ verbunden                   │
│ 02:14                       │
│ [PCMU] [unverschlüsselt]    │
├─────────────────────────────┤
│ Muster AG · +1 weitere      │   Anruferkarte aus dem CRM
│ 04.09. · Migration · A.V.   │   und dem Gesprächsjournal
├─────────────────────────────┤
│ 🔇 Stumm  ⏸ Halten  ⏺ Aufn. │
└─────────────────────────────┘
```

So lässt sich während eines Gesprächs ein zweites aufbauen, makeln und
begleitet übergeben.

---

## Wo das Projekt steht

**Stand 23.09.2026.** Funktional ist nipp weit: Telefonie, Präsenz,
Headset-Tasten, Integrationsplattform, Karten-Designer, Anrufliste, Installer
und Update-Kanal sind gebaut. Der Build läuft ohne Warnungen, **1 274
Komponententests** und **37 Architekturtests** bestehen, und die CI baut bei
jedem Push auf **echter x64-Hardware**.

**Am 23.09.2026 stand nipp zum ersten Mal einen ganzen Arbeitstag lang unter
Beobachtung** — knapp zwölf Stunden Dauerbetrieb, 17 echte Gespräche, ohne
Absturz und ohne eine einzige fehlgeschlagene Anmeldung. Aus demselben
Protokoll liessen sich fünf Zeilen der Testmatrix abnehmen, ohne dass dafür
ein Anruf nötig gewesen wäre; am Abend kamen zwölf weitere dazu.

**Zwei Befunde dieses Tages sind die interessanteren.** Der Audiofehler beim
Verbindungsaufbau (offen seit dem 16.09.2026) hat eine Spur: über vier
Messungen geht **jedem** Fehler-Burst ein Jitterpuffer-Reset voraus, dreimal
davon im Abstand von ein bis vier Millisekunden — der Auslöser ist damit nicht
die Audioausgabe, sondern der Reset davor. Gefunden wurde das, weil jemand
**zugehört** hat: das Protokoll war vollständig und hatte die falsche Frage
beantwortet.

**Und das begleitete Vermitteln ist umgebaut** (ADR-073). Es verlangte bisher,
das zweite Gespräch selbst aufzubauen — zurück zur Wähltastatur, das Ziel
**noch einmal** suchen, anrufen. Jetzt ruft «Zuerst anrufen» das ausgewählte
Ziel an, und «Jetzt übergeben» erscheint, sobald es steht. **Fertig geworden
ist der Umbau erst bei der Abnahme am Gerät:** gebaut, getestet und gepusht
traf «Auflegen» noch den Anrufer statt der Rückfrage.

**Die Standortbestimmung vom 12.09.2026 ist abgearbeitet** — Welle 0, Welle 1
und Welle 2, jede Massnahme als eigener Commit. Zuletzt: ein Skript, das einen
Arbeitsplatz in einem Befehl einrichtet; nipp läuft nur noch einmal, auch ohne
Paketidentität; «Nicht stören» als stummer Klingelton auf Zeit; zwei
Absturzpfade weniger; die Design-Tokens beschreiben endlich, was gezeichnet
wird; und die Dokumentation ist nach Änderungsrate getrennt — Regeln, Stand und
Erfahrung sind drei Dateien statt eine mit 1554 Zeilen.

**Danach kamen vier Meldungen aus der Benutzung**, alle am 13.09.2026 behoben:
die Anmeldung hielt nicht mehr, sobald zwei Netzwege gleichzeitig offen waren
(ADR-060); die Nebenstellen hatten eine Gliederungsebene zu viel (ADR-063); der
Karten-Designer zeigte nach einem echten Abruf den alten Text (ADR-061); und
der Mailbox-Reiter fiel weg, weil ihn niemand benutzte (ADR-062). Dazu zwei
Stellen, die dabei auffielen: der Umschalter zum Umsortieren steht jetzt im
Kopf einer Gruppe statt in einer eigenen Leiste (ADR-064), und **das Ziehen in
eine andere Gruppe funktioniert überhaupt erst seit ADR-065** — es hing an
einer Annahme über WinUI, die niemand gemessen hatte.

**Am 14.09.2026 kamen drei Sachen zusammen, und die letzten beiden hat niemand
gesucht.** Gewünscht war eine **Vorschau beim Ziehen** — die anderen sollen
ausweichen, damit man sieht, wo die Zeile landet (ADR-066). Beim Prüfen fiel
auf, dass **nipp im Gespräch abstürzte**, sobald der Zeiger über den
Auflegen-Knopf fuhr: sechs überschriebene Zustands-Pinsel in einem lokalen
Ressourcen-Wörterbuch, `0xc000027b` in `combase.dll`, **ohne verwaltete
Ausnahme und ohne eine Zeile im Protokoll** (ADR-067). Und beim Messen dazu
zeigte sich, dass **nipp fremde Teams-Meetings beendete** — nicht beim
Klingeln, sondern beim **Ablehnen**, über den Abschlussbericht ans geteilte
Headset-Interface (ADR-068).

Offen bleiben zwei
Punkte, beide mit Grund: Tests für die SDK-Schicht (zurückgestellt bis nach dem
Gerätetag) und der Gerätetag selbst.

**Danach gemeldet und behoben: die Anmeldung hielt nicht, wenn zwei Netzwege
gleichzeitig aktiv sind** (WLAN und Mobilfunk, ADR-060). Das Konto lief im
Sekundentakt an und wieder aus, die Präsenz aller Nebenstellen stand auf
«offline». Dahinter lagen **zwei unabhängige Fehler von derselben Form**: eine
Stelle reichte etwas weiter, ohne zu prüfen, ob es etwas zu melden gab. nipp
gab jedes Windows-Netzereignis an das SDK weiter — und dort ist das keine
Auskunft, sondern eine Neuregistrierung; ausserdem konnte aus einem Speichern
eine Kette ohne Ende werden, **248 MB Protokoll in sechs Minuten**. Beide
Stellen vergleichen jetzt, bevor sie melden.

**Was fehlt, ist nicht Code, sondern Beweis** — aber die Zahl dreht sich
inzwischen. Von **313 Zeilen** der [Testmatrix](docs/test-matrix.md) haben
**176 ein Ergebnis**, 137 sind offen; am 13.09.2026 waren es 23 von 284. Die
Zahl ist nachrechenbar: ein Skript trägt die Zählregel, und «offen» heisst
**ungeprüft**, nicht durchgefallen.

**Der Rest verteilt sich ungleich, und das ist die gute Nachricht:** von den
Abbruchkriterien für den Merge ist alles erledigt, was an der Telefonanlage zu
prüfen war. Übrig bleiben ein **frischer Rechner** (Installation, Update,
Deinstallation) und ein **Windows-10-Arbeitsplatz** — zusammen ein halber Tag,
den es noch nicht gegeben hat. Dazu kommt: die vier nichtfunktionalen Ziele
aus §2 sind **ungemessen**, weil es keine x64-Maschine gibt (ADR-001) und die
Entwicklungsmaschine ARM64 ist. Fällt der frische Rechner auf echte
x64-Hardware, ist beides derselbe Termin.

**Vor einer Abgabe ausser Haus fehlt genau eines:** ein Signaturzertifikat
(AP9.2). Ohne es hält SmartScreen den ersten Start auf — intern einmal
wegzuklicken, beim Kunden nicht.

Die ehrliche Standortbestimmung mit 83 Befunden steht in
[REVIEW-2026-09-12.md](docs/plans/REVIEW-2026-09-12.md); das Urteil dort lautet
**tragfähig und sanierbar**, und die Schuld liegt an den Rändern, nicht im
Kern.

---

## Inhalt

- [Wo das Projekt steht](#wo-das-projekt-steht)
- [Was nipp kann](#was-nipp-kann)
- [Schnellstart](#schnellstart)
- [Integrationen einrichten](#integrationen-einrichten)
- [Entwicklungsmaschine einrichten](#entwicklungsmaschine-einrichten)
- [Aufbau](#aufbau)
- [Die Komponenten im Einzelnen](#die-komponenten-im-einzelnen)
- [Wo was liegt](#wo-was-liegt)
- [Bauen, testen, paketieren](#bauen-testen-paketieren)
- [Fallstricke, die schon zugeschlagen haben](#fallstricke-die-schon-zugeschlagen-haben)
- [Dokumentation](#dokumentation)
- [Lizenz](#lizenz)

---

## Was nipp kann

### Telefonieren

| | |
|---|---|
| **Wählen** | Nummer eingeben oder aus Kontakten und Anrufliste, Enter oder Wählen-Schaltfläche. **Ein Feld für beides** (ADR-046), und **eine Liste je Eingabeart** (ADR-051): eine Nummer zeigt die Vorschläge darunter, ein Name die Trefferliste, die auch die angebundenen Quellen fragt. **Enter auf einem Namen wählt nicht**, sondern springt in die Treffer — was wählbar ist, entscheidet `NumberNormalizer.IsDialable` (ADR-049) |
| **Ohne Maus** | Enter in der Liste ruft an, Menütaste öffnet das Kontextmenü, beim Klingeln liegt der Fokus auf „Annehmen" (ADR-044). Strg+1 bis 4 wechseln den Bereich, Strg+F springt ins Nummernfeld (ADR-046); **im Gespräch Strg+M, Strg+H und Strg+E** für stumm, halten und auflegen, **Escape und Alt+Links gehen zurück** (ADR-050) |
| **Nummern verstehen** | `044 512 84 30`, `+41445128430`, `0041445128430` sind dieselbe Nummer. Interne Ziele (`*8010`, `40`) bleiben unverändert, **Notrufnummern immer** (112, 117, 144, 1414) |
| **Annehmen** | Aus dem Fenster oder direkt aus dem Toast — auch bei geschlossenem Fenster. **Beim Klingeln holt sich nipp den Vordergrund nicht** (ADR-049): das Zeichen ist der Toast, und ein Fenster, das sich vordrängt, nimmt den Fokus mitten aus einer anderen Anwendung. Fallen Benachrichtigungen aus, kommt es selbst nach vorn — **aber ohne den Fokus zu nehmen**: der Fokus in einem nicht aktiven Fenster bekommt keine Tastendrücke, und die Leertaste bleibt dort, wo gerade getippt wird. Bis zum 13.09.2026 war der Befund von ADR-049 in genau diesem Rückfallpfad neu gebaut |
| **Zwei Gespräche** | Halten, Makeln, das zweite aufbauen, ohne das erste zu verlieren |
| **Weiterleiten** | Blind (sofort) oder begleitet (erst sprechen, dann verbinden) |
| **Stumm** | Mikrofon aus, je Gespräch einzeln — im Fenster, über das Symbol im Infobereich und mit **Strg+M**. Dazu ein **zweites systemweites Kürzel nur fürs Stummschalten** (ADR-050), das nipp **nicht** nach vorn holt: man drückt es mitten in einer anderen Anwendung. Ab Werk leer, Vorschlag `Ctrl+Shift+M` |
| **Gerät wechseln** | Das Wiedergabegerät lässt sich **im Gespräch** umstellen, über die Kopfleiste oder das Symbol im Infobereich (ADR-046). Die Wahl überlebt den Neustart |
| **Aktuell bleiben** | nipp sieht beim Start nach, ob es eine neuere Fassung gibt, und **lädt nichts von selbst**. Geladen und neu gestartet wird auf Knopfdruck — nie während eines Gesprächs. Zwei Kanäle: stable und beta |
| **DTMF** | Tastentöne im Gespräch, für Sprachmenüs |
| **Tasten am Headset** | Annehmen, Auflegen und Stumm direkt am Gerät, mit Off-Hook- und Ring-Lampe. Über HID, weil das SDK seine Jabra-Anbindung mit 5.5.0 entfernt hat (§22.5, ADR-028). **Das Interface teilt sich nipp mit anderen Programmen**, und jeder Bericht daran ist ein Eingriff in deren Gespräch: hält ein fremder Prozess die Audio-Sitzung, schweigt nipp — bis der Benutzer annimmt, denn dann hat er entschieden (ADR-068) |
| **Weiterleiten** | Blind oder begleitet — und **das Ergebnis wird abgewartet**: nimmt die Anlage den REFER nicht an, sagt es die Gesprächsansicht, und das eigene Gespräch läuft weiter. Bis zum 13.09.2026 stand «übergeben» im Protokoll, bevor die Anlage geantwortet hatte |
| **Aufnehmen** | Mit sichtbarem Indikator — in der Schweiz ist Mitschneiden ohne Kenntnis der Gegenseite strafbar, deshalb ist der Indikator nicht abschaltbar. **Und es fragt beim ersten Mal je Gespräch** (ADR-049); der Knopf steht bei „Weiterleiten" und „Tastentöne", nicht neben „Stumm" |
| **Qualität sehen** | Codec, Verschlüsselung, Laufzeit, Paketverlust, Jitter-Puffer und geschätzte Sprachqualität im laufenden Gespräch |
| **Verschlüsselung** | SRTP wird angeboten, nicht erzwungen (ADR-007). Bei TLS prüft nipp das Serverzertifikat gegen die Wurzelzertifikate von Windows |

### Wissen, wer anruft

| | |
|---|---|
| **Kontakte** | Team-Nebenstellen aus der Provisionierung oder von Hand gepflegt — mit **Nebenstelle und Handynummer**, in **eigenen Gruppen** (ADR-041), die sich **einzeln zuklappen** lassen und so bleiben (ADR-063). Ein Kollege lässt sich **dorthin ziehen, wo er stehen soll** — in eine andere Gruppe oder an eine andere Stelle derselben, und auf einen Gruppenkopf gezogen ganz nach oben (ADR-065). **Beim Ziehen weichen die anderen aus**, die Zeile steht also schon dort, wo sie landet; beim Loslassen wird nicht mehr gerechnet, sondern geschrieben, was dasteht (ADR-066). Das Ablegeziel ist dabei die **Liste** und nicht die einzelne Zeile — dazwischen lag totes Gebiet, und ein Zug, der dort endete, ging stumm verloren. Wer keine Maus benutzt, nimmt „In Gruppe verschieben" im Kontextmenü (ADR-042). Persönliche Kontakte aus Outlook über COM, kein Cloud-Zugriff — **Achtung:** das *neue* Outlook (`olk.exe`) bietet kein COM an, dort bleiben sie aus. **Ein angeklickter Kontakt klappt in der Zeile auf** — in allen drei Listen gleich (ADR-048) |
| **Breites Fenster** | Ab rund 960 Pixeln zwei Spalten: links wählen und der gewählte Bereich, rechts die Nebenstellen als **Kacheln** mit Name, Firma, Zustand und jeder Nummer einzeln wählbar (§23, ADR-047). Umsortieren per Ziehen funktioniert dort wie in der Liste — **seit ADR-065 über denselben Weg**, nachdem es im Raster vorher gar nicht ging —, und **eine Suche filtert die Kacheln mit** — bei vierzig Nebenstellen wäre Scrollen sonst der einzige Weg (ADR-051). Seit ADR-052 ist die Kachelspalte **nicht mehr gedeckelt** — auf einem maximierten Fenster stehen sechs bis sieben Kacheln je Reihe |
| **Kontaktdetails** | Ein Klick klappt einen Bereich **direkt unter der angeklickten Zeile** auf, mit **jeder Nummer einzeln wählbar** — in allen drei Listen gleich, auch unter 137 Outlook-Kontakten (ADR-048). Der Teilbaum entsteht erst beim Aufklappen, deshalb kostet er die übrigen Zeilen nichts. Ein zweiter Klick schliesst ihn, der Doppelklick wählt weiterhin. Im breiten Fenster brauchen die Nebenstellen ihn gar nicht: auf der Kachel steht schon alles |
| **Namensauflösung** | Eine Nummer wird zum Namen — **ein- und ausgehend gleich**, und in Toast, Gesprächsansicht, Gesprächsleiste, Anrufliste und Infobereich aus **einer** Stelle (ADR-043). Zuerst die eigenen Kontakte, dann was ein externes System weiss; trifft dessen Antwort später ein, wird der Name überall nachgezogen. Eine Nummer erscheint **nie als Name**, und eine Zeile bleibt **nie leer**. Auch die **Handynummer** eines Kollegen wird erkannt |
| **Besetztlampenfeld** | Team-Nebenstellen zeigen frei / im Gespräch / offline — farblich **und** als Text. Gegen `pbx.example.ch` gemessen ([docs/blf-pruefung.md](docs/blf-pruefung.md)); „klingelt" hängt davon ab, was die Anlage meldet |
| **Anrufliste** | Name, Ergebnis, Dauer, **die gewählte Nummer** und die Uhrzeit — bei einem Kollegen mit Festnetz und Mobil stand sonst zweimal derselbe Name da, und der Doppelklick rief irgendeine der beiden zurück (ADR-050). Filter, Suche, Rückruf per Doppelklick, Kontextmenü mit Kopieren, Übernehmen ins Team und Aufnahme zeigen |

### Externe Systeme anbinden

Eine **generische** Anbindung an CRM, ERP, Ticketing und beliebige REST-APIs (§21) — keine fest verdrahtete Integration, sondern eine Konfigurationsdatei je Quelle.

| | |
|---|---|
| **Anruferkontext** | Beim Klingeln fragt nipp alle eingerichteten Quellen **gleichzeitig**, mit Zeitgrenze und Schutzschalter je Quelle. Die Gesprächsansicht erscheint sofort; was ankommt, wird nachgereicht |
| **Anruferkarte** | **Selbst zusammenstellbar** in einem eigenen Fenster: Felderpalette, Aufbau, Vorschau mit echten Daten, Rückgängig. Aus einer festen Bausteinmenge und einer eingeschränkten Ausdruckssprache — **kein freies HTML, kein freies XAML, keine Script-Engine** (§21.6, ADR-032) |
| **Bedeutung statt Quellenname** | Eine Karte fragt `role('name')` — die erste Quelle nach Reihenfolge, die ein Feld dieser Bedeutung liefert. Damit trägt dieselbe Karte bei jedem Kunden, ohne angefasst zu werden |
| **Quelle hinzufügen** | Ein Katalog in nipp: *Eigene REST-API* für alles Neue, dazu jeden Anbieter, dessen Vorlage importiert wurde (ADR-040). Hinzufügen nimmt nichts weg — weder eine andere Quelle noch die eigenen Einstellungen |
| **Anbietervorlagen** | Eine Vorlage ist **eine** JSON-Datei: Adresse, Anmeldeart, Endpunkte, Feldpfade, Beschriftung der Zugangsdaten, erfundene Beispielantwort. Sie wird importiert und enthält **keinen Zugangsschlüssel** — eine, die einen enthält, wird abgelehnt |
| **Kontaktsuche** | Über mehrere Quellen zugleich, entprellt, mit Schutz gegen überholende Antworten und konservativem Zusammenführen nach Nummer und E-Mail |
| **Geheimnisse** | Nie in der Konfigurationsdatei, immer im `SecretStore` unter DPAPI. Das Anmeldeschema steht daneben in der Konfiguration (`auth.scheme`), nie im Geheimniswert |
| **Telefonieren bleibt unabhängig** | Fällt jedes externe System aus, klingelt und wählt nipp unverändert. Ein Lookup wird im Ereignis angestossen, nie erwartet |

Im Betrieb angebunden sind ein **CRM** (Kontakte, Kunden, letzte Stundeneinträge) und ein **Gesprächsjournal** (worüber zuletzt gesprochen wurde, Stimmungsverlauf) — beides über Vorlagen, die nicht Teil dieses Repos sind. Wie eine Quelle eingerichtet wird, vom Token an: [Integrationen einrichten](#integrationen-einrichten).

### Sich in Windows einfügen

| | |
|---|---|
| **Infobereich** | Schliessen beendet nipp nicht — es läuft weiter und nimmt Anrufe an. **Beim ersten Mal sagt es das auch**, als Sprechblase am Symbol, und danach nie wieder. Im Menü: Öffnen, Stumm schalten, das **Wiedergabegerät** (ab zwei Geräten) und Beenden |
| **Toast** | Eingehender Anruf mit **Annehmen (grün)** / **Ablehnen (rot)**, auch ohne offenes Fenster. Die Farben setzt Windows nur, wenn es sie kennt — sonst erscheint der Toast einfarbig und vollständig bedienbar. Als Anruf-Szenario angemeldet: bleibt stehen, bis jemand reagiert |
| **Toast mit Anruferkontext** | Er erscheint **sofort** mit Name oder Nummer und wird ersetzt, sobald eine Quelle antwortet — gewartet wird nie (ADR-030). Windows nimmt genau drei Textzeilen plus die Nummer klein darunter. Die Zeilen sind über denselben Designer zusammenstellbar wie die Karten (ADR-034); ohne eigene Karte gilt die mitgelieferte Zusammensetzung. Beim Anrufende wird der Toast **entfernt** — was darin steht, bliebe sonst im Benachrichtigungscenter liegen |
| **Klick-to-Call** | `tel:`, `sip:`, `sips:`, `callto:` — ein Link aus Outlook, dem Browser oder dem CRM wählt direkt |
| **Tastenkürzel** | Systemweit, Standard `Strg+Umschalt+A`. Klingelt es, nimmt es an; läuft ein Gespräch, legt es auf; sonst holt es nipp nach vorn (ADR-013). Im Fenster: `Strg+F` ins Nummernfeld (ADR-046). **`Strg+1` bis `Strg+4` gab es bis zum 23.09.2026 nur im ToolTip** — die Tastenkombination erreicht die Seite gar nicht, weder als Accelerator noch über `KeyDown`; gemessen und dann gestrichen, statt ein Versprechen stehen zu lassen |
| **Autostart** | Mit Windows, minimiert im Infobereich |
| **Eine Instanz** | Ein zweiter Start aktiviert die laufende |
| **Immer im Vordergrund** | Einschaltbar — das Fenster bleibt über allen anderen |
| **Erscheinungsbild** | Hell, dunkel oder wie Windows (Standard, zieht bei Umstellung mit) |

### Verwalten

| | |
|---|---|
| **Bis zu zehn Konten** | Gleichzeitig registriert, das für ausgehende Anrufe ist wählbar |
| **Provisionierung** | Ein XML-Profil richtet Konten, Team und Einstellungen ein — mitgeliefert oder vom Server, dort nur über https (ADR-012). Ohne Passwort im Profil gilt das lokal hinterlegte. **Was der Benutzer selbst eingestellt hat, bleibt** (ADR-054): das Profil setzt nur, was er nie angefasst hat. Ein **gesperrtes** Feld gewinnt immer — das ist der Weg, einen verstellten Arbeitsplatz zurückzuholen |
| **Gesperrte Felder** | Die Administration kann Einstellungen festlegen (Bedienschutz, keine Sicherheitsgrenze) |
| **Karten-Designer** | Ein eigenes Fenster für die Karte im Gespräch, die kompakte beim Klingeln, die Benachrichtigung und die Anrufliste. Die Vorschau ist genau so breit wie die echte Karte, zeigt aufgelöste Werte statt Ausdrücke — und rechnet mit einer **eingegebenen Rufnummer**, zu der sich ein **echter Abruf** auslösen lässt. Erreichbar über die Gruppe „Anruferkarte" **oder per Rechtsklick auf die Karte im Gespräch** (ADR-051); **er fragt vor dem Schliessen**, und der Fenstertitel trägt einen Punkt, solange etwas offen ist (ADR-049) |
| **Testabruf je Quelle** | Rohantwort und gemappte Felder nebeneinander — daran zeigt sich, ob ein Feldpfad stimmt. Die Antwort bleibt im Arbeitsspeicher und dient dort als Vorschaudaten für den Designer |
| **Aktualisierung** | Beim Start **und danach täglich** wird nachgesehen; geladen wird nie von selbst (ADR-039). nipp lebt im Infobereich und läuft wochenlang durch — eine Prüfung nur beim Start erreichte solche Arbeitsplätze nie |
| **Diagnosepaket** | Logs, Konfiguration ohne Passwörter, Systeminfo als ZIP — ein Test weist nach, dass keine Passwörter darin landen |
| **Jede Änderung wirkt sofort** | Kein „Speichern"-Knopf (ADR-045). Schalter, Auswahllisten und Sammlungen wirken mit der Änderung, Freitext- und Zahlenfelder beim Verlassen des Feldes. Geschrieben wird nur, was die Prüfung durchlässt — sonst steht die Beanstandung da und die Datei bleibt, wie sie war |
| **Zwei Ebenen** | Oben Konto, Audio, Darstellung, **Anruferkarte**, Kontakte, Start und Bedienung, Aktualisierung. Unten ein Aufklapper **„Für Administratoren"** mit Netzwerk, Codecs, **den Quellen**, Provisionierung und Diagnose, Sichern und Zurücksetzen (ADR-046, ADR-051). Die Karte steht oben, weil der Benutzer sie bei jedem Anruf sieht; was eine Quelle einrichtet — Basisadresse, Token, rohes JSON — steht unten |

### Was nipp bewusst nicht kann

Video, Chat, Konferenz mit mehr als zwei Teilnehmern, Voicemail abspielen, Anrufe parken, Warteschlangen. Nichts davon ist geplant — die Anlage kann das, und ein Softphone, das alles nachbaut, kann am Ende nichts richtig.

**Und eines, das naheliegt und noch offen ist:** ein „Nicht stören" für die
Besprechung. Es steht in keinem Paragrafen der Spezifikation, und seit nipp
sich beim Klingeln nicht mehr in den Vordergrund drängt (ADR-049), bleibt als
Anlass nur noch der Klingelton. Der Entscheid steht aus — siehe
[UX-REVIEW-2.md](docs/plans/UX-REVIEW-2.md), Befund C15.

---

## Schnellstart

Für jemanden, der nipp benutzen und nicht daran arbeiten will:

1. **`nipp-win-stable-Setup.exe` ausführen** (siehe [docs/updates.md](docs/updates.md)). Keine Adminrechte nötig, keine Voraussetzungen — .NET und das Windows App SDK sind im Paket. Beim ersten Start meldet Windows **„Unbekannter Herausgeber“**: das Signaturzertifikat fehlt noch (AP9.2), „Weitere Informationen“ → „Trotzdem ausführen“.
2. Beim ersten Start steht oben **„Noch kein Konto eingerichtet"** mit der Schaltfläche **Konto einrichten** — ein Klick öffnet die Einstellungen, klappt **SIP-Konten** auf und setzt den Fokus ins erste Feld.
3. Benutzername, Domäne und Passwort eintragen, Transport wählen, **Hinzufügen**. Steht der Knopf grau, sagt die Zeile darunter, welches Feld noch fehlt.
4. Fertig — **es gibt keinen „Speichern"-Knopf**: jede Änderung wirkt, sobald sie gemacht ist (ADR-045). Die LED oben wird grün, sobald die Anlage antwortet.

Ist ein Provisionierungsprofil eingerichtet, entfallen die Schritte 2 bis 4 — die Konten sind dann schon da.

---

## Integrationen einrichten

Wie das CRM und das Gesprächsjournal an nipp kommen — vom Token bis zum ersten Anruf,
der eine Karte zeigt. Der Weg ist derselbe für jede andere REST-Quelle.

**Ohne eingerichtete Quelle ändert sich an nipp nichts.** Kein Suchfeld im
Kontakte-Tab, keine Anruferkarte, keine Anfrage nach draussen. Telefonieren
hängt an keiner Integration (§21.2).

### 1. Ein Token besorgen

Das passiert **im Zielsystem**, nicht in nipp.

Zwei Dinge gelten für beide Quellen. **Das Token gehört an den Arbeitsplatz,
nicht an eine Person** — es soll einen Passwortwechsel überstehen und nicht mit
einem Konto verschwinden, das jemand beim Austritt schliesst. Und es braucht
**nur Lesezugriff**: nipp schreibt in kein Fremdsystem, weder heute noch
geplant.

**Wo das Token herkommt, steht in der Anbietervorlage** — nicht hier. Jede
Vorlage bringt zu jedem Geheimnis eine Beschriftung und einen Herkunftshinweis
mit, und der erscheint im Formular direkt über dem Eingabefeld. Das ist der
Schritt, der ausserhalb von nipp passiert, und der einzige, bei dem eine
Anleitung wirklich hilft; deshalb steht sie dort, wo sie zum System gehört.

Typische Wege, in dieser Reihenfolge: im **Profil** des Zielsystems (meist
„API-Token" oder „Persönlicher Zugriffsschlüssel"), in der **Administration**,
wenn Token dort zentral vergeben werden, oder **über die API selbst**, wenn eine
Anmeldung einen Schlüssel ausstellt. **Ein Wert erscheint oft nur einmal, beim
Anlegen** — wer ihn dann nicht einträgt, legt einen neuen an.

> **Und die Kopfzeile lügt manchmal.** Es gibt Server, die bei einem `401` mit
> `WWW-Authenticate: Bearer` antworten und **ausschliesslich `Token`
> akzeptieren** — das Django REST Framework tut das in seiner verbreitetsten
> Anmeldung. Der Antwort ist an dieser Stelle nicht zu glauben; dafür gibt es
> `auth.scheme` (Standard `Bearer`), und ausprobieren geht schneller als
> nachlesen. **Das Schema gehört nie in den Geheimniswert** — sonst steckt ein
> Teil des Protokolls in der verschlüsselten Ablage, wo es niemand vermutet.

### 2. Die Quelle hinzufügen

**Einstellungen → Integrationen → Quelle hinzufügen.** Dort steht ein Katalog
(§21.6, ADR-033, ADR-040): *Eigene REST-API* für alles, was nipp noch nicht
kennt — und jeder Anbieter, dessen **Vorlage** importiert wurde. Eine
Anbietervorlage ist eine JSON-Datei, die eine Schnittstelle beschreibt;
eingelesen wird sie über *API-Anbieter importieren …*, und einen
Zugangsschlüssel enthält sie nicht.

**Die Quelle kommt abgeschaltet herein** und wird nach einem erfolgreichen
Testabruf eingeschaltet; ein Test erzwingt das. Hinzufügen nimmt nichts weg —
weder eine andere Quelle noch die eigenen Einstellungen.

**nipp bringt genau eine Vorlage mit:** *Eigene REST-API*, eingebettet unter
`Services/Integrations/Catalog/templates/`. Alles Weitere kommt über
*API-Anbieter importieren …* und liegt danach unter
`%APPDATA%\nipp\connectors`.

**Für die Verteilung** an mehrere Arbeitsplätze bleibt der alte Weg —
**Ausgeben** und **Einlesen**. Achtung: Einlesen ersetzt **alle** Quellen; nipp
fragt vorher. Stimmt an der Datei etwas nicht, wird es gemeldet und **nichts
übernommen**: eine kaputte Datei ändert die laufende Konfiguration nicht. Ziel
ist `%APPDATA%\nipp\integrations.json`, dort auch direkt bearbeitbar.

### 3. Das Token ablegen

Im **Detail der Quelle**, gleich oben. Das Feld trägt seit dem 07.09.2026 die
**Beschriftung** („API-Token") und darunter den Hinweis, **woher es kommt** —
statt eines freien Felds für den technischen Verweis, den man vorher im JSON
nachlesen musste.

Der Wert wird verdeckt eingegeben, nie angezeigt und nie protokolliert. Er
landet unter DPAPI in `%LOCALAPPDATA%\nipp\secrets.dat`, verschlüsselt und an
dieses Windows-Benutzerkonto gebunden.

**Das Schema gehört nicht in den Wert.** Es hat sein eigenes Feld unter
*Verbindung und Anmeldung*: leer heisst `Bearer`, sonst steht der angegebene
Wert davor (das CRM verlangt `Token`). Wer es in den Geheimniswert schreibt,
versteckt einen Teil des Protokolls in der verschlüsselten Ablage, wo es
niemand mehr sieht und niemand korrigieren kann.

Fehlt das Token, wird die Quelle **übersprungen** statt gefragt: eine Anfrage
mit leerem Schlüssel liefert `401` und sieht aus wie ein kaputter Server.

### 4. Testabruf, bevor eingeschaltet wird

**Einstellungen → Integrationen → Verbindung testen.** Eine **Testnummer**
eintragen, die im Zielsystem existiert, und **Abruf ausführen**. Danach stehen
dort nebeneinander:

- die **Antwort im Rohzustand** — daraus entstehen die Feldpfade,
- die **gemappten Felder** — daran zeigt sich, ob die Pfade stimmen,
- Status und Dauer, etwa `HTTP 200 in 143 ms, 12 Felder gemappt`.

Ein Feld, das leer bleibt, obwohl in der Antwort etwas steht, hat einen
falschen Pfad. Genau dafür stehen beide nebeneinander. Datei ändern, erneut
einlesen, erneut testen.

### 5. Einschalten und am Telefon prüfen

Erst jetzt: der Schalter rechts neben der Quelle in der Liste.

- **Anruferkontext** — anrufen lassen. Der Name steht sofort da, aus den
  eigenen Kontakten; die Angaben aus dem Fremdsystem kommen nach. Unter der
  Karte steht, was jede Quelle gerade tut.
- **Kontaktsuche** — im Kontakte-Tab erscheint jetzt ein Suchfeld. Lokale
  Treffer sind sofort da, die fremden kommen dazu.
- **Interne Nebenstellen zeigen nichts.** Das ist Absicht: für einen Kollegen
  auf Nebenstelle 151 hat kein CRM eine Antwort.

**Was auf der Karte wo steht, bestimmen Sie selbst.** Einstellungen →
Integrationen → Anruferkarte → *Bearbeiten*. Der Designer ist ein eigenes
Fenster, seine Vorschau ist genau so breit wie die echte Karte und zeigt Ihre
eigenen Werte, sobald ein Testabruf gelaufen ist:
[docs/integrations/karten.md](docs/integrations/karten.md).

### 6. Auf weitere Arbeitsplätze verteilen

Die Datei auf einen https-Server legen und im Provisionierungsprofil eintragen:

```xml
<integrations src="https://prov.example.ch/integrations.json"/>
```

**Nur https, ohne Ausnahme** — anders als bei der Provisionierung selbst gibt es
hier keinen Schalter. Diese Datei bestimmt, welche fremden Adressen nipp mit
Rufnummern beliefert; wer sie unterwegs austauschen kann, leitet die
Kundendaten eines ganzen Betriebs um.

**Die Karten gehen mit.** Sie stehen in derselben Datei; wer eine
Anruferkarte zusammenstellt, verteilt sie damit an alle Arbeitsplätze.

**Die Token gehen nicht mit.** Sie sind an Gerät und Benutzerkonto gebunden und
auf jedem Arbeitsplatz einmal einzutragen — genau wie das SIP-Passwort. Das
gilt auch nach einem Gerätewechsel.

### Wenn etwas nicht geht

| Was man sieht | Woran es liegt |
|---|---|
| Kein Suchfeld im Kontakte-Tab | Keine Quelle mit `searchContacts` eingeschaltet |
| Karte bleibt leer | Keine Quelle mit `lookupByPhone`, oder `callerLookup.enabled` steht auf `false` |
| „Zugangsdaten fehlen" | Schritt 3 |
| „lehnt die Anmeldung ab (401)" | Falsches Token — oder das falsche `scheme`. Bei das CRM ist es `Token`, bei das Gesprächsjournal `Bearer`, und **der Antwort ist nicht zu glauben** |
| „antwortet nicht" | `timeoutMs` erhöhen. Aber beim Anruferkontext zählt jede Sekunde |
| „hat kein JSON geliefert" | Meist eine Anmeldeseite in HTML. Adresse und Pfad prüfen |
| „wird gerade nicht gefragt" | Der Schutzschalter. Nach fünf Fehlern in Folge pausiert die Quelle eine Minute |
| Nach dem Einlesen einer Datei fehlt eine Quelle | Einlesen ersetzt **alle** Quellen. Eine einzelne kommt über *Quelle hinzufügen* |
| Die Karte zeigt einen Wert nicht | Meist ein Feldname ohne Bedeutung. Der Testabruf zeigt die gemappten Felder — genau das sieht die Karte. Tabelle: [feldnamen.md](docs/integrations/feldnamen.md) |
| Der Toast zeigt nur die Nummer, die Karte aber den Namen | Das Namensfeld heisst nicht wie im Katalog. Der Toast fragt ausschliesslich über Bedeutungen |

Ausführlich, mit dem Aufbau der Konfigurationsdatei:
[docs/integrations/einrichten.md](docs/integrations/einrichten.md).

**Was nicht protokolliert wird:** Rufnummern, Suchtexte, Namen, Adressen mit
Parametern und Antwortinhalte (§21.2). Ein Softphone, das mitschreibt, wer
angerufen hat und was das CRM dazu wusste, führt ein Bewegungsprofil mit
Kundendaten. Das Diagnosepaket enthält Quellen, Zustände und Befunde — ohne
Schlüssel, ohne Endpunktpfade, ohne Anfrageparameter.

---

## Entwicklungsmaschine einrichten

Von einer leeren Windows-Maschine bis zum laufenden nipp.

### 1. Werkzeuge

| Was | Warum | Woher |
|---|---|---|
| **.NET 8 SDK (x64)** | Das Zielframework. **Ausdrücklich x64**, auch auf ARM64 | [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Windows SDK** | `signtool.exe` fürs Paketieren, `makeappx` | Im Visual Studio Installer oder einzeln |
| **Visual Studio 2022** *(optional)* | Für XAML-Designer und Debugger. Workload „Windows-Anwendungsentwicklung" | [visualstudio.microsoft.com](https://visualstudio.microsoft.com/) |
| **Git** | | [git-scm.com](https://git-scm.com/) |

Ohne Visual Studio geht alles über die Kommandozeile; die Skripte im Repo setzen es nicht voraus.

**Auf einer ARM64-Maschine** (die aktuelle Entwicklungsmaschine ist eine, siehe [ADR-001](docs/decisions.md)): das x64-SDK liegt getrennt unter `C:\Program Files\dotnet\x64\dotnet.exe` und ist vom `dotnet` im PATH nicht sichtbar. Deshalb gibt es `build.ps1`, das den richtigen Host auflöst. **Immer darüber bauen.**

### 2. Entwicklermodus einschalten

Ohne ihn lässt sich ein unsigniertes Paket nicht registrieren (Fehler `0x80073CFF`):

Einstellungen → System → Für Entwickler → **Entwicklermodus** ein.

### 3. Repo klonen

```powershell
git clone https://github.com/bv2-GmbH/nipp-softphone.git
cd nipp
```

### 4. Das Linphone SDK beschaffen

**Das SDK liegt nicht im Repo** — 299 MB, gitignoriert. Es muss einmal herunter:

```powershell
# 299 MB, dauert etwa eine Minute
Invoke-WebRequest `
  -Uri 'https://download.linphone.org/releases/windows/sdk/linphone-sdk-win64-5.5.18.zip' `
  -OutFile 'sdk\linphone-sdk-win64-5.5.18.zip'

Expand-Archive -Path 'sdk\linphone-sdk-win64-5.5.18.zip' -DestinationPath 'sdk\extracted'
```

Danach muss es liegen:

```
sdk\extracted\linphone-sdk\win64\
  bin\                                 32 native DLLs
  lib\mediastreamer\plugins\            2 Plugins (WASAPI, AAC)
  share\belr\grammars\                  8 Grammatikdateien
  share\linphonecs\LinphoneWrapper.cs   der C#-Wrapper, 62'253 Zeilen
```

Prüfen:

```powershell
Test-Path 'sdk\extracted\linphone-sdk\win64\bin\liblinphone.dll'
(Get-ChildItem 'sdk\extracted\linphone-sdk\win64\share\belr\grammars' -Filter *.belr).Count  # 8
```

SHA256 und Einzelheiten in [docs/sdk-setup.md](docs/sdk-setup.md).

> **Warum nicht über NuGet?** War der Plan (ADR-002), scheiterte an der Verfügbarkeit des GitLab-Feeds — Timeouts und HTTP 500 (ADR-005). Das ZIP ist der zuverlässige Weg.

### 5. Bauen und starten

```powershell
.\build.ps1 build Nipp.sln -c Debug
.\build.ps1 test Nipp.sln
```

Starten, unpackaged (schnell, zum Entwickeln):

```powershell
.\build.ps1 --% build src\Nipp.App -c Debug -p:WindowsPackageType=None -t:Rebuild
.\src\Nipp.App\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\Nipp.App.exe
```

> **`--%` ist ebenfalls nicht optional.** Ohne den Stopp-Parser bindet
> PowerShell `-p:WindowsPackageType=None` an einen eigenen Parameter und
> bricht mit *parameter name 'p' is ambiguous* ab, bevor der Build
> beginnt.

> **`-t:Rebuild` ist bei unpackaged nicht optional.** Ein inkrementeller Build erzeugt eine App, die beim Start mit `REGDB_E_CLASSNOTREG` stirbt. Zweimal reproduziert, die Ursache ist nicht abschliessend geklärt; die Regel wirkt. Ausführlich in [docs/packaging.md](docs/packaging.md).

Starten, packaged (näher an der Auslieferung, für Toasts und Single-Instance nötig):

```powershell
.\build.ps1 build src\Nipp.App -c Debug
Add-AppxPackage -Register src\Nipp.App\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\AppxManifest.xml
Start-Process "shell:appsFolder\bv2.nipp_b06ws4ca5ehbg!App"
```

### 6. Zugangsdaten für Tests

**Niemals gegen einen Kundentenant testen** (§13). Nur der Test-Trunk.

Die Zugangsdaten gehören in eine Datei **ausserhalb des Repos**:

```
%LOCALAPPDATA%\nipp\test-trunk.json
```

```json
{
  "username": "…",
  "domain": "pbx.example.ch",
  "password": "…",
  "transport": "Udp"
}
```

### 7. Beim Entwickeln zusehen

```powershell
# Log mitlesen, während die App läuft
.\tools\Watch-NippLog.ps1
```

Das Log liegt unter `%LOCALAPPDATA%\nipp\logs\`.

---

## Aufbau

```
┌──────────────────────────────────────────────────────┐
│  Nipp.App          WinUI 3, Fenster, Seiten, Tray    │
│                    kennt keine SDK-Typen             │
├──────────────────────────────────────────────────────┤
│  Nipp.Core         ViewModels, Dienste, Modelle      │
│    ViewModels/         MVVM, SDK-frei                │
│    Services/                                         │
│      Telephony/        ── die einzige Schicht mit ───┼── using Linphone
│      Settings/         Einstellungen, Provisionierung│
│      Contacts/         Outlook, CLIP, BLF            │
│      History/          SQLite                        │
│      Windows/          Autostart, tel:, Hotkey       │
├──────────────────────────────────────────────────────┤
│  Linphone SDK 5.5.18   32 native DLLs + 2 Plugins    │
└──────────────────────────────────────────────────────┘
```

**Die eine Regel:** `using Linphone` gibt es ausschliesslich unter `src/Nipp.Core/Services/Telephony/`. Kein `Linphone.Call`, kein `Linphone.Core` verlässt diese Schicht — alles darüber sieht nur eigene Typen (`CallInfo`, `AccountStatus`, `CallHandle`).

Ein Architekturtest erzwingt das. Er ist nicht Zierde: er ist der Grund, warum ein SDK-Wechsel oder ein Mock für Tests überhaupt möglich bleibt.

### Die Ereignisschleife

Das Linphone SDK verarbeitet auf dem Desktop **nichts von selbst**. `Core.Iterate()` muss regelmässig gerufen werden, sonst kommen keine Netzwerkereignisse an und keine Callbacks feuern.

nipp tut das alle 20 ms über einen `DispatcherQueueTimer` auf dem UI-Thread (`SipPumpHost`). Daraus folgt:

- Alle SDK-Aufrufe und alle Callbacks laufen auf dem UI-Thread.
- **Nichts Blockierendes in einem Callback.** Ein `await` auf eine Netzwerkoperation friert die Oberfläche ein.
- Der Timer hängt am App-Lebenszyklus, nicht am Fenster — nipp bleibt im Infobereich aktiv.

---

## Die Komponenten im Einzelnen

### Telefonie

| Datei | Was sie tut |
|---|---|
| `SipService.cs` | **Die eine Klasse, die den Core besitzt.** Konten, Anrufe, Audio, Präsenz-Abonnements |
| `SipEventBridge.cs` | **Der einzige Ort, der `Core.Listener` setzt.** Das SDK hat genau *einen* Listener mit 97 Delegaten — wer sie anderswo überschreibt, hängt die Bridge stillschweigend ab |
| `NumberNormalizer.cs` | Reine Funktion, 30 Tests. Notrufnummern bleiben unverändert |
| `AudioGain.cs` | 0–100 der Oberfläche ↔ Dezibel des SDK |
| `SettingsApplier.cs` | Überträgt Einstellungen in den laufenden Core, ohne Neustart |
| `SdkLoadProbe.cs` | Prüft vor dem ersten Zugriff, ob die native Kette vollständig ist — mit einer Meldung, die sagt *was* fehlt |
| `SipErrorCatalog.cs` | SIP-Fehlercodes als Sätze, die eine Ursache und eine Abhilfe nennen |
| `SdkLogBridge.cs` | Leitet die SDK-Meldungen ins Protokoll — bei Debug mit SIP-Nachrichten im Klartext |
| `RootCertificates.cs` | Schreibt die Wurzelzertifikate von Windows als PEM-Bündel. Das SDK will eine **Datei**, keinen Zertifikatspeicher (§14.6) |
| `PhoneNumberFormat.cs` | `+41786672728` → `+41 78 667 27 28`. Lieber ungruppiert als falsch gruppiert |

### Einstellungen und Provisionierung

| Datei | Was sie tut |
|---|---|
| `SettingsService.cs` | Lädt und speichert `settings.json`. Ein kaputte Datei kostet nicht den Start |
| `SecretStore.cs` | Passwörter über DPAPI, an den Windows-Benutzer gebunden. Nie im Klartext auf der Platte |
| `ProvisioningParser.cs` | Liest das Profil-XML. DTD aus, kein `XmlResolver` — das XML kommt von aussen |
| `ProvisioningService.cs` | Factory-Config und Remote-Profil, in dieser Reihenfolge. Ein Fehler verhindert den Start nicht |
| `PolicyService.cs` | Gesperrte Felder. **Bedienschutz, keine Sicherheitsgrenze** — der Satz steht im Code, weil er leicht vergessen wird |

### Kontakte

| Datei | Was sie tut |
|---|---|
| `OutlookContactSource.cs` | Späte COM-Bindung an ein **laufendes** Outlook, eigener STA-Thread, 60 Sekunden Grenze. Startet Outlook nicht selbst (§8.4); ein verspätetes Ergebnis wird aufgehoben, nicht verworfen |
| `TeamContactSource.cs` | Nebenstellen aus den Einstellungen — sofort da, kein Netz |
| `ContactStore.cs` | Beide Quellen, zwölf Stunden Zwischenspeicher, Suche über Namen und Nummern |
| `ClipResolver.cs` | Nummer → Name **aus den eigenen Kontakten**. Vergleicht die Ziffern von hinten, ohne führende Null. Wer den Namen des Gesprächspartners braucht, fragt nicht hier, sondern `CallPartyResolver` — der kennt auch die externen Quellen |
| `BlfService.cs` | Präsenz — **nur für Team-Nebenstellen**. Jedes Abonnement kostet die Anlage ein dauerhaftes SUBSCRIBE |
| `SipUri.cs` | Bringt `"152" <sip:152@pbx>` und `sip:152@pbx` auf eine Form. Ohne das blieben alle Lampen grau |

### Integrationen (§21)

Der grösste Teilbereich — 50 Dateien unter
`src/Nipp.Core/Services/Integrations/`. Er kennt **weder das SDK noch WinUI**;
ein Architekturtest erzwingt das.

| Datei | Was sie tut |
|---|---|
| `Config/IntegrationConfigStore.cs` | `integrations.json`: atomar schreiben, kaputte Datei beiseitelegen, Schemaversion. Dazu `AddOrReplaceSource` — **eine** Quelle einfügen und die anderen unberührt lassen |
| `Config/IntegrationConfigValidator.cs` | Alle Befunde auf einmal, mit Pfad und Abhilfe (§15). Wer fünf Fehler hat, will nicht fünfmal speichern |
| `Http/IntegrationHttpClient.cs` | Zeit- und Grössengrenze, Schutzschalter je Quelle, Anmeldung aus dem `SecretStore` |
| `Mapping/MappingEngine.cs` | JSONPath auf die Antwort. Die einzige Stelle mit `using Json.Path` |
| `Expressions/` | Eine eingeschränkte Ausdruckssprache — geschlossene Funktionsmenge, keine Methodenaufrufe, keine Reflection. Sie ist die Sicherheitsgrenze (ADR-016) |
| `Context/CallerContextService.cs` | Fragt alle Quellen **gleichzeitig**, reicht Ergebnisse nach, Zwischenspeicher nur im Arbeitsspeicher |
| `Cards/CardLayoutEngine.cs` | Übersetzt eine Karte einmal, löst sie bei jeder Antwort auf. Der Renderer entscheidet nichts |
| `Cards/FieldCatalog.cs` | **Die einzige Stelle**, die entscheidet, welcher Feldname was bedeutet. Grundlage von `role(...)`, des Toasts und der Designer-Palette |
| `Cards/CardResolver.cs` | Welche Karte je Art gilt: die eigene, sonst die mitgelieferte — bei einem Fehler die mitgelieferte **plus einen Befund** |
| `Cards/ToastComposer.cs` | Die drei Zeilen des Toasts. Liegt im Kern, weil `Nipp.App` kein Testprojekt hat |
| `Cards/ContextRoles.cs` | Der Wert zu einer **Bedeutung** statt zu einem Feldnamen, Feld vor Quelle. Toast und Gesprächsansicht teilen sich die Regel — zwei Kopien wären zwei Gelegenheiten, sie falsch zu haben |
| `Context/CallPartyResolver.cs` | **Wie der Gesprächspartner heisst — die eine Stelle** (ADR-043). Zwei Fragen: `NameOf` (Name oder nichts, nie eine Nummer) und `Describe` (nie leer). Richtungsneutral, mit Zwischenspeicher je Gespräch, und er meldet nach, wenn eine Quelle spät antwortet |
| `Catalog/ConnectorLibrary.cs` | Die Vorlagen für „Quelle hinzufügen": die mitgelieferte (eingebettet) und die importierten (im Benutzerprofil). Ein Dienst, kein statisches Singleton — mit dem Import ist der Katalog veränderlich |
| `Catalog/ConnectorTemplateReader.cs` | Liest eine Vorlage aus Text und **lehnt ab**: ein Wert, der wie ein Geheimnis aussieht, eine unbekannte Fassung, eine Datei über 256 kB. Rein, ohne Dateisystem — deshalb prüfbar |
| `Catalog/TestSampleStore.cs` | Die Antwort des letzten Testabrufs für die Kartenvorschau — **nur im Arbeitsspeicher** (§21.2) |

Der Karten-Designer liegt daneben, unter `src/Nipp.Core/ViewModels/` — er ist
ein ViewModel und keine Dienstleistung:

| Datei | Was sie tut |
|---|---|
| `CardDraft.cs` | Eine Karte, während sie bearbeitet wird. Der Rückweg vom Ausdruck in Formulare wird **zeichengleich** geprüft, damit keine Karte etwas verliert |
| `CardDesignerViewModel.cs` | Auswahl, Einfügen, Verschieben, Rückgängig, Prüfung, Vorschau. Der ganze Editor, ohne Fenster prüfbar — `CardDesignerWindow` in `Nipp.App` zeichnet nur |

### Windows-Integration

| Datei | Was sie tut |
|---|---|
| `WindowsIntegration.cs` | Autostart und Protokoll-Handler über HKCU (die unpackaged-Rückfallebene) |
| `GlobalHotkeyService.cs` | Eigenes Nachrichtenfenster auf eigenem Thread — WinUI verwirft `WM_HOTKEY` ohne Fenster |
| `TrayIconHost.cs` | Symbol im Infobereich, in der Grösse geladen, die die Shell verlangt. **Eine** Symboldatei: die hell/dunkel-Wahl gab es, und beide Dateien waren byteidentisch (W1.4) |
| `ToastService.cs` | Eingehende Anrufe als Toast, mit Aktionen |
| `ConnectivityMonitor.cs` | Netzwerkwechsel → Neuregistrierung |
| `ThemeService.cs` | Hell/dunkel/Windows, zieht bei Umstellung mit — **auch beim Kontrastmodus im laufenden Betrieb**, und unabhängig davon, ob ein festes Thema eingestellt ist |
| `WindowPlacement.cs` | Fenstergrösse auf den Arbeitsbereich begrenzt, Lage gemerkt — und die physischen Pixel von `AppWindow` umgerechnet |
| `SettingCard.cs` | Eine Einstellungszeile mit Schloss für gesperrte Felder (§17) |

### Werkzeuge

| | |
|---|---|
| `build.ps1` | Baut mit dem richtigen dotnet-Host, warnt vor der `-t:Rebuild`-Falle |
| `build\Pack-Nipp.ps1` | MSIX bauen, **native Kette im Paket prüfen**, signieren |
| `tools\Build-Icons.py` | Erzeugt alle Symbole und Kacheln aus **einer** Quelldatei: `python tools\Build-Icons.py Icons\nipp-v2-logo.png` |
| `tools\Build-Sounds.py` | Erzeugt Klingelton und Rufton (`Assets\Sounds\*.wav`) |
| `tools\Watch-NippLog.ps1` | Log mitlesen |
| `tools\Test-Blf.ps1` | Wertet aus, was die Anlage auf die Präsenz-Abonnements antwortet |
| `tools\Test-Ton.ps1` | Wertet aus, welchen Weg die Töne genommen haben — Karte, Rufzustand, Filterkette |
| `tools\Reset-IconCache.ps1` | Bringt Windows dazu, ein neues Logo neu zu lesen (Symbolcache, verwaistes Paket) |
| `nippprov` | Provisionierungsprofile erzeugen und prüfen |

---

## Wo was liegt

### Zur Laufzeit

| Pfad | Inhalt |
|---|---|
| `%APPDATA%\nipp\settings.json` | Einstellungen. Klartext, **keine Passwörter** |
| `%LOCALAPPDATA%\nipp\secrets.dat` | Passwörter, DPAPI-verschlüsselt |
| `%APPDATA%\nipp\linphonerc` | Konfiguration des SDK |
| `%LOCALAPPDATA%\nipp\logs\` | Protokolle |
| `%LOCALAPPDATA%\nipp\history.db` | Anrufliste (SQLite) |
| `%LOCALAPPDATA%\nipp\recordings\` | Aufnahmen |
| `%LOCALAPPDATA%\nipp\rootca.pem` | Wurzelzertifikate für TLS, aus dem Windows-Speicher erzeugt. Löschen erzwingt ein Neuschreiben |
| `%PROGRAMDATA%\bv2\nipp\nipp-factory.xml` | Auslieferungszustand |

### Im Repo

| Pfad | Inhalt |
|---|---|
| `NIPP-BUILD.md` | **Die Spezifikation.** Im Zweifel dort nachlesen |
| `docs/plans/IMPLEMENTATION-PLAN.md` | Phasen, Arbeitspakete, Fortschritt |
| `docs/plans/INTEGRATION-PLAN.md` | Die Integrationsplattform (§21): Analyse, Zielarchitektur, Phasen I0–I8 und der Ausbau I9–I14 |
| `docs/plans/EINRICHTUNG-PLAN.md` | Einrichtung und Karten (§21.6): Befunde, Phasen K0–K6, Umsetzungsstand |
| `CLAUDE.md` | Kurzkontext für eine neue Sitzung |
| `docs/plans/REVIEW.md` | Review vom 05.09.2026: Befunde, Umsetzung, was offen blieb |
| `docs/decisions.md` | Jede Abweichung von der Spezifikation als ADR |
| `docs/sdk-api-notes.md` | Wo die SDK-API von der Spezifikation abweicht |
| `docs/integrations/` | Anleitungen: einrichten, karten, feldnamen |
| `sdk/` | Das SDK. Gitignoriert, muss lokal beschafft werden |

---

## Bauen, testen, paketieren

```powershell
# Bauen
.\build.ps1 build Nipp.sln -c Debug

# Tests — 1274 Komponententests plus 37 Architekturtests
.\build.ps1 test Nipp.sln

# Formatieren — nur die eigenen Dateien
dotnet format --include <pfad>

# Setup bauen (der Auslieferungsweg, ADR-038)
.\build\Release-Nipp.ps1 -Version 0.9.0 -Channel stable

# MSIX (wird nicht ausgeliefert, wartet auf T110 und ein Zertifikat)
.\build\Pack-Nipp.ps1 -Version 1.0.0.0
```

**Ausgeliefert wird ein Velopack-Setup**, unpackaged und self-contained: .NET 8
und das Windows App SDK liegen im Paket, der Zielrechner braucht nichts
vorinstalliert. Updates kommen über GitHub Releases mit den Kanälen **stable**
und **beta**; beim Start wird nachgesehen, aber **nichts geladen** — das
entscheidet der Benutzer, und angewandt wird nie während eines Gesprächs.
Einzelheiten in [docs/updates.md](docs/updates.md).

**Warnungen sind Fehler.** `TreatWarningsAsErrors` ist eingeschaltet, mit strengen Analyzern. Das ist unbequem und Absicht: eine unterdrückte Warnung ist eine Entscheidung, und Entscheidungen gehören begründet — im Code oder als ADR.

---

## Fallstricke, die schon zugeschlagen haben

Keine Theorie. Jeder Punkt hat Zeit gekostet.

### Eine Meldung an das SDK ist ein Auftrag, keine Auskunft

Auf einem Arbeitsplatz mit **zwei aktiven Netzwegen** — WLAN und ein
Mobilfunkadapter — hielt die Anmeldung nicht: das Konto lief im Sekundentakt
`Ok → Progress → Failed → Ok`, und die Präsenz aller zehn Nebenstellen stand
auf «offline». Der Mobilfunkadapter wechselte seine Adresse von selbst, und
nipp reichte **jedes** Windows-Netzereignis an das SDK weiter — auch wenn sich
nichts geändert hatte. Dort ist das keine Mitteilung, sondern eine
Neuregistrierung; die Anlage sah einen Anmeldesturm.

**Die Entprellung aus dem Nachtrag zu ADR-006 fing das nicht**, und das war
kein Versäumnis, sondern eine andere Frage: sie fasst zusammen, was innerhalb
von zwei Sekunden kommt — hier kam es über Minuten verteilt. Was fehlte, war
nicht ein längeres Fenster, sondern die Frage, **ob sich überhaupt etwas
geändert hat**.

Gemeldet wird jetzt nur eine veränderte Lage, und die besteht aus der
Erreichbarkeit **und den lokalen Adressen**. Die Adressen gehören dazu: beim
Wechsel WLAN → LAN bleibt «erreichbar» true, und trotzdem *muss* neu
registriert werden — der Contact-Header trägt sonst die alte Adresse, und die
Anlage schickt eingehende Anrufe dorthin. **Eine zu grobe Bremse verschluckt
genau den Fall, für den es den Dienst gibt** (ADR-060).

### Ein Speichern, das nichts ändert, kann eine Kette ohne Ende werden

Im selben Protokoll wiederholte sich **alle neun Millisekunden** derselbe
Block: Einstellungen gespeichert, auf den laufenden Core übertragen, Autostart
geschrieben, Kürzel angemeldet, Erscheinungsbild gesetzt — und wieder
gespeichert. An `Changed` hängen vier Empfänger, und einer schrieb zurück.
**In sechs Minuten wurden daraus 248 MB Protokoll**, und jede Runde übertrug
die Einstellungen erneut auf den Core.

Wer die Kette auslöst, ist die falsche Frage: sie konnte entstehen, weil
niemand geprüft hat, ob es überhaupt etwas zu schreiben gibt. Diese Prüfung
gehört an die **eine** Stelle, durch die jeder Schreibweg läuft — nicht in vier
Empfänger, die sich daran erinnern müssten. Verglichen wird der **Text**, der
auf die Platte ginge: `NippSettings` ist ein `record`, aber seine Listen
vergleichen sich über die Referenz, und die Prüfung liefe sonst ins Leere.

### Ein `ContextFlyout` am Zelleninhalt ist mit der Tastatur unerreichbar

Die Kontextmenüs der Listen hängen am Inhalt der Zeilenvorlage — anders geht es
nicht, weil nur dort der Eintrag als `DataContext` steht. Bei Tastaturbedienung
liegt der Fokus aber auf dem `ListViewItem`, und dort findet sich kein Flyout:
**Menütaste und Umschalt+F10 griffen ins Leere.** Damit war „Anrufen" aus der
Kontaktliste ohne Maus unerreichbar, während der Kommentar daneben ausdrücklich
das Gegenteil behauptete. `ContextRequested` an der Liste löst es;
`TryGetPosition` unterscheidet Tastatur von Maus (ADR-044).

### Eine Tonfarbe als Hintergrund verlangt einen mitgesetzten Vordergrund

Verschlüsselungs-Chip und Kartenabzeichen setzten eine Statusfarbe als Fläche
und liessen den Text seine Farbe vom Thema erben. Im dunklen Erscheinungsbild
stand damit fast weisser Text auf `#FCE100` — rund **1,4:1**, praktisch
unlesbar, und zwar ausgerechnet bei „unverschlüsselt". Als **Vordergrund** sind
dieselben Pinsel in beiden Themen geprüft; der Ton steht deshalb als Rand und
Schrift da (ADR-044).

### Zwei Speichermodelle auf einer Seite sind unlernbar

Ein Konto wurde beim Klick auf „Hinzufügen" sofort geschrieben, eine
Nebenstelle nicht — sie erschien in der Liste und war beim Verlassen der Seite
lautlos weg. **Der Schaden lag nicht beim Verklicken, sondern beim Vergessen
des Knopfes.** Eine Sperrliste statt einer Erlaubnisliste entscheidet jetzt,
was beim Ändern geschrieben wird: wer dort etwas vergisst, bekommt eine
überflüssige Schreiboperation, nicht einen stillen Datenverlust (ADR-045).

### Eine Fähigkeit zu bauen heisst nicht, sie anzuschliessen

`App.SdkStatus` wurde beim Start gesetzt, als öffentliche Eigenschaft angeboten
— und von **keiner Ansicht gelesen**. Eine unvollständige Installation sah
damit aus wie „noch kein Konto eingerichtet". Dasselbe Muster wie bei
`CardKind.History`, `IntegrationConfig.cards` und `ClipResolver.DescribeCaller`
— **zum vierten Mal** (ADR-045).

**Und zum fünften Mal beim Karten-Designer** (ADR-049):
`HasUnsavedChanges` wurde an drei Stellen gesetzt und an **einer** gelesen — um
zu entscheiden, ob das Fenster nach dem Speichern zugeht. Wer es über das Kreuz
verliess, verlor eine halbe Stunde Kartenarbeit, lautlos. Der Kommentar in
`Show` behauptete seit jeher das Gegenteil: *„Es fragt seinerseits nach
ungespeicherten Änderungen."* **Bei den ersten vier fehlte eine Anzeige; hier
fehlte eine Schutzregel, und das kostet Arbeit statt Auffindbarkeit.**

**Zum sechsten und siebten Mal am 13.09.2026.** Die Standortbestimmung hat
gleich zwei gefunden, und beide standen in einem Dokument als erledigt:

- **Der SIP-Port erreichte das SDK nie.** Feld im Modell, Eingabefeld,
  Profilschlüssel, Prüfung im Validator, Neustart-Hinweis — `core.Transports`
  kam im ganzen Telefonie-Ordner nicht vor. Die einzige Fundstelle war ein
  Kommentar, der erklärte, warum die Änderung einen Neustart braucht. **ADR-019
  hatte genau diese Einstellung ins Profil verwiesen** und behauptet, sie sei
  dort umgesetzt: der Weg, auf den die Entscheidung verwies, führte ins Leere.
- **Der Rückbau beim Deinstallieren war nicht angemeldet.** Im `RELEASE-PLAN`
  stand «Velopack ruft dafür einen Hook auf; die Rückbaulogik hat
  `WindowsIntegration` bereits» — die Logik gab es, den Aufruf nicht. Nach dem
  Deinstallieren zeigten der Autostart-Eintrag und die Handler für `tel:` auf
  eine gelöschte EXE.

**Die Lehre wird mit jedem Mal schärfer:** wer eine Fähigkeit in eine andere
Ebene verweist oder einen Haken setzt, prüft, dass sie dort ankommt. Ein
Kommentar, der es behauptet, ist kein Nachweis — er ist der Grund, warum
niemand nachsieht.

### Ein Fänger ohne Protokollzeile ist eine Lücke, ein Protokoll ohne Filter ist eine andere

Zwei Befunde aus derselben Prüfung, und sie ziehen in entgegengesetzte
Richtungen.

**Erstens:** an acht Stellen las nipp eine SDK-Eigenschaft, die vor der
Verhandlung wirft — Codec, Verschlüsselung, Grund des Anrufendes, Messwerte des
Ruftons. Jede fing die Ausnahme und gab einen Ersatzwert zurück, **ohne eine
Zeile**. Das ist das Muster, das dieses Projekt zweimal teuer bezahlt hat.
Jede davon bei jedem Durchlauf zu protokollieren wäre aber Rauschen: der Pump
läuft alle 20 ms. Die Auflösung ist `QuietFailures` — **einmal je Stelle und
Sitzung**. Der erwartete Fall kostet eine Zeile beim ersten Anruf; ein *neuer*
Grund ist sichtbar, statt sich hinter einem Ersatzwert zu verstecken.

**Zweitens:** auf Stufe Debug reichte `SdkLogBridge` den SIP-Verkehr im
Klartext weiter. Im Protokoll dieser Maschine standen an einem Tag **714 Zeilen
mit Digest-Kopfzeilen und 4 282 mit Rufnummern** — und Debug ist genau die
Stufe, die der Support einschalten lässt. `LogMasking.SipLine` filtert jetzt;
was der Support braucht — Zustandsverläufe, Antwortcodes, Filterketten — bleibt
lesbar. **Eine Maskierung, die dem Support nimmt, wofür er Debug einschalten
lässt, wird abgeschaltet und schützt dann gar nichts.**

### Eine Ausnahme aus einem SDK-Callback hat keinen Fänger

Die Callbacks von liblinphone kommen aus `linphone_core_iterate` — also über
einen Reverse-P/Invoke-Rahmen. **Der `try` in `SipPumpHost.OnTick` liegt
ausserhalb davon und sieht sie nie.** An `CallStateChanged` hingen acht
Abonnenten, darunter die Seitennavigation des Hauptfensters und der
Schreibzugriff auf die Anrufliste; `LinphoneException` wurde im ganzen `src/`
**nirgends** gefangen, obwohl `Pause`, `Resume`, `SendDtmf` und `Terminate` sie
werfen. «Stumm» drücken, während die Gegenseite auflegt, war ein Absturz.

Die Grenze liegt jetzt an drei Stellen: `CallbackGuard` um jeden SDK-Callback,
eine Übersetzung von `LinphoneException` in eine Meldung, die der Benutzer
lesen kann, und ein Fänger um jeden `async void`. **`OnUnhandledException`
bleibt, wie es ist** — wer dort ankommt, hat einen Fehler, den niemand
vorhergesehen hat, und der soll nicht still weiterlaufen (ADR-053).

### Zwei richtige Entscheidungen ergeben nicht automatisch ein richtiges Ergebnis

ADR-044 hat den Fokus beim Klingeln auf „Annehmen" gelegt — richtig, denn die
zeitkritischste Handlung war die am schlechtesten erreichbare. `MainWindow`
holte das Fenster beim Klingeln in den Vordergrund — nachvollziehbar, denn nipp
lebt im Infobereich.

**Zusammen hiess das: wer in einer anderen Anwendung tippte, bekam nipp vor die
Nase, und die nächste Leertaste nahm den Anruf an.** Niemand hatte etwas
angeklickt. Keine der beiden Stellen war für sich falsch, und keine kannte die
andere. Und §8.6 sagt es wörtlich: *„Klick auf Annehmen: App in den
Vordergrund"* — der Vordergrund gehört an das **Annehmen** (ADR-049).

### Ein Feld mit zwei Bedeutungen und eine Standardtaste

Seit ADR-046 ist das Nummernfeld auch das Suchfeld. Die Eingabetaste blieb
unverändert verdrahtet: wer „Meier" tippte und Enter drückte, löste einen Anruf
an `sip:Meier@…` aus. Der `NumberNormalizer` reicht benannte Ziele absichtlich
durch, damit aus `112` nie `+41112` wird — **genau diese richtige Regel machte
aus einer Suche einen fehlgeschlagenen Anruf.**

Die Antwort steht jetzt im Kern, neben dem Normalizer: `IsDialable` ist die
Gegenfrage zu `Normalize`, aus denselben Regeln. **Eine Antwort, zwei
Wirkungen** — was Enter tut (ADR-049) und welche der beiden Trefferlisten steht
(ADR-051).

### Eine Protokollzeile, die eine Ursache nahelegt, die sie nicht gemessen hat

Seit nipp zwei systemweite Kürzel anmeldet, stand im Protokoll untereinander:

```
[WRN] Tastenkuerzel Ctrl+Shift+A ist bereits von einer anderen Anwendung belegt
[INF] Tastenkuerzel abgeschaltet
```

Die zweite Zeile gehörte zum **zweiten** Kürzel — dem leeren Stummkürzel, das
nipp erwartungsgemäss nicht anmeldet — und las sich wie die Folge der ersten.
Alle vier Meldungen des Hotkey-Dienstes nennen jetzt ihre Rolle, und zwar in
der Sprache der Oberfläche („annehmen und auflegen", „stumm schalten") statt
als Bezeichner: das Protokoll landet beim Support.

**Dasselbe Muster wie beim Rufton** (ADR-029), wo eine Zeile *„die Gegenstelle
schickt Early Media ohne Audio"* behauptete, obwohl beide Male Audio da war.

### Das SDK braucht Ressourcendateien, nicht nur DLLs

Der erste Start starb an `bctbx-fatal: Unable to load VCARD grammar`. `belr` lädt acht Grammatiken zur Laufzeit aus `share/belr/grammars/`. Die Meldung sagt nicht, dass eine *Datei* fehlt.

`build\Linphone.Sdk.targets` regelt das an einer Stelle, `Pack-Nipp.ps1` prüft es im fertigen Paket.

### Es gibt nur einen Listener pro Core

`Core.Listener` ist eine einzelne Eigenschaft mit 97 Delegaten, kein `AddListener`. Wer die Delegaten irgendwo anders setzt, hängt `SipEventBridge` ab — ohne Fehlermeldung, die Ereignisse kommen einfach nicht mehr an.

### Der Aufnahmepfad muss beim Aufbau gesetzt werden

`Call.Params` ist nur lesbar, `Call.CurrentParams` eine Momentaufnahme. Ein später gesetzter Pfad wird stillschweigend verworfen: die Aufnahme meldet Erfolg und schreibt nichts. Der Pfad gehört an `InviteAddressWithParams` beziehungsweise `AcceptWithParams`. Zweimal in die Falle getappt.

### Die nationale Null verhindert den Nummernvergleich

`+41445128430` und `0445128430` sind dieselbe Nummer, aber `41445128430` endet nicht auf `0445128430`. Der erste Testlauf des `ClipResolver` fand das — führende Nullen fallen jetzt vor dem Vergleich.

### `TwoWay` auf `ListView.SelectedItem` mit unveränderlichen Modellen

`CallInfo` ist ein Record; jede Zustandsänderung ersetzt die Instanz. Die Liste verlor ihre Auswahl und schrieb `null` zurück ins ViewModel — die Gesprächsansicht wurde beim Halten leer. Auswahl über einen Schlüssel führen, nicht über die Referenz.

**Seit dem 12.09.2026 ist die Ursache weg statt umgangen:** in der Liste steht
eine `CallRow`, die dem Gespräch über alle Zustandswechsel hinweg erhalten
bleibt. Die Führung über den Schlüssel bleibt richtig — nur muss sie jetzt
nichts mehr auffangen. **Wer ein unveränderliches Modell in eine Liste bindet,
bindet eine Identität, die es nicht gibt.**

### Eine zwischengespeicherte Seite verdrahtet sich nur einmal

`NavigationCacheMode.Required` behebt ein Speicherleck und schafft ein neues
Problem: der Konstruktor läuft genau einmal. Wer dort abonniert und in
`OnUnloaded` abmeldet, hat nach der ersten Navigation eine Seite, die aus dem
Zwischenspeicher zurückkommt und nichts mehr mitbekommt — Kontostand,
Abzeichen und Anrufliste stehen still, ohne dass etwas abstürzt. Abonnements
gehören in `OnLoaded`, gelöst wird in `OnUnloaded`.

### Kein `Accept` aus dem Zustands-Callback

Das SDK meldet daraufhin sofort die nächsten Zustände — mitten im laufenden
Aufruf. Der äussere Rahmen schreibt danach seinen längst veralteten Zustand
zurück, und die Oberfläche bietet „Annehmen" für ein Gespräch an, das
bereits steht. Vormerken und im nächsten `Pump()` ausführen.

### `GetActiveObject` will eine CLSID

Mit der ProgID als Zeichenfolge liest die Funktion die ersten sechzehn Bytes
des Textes als CLSID. Sie scheitert immer, und das Ergebnis ist von „Outlook
läuft nicht" nicht zu unterscheiden. Erst `CLSIDFromProgID`, dann
`GetActiveObject`.

### Konverter gehören in `App.xaml`

Eine Seite in einem `Frame` hat ihren eigenen Ressourcenbaum. Ein Konverter in den Fensterressourcen wird dort nicht gefunden, und die App stürzt beim Start ab.

### `Linphone.LogLevel` gegen `Microsoft.Extensions.Logging.LogLevel`

Bricht den `[LoggerMessage]`-Generator mit sieben Meldungen (CS8795), die alle nicht auf die Ursache zeigen. Log-Klassen gehören in eigene Dateien ohne `using Linphone`.

### `RuntimeIdentifier` leitet sich von der Baumaschine ab

Das WinUI-Template nimmt die Prozessarchitektur der *Baumaschine*. Auf ARM64 wird daraus `win-arm64`, und die x64-Kette lädt nicht. Fest verdrahtet und kommentiert — zweimal zugeschlagen.

### Tests auf dem Benutzerprofil

Die Einstellungstests liefen auf `%APPDATA%\nipp\settings.json` — mit der Begründung, das sei „ehrlicher als eine Abstraktion". Ein `dotnet test` hat damit das eingerichtete SIP-Konto samt Passwort gelöscht. Pfade kommen jetzt per Konstruktor, jeder Test hat ein Wegwerfverzeichnis, `TestIsolationTests` verhindert den Rückfall.

### Ein fehlender Ressourcenschlüssel ist ein Absturz

XAML löst `StaticResource` erst zur Laufzeit auf. Ein Konverter, der referenziert, aber nie definiert wurde, liess die Einstellungsseite beim ersten Öffnen sterben — der Build war wochenlang grün. `XamlResourceTests` gleicht jetzt alle Verweise ab. Typfehler (ein `x:Double` an `RowDefinition.Height`) findet er nicht; das steht als Zahl im XAML.

### Ein Themenwörterbuch nach dem Namen suchen findet das falsche

`XamlControlsResources` steht in `App.xaml` vor `Tokens.xaml` und bringt eigene ThemeDictionaries für „Light", „Dark" und „HighContrast" mit. Wer die `MergedDictionaries` nur nach dem Themennamen durchsucht, bekommt das der Steuerelemente — und darin steht kein einziger eigener Schlüssel. Der Zugriff lief über den Indexer, der bei einem fehlenden Schlüssel wirft, und das aus dem Konstruktor von `MainWindow`: **nipp startete gar nicht mehr.** Kein Fenster, keine Meldung, nur ein `[FTL]` im Protokoll — von aussen sah es aus, als liesse sich die EXE nicht starten.

`ThemeService` prüft jetzt zusätzlich auf einen bekannten Schlüssel und liest mit `TryGetValue`. Das tauscht allerdings einen lauten Fehler gegen einen stillen: fehlt eine Farbe, bleibt der Pinsel unbemerkt in der Farbe des Systemthemas. `ThemedBrushTests` gleicht deshalb jeden Namen gegen `Tokens.xaml` ab — `XamlResourceTests` kann das nicht, weil die Namen als Zeichenfolgen in C# stehen und nicht im XAML.

### Ein Icon-Handle überlebt sein `Icon`-Objekt nicht

```csharp
using var icon = new System.Drawing.Icon(path);
return icon.Handle;          // beim Return schon freigegeben
```

`Icon.Dispose` ruft `DestroyIcon`. Der Infobereich bekam eine Zahl, die auf nichts mehr zeigte, und zeigte **nipp ohne Symbol** — während das Protokoll Erfolg meldete, weil aus Sicht der Anwendung alles gutgegangen war. Sichtbar war es nur manchmal: ein freigegebenes Handle wird erst ungültig, wenn Windows den Platz neu vergibt. Das `Icon` bleibt jetzt am Leben, solange es gezeichnet wird; beim Themenwechsel wird das bisherige erst **nach** `UpdateIcon` freigegeben.

### `AppWindow` rechnet in physischen Pixeln

`MoveAndResize(400, 660)` ergibt auf einem 150-%-Bildschirm ein Fenster von 267 × 440 *logischen* Pixeln — alles wirkt riesig, der Inhalt schrumpft auf einen Streifen. `WindowPlacement` multipliziert mit `GetDpiForWindow / 96`.

### `FriendList` nie ersetzen

`linphone.db` hat `UNIQUE (name)` auf `friends_list`. Das zweite `AddFriendList("nipp-blf")` im selben Lauf warf eine SEH-Ausnahme aus `MainDb::insertFriendList`. Die Liste wird einmal angelegt oder per `GetFriendListByName` übernommen und danach nur noch abgeglichen.

### `AsString()` trägt den Anzeigenamen

Das SDK meldet eine Präsenz für `"152" <sip:152@pbx.example.ch>`, die Einstellungen kennen `sip:152@pbx.example.ch`. Zehn Zustände kamen an, zehn Lampen blieben grau. `AsStringUriOnly()` und `SipUri.Same`.

### `Refreshing` ist kein Fehler

„Ein unbekannter Wert gilt als Fehler" — gut gemeint, aber das SDK kennt `RegistrationState.Refreshing`, den normalen Vorgang beim Erneuern. Die LED wurde dafür kurz rot und das Protokoll riet, die Zugangsdaten zu prüfen. Jetzt umgekehrt: nur ein ausdrücklicher Fehlerzustand ist einer.

### Eine Erfolgsmeldung als letzte Zeile im Protokoll

`HidD_SetOutputReport` blockiert, bis das Headset den Bericht bestätigt. Dieser Aufruf stand im Startpfad, also auf dem UI-Thread — und **nipp startete nicht mehr**: kein Fenster, keine Anmeldung, nur ein Protokoll, das mitten im Start endete. Das Tückische war die letzte Zeile darin: „Headset-Tasten angebunden". Sie sah wie ein abgeschlossener Schritt aus, war aber der Punkt, an dem alles stehenblieb. Wer einen Aufhänger sucht, schaut auf die erste fehlende Zeile, nicht auf die letzte vorhandene.

### `Windows.Media.Devices.CallControl` gibt es auf dem Desktop nicht

Die Klasse steht in den Metadaten, hat die passenden Ereignisse und lässt sich fehlerfrei kompilieren. Zur Laufzeit scheitern `FromId` **und** `GetDefault` mit `0x80040111`, `CLASS_E_CLASSNOTAVAILABLE`. Der erste Anlauf für die Headset-Tasten war vollständig darauf gebaut. Und `Windows.Devices.HumanInterfaceDevice`, der naheliegende Ersatz, sperrt genau die Usage Page aus, um die es geht: Telefonie (0x0B) ist für Anwendungen reserviert. Bleibt Win32-HID (ADR-028).

### Ein `[JsonConverter]` an einer Eigenschaft schlägt die Optionen

Die Enums von `integrations.json` trugen `[JsonConverter(typeof(JsonStringEnumConverter))]`
**ohne** Benennungsregel, während der Store seine Optionen mit `CamelCase`
aufsetzte. Das Attribut gewinnt — nipp schrieb `"Bearer"` und
`"ActiveExpanded"`, wo jede Vorlage `"bearer"` zeigt. **Aufgefallen wäre das
nie:** Enums werden unabhängig von der Schreibweise gelesen, also lief beides.
Jetzt `CamelCaseEnumConverter`, und die JSON-Optionen liegen an einer Stelle
statt in zwei Kopien, von denen einer der Konverter fehlte.

### `WhenWritingNull` verliert die Bedeutung von `null`

`CardField.EmptyText = null` heisst „die Zeile verschwindet", die Vorgabe ist
`"—"`. Beim Speichern fiel das `null` weg, beim Lesen griff die Vorgabe — **eine
Karte änderte auf dem Weg durch die Datei ihr Verhalten**. Wer ein Feld hat, bei
dem `null` etwas bedeutet und die Vorgabe etwas anderes ist, schreibt
`[JsonIgnore(Condition = JsonIgnoreCondition.Never)]` daran.

### `ThemeService.Attach` merkt sich genau ein Wurzelelement

Ein zweites Fenster, das es ruft, **entzieht dem ersten das Erscheinungsbild**:
ein Systemwechsel kommt danach nur noch im neuen Fenster an und nach seinem
Schliessen nirgends mehr. WinUI kennt kein anwendungsweites Erscheinungsbild —
es hängt am Element. Ein zweites Fenster setzt `RequestedTheme` selbst und hört
auf `EffectiveThemeChanged`.

### Ein `record` in einer Liste liest sich als `ToString()` vor

Ohne `AutomationProperties.Name` nahm die Sprachausgabe den `ToString()` des
Datensatzes: bei einer Katalogzeile mehrere Tausend Zeichen samt vollständiger
Beispielantwort. Gerade weil ein `record` ein hilfreiches `ToString()` hat, ist
er dort unbrauchbar. Gemessen über UI-Automation, nicht vermutet.

### Ein HID-Gerät nummeriert seine Reports selbst

Beim Jabra Link 400 ist die Report-Kennung **2**. Ein festes 0 hätte `HidP_InitializeReportForID` scheitern lassen, der Report wäre leer hinausgegangen und die Lampe nie angegangen — und weil an der Off-Hook-Lampe hängt, was die Taste als nächstes bedeutet, wäre der Fehler nicht „keine Lampe", sondern „jeder zweite Tastendruck falsch". Kennung, Tasten und Lampen kommen deshalb aus dem Report-Deskriptor des Geräts, nicht aus dem Quelltext.

### Zwei Geräte-APIs, und die Töne lesen die alte

Der Rufton beim Wählen war stumm, obwohl das Protokoll ihn als gespielt auswies: Datei offen, Stream läuft. Nur endete seine Filterkette in einem `MSVoidSink` statt in `MSWASAPIWrite`. Der Grund: nipp wählt Geräte über die moderne `AudioDevice`-API, der Tonspieler des SDK liest aber `sound_conf.play_sndcard` — und die war leer, während `ring_sndcard` gesetzt war. Deshalb klingelte es bei eingehenden Anrufen und war beim Wählen still. Der Gesprächston war nie betroffen. `SettingsApplier.ApplyToneCards` zieht die alte API jetzt nach, mit einem eng begrenzten `#pragma warning disable CS0612`.

### Die native Kette braucht DREI Kopierziele, nicht zwei

Beim ersten `dotnet publish` für den Installer lagen 229 MB .NET- und WindowsAppSDK-Laufzeit im Ausgabeverzeichnis — und **keine einzige Linphone-DLL**. Das Kopier-Target hängt an `Build` und schreibt nach `$(OutDir)`; für MSIX gibt es einen zweiten Weg ins Paketlayout. `publish` sammelt aber seine eigene Dateiliste und kannte beide nicht.

Das Tückische daran: der Build war **grün**, das Verzeichnis sah vollständig aus, und aufgefallen wäre es erst auf dem Zielrechner — mit der Meldung aus §14.2, die nicht sagt, welche Datei fehlt. Es gibt jetzt ein drittes Target (`AddLinphoneToPublish`), **und** `Release-Nipp.ps1` zählt im fertigen Verzeichnis nach: sechs Kernbibliotheken, 8 von 8 Grammatiken, das WASAPI-Plugin. Ein Target, das lautlos nichts tut, sieht wie ein Erfolg aus.

### Eine .gitignore-Regel hat Quellcode verschluckt

`secrets/` stand in der `.gitignore` für Zugangsdaten — und traf den Quellordner `src\Nipp.Core\Services\Integrations\Secrets\` mit. `IntegrationSecrets.cs` war damit **seit ihrem ersten Tag nie eingecheckt**. Lokal fiel das nie auf: die Datei lag da, alles baute.

Aufgefallen ist es beim ersten Bau aus einem frischen Klon (GitHub Actions). Der brach mit zehn `CS0234`/`CS0246` ab — und **keiner der Fehler zeigte auf die fehlende Datei**, gemeldet wurden die Verwender eines Namensraums, den es dort plötzlich nicht mehr gab. Wer so eine Meldung sieht, prüft zuerst `git ls-files`, nicht die Verwender.

Zwei Lehren: Muster für Ablageordner gehören verankert (`/secrets/` statt `secrets/`), und eine Ausnahme mit `!` hilft **nicht**, wenn schon das Verzeichnis ausgeschlossen ist. `RepositoryCompletenessTests` vergleicht jetzt jede gebaute Quelldatei mit `git ls-files`.

### Eine Kennung, die einen Index enthält, ist keine

Team-Nebenstellen bekamen ihre Kennung aus ihrer **Position** in der Liste
(`team:{index}:{kurzwahl}`). Beim ersten Ziehen ging das gut. Beim zweiten
trugen die Zeilen im Speicher ihre alten Kennungen, die gespeicherte Liste war
längst umsortiert — **jede Zuordnung schlug fehl**, das Ergebnis war
„unverändert", und die Prüfung „nichts zu speichern" traf zu.

Der Zug ging damit **stumm** verloren: keine Fehlermeldung, keine
Protokollzeile, und die Anzeige zeigte ihn trotzdem. Sichtbar wurde es erst
beim nächsten Start. Eine Kennung nimmt etwas aus dem **Inhalt**, nie aus dem
Platz (ADR-042).

Dazu, gleich daneben: **`DragItemsCompleted` sagt nicht, wohin gezogen wurde.**
Die Ereignisdaten tragen die gezogenen Zeilen und `DropResult` — keinen
Zielindex, keine Gruppe.

**Und hier stand über Tage eine zweite Erklärung, die niemand gemessen hatte:**
das Ziel stehe im Zustand der Sammlungen, WinUI habe sie bereits umgebaut,
bevor das Ereignis feuert. Am 13.09.2026 nachgestellt — **es baut gar nichts
um.** Bei einer gruppierten Liste endet jeder Zug mit `None`, und das Ziehen
hat nie funktioniert; es war nur nirgends zu sehen, weil der Fehlerfall
schwieg. Seit ADR-065 wird der Zug **selbst gestartet und selbst ausgewertet**,
und die Nebenstelle landet dort, wo sie losgelassen wird.

### Ein Absturz ohne Spur ist ein Hinweis, kein Rätsel

nipp beendete sich im Gespräch — siebenmal, ausgelöst vom blossen
**Überfahren** des Auflegen-Knopfes mit der Maus. Keine verwaltete Ausnahme,
keine Zeile in `crash.txt` (die Ausnahmen **aller** Threads fängt), nichts im
Protokoll. Im Windows-Ereignisprotokoll dagegen stand jedes Mal derselbe Satz:
`0xc000027b` in `combase.dll` — eine WinRT-«stowed exception», die durch einen
COM-Rahmen zurückkommt und den Prozess sofort beendet.

**Genau dafür hat ADR-053 drei Schutzwälle aufgestellt, und sie greifen
nicht**, weil nie eine verwaltete Ausnahme entsteht. Ein 1-GB-Speicherabbild
bestätigte es von der anderen Seite: kein Thread trägt eine Ausnahme, kein
verwalteter Stapel, keine XAML-Fehlermeldung.

Die Ursache war das **Lightweight-Styling** am Knopf: sechs überschriebene
Zustands-Schlüssel (`ButtonBackgroundPointerOver` und Geschwister) in einem
lokalen `Button.Resources`, aufgelöst genau dann, wenn der Zustand gebraucht
wird. Flach, als Verweis in ein globales Wörterbuch und in `ThemeDictionaries`
— **alle drei Formen stürzten ab**, ohne sie blieb der Knopf stehen. Die rote
Warnfarbe trägt jetzt ein Rahmen *im* Knopf, und die Rückmeldung macht dessen
Deckkraft (ADR-067).

**Zwei Lehren, die teurer waren als der Fix.** «Funktioniert nicht» und
«funktioniert meistens» sehen am Fenster gleich aus — die erste Quote in diesem
Befund war aus zwei Stichproben von sieben und sechs Zügen gegriffen und zu
hoch. Und der Satz «das einzige Ende mitten im Gespräch in zwei Tagen» war
wertlos, weil es **auch das einzige Gespräch** dieser beiden Tage war.

### Ein Bericht an ein geteiltes Gerät ist nie folgenlos — auch der, der aufräumt

nipp beendete fremde Teams-Meetings. **Nicht beim Klingeln, sondern beim
Ablehnen:** der Abschlussbericht ans Headset liess das Gerät seinen Zustand
neu verhandeln, die Meldung ging über das geteilte Handle auch an Teams, und
dort ist das ein Tastendruck.

Der Schutz dafür war gebaut (ADR-028 Nachtrag 4) und versagte dreifach: Er
prüfte nur den **Ring**, nicht den Abschluss. Seine Erkennung hing am
**Gabelzustand** — und in einem Teams-**Meeting** meldet das Jabra durchgehend
«aufgelegt». Und sie wurde mit `calls.Count > 0` aufgerufen, war beim Klingeln
also ohnehin immer falsch.

**Vier Messungen vor dem ersten Codezeichen**, und eine drehte den Plan: Ohne
Abschlussbericht **klingelt das Gerät weiter**, auch nachdem der Anrufer
aufgelegt hat. Der einfache Weg war damit tot, bevor er gebaut wurde. Die
**Audio-Sitzung** dagegen zeigt ein Meeting eindeutig — in Ruhe leer, im
Meeting steht das andere Programm in Wiedergabe und Aufnahme (ADR-068).

### Ein Name, der wie eine Antwort klingt, ist keine

`CallInfo.DisplayLabel` hiess wie die Antwort auf „wie heisst der Anrufer",
stand am zentralsten Typ der Telefonie — und kannte keine Kontakte. Bei
eingehenden Anrufen kaschierte der Anzeigename der Anlage die Lücke; bei
**ausgehenden** ist der leer, und deshalb stand dort die blosse Nummer, in der
Kopfzeile, der Makel-Liste, dem Toast und dem Infobereich.

Die Auflösung lief die ganze Zeit richtungsneutral — nur eben dreimal
ausgeschrieben und nirgends dort, wo die Gesprächsansicht hinsah. Und
`ClipResolver.DescribeCaller`, gebaut als „die eine Stelle für alle", hatte im
ganzen `src/` **keinen einzigen Aufrufer**.

**Wer einer Eigenschaft einen Namen gibt, der mehr verspricht als sie hält,
baut die nächste Fehlersuche ein.** Beides ist gelöscht; die Frage beantwortet
`CallPartyResolver`, und zwar zweimal verschieden — „Name oder nichts" und „nie
leer" sind nicht dieselbe Frage (ADR-043).

### „Beenden“ beendete nicht

Ein Klick auf „Beenden“ im Infobereich-Menü fährt nipp vollständig herunter — Ereignisschleife gestoppt, Konto abgemeldet, „Core gestoppt“ im Protokoll — und der **Prozess blieb liegen**. Vier Minuten später hielt er `Nipp.Core.dll` weiter offen, und der nächste Build scheiterte an MSB3027, obwohl niemand mehr etwas offen hatte.

Für den Alltag war das lästig. Mit dem Installer ist es mehr: Velopack ersetzt beim Update den Inhalt von `current\`, und ein Prozess, der seine DLLs hält, lässt genau das scheitern. `ExitApplication` schreibt deshalb `Beenden:`-Zeilen — **die erste fehlende sagt, wo es hängt** — und ein Wächter beendet hart.

**Gefunden hat es am 12.09.2026 die Uhr, nicht der Code.** Zwischen der letzten Abbauzeile und dem Zugriff des Wächters lagen achtmal konstant 6,8 Sekunden, während der ganze Abbau **eine** dauert: es hing nicht in einem Dienst, es hing in der letzten Anweisung. `Application.Exit()` lief auf dem Thread des Infobereich-Symbols — H.NotifyIcon führt dort ein eigenes Nachrichtenfenster mit eigener Pumpe —, und dort ist der Aufruf ein **stiller Leerlauf**: die XAML-Nachrichtenschleife auf dem `[STAThread]`-Hauptthread läuft weiter, und der ist der einzige Vordergrundthread des Prozesses.

Die Korrektur ist eine Zeile: der Aufruf geht über die `DispatcherQueue`, wie der Nachbarpfad „Öffnen" es seit dem 07.09.2026 tut. **Genau das ist die Lehre** — die Korrektur von damals erfasste `OpenRequested` und übersah `ExitRequested`, obwohl acht Zeilen Kommentar darüber erklärten, warum sie nötig ist. Der Wächter bleibt und greift nach drei statt acht Sekunden; am Gerät abzunehmen als T134.

Mehr davon: [docs/sdk-api-notes.md](docs/sdk-api-notes.md) und der Fortschrittsteil in [IMPLEMENTATION-PLAN.md](docs/plans/IMPLEMENTATION-PLAN.md).

---

## Dokumentation

| Datei | Wofür |
|---|---|
| [NIPP-BUILD.md](NIPP-BUILD.md) | Die Spezifikation. Verbindlich |
| [CLAUDE.md](CLAUDE.md) | **Die Regeln** — Grenzen, Befehle, was beim Bauen gilt. Seit dem 13.09.2026 nur noch das (ADR-059) |
| [docs/stand.md](docs/stand.md) | **Was zuletzt passiert ist** — Meilenstein, was am Gerät aussteht, und die Chronologie von unten nach oben |
| [docs/lehren.md](docs/lehren.md) | **Was Erfahrung ist und keine Regel** — die teuren Stellen, nach Gebiet gruppiert: Telefonie, Audio, WinUI, Windows-Integration, Konfiguration, Bauen, packaged |
| [ALLTAG-PLAN-2.md](docs/plans/ALLTAG-PLAN-2.md) | Drei Meldungen aus dem Alltag vom 13.09.2026 — eine Gliederungsebene weniger bei den Nebenstellen, die Vorschau im Karten-Designer, der Mailbox-Reiter fällt weg. **Alle drei umgesetzt** (ADR-062 bis ADR-064) |
| [ZIEHVORSCHAU-PLAN.md](docs/plans/ZIEHVORSCHAU-PLAN.md) | Die Vorschau beim Ziehen (ADR-066) — mit dem Messprotokoll von 29 Zügen: was trägt, was zittert, und warum das Ablegeziel die Liste ist und nicht die Zeile |
| [HEADSET-FREMDBELEGUNG-PLAN.md](docs/plans/HEADSET-FREMDBELEGUNG-PLAN.md) | nipp beendete fremde Gespräche (ADR-068) — vier Messungen vor dem ersten Codezeichen, darunter die eine, die den Plan gedreht hat. **Offen: H5**, der Notausgang als Einstellung |
| `docs/plans/` | **Die abgeschlossenen Pläne und Reviews**, sechzehn Stück. Sie lagen bis zum 13.09.2026 im Wurzelverzeichnis |
| [IMPLEMENTATION-PLAN.md](docs/plans/IMPLEMENTATION-PLAN.md) | Phasen, Arbeitspakete, was wann fertig wurde |
| [INTEGRATION-PLAN.md](docs/plans/INTEGRATION-PLAN.md) | Generische Anbindung externer Systeme (CRM, ERP, Ticketing) für Anruferkontext und Kontaktsuche — Analyse des Bestands, Zielarchitektur, Phasen I0–I8. **Gebaut und angebunden** |
| [REVIEW-2026-09-12.md](docs/plans/REVIEW-2026-09-12.md) | **Das jüngste Dokument. Die Standortbestimmung:** fünf Achsen, **83 Befunde mit Datei und Zeile**, 16 davon hoch, keiner blockierend — dazu der Massnahmenplan in Wellen und die Lücken, die diese Prüfung nicht schliessen konnte. Urteil: tragfähig und sanierbar, die Schuld liegt an den Rändern |
| [WELLE-0-PLAN.md](docs/plans/WELLE-0-PLAN.md) | Die fünf Schritte, die vor jeder weiteren Entwicklung standen — mit den vier Entscheiden vom 13.09.2026 und dem Umsetzungsstand ganz vorn. **Alle gebaut**; Welle 1 folgte am selben Tag |
| [UX-REVIEW-2.md](docs/plans/UX-REVIEW-2.md) | Die zweite UX-Runde: 20 Befunde, **keiner eine Wiederholung**, gesucht dort, wo die erste Umsetzung neue Kanten erzeugt hat. Achtzehn umgesetzt (ADR-049 bis ADR-051), zwei begründet offen — mit Umsetzungsstand und einer Korrektur am Bericht selbst ganz vorn |
| [BREITBILD-PLAN.md](docs/plans/BREITBILD-PLAN.md) | Das breite Fenster: zwei Spalten ab 960 Pixeln, die Nebenstellen als Kacheln, der Detailbereich in der Zeile — mit Umsetzungsstand ganz vorn (§23, ADR-047, ADR-048) |
| [UX-REVIEW.md](docs/plans/UX-REVIEW.md) | Die erste Runde: 25 Befunde, sechs Abläufe im Vorher/Nachher, und der Umsetzungsstand ganz vorn (ADR-044 bis ADR-046). **Zwei Befunde daraus sind einen Tag später anders entschieden worden** — siehe docs/plans/BREITBILD-PLAN.md |
| [RELEASE-PLAN.md](docs/plans/RELEASE-PLAN.md) | Installer und Update-Verteilung: warum Velopack statt MSIX, was die Lizenzfrage damit zu tun hat, und was beim Öffentlichmachen zu tun wäre (R10) |
| [ABNAHME-ALLTAG.md](ABNAHME-ALLTAG.md) | **Der Einstieg für einen Tag mit dem Gerät.** Woran im Alltag zu denken ist, was schon bestanden hat, und die zwei Dinge, die heute nicht funktionieren |
| [GESAMT-REVIEW.md](docs/plans/GESAMT-REVIEW.md) | Die Prüfung der ganzen Anwendung: Befunde, Umsetzungsstand ganz vorn, und was bewusst offen blieb |
| [ALLTAG-PLAN.md](docs/plans/ALLTAG-PLAN.md) | Sechs Befunde aus dem ersten Tag im Alltag: drei Fehler mit gefundener Ursache, drei Funktionen ohne Auftrag — mit den Entscheidungen, die vor dem Code zu treffen sind |
| [REVIEW.md](docs/plans/REVIEW.md) | Review von Optik, Bedienung und Code: Befunde, was umgesetzt wurde, was offen blieb — und in §7 das Review der Umsetzung selbst |
| [docs/decisions.md](docs/decisions.md) | Alle ADRs — jede Abweichung mit Begründung |
| [docs/sdk-setup.md](docs/sdk-setup.md) | SDK beschaffen, Prüfsummen, Layout |
| [docs/sdk-api-notes.md](docs/sdk-api-notes.md) | Wo die API anders ist als die Spezifikation annimmt |
| [docs/provisioning.md](docs/provisioning.md) | Profile erstellen und ausliefern |
| [docs/updates.md](docs/updates.md) | **Der Auslieferungsweg**: Setup bauen, Kanäle stable/beta, das Token fürs private Repo, und was bei einem kaputten Release zu tun ist |
| [docs/packaging.md](docs/packaging.md) | MSIX bauen, signieren, verteilen — **nicht der Auslieferungsweg**, sondern der Anhang für Intune, sobald T110 und das Zertifikat da sind |
| [docs/test-matrix.md](docs/test-matrix.md) | Was von Hand zu prüfen ist |
| [docs/abnahme-m2-m3.md](docs/abnahme-m2-m3.md) | Abnahmeleitfaden |
| [docs/review-oberflaeche.md](docs/review-oberflaeche.md) | Review der Oberfläche mit Vorher/Nachher und Ergebnis |
| [docs/blf-pruefung.md](docs/blf-pruefung.md) | Besetztlampenfeld gegen die Anlage gemessen — Verfahren und Befund |
| `tools/Get-HidTelephony.ps1` | Welche Tasten und Lampen ein Headset über HID anbietet. Für die Frage „warum tut die Taste nichts?" |
| `tools/Test-Ui.ps1` | Liest und bedient die Oberfläche über UI Automation — für die Zeilen der Testmatrix, die sich ohne Augen prüfen lassen. `Test-NippAccessibleNames` zeigt, wo eine Sprachausgabe einen Klassennamen vorläse; so wurde der Gruppenkopf gefunden (§8.4) |
| [docs/integrations/einrichten.md](docs/integrations/einrichten.md) | Eine Integration einrichten — in sechs Schritten, seit dem 07.09.2026 über den Katalog |
| [docs/integrations/karten.md](docs/integrations/karten.md) | Karten zusammenstellen: der Designer, die Bausteine, woher ein Wert kommt |
| [docs/integrations/feldnamen.md](docs/integrations/feldnamen.md) | Welcher Feldname was bedeutet — und warum nur ein katalogisierter den Toast bekommt |
| [LICENSE](LICENSE) · [NOTICE](NOTICE) | Die AGPLv3 im Wortlaut und die Kurzfassung mit den Drittkomponenten |
| [docs/licensing.md](docs/licensing.md) | Die AGPL-Frage |
| [docs/environment.md](docs/environment.md) | Die Entwicklungsmaschine |

---

## Lizenz

**nipp steht unter der GNU Affero General Public License v3** — der Lizenztext
liegt als [LICENSE](LICENSE) bei, die Kurzfassung als [NOTICE](NOTICE).

Der Grund ist das Linphone SDK: es ist **AGPLv3 oder kommerziell**, und ein
Closed-Source-Client, der weitergegeben wird, ist mit der AGPLv3 unvereinbar.
Statt eine kommerzielle Lizenz zu verhandeln, wird der Quelltext offengelegt
(**ADR-040**, 11.09.2026). Damit ist die Frage beantwortet, die seit dem
04.09.2026 offenstand und jede Abgabe ausser Haus sperrte.

**Was das praktisch heisst:** wer nipp weitergibt oder verändert weitergibt,
gibt den Quelltext mit — auch den des SDK. Und: die Anbindung an die
bv2-eigenen Systeme ist **kein Teil des Quelltexts mehr**, sondern eine
importierbare Vorlagendatei (ADR-040). Offengelegt wird das Softphone, nicht
das Innenleben fremder Systeme.

Die Fremdbestandteile mit ihren Lizenzen stehen in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md); Einzelheiten und der Stand in
[docs/licensing.md](docs/licensing.md).

**Der Schritt, der noch fehlt:** das Repo ist noch privat. Was bis zum
Öffentlichmachen zu tun ist — Scan über alle Commits, SDK-Spiegel,
Repo-Wechsel —, steht als **R10** in [RELEASE-PLAN.md](docs/plans/RELEASE-PLAN.md) und
zusammengefasst in [docs/licensing.md](docs/licensing.md).
