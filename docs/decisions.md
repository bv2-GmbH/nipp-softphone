# Architekturentscheidungen (ADRs)

Jede Abweichung von `NIPP-BUILD.md` gehört hier hinein — mit Kontext, Entscheidung und Konsequenz (§15). Nicht stillschweigend abweichen.

Format: neueste zuoberst. Status ist `angenommen`, `offen`, `abgelöst durch ADR-nnn` oder `verworfen`.

---

## ADR-069 — Das geprüfte SDK-ZIP liegt unter eigener Kontrolle

**Datum:** 17.09.2026 · **Status:** **angenommen** · **Bezug:** §5, **ADR-005**, ADR-040, `docs/sdk-setup.md`, `docs/stand.md` (17.09.2026)

**Kontext.** Das Linphone SDK ist 299 MB gross, liegt deshalb nicht im Repo und
wurde bisher beim Bauen von `download.linphone.org` geholt — Adresse und
SHA256 stehen in `ci.yml`, `release.yml` und `docs/sdk-setup.md`.

**Unter derselben Adresse lag ab dem 07.09.2026 eine andere Datei.** Gleiche
Versionsnummer 5.5.18, aber `Last-Modified: 07.09.2026 20:19 GMT` und
**313 666 099 Bytes** gegen die 313 467 757 der Fassung vom 04.09.2026, mit der
hier gebaut wird — 198 342 Bytes Unterschied, `Content-Type: application/zip`.
Keine Fehlerseite, sondern ein stilles Neuablegen desselben Release.

**Was daran zählte, war nicht der rote Haken.** Jeder CI-Lauf im öffentlichen
Repo brach an der Prüfsumme ab — **vom ersten Commit am 14.09.2026 an,
siebzehn Läufe** —, und zwar bevor `PublicRepositoryTests` lief. Das ist laut
`CLAUDE.md` «die letzte Kontrolle vor der Öffentlichkeit»; sie hat seit dem
Repo-Wechsel nur noch lokal gegriffen. `release.yml` trug dieselbe Prüfsumme:
ein Release über GitHub Actions wäre genauso gescheitert.

**Die Prüfsumme nachzuziehen war die naheliegende und falsche Antwort.** Sie ist
die Kontrolle dagegen, dass ein unbesehen verändertes SDK in den Build kommt —
dieselbe Überlegung wie ADR-040 («Was in einer fremden Datei steht, hat niemand
geprüft»). Wer sie anfasst, sieht vorher nach, was sich geändert hat; und beim
nächsten stillen Neuablegen stünde dasselbe Problem wieder da.

**Entscheidung: das geprüfte ZIP kommt unter eigene Kontrolle.** Es liegt
unverändert als Release-Asset in
[`bv2-GmbH/nipp-build-deps`](https://github.com/bv2-GmbH/nipp-build-deps),
einem öffentlichen Repo ohne Code, und `ci.yml`, `release.yml` und
`docs/sdk-setup.md` zeigen dorthin. Die Prüfsumme bleibt unverändert — es ist
dieselbe Datei.

**Damit ist ADR-005 zu Ende gedacht.** Dort stand schon, dass der Feed des
Herstellers unzuverlässig ist, und die Antwort war, das ZIP von Hand zu holen.
Sie ging davon aus, dass eine Adresse mit Versionsnummer immer dieselbe Datei
liefert. Das hielt drei Tage.

**Ein eigenes Repo, und nicht ein Release im Hauptrepo.** Velopack sucht dort
nach dem neuesten Release, um Updates zu finden (`VelopackUpdateGateway`,
`GithubSource`). Ein SDK-Release hätte GitHubs `latest` verschoben und damit
den Auslieferungspfad der installierten Arbeitsplätze treffen können — es als
Vorabversion zu markieren, nur um einen Sortiermechanismus auszutricksen, wäre
genau die stille Kopplung, die dieses Haus sonst überall auflöst. Der Preis ist
ein zusätzliches sichtbares Repo, und er ist kleiner.

**Lizenz.** Das SDK steht unter GPL v3 (oder kommerziell von Belledonne). Was
dort liegt, ist eine **unveränderte Kopie** des öffentlich veröffentlichten
Binärpakets; der Quellcode ist bei Belledonne verfügbar, und das Repo enthält
keinen eigenen Code. nipp selbst bleibt AGPLv3.

**Konsequenz.**

- **Die CI kann wieder grün werden**, und `PublicRepositoryTests` läuft dort
  zum ersten Mal überhaupt. **Bis das ein Lauf gezeigt hat, ist es eine
  Erwartung und kein Ergebnis** — nachzusehen am nächsten Push.
- **Beim nächsten SDK-Wechsel** wird die neue Fassung erst geholt, angesehen
  und gegen die alte verglichen; dann liegt sie im Abhängigkeits-Repo, und erst
  dann wandern Version, Adresse und Prüfsumme in die drei Stellen.
- **Der Hersteller bleibt die Quelle**, nur nicht mehr die Bezugsstelle des
  Builds. `docs/sdk-setup.md` führt beide Adressen.

---

## ADR-068 — Die Fremdbelegung hängt an der Audio-Sitzung, nicht am Gabelzustand

**Datum:** 14.09.2026 · **Status:** **angenommen** · **Bezug:** §22.5, **ADR-028** (Nachtrag 4)

### Der Anlass

Gemeldet: «ich war in einem Teams-Meeting, ein Anruf kam in nipp rein, und als
ich ihn **abgelehnt** habe, war das Meeting weg.»

ADR-028 Nachtrag 4 hat diesen Fall bereits behandelt — und trotzdem trat er
ein. Der Grund ist nicht die Entscheidung von damals, sondern **ihre
Erkennung**.

### Was gemessen wurde (14.09.2026, Jabra PRO 9470)

    12:50:21.776  klingelt=true          — Teams laeuft weiter
    12:50:25.486  Anruf abgelehnt (durch den Benutzer)
    12:50:25.528  imGespraech=false, klingelt=false, stumm=false
    12:50:25.731  Geraet antwortet: Usages 0x2A 0x97, Gabel abgenommen=false

**Nicht der Ring beendet das Meeting, sondern der Abschlussbericht.** Und das
Gerät meldete während des ganzen Meetings `abgenommen=false` — ein
Teams-**Meeting** setzt den Gabelzustand nicht, anders als ein
Teams-**Anruf**.

Damit versagte der gebaute Schutz doppelt: Er prüft die Fremdbelegung **nur
beim Klingeln** (der Abschluss geht durch, sobald `jeGemeldet` steht), und
selbst dort hätte `HookWatch.Fremdbelegung` nichts gefunden. Ein dritter
Defekt in derselben Kette: `Fremdbelegung(calls.Count > 0)` ist beim Klingeln
**immer** falsch, weil nipp dann einen eigenen Anruf hat — und der Ring war
der einzige Fall, für den die Prüfung existierte.

| Frage | Ergebnis |
|---|---|
| **M1** — klingelt das Gerät ohne Abschlussbericht weiter? | **Ja**, auch nachdem der Anrufer aufgelegt hat; es hört nicht von selbst auf |
| **M2** — zeigt die Audio-Sitzung ein Meeting? | **Ja, deutlich.** In Ruhe leer; im Meeting steht das andere Programm in Wiedergabe **und** Aufnahme |
| **M3** — erkennt nipp seine eigene Sitzung? | **Ja**, über die eigene Prozesskennung |
| **M4** — was kostet die Abfrage? | **3,2 bis 10,8 ms** für beide Geräte — nicht pumptauglich, aber in `PushState` unbedenklich |

### Die Entscheidung

**1. Die Fremdbelegung kommt aus der Audio-Sitzung** (`AudioSessionWatch`
über `CoreAudioSessions`). Der Gabelzustand bleibt als zweite Quelle — er
erkennt den Teams-**Anruf**, kostet nichts, und keine der beiden Quellen
erkennt alles.

**2. Sie gilt für jeden Bericht, nicht nur für den Ring.** Wird der Ring
verschwiegen, bleibt `jeGemeldet` auf false — **und damit bleibt auch der
Abschluss liegen.** Das fremde Gespräch wird gar nicht erst angefasst.

**3. Das eigene Gespräch schlägt die Fremdbelegung.** Wer annimmt oder selbst
wählt, hat sich entschieden; dass das andere Programm dabei das Gerät
verliert, ist die Folge dieser Wahl. **Ein klingelnder Anruf ist noch keine
Wahl** — deshalb zählt nur ein Anruf, der nicht bloss klingelt.

**4. Was nipp gemeldet hat, nimmt es zurück** — auch bei Fremdbelegung. Sonst
bliebe die Lampe des eigenen, gerade beendeten Gesprächs stehen; M1 hat
gezeigt, dass das Gerät sich nicht selbst zurücksetzt.

**5. Im Zweifel frei.** Scheitert die Abfrage der Sitzungen, gilt das Gerät als
unbelegt — das ist das Verhalten von vor diesem ADR und damit kein
Rückschritt. Andersherum wäre ein Fehler an der Audio-Schnittstelle ein
Headset, das nie wieder etwas anzeigt, und niemand käme darauf, warum.

### Konsequenz

- Was ADR-028 Nachtrag 4 **entschieden** hat, bleibt gültig: ein Bericht an
  ein geteiltes Gerät ist nie folgenlos. **Seine Erkennung ist ersetzt.**
- Am Gerät geprüft (14.09.2026): Meeting mit Ablehnen, Meeting mit Annehmen
  samt Auflegen danach, ohne Teams, und wegklingeln lassen — alle vier.

### Was offen bleibt

- **Gefragt werden die Standardgeräte, nicht das Headset.** Solange das
  Headset das Standardgerät ist, ist das dasselbe, sonst nicht. **Das ist eine
  Annahme und keine Messung** (T300).
- **Ein Hintergrunddienst mit dauerhaft aktiver Sitzung brächte nipp dauerhaft
  zum Schweigen.** Beim Messen stand neben Teams eine Aufnahmeanwendung in der
  Liste — allerdings nur, solange Teams lief. Dagegen steht der Notausgang aus
  H5 des Plans: eine Einstellung «Signale ans Headset senden». **Noch nicht
  gebaut.**

---

## ADR-067 — Kein Lightweight-Styling am Auflegen-Knopf

**Datum:** 14.09.2026 · **Status:** **angenommen** · **Bezug:** §8.2, ADR-044, ADR-053, W2.6 (Befund D14)

### Der Anlass

Gemeldet: «das Softphone ist während eines Gesprächs abgestürzt». Sieben Mal
reproduziert, immer **beim blossen Überfahren des Auflegen-Knopfes** mit der
Maus — ohne Klick.

**Und es hinterliess nichts.** Kein Eintrag in `crash.txt` (die fängt
Ausnahmen aus *allen* Threads), keine Zeile im Protokoll, kein
`UnhandledException`. Im Ereignisprotokoll stand jedes Mal derselbe Satz:

    Fehlerhafter Modulname: combase.dll
    Ausnahmecode: 0xc000027b

Das ist `STATUS_STOWED_EXCEPTION` — ein Fehler, der durch einen WinRT-Rahmen
zurückkommt und den Prozess sofort beendet. **Genau die Sorte, für die ADR-053
die drei Schutzwälle aufgestellt hat**; sie greifen hier nicht, weil nie eine
verwaltete Ausnahme entsteht.

### Was gemessen wurde, in dieser Reihenfolge

| Kandidat | Befund |
|---|---|
| Die Ziehvorschau (ADR-066, am selben Tag gebaut) | **Nein** — derselbe Absturz auf dem Stand davor, gleiches Modul, gleicher Offset |
| Eine verwaltete Ausnahme | **Nein** — `FirstChanceException` über alle Threads mitgeschrieben: zwischen Start und Absturz kein Eintrag |
| Die Qualitätsanzeige im Sekundentakt | **Nein** — eine Sonde je Teilschritt zeigt die letzte Runde vollständig durchgelaufen |
| Die Form der Zustands-Pinsel | **Nein** — als `SolidColorBrush` mit `ThemeResource`, als Verweis nach `Tokens.xaml` und in `ThemeDictionaries`: alle drei stürzten ab |
| Der ToolTip | **Nein** — entfernt, Absturz blieb; alle anderen Knöpfe der Seite zeigen ihre Hilfetexte unbeschadet |
| **Das Überschreiben der Zustands-Schlüssel selbst** | **Ja** — ohne die sechs Zeilen blieb der Knopf stehen |

Das Speicherabbild (WER `LocalDumps`, 1 GB) bestätigte den Befund von der
anderen Seite: **kein Thread trägt eine Ausnahme**, der UI-Thread steht in
`Application.Start`, und im ganzen Abbild steht kein verwalteter Stapel und
keine XAML-Fehlermeldung.

### Die Entscheidung

**An diesem Knopf wird kein Lightweight-Styling mehr verwendet.** Die sechs
Schlüssel (`ButtonBackgroundPointerOver` und Geschwister) sind ersatzlos weg —
in jeder Form, auch in `ThemeDictionaries`, die die WinUI-Doku dafür vorsieht.

**Die rote Rückmeldung bleibt trotzdem** (Befund D14 war berechtigt: bei
«Auflegen» *ist* die Farbe die Warnung). Sie entsteht jetzt anders:

- Der Knopf ist **durchsichtig** und bleibt in allem der Standardknopf —
  Fokus, Tastatur, Sprachausgabe, `HangUpCommand`, Kurzinfo.
- Die Farbe trägt ein **Rahmen darin**, und der deckt den grauen Grund ab, den
  die Standardvorlage beim Überfahren setzt.
- Die Abstufung (90 % überfahren, 80 % gedrückt) macht die **Deckkraft** dieses
  Rahmens, über vier kleine Zeigerbehandler. «Verlassen», «Abgebrochen» und
  «Zeiger verloren» teilen sich einen — alle drei bedeuten dasselbe.

### Konsequenz

- **Wer eine Zustandsfarbe eines Bedienelements ändern will, überschreibt keine
  WinUI-Ressourcenschlüssel**, sondern legt die Farbe in den Inhalt. Der Weg
  über `Button.Resources` ist auf dieser Plattform ein Absturzrisiko, und er
  ist stumm: er kostet keinen Test, keine Warnung und keine Protokollzeile,
  sondern den Prozess.
- **Ein Absturz ohne `crash.txt` ist kein Absturz ohne Ursache.** Er ist der
  Hinweis auf einen WinRT-Rahmen. Der Weg dorthin führt über das
  Ereignisprotokoll (`Application Error`, Modulname und Ausnahmecode) und
  nicht über das eigene Protokoll.
- Die Schwäche von ADR-053 ist damit benannt: die drei Wälle fangen alles, was
  als verwaltete Ausnahme entsteht. Dieser Fall entstand nie als solche.

### Was offen bleibt

**Warum** WinUI hier stirbt, ist nicht geklärt — nur **dass** und **woran**.
Ohne Debugger mit Symbolen für `combase.dll` ist der native Stapel nicht zu
lesen, und der Aufwand steht in keinem Verhältnis: die Stelle ist ersetzt, der
Weg dorthin ist dokumentiert, und eine zweite Stelle mit diesem Muster gibt es
im Programm nicht (geprüft).

---

## ADR-066 — Die Vorschau ist der Auftrag, und das Ablegeziel ist die Liste

**Datum:** 14.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, ADR-041, ADR-042, ADR-048, **ADR-065**

### Der Anlass

Zwei Dinge, die sich als eines herausstellten.

Gewünscht war eine **Vorschau**: beim Ziehen soll sichtbar sein, wo die Zeile
landet, indem die anderen ausweichen. Bis dahin sagte nur die Beschriftung am
Zeiger «Hierhin verschieben», und wo «hier» ist, sah man erst nach dem
Loslassen.

Gemeldet war ausserdem, ein Wechsel in eine andere Gruppe sei **nicht möglich**.
Das stimmte so nicht — er ging in 15 von 16 Zügen. Aber der sechzehnte ging
stumm verloren, und ein Fehlschlag, der schweigt, macht aus «geht meistens»
in der Wahrnehmung «geht nicht».

### Was gemessen wurde (14.09.2026, am gebauten Fenster)

Zwei Sonden, 29 Züge, Protokoll in `docs/plans/ZIEHVORSCHAU-PLAN.md`.

| Frage | Ergebnis |
|---|---|
| Überlebt ein laufender `StartDragAsync`, dass ihm die Sammlung umsortiert wird? | **Ja** — 67 Umhängungen, viele über Gruppengrenzen, kein Abbruch |
| Wo kommt der Drop an, wenn die Vorschau läuft? | **Vier von fünf auf der gezogenen Zeile selbst** — sie liegt danach unter dem Zeiger |
| Wirft eine Präsenzmeldung die Vorschau zurück? | **Nein** — `UpdatePresence` setzt nur `row.Presence` |
| Zittert die Vorschau? | **Ja, an der Gruppengrenze**, 25 bis 50 ms zwischen Hin und Zurück |
| Kommt `DragLeave` vor oder nach dem nächsten `DragOver`? | **Danach**, und doppelt so oft (136 gegen 67) |
| Warum endet ein Zug ohne Ergebnis? | *letztes Ziel Zeile, vor **703 ms*** — beim Loslassen stand kein Ablegeziel unter dem Zeiger |

### Die Entscheidung

**1. Die Vorschau ist der Auftrag, kein Bild davon.** Die gezogene Zeile wird
beim Überfahren wirklich umgehängt; beim Loslassen wird nicht mehr gerechnet,
sondern geschrieben, was dasteht (`ApplyTeamLayout()` liest die Anzeige). Eine
Rechnung fürs Zeichnen und eine fürs Speichern wären zwei Wahrheiten über
dieselbe Frage. Der Zustand dazu liegt im Kern (`TeamDragPreview`), weil ein
Zug Herkunft, Stand und Abbruch trägt — und `Nipp.App` kein Testprojekt hat.

**2. Das Ablegeziel ist die Liste, nicht die Zeile.** Zwischen den Zeilen liegt
totes Gebiet: Abstände, Padding, der Streifen zwischen zwei Gruppen. Wer dort
loslässt, trifft nichts. `ListView` und `GridView` tragen jetzt `AllowDrop` und
rechnen die Stelle aus der Zeigerposition (`ZielStelle`); Zeile und Kachel sind
dafür dasselbe, sobald man sie als Rechteck nimmt. **Damit erledigt sich auch
der Befund, dass vier von fünf Drops auf der gezogenen Zeile ankommen** — sie
ist gar nicht mehr das Ziel. Der **Gruppenkopf** bleibt ein eigenes Ziel: eine
leere Gruppe hat keine Zeile, an der sich etwas ausrechnen liesse.

**3. Ein Vorschauschritt braucht eine Zeigerbewegung** (`VorschauSchwelle`, 6
Pixel). Das Zittern an der Gruppengrenze ist eine Rückkopplung: die Zeile
wechselt die Gruppe, alles darunter rutscht nach, und unter dem **stehenden**
Zeiger liegt wieder die alte Nachbarschaft. Bewegt sich der Zeiger nicht,
ändert sich die Vorschau nicht. Der Wert ist **gewählt, nicht gemessen** —
T293.

**4. Im Sortiermodus bleibt der Detailbereich zu.** Diese Regel stand seit
ADR-042 im Kommentar an `IsTeamReorderMode` — «kein offener Detailbereich», als
eine von drei — und **es gab sie nicht**: geschlossen wurde er beim
Einschalten, ein Klick öffnete ihn wieder. Ungleich hohe Zeilen sind für eine
Vorschau eine Quelle von Sprüngen. Jetzt steht sie in
`ToggleContactDetails`, mit zwei Tests.

**5. `Einfassen` entfällt.** Es setzte `Opacity` am Container, und Container
werden virtualisiert und wiederverwendet — die Dämpfung wäre früher oder später
an der falschen Zeile hängen geblieben. Gezeigt wird jetzt die **gezogene**
Zeile, über `ContactRow.DragOpacity`, also an der Zeile gebunden.

### Konsequenz

- Was ADR-065 über das Ablegeziel sagt, gilt nicht mehr wörtlich: **die
  Zielstelle entsteht beim Überfahren, nicht beim Loslassen.** Die Wahl von
  ADR-065 — der Zug gehört uns, nicht WinUI — bleibt gültig und ist die
  Voraussetzung dafür.
- `MoveContactToGroupCommand` und `TeamLayout.Move` bleiben — für das
  **Kontextmenü**, den zweiten gleichwertigen Weg ohne Maus (ADR-042). Es hat
  keine Vorschau und braucht die Rechnung deshalb weiter.
- Ein Zug, der nichts bewirkt, meldet es im Protokoll (`DragWithoutMove`).

### Was offen bleibt

Ob ein Zug im **Kachelraster** und ein Ablegen auf einem **Gruppenkopf** in der
neuen Fassung durchlaufen, ist am Gerät zu prüfen (T292 bis T294) — beide
Ansichten teilen sich die Behandler, aber das Protokoll unterscheidet sie
nicht, und in den Messungen vom 14.09.2026 kam kein einziges Ablegen auf einem
Kopf vor.

---

## ADR-065 — Der Zug wird selbst gestartet und selbst ausgewertet

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §17, §20.1, ADR-041, **ADR-042**, ADR-047, ADR-052, ADR-063, ADR-064

### Der Anlass

Gemeldet aus der Benutzung: im Sortiermodus lässt sich eine Nebenstelle
umsortieren, aber **nicht in eine andere Gruppe ziehen**.

Die Ursache lag tiefer als erwartet. Im ganzen Programm gab es **keinen
einzigen Ablegebehandler**; der Ziehvorgang bestand aus vier
WinUI-Eigenschaften und zwei passiven Ereignissen. Dass eine Zeile die Gruppe
wechselt, hing vollständig an einer Annahme aus ADR-042.

### Was ADR-042 behauptete — und was gemessen wurde

> *„WinUI hat die Zeile aber bereits umgehängt, bevor das Ereignis feuert."*

Ohne Messung, ohne Quelle. **Am 13.09.2026 am gebauten Fenster nachgestellt:**

| Gemessen | Ergebnis |
|---|---|
| Zug über die Gruppengrenze, schmale Liste | Drop endet `None` — nichts umgehängt, nichts geschrieben |
| Zug **innerhalb** einer Gruppe, schmale Liste | ebenfalls `None` |
| Zug im Kachelraster | **kein Zugbeginn** — das Ereignis feuert dort gar nicht |

Der eingebaute Umsortierweg von WinUI trägt bei einer gruppierten
`CollectionViewSource` **nichts** bei. Und der Fehlerfall war **dreifach
stumm**: kein Schreiben, keine Protokollzeile, keine Meldung — ein misslungener
Zug war von einem zurückgezogenen nicht zu unterscheiden.

### Die Entscheidung

**Der Zug wird selbst ausgewertet.** Zeile, Kachel und Gruppenkopf sind
Ablegeziele; wohin die Nebenstelle kommt, entscheidet der Ort, an dem
losgelassen wurde.

| Ziel | Ergebnis |
|---|---|
| obere Hälfte einer Zeile (linke einer Kachel) | davor |
| untere Hälfte (rechte) | dahinter |
| Gruppenkopf | ganz oben in dieser Gruppe — **bei einer leeren Gruppe das einzige Ziel** |

`ContactGroupMove` trägt dafür einen Zielindex; `null` heisst weiterhin «ans
Ende», **und damit bleibt das Kontextmenü unverändert**. Gerechnet wird in
`TeamLayout.Move`, einer reinen Funktion im Kern — `Nipp.App` hat kein
Testprojekt, und wohin eine Zeile gehört, ist eine Rechnung über eine Liste.

**Ein Weg für beide Richtungen.** Der gruppeninterne Zug läuft durch dieselbe
Stelle wie der gruppenübergreifende. Zwei Wege zum selben Ergebnis, von denen
einer nur manchmal greift, sind die Doppelwahrheit, die dieses Projekt
wiederholt bezahlt hat.

**Und auch der Anfang des Zugs ist selbst gebaut.** Der eingebaute Weg —
`CanDragItems` an der Liste, `CanDrag` am Element der Vorlage — startete im
Kachelraster **keinen** Zug: bei eingeschaltetem Sortiermodus und nachweislich
gesetzten Eigenschaften feuerte dort kein einziges Ereignis, während dieselbe
Vorlage in der schmalen Liste zog. Drei Ansätze wurden gemessen (Item-Drag
von WinUI, `CanDrag` am Element, Cross-Slide abgeschaltet); keiner half.

Deshalb merkt sich **die Liste** das Drücken, und sobald sich der Zeiger acht
Pixel bewegt hat, startet das Vorlagenelement den Zug selbst
(`StartDragAsync`). Unterhalb der Schwelle ist es ein Klick: Auswahl,
Doppelklick und Kontextmenü bleiben, was sie waren. Weil dieselbe
Zeilenvorlage auch die Outlook- und die Suchliste trägt, steht die Bedingung
im Behandler und nicht im XAML: gezogen wird nur im Sortiermodus und nur, was
in einer Team-Gruppe steht.

### Die Stelle, die es brauchte: `handledEventsToo`

Der erste Versuch band `PointerPressed` als XAML-Attribut an die Liste — **und
bekam nie einen Druck auf eine Zeile zu sehen.** `ListViewItem` und
`GridViewItem` markieren den Druck als behandelt, für ihre Auswahl; ein
behandeltes Ereignis steigt nicht weiter auf. Gemessen mit einer
Protokollzeile im Behandler, die nie erschien. Erst `AddHandler(…, true)` im
Konstruktor sieht den Druck trotzdem — und ab da zog das Raster wie die Liste.

**Am Gerät: T291.**

### Folgen

- **Die Aussage aus ADR-042 ist richtiggestellt**, dort und in
  `TeamLayout.cs`. Sie war fünf Tage lang die Begründung, die Stelle nicht
  anzufassen — **zum dritten Mal in diesem Projekt eine ungemessene Erklärung,
  und diesmal stand sie in einem ADR.**
- Drei Protokollzeilen, wo vorher Stille war: angefragt, ohne Verschiebung
  beendet, beendet mit oder ohne Gruppenwechsel. **Ohne Rufnummer und ohne
  Namen** (§21.2) — gezählt wird, nicht benannt.
- Die Zeile «angefragt» steht **vor** dem Abbruch. Stünde sie danach, wäre ein
  abgelehnter Zug wieder von einem nicht begonnenen nicht zu unterscheiden.
- Ob eine Zeile die Gruppe gewechselt hat, wird **über den Gruppennamen**
  festgestellt. Der Schreibweg baut die Gruppen neu auf; ein Referenzvergleich
  meldete jeden Zug als Wechsel.
- Ob eine Zeile die Gruppe gewechselt hat, hält **das Ablegeziel** fest. Wer
  es hinterher aus den Gruppen liest, findet die gezogene Instanz nicht mehr
  und meldet jeden Zug als Wechsel — zweimal so gemessen.
- `TeamLayoutMoveTests` prüft die Rechnung in neun Fällen, darunter den leeren
  Zielblock, den negativen und den zu grossen Index.

---

## ADR-064 — Der Umschalter steht im Kopf der ersten offenen Gruppe

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §17, §20.1, §23, ADR-041, ADR-042, ADR-046, ADR-047, ADR-063

### Der Anlass

Der Umschalter «Reihenfolge ändern» stand seit ADR-063 in einer eigenen
32-Pixel-Leiste über der Nebenstellen-Liste: rechtsbündig, daneben eine leere
Füllspalte, darunter ein Streifen Luft. Gemeldet als «deplaziert», und das
traf es — ein einzelner Knopf über einer Fläche, die sonst nichts enthielt.

Er sass vorher in der Kopfzeile des Aufklappers «Nebenstellen (n)». Als die
mit ADR-063 wegfiel, bekam er einen eigenen Platz; das war die Notlösung, nicht
der Entwurf.

### Die Entscheidung

**Er steht im Kopf einer Gruppe — dort, wo er etwas betrifft.** Welcher, sagt
`ReorderHost.Pick`: die erste **offene**, sonst die erste.

| Fall | Träger |
|---|---|
| «Team» offen | «Team» |
| «Team» zugeklappt, «Dienste» offen | «Dienste» |
| alle zugeklappt | «Team» |

**Der dritte Fall ist der, der zählt.** `null` hiesse dort: der Umschalter ist
unerreichbar, und mit ihm das Umsortieren. **Eine Funktion, die sich selbst
wegsperrt, ist schlimmer als eine an einem mittelguten Platz.**

**In beiden Layouts gleich** — schmale Liste und breite Kacheln teilen sich
seit heute *eine* Kopfvorlage.

### Was dabei eine Entscheidung brauchte: das Springen

Im Sortiermodus gehen **alle** Gruppen auf (`ShouldBeExpanded`, ADR-042). Wer
bei zugeklapptem «Team» unten bei «Dienste» steht und dort drückt, sähe den
Knopf im selben Augenblick nach oben springen — **unter dem Zeiger weg, als
Folge des eigenen Klicks.**

Er bleibt deshalb angeheftet, solange der Modus läuft. Gemerkt wird der
**Gruppenname**, nicht die Instanz: `RebuildTeamGroups` ersetzt die Gruppen,
sobald sich ihre Anzahl oder Reihenfolge ändert, und eine gemerkte Referenz
wäre danach eine Leiche.

### Was das nebenbei bereinigt

**Der Umschalter war nicht mehr über einen Namen erreichbar** — Kopfzeilen
liegen in einem `DataTemplate` und werden beim Scrollen erzeugt und verworfen.
Das erzwang, was ohnehin richtig ist:

- **Eine Wahrheit, N Anzeigen.** Vorher gab es zwei benannte Knöpfe, die
  einander von Hand nachgeführt wurden — samt einem Schutzflag gegen das eigene
  Ereignis, das dabei entstand. Beides ist weg; der Knopf setzt nur noch den
  Zustand, alles Gezeichnete folgt daraus.
- **Die Sichtbarkeitsregel stand an zwei Stellen im Fenster**, und die zweite
  lief im schmalen Layout gar nicht, weil die Methode dort vorher aussteigt.
  Jetzt einmal im Kern, als `ShowTeamReorder`.
- **«Kein Sortiermodus ohne `CanReorderTeam`»** wirkte über einen Umweg: ein
  zurückgesetzter Knopf löste sein eigenes Ereignis aus, das den Modus
  beendete. Jetzt steht die Regel dort, wo `CanReorderTeam` lebt.
- **Der Gruppenkopf war zweimal ausgeschrieben** und unterschied sich in einer
  Zahl — eine Abschrift, die auseinandergelaufen ist. Jetzt eine Ressource.
- **`Text="{x:Bind Header}"` war `OneTime`.** `SetRows` meldet `Header`
  ausdrücklich, weil sich der Zähler in Klammern ändert; die Meldung lief ins
  Leere, und der Kopf zeigte einen veralteten Stand, bis die Sammlung zufällig
  ersetzt wurde. **Ein latenter Fehler in genau der Zeile, die ohnehin
  angefasst wurde.**

### Und der Streifen Luft aus ADR-063 ist gemessen

ADR-063 vermerkte ihn als «nicht gemessen» und vermutete ihn bei der Leiste.
**Er kam vom Kopf-Container.** `ListViewHeaderItem` und `GridViewHeaderItem`
bringen aus Fluent eine eigene `MinHeight` und ein eigenes `Padding` mit — und
ein `HorizontalContentAlignment="Left"`, wegen dem der Umschalter im ersten
Anlauf am Gruppennamen klebte statt am rechten Rand. **Am gebauten Fenster
gesehen, nicht hergeleitet.** Zwei Container-Stile beheben beides.

### Folgen

- Der Umschalter **scrollt jetzt mit** aus dem Bild. Die Hinweisleiste mit
  «Fertig» ist damit kein Zusatz mehr, sondern **der ortsfeste Ausstieg** aus
  dem Sortiermodus.
- Beim Moduswechsel wird die Kopfzeile neu erzeugt; der Tastaturfokus geht vom
  Knopf weg. Hinnehmbar.
- `ReorderHostTests` prüft die Regel in allen drei Fällen, `ShellReorderTests`
  ihre Anbindung — **einschliesslich der Anheftung**, die am Fenster nur
  schwer nachzustellen wäre.
- Am Gerät: **T290**.

---

## ADR-063 — Eine Gliederungsebene, nicht zwei

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §20.1, §23, ADR-041, ADR-042, ADR-044, ADR-047, ADR-048, `docs/plans/ALLTAG-PLAN-2.md` A

### Der Anlass

Die Nebenstellen standen unter **zwei** Klappebenen übereinander: aussen ein
Aufklapper mit der Kopfzeile «Nebenstellen (10)», darin je Gruppe («Team»,
«Dienste») ein zweiter. Gewünscht: nur noch «Team» und «Dienste», jede für
sich zum Auf- und Zuklappen.

### Der Befund ist angenehmer als der Wunsch

**Die innere Ebene konnte bereits alles.** Sie klappt seit ADR-042 auf und zu,
und der Zustand überlebt den Neustart: die Namen der zugeklappten Gruppen
stehen in `Advanced.CollapsedTeamGroups` und gehen über `SaveViewState`, lösen
also keine Neuanmeldung aus. **Auch die Vorgabe für Nebenstellen ohne Gruppe
stand schon richtig:** `TeamGroups.NameOf` gibt die erste Gruppe zurück — ab
Werk «Team» —, und zwar auch dann, wenn eine Nebenstelle auf eine Gruppe
verweist, die es nicht mehr gibt.

Zu tun war also **eine Ebene zu entfernen, keine zu bauen**. Zwei Stellen
standen dem im Weg:

1. **`var koepfe = namen.Count > 1;`** Bei genau *einer* Gruppe wurde kein
   Gruppenkopf gezeichnet — die äussere Überschrift trug ihn ja. Ohne
   Korrektur hätte ein Arbeitsplatz mit einer einzigen Gruppe danach **gar
   keinen** Kopf mehr gehabt und nichts mehr zuklappen können.
2. **Das breite Layout hatte den Aufklapper gar nicht**, sondern nur denselben
   Text als Überschrift über den Kacheln. Beide Ansichten mussten gemeinsam
   geändert werden, sonst wäre die Ungleichheit zurückgekommen, die ADR-048
   beseitigt hat.

### Die Entscheidung

- **Der äussere Aufklapper fällt weg**, in beiden Layouts. «Team» und
  «Dienste» stehen direkt da.
- **Ein Gruppenkopf wird immer gezeichnet.** `ContactGroupRow.ShowHeader` ist
  ersatzlos entfernt statt auf `true` verdrahtet: ein Parameter, der nur noch
  einen Wert annimmt, ist eine Frage, die niemand mehr stellt.
- **Der Zähler steht je Gruppe**, wo er etwas aussagt. `TeamHeader`
  («Nebenstellen (n)») ist gelöscht — und mit ihm der Grund, aus dem ADR-044
  den Abschnitt so genannt hat: dort ging es darum, dass «Team (10)» über
  «Team (3)» verwirrt. Ohne äussere Überschrift gibt es den Konflikt nicht
  mehr.
- **`IsTeamExpanded` und `Advanced.ShowTeamContacts` fallen ebenfalls weg.**
  Auf Nachfrage entschieden: die einzelnen Gruppen reichen. Wer Platz braucht,
  klappt sie zu — und das wird gespeichert.

### Was dabei nicht gelöst wurde

Der Sortier-Umschalter sass in der Kopfzeile des Aufklappers und hat jetzt
eine eigene, knopfhohe Zeile über der Liste. **Über dem ersten Gruppenkopf
bleibt ein Streifen Luft, und woher der kommt, ist nicht gemessen.** Er stand
vorher unter der Expander-Kopfzeile genauso da; es ist also kein Rückschritt,
aber auch keine Verbesserung. Wer ihn wegbekommen will, misst zuerst — der
Kommentar an der Stelle sagt das ausdrücklich, statt eine Ursache zu
behaupten.

> **Nachtrag vom selben Tag (ADR-064): gemessen.** Er kam nicht von der Leiste,
> sondern vom Kopf-Container — `ListViewHeaderItem` bringt aus Fluent eine
> eigene `MinHeight` und ein eigenes `Padding` mit. Zwei Container-Stile haben
> ihn beseitigt, und die Leiste gibt es inzwischen nicht mehr.

### Folgen

- `ContactGroupRowTests` kennt `showHeader` nicht mehr; die Gruppen tragen
  ihren Kopf immer.
- Eine bestehende `settings.json` mit `ShowTeamContacts` wird beim nächsten
  Speichern um das Feld erleichtert. Der Klappzustand der **Gruppen** bleibt
  unberührt — er steht in einem anderen Feld.
- Am Gerät: **T287**.

---

## ADR-062 — Die Mailbox entfällt, und zwar ganz

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §2, §8.5, §8.6, §9.1, §20.1, §23, M5, ADR-045, ADR-046, ADR-052, `docs/plans/ALLTAG-PLAN-2.md` C

### Der Anlass

Gewünscht: der Mailbox-Reiter soll weg, es bleiben Kontakte, Anrufe und
Einstellungen. **Auf Nachfrage entschieden: alles, was an der Mailbox hängt.**

### Was der Befund vor dem Löschen zutage brachte

Zwei Dinge, die man wissen muss, bevor man einen Reiter entfernt:

1. **Der Reiter war der einzige Weg zu «Mailbox anrufen».** Der Befehl hatte
   genau einen Aufrufer — den Knopf in der Mailbox-Ansicht. Kein Tastenkürzel,
   kein Eintrag im Infobereich.
2. **Der Reiter war die einzige Anzeige für wartende Nachrichten.** Die Kette
   von der Anlage bis ins Fenster war vollständig gebaut — `VoicemailAddress`
   an den Kontoparametern abonniert MWI, die Bridge meldet es weiter, das
   ViewModel führte einen Zähler —, und ihre einzige sichtbare Wirkung waren
   das Abzeichen und ein Satz im Panel.

### Die Entscheidung

**Entfernt wird die ganze Kette**, nicht nur ihr sichtbares Ende:

| Ebene | Was |
|---|---|
| Fenster | Reiter, Abzeichen, Inhaltsbereich, das vierte Tastenkürzel, der Sprung zu «Mailboxnummer eintragen» |
| ViewModel | `ShellSection.Voicemail`, `VoicemailCount`, `CallVoicemailCommand`, `VoicemailAddress`, `HasVoicemailAddress`, das MWI-Abo |
| Toast | der Knopf «Mailbox» und die Umleitung dahinter |
| Telefonie | `RedirectAsync`, `MessageWaitingChanged`, `MessageWaitingEventArgs`, die Protokollzeile, `accountParams.VoicemailAddress` |
| Einstellungen | das Feld «Mailboxnummer», `AccountSettings.VoicemailAddress` |
| Provisionierung | das Attribut `voicemail`, auch in der Hilfe von `nippprov` |

**Warum auch `RedirectAsync`.** Es ist eine SIP-Funktion und hätte bleiben
können — aber sein einziger Aufrufer war der Toast-Knopf. Eine Fähigkeit ohne
Aufrufer ist genau das Muster, das dieses Projekt **siebenmal** bezahlt hat
(`CardKind.History`, `IntegrationConfig.cards`, `ClipResolver.DescribeCaller`,
`App.SdkStatus`, `ContactNumberKind.Mobile`, der SIP-Port, der
Deinstallations-Hook). **Wer sie stehen lässt, meldet sie beim nächsten Review
als Fund.**

Damit bleiben **drei Reiter**, und die Kürzel sind Strg+1 bis Strg+3.

### Abweichung von der Spezifikation

`NIPP-BUILD.md` verlangt die Mailbox an acht Stellen, darunter **§8.5 als
eigenen Abschnitt** und das Akzeptanzkriterium von **M5**. Die Spezifikation
ist nicht umgeschrieben worden — sie bleibt das Dokument, das festhält, was
einmal verlangt war —, aber **an jeder dieser Stellen steht jetzt der Verweis
hierher**. Eine Spezifikation, die eine Fähigkeit fordert, die bewusst entfernt
wurde, ist die nächste Fehlersuche.

### Was mit bestehenden Installationen geschieht

**Eine eingetragene Mailboxnummer geht verloren**, und das ist die Folge des
Entscheids, kein Fehler. `settings.json` trägt sie weiter, bis nipp das nächste
Mal speichert; danach ist sie weg. Wer die Mailbox anrufen will, wählt ihre
Nummer wie jede andere.

**Ein Profil von vorher wird unverändert angenommen.** Der Provisionierungsleser
holt jedes Attribut einzeln über `element.Attribute(...)`; ein unbekanntes
`voicemail="*98"` bleibt folgenlos liegen. `ProvisioningParserTests` behält die
Beispieldatei **mitsamt** dem Attribut — damit prüft der Test seither auch das:
dass ein altes Profil vollständig gelesen wird.

### Folgen

- Wer die Mailbox zurückholt, baut sie neu — und das ist richtig so: ein
  auskommentierter Bereich, der ein halbes Jahr mitläuft, ist teurer als der
  Neubau.
- `NumberNormalizer` lässt weiterhin benannte Ziele wie `voicemail` durch. Das
  ist eine Anlagenfunktion und hat mit diesem ADR nichts zu tun.
- Am Gerät: **T289**.

---

## ADR-061 — Was «Vorschau» heisst, geht in kein Parser

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §21.2, §21.4, ADR-032, ADR-034, ADR-040, `docs/plans/ALLTAG-PLAN-2.md` B

### Der Anlass

Im Karten-Designer lässt sich eine Rufnummer eingeben und ein **echter** Abruf
auslösen. Gemeldet: die Vorschau zeigt danach weiter die erfundenen
Beispieldaten.

**Die Ursache lag nicht dort, wo man sie sucht.** Der Renderpfad war in
Ordnung — `CardLayoutEngine.Build` erzeugt bei jedem Lauf neue Abschnitte, die
`DependencyProperty` in `CardView` feuert, `Rebuild()` läuft. **Es wurde neu
gezeichnet, nur mit den alten Daten.**

Die Kette brach drei Schritte vorher:

1. `IntegrationTester` gab die Antwort als **Anzeigefassung** zurück: bei
   8192 Zeichen abgeschnitten, mit `… (gekürzt)` am Ende.
2. Der Designer reichte genau diesen Text an den Probenspeicher weiter.
3. `JsonNode.Parse` warf — abgeschnittenes JSON ist keines —, und der Fänger
   **kehrte still zurück**.

**Und es sah aus wie ein Erfolg.** Der Zähler stand *vor* der Prüfung: die
Statuszeile meldete «1 von 1 Quellen haben geantwortet», und weil der Zähler
über null lag, wurde die Vorschau neu gezeichnet — mit denselben erfundenen
Daten. **Eine Meldung, die einen Erfolg behauptet, den sie nicht geprüft hat**,
zum dritten Mal in diesem Projekt.

### Der Befund hinter dem Befund

**Das Feld hiess `RawResponse`, und sein Kommentar sagte ausdrücklich: «Sie ist
zum Ansehen da, nicht zum Weiterverarbeiten.»** Zwei Stellen verarbeiteten es
trotzdem weiter — der Karten-Designer **und** die Einstellungsseite. Die zweite
ist beim Suchen aufgefallen und hatte denselben Fehler.

Ein Name, der «roh» verspricht, lädt dazu ein. **Ein Satz im Kommentar schützt
keine Schnittstelle.**

### Die Entscheidung

**Zwei Felder mit Namen, die ihren Zweck tragen:**

| Feld | Was | Wer nimmt es |
|---|---|---|
| `ResponsePreview` | eingerückt, bei 8192 Zeichen gekürzt | das Textfeld auf der Einstellungsseite |
| `ResponseBody` | ungekürzt, unverändert | alles, was parst |

Die Kürzung bleibt, wo sie hingehört. **Eine eigene Grenze bekommt der Rumpf
nicht:** die Grösse ist bereits im HTTP-Client gedeckelt (§21.2), und eine
zweite Schranke wäre eine zweite Wahrheit darüber, wie gross eine Antwort sein
darf.

**`SetFromLiveCall` sagt, ob es geklappt hat**, statt still zurückzukehren —
und gezählt wird, **was übernommen wurde**, nicht was geantwortet hat. Die
Statuszeile sagt seither «übernommen» statt «geantwortet»: eine Quelle kann
antworten, ohne dass die Vorschau davon etwas hat, und genau diesen Unterschied
hat sie verschwiegen.

### Was ausdrücklich **nicht** geändert wird

**Die Vorschau zeigt weiterhin auch ausgeschaltete Quellen.** Beim Suchen sah
es nach einem zweiten Fehler aus: der Abruf fragt nur eingeschaltete Quellen,
der Schnappschuss baut über alle. **Drei Tests haben die Änderung
zurückgeholt, und sie hatten recht** — eine Vorlage wird nie eingeschaltet
ausgeliefert (ADR-040), eingeschaltet wird *nach* dem Testabruf. Wer hier auf
`Enabled` prüft, macht die Vorschau beim Einrichten leer, also genau dann, wenn
man sie braucht. Dasselbe gilt für den Abruf-Knopf: er bleibt aktiv, auch wenn
noch keine Quelle eingeschaltet ist.

**Ein Wächtertest, der anschlägt, wird begründet erweitert — oder die Änderung
war falsch.** Hier war sie es.

### Folgen

- `LiveCallSampleTests` prüft die Stelle, an der es **brach**: eine Antwort
  über der Kürzungsgrenze kommt an, eine abgeschnittene wird abgelehnt und
  lässt die vorhandene stehen. Der ältere Test rief `SetFromLiveCall` mit
  sauberem JSON auf und war deshalb die ganze Zeit grün — **geprüft war die
  Stelle, an der es funktioniert.**
- `IntegrationTesterTests` hält beide Fassungen an derselben Antwort fest: die
  eine ist gekürzt, die andere lässt sich parsen.
- Wer künftig eine Antwort weiterverarbeitet, nimmt `ResponseBody`. Der Name
  sagt es; ein Kommentar muss es nicht mehr.

---

## ADR-060 — Eine Meldung ist ein Auftrag, keine Auskunft

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §9.2, §10, AP3.6, ADR-045, ADR-054, Nachtrag zu ADR-006

### Der Anlass

Auf einem Arbeitsplatz mit **zwei aktiven Netzwegen** — WLAN und ein
Mobilfunkadapter — hielt die Anmeldung nicht. Das Konto lief im Sekundentakt
`Ok → Progress → Failed → Ok`, die Präsenz aller zehn Nebenstellen stand auf
«offline», und die Oberfläche zeigte abwechselnd «Angemeldet» und «Anmeldung
fehlgeschlagen». **Am gebauten Programm gemessen, nicht hergeleitet.**

Dahinter lagen **zwei voneinander unabhängige Fehler**, und beide haben
dieselbe Form: eine Stelle meldete etwas weiter, ohne zu prüfen, ob es etwas zu
melden gab.

### Erstens: der Netzzustand

`ConnectivityMonitor` reichte **jedes** Netzereignis von Windows an das SDK
weiter. Der Mobilfunkadapter wechselte dabei seine Adresse von selbst — binnen
Minuten von `10.26.178.37` auf `10.255.230.35` —, und jedes dieser Ereignisse
meldete «erreichbar=true» an ein SDK, dem das längst gesagt worden war.

**Eine Meldung an `Core.NetworkReachable` ist keine Auskunft, sondern ein
Auftrag: sie kostet eine Neuregistrierung.** Die Anlage sah einen
Anmeldesturm.

**Die Entprellung aus dem Nachtrag zu ADR-006 fing das nicht**, und das ist
kein Versäumnis, sondern eine andere Frage: sie fasst zusammen, was innerhalb
von zwei Sekunden kommt. Hier kam es über Minuten verteilt. Was fehlte, war
nicht ein längeres Fenster, sondern die Frage, **ob sich überhaupt etwas
geändert hat**.

Gemeldet wird jetzt nur noch eine **veränderte Lage**, und die besteht aus der
Erreichbarkeit **und den lokalen Adressen**. Die Adressen gehören dazu und sind
nicht Beiwerk: beim Wechsel WLAN → LAN bleibt «erreichbar» true, und trotzdem
*muss* das SDK neu registrieren — der Contact-Header trägt sonst die alte
Adresse, und die Anlage schickt eingehende Anrufe dorthin. **Ein Vergleich, der
nur die Erreichbarkeit ansieht, würde genau den Fall verschlucken, für den es
diesen Dienst gibt** (der Befund vom 08.09.2026). Verbindungslokale Adressen
(`169.254.*`, `fe80::`) bleiben draussen; sie entstehen und vergehen an
Adaptern ohne Netz und sind das Rauschen selbst.

### Zweitens: eine Kette ohne Ende

Im selben Protokoll wiederholte sich **alle neun Millisekunden** derselbe
Block: Einstellungen gespeichert, auf den laufenden Core übertragen, Autostart
und Protokoll-Handler geschrieben, Kürzel angemeldet, Präsenz-Abos erneuert,
Erscheinungsbild gesetzt — und wieder gespeichert. An `Changed` hängen vier
Empfänger, und einer davon schrieb zurück. **In sechs Minuten wurden daraus
248 MB Protokoll**, und jede Runde übertrug die Einstellungen erneut auf den
Core.

**Wer die Kette auslöst, ist dabei die falsche Frage.** Sie konnte entstehen,
weil niemand geprüft hat, ob es überhaupt etwas zu schreiben gibt — und diese
Prüfung gehört an die eine Stelle, durch die jeder Schreibweg läuft, nicht in
vier Empfänger, die sich daran erinnern müssten. `SettingsService.Write` kehrt
jetzt zurück, wenn der Text, der auf die Platte ginge, derselbe ist wie beim
letzten Mal: **kein Schreiben, kein `Changed`.**

Verglichen wird der **Text** und nicht der Wert. `NippSettings` ist ein
`record`, aber seine Listen vergleichen sich über die Referenz — zwei inhaltlich
gleiche Teamlisten gälten als verschieden, und die Prüfung liefe ins Leere. Den
Text bildet `Write` ohnehin gerade.

### Der Nebenbefund, gefunden vom Test zur Reparatur

`MarkUserChanges` nahm die Liste der Benutzermarkierungen aus dem
**übergebenen** Objekt. Wer ein `NippSettings` speichert, das er einen
Augenblick früher aus `Current` abgeleitet hat, bringt eine veraltete mit —
**und löschte damit, was der Benutzer angefasst hatte.** Beim nächsten Start
hätte das Profil wieder gewonnen, also genau der Fall, den ADR-054 beseitigt
hat. `UserOverrides` ist eine Historie und kein Feld, das ein Aufrufer setzt:
sie wächst in `MarkUserChanges` und wird nur von einer Sperre gekürzt, und die
geht über `SaveFromProfile` an dieser Funktion vorbei.

Aufgefallen ist das, weil der erste Test der Schleifenbremse zwei Meldungen
zählte statt einer. **Ein Test, der aus dem falschen Grund rot ist, hat
trotzdem recht.**

### Folgen

- **Wer aus nipp heraus etwas an ein System weitergibt, das darauf handelt —
  das SDK, die Registrierung, ein Ereignis mit Empfängern —, prüft vorher, ob
  sich etwas geändert hat.** Beide Fehler hier sind dieselbe Form.
- `ConnectivityMonitorTests` hält beide Hälften fest: ein Wechsel ohne Wirkung
  wird nicht gemeldet, **und** eine neue lokale Adresse wird gemeldet. Die
  Adressliste ist dafür injizierbar, wie die Erreichbarkeit schon.
- `SettingsNoOpSaveTests` ebenso: dreimal derselbe Stand meldet einmal, ein
  Empfänger, der zurückspeichert, hält nach einer Runde an, und vier echte
  Änderungen melden viermal.
- **Am Gerät noch zu prüfen: T284 und T285.** Der Beweis unter einem echten
  Adresswechsel steht aus — beim Nachlauf war der Adapter still.

---

## ADR-059 — Die Regeln, der Stand und die Erfahrung sind drei Dateien

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §15, `docs/plans/REVIEW-2026-09-12.md` A8, A11

### Der Anlass

`CLAUDE.md` ist die Datei, die man vor jeder Änderung an nipp liest. Sie war
**1554 Zeilen** lang, und darin standen drei verschiedene Dinge übereinander:
die Regeln (was gilt), der Stand (was zuletzt passiert ist) und die Erfahrung
(was uns schon einmal Tage gekostet hat). Dazu lagen **achtzehn
Markdown-Dateien im Repo-Wurzelverzeichnis**, dreizehn davon abgeschlossene
Pläne und Reviews — wer das Projekt zum ersten Mal öffnete, sah nicht, welche
zwei davon noch gelten.

**Die drei wachsen verschieden schnell.** Regeln ändern sich selten und nur mit
einem ADR. Der Stand ändert sich an jedem Arbeitstag. Die Erfahrung wächst, aber
sie veraltet nie. In einer Datei heisst das: die Regeln verschwinden zwischen
den Arbeitstagen, und niemand traut sich zu kürzen, weil alles darin einmal
teuer war.

### Die Entscheidung

**Drei Dateien, nach Änderungsrate getrennt.**

| Datei | Inhalt | Ändert sich |
|---|---|---|
| `CLAUDE.md` | Grenzen, Befehle, Regeln, Bauen, Umgebungsvorbehalt | mit einem ADR |
| `docs/stand.md` | Meilenstein, was am Gerät aussteht, Chronologie | an jedem Arbeitstag |
| `docs/lehren.md` | die teuren Stellen, gruppiert | wenn etwas Neues weh getan hat |

`CLAUDE.md` steht damit bei **unter 400 Zeilen** und beginnt mit einer Tabelle,
die auf die anderen beiden zeigt. **Abgeschlossene Pläne und Reviews liegen
unter `docs/plans/`** — dreizehn Stück; im Wurzelverzeichnis bleiben
`README.md`, `CLAUDE.md`, `NIPP-BUILD.md`, `ABNAHME-ALLTAG.md` und
`THIRD-PARTY-NOTICES.md`. **Verschoben, nicht gelöscht:** rund dreissig Stellen
verweisen auf einzelne Befundnummern älterer Reviews, und diese Verweise sind
mitgezogen worden.

### Was ausdrücklich **nicht** umgesetzt wird

**A8 in seiner Mengenform: der Kommentaranteil von 32 Prozent bleibt.** Der
Befund nennt drei Stellen beispielhaft, und alle drei sind bei der Nachprüfung
**Absicht und keine Anekdote**: die vier Symbolquellen im csproj sind das
Wissen, das dreimal Zeit gekostet hat; der Hinweis in `ProvisioningService`
erklärt, warum ein naheliegender Rename am Analyzer scheitert; und der Satz
über den Platzhalter im Nummernfeld nennt die Messung, wegen der der Hinweis
nicht dort steht. **Wer sie kürzt, spart Zeilen und baut die nächste
Fehlersuche ein.**

Der wirksame Teil des Befunds ist ein anderer, und er steht jetzt als Regel in
`CLAUDE.md`: **ein Kommentar, der eine Ursache behauptet, nennt die Messung.**
Der Kommentar am `GroupStyle.Panel` erklärte seit ADR-047, warum ein
gruppiertes GridView «NUR so» virtualisiere — niemand hatte das gemessen, es
stimmte nicht, und der Satz war fünf Tage lang genau die Begründung, die Stelle
nicht anzufassen. Das ist kein Mengenproblem.

**A11 — 26 Log-Klassen zusammenlegen.** Der Befund stuft sich selbst als
kosmetisch ein und nennt seinen eigenen Grund dagegen: das Muster ist
einheitlich, und ein Zusammenlegen brächte einen grossen Diff und sonst nichts.

### Der Nebenbefund, und er ist der teuerste des Umzugs

**`PublicRepositoryTests` hat dreizehn Dateien nie angesehen.** Er prüfte
`src/`, `tests/`, `docs/`, `tools/`, `build/` und **drei** namentlich genannte
Markdown-Dateien in der Wurzel; ein Kommentar daneben begründete das: «Die
Planungsdokumente wandern ins private; sie zu bereinigen kostet mehr und
verfälscht sie.» Beim ersten Lauf nach dem Umzug meldete er **67 Stellen** —
die beiden Systemnamen und den Hostnamen der Telefonanlage, seit ADR-040
unbemerkt im Repo, während `CLAUDE.md` festhielt, sie stünden nicht mehr darin.

**Der Einwand von damals war richtig gerechnet und falsch geschlossen.** Die
Bereinigung kostete zwei Ersetzungen der **Wortstämme** — Artikel und
Präposition bleiben dadurch stehen, weil beide Ersatzwörter sächlich sind wie
die Originale — und eine Durchsicht des Diffs auf Grammatik, die fünfzehn
Stellen fand, an denen der Stamm allein nicht reichte. **«Kostet mehr» hiess in Wahrheit «wird
nicht geprüft»**, und das ist in einem Repo, das öffentlich werden soll, die
teurere Antwort. Der Ordner wird jetzt über `docs/` mitgeprüft.

### Folgen

- **Wer den Stand fortschreibt, ändert `docs/stand.md`** und nicht
  `CLAUDE.md`. Steht eine neue Regel dahinter, kommt sie zusätzlich und kurz
  in die Regeln.
- Ein Verweis auf einen Plan schreibt sich `docs/plans/<name>.md`. Innerhalb
  von `docs/plans/` bleibt er relativ.
- `README.md` bleibt der Überblick für einen Menschen und führt weiterhin
  seinen eigenen Stand — es ist die Datei, die jemand liest, der nipp noch
  nicht kennt, und ein Verweis auf eine vierte Datei wäre dort eine Hürde.

---

## ADR-058 — Eigene Tokens nur für das, was Fluent nicht hat — und zwei Eigenbauten bleiben

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §20.4, ADR-044, ADR-046, ADR-052, `docs/plans/REVIEW-2026-09-12.md` D5, D10 bis D16

### Der Anlass

Ein Design-Token soll die eine Stelle sein, an der eine Zahl steht. In nipp
waren es zwei Stellen, und die zweite war die grössere.

**Die Abstände (D5).** `Tokens.xaml` gab eine Skala von 3/6/10/14 vor.
Danebengezeichnet wurde ein zweites Raster aus Literalen: einundfünfzig mal
die 8, fünfzehn mal die 4, zehn mal die 1 — gegen **zwei** Verwendungen von
`NippGapLarge` und **eine** von `NippGapSection`. Die Skala beschrieb also
nicht, was gezeichnet wurde, sondern was einmal gedacht worden war. Eine
Änderung daran hätte drei Stellen bewegt und hundert stehen lassen.

**Die Radien (D10).** `NippCardCornerRadius` war 8 wie `OverlayCornerRadius`,
`NippControlCornerRadius` 4 wie `ControlCornerRadius` — eigene Namen für fremde
Werte. Daneben standen dieselben Zahlen noch einmal als Literale, und der
Avatar nahm seine **Grösse** aus einem Token und seinen **Radius** fest als 14:
wer ihn vergrössert hätte, hätte drei abgerundete Quadrate bekommen.

**Die Schriftgrössen (D12).** Achtunddreissig Symbole trugen ihre Grösse als
Zahl, darunter fünfzehn mal 14 und fünf mal 15 — **dieselbe Absicht, an zwei
Tagen geschrieben**.

### Die Entscheidung

**Die Skala folgt der Mehrheit, nicht umgekehrt.** Fünf Stufen — 1, 3, 6, 8,
12 —, jede belegt; `NippGapLarge` ist 8 statt 10, `NippGapSection` 12 statt 14.
Die Literale sind auf die nächstliegende Stufe gezogen, was an einundzwanzig
Stellen ein bis vier Pixel kostet. **3 und 6 bleiben, obwohl sie nicht ins
Viererraster passen:** das ist die Verdichtung vom 05.09.2026, und an
`NippGapSmall` hängt eine Rechnung — die Kachelhöhe in ADR-047 zählt zweimal 3
mit, und die Zelle ist fest.

**Ein eigenes Token gibt es nur, wo Fluent nichts hat.** Die beiden
abgeschriebenen Radien sind gelöscht; ihre elf Verwender nehmen das Original,
und damit geht nipp mit, wenn Windows seine Radien ändert. Für Code, der ohne
XAML zeichnet, gibt es `ThemeValues.Radius` — sonst stünde dieselbe Zahl in
`CardView` und im Designer ein drittes und viertes Mal.

**Drei Symbolgrössen statt fünf**, und die Zehner sind auf 12 gehoben: acht
und zehn Pixel liegen unter jeder Stufe der Fluent-Skala.

**Ein dezenter Knopf ist ein Stil und keine Attributsammlung.** Sieben Knöpfe
trugen dieselben vier bis fünf Attribute einzeln. **Ihr Radius ist jetzt der
von Fluent und nicht mehr 16:** auf einem 32er-Knopf ergibt 16 einen Kreis, und
drei runde Knöpfe standen ausgerechnet im Nummernfeld nebeneinander.

**Der Auflegen-Knopf antwortet wieder** (D14). Seine drei Zustände standen auf
demselben Pinsel — die Lösung dafür, dass das Rot beim Überfahren verschwand,
hatte mit der Farbe auch die Rückmeldung weggenommen. Neunzig und achtzig
Prozent Deckkraft auf derselben Farbe sind die Abstufung, die Fluent für seine
Akzentknöpfe benutzt.

**Das Abzeichen bekommt einen eigenen Platz statt eines negativen Randes**
(D13). `Margin="11,-5,-9,0"` war eine feste Pixelzahl neben einem Symbol, das
mit der Windows-Textskalierung wächst. Jetzt drei Spalten, links ein leerer
Platz so breit wie der rechte: das Symbol bleibt mittig, das Abzeichen hat
seinen Raum, und nichts springt.

### Was ausdrücklich **nicht** umgesetzt wird

**D15 — die `SettingsCard` aus dem Community Toolkit.** Der Eigenbau bleibt.
Er trägt das Schloss aus der Provisionierung (§17) als eigene Zustandslogik,
und ein Toolkit-Paket dafür einzuziehen hiesse, eine Abhängigkeit gegen fünf
Padding-Zeilen zu tauschen — in einem Programm, das self-contained ausgeliefert
wird und dessen Auslieferung schon 229 MB wiegt. **Der Nebenbefund war dagegen
echt und ist behoben:** `HasDescription` wurde nur beim Aufbau gesetzt, eine
spätere Änderung an `Description` ergab einen unsichtbaren Satz.

**D16 — die `SelectorBar` statt vier `ToggleButton`.** Sie trägt Symbol und
Text mit Auswahlindikator, aber weder das Abzeichen noch Strg+1 bis Strg+4 —
beides müsste nachgebaut werden, und damit wäre der Eigenbau nur verschoben.

### Folgen

- `SpacingTokenTests` hält beides fest: kein Abstand steht als Zahl im XAML,
  **und** die Skala hat genau fünf Stufen. Ohne die zweite Hälfte liesse sich
  die erste dadurch beruhigen, dass jemand für jede Zahl ein Token anlegt.
- `XamlResourceTests` führt die drei Fluent-Schlüssel jetzt mit. **Am gebauten
  Fenster nachgeprüft**, nicht angenommen: die Hauptansicht kam hoch, und ein
  Wählversuch auf eine Nummer, die es an der Anlage nicht gibt, hat die
  Gesprächsansicht geladen (`Dialing -> Failed`). Ein fehlender Schlüssel wäre
  dort ein Absturz gewesen, nicht eine Meldung.

---

## ADR-057 — Ein Schreibfehler ist eine Meldung, kein Absturz — und Dateiauswahl bleibt im Fenster

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §10, §12, ADR-045, ADR-053, `docs/plans/REVIEW-2026-09-12.md` A6, B21, B22

### Der Anlass

Zwei Absturzpfade und eine Regel, die auf dem Papier gut klang.

**Erstens (B21):** seit ADR-045 gibt es keinen «Speichern»-Knopf mehr — jede
Feldänderung schreibt sofort. `File.WriteAllText` und `File.Move` standen dabei
ungefangen da. Hält ein Virenscanner, eine Sicherung oder OneDrive die Datei
einen Augenblick, wirft der Aufruf. Nicht jeder Aufrufer fing das: «Konto
entfernen» läuft aus einem `async void`-Handler, und eine Ausnahme von dort
nimmt den Prozess mit. **Ein Telefon, das beim Entfernen eines Kontos
verschwindet**, ist schlimmer als eines, das den Vorgang ablehnt.

**Zweitens (B22):** der `ContactStore` meldet `Changed` und `LoadingChanged` aus
dem Hintergrund — Outlook liest über COM, die Netzsuche über HTTP. Die
Empfänger sind gebundene Oberflächen. Genau das war am 08.09.2026 schon einmal
der Absturz beim `UpdateService`: «stürzt ab, wenn ich nach Updates suche».
**Derselbe Fehler, ein Dienst weiter.**

### Die Entscheidung

**Ein Fehlschlag beim Schreiben wird zu einem Satz, den ein Mensch lesen kann.**
`SettingsService.Write` fängt `IOException` und `UnauthorizedAccessException`,
schreibt eine Protokollzeile mit maskiertem Pfad und wirft eine
`InvalidOperationException` mit dem Satz «Die Einstellungen liessen sich nicht
speichern… noch einmal versuchen. Die bisherigen Einstellungen gelten weiter.»
Die letzte Zusage ist keine Freundlichkeit, sondern eine Eigenschaft der
Reihenfolge: geschrieben wird in eine temporäre Datei, und `Current` wird erst
danach gesetzt. Ein halb übernommener Stand wäre schlimmer als ein Fehlschlag.

**Ein Kern, der meldet, marshallt selbst.** Der `ContactStore` fängt im
Konstruktor den `SynchronizationContext` ein und postet seine Ereignisse
dorthin — wie `ShellViewModel` und `ConnectivityMonitor` es seit dem 08.09.2026
tun. Ohne Kontext (Tests, Konsolenwerkzeuge) läuft es geradeaus weiter.

### Was ausdrücklich **nicht** umgesetzt wird

Befund A6 verlangt wörtlich, «Datei- und Prozessoperationen in
ViewModel-Befehle zu verlegen». **Das wird nicht getan, und der Grund ist die
Schichtregel selbst.** Ein Dateiauswahldialog ist ein `FileOpenPicker`, und
`Windows.System.Launcher` öffnet den Explorer — beides sind WinRT- und
WinUI-Typen, die ein Fensterhandle brauchen. Sie in ein ViewModel zu ziehen
hiesse, `Nipp.Core` an WinUI zu binden, also genau die Grenze aufzuweichen, um
derentwillen der Befund geschrieben wurde. **Die Regel, die dahintersteht, ist
nicht «kein Dateizugriff im Fenster», sondern «keine Entscheidung im
Fenster».** Was ausgewählt wurde, entscheidet weiterhin der Kern; wo der Dialog
steht, ist eine Frage der Plattform.

### Folgen

- Wer im Kern schreibt und will, dass ein Fehlschlag ankommt, wirft eine
  `InvalidOperationException` mit einem fertigen Satz — nicht die rohe
  `IOException`. Der Aufrufer zeigt `ex.Message` und muss nichts übersetzen.
- Wer aus dem Kern ein Ereignis meldet, das eine Oberfläche erreichen kann,
  fängt den `SynchronizationContext` ein. **Zum dritten Mal dieselbe Lehre.**
- `SettingsWriteFailureTests` hält beide Zusagen fest: die lesbare Meldung und
  den unveränderten Stand danach.

---

## ADR-056 — Wohin eine Einstellung gehört, entscheidet eine Regel und nicht der Geschmack

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §9, §11, §17, ADR-019, ADR-045, ADR-046, ADR-051, ADR-054, `docs/plans/REVIEW-2026-09-12.md` C12, D8

**Kontext.** Die Einstellungsseite ist in einer Woche **dreimal** umgebaut
worden, und das Pendel hat dabei die Richtung gewechselt:

- **docs/plans/REVIEW.md U7** (05.09.) wollte die Netzfelder **in** die Oberfläche: ein
  Supporter konnte Port, Zertifikatsprüfung und Keep-Alive nur über ein Profil
  setzen.
- **ADR-019** (06.09.) verwies ein Dutzend §9-Felder **aus** der Oberfläche ins
  Profil: ein DSCP-Wert sei keine Benutzerentscheidung.
- **ADR-046** (12.09.) teilte die Seite in zwei Ebenen, weil 114
  Eingabeelemente auf einer standen.
- **C12** (12.09.) findet den Transport auf Ebene 1 zu prominent, während
  «Serverzertifikat prüfen» unter «Für Administratoren» liegt — zwei Hälften
  derselben Frage an zwei Orten.

**Jedes Mal war die Einzelentscheidung nachvollziehbar, und jedes Mal fehlte
die Regel.** Ohne sie ist der nächste Umbau eine Geschmacksfrage, und der
übernächste nimmt ihn zurück.

**Entscheidung.** Eine Einstellung gehört dorthin, wo die Frage «wer
entscheidet das?» sie hinstellt:

| Wer entscheidet | Wohin | Beispiele |
|---|---|---|
| **Der Benutzer, im Alltag** | **Ebene 1** | Klingelton, Audiogeräte, Erscheinungsbild, Anruferkarte, Nebenstellen, Aufbewahrung der Anrufliste |
| **Der Benutzer, einmal beim Einrichten** | **Ebene 1**, in der Gruppe der Sache | Konto samt Passwort, Mailboxnummer, Tastenkürzel |
| **Die Administration, einmal je Kunde** | **«Für Administratoren»** | Netz und Verschlüsselung, Codecs, Quellen, Provisionierung, Diagnose |
| **Niemand — es ist eine Netzentscheidung** | **nur ins Profil**, gar nicht in die Oberfläche | DSCP, RTP-Portbereich, IPv6, TURN-Zugangsdaten (ADR-019) |

**Drei Sätze, die daraus folgen:**

1. **Was zusammen entschieden wird, steht zusammen.** Transport und
   Zertifikatsprüfung gehören derselben Frage an — «wie redet nipp mit der
   Anlage» —, also stehen sie beide unter «Für Administratoren». Der Transport
   bleibt trotzdem im **Kontoformular** sichtbar, weil ein Konto ohne ihn nicht
   angelegt werden kann; **dort ist er Teil des Formulars, nicht eine eigene
   Einstellung.**
2. **Was ein Profil setzt, ist für den Benutzer Anzeige.** Gesperrte Felder
   bleiben sichtbar und ausgegraut (§17) — ein Feld, das fehlt, lässt offen, ob
   jemand daran gedacht hat. Seit ADR-054 gilt zusätzlich: was er selbst
   eingestellt hat, bleibt.
3. **Höchstens zwei Ebenen.** Eine Gruppe, ein Aufklapper darin, Schluss. Heute
   stehen vier Ebenen Chevrons übereinander (D8): die Scrollposition springt
   beim Aufklappen, und was oben stand, ist weg.

**Konsequenz.**

- **C12 ist damit entschieden, aber nicht umgesetzt:** der Transport bleibt, wo
  er ist — im Kontoformular —, und «Serverzertifikat prüfen» bleibt bei den
  Administratoren. Es war kein Fehler, sondern zwei verschiedene Rollen
  desselben Worts.
- **D8 bleibt offen und ist jetzt begründet fällig:** die vierte Ebene gehört
  weg. Das ist ein Umbau der Seite und braucht den Gerätetag davor, damit die
  Änderung nicht zwischen zwei ungeprüfte Zustände fällt.
- **Wer eine Einstellung ergänzt, beantwortet zuerst die Frage aus der
  Tabelle.** Steht die Antwort in der Zeile «niemand», gehört sie in den
  `ProvisioningCatalog` und **nicht** in die Oberfläche.

---

## ADR-055 — «Nicht stören» ist ein stummer Klingelton auf Zeit, kein Präsenzzustand

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §10, ADR-013, ADR-049, `docs/plans/UX-REVIEW-2.md` C15, `docs/plans/REVIEW-2026-09-12.md` C8

**Kontext.** §10 verlangt «Präsenz setzen» im Menü des Infobereich-Symbols, und
der Kommentar in `TrayIconHost` zitiert die Vorgabe wörtlich. **Gebaut war es
nie.** Im Menü stehen Öffnen, Stumm, Wiedergabegerät und Beenden; grep nach
einem Weg, Präsenz zu *setzen*, findet nur das Abbilden fremder Zustände.

Dieselbe Fähigkeit stand ein zweites Mal offen: **C15** aus dem zweiten
UX-Review vom 12.09.2026 wartete seit einem Tag auf einen Entscheid. Der Anlass
dort war ADR-049 — seit nipp sich beim Klingeln nicht mehr in den Vordergrund
drängt, bleibt als Störung in einer Besprechung nur noch der Klingelton, und
genau den kann man heute nur abschalten, indem man das Klingelgerät wechselt
oder nipp beendet.

**Entscheidung, gefällt am 13.09.2026: der Klingelton schweigt auf Zeit.**

1. **Nur der Klingelton.** Anrufe kommen weiterhin an, der Toast erscheint, die
   Anrufliste füllt sich. Wer zurück ist, hat nichts verpasst — er sieht es.
2. **Kein SIP-Zustand.** nipp veröffentlicht nichts über PUBLISH, und ein
   Kollege sieht am Besetztlampenfeld unverändert «frei». **Das ist eine
   Einschränkung und keine Nachlässigkeit:** eine veröffentlichte Präsenz ist
   eine Aussage über einen Menschen, die auf fremden Bildschirmen stehen bleibt,
   und sie hängt daran, dass die Anlage mitspielt. Wer das will, bekommt eine
   eigene Entscheidung.
3. **Auf Zeit, nicht auf Dauer** — 30 oder 60 Minuten. Ein Schalter, den man
   einschaltet und vergisst, ist die schlechteste Form davon: er nimmt Anrufe
   entgegen, die niemand hört, und niemand merkt es.
4. **Ohne Speicherung.** Der Zustand überlebt keinen Neustart. Wer nipp neu
   startet, ist aus der Besprechung zurück — und eine Einstellung, die man
   suchen muss, um wieder erreichbar zu sein, ist eine Falle.
5. **Im Infobereich-Menü**, nicht in den Einstellungen. Wer in eine Besprechung
   geht, hat keine Zeit für zwei Ebenen; und nipp lebt dort (§10).

**Umsetzung.** `DoNotDisturb` (reiner Zustand mit Uhr von aussen, damit er ohne
Warten prüfbar ist), `SipService.SetDoNotDisturb`, und das Ablaufen prüft der
**Pump** — er tickt ohnehin alle 20 ms, und ein zweiter Taktgeber wäre eine
zweite Stelle, an der der Zustand endet. Stummgeschaltet wird über
`Core.Ring = ""`; beim Aufheben setzt `SettingsApplier.ApplyRingtoneOnly` den
eingestellten Ton neu, statt einen gemerkten Pfad zurückzuschreiben — sonst
liefe er auseinander, sobald jemand den Klingelton wechselt.

**Konsequenz.**

- §10 ist damit **erfüllt in der kleinen Fassung**, nicht in der wörtlichen.
  Wer «Präsenz setzen» im Sinne von SIP-PUBLISH liest, findet es weiterhin
  nicht — das steht jetzt hier statt in einem Kommentar.
- **C15 ist entschieden** und kann in `docs/plans/UX-REVIEW-2.md` geschlossen werden.
- Die Protokollzeile steht auf **Information**: «es hat nicht geklingelt» wäre
  sonst nicht zu beantworten, und das ist genau die Frage, die ein stummer
  Klingelton erzeugt.
- **Am Gerät abzunehmen: T274.**

**Nachtrag vom 17.09.2026 — die Spezifikation zieht nach.** Dieser Entscheid
liess §10 stehen, wie er war, und erklärte ihn für «erfüllt in der kleinen
Fassung». Das hat vier Tage später Arbeit gekostet: die Schreibtisch-Runde A1
hat das Infobereich-Menü am laufenden Programm durchgegangen, «Präsenz setzen»
nicht gefunden und es als **Befund A1-3** eingetragen — mit dem Vermerk, C8 aus
`REVIEW-2026-09-12.md` sei damit am Programm bestätigt. Beides stimmte, und
beides war überflüssig: die Entscheidung stand längst hier, nur nicht dort, wo
jemand nachsieht.

**Dominic hat am 17.09.2026 gestrichen.** In §10 steht jetzt, was das Menü
wirklich trägt — Öffnen, Stumm, Wiedergabegerät, «Klingelton stumm für 30/60
Minuten», Beenden —, und daneben der Verweis hierher. **C8 und A1-3 sind damit
geschlossen**, ohne dass eine Zeile Code entstanden ist.

**Die Lehre ist nicht neu, aber hier steht sie mit einem Preis:** ein ADR, der
eine Vorgabe für «im Geiste erfüllt» erklärt, statt sie zu ändern, lässt die
Vorgabe als offenen Punkt stehen. Wer danach prüft, prüft gegen den alten Text
und meldet einen Fehlschlag, der keiner ist — dieselbe Sorte wie die vier
Mailbox-Zeilen aus A0. **Wer eine Fähigkeit streicht, streicht sie in der
Spezifikation, nicht nur im ADR.**

---

## ADR-054 — Der Benutzer gewinnt, und die Einstellungen wissen, was er angefasst hat

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §9.2, §11, §17, ADR-012, ADR-019, ADR-045, `docs/plans/REVIEW-2026-09-12.md` A1/E3, A2, B4, B13

**Kontext.** `docs/provisioning.md` beschreibt drei Ebenen und den Benutzer als
letzte: was er einstellt, gilt — ausser bei gesperrten Feldern. **Gebaut war
das Gegenteil.** `ApplyValues` schrieb bei **jedem Start** jeden Wert, den das
Profil nannte; das Wort `IsLocked` kam in der Datei nicht vor. Damit galt
faktisch das Profil, und die Sperre entschied nur darüber, ob ein Feld
ausgegraut aussieht. Eine von Hand geänderte Codec-Reihenfolge war nach dem
nächsten Start lautlos weg, und ein selbst angelegtes zweites Konto ebenso —
das Profil ersetzte die Kontenliste vollständig.

Dazu zwei Befunde derselben Familie. **Die Schlüsselliste stand zweimal:** als
`switch` im Dienst mit 29 Pfaden und als Zeichenkettenliste in `nippprov` mit
25. Der Kommentar dort gab die Gefahr zu; eingetreten war sie längst —
`audio.ringtone`, `update.channel`, `update.check-on-start` und `update.token`
fehlten. Und **der SIP-Port erreichte das SDK nie:** Feld, Eingabe,
Profilschlüssel, Validator und Neustart-Hinweis waren da, `core.Transports`
kam im ganzen Telefonie-Ordner nicht vor.

**Entscheidung, gefällt am 13.09.2026: der Benutzer gewinnt.** Im Einzelnen:

1. **`NippSettings.UserOverrides`** führt die Profilpfade, die der Benutzer
   selbst eingestellt hat. Ein Profil überspringt sie und protokolliert das.
2. **Eingetragen wird an genau einer Stelle:** `SettingsService.Write`
   vergleicht neu gegen alt über den Katalog. Kein Aufrufer muss daran denken —
   und das ist Absicht. Eine Erlaubnisliste, an die sich jemand erinnern muss,
   ist die Bauart, die ADR-045 für das Speichern schon einmal verworfen hat.
3. **Eine Sperre gewinnt immer** und löscht die Markierung. Das ist der Weg des
   Administrators, einen Benutzerwert zurückzuholen — ohne ihn wäre die Regel
   eine Einbahnstrasse.
4. **Das Profil speichert über `SaveFromProfile`** und vermerkt damit nichts.
   Sonst gälte sein eigener Wert sofort als Benutzeränderung, und die Regel
   liefe ab dem zweiten Start leer.
5. **`ProvisioningCatalog` ist die eine Liste**, aus der Dienst und Generator
   lesen. Je Pfad ein `Read` und ein `Apply`; `Read` ist zugleich die
   Voraussetzung für Punkt 2.
6. **Der SIP-Port kommt an** (`TransportPorts` plus `SipService`), und zwar für
   den Transport des **ersten Kontos** — zwei Sockets können nicht denselben
   Port belegen. Ohne Konto bleibt alles auf dem Standard des SDK.
7. **Die Zertifikatsprüfung verlässt `RequiresRestart`** (Befund B13): `Apply`
   setzt sie bei jedem Durchlauf sofort, der Hinweis verlangte einen Neustart
   für etwas, das schon gewirkt hatte.

**Die Wanderung.** Eine `settings.json` von vor dem 13.09.2026 hat das Feld
nicht und bekommt eine leere Menge. **Das Profil gewinnt dort beim ersten Start
noch einmal**, wie bisher; ab dem zweiten gilt die neue Regel. Das ist
bewusst — die Alternative wäre, jede bestehende Einstellung zur
Benutzerentscheidung zu erklären und damit jedes vorhandene Profil wirkungslos
zu machen. **Am Gerät zu prüfen: T258.**

**Was nicht dabei ist: ein Knopf «Auf Profilwerte zurücksetzen».** Ohne ihn
kann nur der Administrator per Sperre zurückholen. Für einen Benutzer, der sich
verstellt hat, ist das ein Umweg über den Support; die Massnahme steht als
Welle-1-Punkt in `docs/plans/REVIEW-2026-09-12.md` und wartet auf einen Bedarf aus dem
Alltag.

**Konsequenz.**

- Neu: `Services/Settings/ProvisioningCatalog.cs`,
  `Services/Telephony/TransportPorts.cs`.
- `ISipService.InitializeAsync` nimmt die Einstellungen entgegen: der Port muss
  **vor** `Core.Start()` stehen.
- `docs/provisioning.md` beschreibt jetzt, was der Code tut. Es war umgekehrt
  falsch — und eine Dokumentation, die etwas anderes zusagt als der Code hält,
  ist schlechter als keine.
- Sieben Tests in `ProvisioningUserOverrideTests`, sechs in
  `ProvisioningCatalogTests`, sechs in `TransportPortsTests`.
- **Ein bestehender Test hat seine Bedeutung geändert:**
  `Ein_Profil_ersetzt_die_Kontoliste_und_laesst_den_Rest_stehen` legte das
  Konto vorher über `Save` an und prüfte damit ab heute den Gegenfall. Er legt
  es jetzt über `SaveFromProfile` an und prüft weiterhin den Normalfall — den
  Arbeitsplatz, an dem niemand von Hand eingegriffen hat.

**Am Gerät abzunehmen: T257 bis T259.**

---

## ADR-053 — Die Ausnahmegrenze liegt an der Bridge und am Befehl, nie am SDK

**Datum:** 13.09.2026 · **Status:** **angenommen** · **Bezug:** §6, §8.2, §14.1, ADR-028, `docs/plans/REVIEW-2026-09-12.md` B1–B3, B6, B17, B19, B14

**Kontext.** Die Gesamtprüfung vom 12.09.2026 hat drei Wege gefunden, auf denen
eine Ausnahme nipp beendete, ohne dass irgendwo ein Fänger stand. Alle drei
waren gebaut, keiner absichtlich.

1. **Der Weg durch den nativen Rahmen.** `SipEventBridge` reichte die Callbacks
   des SDK ungeschützt an acht Abonnenten weiter — darunter
   `ContentFrame.Navigate` im Hauptfenster und der Schreibzugriff auf die
   Anrufliste. Die Callbacks kommen aus `linphone_core_iterate`, also über
   einen Reverse-P/Invoke-Rahmen; der `try` in `SipPumpHost.OnTick` liegt
   **ausserhalb** davon und sieht sie nie. **Es ist kein hypothetischer Fall:**
   genau so hat eine `XamlParseException` aus dem Konstruktor der
   Gesprächsansicht nipp schon einmal beim Klingeln mitgenommen.
2. **`LinphoneException` wurde nirgends gefangen** — grep über das ganze `src/`
   ausserhalb des Wrappers: null Treffer. Der Wrapper wirft sie, sobald eine
   native Funktion etwas anderes als 0 zurückgibt, und das tun `Pause`,
   `Resume`, `SendDtmf` und `Terminate`.
3. **Fünf `async void`-Behandler fingen nicht.** «Stumm» drücken, während die
   Gegenseite auflegt, war ein Absturz: `SipService` vergisst ein Gespräch bei
   `End` und `Released`, also **bevor** die Oberfläche nachzieht, und die
   Knöpfe bleiben einen Wimpernschlag klickbar.

Dazu zwei Stellen ohne jeden Fänger: `CallHistoryStore` hatte **keinen
einzigen** `catch`, und sein Konstruktor läuft in `App.OnLaunched`, wo niemand
fängt — eine beschädigte `history.db` hiess: nipp startet nicht. Und im
HID-Lesethread stand `Deute()` ausserhalb des `try`.

**Entscheidung.** Die Grenze liegt an drei Stellen, und an allen dreien fängt
sie **jede** Ausnahme:

1. **`CallbackGuard.Run` um jeden SDK-Callback** (`SipEventBridge`). Ein Fehler
   in einem Abonnenten ist der Fehler dieses Abonnenten, nicht des Telefons.
2. **`SipService.Sdk(…)` um jeden Aufruf ins SDK.** Er übersetzt
   `LinphoneException` in eine `InvalidOperationException` mit einem Satz, den
   der Benutzer lesen kann — **ohne** innere Ausnahme: die Kette wäre sonst ein
   Weg, auf dem ein SDK-Typ die Schicht doch verlässt.
3. **`GuardAsync` um jeden `async void`.** Die Befehle des
   `ActiveCallViewModel` fangen, was von dort kommt, und setzen `LastError`.

`App.OnUnhandledException` bleibt, wie es ist: es protokolliert und setzt
**kein** `Handled`. Wer dort ankommt, hat einen Fehler, den niemand
vorhergesehen hat, und der soll nicht still weiterlaufen.

**Nie still.** Jede gefangene Ausnahme steht auf Stufe `Warning` im Protokoll,
mit Callback- oder Handlernamen und Anrufkennung — aber **nur mit dem Typ und
einer maskierten Meldung**: eine Ausnahmemeldung trägt oft genau das, was nicht
ins Protokoll gehört (§21.2). Eine verschluckte Ausnahme ohne Protokollzeile
ist die Lücke, die dieses Projekt zweimal teuer bezahlt hat.

**Konsequenz.**

- Neu: `Services/Telephony/CallbackGuard.cs`. **Öffentlich, nicht intern** — die
  Zusage gehört geprüft, und ohne `InternalsVisibleTo` geht das nur so; ein
  SDK-Typ steht in keiner Signatur.
- `CallHistoryStore` legt eine unbrauchbare Datei beiseite wie `SettingsService`
  eine kaputte `settings.json`, und **leert vorher den Verbindungspool** —
  sonst hält er das Dateihandle und der Zug scheitert. Jeder Zugriff darauf
  fängt einzeln und gibt einen Ersatzwert.
- `Resolve` nennt die Anrufkennung nicht mehr im Benutzertext (Befund B14). Sie
  ist eine GUID; ins Protokoll gehört sie, in die Meldung nicht.
- `ExceptionBoundaryTests` hält alle drei Regeln als Quelltext-Scan fest.
- **Was das nicht löst:** der Schreibzugriff auf die Anrufliste läuft weiterhin
  synchron aus dem SDK-Callback (§14.1). Er ist jetzt abgesichert, aber nicht
  verlegt — das braucht ein Verbindungsmodell je Thread und eine
  Reihenfolgegarantie und steht als Welle-2-Massnahme in
  `docs/plans/REVIEW-2026-09-12.md`.

**Am Gerät abzunehmen: T261**, und die Gegenproben T04 bis T09.

---

## ADR-052 — Die Breite gehört dem Fenster

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §20.1, §23, ADR-046, ADR-047, ADR-051 (C17)

**Kontext.** Dominic hat die Hauptansicht auf einem Fenster von rund 830
logischen Pixeln fotografiert und die leeren Seitenstreifen rot eingekreist:
der Inhalt stand als 480 Pixel breiter Block in der Mitte, links und rechts je
gut 170 Pixel Nichts. **Das war kein Fehler, sondern die Deckelung aus
ADR-046** — und sie ist nicht mehr gewollt.

Dazu zwei Beobachtungen aus dem breiten Layout: die Umschaltleiste zieht sich
über die ganze Fensterbreite, obwohl sie nur die linke Spalte steuert, und die
Team-Kacheln stehen zu wenige nebeneinander.

**Entscheidung.** Der Inhalt füllt das Fenster. Im Einzelnen:

1. **Die Deckelung entfällt** — schmal wie breit. Der Hauptfluss steht auf
   voller Fensterbreite.
2. **Im breiten Layout ist die linke Spalte fest 480 Pixel breit**, und die
   Kachelspalte nimmt den **ganzen** Rest. Die Obergrenze von 1070 Pixeln aus
   **ADR-051 (C17) wird zurückgenommen.**
3. **Die Umschaltleiste steht im breiten Layout nur unter der linken Spalte**;
   der Kachelbereich reicht neben ihr bis an den unteren Fensterrand.

**Was daran ein gemessener Fehler war und keine Geschmacksfrage.** Punkt 2 hat
eine Ursache, die im Screenshot nicht zu sehen ist: `LeftColumn` und
`TilesColumn` waren **beide** `Width="*"`, die eine auf 480 gedeckelt, die
andere auf 1070. **Ein WinUI-Grid verteilt Sternspalten gleichmässig und kürzt
erst danach an der Höchstbreite — den frei gewordenen Anteil gibt es nicht
weiter** (anders als WPF, das ihn seit .NET 4.7 umverteilt). Auf einem 1920er
Fenster hiess das: links 480, rechts 960, und **468 Pixel blieben tot**. Statt
sieben Kacheln standen vier. Die feste linke Spalte behebt das; eine
Sternspalte neben einer festen bekommt wirklich den Rest.

**Und der zweite Fehler sass eine Ebene tiefer — die Kacheln standen
untereinander, nicht zu wenige nebeneinander.** Die Annahme beim Planen war,
das `ItemsWrapGrid` sei richtig verdrahtet und habe nur zu wenig Platz. **Das
gebaute Fenster hat sie widerlegt:** eine Kachel je Reihe, über die volle
Spaltenbreite gestreckt.

Das Raster stand als `GroupStyle.Panel`, das `ItemsPanel` war ein
`ItemsStackPanel`. **Die virtualisierenden Panels von WinUI
(`ItemsStackPanel`, `ItemsWrapGrid`) tragen die Gruppierung eingebaut und lesen
`GroupStyle.Panel` gar nicht** — das gehört zur alten, nicht virtualisierenden
Gruppierung. Gezeichnet hat also das `ItemsStackPanel`: Köpfe und Kacheln stur
untereinander. Das `ItemsWrapGrid` steht jetzt dort, wo es wirkt, als
`ItemsPanel` mit `GroupHeaderPlacement="Top"`; es ordnet die Kacheln einer
Gruppe nebeneinander an, bricht innerhalb der Gruppe um und beginnt bei jedem
Gruppenkopf eine neue Reihe.

**Der Kommentar daneben behauptete das Gegenteil** — «ein gruppiertes GridView
virtualisiert NUR so» —, und er stand seit ADR-047 unwidersprochen da. Er war
die Begründung dafür, die Stelle nicht anzufassen. **Eine Erklärung, die
niemand gemessen hat, ist eine Behauptung**, und diese hier hat die Ursache
zwei Tage lang zugedeckt: es sah aus wie zu wenig Platz und war das falsche
Panel.

**Und ein Nebenbefund, den erst das Wegnehmen sichtbar gemacht hätte:** der
Spaltenabstand gilt auch für eine Spalte der Breite 0. Solange die Deckelung
den Inhalt in die Mitte stellte, verschwand er im Rand; ohne sie wäre er ein
toter Streifen rechts aussen gewesen — genau der Rand, den diese Entscheidung
wegnimmt, nur schmaler. `ApplyLayout` setzt ihn im schmalen Layout auf 0.

**Konsequenz — und der Einwand, der bewusst getragen wird.** ADR-046 hatte
einen echten Befund: auf den damals gemessenen 1023 Pixeln bestand eine
Kontaktzeile aus zwei Hälften mit neunhundert Pixeln dazwischen, links Avatar
und Name, rechts aussen Präsenztext und Punkt. **Dieser Befund kommt im Band
zwischen 480 und 960 Pixeln zurück.** Er ist Dominic genannt und von ihm
entschieden worden; das ist keine übersehene Regression, sondern eine
abgewogene.

**Die Milderung steht bereit, falls es im Alltag stört**, und sie braucht
keinen Umbau: die Namensspalte in `NippContactRowTemplate` bekommt statt
`Width="*"` ein `Width="Auto"` mit Höchstbreite und eine leere Sternspalte
davor — dann steht die Präsenz direkt hinter dem Namen. Der Preis ist, dass die
Präsenzpunkte nicht mehr über die Zeilen hinweg in einer Spalte stehen. **Nicht
gebaut**, hier nur festgehalten, damit die Antwort dasteht, wenn die Frage
kommt.

**Was gleich bleibt.** Schwelle (960) und Hysterese (40) sind unverändert,
ebenso die Rechnung dahinter: bei genau 960 bleiben rund 450 Pixel für zwei
Kachelzellen. Die Wähltastatur bleibt zentriert — ihre Tasten haben feste
Breiten, und mitwachsende Telefontasten will niemand. **Und entschieden wird
weiterhin an genau einer Stelle**, `ShellViewModel.ApplyWidth`; `ApplyLayout`
zeichnet nur, was daraus folgt.

**Zu prüfen am Gerät:** T251 bis T254, dazu T211 und T242 in neuer Fassung.

---

## ADR-051 — Die Struktur: eine Liste je Eingabeart, und die Karte kommt nach oben

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §20.1, §21.4, ADR-014, ADR-025, ADR-042, ADR-045, ADR-046, ADR-047, ADR-049, ADR-050

**Kontext.** Phase 3 des zweiten UX-Reviews (`docs/plans/UX-REVIEW-2.md`): die Befunde, die
etwas umstellen statt etwas zu reparieren. Zwei davon sind **nicht** umgesetzt,
und das steht am Ende dieser ADR mit Begründung.

### Eine Liste je Eingabeart

Wer einen Namen tippte, sah **zwei Listen gleichzeitig** — die Vorschläge unter
dem Feld und die Treffer im Bereich darunter. Und beide zeigten dieselben
Leute: `UpdateSuggestions` ruft `ContactStore.Search`, der
`LocalSnapshotSearchProvider` der Trefferliste ruft dieselbe Methode. Derselbe
Kollege stand zweimal auf dem Bildschirm, mit **zwei Bedeutungen für dieselbe
Geste**: oben übernimmt ein Klick die Nummer, unten wählt ein Doppelklick.

**Das ist die Doppelanzeige, die ADR-046 bei den zwei Suchfeldern aufgelöst hat
— sie war eine Ebene tiefer gewandert.** ADR-025 hatte das zweite Feld
eingeführt, ADR-046 es entfernt; die Krankheit sass nie im Feld, sondern in der
Frage «welche Antwort gilt».

**Die Regel ist die, die ADR-049 schon der Eingabetaste gegeben hat:**
`NumberNormalizer.IsDialable`.

| Eingabe | Was steht | Warum |
|---|---|---|
| Eine **Nummer** | die Vorschlagsliste | sie ist die Wählhilfe und kennt Kontakte **und die Anrufliste** |
| Ein **Name** | die Trefferliste | sie fragt fremde Systeme mit, zeigt die Herkunft, trägt ein Kontextmenü und klappt in der Zeile auf |

Eine Regel, zwei Wirkungen. Die Listen schliessen sich aus wie Löschkreuz und
Verlaufspfeil im selben Feld.

**Der Review hatte das Gegenteil vorgeschlagen** — die Netztreffer in die
Vorschlagsliste wachsen zu lassen und `SearchBody` aufzulösen. Beim Lesen des
Suchdienstes zeigte sich der bessere Weg: **lokale Quellen werden dort ohnehin
vor dem Debounce gefragt** (`ContactSearchService.RunAsync`), die Trefferliste
ist also genauso schnell wie die Vorschlagsliste — und sie kann mehr. Umgekehrt
kennt nur die Vorschlagsliste die Anrufliste, und die zählt genau dann, wenn
jemand eine Nummer tippt. Jede Liste bleibt, wo sie die bessere ist, und keine
Funktion geht verloren. **Ein Review ist ein Befund und kein Bauplan.**

### Die Anruferkarte steht oben, die Quellen unten

Die Karte ist das, was der Benutzer bei **jedem** Anruf sieht, und sie lag vier
Ebenen tief — Einstellungen, Integrationen, Anruferkarte, Bearbeiten. Drei
davon hiessen nach dem technischen Unterbau.

Sie ist jetzt eine Gruppe erster Ebene neben «Darstellung». Dazu der Weg, der
die Ebenen ganz spart: ein **Rechtsklick auf die Karte im Gespräch** öffnet den
Designer. Das ist der Moment, in dem einem auffällt, dass eine Zeile fehlt.

Umgekehrt wandert alles, was eine Quelle einrichtet, unter «Für
Administratoren». Der Benutzerteil trug bis dahin Basisadresse, Zeitgrenze,
`Authorization`-Schema, Zugangsschlüssel und ein Textfeld für rohes JSON — wer
nach «Klingelton» suchte, kam an «Schema der Authorization-Kopfzeile» vorbei.
Genau die Gleichrangigkeit, die ADR-046 aufgelöst hat, an einer übersehenen
Stelle.

**Warum oben nichts davon zurückbleibt.** Naheliegend wäre eine kurze Liste
«welche Quellen gibt es, an oder aus». Das wäre dieselbe Liste zweimal im
selben Programm — genau der Fehler, den derselbe Entscheid eine Zeile weiter
oben behebt. Eine Quelle ein- und auszuschalten ist selten; «Für
Administratoren» ist nicht gesperrt, nur eine Ebene tiefer.

### Das Kachelraster filtert mit

Der Filter sitzt dort, wo die Gruppen gebaut werden, und gilt damit für Liste
und Raster **aus einer Regel**.

Dazu ein Schutz, der kein Anzeigeproblem ist: `CanReorderTeam` wird falsch,
solange gefiltert wird. `TeamLayout.From` liest die Ordnung aus dem Zustand der
Sammlungen (ADR-042) und schriebe sonst eine Reihenfolge zurück, **in der die
ausgefilterten Nebenstellen fehlen** — Datenverlust, nicht ein verrutschter
Eintrag. Ein laufender Sortiermodus endet, sobald jemand zu tippen anfängt.

### Zwei Kleinigkeiten mit Gewicht

Der Designer sagt jetzt neben seinen Knöpfen «Noch nicht gespeichert.» — er ist
die Ausnahme im Programm (ADR-045 hat den Speichern-Knopf sonst abgeschafft),
und eine Ausnahme, die man nicht sieht, ist eine Falle. Und die Kachelspalte
bekommt eine Obergrenze von 1070 Pixeln: auf einem maximierten Fenster standen
sieben Kacheln nebeneinander, und bei acht Nebenstellen war darunter Leere über
zwei Drittel der Breite. **Dieselbe Begründung wie ADR-046 für die Zeile, nur
fürs Raster.**

> **C17 ist am 12.09.2026 durch ADR-052 zurückgenommen worden.** Die
> Kachelspalte nimmt wieder den ganzen Rest des Fensters. Der Befund dahinter
> war richtig beobachtet und falsch zugeordnet: dass nur vier statt sieben
> Kacheln nebeneinander standen, lag nicht an zu viel Breite, sondern daran,
> dass zwei Sternspalten mit Höchstbreiten den Restplatz verfallen lassen.

### Zwei Befunde bleiben offen, und warum

**C16 («Die Mailbox belegt ein Viertel der Hauptnavigation») ist
zurückgestellt**, und der Grund ist ein Widerspruch, den der Review selbst
nicht gesehen hat: Fassung A wollte die Mailbox-Fläche verstecken, solange
keine Mailboxnummer hinterlegt ist. **Genau dort steht aber der Knopf
«Mailboxnummer eintragen»**, den C8 in derselben Runde repariert hat — die
Fläche zu verstecken nähme den einzigen Weg weg, sie einzurichten. Fassung B
(die Mailbox als Zeile in der Anrufliste) ist die bessere Struktur, berührt
aber §20.1, `ShellSection` und vier Tests, und ihr Gewinn ist eine Fläche in
der Leiste. **Ein Befund, dessen Umsetzung einen anderen Befund rückgängig
macht, wird nicht umgesetzt, sondern nachgedacht.**

**C15 («Nicht stören») ist nicht umgesetzt**, weil es als einziger Befund der
Runde eine **neue Fähigkeit** vorschlägt statt etwas zurechtzurücken — ohne
Auftrag aus `NIPP-BUILD.md`. Und die Hälfte seines Anlasses ist mit ADR-049
weg: das Fenster springt beim Klingeln nicht mehr nach vorn. Was bleibt, ist
der Klingelton in einer Besprechung. Das ist es wert, entschieden zu werden —
aber als Entscheid, nicht als Nebenprodukt.

**Konsequenz.** Abzunehmen als **T241 bis T250**. Damit sind aus den zwanzig
Befunden des zweiten Reviews **achtzehn umgesetzt** (ADR-049 bis ADR-051) und
zwei begründet offen.

---

## ADR-050 — Der Alltag: ein zweites systemweites Kürzel, und die Wähltastatur macht Platz

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §8.5, §9.6, §20.1, §22.5, ADR-026, ADR-045, ADR-046, ADR-049

**Kontext.** Phase 2 des zweiten UX-Reviews (`docs/plans/UX-REVIEW-2.md`): die sieben
Befunde, die keine Fehlbedienung verursachen, aber **jeden Tag Handgriffe
kosten**. Einer davon weitet den Auftrag und ist der eigentliche Grund für
diese ADR.

### Ein zweites systemweites Kürzel, nur fürs Stummschalten

**§22.5 sieht genau einen Hotkey vor** — „Annehmen/Auflegen". Das bleibt so;
dazu kommt ein zweiter, der nichts anderes tut als stumm schalten.

**Warum das den Auftrag wert ist.** Stummschalten ist im Gespräch der
häufigste Griff, und nipp lebt im Infobereich (§10, „minimiert starten" ist
Standard). Der Weg dorthin hiess: Fenster suchen, nach vorn holen, hinsehen,
klicken. Drei Sekunden für etwas, das eine halbe dauern sollte — und in der
halben Sekunde steckt der Sinn: man drückt es, weil jemand ins Büro kommt.

**Die Regel, die dabei halten muss**, ist die aus dem Nachtrag zu ADR-028: *was
ein Tastendruck bedeutet, entscheidet genau eine Stelle.* Sie gilt weiter, und
zwar **je Rolle**: `HeadsetPolicy.Interpret` für Annehmen und Auflegen,
`App.HandleMuteHotkey` fürs Stummschalten. Der zweite Hotkey wird **nicht** in
die bestehende Bedeutung hineingerechnet — genau das war am Headset der Fehler,
als der Gabelzustand mitentschied.

`GlobalHotkeyService` trägt dafür eine `HotkeyRole` je Registrierung, meldet sie
im `Pressed`-Ereignis mit und räumt beide in `Dispose` ab. Windows verlangt die
Kennung je Fenster eindeutig; sie wird aus der Rolle gerechnet.

**Drei Entscheidungen daran, die nicht offensichtlich sind:**

- **Die Vorgabe ist leer, nicht `Ctrl+Shift+M`.** Ein zweites Kürzel, das nipp
  beim ersten Start ungefragt für sich beansprucht, nimmt es womöglich der
  Anwendung weg, in der man gerade sitzt. Der Platzhalter nennt den Vorschlag —
  dasselbe, das Teams benutzt.
- **Es holt nipp nicht nach vorn.** Das ist sein ganzer Sinn; ein Fenster, das
  dabei aufspringt, nähme genau das weg, wofür man die Taste drückt. (Und es
  ist dieselbe Einsicht wie in ADR-049 beim Klingeln.)
- **Ohne Gespräch tut es nichts** — aber es schreibt eine Protokollzeile.
  „Die Taste tut nichts" war am Headset zwei Tage lang nicht von „hier kommt
  nichts an" zu unterscheiden; diese Lücke wird nicht noch einmal gebaut.

Im Fenster kommen dazu `Strg+M`, `Strg+H` und `Strg+E`. Sie führen dieselben
Befehle aus wie die Knöpfe — ein Kürzel mit eigener Fassung von „stumm
schalten" wäre die zweite Wahrheit, an der dieses Projekt schon gelitten hat.

### Die Wähltastatur ist standardmässig zu

`ShowDialpad` stand auf `true`. Gerechnet auf die Standardgrösse 400 × 660
bleiben der Kontaktliste damit **rund 250 Pixel** — fünf Zeilen, verteilt auf
zwei Abschnitte mit Kopfzeilen. Ohne die Tastatur sind es zehn.

Und sie ist auf einem Arbeitsplatz-PC redundant: Ziffern tippt man auf der
Tastatur, für Sprachmenüs steht die eigene Zehnertastatur in der
Gesprächsansicht. **ADR-026 hat sie aus demselben Grund schon aus der
Anrufliste und der Mailbox genommen** — dieser Entscheid führt ihn zu Ende.

Der Umschalter bleibt, wo er ist, und die Wahl überlebt den Neustart: wer die
Tastatur will, schaltet sie einmal ein. Mit Touch-Geräten im Feld wäre das eine
andere Rechnung.

### Ein Hinweisknopf, der am Ziel vorbeiführte

„Mailboxnummer eintragen" sprang auf die Gruppe „SIP-Konten" und liess den
Benutzer dort weitersuchen: der Fokus lag auf „Benutzername", und das Formular
stand als **leeres Anlegen-Formular** da, während die gesuchte Angabe zu einem
bestehenden Konto gehört und in einem zugeklappten Unter-Aufklapper steht.

Der Knopf war die Antwort auf B2 (ADR-045) — *„kein Hinweistext führt dorthin,
wohin er verweist"* —, und er löste sie nur zur Hälfte. Ein drittes Sprungziel
tut jetzt die drei Handgriffe, die alle schon gebaut waren.

**Die Gegenprobe gehört zum Muster und nicht zum Einzelfall:** jeder
`InfoBar.ActionButton` und jeder Hinweisknopf ist einmal durchzuklicken mit der
Frage, ob der Cursor danach in dem Feld steht, von dem der Satz sprach.

### Escape und Alt+Links

Weder die Einstellungsseite noch die Gesprächsansicht reagierten darauf. Beide
tun jetzt, was der Pfeil oben links tut. **Escape beendet kein Gespräch** — auf
der Gesprächsansicht heisst „zurück" wörtlich *„zurück zur Wähltastatur, das
Gespräch läuft weiter"*, und steht ein Bereich offen, schliesst das erste
Escape ihn.

Dabei einmal in die bekannte Falle getreten: `Windows.System.VirtualKey` ist
innerhalb von `Nipp.App` verdeckt, weil der eigene Namensraum
`Nipp.App.Windows` gewinnt. Der Compiler meldet einen fehlenden
Assemblyverweis, und der XAML-Compiler wirft hinterher zwölf Folgefehler über
unbekannte Konverter — **der echte Fehler steht in der ersten Zeile, nicht in
den zwölf darunter.**

### Drei Kleinigkeiten mit demselben Muster

Die Nummer in der Anrufliste (C18), der Wortlaut an den Nummernknöpfen (C19)
und der Hinweis im Sortiermodus (C20) haben gemeinsam, dass **eine Angabe
vorhanden war und an ihrem Ort nicht ankam**: die Nummer stand in der
Datenbank, der Wortlaut stand im Menü, der Modus stand im ViewModel.

**Konsequenz.** Abzunehmen als **T231 bis T240**. `settings.json` bekommt ein
Feld `MuteHotkey` — eine Datei von vor dem 13.09.2026 hat keines und bekommt
auch keines, weil leer die Vorgabe ist.

---

## ADR-049 — Was ein Fehlgriff kosten darf: fünf Wege, auf denen nipp zu viel annahm

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §8.1, §8.2, §8.6, §10, ADR-026, ADR-044, ADR-045, ADR-046

**Kontext.** Ein zweites UX-Review (`docs/plans/UX-REVIEW-2.md`) hat zwanzig Befunde
ergeben — keiner davon eine Wiederholung aus `docs/plans/UX-REVIEW.md`. Diese Entscheidung
trägt die fünf, bei denen **ein einzelner Fehlgriff mehr kostet als eine
Korrektur**: verlorene Arbeit, ein Anruf, den niemand angenommen hat, eine
Aufzeichnung, die niemand wollte.

**Der Befund über den fünf Befunden.** Die Fehlervermeidung stand nach den drei
Phasen von ADR-044 bis ADR-046 bei derselben Bewertung wie davor. Die alten
Löcher waren gestopft, und in denselben Wochen sind drei neue entstanden — alle
drei nach demselben Muster: **ein Weg wurde gebaut und seine Kehrseite nicht
mitgedacht.**

### Der Designer fragt, bevor die Arbeit verlorengeht

`CardDesignerViewModel.HasUnsavedChanges` wurde an drei Stellen gesetzt und an
**einer** gelesen — in `OnSaveClick`, um zu entscheiden, ob das Fenster danach
zugeht. Wer den Designer über das Kreuz verliess, verlor eine halbe Stunde
Kartenarbeit, lautlos.

**Der Kommentar in `Show` behauptete seit jeher das Gegenteil**: *„Es fragt
seinerseits nach ungespeicherten Änderungen."* Eine Zusage im Kommentar, die
der Code nicht einlöste — und damit dasselbe Muster wie `CardKind.History`,
`IntegrationConfig.cards`, `ClipResolver.DescribeCaller` und `App.SdkStatus`,
**zum fünften Mal**. Bei den vier davor fehlte eine Anzeige; hier fehlte eine
Schutzregel, und das kostet Arbeit statt Auffindbarkeit.

`AppWindow.Closing` bricht jetzt ab und fragt. Der Umweg über `args.Cancel` und
ein zweites `Close` ist nötig, weil `Closing` synchron ist und ein
`ContentDialog` nicht. Der Fenstertitel trägt einen Punkt, solange etwas offen
ist.

### Das Fenster springt beim Klingeln nicht mehr nach vorn

**Zwei je für sich richtige Entscheidungen ergaben zusammen einen ungewollten
Anruf.** ADR-044 hat den Fokus beim Klingeln auf „Annehmen" gelegt (B10, und
das bleibt richtig). `MainWindow` holte das Fenster beim Klingeln zusätzlich in
den Vordergrund — `Activate()`, notfalls `SetForegroundWindow`. Wer gerade in
einer anderen Anwendung tippte, bekam nipp vor die Nase, und die nächste
Leertaste oder Eingabetaste nahm den Anruf entgegen.

**Und es stand so nicht in der Spezifikation.** §8.6 sagt: *„Klick auf
Annehmen: App in den Vordergrund."* Der Vordergrund gehört an das **Annehmen**.
Das Zeichen beim Klingeln ist der Toast — Szenario `IncomingCall`, er bleibt
stehen, bis jemand reagiert, und erscheint über einer Vollbildanwendung.

**Die Ausnahme ist eine Regel und keine Einstellung.** Kommt der Toast nicht
durch, ist das Fenster das einzige Zeichen, und dann wiegt der Fokus weniger
als ein Anruf, von dem niemand erfährt. Gefragt wird
`ToastService.NotificationsAvailable` — wer das beantworten kann, ist der
Dienst. Packaged ist es bis heute `false` (T110), und genau deshalb wird
unpackaged ausgeliefert.

### Die Aufnahme fragt, und sie steht nicht mehr neben „Stumm"

„Stumm", „Halten" und „Aufnahme" standen als drei gleich grosse, gleich
aussehende Umschalter nebeneinander. „Stumm" ist der Knopf, den man ohne
hinzusehen drückt; zwei Spalten daneben lag eine Handlung, die in der Schweiz
ohne Kenntnis der Gegenseite strafbar ist (Art. 179ter StGB) und eine Datei auf
der Platte hinterlässt.

**Drei visuell gleichwertige Knöpfe, von denen einer eine andere Art von Folge
hat.** Die Aufnahme wandert deshalb in die zweite Reihe zu „Weiterleiten" und
„Tastentöne" — zu den Handlungen, die man bewusst sucht —, und sie fragt beim
ersten Einschalten **je Gespräch**. Nicht bei jedem Mal: wer bewusst
aufzeichnet, tut es in Serie. Je Gespräch und nicht je Sitzung, weil das
nächste eine andere Gegenseite hat.

### Die Eingabetaste entscheidet nach dem Feldinhalt

Seit ADR-046 ist das Nummernfeld auch das Suchfeld. Die Standardtaste kann aber
nur **eine** Bedeutung tragen, und sie trug die falsche: wer „Meier" tippte und
Enter drückte, löste einen Anruf an `sip:Meier@…` aus. Der `NumberNormalizer`
reicht benannte Ziele absichtlich durch, damit aus „112" nie „+41112" wird —
genau diese richtige Regel machte aus einer Suche einen fehlgeschlagenen Anruf.

Die Prüfung steht im Kern, neben dem Normalizer: **`IsDialable` ist die
Gegenfrage zu `Normalize`** und beantwortet sie aus denselben Regeln.
Adressartiges zählt als wählbar, weil `Normalize` es aus demselben Grund in
Ruhe lässt. Steht ein Name im Feld, nennt `DialIssue` den Grund (ADR-045), und
die Eingabetaste springt in die Trefferliste, statt zu wählen — dieselbe
Zurückhaltung, mit der die Vorschlagsliste seit jeher nur übernimmt.

### Drei Knöpfe, die stillschweigend nichts taten

„Stumm schalten" im Infobereich-Menü ohne laufendes Gespräch, „Entfernen" bei
der letzten Gruppe, und die Geräteliste im Infobereich-Menü ohne ein Wort
darüber, wofür sie gilt.

**Ein Klick ohne Wirkung und ohne Wort ist von einem Fehler nicht zu
unterscheiden** — dieselbe Lücke, die dieses Projekt bei den empfangenen
HID-Reports und beim Symbol im Infobereich schon zweimal teuer bezahlt hat, nur
trifft sie hier den Benutzer statt den Entwickler. Alle drei sind jetzt
beschriftet, ausgegraut oder tragen ihren Grund in der Kurzinfo. Das Menü im
Infobereich wird dafür bei jedem Anrufzustandswechsel neu gebaut: eines, das
beim Start entsteht und nie wieder, zeigt den Zustand von damals.

**Konsequenz.** Abzunehmen als **T222 bis T230**. Die beiden Fälle, die ohne
Gerät nicht zu prüfen sind, stehen dort ausdrücklich: das versehentliche
Annehmen (T223) und die Gegenprobe ohne Benachrichtigungen (T224).

---

## ADR-048 — Der Detailbereich klappt in der Zeile auf, in allen drei Listen

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §20.1, ADR-041, ADR-042, ADR-046, ADR-047

**Kontext.** Gemeldet von Dominic: *„in der aktuellen Liste gefällt mir nicht,
wenn ich einen Kontakt anklicke, dass die Infos ganz unten sind."*

Der Bereich hat in vier Tagen zweimal den Ort gewechselt, und beide Male aus
einem guten Grund:

- **ADR-041** stellte ihn unter die Liste — einen Ort für alle.
- **ADR-042** holte ihn für Team-Nebenstellen in die Zeile, weil er dort näher
  an der Handlung steht. Für Outlook blieb er unten: über hundert Zeilen dürfen
  nicht jede einen ausklappbaren Teil mitbringen.
- **ADR-046** machte daraus wieder einen Ort — unten —, weil dieselbe Handlung
  an optisch gleichen Zeilen zwei Ergebnisse hatte.

**Der Fehler, den ADR-046 behoben hat, war die Ungleichheit, nicht der Ort.**
Und der Ort unten hat einen eigenen Preis, den man erst im Alltag sieht: wer in
einer Liste von 137 Namen unten etwas anklickt, liest die Antwort am anderen
Ende des Fensters. Der Blick muss die ganze Höhe queren und dabei die Zeile
halten — dasselbe Argument, mit dem ADR-046 die Breite gedeckelt hat, nur um 90
Grad gedreht.

**Entscheidung.** Der Bereich klappt **in der Zeile** auf, in allen drei Listen
gleich — Nebenstellen, Outlook-Kontakte und Suchtreffer. `ContactRow` bekommt
`IsDetailExpanded` zurück; gesetzt wird die Fahne **ausschliesslich im Setter
von `ShellViewModel.ExpandedContact`**, damit «es ist höchstens eine offen»
eine Eigenschaft der Eigenschaft ist und kein Vertrag, an den sich drei
Aufrufer erinnern müssen. Dasselbe Muster wie `TeamOrder.ApplyLayout`, das mit
`TeamGroups.Normalize` endet (ADR-042).

**Der Leistungseinwand aus ADR-042 ist ausgeräumt, nicht umgangen.** Der
Detailteil hängt an `x:Load="{x:Bind IsDetailExpanded, Mode=OneWay}"`: der
Teilbaum entsteht beim Aufklappen und verschwindet beim Zuklappen wieder. Über
hundert Outlook-Zeilen tragen damit **null** zusätzliche Elemente, solange
niemand etwas angeklickt hat — es ist nie mehr als **ein** Detailbereich im
Baum, egal wie lang die Liste ist. Geprüft ist bisher, dass der XAML-Compiler
daraus die erwartete Lade-Mechanik erzeugt (`FindName` / `UnloadObject`); die
Messung am Gerät steht als **T220** aus.

**Konsequenz.**

- Der Bereich unter der Liste ist weg, samt seinem Schliesskreuz. Geschlossen
  wird durch erneutes Anklicken derselben Zeile — die Geste, mit der er aufging.
- Eine Vorlage für den Inhalt (`NippContactDetailTemplate`), von allen drei
  Zeilenvorlagen über ein `ContentControl` benutzt. Drei Kopien wären drei
  Gelegenheiten, sie auseinanderlaufen zu lassen.
- Der Doppelklick wird im aufgeklappten Teil abgefangen. Ohne das wählte ein
  Doppelklick auf einen Nummernknopf zusätzlich die Hauptnummer: `Tapped` läuft
  nach aussen weiter und trifft dort den Doppelklick-Handler der Zeile — bei
  einem Telefon wird dabei sofort gewählt.
- Die Zeile wird nach dem Aufklappen in den Blick gescrollt: die Liste wächst
  unter ihr, und ohne das rutscht sie hinaus.
- **Im breiten Layout gibt es ihn bei den Nebenstellen gar nicht mehr** — eine
  Kachel zeigt alles direkt (ADR-047).

---

## ADR-047 — Zwei Spalten ab 960 Pixeln, die Nebenstellen als Kacheln

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §20.1, **§23**, §8.4, ADR-041, ADR-042, ADR-044, ADR-046

**Kontext.** ADR-046 hat die Inhaltsbreite auf 480 Pixel gedeckelt und dabei
wörtlich festgehalten: *„Wer den Platz nutzen wollte, bräuchte eine zweite
Spalte — das wäre eine Abweichung von §20.1 und ein eigener Entscheid."* Dies
ist dieser Entscheid; der Auftrag dazu steht als **§23** in `NIPP-BUILD.md`.

Der Anlass ist der Alltag: nipp läuft auf einem 24-Zöller im Vollbild, und dort
standen links 480 Pixel Inhalt und daneben eine leere Fläche. Die häufigste
Frage an ein Softphone — *„ist der Kollege frei?"* — kostete dabei einen
Wechsel in den Kontakte-Bereich und einen Klick in die Zeile.

**Entscheidung.** Ab **960 logischen Pixeln** stehen zwei Spalten: links
Kontoauswahl, Nummernfeld, Wähltastatur und der gewählte Bereich, rechts die
Nebenstellen als **Kacheln**, nach Gruppen gegliedert.

**Die Schwelle ist gerechnet und nicht geschätzt:** 24 Seitenrand + 480 linke
Spalte + 12 Spaltenabstand + rund 30 für Rahmen und Bildlaufleiste + zwei
Kachelzellen à 200 = 946, aufgerundet auf 960. Darunter stünde rechts **eine**
Kachelspalte, und dafür ist die Liste die bessere Form.

**Mit Hysterese, und die ist kein Feinschliff:** zurück auf eine Spalte geht es
erst unter 920. Wer das Fenster genau an der Schwelle zieht, baute sonst bei
jedem Pixel die halbe Seite neu auf — auf dem Thread, der alle 20 ms
`Core.Iterate()` bedient.

### Die Deckelung wandert, sie verschwindet nicht

Im breiten Layout deckelt `NippContentMaxWidth` die **linke Spalte** statt des
ganzen Fensters. Damit bleibt eine Kontaktzeile so breit wie im schmalen
Fenster, und die Zusage von ADR-046 gilt wörtlich weiter: keine Zeile zerfällt
in zwei Hälften mit neunhundert Pixeln Nichts dazwischen.

> **Am 12.09.2026 durch ADR-052 revidiert.** Sie verschwindet doch: der Inhalt
> füllt jetzt das Fenster, und die linke Spalte ist im breiten Layout **fest**
> 480 Pixel breit statt gedeckelt. Der Unterschied ist nicht kosmetisch — zwei
> Sternspalten mit Höchstbreiten liessen den Restplatz verfallen.

### Die Umschaltleiste steuert die linke Spalte

Kontakte, Anrufe, Mailbox und Einstellungen bleiben, wo sie sind, und wechseln
den Inhalt **links**. Die Kacheln rechts stehen unabhängig davon.

> **Seit ADR-052 steht sie im breiten Layout auch nur unter der linken
> Spalte** — sie steuert links, also gehört sie dorthin, und vier Knöpfe über
> 1920 Pixel gezogen waren vier riesige Flächen mit einem 16-Pixel-Symbol in
> der Mitte. Im Bereich
«Kontakte» zeigt die linke Spalte dann nur noch Outlook und die Suchtreffer —
**zweimal dieselbe Liste in einem Fenster** wäre die Doppelanzeige, wegen der
ADR-046 die beiden Suchfelder zusammengelegt hat.

### Warum eine Kachel und nicht eine breite Zeile

Die Kachel zeigt, was die Zeile erst nach einem Klick zeigt: Name, Firma,
Zustand als Punkt **und** Text (§8.4) und jede Nummer einzeln wählbar. Das ist
der Sinn der Form, nicht ihr Schmuck — sie ersetzt «anklicken, aufklappen,
lesen» durch «hinsehen».

**Der Statuston färbt Punkt, Schrift und Rand — nie die Fläche** (ADR-044).
Eine Kachel, die im Gesprächszustand gelb hinterlegt wäre, stünde im dunklen
Erscheinungsbild bei rund 1,4:1; genau deshalb gibt es die Regel. Ist kein
Zustand bekannt, bekommt der Rand die gewöhnliche Kartenfarbe: ein grauer
Sonderrand behauptete sonst eine Beobachtung, die nicht stattfindet.

**Die Kachelgrösse ist fest, und das ist erzwungen.** Ein `ItemsWrapGrid` misst
die erste Kachel und gibt allen anderen dasselbe Mass; ungleich hohe Kacheln
ergäben ein Raster mit Löchern. Die Höhe trägt genau **zwei** Nummernzeilen —
Nebenstelle und Handy, der Regelfall seit ADR-041. Eine dritte Nummer steht
hinter dem Zusatz «+1 Nummer». Zeigt sich im Alltag, dass drei häufig sind, ist
das eine Zahl in `Tokens.xaml`.

### Was unverändert bleibt, und warum das der Punkt ist

`TeamLayout.From` liest die Ordnung aus dem **Zustand der Sammlungen** und
nicht aus dem Ziehereignis (ADR-042). Das gilt für ein `GridView` wörtlich
gleich — **das Umsortieren zwischen Gruppen übersteht den Umbau ohne eine
Zeile Änderung**, samt Kontextmenü «In Gruppe verschieben» für den Weg ohne
Maus. Auch `ContactGroupRow` mit seinem Klappzustand bleibt, wie es ist.

**Konsequenz.**

- Der Layoutzustand liegt im Kern (`ShellViewModel.ApplyWidth`,
  `ShellLayout`), nicht im XAML. `Nipp.App` hat kein Testprojekt, und an dieser
  einen Entscheidung hängt die halbe Hauptansicht; eine Regel, die nur im XAML
  steht, ist eine ungeprüfte Regel. Achtzehn Tests halten sie fest.
- **Kein `AdaptiveTrigger`.** Er kann die Breite messen, aber nicht mit der
  gewählten Sektion verrechnen — «zeige das Team links, aber nur im schmalen
  Layout» stünde dann ein zweites Mal ausgeschrieben im XAML.
- **Ein Baum und nicht zwei.** Die zweite Spalte hat im schmalen Layout die
  Breite 0. Ein zweites XAML für das breite Format wäre die Doppelwahrheit, die
  dieses Projekt an vier Stellen teuer bezahlt hat.
- **Zwei getrennte `CollectionViewSource`** für Liste und Raster: ein
  `ICollectionView` führt ein `CurrentItem`, und zwei Listen, die sich eines
  teilen, teilen damit auch ihre Auswahl. Die Gruppen und Zeilen darin sind
  dieselben Instanzen — sonst hätte das Raster Lampen, die einfrieren.
- **`ItemsStackPanel` aussen, `ItemsWrapGrid` im `GroupStyle.Panel`.** Ein
  gruppiertes `GridView` virtualisiert nur so. Ohne diese Zeile erzeugt es
  jede Kachel jeder Gruppe sofort — derselbe Fehler wie zwei `ListView`s in
  einem `ScrollViewer`, nur unter anderem Namen, und wieder ein Ruckeln statt
  eines Absturzes (**T215**).
- Das Kontextmenü über die Menütaste sucht jetzt einen `SelectorItem` statt
  eines `ListViewItem`: eine Kachel steckt in einem `GridViewItem`, und die
  beiden sind nur über diesen Basistyp verwandt. Ohne das griffe die Menütaste
  auf einer Kachel ins Leere — genau der Befund, den ADR-044 für die
  Kontaktliste behoben hat.
- **Nebenbefund, und er wäre sonst erst im Alltag aufgefallen: ein maximiertes
  Fenster kam nicht maximiert zurück.** `WindowPlacement.Capture` schrieb
  Position und Grösse, nicht den Zustand des Presenters, und
  `TryApplyRemembered` klemmt zusätzlich auf 92 Prozent des Arbeitsbereichs —
  wer nipp maximiert schloss, fand es beim nächsten Start als beinahe volles
  Fenster mit Rand ringsum. Solange das schmale Fenster der Normalfall war, war
  das eine Kleinigkeit; wenn Vollbild der Anlass für ein ganzes Layout ist,
  ist es einer. Gemerkt wird jetzt das Wort `maximized` statt vierer Zahlen:
  die Lage eines maximierten Fensters gehört dem Bildschirm, auf dem es steht.
  Ältere Einträge mit vier Zahlen lesen sich unverändert weiter (**T221**).
- **Die Gesprächsansicht bleibt einspaltig.** Kacheln, die während eines
  Gesprächs weiterleiten, sind ein starker Griff — und sie legten die
  gefährlichste Handlung des Programms auf einen einzelnen Klick, kurz nachdem
  T188 Enter aus dem Weiterleitungsfeld genommen hat. Eigene Etappe.

---

## ADR-046 — Zwei Ebenen in den Einstellungen, ein Suchfeld, ein Detailbereich, eine gedeckelte Breite

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §14.8, §20.1, §21.4, ADR-014, ADR-025, ADR-041, ADR-042, ADR-044, ADR-045

**Kontext.** Der dritte Teil aus `docs/plans/UX-REVIEW.md`: die Struktur. Was hier steht,
sind Zusammenlegungen — überall dort, wo dieselbe Sache an zwei Orten stand
oder an zwei Orten anders aussah.

### Die Einstellungen bekommen eine zweite Ebene

Eine Seite, gezählt: **25 Textfelder, 23 Auswahllisten, 19 Schalter, 4
Zahlenfelder, 2 Schieberegler, 38 Schaltflächen, 31 Aufklappbereiche.** Auf
derselben Ebene standen, was ein Benutzer braucht (Konto, Mikrofon,
Klingelton), was ein Administrator einmal setzt (SIP-Port, Keep-Alive, STUN,
ICE, Codec-Reihenfolge, DTMF-Verfahren, Provisioning) und was ein Entwickler
tut (Endpunkte als JSON).

Der Benutzer, der seinen Klingelton sucht, scrollte an *„Registrierungsdauer
(Sekunden)"* und *„Keep-Alive (Sekunden, 0 = aus)"* vorbei. Und wer hier etwas
verstellt, das er nicht versteht, bemerkt es unter Umständen erst, wenn keine
Gespräche mehr zustande kommen — die Warnung an „Verschlüsselung erzwingen" war
die einzige ihrer Art.

Oben stehen jetzt **Konto · Audio · Darstellung · Kontakte · Start und
Bedienung · Integrationen**, unten ein Aufklapper **„Für Administratoren"** mit
Netzwerk, Codecs, Provisionierung und Diagnose sowie Sichern und Zurücksetzen.
Die Aktualisierung wandert von der Mitte in den Fuss, zum Versionsblock —
dorthin, wo man sie sucht.

**Ein Aufklapper und keine eigene Seite:** es bleibt **ein** Ort, an dem alles
steht, und ein Supporter am Telefon sagt weiterhin „ganz unten aufklappen",
statt einen zweiten Navigationsweg zu erklären.

**Die Gruppe „Erweitert" ist aufgelöst.** Sie war kein Thema, sondern ein
Restehaufen: Länderpräfix und Aufnahmeordner gehören zum Benutzer,
Protokollierung und Provisionierung zum Administrator.

### Ein Suchfeld statt zwei

Übereinander standen zwei Eingaben, die beide Kontakte finden: das Nummernfeld
(lokal, sofort, höchstens fünf Vorschläge) und darunter *„In «Quelle» und
Outlook suchen"*, das zusätzlich die angebundenen Systeme fragt.

Die Trennung war begründet — Netzlatenz gehört nicht dorthin, wo jemand eine
Nummer tippt — und für den Benutzer blieb sie trotzdem die Frage: **in welches
Feld tippe ich?** Wer den Kollegen oben suchte, fand ihn. Wer den Kunden oben
suchte, fand ihn nicht und hielt ihn für nicht vorhanden. **Genau dieser
Fehlschluss steht in ADR-025 und hat zum zweiten Feld geführt** — zwei Felder
lösen ihn nicht, sie verschieben ihn.

Jetzt **ein Feld, zwei Tiefen**: die lokalen Treffer stehen unverändert sofort
unter dem Nummernfeld, dieselbe Eingabe geht entprellt an die Quellen, und ihre
Treffer erscheinen in der Liste darunter. **Die Zusage aus ADR-025 bleibt
eingehalten** — das Netz liegt nicht im Tippweg. Der Platzhalter des
Nummernfelds nennt die Quellen, die es mitfragt; ohne Netzquelle bleibt es bei
„Nummer".

### Ein Detailbereich statt zwei

Eine Team-Zeile klappte bei Auswahl **in der Zeile** auf, eine Outlook-Zeile
und ein Suchtreffer **unter der Liste** — an optisch identischen Zeilen.
Dieselbe Handlung, zwei Ergebnisse, und wo die Antwort erscheint, war nicht
vorhersagbar; bei der Team-Liste wanderte dabei alles darunter nach unten.

Der Grund für die Trennung war Leistung (ADR-042): über hundert Outlook-Zeilen
dürfen nicht jede einen ausklappbaren Teil mitbringen. Das war richtig — und
die Lösung ist, den **einen** Ort zu nehmen, den es ohnehin gibt: den Bereich
unter der Liste. Er verschiebt nichts, und die Team-Vorlage ist damit wieder so
leicht wie die der anderen. `ContactRow.IsDetailExpanded` ist gelöscht.

### Eine gedeckelte Breite

§20.1 gibt ein schmales Fenster im Smartphone-Format vor; die Standardbreite ist
400 Pixel, die Mindestbreite 320. Eine **Obergrenze gab es nicht**, und das
Fenster ist frei ziehbar — gemessen am 12.09.2026 stand es auf **1023 Pixeln**.
Jede Zeile bestand dann aus zwei Hälften mit neunhundert Pixeln Nichts
dazwischen: links Avatar, Name und Nummer, rechts aussen Präsenztext und Punkt.
Um zu sehen, ob ein Kollege frei ist, musste der Blick die ganze Fensterbreite
queren und dabei die Zeile halten.

Die Inhaltsspalte ist auf 480 Pixel gedeckelt. **Das hält die Vorgabe ein,
statt sie zu brechen:** die Proportionen bleiben, egal wie breit das Fenster
steht. Wer den Platz nutzen wollte, bräuchte eine zweite Spalte — das wäre eine
Abweichung von §20.1 und ein eigener Entscheid.

> **Am 12.09.2026 durch ADR-052 zurückgenommen.** Erst kam die zweite Spalte
> (ADR-047), dann fiel die Deckelung ganz: die leeren Seitenstreifen im Band
> zwischen 480 und 960 Pixeln wogen im Alltag schwerer als die
> auseinandergezogene Zeile. **Der Befund oben bleibt richtig** — er ist jetzt
> ein bewusst getragener Preis, und die Milderung steht in ADR-052.

### Das Wiedergabegerät ist zwei Klicks entfernt

Mikrofon, Lautsprecher und Klingelgerät standen ausschliesslich in den
Einstellungen. Das ist der Wechsel, der im Alltag am häufigsten vorkommt —
Headset angesteckt, Headset abgezogen, Dockingstation verlassen —, und nipp
meldet ihn längst von selbst in der Hinweisleiste; nur die Handlung darauf lag
fünf Klicks entfernt, hinter einer Gruppe auf einer Seite, die man im Gespräch
gar nicht sehen will.

Das Wiedergabegerät lässt sich jetzt **in der Kopfleiste der Gesprächsansicht**
und **im Infobereich-Menü** wählen. Beide Wege gehen über
`SettingsService.Save` — `Changed` löst das Anwenden aus, und ein zweiter Weg
am SDK vorbei wäre die nächste Doppelwahrheit. Nebenwirkung, die zählt: die
Wahl überlebt den Neustart, was ein Benutzer nach einem Gerätewechsel erwartet.

**Das geht über §20 hinaus** und ist hiermit festgehalten. Im Infobereich
stehen die Geräte nur, wenn es mehr als eines gibt — eine Wahl zwischen einer
Möglichkeit ist keine.

### Die Sprachausgabe hört, was auf dem Bildschirm steht

Die Listenzeilen setzen ihren Namen auf dem umschliessenden Raster. Damit gilt
dieser Name für die ganze Zeile, und was sonst noch darin steht, wird beim
Durchgehen nicht mehr vorgelesen:

- die **Präsenz** fehlte, obwohl sie sichtbar danebensteht — §8.4 verlangt
  Farbe *und* Text, und sichtbar war beides da;
- die Anruflistenzeile nannte nur bei **ungelesenen** verpassten Anrufen etwas
  darüber hinaus: ein gesehener verpasster Anruf wurde wie ein angenommener
  angesagt, und **wann** er war, stand nur in der Spalte, die der Name
  überdeckt;
- die **Abzeichen** an „Anrufe" und „Mailbox" setzten nur ihren Zahlenwert —
  „drei verpasste Anrufe" kam nie an.

Alle drei sind ergänzt. `RelativeTime` ist dafür aus `ShellViewModel`
herausgelöst worden; eine zweite Kopie der Regel wäre die dritte Stelle
gewesen.

### Kleineres im selben Zug

- **Tastenkürzel.** In der ganzen Anwendung gab es zwei, beide im
  Karten-Designer. Jetzt Strg+1 bis 4 für die Bereiche und Strg+F ins
  Nummernfeld — und die Kürzel stehen in den Kurzinfos, sonst findet sie
  niemand. Annehmen und Auflegen deckt der systemweite Hotkey ab; ein zweiter
  dafür wäre derselbe Befehl an zwei Stellen.
- **Klickziele auf mindestens 32 Pixel**, die Buchstabenzeile der Wähltastatur
  von 8 auf 10, die Avatar-Initialen auf die Caption-Grösse. Acht Pixel liegen
  unter jeder Ramp der Fluent-Skala — dieselbe Regel, die für die
  Umschaltleiste längst festgehalten ist.

### Zwei Nachträge vom ersten Start

**Der Quellenhinweis passt nicht in den Platzhalter.** Er stand zuerst dort —
*„Nummer oder Name — auch in «Quelle»"* — und war abgeschnitten: das Feld trägt
die Rufnummer in Schriftgrösse 20 und hält rechts 76 Pixel für zwei Symbole
frei. **Ein abgeschnittener Platzhalter sagt weniger als ein kurzer.** Der
Platzhalter heisst jetzt „Nummer oder Name", der Hinweis steht in
Beschriftungsgrösse in der Zeile darunter — an derselben Stelle wie die
normalisierte Form, und beide sind sich nie im Weg: die eine gilt bei leerem
Feld, die andere bei gefülltem.

**Die gruppierte Liste bringt eine Auswahl mit, die niemand getroffen hat.**
Ein `ICollectionView` führt ein `CurrentItem`, und die `ListView` zieht ihre
Auswahl daran nach — beim Anhängen ist das die erste Zeile. Solange der
Detailbereich nur Outlook betraf, sah man davon nichts; mit einem Bereich für
alle drei Listen ging er **beim Start von selbst auf**, mit dem Namen
irgendeines Kollegen darin. Beim Anhängen wird die Auswahl jetzt gelöst, und
`UpdateContactDetails` füllt den Bereich für **alle** Zeilen — die
`IsTeam`-Ausnahme war ein Rest aus ADR-042 und hinterliess einen leeren Kasten
mit einem Schliessen-Kreuz.

**Beides ist beim ersten Start der gebauten Fassung aufgefallen**, nicht in den
Tests: das eine ist eine Frage der Textbreite, das andere eine Eigenschaft von
WinUI. Dafür gibt es keinen Komponententest — dafür gibt es T203 und T205.

**Konsequenz.** Wer eine Einstellung hinzufügt, entscheidet zuerst, ob sie ein
Benutzer braucht — sonst gehört sie unter „Für Administratoren". Der
Detailbereich der Kontakte steht an **einer** Stelle; wer ihn wieder in eine
Zeilenvorlage legt, baut die Ungleichheit neu auf. Und wer eine Listenzeile
umbaut, prüft, was ihr `AccessibleName` sagt: der Name auf dem Raster überdeckt
alles darunter.

**Am Gerät abzunehmen: T202 bis T210.**

---

## ADR-045 — Jede Änderung wirkt sofort, und jeder Hinweis führt dorthin, wohin er verweist

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §9.1, §15, §20, ADR-033, ADR-040, ADR-041, ADR-044

**Kontext.** Der zweite Teil der Umsetzung aus `docs/plans/UX-REVIEW.md`: der Weg vom
ersten Start bis zum ersten Anruf, und der einzige Ort, an dem in nipp Arbeit
verlorenging.

### Fünf Speicherregeln werden eine

Auf der Einstellungsseite galten nebeneinander:

- **sofort wirksam** — „Konto hinzufügen" registrierte und schrieb die Datei,
  bevor die Anlage geantwortet hatte;
- **erst mit „Speichern" unten** — Nebenstellen, Gruppen, Audio, Darstellung,
  Netzwerk, Codecs, alles Übrige;
- **eigenes „Übernehmen"** — der Integrationsblock, dreimal;
- **eigenes „Ablegen"** — jedes Zugangsgeheimnis einzeln;
- **eigenes Fenster mit eigenem „Speichern"** — der Karten-Designer.

Es gab keinen Zustand „geändert, nicht gespeichert", keine Warnung beim
Verlassen über den Pfeil und keine beim Wechsel auf einen anderen Tab.

**Der Schaden lag nicht beim Verklicken, sondern beim Vergessen.** Wer eine
Nebenstelle anlegte, sah sie in der Liste stehen — das ist die Rückmeldung, die
man erwartet, und sie war hier keine Zusage. Ein Klick auf den Rückweg, und die
Eingabe war weg, ohne Meldung. Dass das *Konto* darüber sich anders verhielt,
machte die Regel unlernbar: die eine Erfahrung widerlegte die andere.

**Die Klassendokumentation begründete das Gegenteil** — „arbeitet auf einer
Kopie … wer sich verklickt, kann die Seite verlassen, ohne etwas angerichtet zu
haben". Die Begründung war nachvollziehbar und trug trotzdem nicht: sie galt
für die Hälfte der Seite, und der teurere Fehler war der andere.

**Jetzt wirkt jede Änderung, sobald sie gemacht ist.** Schalter, Auswahllisten,
Regler und Sammlungen mit der Änderung; Freitext- und Zahlenfelder beim
Verlassen des Feldes (`ApplyEdits`, gerufen aus `FocusManager.LosingFocus`) —
sonst prüfte jeder Tastendruck eine halbfertige Eingabe und beanstandete das
Tippen selbst. Geschrieben wird nur, was `Validate` durchlässt; sonst steht die
Beanstandung in `Error` und die Datei bleibt, wie sie war.

Die Schaltfläche **„Speichern" ist entfallen.** Der Neustarthinweis für die drei
Werte, die nicht im laufenden Betrieb greifen, bleibt.

**Eine Sperrliste, keine Erlaubnisliste.** `NurAnzeige` nennt die
Eigenschaften, die nichts einstellen. Wer dort eine vergisst, bekommt eine
überflüssige Schreiboperation mit demselben Inhalt; bei einer Erlaubnisliste
wäre das Vergessen ein stiller Datenverlust — also genau der Fehler, den diese
Entscheidung abstellt. **Die Liste irrt in die harmlose Richtung.**
`SettingsSaveModelTests` prüft per Reflexion, dass beide Listen auf vorhandene
Eigenschaften zeigen.

### Jeder Hinweis führt dorthin, wohin er verweist

Im ganzen Programm gab es **keinen einzigen** `InfoBar.ActionButton` und
ausserhalb des Impressums keinen einzigen `HyperlinkButton`. Jeder Hinweis
beschrieb einen Weg in Prosa: *„Noch kein SIP-Konto eingerichtet. Unten auf
Einstellungen tippen."* Der Benutzer las die Wegbeschreibung, merkte sie sich,
klickte weg, suchte die Gruppe, klappte sie auf — vier Handgriffe für etwas,
das ein Klick ist, und beim ersten Start der Handgriff, an dem alles hängt.

Die Hinweisleiste trägt jetzt **„Konto einrichten"**, der leere Kontaktbereich
**„Nebenstelle anlegen"**, und die Mailbox ohne hinterlegte Nummer
**„Mailboxnummer eintragen"**. Alle drei navigieren **und** klappen die richtige
Gruppe auf und setzen den Fokus ins erste Feld — ohne den zweiten Teil wäre der
Knopf nur eine Abkürzung für den Tabwechsel.

### Der Erststart hat einen Einstieg

Ohne eingerichtetes Konto klappt die Einstellungsseite die Gruppe „SIP-Konten"
von selbst auf. Vorher sah der Benutzer neun zugeklappte Überschriften und
musste raten — ausgerechnet in dem Moment, in dem das Programm ihm gerade
gesagt hatte, dass ihm ein Konto fehlt.

Drei der vier sichtbaren Felder sind Pflicht, und beschriftet war nur das
optionale („Anzeigename (optional)"). Unter der Schaltfläche steht jetzt, was
fehlt — **das erste fehlende Feld**, nicht alle drei; eine Aufzählung beim
leeren Formular wäre eine Beanstandung, bevor jemand etwas getan hat.

**Und die Domain wird geprüft, bevor sie zwölf Sekunden kostet.**
`SettingsValidator.DescribeDomainIssue` erkennt die vier Formen, in denen
jemand eine SIP-Adresse statt einer Domain einträgt: `sip:`-Präfix, At-Zeichen,
Leerzeichen, Schrägstrich. Bewusst nachsichtig — eine Anlage kann im Intranet
stehen und „pbx" heissen, ein fehlender Punkt ist deshalb kein Grund.

### Eine unvollständige Installation sieht nicht mehr aus wie „kein Konto"

`App.SdkStatus` wurde beim Start gesetzt und als öffentliche Eigenschaft
angeboten — **gelesen hat sie keine Ansicht.** Fehlte das SDK, sah der Benutzer
genau dasselbe Bild wie bei „noch kein Konto eingerichtet", und die fertig
formulierte Meldung aus `SipErrorCatalog.DescribeSdkUnavailable` erreichte ihn
erst, wenn er erfolglos ein Konto anzulegen versuchte. Zwei grundverschiedene
Ursachen, ein Bildschirm, die falsche Abhilfe.

**Dasselbe Muster zum vierten Mal** — nach `CardKind.History`,
`IntegrationConfig.cards` und `ClipResolver.DescribeCaller`: eine Fähigkeit war
gebaut und nicht angeschlossen.

### Kleineres im selben Zug

- **Gründe an deaktivierten Schaltflächen.** „Anrufen" ist grau, wenn zwei
  Gespräche laufen — das stand nirgends, und der Grenzfall tritt unter
  Zeitdruck auf. Der Grund steht jetzt als Kurzinfo **und** als Hilfetext für
  die Bedienhilfe; ein Tooltip ist mit der Tastatur nicht erreichbar.
- **Der Katalog überspringt sich bei einer Vorlage.** Seit ADR-040 liegt genau
  eine bei, und „Quelle hinzufügen" öffnete trotzdem einen Auswahldialog mit
  einer Zeile. Der Import einer Anbietervorlage steht jetzt **zuerst** — seit
  die beiden eigenen Anbieter den Quelltext verlassen haben, ist er der
  Normalweg.
- **Technische Innereien raus aus den Benutzertexten.** Ein Paragraf des
  Pflichtenhefts (`§9.5`), ein Methodenname (`InitializeAsync`), „das SDK", ein
  .NET-Typname im Leerzustand, nackte HTTP-Codes als ganze Aussage, und
  *„Outlook öffnen und den Tab neu betreten"* — ein UI-Begriff, den es in
  dieser Oberfläche nicht gibt. Dazu Meldungen ohne Abhilfe, wo der Rest des
  Katalogs eine hat.

  **Nicht betroffen:** die Diagnosetexte zum Besetztlampenfeld („RFC 4235",
  „siehe docs/decisions.md"). Der UX-Bericht führte sie als Benutzertexte; sie
  gehen in Wirklichkeit ins **Protokoll**, und dort ist ein RFC-Verweis genau
  das Richtige. Sie bleiben unverändert.

**Konsequenz.** Die Einstellungsseite hat keinen „Speichern"-Knopf mehr; wer
eine Eigenschaft hinzufügt, die nichts einstellt, trägt sie in `NurAnzeige`
ein, und wer ein Freitextfeld hinzufügt, in `ErstBeimVerlassen`. Beide Listen
sind per Reflexion abgesichert. Ein Hinweis, der einen Weg beschreibt, bekommt
die Schaltfläche dazu — und die klappt auf, was sie meint.

**Am Gerät abzunehmen: T194 bis T201.**

---

## ADR-044 — Ein Wort je Zustand, ein Weg ohne Maus, und ein Ton färbt Schrift statt Fläche

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §8.2, §8.4, §15, §20.1, ADR-032, ADR-042, ADR-043

**Kontext.** Ein UX-Review der ganzen Oberfläche (`docs/plans/UX-REVIEW.md`) hat
fünfundzwanzig Befunde ergeben. Diese Entscheidung trägt die neun, die ohne
Umbau zu beheben waren und je eine Fehlbedienung, einen Lesbarkeitsfehler oder
eine gebrochene Tastaturbedienung beseitigen.

### Ein Wort je Zustand

Der Zustand eines Anrufs stand **dreimal** im Code — in `ShellViewModel`, in
`ActiveCallViewModel` und im Code-behind der Gesprächsansicht —, jedes Mal mit
eigenem Wortlaut: *„eingehender Anruf"* gegen *„Anruf wartet"*, *„von der
Gegenstelle gehalten"* gegen *„von der Gegenseite gehalten"*. Der Zustand eines
Kontos hatte **vier** Fassungen: die Hauptansicht sagte *„angemeldet"*, die
Einstellungen *„Angemeldet"*, das Infobereich-Symbol *„registriert"*, und die
Fehlermeldungen sprachen von *„Registrierung … fehlgeschlagen"*.

**Kein Benutzer sieht zwei davon gleichzeitig — er sieht sie nacheinander.**
Wer eine Fehlermeldung an den Support weitergab, suchte danach vergeblich nach
dem Wort „Registrierung" in der Oberfläche.

Beide Aufzählungen stehen jetzt an genau einer Stelle: `CallStateCatalog` und
`AccountStateCatalog` unter `ViewModels/`. Sie sind nach `SipErrorCatalog`
benannt, der im Projekt dasselbe für Fehlermeldungen tut. Jeder Katalog
beantwortet **zwei** Fragen, wie der `CallPartyResolver` es seit ADR-043
vormacht: `Of` gibt das Wort und antwortet immer, `Caption` beziehungsweise
`Line` gibt die Fassung für eine bestimmte Stelle und darf leer bleiben.

Festgelegt: **„Anmeldung"** statt „Registrierung" — das ist das Wort, das ein
Benutzer benutzt, und es stand bereits an der prominentesten Stelle.
**„Gegenseite"** statt „Gegenstelle" — die sichtbaren XAML-Texte sagen längst
*„die Gegenseite muss davon wissen"*. „Registrierung" und `REGISTER` bleiben im
Protokoll, wo man sie mit dem SIP-Trace daneben sucht.

### Die Kontaktliste ist ohne Maus bedienbar

Die Kontextmenüs hängen am Inhalt der Zeilenvorlage und nicht am
`ListViewItem` — anders geht es nicht, weil nur dort der Eintrag als
`DataContext` steht. Bei Tastaturbedienung liegt der Fokus aber auf dem
`ListViewItem`, und dort findet sich kein Flyout: **Menütaste und
Umschalt+F10 griffen ins Leere.** Damit waren „Anrufen", „In Gruppe
verschieben" und „Zurückrufen" ohne Maus unerreichbar — während der Kommentar
an der Gruppenverschiebung seit ADR-042 ausdrücklich das Gegenteil behauptete
(*„kein Rückfall, sondern der zweite gleichwertige Weg"*).

Die vier Listen bekommen deshalb einen `ContextRequested`-Behandler, der bei
**Tastaturaufruf** das Flyout der markierten Zeile sucht und zeigt.
`TryGetPosition` unterscheidet das: bei Maus und Finger liefert es einen Punkt,
bei der Tastatur nicht. Verdrahtet wird im Konstruktor und nicht im XAML — der
Behandler braucht keinen Instanzzustand, und der XAML-Generator verdrahtet
ausschliesslich Instanzmitglieder.

Dazu: **die Eingabetaste ruft an**, in beiden Kontaktlisten und in der
Trefferliste, und ruft in der Anrufliste zurück. Dieselbe Handlung wie der
Doppelklick, den es längst gibt.

### Ein Ton färbt Schrift, nicht Fläche

Zwei Stellen setzten eine Statusfarbe als **Hintergrund** und liessen den Text
seine Farbe vom Thema erben: der Verschlüsselungs-Chip in der
Gesprächsansicht und jedes Abzeichen auf einer Karte. Im dunklen
Erscheinungsbild stand damit fast weisser Text auf `#FCE100` — rund **1,4:1**,
praktisch unlesbar. Betroffen war ausgerechnet *„unverschlüsselt"*, die Angabe,
die §8.2 als Pflichtanzeige führt.

Eine Tonfläche verlangt einen mitgesetzten Vordergrund, und der müsste je
Thema ein anderer sein. Als **Vordergrund** sind dieselben Pinsel dagegen in
beiden Themen geprüft — die Kontaktliste zeigt ihre Präsenz seit jeher so.
Der Ton steht deshalb als Rand und Schrift da, die Fläche bleibt neutral. Die
Vorlage stand zwei Dutzend Zeilen darüber: die Auflegen-Schaltfläche setzt
ihren Vordergrund ausdrücklich, samt den Zuständen für Überfahren und
Drücken.

### Enter übernimmt, statt das Gespräch abzugeben

Enter im Weiterleitungsfeld führte unmittelbar die **blinde** Weiterleitung
aus. Der Tooltip derselben Schaltfläche sagt: *„sofort abgeben, ohne
Rückfrage. Das Gespräch ist danach weg."* Damit lag die einzige
unwiderrufliche Handlung des Programms auf der Taste, die man beim Tippen einer
Nummer zuletzt drückt.

Das Muster dagegen gibt es schon: die Vorschlagsliste unter dem Nummernfeld
übernimmt bei Enter und wählt nicht, mit genau dieser Begründung. Enter
übernimmt jetzt den obersten Vorschlag und setzt den Fokus auf „Sofort
abgeben"; das zweite Enter führt aus — auf einer Schaltfläche, die sichtbar
den Fokus trägt.

### Der Fokus liegt auf der Handlung, um die es geht

Drei Ansichten setzten beim Öffnen gar keinen Fokus. Bei einem eingehenden
Anruf erschien „Annehmen" über die volle Breite, erreichbar mit der Tastatur
aber erst nach mehreren Tabulatorschritten — **die zeitkritischste Handlung des
Programms war die am schlechtesten erreichbare.** Jetzt: klingelt es, Fokus auf
„Annehmen", sonst auf „Auflegen"; die Einstellungen auf die erste Gruppe, der
Designer auf die Feldpalette.

Umgekehrt zog die Wähltastatur den Fokus nach **jedem** Tastendruck zurück ins
Nummernfeld. Wer sich mit Tabulator auf die „5" gestellt hatte, konnte nicht zur
„6" weitergehen. Der Rücksprung findet nur noch statt, wenn der Fokus nicht
ohnehin auf der Tastatur liegt.

### Kleineres im selben Zug

- **`DefaultButton` in jedem destruktiven Dialog.** Vier von neun hatten keinen,
  und dort tat Enter nichts, während die anderen fünf reagierten. Überall
  „Abbrechen", ausser beim konstruktiven „Quelle hinzufügen".
- **Der Abschnitt heisst „Nebenstellen".** Seit ADR-041 gliedert er sich in
  Gruppen, und eine davon heisst „Team" — dasselbe Wort stand zweimal
  übereinander, und die Zahlen widersprachen sich scheinbar („Team (10)" über
  „Team (3)").
- **Die Wahlwiederholung trägt das Verlaufssymbol.** Der Chevron `E70D` stand
  40 Pixel unter einer echten ComboBox mit genau demselben Zeichen; das
  Nummernfeld sah dadurch aus, als wäre es selbst eine Auswahlliste. `E81C` ist
  ausserdem das Symbol der Anrufliste, aus der die Nummern stammen.
- **Der Tooltip der Anrufliste heisst „Anrufe"** wie der sichtbare Text. Drei
  Zeichenfolgen an einem Knopf verletzen WCAG 2.5.3 und lassen die
  Sprachsteuerung ins Leere greifen — der Kommentar daneben beruft sich seit
  jeher auf genau diese Regel.
- **Umlaute und Plural in Benutzertexten.** *„liess sich nicht oeffnen"*,
  *„ausfuehren"* und *„1 neue Nachrichten auf der Mailbox."* Die
  Ersatzschreibungen stammen aus Kommentaren, wo sie Absicht sind; im
  Benutzertext sind sie eine unbemerkte Abweichung von der Projektregel
  „Benutzertexte auf Hochdeutsch".

**Konsequenz.** `CallStateCatalog` und `AccountStateCatalog` sind ab jetzt die
einzigen Stellen, an denen ein Zustand ein Wort bekommt; `StateCatalogTests`
hält beides fest. Wer eine Zeilenvorlage einer Liste umbaut, prüft den
Tastaturweg zum Kontextmenü nach — er sucht das Flyout im Baum und ist deshalb
vom Aufbau der Vorlage unabhängig, aber nicht davon, dass es eines gibt. Und
wer einen Statuston als Fläche setzt, setzt den Vordergrund mit.

**Am Gerät abzunehmen: T185 bis T193.**

---

## ADR-040 — Der Quelltext wird offengelegt, die eigenen Anbieter kommen als Vorlage zurück

**Datum:** 11.09.2026 · **Status:** **angenommen** · **Löst ADR-033 ab** · **Bezug:** §21, §21.6, `docs/licensing.md`, `docs/plans/RELEASE-PLAN.md` R10

**Kontext.** nipp soll ausser Haus abgegeben werden können. Das geht nicht ohne
Antwort auf die Lizenzfrage: das linphone-sdk ist **AGPLv3 oder kommerziell**,
und ein Closed-Source-Client, der weitergegeben wird, ist mit der AGPLv3
unvereinbar. Die Frage stand seit dem 04.09.2026 auf „verschoben — nipp bleibt
intern", und genau das sperrte jede Abgabe.

Dagegen stand ein handfester Grund: im Quelltext, in der Dokumentation und in
den Tests standen **zwei bv2-eigene Systeme** mit Namen, Adressen und
Beispielantworten. Sie waren nie Code — die Plattform ist datengetrieben —,
aber sie waren überall: zwei JSON-Vorlagen, zwei Manifest-Einträge, zwei
csproj-Zeilen, zwei Beispielantworten und viel Prosa in Kommentaren.

**Entscheidung 1: Der Quelltext wird unter AGPLv3 offengelegt.** `LICENSE` und
`NOTICE` liegen im Repo, `docs/licensing.md` ist umgeschrieben statt ergänzt.

> **Umgeschrieben und nicht angehängt:** eine Statuszeile „nipp bleibt intern",
> während ausgeliefert wird, ist schlimmer als keine — sie beantwortet die
> Frage, die jemand stellt, falsch.

**Entscheidung 2: Eine Anbietervorlage ist genau eine Datei, und sie wird
importiert.** Format `kind: "nippConnectorTemplate"`, Endung `.json`,
unterschieden wird am **Inhalt** — Kennung, Beschriftung, Geheimnisse mit
Herkunftshinweis, genau eine Quellenbeschreibung, eine erfundene
Beispielantwort. Die beiden bv2-Anbieter kommen auf diesem Weg zurück und
liegen in einem privaten Vorlagen-Repo.

> Vorher lag eine Vorlage in **drei** Teilen: Manifest-Eintrag,
> Quellenbeschreibung unter `docs/`, Beispielantwort daneben. Das war richtig,
> solange nur nipp selbst Vorlagen mitbrachte — weitergeben lässt sich so
> etwas nicht. `sourceId` entfällt: eine Vorlage ist ein Anbieter ist eine
> Quelle.
>
> **Die Herkunft setzt der Lader, nicht die Datei.** `ConnectorVendor` (mit den
> Werten `Generic` und `Bv2`) wird zu `ConnectorOrigin { BuiltIn, Imported }`,
> und der Herstellername ist ein eigenes Freitextfeld, das nur angezeigt wird.
> Sonst könnte eine eingelesene Datei behaupten, mitgeliefert zu sein.

**Entscheidung 3: Die beiden Zusagen des Katalogs werden im Leser produktiv,
nicht nur im Test.**

- *„Eine Vorlage wird nie eingeschaltet ausgeliefert"* — der Leser normalisiert
  auf `Enabled = false`. Damit gilt der bestehende Test automatisch auch für
  fremde Dateien.
- *„In keiner Vorlage steht ein Geheimnis"* — bei eigenen Dateien war das ein
  Test, bei fremden ist es ein **Ablehnungsgrund**: der rohe `auth`-Knoten wird
  auf Eigenschaften abgeklopft, die nach einem Geheimnis aussehen und einen
  Wert tragen. **System.Text.Json schluckt ein unbekanntes `token` sonst
  still** (`IntegrationJson.Options` kennt kein `UnmappedMemberHandling`), und
  dann läge ein Zugangsschlüssel in einer Datei, die herumgereicht wird. Dazu:
  256 kB Obergrenze, `secretRef` unter 40 Zeichen und ohne Leerzeichen,
  unbekannte `schemaVersion` wird abgelehnt statt heimlich versucht.

**Entscheidung 4: Der Katalog wird ein Dienst.** `ConnectorCatalog` war statisch
mit `Lazy` — vertretbar, solange er **unveränderlich** war. Mit dem Import ist
er es nicht mehr, und §15 verlangt DI statt statischer Singletons. Neu:
`ConnectorLibrary` (Ablageort im Konstruktor, wie `SettingsService`,
`SecretStore` und `IntegrationConfigStore` — `TestIsolationTests` erzwingt das)
und `ConnectorTemplateReader` (statisch und rein, ohne Dateisystem prüfbar).

> **Ein Aufrufer, den man leicht übersieht:** `TestSampleStore` griff statisch
> auf den Katalog zu — daran hängt die Vorschau im Karten-Designer, nicht nur
> die Einstellungsseite.
>
> **Kein `FileSystemWatcher`:** Änderungen entstehen nur über Import und
> Entfernen, und die lösen ihr Ereignis selbst aus. Ein Watcher auf einem
> Roaming-Profil wäre eine Fehlerquelle ohne Gegenwert.

**Entscheidung 5: Der Import legt keine Quelle an.** Die Vorlage bleibt liegen;
eingerichtet wird danach über „Quelle hinzufügen" — der bekannte Weg.

> Zwei Gründe: `SecretsFor` sucht Beschriftung und Herkunftshinweis eines
> Geheimnisses über **alle** Vorlagen — verschwände sie nach dem Anlegen,
> stünde im Zugangsdatenformular wieder das generische „API-Token", also genau
> der Rückschritt, den ADR-033 behoben hat. Und dieselbe Vorlage muss für zwei
> Mandanten zweimal anwendbar sein (dafür gibt es `FreeSourceId`).

**Was das kostet, und es steht hier, weil es sonst niemand merkt.**
`ExpectedResponseTests` prüfte dreierlei; nur zweierlei liess sich ersetzen:

| geprüft | Schicksal |
|---|---|
| Die Vorlage passt zur vereinbarten Antwortform | Aussage über zwei fremde APIs — zieht ins private Vorlagen-Repo um |
| **Die Mapping-Maschine beherrscht diese Formen** (verschachtelte Arrays, Filter auf ein Kennzeichen, `join()`, Bereiche, Datumsformate, `itemsPath`, Kürzung mit `…`) | **bleibt** — `SyntheticTemplateMappingTests` gegen die synthetischen Vorlagen |
| **Regressionswächter gegen die echten Antworten** | **verloren, ersatzlos.** Weicht ein Endpunkt künftig ab, merkt es der Testabruf in den Einstellungen — also ein Mensch, nicht die Pipeline. **Das ist der eigentliche Preis dieser Entscheidung** |

**Die synthetischen Testvorlagen** übernehmen die Rolle, die die beiden echten
Systeme in den Tests spielten: „Musterkontor" (`api.musterkontor.example`, beide
Fähigkeiten, `auth.scheme: "Token"` — damit bleibt der Fall „die Kopfzeile lügt"
geprüft, ohne einen Systemnamen zu nennen) und „Gesprächsjournal" (nur
Anruferkontext). `.example` ist nach RFC 2606 reserviert: ein versehentlicher
Testabruf landet nirgends. Sie liegen unter
`tests/Nipp.Core.Tests/TestData/Connectors/` als `Content` und werden
**nicht** in `Nipp.Core` eingebettet — eine Testvorlage im Produkt wäre genau
die Lehre dieses Arbeitspakets.

**Der Wächter.** `PublicRepositoryTests` scannt `src`, `tests`, `docs`, `tools`,
`build` und die bleibenden Wurzel-Markdowns auf die beiden Systemnamen, auf den
internen Hostnamen der Anlage und auf Dateien, die behaupten, eine echte
Antwort wörtlich zu enthalten.

> **Ohne ihn kommt der nächste Kommentar „bei dem einen System war das so"
> innerhalb eines Monats zurück** — und er kommt aus guter Absicht: es ist die
> Erfahrung, an der etwas gelernt wurde. Der Erfahrungswert gehört erhalten,
> der Systemname nicht. Damit der Wächter keine Ausnahmeliste für sich selbst
> braucht, nennt auch dieses ADR die beiden Systeme nicht beim Namen, und der
> Test setzt seine Muster zusammen, statt sie auszuschreiben.

**Und ein Befund, der mit den Systemnamen nichts zu tun hatte:** im Repo standen
**echte Kundendaten**. `ExpectedResponseTests` trug eine ausdrücklich als „echte
Antwort" deklarierte Gesprächszusammenfassung mit Klarnamen, Firma und
Rufnummer; weitere Klarnamen standen in `docs/test-matrix.md`, in
`ToastComposerTests` und in `ToastCardTests`. Alles durch Musternamen ersetzt.
**Das wäre auch ohne diese Entscheidung fällig gewesen.**

**Konsequenz.**

- Mitgeliefert wird nur noch **eine** Vorlage: „Eigene REST-API", jetzt unter
  `Services/Integrations/Catalog/templates/`. Die csproj-Einträge, die
  Quellenbeschreibungen aus `docs/integrations/` einbetteten, entfallen — und
  mit ihnen die unübliche Lage von Produktressourcen unter `docs/`.
- Importierte Vorlagen liegen unter `%APPDATA%\nipp\connectors`, neben
  `integrations.json`.
- Die Bedienung: **„API-Anbieter importieren …"** unter „Quelle hinzufügen",
  bewusst **nicht** in der Zeile „Ausgeben"/„Einlesen" — dort geht es um die
  ganze Konfiguration, und Einlesen ersetzt alles. Zwei verschiedene Importe
  nebeneinander wären die Verwechslung, die man sich einbaut. Der
  wahrscheinlichste Fehlgriff — jemand wählt seine `integrations.json` — bekommt
  eine eigene Meldung.
- **ADR-033 ist abgelöst.** Seine Entscheidung war der Katalog; der bleibt. Was
  entfällt, ist die Kennzeichnung „intern bv2": sie meinte zwei Dinge zugleich
  — von uns, und nicht für Kunden gedacht —, und beide sind mit dem Import
  gegenstandslos.
- **Offen und dem Menschen vorbehalten** (`docs/plans/RELEASE-PLAN.md` R10): der Scan über
  **alle** Commits, der Repo-Wechsel selbst, der SDK-Quelltext als Spiegel und
  ein `README.md` für Fremde. **Der Repo-Wechsel ist der Schritt, ohne den alle
  vorherigen wirkungslos sind** — und er hat einen Fallstrick: die Update-Quelle
  ist eine Konstante im Code (`VelopackUpdateGateway.RepositoryUrl`, dazu
  `build/Release-Nipp.ps1`). Ein Repo unter neuem Namen heisst, dass alle
  installierten Arbeitsplätze weiter im alten suchen. Empfehlung: das private
  umbenennen, das öffentliche unter dem gleichen Namen anlegen.
- Am Gerät abzunehmen: **T169 bis T174**.

---

## ADR-043 — Der Name des Gesprächspartners kommt aus einer Stelle, und eine Nummer ist kein Titel

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Bezug:** §8.2, §21.1, §21.2, ADR-022, ADR-027, ADR-032

**Kontext.** Aus dem Alltag kam ein Satz: *„bei abgehenden Anrufen steht nur die
Nummer."* Die naheliegende Erklärung — die Auflösung könne nur eingehend — war
falsch. `ClipResolver` fragt nach einer Nummer und weiss nichts von einer
Richtung, `CallerContextService` ist richtungsneutral, und `LookupOutgoing`
steht ab Werk auf ein.

**Der Fehler steckte in einem Namen.** `CallInfo.DisplayLabel` war der einzige
richtungsneutrale „Name" im System — *„der Anzeigename aus dem Signal, sonst die
Nummer"* — und er schaute **nie auf Kontakte**. Bei eingehenden Anrufen
kaschierte das der Anzeigename der Anlage; bei ausgehenden ist der leer, und
damit blieb die blosse Nummer. Sie stand so in der Kopfzeile der
Gesprächsansicht, in der Makel-Liste, im Toast und im Infobereich.

Die Auflösung lief daneben **dreimal ausgeschrieben**: in der Gesprächsleiste,
in der Anrufliste und — über das Feld `displayName` der lokalen Quelle — auf der
Karte. Und `ClipResolver.DescribeCaller`, die als „die eine Stelle für alle"
gedachte Methode, hatte im ganzen `src/` **keinen einzigen Aufrufer**. Sie war
gebaut und nie angeschlossen.

**Entscheidung 1: Die Auflösung ist richtungsneutral, und „Anrufer" war der
Denkfehler.** Es gibt einen `CallPartyResolver`, der nach dem *Gesprächspartner*
fragt. Ob nipp gerufen hat oder gerufen wurde, ändert daran nichts.

**Entscheidung 2: Zwei Fragen, zwei Rückgaben.**

| Frage | Methode | Antwort | Wer fragt |
|---|---|---|---|
| „Wie heisst der Gesprächspartner?" | `NameOf` | Name oder **nichts** | Kopfzeile, Anrufliste |
| „Was steht in dieser Zeile?" | `Describe` | Name, sonst formatierte Nummer, **nie leer** | Gesprächsleiste, Makel-Liste, Infobereich, Toast |

Die erste darf **nie** eine Nummer liefern, die zweite **nie** leer sein. Beide
in einem Dienst, weil die zweite die erste benutzt — getrennt wären es zwei
Rückfallketten, die auseinanderlaufen.

Die Kette: **lokaler Kontakt** → **`role('name')` einer externen Quelle** →
**`RemoteDisplayName`, nur wenn er nicht die Nummer wiederholt** → nichts. Der
lokale Schritt steht vorn, weil es ihn auch ohne eingerichtete Integration gibt
(§21.2). Der letzte Schritt hat seine Bedingung, weil manche Anlagen als
Anzeigenamen die Nummer noch einmal schicken — dann stünde sie als „Name" da.

**Entscheidung 3: Der Gesprächskopf ist kein Kartenbestandteil**, obwohl die
Karte dieselbe Frage als Ausdruck beantworten könnte. Sonst hinge der Name
davon ab, was ein Administrator in seine Karte geschrieben hat (ADR-032) — und
eine kaputte Karte kostete dann nicht nur sich selbst, sondern die Antwort auf
„wer ruft da an". Die Karte bleibt, was sie ist: der Bereich **unter** dem Kopf.

**Entscheidung 4: Ein Name, den nur ein externes System kennt, geht in die
Anrufliste.** `RecordInHistory` ruft `NameOf`. Das ist bewusst und berührt
ADR-027 nicht: der Klassenkommentar von `HistoryStoresNoContextTests` erlaubt
genau das — verboten ist **Gesprächsinhalt**, nicht der Name zu einer Nummer,
die ohnehin dort steht.

**Konsequenzen.**

- **Gelöscht:** `CallInfo.DisplayLabel` und `ClipResolver.DescribeCaller`.
  Ersteres ist die Falle: es heisst wie eine Antwort auf „wie heisst der
  Anrufer", steht am zentralsten Typ und kennt keine Kontakte. Wer es
  stehenlässt, hat die Asymmetrie in sechs Monaten wieder.
  **`CallHistoryEntry.DisplayLabel` ist ein anderes Ding und bleibt.**
- **Der Zwischenspeicher liegt am `CallHandle`**, nicht an der Nummer. Ein
  Ein-Platz-Speicher — so lief es in `ShellViewModel` — verfehlt bei zwei
  Gesprächen jedes Mal, wenn die Ansicht zwischen ihnen wechselt. Gemerkt wird
  **nur der lokale Schritt**: merkte man das Endergebnis, fröre der Titel auf
  dem ein, was vor der Antwort des Fremdsystems bekannt war.
- **`PartyChanged`**, weil die Antwort einer Quelle **nach** dem letzten
  Zustandswechsel eintrifft. Ohne das Ereignis stünde in Leiste, Makel-Liste und
  Infobereich weiter die Nummer, während im Kopf längst der Name steht. Gemeldet
  wird nur bei **echter** Änderung — jede Quellenantwort veröffentlicht einen
  Schnappschuss, und ohne die Prüfung zeichnete die Oberfläche vier Mal je Anruf
  neu.
- **`Nipp.App` trägt danach null Zeilen Auflösungswissen.** Aus der Zuweisung in
  `ActiveCallPage` ist eine Zuweisung ohne Regel geworden — dort gibt es kein
  Testprojekt.
- **Die Makel-Liste bekommt eine `CallRow`.** Ein Konverter konnte es nicht:
  zustandslos, käme an den Dienst nur statisch heran und könnte auf
  `PartyChanged` nicht reagieren. Nebengewinn: `ActiveCallViewModel` tauschte
  bei jedem Zustandswechsel die `CallInfo` in der Sammlung aus — genau das
  Austauschen, das die Ansicht mit einer Auswahl über die Kennung umgeht. Eine
  Zeile mit stabiler Identität beseitigt die Ursache statt ihrer Wirkung.
- **Der Dienst liegt in `Integrations/Context/`, nicht in `Contacts/`.**
  `Contacts` ist die untere Schicht; ein Auflöser dort, der `ContextSnapshot`
  liest, drehte die Richtung um und machte die Integrationsplattform unlösbar
  (ADR-015). In `Services/Telephony` ändert sich **nichts ausser zwei
  Kommentaren und einer gelöschten Eigenschaft** — die Auflösung gehört nicht in
  den Telefoniedienst (§6, Architekturtest).
- **Welcher Feldname welche Bedeutung trägt, entscheidet weiterhin
  `FieldCatalog`.** Die Leseregel dafür stand in `ToastComposer` und heisst
  jetzt `ContextRoles.Text`; beide Aufrufer teilen sie. Eine eigene Feldliste im
  Auflöser wäre die Doppelwahrheit, die ADR-032 aufgelöst hat.
- **Der Dienst protokolliert nichts** und hat keinen Logger. Das Einzige, was
  man hier schreiben wollte, wären Name und Nummer (§21.2, ADR-022).
- Am Gerät abzunehmen: **T181 bis T184**.

---

## ADR-042 — Ziehen wechselt die Gruppe, und eine Kennung hängt nicht am Platz

**Datum:** 12.09.2026 · **Status:** **angenommen** · **Kehrt einen Punkt aus ADR-041 um** · **Bezug:** §8.4, §20.1, ADR-041

**Kontext.** Die Gruppen aus ADR-041 liefen einen Tag im Alltag. Drei Meldungen
kamen zurück, und zwei davon waren derselbe Satz: *„ich ziehe einen Kontakt in
eine andere Gruppe, und dann fliegt ein anderer raus."*

Dahinter lagen **zwei unabhängige Fehler übereinander**:

1. **Der Ziehvorgang schrieb die Gruppe nirgends.** WinUI verschiebt die Zeile
   zwischen den Gruppensammlungen; `TeamExtension.Group` blieb stehen. Die
   gespeicherte Liste war danach verschachtelt (A, B, A) — die Invariante aus
   ADR-041 verletzt —, und beim nächsten Laden sprang die Zeile zurück.
2. **Die Kennung einer Nebenstelle enthielt ihren Index.** Nach dem ersten Zug
   hatte sich die gespeicherte Liste verschoben, die Zeilen im Speicher trugen
   aber ihre alten Kennungen. Beim **zweiten** Zug schlugen deshalb *alle*
   Zuordnungen fehl, das Ergebnis war unverändert, und die Prüfung „nichts
   geändert" traf zu: **kein Speichern, kein Protokolleintrag**, während die
   Liste die neue Ordnung zeigte. **Dieser Fehler galt auch ohne Gruppen** und
   war seit dem Bau des Umsortierens da.

**Entscheidung 1: Ziehen darf die Gruppe wechseln.** ADR-041 sagte in seinen
Konsequenzen ausdrücklich das Gegenteil — *„Ziehen bleibt vorerst auf die eigene
Gruppe beschränkt. Ein Ziehvorgang, der die Gruppe halb wechselt, wäre der
schlimmste Ausgang"*. **Diese Entscheidung ist hiermit umgekehrt**, und zwar
nicht, weil die Sorge unbegründet war, sondern weil sie beantwortet ist:

> **Die Zielgruppe wird aus dem Zustand der Sammlungen gelesen, nicht aus dem
> Ereignis.** `DragItemsCompletedEventArgs` trägt nur die gezogenen Zeilen und
> das Ergebnis der Operation — keinen Zielindex, keine Gruppe. WinUI hat die
> Zeile aber bereits umgehängt, bevor das Ereignis feuert. `TeamLayout.From`
> liest daraus je Zeile ihre Gruppe.
>
> **Nachtrag vom 13.09.2026: der letzte Satz war falsch, und er war nie
> gemessen.** Siehe den Nachtrag unten.
>
> **Gruppe und Reihenfolge werden zusammen geschrieben** (`TeamOrder.ApplyLayout`),
> und das Ergebnis ist normalisiert. Damit ist die Invariante „blockweise nach
> Gruppen" **keine Regel mehr, an die sich ein Aufrufer halten muss, sondern
> eine Eigenschaft dessen, was herauskommt**.
>
> **Der Sortiermodus klappt alle Gruppen auf** und stellt den Klappzustand beim
> Verlassen wieder her. Eine zugeklappte Gruppe ist kein Ziel, das jemand
> treffen könnte — und eines, in das WinUI etwas legen kann, ohne dass man es
> sieht. Gespeichert wird dabei nichts.

**Entscheidung 2: Die Kennung zählt gleiche Kurzwahlen statt Plätze.**
`TeamContactSource.IdOf(member, gleicheKurzwahl)`, gebildet ausschliesslich in
`IdsOf` — **eine Formel, ein Ort**, wie der Kommentar an `IdOf` es seit jeher
fordert und wie es bis zum 12.09.2026 an zwei Orten stand.

> Kollisionsfrei ohne Zusatzannahme: zwei Einträge ergeben dieselbe Zeichenkette
> nur bei gleicher Kurzwahl *und* gleichem Zähler. **Keine Migration** — die
> Kennung steht nirgends auf der Platte. Ein eigenes Kennungsfeld an
> `TeamExtension` wäre die Alternative gewesen und ist verworfen: Migration der
> `settings.json`, Änderung am Provisioning-Schema und an `nippprov`, für einen
> Wert, der den Prozess nicht überlebt.
>
> Nebengewinn: der offene Detailbereich findet seinen Kontakt nach einem
> Umsortieren wieder, statt zuzuklappen.

**Entscheidung 3: Der Detailbereich klappt in der Team-Zeile auf — und nur
dort.** Das präzisiert ADR-041, Entscheidung 3 (*„der Bereich steht unter der
Liste"*): für Outlook-Kontakte und Suchtreffer bleibt er unten.

> Der Grund für die Unterscheidung ist nicht Geschmack, sondern Zahl: die
> Team-Liste hat zehn Zeilen, die Outlook-Liste über hundert. Jede davon um
> einen ausklappbaren Teil zu erweitern kostet bei jedem Erzeugen — auf dem
> Thread, der alle 20 ms `Core.Iterate()` bedient. Es bleibt **eine** ListView;
> das Element wird nur höher, und die Höhenlogik der Seite rechnet unverändert
> weiter.

**Entscheidung 4: Die Gruppensicht hängt nicht mehr allein an den Kontakten.**
`ShellViewModel.OnSettingsChanged` lässt den Team-Block neu bauen
(`ContactStore.ReloadTeam`), und der Neuaufbau vergleicht zusätzlich die Folge
der Gruppennamen.

> Eine neu angelegte, **leere** Gruppe ändert weder eine Nummer noch eine
> Kennung — die alte Prüfung meldete „gleich", und die Gruppe erschien erst nach
> einem Neustart. Auch „Kontakte neu einlesen" half nicht. **Die Prüfung
> entschied über etwas, wovon sie nichts wusste.**
>
> Die Kette wird dabei nicht verlängert: `ReloadTeam` liest die Einstellungen,
> baut eine Liste im Speicher und löst kein `Changed` aus — kein COM, kein Netz,
> kein SDK. Und sie läuft über `OnUiThread`, weil das Ereignis vom speichernden
> Thread kommt und die Sammlungen der Oberfläche gehören.

**Konsequenzen.**

- `ContactStore.ReorderTeam(ids)` wird zu `ReplaceTeam(contacts)`: nach einem
  Gruppenwechsel **muss** der Kontakt neu entstehen, weil `Contact.Group`
  unveränderlich ist. `TeamOrder.ApplyToContacts` entfällt.
- `ContactGroupRow.SetRows` führt Bestand und Anzeige nach, statt die Gruppe zu
  ersetzen. `Clear()` auf der Quelle einer `CollectionViewSource` kostet
  Bildlauf und Auswahl — beim Umsortieren also unmittelbar nach jedem
  Loslassen. Nebenbei ist damit der falsche Zähler im Gruppenkopf erledigt.
- `SameContacts` vergleicht Kennung **und** Gruppe. Sonst rutscht genau ein Fall
  durch: wer den letzten Eintrag von A an den Anfang von B zieht, ändert die
  flache Reihenfolge nicht.
- Das Nachziehen nach einem Zug läuft über `PostToUi` — **immer** über die
  Nachrichtenschlange, auch auf dem richtigen Thread: beim Eintreffen von
  `DragItemsCompleted` hält die Liste noch Verweise auf ihre Zeilen.
- **Ein zweiter, gleichwertiger Weg:** „In Gruppe verschieben" im Kontextmenü.
  Ziehen ist für eine Sprachausgabe kein Weg, und für jemanden ohne Maus auch
  nicht. Er baut dasselbe Layout und läuft durch dieselbe Schreibstelle — eine
  zweite wäre die zweite Gelegenheit, die Invariante zu verletzen.
- Ins Protokoll geht bei einem Gruppenwechsel **nur eine Zahl** (§21.2): wer in
  welcher Gruppe steht, ist eine Aussage über Personen.
- Am Gerät abzunehmen: **T175 bis T180**, besonders **T176** (zweimal
  hintereinander ziehen — der Fall, der bisher stumm verlorenging).

### Nachtrag vom 13.09.2026: die zentrale Annahme war falsch

**«WinUI hat die Zeile bereits umgehängt» stimmt nicht**, und es war nie
gemessen. Am gebauten Fenster nachgestellt: bei einer gruppierten
`CollectionViewSource` endet **jeder** Drop in diesen Listen mit `None` — auch
der innerhalb einer Gruppe. Im Kachelraster feuert nicht einmal der Zugbeginn.

Der Satz stand sieben Tage da und war genau die Begründung, die Stelle nicht
anzufassen. Was daraus folgt, steht in **ADR-065**: der Zug wird seitdem selbst
ausgewertet, und die Zielstelle kommt vom Ablegeort statt aus einem Zustand,
den niemand herstellt.

**Was hier richtig blieb:** die Kennung ohne Platzbezug, das gemeinsame
Schreiben von Gruppe und Reihenfolge, die Normalisierung am Ende, das
Kontextmenü als zweiter Weg — und dass ins Protokoll nur eine Zahl geht.

---

## ADR-041 — Gruppen als eigene Liste, eine gruppierte Liste statt N, Details an der Auswahl

**Datum:** 11.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §14.8, §20.1, ADR-014, ADR-018

**Kontext.** Aus dem Alltag kamen drei Wünsche an die Team-Kontakte: eine
**Handynummer** am Kollegen, ein **Detailbereich** beim Anklicken (Präsenz, alle
Nummern, jede einzeln wählbar) und **eigene Gruppen** neben „Team".

Der grösste Teil davon war bereits gebaut und nur nicht angeschlossen:
`ContactNumberKind.Mobile` gibt es seit P6, `ContactRow.Choices` führt alle
Nummern mit deutscher Beschriftung, `CallNumberCommand` wählt eine bestimmte,
und `ClipResolver` läuft beim eingehenden Anruf über **alle** Nummern. Die
Lücke war eine einzige Zeile: `TeamContactSource` baute genau eine Nummer.

Die Handynummer wiegt schwerer, als sie klingt. Auf dem neuen Outlook gibt es
kein COM (ADR-018), und damit war eine Handynummer eines Kollegen bisher
**nirgends** auflösbar — weder zum Wählen noch beim eingehenden Anruf.

**Entscheidung 1: Die Gruppen stehen als eigene Liste in den Einstellungen
(`Contacts.Groups`), nicht implizit an den Einträgen.** Der Standard ist nicht
der *Name* „Team", sondern **die erste Gruppe**; sie heisst ab Werk so und darf
umbenannt werden.

> Abgeleitet aus den Einträgen wäre eine **leere** Gruppe nicht anlegbar — man
> müsste „Support" erst befüllen, um ihn zu haben —, das Umbenennen liesse eine
> gerade leere Gruppe verschwinden, und die Gruppenreihenfolge hinge an der
> Reihenfolge ihres ersten Mitglieds: wer im Sortiermodus einen Eintrag nach
> oben zieht, verschöbe die ganze Gruppe.

**Die Invariante, an der das Sortieren hängt:** `Contacts.Team` ist immer
blockweise nach `Contacts.Groups` sortiert. Hergestellt an drei Stellen über
`TeamGroups.Normalize` — beim Speichern der Einstellungen, beim Anwenden eines
Profils und einmal nach dem Deserialisieren in `SettingsService.Load` (dort
**ohne zu schreiben**; die normalisierte Fassung landet beim nächsten regulären
`Save`).

> Ohne sie liefen Anzeige und Speicher auseinander: die Liste zeigt nach
> Gruppen, die gespeicherte Reihenfolge wäre verschachtelt (A, B, A) — und
> `ApplyTeamOrder` nimmt die Kennungen **in der Reihenfolge der angezeigten
> Zeilen**. **Ein einziger Ziehvorgang schriebe dann eine Reihenfolge zurück,
> die niemand hergestellt hat.** `TeamGroupsTests` nagelt das fest: die
> Anzeigereihenfolge durch `TeamOrder.Apply` gejagt ergibt eine Liste, die
> `Normalize` nicht mehr verändert.

**Entscheidung 2: Eine gruppierte ListView, nicht N Expander mit N Listen.**
Gruppiert wird über eine `CollectionViewSource` (`IsSourceGrouped`,
`ItemsPath="Rows"`) mit `GroupStyle.HeaderTemplate` — dasselbe Muster wie die
Palette im Karten-Designer.

> N Listen wären **exakt der Fehler, den CLAUDE.md festhält**: zwei ListViews in
> einem ScrollViewer bekommen unendliche Höhe angeboten und erzeugen jede ihrer
> Zeilen. Bei vier Gruppen und über hundert Outlook-Kontakten wäre das ein
> Ruckeln auf dem Thread, der alle 20 ms `Core.Iterate()` bedient. Der
> Nebengewinn zählt auch: die Höhenlogik der Seite bleibt **wörtlich
> unverändert**, weil es weiterhin genau eine Team-Liste gibt.
>
> Eine zugeklappte Gruppe leert ihre angezeigten Zeilen und erzeugt damit keine
> Container — kennt ihre Einträge aber weiter (`All`), sonst hinge ihr ganzer
> Block beim nächsten Ziehvorgang hinten an. Der Klappzustand geht über
> `SaveViewState` und nicht über `Save`: an `Changed` hängen fünf Empfänger bis
> hinunter zum Präsenz-Neuabo, und ein zugeklappter Abschnitt ist kein Grund,
> ins SDK zu greifen.

**Entscheidung 3: Der Detailbereich öffnet an der Auswahl, nicht am
Doppelklick** — und steht unter der Liste **und** unter der Trefferliste, gilt
also für beide. Er gilt auch für Outlook-Kontakte; dort ist der Gewinn sogar
grösser, weil deren zweite Nummer bisher nur über Doppelklick und Flyout
erreichbar war.

> Dasselbe Muster und dieselbe Begründung wie in der Anrufliste: der Doppelklick
> wählt und muss das weiter tun. **Die Falle dabei:** die Quersynchronisation
> zwischen Team- und Outlook-Liste setzt der anderen Liste `SelectedIndex = -1`
> und feuert damit erneut `SelectionChanged`. Der Bereich darf deshalb **nur**
> aus dem Zweig mit `AddedItems.Count > 0` geöffnet werden — sonst schliesst die
> Synchronisation ihn sofort wieder.
>
> Darin ein `ItemsControl` und **keine dritte ListView**, aus demselben Grund wie
> beim Kontextbereich der Anrufliste. Die Präsenz zieht ohne Zusatzcode nach: es
> ist dieselbe `ContactRow`-Instanz wie in der Liste. §8.4 gilt auch hier —
> Farbe **und** Text.

**Was sich ausdrücklich nicht ändert.** Das Besetztlampenfeld abonniert
`SipAddress`, nicht `Numbers`: **eine zweite Nummer erzeugt kein zweites
SUBSCRIBE**, die Last auf der Anlage ändert sich um null (§14.8). `TeamMobileTests`
prüft das. Und die Nebenstelle bleibt `Numbers[0]`, also `PrimaryNumber` — ein
Klick auf den Kollegen wählt weiterhin intern und nicht aufs Handy.

**Konsequenz.**

- `TeamExtension` bekommt `Mobile` und `Group`, beide `string?` mit Vorgabe
  `null` — **nicht** `"Team"`. `SettingsService` schreibt mit
  `WhenWritingNull`; ein deklarierter Vorgabewert wäre die Falle, weil unklar
  ist, ob System.Text.Json ihn bei einem fehlenden Member einsetzt oder
  `default(T)`. `TeamExtensionJsonTests` beantwortet das empirisch, statt es zu
  zitieren.
- `SchemaVersion` bleibt **1**: der Kommentar dort sagt „wird erhöht, wenn ein
  Feld seine Bedeutung ändert — nicht, wenn eines dazukommt".
- `SettingsService.Reset` rettet neben `Team` künftig auch `Groups`. **Das ist
  die eine Stelle, an der diese Änderung sonst unbemerkt danebengegangen
  wäre:** ein Zurücksetzen hätte die Ordnung verloren, während die Einträge
  bleiben, und alles wäre stumm in „Team" gelandet.
- Die Provisionierung liest `mobile` und `group` (`docs/provisioning.md`,
  `nippprov schema`). Ein Profil, das Nebenstellen setzt, setzt damit auch die
  Gruppen — konsequent zur bestehenden Regel für Konten.
- ~~**Ziehen bleibt vorerst auf die eigene Gruppe beschränkt.**~~
  **Abgelöst durch ADR-042 (12.09.2026).** Ziehen wechselt die Gruppe; die Sorge
  dahinter — ein Zug, der die Gruppe halb wechselt — ist dort beantwortet, nicht
  weggewischt. Das Kontextmenü „In Gruppe verschieben" gibt es zusätzlich, als
  Weg ohne Maus.
- Am Gerät abzunehmen: **T160 bis T168**, besonders **T163** (kein Ruckeln beim
  Tab-Wechsel — der Fehler ist kein Absturz) und **T168** (eine
  `settings.json` von vor dieser Änderung).

---

## ADR-039 — Updates über GitHub, zwei Kanäle, und gefragt wird vorher

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Beantwortet §16 Punkt 4** · **Bezug:** §9.6, §12 (M8), ADR-038

**Kontext.** §16.4 („Update-Mechanismus") stand seit dem 04.09.2026 als offene
Entscheidung — App-Installer-URL, eigener Check oder Intune. §9.6 verlangt in
der Einstellungstabelle eine Zeile „Version / Update prüfen", die nie gebaut
wurde. Mit dem Installer (ADR-038) wird beides gleichzeitig fällig.

**Entscheidung 1: GitHub Releases als Feed, zwei Kanäle — `win-stable` und
`win-beta`.** Sie sind getrennte Feeds; eine installierte Fassung sucht in ihrem
eigenen Kanal weiter. Der Wechsel steht in den Einstellungen.

**Entscheidung 2: der Rückweg von beta nach stable ist erlaubt.** Er führt auf
eine **niedrigere** Versionsnummer und braucht deshalb
`AllowVersionDowngrade`. Ohne das wäre beta eine Einbahnstrasse, aus der nur
eine Neuinstallation herausführt — und dann probiert es niemand aus.

**Entscheidung 3: beim Start wird gefragt, nicht geladen.** Nachgesehen wird
zuletzt, nachdem Anmeldung und Fenster stehen, auf einem eigenen Thread.
Gefundenes steht als ruhige Zeile in den Einstellungen; geladen wird auf
Knopfdruck, angewandt ebenfalls.

**Entscheidung 4: nie während eines Gesprächs.** Der Knopf ist dann abgeblendet,
mit dem Grund daneben, und `UpdateService.ApplyAndRestart` prüft **noch einmal
selbst** — zwischen dem Zeichnen eines Knopfes und seinem Druck kann ein Anruf
hereinkommen. Die Oberfläche ist der falsche Ort für diese Sicherheit.

**Entscheidung 5: kein Toast.** Der Toast ist bei nipp die Anrufmeldung. Wer ihn
für ein Update benutzt, verwässert das einzige Zeichen, das sofort
Aufmerksamkeit verdient (ADR-030).

**Das Token, solange das Repo privat ist.** Ein Release-Asset in einem privaten
Repo ist ohne Anmeldung nicht abrufbar. Das Token steht **nicht** im Quelltext:
es kommt über die Provisionierung (`update.token`) und liegt über DPAPI im
`SecretStore` — derselbe Weg wie das SIP-Passwort. Aus einem Profil **aus dem
Netz** wird es nicht übernommen: wer es aus der Ferne setzen könnte, bestimmte
damit, aus welchem Repo dieser Arbeitsplatz seine nächste Fassung bezieht
(dieselbe Überlegung wie bei ADR-012). Fehlt es, ist die Prüfung still aus.

**Konsequenz.**
- Ein gescheiterter Abruf ist ein **Zustand, keine Ausnahme**. Kein Netz ist der
  Normalfall; der nächste Start sieht wieder nach, und die Telefonie merkt
  nichts davon.
- `using Velopack` ist auf `Services/Updates/` und `Nipp.App/Program.cs`
  beschränkt, `UpdateBoundaryTests` erzwingt das. Der `UpdateService` selbst
  kennt keinen Velopack-Typ — deshalb laufen seine Tests ohne Netz und ohne
  Installation.
- Die Kanalnamen stehen an **einer** Stelle (`UpdateChannels.NameOf`) und
  gleichlautend im Release-Skript. Ein Tippfehler dort heisst: die App sucht
  einen Feed, den niemand hochlädt, und meldet wahrheitsgemäss „kein Update".
- **Für eine Kundenverteilung trägt das Token nicht.** Ein Token, das auf jedem
  Rechner liegt, ist ein Token, das jeder auslesen kann. Bis dahin ist entweder
  das Repo öffentlich (docs/plans/RELEASE-PLAN.md R10, dann fällt es ersatzlos weg) oder
  der Feed liegt auf einem bv2-Webserver.

---

## ADR-038 — Ausgeliefert wird ein Velopack-Setup, unpackaged und self-contained

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Ergänzt ADR-008, ersetzt es nicht** · **Beantwortet §16 Punkt 3** · **Bezug:** §4, §12 (M8), §16.3

**Kontext.** nipp soll weitergegeben werden können. §16.3 („Verteilung") stand
offen: MSIX über Intune, oder klassisches MSI/EXE, weil nicht jede
Kundenumgebung Intune hat. Gebaut ist MSIX (ADR-008, `Pack-Nipp.ps1`) — und es
ist heute aus zwei Gründen nicht der Weg zur Auslieferung:

1. **T110 ist offen.** `AppNotificationManager.Register()` scheitert packaged
   mit `0x80070490`; ohne Toast ist §8.6 nicht erfüllt, und weil nipp im
   Infobereich lebt, gäbe ein eingehender Anruf bei geschlossenem Fenster **kein
   Zeichen**. Der Alltag läuft deshalb ohnehin unpackaged.
2. **Ohne echtes Zertifikat lässt sich ein MSIX gar nicht installieren.** Bei
   einer Setup.exe ist ein fehlendes Zertifikat eine hässliche Warnung, bei MSIX
   ein hartes Nein. AP9.2 ist nicht beschafft.

**Entscheidung: Velopack** (MIT). Es installiert ohne Adminrechte nach
`%LocalAppData%`, nimmt GitHub Releases als Feed, führt Kanäle als getrennte
Feeds und baut Delta-Pakete.

**Entscheidung: self-contained.** .NET 8 und das Windows App SDK liegen im
Paket. Das kostet Grösse — gemessen 345 MB im Installordner, 145 MB als
Setup.exe — und spart die Fehlerklasse „startet nicht und sagt nicht warum".

**Was das am Code geändert hat, und der Befund dabei.**

- **Ein eigener Einstiegspunkt** (`Program.cs`, `DISABLE_XAML_GENERATED_MAIN`):
  `VelopackApp.Build().Run()` muss vor jeder WinUI-Initialisierung laufen, weil
  Velopack die eigene EXE mit Hook-Argumenten aufruft. Die Startlogik selbst
  bleibt die des XAML-Compilers (`XamlGeneratedProgram.XamlGeneratedMain()`) —
  nachgebaut wäre sie eine zweite Wahrheit.
- **Der Befund, und er hätte teuer werden können:** beim ersten
  `dotnet publish` lagen 229 MB Laufzeit im Ausgabeverzeichnis und **keine
  einzige Linphone-DLL**. `build/Linphone.Sdk.targets` kopierte die native Kette
  an `Build` (nach `$(OutDir)`) und für MSIX ins Paketlayout — `publish` sammelt
  aber seine eigene Dateiliste. Aufgefallen wäre es erst auf dem Zielrechner,
  mit der Meldung aus §14.2, die nicht sagt, welche Datei fehlt. Jetzt gibt es
  ein drittes Target (`AddLinphoneToPublish`), **und** `Release-Nipp.ps1` prüft
  das Ergebnis noch einmal nach: ein Target, das lautlos nichts tut, sieht wie
  ein Erfolg aus.
- **Autostart und Protokoll-Handler zeigen auf den Stub**, nicht in den
  `current`-Ordner, den ein Update ersetzt (`ResolveExecutablePath`).

### Nachtrag vom selben Abend — „Beenden“ beendete nicht

Beim ersten Debug-Lauf nach dem Umbau fiel ein Befund an, der älter ist als
dieser ADR und ihn trotzdem trifft: **ein Klick auf „Beenden“ fährt nipp
vollständig herunter — und der Prozess bleibt liegen.** Gemessen am
07.09.2026: 19:23:20 „Beenden“, 19:23:21 Ereignisschleife gestoppt, 19:23:22
Konto abgemeldet und „Core gestoppt“. Danach keine Zeile mehr, und vier
Minuten später hielt derselbe Prozess `Nipp.Core.dll` weiter offen; der
nächste Build scheiterte an MSB3027.

**Für den Alltag war das lästig. Mit dieser ADR ist es mehr:** Velopack ersetzt
beim Update den Inhalt von `current\`. Ein Prozess, der seine DLLs offen hält,
lässt genau das scheitern — und ein Update, das nach dem Neustart nicht
angewandt ist, ist von „kein Update da“ nicht zu unterscheiden.

**Zwei Dinge sind gebaut, und nur eines davon ist eine Lösung:**

- **Drei Protokollzeilen** in `ExitApplication` („Telefonie beendet“,
  „Infobereich und Benachrichtigungen freigegeben“, „Dienste freigegeben“).
  Bisher war „Core gestoppt“ die letzte Zeile und danach Schweigen — damit
  liess sich nicht sagen, ob die Aufräumkette hängt oder ob sie durchläuft und
  der Prozess danach nicht endet. **Die erste fehlende Zeile sagt es**, dieselbe
  Diagnose wie beim Start.
- **Ein Wächter**: acht Sekunden nach dem Beginn des Herunterfahrens wird hart
  beendet, mit Warnung im Protokoll. Zu dem Zeitpunkt sind Telefonie,
  Infobereich und Protokoll geordnet zurückgebaut, es geht nichts verloren.

**Das ist eine Sicherung, keine Ursachenbehebung.** Die beiden eigenen Threads
(Hotkey, Headset) sind als Hintergrundthreads markiert und scheiden aus; ob es
in der Dispose-Kette hängt oder ein fremder Vordergrundthread den Prozess
hält, sagt der nächste Lauf. **T134** hält es offen.

**Konsequenz.**
- **MSIX bleibt gebaut** und wartet auf T110 und AP9.2; für Intune-Umgebungen
  ist es dann wieder die bessere Wahl. Ausgeliefert wird trotzdem nur einer der
  beiden Wege — zwei Update-Wege sind zwei halbe.
- **Ohne Zertifikat bleibt das Setup unsigniert**, und Windows zeigt beim ersten
  Start „Unbekannter Herausgeber" (T130). Intern einmal wegzuklicken, für eine
  Abgabe ausser Haus der Blocker. AP9.2 gehört sofort angestossen.
- Die Prüfung der nativen Kette aus `Pack-Nipp.ps1` gilt jetzt für **beide**
  Paketformate und steht in beiden Skripten.

---

## ADR-037 — Abstand mit drei Grössen, Beschriftung über einen Schalter

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §21.2, §8.4, ADR-032

**Kontext.** Zwei Wünsche aus dem Alltag an den Karten-Designer: Bereiche
trennen können, und „Hans Muster" statt „Name: Hans Muster" schreiben können.
Die **Trennlinie gab es schon** (`CardDivider`, Knopf „Linie") — sie ist ein
Strich von einem Pixel und fällt zwischen zwei Zeilen kaum auf. Gefehlt hat der
Abstand.

**Entscheidung 1: `CardSpacer` mit `small` / `medium` / `large`, nicht mit
einer Pixelzahl.** §21.2 beschreibt Layout über Struktur und nicht über
Bildschirmkoordinaten; ein Feld für Pixel wäre die erste Stelle, an der eine
verteilte Konfigurationsdatei Bildschirmmasse setzt, und die zweite wäre die
Frage, was davon bei 150 % Skalierung gilt. Was „mittel" heisst, entscheidet
die Oberfläche (4 / 12 / 24 logische Pixel).

**Entscheidung 2: `CardField.ShowLabel`, nicht „Beschriftung leer lassen".**
Zwei Gründe, und der zweite ist der wichtigere:

- Die leere Zeichenkette liesse offen, ob jemand die Beschriftung **abgewählt**
  oder **vergessen** hat.
- Die Beschriftung wird auch abgeschaltet noch gebraucht: der Renderer trägt
  sie als `AutomationProperties.Name` an den Wert. Eine Sprachausgabe sagt
  weiterhin „Firma: Muster AG", auch wenn nur „Muster AG" dasteht. §8.4
  verlangt genau das — eine Aussage, die allein an der Darstellung hängt, kommt
  bei einer Sprachausgabe nicht an.

**Konsequenz.**
- Ohne Beschriftung fällt die **Spalte** weg, nicht nur ihr Text: die 110 Pixel
  Mindestbreite blieben sonst stehen, und der Wert stünde eingerückt an einer
  leeren Stelle — genau das, was „nur den Wert" vermeiden soll.
- Im Toast wird ein Abstand übergangen wie die Trennlinie: Windows nimmt dort
  nur Text. Gemeldet wird er nicht.
- Ein `bool` wird immer geschrieben; die Falle, die `emptyText: null` das
  Speichern nicht überleben liess, trifft `showLabel` nicht. Ein Rundlauftest
  hält das trotzdem fest — es ist dieselbe Sorte Feld.

---

## ADR-036 — Die Anrufliste bekommt eine Karte, der Kopf bleibt Rahmen

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §22.3, §21, ADR-027, ADR-032, ADR-034

**Kontext.** `CardKind.History` stand seit I4 im Modell, mit dem Kommentar
„noch nicht verwendet". Der Kontextbereich unter der Anrufliste baute seine
Zeilen stattdessen **selbst** und beschriftete sie mit `Humanize(feldname)` —
also maschinell. Auf der Gesprächskarte war „Letzte arbeit zeile" am 07.09. ein
Fehler; hier war es der Normalfall. Dieselbe Lücke wie bei den Karten in der
Konfiguration (EINRICHTUNG-PLAN B1): **die Fähigkeit war gebaut, nur nicht
angeschlossen.**

**Entscheidung. Der Bereich zeichnet eine Karte der Art `history`, und sie ist
im Designer zu bearbeiten wie die drei anderen. Die Kopfzeile — Name, Uhrzeit,
Ergebnis und das Schliessen-Kreuz — bleibt Rahmen und wandert nicht in die
Karte.**

Der Grund ist derselbe wie beim Toast (ADR-034): eine Angabe, die sagt,
**welcher Eintrag gerade offen ist**, darf nicht wegkonfigurierbar sein. Der
Bereich steht unter der Liste und nicht in der Zeile; nimmt man ihm den Titel,
zeigt er Werte ohne Bezug.

**Dazu der Namensraum `call`.** Eine Karte in der Anrufliste muss Zeit, Dauer
und Ergebnis ansprechen können. `call` war im Validator längst als reservierte
Kennung geführt, in `ContextSnapshot.Resolve` aber nicht vorhanden. Er steht
jetzt **neben** `number` und nicht als Quelle: ein synthetisches Fragment mit
der Kennung `call` wäre einfacher gewesen und an drei Stellen falsch — es
stünde in `SourcesByPriority`, würde bei `role()` mitgewichtet und erschiene in
der Oberfläche als Quelle, die niemand eingerichtet hat.

**Und der Bereich klappt auf.** Vorher waren es feste 220 Pixel: bei drei
Feldern stand der Kasten halb leer, bei zehn scrollte er, während darüber Platz
frei war. Jetzt bis zu 60 % der Höhe, gerechnet und nicht festgeschrieben — die
Liste behält eine Mindesthöhe, damit sich durch mehrere verpasste Anrufe
weiterklicken lässt.

**Verworfen: der Aufklappbereich in der Zeile.** Die Zeilen brauchten einen
Chevron, die Liste bräche bei jedem Öffnen um, und der Kontext stünde jedes Mal
woanders. Ebenso verworfen: den Eintrag die Liste **ersetzen** zu lassen — das
gäbe den ganzen Platz, macht aber aus dem Durchsehen mehrerer Einträge ein Hin
und Her.

**Konsequenz.**
- ADR-027 bleibt unangetastet: abgerufen wird beim Aufklappen, gespeichert wird
  nichts. `HistoryStoresNoContextTests` läuft unverändert.
- Der Kartenbereich bekommt einen **eigenen** `ScrollViewer`; die Liste behält
  ihren. Zwei Listen in einem gemeinsamen verlieren die Virtualisierung — auf
  demselben Thread, der alle 20 ms `Core.Iterate()` bedient. Am Gerät zu prüfen
  (T113): der Fehler ist kein Absturz, sondern ein Ruckeln.
- Eine kaputte Karte kostet nur sich selbst: der `CardResolver` fällt auf die
  mitgelieferte zurück und meldet einen Befund (T116).
- **Von einer anderen Karte übernehmen** gibt es jetzt im Designer. Kennung,
  Name und Art bleiben die der bearbeiteten Karte — käme die Kennung mit,
  stünden zwei Karten mit derselben in der Datei, und der Benutzer bekäme einen
  Befund für etwas, das er nicht getan hat. Gekürzt wird nichts: wer die
  Gesprächskarte in den Toast übernimmt, bekommt einen Befund, der das
  Speichern sperrt und sagt, was zu viel ist.

---

## ADR-035 — „Gesehen" steht in der Datenbank, nicht im Arbeitsspeicher

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §8.3, §20.1, §20.3, ADR-022

**Kontext.** Das Abzeichen an der Anrufliste zeigte `CountMissed()` — und das
zählte **alle** verpassten Anrufe der Aufbewahrungsfrist, also bis zu einem
Jahr. Die Zahl wurde nie kleiner, ausser jemand löschte die Liste. Sie mass
„jemals verpasst" statt „da ist noch etwas offen", und ein Abzeichen, das man
nicht wegbekommt, wird nach zwei Wochen nicht mehr gelesen.

**Entscheidung. Ein Eintrag bekommt eine Spalte `seen_at`. Angeklickt heisst
gesehen: das Abzeichen wird um eins kleiner, die Zeile verliert ihre
Fettschrift. Das Abzeichen zählt nur noch ungesehene verpasste Anrufe.**

**Warum nicht im Arbeitsspeicher.** nipp läuft im Infobereich und wird selten
beendet — aber wenn, wäre beim nächsten Start die weggeklickte Zahl wieder da.
Ein Zustand, der einen Neustart nicht überlebt, taugt für ein Abzeichen nicht.

**Damit ist es die erste echte Wanderung von `history.db`** (Fassung 2).
`StampSchemaVersion` ist im September dafür angelegt worden und hatte bis heute
nichts zu tun. Gefragt wird dabei die **Tabelle** (`PRAGMA table_info`) und
nicht die Fassungsnummer: eine frisch angelegte Datei bringt die Spalte schon
aus `CREATE TABLE` mit, steht aber noch auf `user_version 0` — ein blindes
`ALTER TABLE` wäre dort ein „duplicate column name" beim ersten Start auf einem
neuen Gerät, also ausgerechnet dort, wo nichts zu wandern war.

**Datenschutz.** `seen_at` ist ein Zeitstempel, kein Inhalt und keine Angabe
aus einem Fremdsystem. ADR-022 und ADR-027 sind nicht berührt;
`HistoryStoresNoContextTests` hat angeschlagen und ist mit dieser Begründung
erweitert worden — nicht umgangen.

**Konsequenz.**
- Nur **verpasste** Anrufe sind je „neu". Wäre jeder selbst gewählte Anruf
  fett, sagte die Schrift etwas anderes als die Zahl daneben — zwei Anzeigen
  für denselben Sachverhalt, die sich unterscheiden.
- Die Fettschrift ist nicht die einzige Aussage: der Name der Zeile für die
  Sprachausgabe sagt „ungelesen" mit (§8.4).
- „Alle als gesehen markieren" steht im Kontextmenü. Ohne diesen Weg bliebe ein
  Abzeichen mit dreissig alten Einträgen nur über das Löschen der Liste
  wegzubekommen — also über den Verlust der Liste selbst.
- Die Liste bindet auf `HistoryRow` statt auf `CallHistoryEntry`. Der Eintrag
  ist ein unveränderlicher `record`; ihn in der Sammlung auszutauschen hiesse,
  das gewählte Element mitten im laufenden `SelectionChanged` zu ersetzen — die
  Auswahl fiele weg, und der Kontextbereich schlösse sich beim Anklicken sofort
  wieder. Dasselbe Muster wie `ContactRow`.

---

## ADR-034 — Der Toast wird eine Kartenart, die mitgelieferte Fassung bleibt Code

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §8.6, §21.6, ADR-030

**Kontext.** Aus dem Auftrag von §21.6: der Toast soll über denselben Editor
zusammenstellbar sein wie die Karten. Die Umsetzung von ADR-030 traf ihre
Entscheidungen bis dahin über **fünf fest verdrahtete Feldlisten** im
`ToastComposer` — dieselbe Frage, die die Karte in ihren `coalesce`-Ketten
stellte. Die Feldnamen selbst sind mit K1 in den `FieldCatalog` gewandert;
offen blieb die **Zusammensetzung**.

**Der Plan wollte die mitgelieferte Fassung als Karte nachbauen** — „wörtlich
über `role()`". Beim Umsetzen zeigte sich, dass das nicht geht, und der Grund
ist eine Modellierungsgrenze:

- Eine Karte besteht aus **unabhängigen** Textzeilen.
- Der Toast setzt seine erste Zeile aus **drei Werten zusammen**: „Hans Muster
  · Muster AG (Kunde)", jeder Teil einzeln weglassbar, und die Firma
  unterdrückt, wenn sie dasselbe sagt wie der Name (bei einer Firma als Kontakt
  liefern beide Felder denselben Text).
- Dasselbe bei der Arbeitszeile, an die der Kollege nur angehängt wird, wenn er
  nicht schon darin steht.

Als Karte ausgedrückt wären das drei `concat(if(isEmpty(...)))`-Ungetüme:
technisch richtig, im Designer nur als „Ausdruck" bearbeitbar — und für
niemanden lesbar. Die Konfigurierbarkeit hätte damit nichts gebracht, was sie
verspricht.

**Entscheidung. Die mitgelieferte Zusammensetzung bleibt Code; eine
eingerichtete Karte der Art `toast` ersetzt sie.**

- Ohne eigene Karte gilt exakt das Verhalten von ADR-030 — dasselbe, das am
  Gerät belegt ist.
- Mit eigener Karte gelten deren erste drei sichtbare Textzeilen, in der
  Reihenfolge, in der sie darauf stehen. Die Rufnummer bleibt die
  Attributionszeile.
- Wer den Toast selbst zusammenstellt, **gibt die Feinheiten auf** — drei
  einfache Zeilen statt der zusammengesetzten. Das ist seine Wahl und in der
  Vorschau des Designers zu sehen.
- Liefert die Karte nichts, gilt wieder die mitgelieferte Fassung. Ein Toast
  mit drei leeren Zeilen wäre schlimmer als einer mit dem Namen.

**Warum das eine Abweichung vom Grundsatz aus I4 ist, und warum sie hier
richtig ist.** Sonst gilt: „die mitgelieferten Karten sind gewöhnliche
Beschreibungen, kein Sonderfall im Code" — wären sie fest verdrahtet, fiele
erst beim ersten Kunden auf, was sich nicht beschreiben lässt. Genau das ist
hier eingetreten, nur früher: es lässt sich nicht **lesbar** beschreiben. Für
die beiden anderen Kartenarten gilt der Grundsatz unverändert.

**Konsequenz.**
- **Die Abnahme war, dass `ToastComposerTests` unverändert durchläuft** — 15
  Tests, Datei nicht angefasst. ADR-030 ist damit nicht angetastet.
- **T90 bis T92 sind trotzdem neu zu fahren.** Sie standen offen, und der Weg
  ist angefasst worden; die Tests sind das Tor, nicht die Abnahme.
- Eine vierte Textzeile wird beim Einrichten als Befund gemeldet und im Toast
  weggelassen — Windows würde sie nicht abschneiden, sondern nicht zeigen.
- Der Anfangsentwurf im Designer bringt drei lesbare Zeilen mit
  (`role('name')`, `role('work')`, `role('summary')`), damit niemand vor einer
  leeren Fläche steht.

---

## ADR-033 — Der Connector-Katalog liegt eingebaut in nipp, bv2-Einträge gekennzeichnet

**Datum:** 07.09.2026 · **Status:** **abgelöst durch ADR-040** · **Bezug:** §21.3, §21.6, INTEGRATION-PLAN I12

**Kontext.** Bis zum 07.09.2026 entstand eine neue Quelle **nur** dadurch, dass
jemand eine JSON-Datei von aussen einlas — und dieses Einlesen ersetzte die
**ganze** Konfiguration. Nach `crm.json` war das Gesprächsjournal weg. Eine zweite
Quelle ging nur über Handarbeit im JSON, und genau das verhinderte, was §21
verlangt: weitere APIs anbinden.

Die drei Möglichkeiten, die zur Wahl standen:

1. **Alles eingebaut**, bv2-Einträge gekennzeichnet.
2. **Eingebaut, aber nur intern sichtbar** — bv2-Vorlagen erscheinen nur, wenn
   das Provisionierungsprofil sie freigibt.
3. **Nur „Eigene REST-API" eingebaut**, die bv2-Vorlagen je Gerät als Datei
   oder über eine Katalog-URL.

**Entscheidung. Weg 1.** Alle drei Vorlagen liegen als eingebettete Ressource
in `Nipp.Core`; `das CRM` und `das Gesprächsjournal` tragen im Katalog die
Kennzeichnung **„intern bv2"**.

**Was dabei in Kauf genommen wird:** ein Kunde sieht diese beiden Namen in
seinem Katalog. Das ist der Preis dafür, dass wir selbst nicht wieder über
Dateien einrichten — und die Kennzeichnung macht erkennbar, dass sie nicht
seine sind. Weg 2 wäre eine Sichtbarkeitsregel mehr im Datenmodell für einen
kosmetischen Gewinn; Weg 3 hätte den Anlass des Auftrags nicht behoben.

**Konsequenz.**
- **Es gibt keine zweite Kopie der Quellenbeschreibungen.** Das csproj bettet
  **dieselben** Dateien ein, die unter `docs/integrations/` liegen und den
  beiden API-Teams als Auftrag dienten. Produktressourcen in `docs/` sind
  unübliche Lage; zwei Dateien mit demselben Inhalt wären schlimmer, weil sie
  driften. Ein Test hält fest, dass jede im Katalog genannte Datei wirklich
  eingebettet ist.
- **„Eigene REST-API" steht oben** im Katalog. Ein Katalog, der mit zwei
  fremden Firmennamen beginnt, sieht aus wie ein Produkt für jemand anderen.
- Jede Vorlage nennt zu jedem Geheimnis eine **Beschriftung** und **woher es
  kommt** („das CRM → Profil → API-Token; Lesezugriff genügt"). Vorher war das
  Eingabefeld freier Text mit dem Platzhalter `crm.apiKey`: wer ein Token
  eintragen wollte, musste vorher im JSON nachlesen, dass der Verweis
  `crm` heisst.
- **Hinzufügen ersetzt nichts.** `AddOrReplaceSource` fügt genau eine Quelle
  ein und lässt die anderen unberührt — auch die globalen Einstellungen. Eine
  Vorlage bringt `callerLookup` und `contactSearch` mit; das sind Vorschläge
  für eine leere Konfiguration, keine Anweisung.
- **„Einlesen" bleibt** und ersetzt weiterhin alles — für die Verteilung an
  mehrere Arbeitsplätze. Es sagt jetzt, wie viele Quellen das kostet.
- Die Regel „eine Vorlage wird nie eingeschaltet ausgeliefert" gilt auch für
  den Katalog, und ein Test erzwingt sie dort.

---

## ADR-032 — Karten stehen in der Konfiguration, der Editor ist ein eigenes Fenster

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §21, §21.6, INTEGRATION-PLAN I9

**Kontext.** Der Auftrag aus §21.6: „Ich möchte die Werte selber in der
Info-Card anordnen und auswählen können."

Der Befund dazu war überraschend: **das ging nicht, weil es nicht angeschlossen
war.** `IntegrationConfig` hatte kein `cards`, obwohl `docs/plans/INTEGRATION-PLAN.md` D.2
die Datei genau so beschreibt und `CardLayoutEngine` jede Karte übersetzen
kann. `CallerCardViewModel` nahm immer `DefaultCards`. Eine eigene Karte war
also nicht schwer einzurichten, sondern **unmöglich**.

§21.5 nimmt einen visuellen Karten-Designer ausdrücklich aus dem **ersten**
Ausbau heraus; `docs/plans/INTEGRATION-PLAN.md` führt ihn als I9 mit dem Vermerk, dass er
keinen Umbau des Kerns verlangt. Das wird jetzt eingelöst.

**Entscheidung.**

1. **Karten stehen in `integrations.json`**, in der Form aus D.2 — nicht in
   einer neuen. Leer heisst: es gelten die mitgelieferten. Je Art gilt eine
   Karte; eine zweite ist ein Befund, keine stille Auswahl.
2. **Eine kaputte eigene Karte fällt auf die mitgelieferte zurück und meldet
   das** mit Pfad und Abhilfe. I4 verlangt das wörtlich. Und der Rückfall wird
   **benannt** — sonst sieht der Benutzer die mitgelieferte Karte und hält
   seine eigene für gespeichert.
3. **Der Editor ist ein eigenes Fenster.** 400 Pixel (§20.1) tragen keine
   Felderpalette. Die Karte selbst bleibt trotzdem 400 Pixel breit; die
   Vorschau im Designer ist deshalb genau so breit — eine Vorschau in anderer
   Breite wäre keine.
4. **Aller Zustand des Editors liegt in `Nipp.Core`.** `Nipp.App` hat kein
   Testprojekt, und ein Editor hat mehr Zustand als alles andere in nipp:
   Auswahl, Einfügen, Verschieben, Rückgängig, Prüfung, Vorschau. Das Fenster
   zeichnet und ruft.
5. **Rückgängig über Schnappschüsse**, nicht über umgekehrte Befehle. Eine
   Karte hat zwanzig Bausteine; ein Schnappschuss der unveränderlichen
   `CardDefinition` kostet nichts — und man kann ihn nicht falsch zurücklegen,
   während jede von Hand geschriebene Gegenoperation eine Gelegenheit ist,
   falsch zu sein.
6. **Ein Wert wird als Absicht bearbeitet**, nicht als Zeichenkette: Feld,
   Bedeutung (`role(...)`), erster Treffer aus (`coalesce(...)`), fester Text,
   Ausdruck. Der Rückweg nimmt eine strukturierte Form **nur** an, wenn sie
   sich **zeichengleich** wieder zum Original erzeugen lässt; sonst bleibt es
   ein Ausdruck und wird unverändert weitergegeben. Der Designer kann damit
   jede Karte öffnen, auch eine von Hand geschriebene, und keine verliert
   etwas.

**Konsequenz.**
- Zwei Befunde am Rande, die die Tests fanden und die ohne sie unsichtbar
  geblieben wären: die Enums standen in der ausgegebenen Datei
  grossgeschrieben (ein `[JsonConverter]` an einer Eigenschaft schlägt den
  Konverter aus den Optionen), und `emptyText: null` — „die Zeile
  verschwindet" — überlebte das Speichern nicht, weil die Datei `null` beim
  Schreiben auslässt und die Vorgabe `"—"` ist. Eine Karte änderte damit beim
  Weg durch die Datei ihr Verhalten.
- Das Designer-Fenster fasst am Hauptfenster nichts an. Insbesondere ruft es
  **nicht** `ThemeService.Attach` — der merkt sich **ein** Wurzelelement, und
  der Designer hätte dem Hauptfenster damit das Erscheinungsbild entzogen.
- Ein Anruf zählt mehr als der Designer: kommt einer, holt `MainWindow` die
  Gesprächsansicht wie immer nach vorn (§10, T06).
- Am Gerät nachgesehen (07.09.2026): Fenster öffnet, Palette mit 45 Einträgen
  in fünf Gruppen, Aufbau zeigt alle Bausteine mit ihren Ausdrücken, und die
  Vorschau löst sie gegen die Beispieldaten auf. **Was aussteht:** Ziehen aus
  der Palette (T101), und die Abnahme mit laufendem Gespräch (T99).

---

## ADR-031 — Eigene Klänge, und der Klingelton wird wählbar

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §9.4, §16.2, §17

**Kontext.** Aus dem Alltag: „Der Klingelton ist etwas nervend." Es war
`oldphone-mono.wav` aus dem SDK-Paket — eine alte Telefonglocke, 44,1 kHz, 1,77
Sekunden, in Schleife gespielt.

Das SDK bringt sechs sanfte Töne mit (`soft_as_snow`, `leaving_dreams` und
Verwandte). **Alle sechs sind unbrauchbar:** sie liegen als `.mkv` vor, und die
dafür nötige `bcmatroska2.dll` fehlt im win64-Prebuilt. Genau deshalb stand die
Glocke dort — sie war die einzige WAV neben `toy-mono.wav`.

**Entscheidung. Eigene Klänge, erzeugt von `tools\Build-Sounds.py`, und eine
Auswahl in den Einstellungen.**

- `nipp-ring.wav` — ein aufsteigender Dreiklang mit weichem Einsatz und
  exponentiellem Ausklang, dann **drei Sekunden Stille**. Die Stille ist der
  Punkt: das SDK spielt in Schleife, und eine durchgehend klingende Datei wird
  damit zum Dauerton.
- `nipp-ringback.wav` — der Schweizer Rufton (425 Hz, 1 s an, 4 s aus), für
  ADR-029. Der des SDK ist mit 1,5 Sekunden zu kurz, um ein Läuten
  nachzubilden.

**Warum ein Skript und nicht zwei Dateien im Repo.** Der Klang steht damit als
Zahlen mit Namen da, nicht als Wellenbild: wer ihn ändern will, ändert
Frequenz, Hüllkurve und Pause. Nur Standardbibliothek, kein numpy, kein ffmpeg.

**Warum eine Auswahl.** „Nervend" ist Geschmack. `AudioSettings.RingtonePath`
stand seit P5 im Modell, hatte aber **keine Oberfläche** und wurde von
`Compose()` nicht einmal geschrieben — änderbar war der Klingelton nur von Hand
in `settings.json`. Jetzt: mitgelieferte Klänge, eine eigene Datei, und
„Probe hören" auf dem Klingelgerät.

**Konsequenz.**
- Mitgelieferte Klänge werden **relativ** gespeichert
  (`Assets/Sounds/nipp-ring.wav`). Ein absoluter Pfad ins Ausgabeverzeichnis
  überlebt kein Update und keinen Umzug; eine eigene Datei des Benutzers bleibt
  dagegen absolut.
- Der Provisioning-Schlüssel `audio.ringtone` nimmt aus dem **Netz** nur
  relative Pfade ohne `..`. Ein Profil, das einen beliebigen Pfad setzen darf,
  kann eine UNC-Freigabe hinterlegen — und dann fragt nipp beim nächsten
  Klingeln einen fremden Server, mit den Anmeldedaten des angemeldeten
  Benutzers. Absolute Pfade darf nur die mitgelieferte Konfiguration setzen,
  dieselbe Grenze wie bei der Provisioning-Adresse (ADR-012).
- `.wav` bleibt die einzige Form. `Core.Ring` verlangt sie ausdrücklich, und
  ein Filter, der mehr anbietet, führte zu einem Klingeln, das stumm bleibt.

---

## ADR-030 — Der Toast zeigt den Anruferkontext

> **Nachtrag 07.09.2026:** Die **Umsetzung** dieser Entscheidung ist durch
> **ADR-034** ergänzt — der Toast ist jetzt über eine Karte zusammenstellbar.
> Die **Regeln** hier gelten unverändert: drei Zeilen plus Attribution, sofort
> und dann ersetzt, Entfernen beim Anrufende. Ohne eigene Karte gilt exakt das
> Verhalten, das hier steht, und `ToastComposerTests` läuft dafür unverändert
> durch. Dasselbe Muster wie ADR-009 gegen ADR-018.

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §8.6, §21.2,
§21.4, ADR-027

**Kontext.** Aus dem Alltag: beim eingehenden Anruf sollen Name, Firma,
Kontaktart, die letzte Arbeit, die Zusammenfassung des letzten Gesprächs und
der intern zuletzt Beteiligte im Toast stehen.

**Das war ausdrücklich anders entschieden.** §21.4 hält den Toast „vorerst
unverändert bei Name oder Nummer", und `docs/plans/INTEGRATION-PLAN.md` (Entscheidung 7
vom 06.09.2026) verschiebt die Anreicherung auf I13 — mit der Begründung
„bleibt bei Name/Nummer, bis T06 abgenommen ist". **T06 ist am 07.09.2026
bestanden.** Die Begründung ist damit erfüllt, die Festlegung wird ersetzt.

**Entscheidung. Der Toast wird angereichert, aber nie verzögert.**

Er erscheint sofort mit dem, was das SIP-Signal hergibt, und wird **ersetzt**,
sobald eine Quelle geantwortet hat — durch ein zweites `Show()` mit demselben
`Tag`. Auf eine Quelle zu warten, hiesse den einzigen Hinweis auf einen
klingelnden Anruf zu verzögern; §21 verlangt, dass Telefonieren von keiner
Integration abhängt.

**Was nicht ging: mehr als vier Angaben.** Windows nimmt höchstens **drei**
Textelemente plus die Attributionszeile. Die Belegung (Entscheidung von Dominic
aus drei Entwürfen):

    Hans Muster · Muster AG (Kunde)
    04.09. · Migration Telefonie · A. Beispiel
    Zuletzt: Musterwerk mit Frau Beispiel besprechen, dieser ruft zurück.
    +41 44 395 40 16

„Eingehender Anruf" fällt damit weg — es kostete eine der drei Zeilen, und die
Knöpfe darunter sagen dasselbe.

**Datenschutz — die Grenze aus ADR-027 gilt weiter.**
- Der Toast wird beim Anrufende **entfernt** (`RemoveByTagAsync`), auch bei
  einem verpassten Anruf. Sonst bliebe eine Gesprächszusammenfassung im
  Windows-Benachrichtigungscenter liegen, bis jemand sie wegklickt — und das
  überlebt jedes Zeitlimit, das der Zwischenspeicher setzt.
- **Kein Feldinhalt ins Protokoll.** `ToastLog.Shown` trug vorher den
  Anzeigenamen; jetzt steht dort „mit Kontext" oder „ohne Kontext" (§21.2).
- Die **kurze** Zusammenfassung, nicht die lange.

**Konsequenz.**
- Die Textzusammensetzung liegt als reine Funktion in `Nipp.Core`
  (`ToastComposer`), nicht im `ToastService`: **`Nipp.App` hat kein
  Testprojekt.** Beim Toast war das schon einmal teuer — die Regel „beginnt
  hier ein Anruf zu klingeln?" stand doppelt und war beide Male falsch, wodurch
  ein eingehender Anruf gar kein Zeichen gab.
- Gesucht wird **nach Feldnamen über alle Quellen**, nicht nach
  `crm.contactName`: die Plattform ist generisch, und die Quellen heissen
  beim nächsten Kunden anders.
- **Nur bei tatsächlicher Textänderung ersetzen.** Jede Quelle meldet sich
  einzeln; ohne diesen Vergleich erschiene der Toast bei einem Anruf drei- bis
  viermal neu auf dem Bildschirm, mit demselben Inhalt.
- §8.6 und §21.4 in `NIPP-BUILD.md` sind nachgezogen.

**Dabei aufgefallen:** `DefaultCards` adressierte `crm.*` und `erp.*` — Namen
aus dem Beispiel des Auftrags. Die echten Quellen heissen `crm` und
`memory`, und damit traf **keine einzige Zeile der Karte im Gespräch**; sichtbar
waren die Felder nur über die Rückfallebene mit maschinellen Beschriftungen
(„Letzte arbeit zeile"). Korrigiert, beide Konventionen stehen jetzt per
`coalesce` darin.

---

## ADR-029 — Bei Early Media ohne Audio spielt nipp den Rufton selbst

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §9.4, §14.1, T78

**Kontext.** Aus dem Alltag: „Bei abgehendem Anruf, wenn er wählt und der
andere noch nicht abgenommen hat, höre ich keinen Ton." Am Vormittag desselben
Tages war dafür schon eine Ursache behoben worden — der Tonspieler des SDK
liest eine veraltete Geräte-API (`play_sndcard`), die leer war. **T78 galt
danach als bestanden.**

**Das war eine Fehlabnahme, und das Protokoll zeigt es.** Die beiden ausgehenden
Anrufe nach der Korrektur gingen `Dialing → Connected` — interne Nummern, ohne
Rufzustand. Es gab überhaupt keinen Rufton zu hören. Bei den **externen**
Anrufen (07:47:21, 08:59:30) steht dagegen kein einziges `startRingbackTone`:
der Anruf geht `OutgoingProgress → OutgoingEarlyMedia`, weil die Anlage ein
`183 Session Progress` **mit SDP** schickt. Das SDK hält Early Media damit für
die Audioquelle und schweigt selbst — nur sendet die Anlage kein RTP. Belegt am
Jitter-Puffer, der nie konvergiert („stays unconverged for one second",
Puffergrösse 9 761 581 ms).

**Am SDK ist das nicht zu richten.** Geprüft in `include/linphone/core.h`:
`set_ringback` setzt nur die Datei, `set_remote_ringback_tone` ist der Ton **für
die Gegenseite**, und `set_ring_during_incoming_early_media` gilt nur für
eingehende Anrufe. Einen Schalter, ausgehendes Early Media abzulehnen, gibt es
nicht.

**Entscheidung. nipp spielt den Rufton selbst, wenn nichts hereinkommt.**

`RingbackWatch` entscheidet: Zustand `OutgoingEarlyMedia` länger als 800 ms und
eingehende Audio-Bandbreite unter 5 kbit/s → eigener Rufton über
`Core.CreateLocalPlayer`. Erstes echtes RTP, `Connected` oder Anrufende → Stopp.

**Begründung der Zahlen.** Die 800 ms sind die Karenz, in der der Strom der
Anlage anlaufen darf; ein Ton, der nach 200 ms abbricht, weil doch etwas kam,
klingt nach Fehler. Die 5 kbit/s trennen „nichts" von einem Sprachstrom, der in
PCMU bei 80 liegt.

**Konsequenz.**
- **Die Regel liegt in einer reinen Zustandsmaschine**, ohne SDK-Typ: über einen
  Ton, den in einem Test niemand hört, muss ohne Gerät entschieden werden
  können. Ausgeführt wird im `Pump()`, nie in einem SDK-Callback (§14.1); ein
  Stopp wird vorgemerkt und im nächsten Durchlauf ausgeführt, sonst spielte der
  Rufton in das angenommene Gespräch hinein.
- **Bekannte Grenze:** sendet die Anlage RTP mit echter Stille (PCMU-Stille,
  80 kbit/s), schweigt nipp weiter. Dieser Fall ist von einem Läuten, das man
  hören sollte, nicht zu unterscheiden.
- `Core.Ringback` wird jetzt **ausdrücklich gesetzt** (mit Log-Zeile). Bisher
  stand es nirgends in nipp; der Ton kam allein daraus, dass das SDK
  `ringback.wav` selbst findet — und im Protokoll stand dazu keine Zeile.
- `ApplyToneCards` gibt dem SDK jetzt **mehrere Kandidaten**
  (`ToneCardChooser`). Der Setter `Core.PlaybackDevice` wirft bei einem Namen,
  den er nicht kennt, und die alte API führt ein Treiberpräfix („WASAPI:
  Kopfhörer (Jabra Link 400)"), die neue nicht. Für „Default Playback" ging es
  gut, für ein namentlich gewähltes Headset war es ungeprüft. Am Gerät bestätigt:
  seit der Änderung steht dort `WASAPI: Default Playback` statt `Default
  Playback` — der Präfix war vorher nie getroffen.
- **`tools\Test-Ton.ps1`** wertet aus, welchen Weg ein Ton genommen hat — Karte,
  Rufzustand, `MSWASAPIWrite` gegen `MSVoidSink`. Genau daran ist die Abnahme
  von T78 gescheitert: „gehört / nicht gehört" ist kein Nachweis. T78 ist in
  **T78a** (intern, prüft überhaupt einen Rufzustand) und **T78b** (extern mit
  Headset — der eigentliche Fall) geteilt.

### Nachtrag vom 10.09.2026 — die Anlage hat Vorrang, und nipp entschied blind

**Der Befund.** „Beim Wählen höre ich einen Ton, aber nicht den der
Telefonanlage, sondern einen nipp-eigenen." Gemessen im Protokoll waren es
zweimal **168 und 193 Millisekunden**, in denen der eigene Rufton über dem
Anfang des Anlagentons lag:

    14:34:06.178  Dialing -> Ringing (OutgoingEarlyMedia)
    14:34:07.271  Eigener Rufton gestartet … "Early Media ohne Audio"
    14:34:07.416  erster Bandbreitenbericht des Stroms: 15.6 kbit/s
    14:34:07.444  Eigener Rufton beendet (15.6 kbit/s, Pegel -5.3 dBm0)

**Die naheliegende Erklärung war falsch, und das ist der Teil, der sich
lohnt.** Vermutet wurde die Kadenz: die Anlage läutet 1 s und schweigt 4 s,
also fällt die Karenzzeit in eine Pause. Dagegen sprechen die Zahlen — über
5,07 Sekunden Early Media kamen **252 Pakete** an (252 × 20 ms = 5,04 s), der
Strom war lückenlos. Die Kadenz steckt im **Pegel**, nicht im Paketstrom.

**Zwei echte Ursachen, beide in der Entscheidungsgrundlage.**

1. **`DownloadBandwidth` ist ein Sekundenmittel.** liblinphone berechnet es
   einmal pro Sekunde neu — an derselben Stelle, die
   `Bandwidth usage for CallSession` schreibt; davor steht dort 0. **Eine
   Entscheidung nach 800 Millisekunden kann auf einem Wert, der einmal pro
   Sekunde entsteht, nicht ruhen.** Beim Anruf um 11:32 lief der Strom der
   Anlage schon rund 200 ms, als nipp anfing.
2. **Und die Karenzzeit selbst war zu kurz.** Der Strom setzte **0,91 und
   1,04 Sekunden** nach dem Läuten ein, entschieden wurde nach 0,8. Ein
   frischer Sensor allein hätte daran nichts geändert: **wer zu früh
   entscheidet, sieht auch mit dem besten Messgerät nichts.**

**Entscheidung.** Der Ton der Anlage hat Vorrang, solange unentschieden ist, ob
sie läutet.

- **Sensor:** `CallStats.RtpPacketRecv`, pro Paket geführt. Ist er nicht zu
  haben, entscheidet wie bisher die Bandbreite (`RingbackSample.ReceivedPackets`
  ist `null`-fähig, mit Test).
- **Zwei Karenzzeiten:** `Grace` = **1,4 s**, wenn gar kein Strom ankommt (der
  Fall aus dieser ADR); `SilentStreamGrace` = **5 s**, wenn ein Strom ankommt,
  aber ohne messbaren Pegel — länger als die längste Pause einer üblichen
  Kadenz (4 s) plus ein Durchlauf und der Nachlauf des Jitter-Puffers.
- **Die Karenzzeit läuft ab dem SIP-Ereignis** (`TrackedCall.RingingSince`),
  nicht ab dem ersten Durchlauf, der den Anruf sieht: dazwischen lagen 260 ms,
  weil der Aufbau des Audiostroms die Ereignisschleife aufhielt.
- **Ein Strom, der abreisst, behält die lange Karenzzeit.** Sonst setzte nipp in
  einer Kadenzpause ein, also genau dort, wo der Ton gleich weitergeht.

**Was von der Korrektur vom 08.09.2026 gilt — und was daran ungenau war.** Die
Pegelprüfung bleibt und ist richtig: ankommen ist nicht hörbar sein. Ihre
**Begründung** trug aber nur bei T78b (17:54, 2,5 Sekunden ohne ein einziges
Paket). Der zweite dort genannte Fall — die 402 ms um 15:47 — hat **keinen
Pegelwert** im Protokoll, und es kamen 50 Pakete an. Aus einer Begründung, die
zwei Fälle in einen zog, wurde eine Konstante, die für den einen zu kurz war.

**Folgen, durchgerechnet.**

| Fall | Vorher | Jetzt |
|---|---|---|
| Anlage schickt Early Media mit Ton (der Befund) | eigener Ton 168 / 193 ms darüber | **nipp schweigt** |
| T78b (kein RTP über 2,5 s) | Ton ab 0,8 s, 2,28 s lang | Ton ab 1,4 s ab dem SIP-Ereignis, also **560 ms früher als gemessen**, ~1,9 s lang |
| RTP mit echter Stille über die ganze Läutdauer | Ton ab 0,8 s | Ton ab 5 s |

**Bewusst nicht genommen:** „bei jedem Strom schweigen", die Regel vor dem
08.09.2026 mit frischem Sensor. Sie wäre in **jedem je gemessenen Fall** richtig
gewesen — alle neun Episoden des eigenen Ruftons in der Protokollhistorie endeten
mit echtem Audio der Anlage. Sie gibt aber die Fähigkeit dieser ADR auf der
Grundlage von „nie beobachtet" auf, und der Fall aus der bekannten Grenze oben
ist genau der, der dann still bliebe.

**Was das Protokoll jetzt sagt.** Die Startzeile (2097) nennt **den gemessenen
Grund**, die Paketzahl und die Wartezeit — bis hierher behauptete sie „die
Gegenstelle schickt Early Media ohne Audio" und lag zweimal falsch; *eine
Protokollzeile, die eine Ursache behauptet, die sie nicht gemessen hat, ist
schlechter als keine*. Dazu neu: **2103** (wann der Strom der Gegenstelle
einsetzte), **2104** (der 200-ms-Takt mit Pegel, Paketen und Puffergrösse — erst
damit wäre eine Kadenz im Protokoll sichtbar) und **2105** (welcher Weg spielt:
das SDK bei 180 ohne SDP, oder Early Media). Die Stoppzeile (2098) nennt die
Spielzeit: 168 ms gegen 2 282 ms ist der ganze Unterschied zwischen Fehler und
Absicht.

**Weg A bleibt unverändert.** Bei `OutgoingRinging` (180 ohne SDP) sendet die
Anlage kein RTP — dort gibt es keinen Anlagenton, den man vorziehen könnte, und
alles Hörbare ist zwangsläufig lokal. §9.4 gibt `nipp-ringback.wav` für
`Core.Ringback` vor, und das bleibt so. **Keine Einstellung „von der Anlage /
eigener Ton":** ob die Anlage Early Media mit Audio schickt, wechselt von Anruf
zu Anruf; das kann niemand vorher entscheiden. ADR-031 taugt nicht als Vorbild
— dort steht eine Vorliebe zur Wahl, hier eine Tatsache.

**Offener Punkt, eigene Spur.** Im selben Anruf konvergierte der Jitter-Puffer
des Early-Media-Stroms eine Sekunde lang nicht (`9 942 363 ms`, Reset, 9 von
252 Paketen zu spät): **der Anfang des Anlagentons kommt beschädigt an.** Das
ist der zweite Kandidat dafür, dass ein Ton „nicht nach unserer Anlage" klingt,
und hat mit dieser Regel nichts zu tun. Erst messen (**T146**), dann
entscheiden.

---

## ADR-028 — Headset-Tasten über Win32-HID, nicht über WinRT

**Datum:** 07.09.2026 · **Status:** **angenommen** · **Bezug:** §9.4, §22.5, §6

**Kontext.** Nach einem Anruf im Alltag: „ich kann mit dem Headset nicht aufhängen". Das Gespräch selbst war einwandfrei, achtzehn Minuten ohne einen Fehler im Protokoll. Die Taste am Jabra Evolve 65 tat nichts.

Die Ursache stand in der Spezifikation, im Änderungsprotokoll des SDK (§5): **Linphone 5.5.0 hat „HIDAPI/Jabra" entfernt.** Bis 5.4 hätte nipp die Taste gratis bekommen; mit 5.5.18 gibt es dafür im SDK nichts mehr.

**Drei Wege, zwei davon am Gerät widerlegt.**

1. *`Windows.Media.Devices.CallControl`.* Die WinRT-Klasse, die genau dafür vorgesehen ist — mit `AnswerRequested`, `HangUpRequested` und `IndicateActiveCall`. Sie existiert in den Metadaten, ist aber auf dem Desktop **nicht implementiert**: `CallControl.FromId` **und** `CallControl.GetDefault` scheitern beide mit `0x80040111`, `CLASS_E_CLASSNOTAVAILABLE`. Belegt im Protokoll vom 07.09.2026, 09:55. Der erste Anlauf war vollständig auf dieser Klasse gebaut; verworfen wurde sie erst, als beide Wege dasselbe HRESULT lieferten.

2. *`Windows.Devices.HumanInterfaceDevice`.* Der WinRT-Weg zu HID-Geräten. Er sperrt genau die Usage Page aus, um die es geht: **Telefonie (0x0B) gehört zu den für Anwendungen reservierten Seiten.** Nicht am Gerät geprüft, weil die Sperre dokumentiert ist — aber deshalb auch nicht als Möglichkeit offengelassen.

3. *Win32-HID über SetupAPI und `hid.dll`.* Der Weg, den auch das entfernte SDK-Modul ging. Gewählt.

**Entscheidung.** Win32-HID, in `src/Nipp.Core/Services/Windows/Hid/`. Das Gerät wird über SetupAPI gefunden, an seiner Usage Page als Telefoniegerät erkannt und lesend wie schreibend geteilt geöffnet.

**Nichts davon deutet Bits von Hand.** `HidP_GetUsages` und `HidP_SetUsages` arbeiten mit dem vorverarbeiteten Report-Deskriptor des Geräts; auch die Report-Kennung wird aus den Button-Caps abgelesen. Ein selbst gerechneter Bit-Versatz wäre für genau ein Modell richtig gewesen. Am Testgerät steht die Kennung auf **2**, nicht auf 0 — ein naheliegender fester Wert hätte funktioniert wie ein Fehler, der nie auffällt: der Report wäre leer hinausgegangen und die Lampe nie angegangen.

**Was dabei belegt statt vermutet wurde.** Das Jabra Link 400 meldet Usage Page 0x0B, Usage 0x05 (Headset), Ein- und Ausgangsreport je drei Bytes. Auf der Eingangsseite unter anderem Hook Switch (0x20) und Phone Mute (0x2F), auf der Ausgangsseite Off-Hook (0x17), Ring (0x18) und Mute (0x09) der LED-Seite. Ausgelesen mit einem Wegwerfskript, bevor eine Zeile Produktivcode entstand.

**Zwei Konsequenzen, die keine Kosmetik sind.**

- **Die Lampen sind Teil des Protokolls.** Ein Headset führt seinen eigenen Gabelzustand. Wer ihm nicht sagt, dass ein Gespräch läuft, bekommt beim nächsten Druck den falschen Wert — das Gerät glaubt weiter, es sei aufgelegt. Wird ein Gespräch in der Oberfläche beendet, muss der Zustand deshalb nachgezogen werden, sonst ist ab da **jeder zweite Tastendruck falsch**.
- **Nichts davon auf dem UI-Thread.** `HidD_SetOutputReport` blockiert, bis das Gerät bestätigt. Beim ersten Versuch stand dieser Aufruf im Startpfad — **und nipp startete nicht mehr**: kein Fenster, keine Anmeldung, das Protokoll endete mitten im Start, und die letzte Zeile darin war eine Erfolgsmeldung. Suchen und Schreiben laufen jetzt auf eigenen Threads; nur die Deutung eines Tastendrucks wird auf den Thread gepostet, der `Core.Iterate()` bedient (§6).

**Prüfbar gemacht.** Die zwei Regeln — was ein Tastendruck bedeutet, und was dem Gerät zu melden ist — stehen in `HeadsetPolicy` als reine Funktionen mit 19 Tests. Dieselbe Art Regel hat in nipp schon dreimal drei Anläufe gebraucht; in einer Klasse mit Threads, Handles und P/Invoke wäre sie nur am Gerät prüfbar, und dort fällt ein Fehler erst auf, wenn er ein Kundengespräch beendet.

**Was bewusst offenbleibt.** Die Audiowege bleiben beim SDK: ein Headset, das über HID ein Gerät umschalten will, wird nicht bedient (§22.5). Und geprüft ist das Ganze an **einem** Gerät — Jabra Link 400. Der Weg über den Report-Deskriptor ist so gebaut, dass ein anderes Modell funktionieren sollte; behauptet wird es nicht.

### Nachtrag vom 09.09.2026 — die Bedeutung der Taste hängt nicht mehr am Gerät

Der letzte Absatz war die Warnung, und sie ist eingetroffen. Aber nicht so, wie
sie dastand: es lag nicht am zweiten Modell, sondern an der Konstruktion selbst.

**Der Befund.** „Ich kann gar keine Anrufe mehr entgegennehmen. Sobald ich
abnehme, hängt es wieder auf." Im Protokoll dreimal derselbe Ablauf, und der
Abstand ist das Argument:

    16:01:57.797  Toast-Aktion: accept
    16:01:57.892  Anruf 5e83db16: Incoming -> Connected
    16:01:59.458  Auflegen am Headset gedrueckt
    16:01:59.547  Anruf 5e83db16: Connected -> Ended

Der Anruf wurde **im Toast** angenommen. Niemand hat eine Taste berührt.
Dasselbe bei ausgehenden Anrufen, 331 ms nach dem Verbinden — auch dort drückte
niemand. Das Ereignis folgte nie einem Tastendruck, sondern immer einer
Zustandsänderung, die **nipp selbst** an das Gerät gemeldet hatte.

**Die Ursache ist der Satz „die Lampen sind Teil des Protokolls", zu wörtlich
genommen.** Um den Gabelzustand nachzuziehen, schrieb `PushState` ihn mit
`SyncHook(imGespraech)` direkt in das Feld, aus dem der Lese-Thread seine
Flanken ableitet. Damit stand dort „abgenommen", sobald ein Gespräch verbunden
war — ohne dass das Gerät je eine Taste gemeldet hätte. Und weil `Deute` den
Zustand aus der **Abwesenheit** der Hook-Usage ableitet, war der nächste
gewöhnliche Eingangsreport eine Flanke nach unten, die es physisch nie gab.
Gedeutet wurde sie als „auflegen".

**Ein Spiegel dessen, was das Gerät gemeldet hat, darf nur aus Gerätereports
gefüllt werden.** Wer ihn auch mit der eigenen Absicht beschreibt, erfindet
Ereignisse — und zwar solche, die von echten nicht zu unterscheiden sind.

**Die Korrektur geht weiter als der Fehler.** Ein `SyncHook` zu entfernen hätte
das Symptom behoben und die alte Lücke zurückgebracht, für die es gebaut war
(T82: ein Gespräch in der Oberfläche beendet, danach ist jeder zweite Druck
falsch). Beide Fälle haben dieselbe Wurzel: **die Bedeutung eines Tastendrucks
hing am Gabelzustand des Geräts.** Diese Abhängigkeit ist aufgelöst.

- Eine Gerätemeldung heisst nur noch „**betätigt**". `HookWatch` entscheidet, ob
  sie das war — verworfen werden ein unveränderter Zustand, das Loslassen kurz
  nach dem Drücken (700 ms) und das Echo einer eigenen Meldung (1,5 s).
- **Was die Betätigung bedeutet, entscheidet der Anrufzustand:** klingelt etwas,
  wird angenommen; sonst wird das Gespräch im Vordergrund aufgelegt. Das ist
  dieselbe Regel, die der globale Hotkey seit immer benutzt — und der hat nie
  ein Gespräch verloren, weil er keinen Gerätezustand kennt. Sie stand zweimal
  im Code; jetzt einmal, in `HeadsetPolicy.Interpret`, und `HandleHotkey` ruft
  sie auf.
- Damit ist auch gleichgültig, ob ein Gerät die Taste als Ein/Aus-Schalter oder
  als Momentan-Taster meldet. Das steht in seinem Report-Deskriptor, nicht bei
  uns, und nipp läuft an dreien (Engage 75, Link 400, PRO 9470).
- Die Lampen bleiben — als **Anzeige** (T84). Es hängt nur kein Gespräch mehr
  daran.

**Und der Grund, warum es zwei Tage dauerte, ist der eigentliche Befund.** Über
empfangene HID-Reports stand **nie eine Zeile im Protokoll**. Damit war „die
Taste tut nichts" nicht von „hier kommt gar nichts an" zu unterscheiden —
dieselbe Lücke, die schon beim Symbol im Infobereich gekostet hat. Auf Debug
protokolliert nipp jetzt jeden Eingangsreport mit Kennung und Usages, jeden
Ausgangsreport samt Erfolg, und beim Anbinden, welche Tasten das Gerät
überhaupt führt. `tools\Test-Headset.ps1` wertet das aus und prüft den Befund
gegen: ein Auflegen kurz nach einem Verbinden schlägt dort an.

**Was das über die Abnahme sagt.** T79 (Auflegen) galt seit dem 07.09.2026 als
bestanden; T80 (Annehmen) hatte nie ein Ergebnis. In **keinem** Protokoll dieses
Projekts steht je „Annehmen am Headset gedrueckt" — an keinem der drei Geräte.
Ein bestandenes Auflegen hat also nie belegt, dass die Taste funktioniert; es
belegte, dass eine der beiden Richtungen zufällig traf. **Eine Taste ist erst
geprüft, wenn beide Bedeutungen einmal am Gerät ausgelöst wurden** — und jetzt
an jedem der drei.

### Zweiter Nachtrag, eine Stunde später — was die Messung am Gerät ergab

Die Reparatur oben war richtig und nicht ausreichend. Mit den neuen
Protokollzeilen kam beim ersten Anruf heraus, dass zwei Annahmen darin falsch
waren. **Der Unterschied zu vorher: das steht jetzt in Zahlen da, statt vermutet
zu werden.**

**Erstens: `HookSwitch` (0x20) ist an diesem Gerät kein Tastendruck, sondern ein
gehaltener Zustand — und das Gerät ändert ihn auf unsere Meldung hin selbst.**

    26.655  Bericht 2: Usages 0x2A 0x97 0x20   Gabel=true    <- echter Druck
    26.658  Annehmen am Headset gedrueckt                    <- erstmals überhaupt
    26.762  Anruf e4e5e665: Incoming -> Connected
    29.645  Bericht 2: Usages 0x2A 0x97        Gabel=false   <- Antwort des Geräts
    29.647  Auflegen am Headset gedrueckt                    <- der Fehler
    29.701  Zustand gemeldet … angenommen=true               <- Report zurück

Auf „ein Gespräch läuft" hin gibt das Engage 75 seinen Off-Hook-Zustand **auf**.
Die Annahme, ein Gerät *spiegele* den gemeldeten Zustand, war falsch — es
verhandelt ihn. Damit greift auch ein Richtungsvergleich nicht: die Antwort ging
in die Gegenrichtung und galt deshalb als Absicht.

**Zweitens, und es erklärt den Rest: ein Report an dieses Gerät dauerte 2,94
Sekunden.** Zwei andere gingen 1 ms auseinander hinaus; dieser hing. Daran hängt
alles, was der Alltag gemeldet hat:

- **„Das Headset läutet weiter, obwohl ich den anderen höre."** Die Meldung „es
  klingelt nicht mehr" erreichte das Gerät drei Sekunden nach dem Annehmen. Das
  Läuten ist kein Anzeigefehler, sondern die Laufzeit eines Reports.
- **„Extrem verzögert und unkontrollierbar."** In diesen drei Sekunden liefen
  Gerät und nipp auseinander, und jede Meldung darin war zweideutig.

**Die Regel ist deshalb zeitlich, nicht inhaltlich:** solange ein Ausgangsreport
unterwegs ist — und 800 ms darüber hinaus —, ist jede Gabelmeldung eine
**Antwort** des Geräts, kein Tastendruck. Ein Schreibvorgang dauert normalerweise
eine Millisekunde, das Fenster ist also im Normalfall keines. Der Preis ist
benannt: ein echter Druck in diesen drei Sekunden wirkt nicht, und er steht als
verworfen im Protokoll. Ein Anruf, der ungefragt endet, ist teurer.

**Dazu zwei Kleinigkeiten mit Wirkung.** Der Schreib-Thread sammelt 60 ms, bevor
er schreibt — bei einem Selbstanruf gingen zwei Reports 1 ms auseinander hinaus,
und der erste war beim Ankommen längst überholt; bei drei Sekunden pro Report
ist jeder gesparte einer zu viel. Und die Protokollzeile nennt jetzt die
**Dauer**: ohne sie war „läutet weiter" nicht von einem Logikfehler zu
unterscheiden. Dieselbe Lehre wie beim Rufton, wo erst die Messwerte in der
Stopp-Zeile aus der Erklärung einen Beleg machten.

**Was daran zu lernen ist.** Die erste Fassung hat den Gerätezustand
*behauptet*, die zweite hat ihn *erwartet* — und beide sind an derselben Stelle
gescheitert: sie nahmen an, das Gerät verhalte sich vorhersagbar. Es tut es
nicht. Was trägt, ist die Frage „habe ich gerade selbst hineingeredet?" — die
kann nipp beantworten, ohne über das Gerät etwas zu wissen.

### Dritter Nachtrag — die drei Sekunden waren nicht das Gerät, sondern der Weg

Die Messung oben hat sich als Untergrenze entpuppt. Beim nächsten Anruf standen
im Protokoll **10,9 s, 13,8 s, 21,3 s und 83,2 s** für einen Report von drei
Byte. Und damit kippte die Deutung: eine Funkstrecke ist langsam, aber nicht
dreiundachtzig Sekunden langsam. **Das war kein Gerät, das trödelt, sondern ein
Aufruf, der hängt.**

`HidD_SetOutputReport` schickt den Report über die **Control-Pipe** und wartet
auf die Bestätigung. Der reguläre Weg für Output-Reports ist `WriteFile` über
die **Interrupt-Out-Pipe** — und es ist auch der Weg, den HIDAPI ging, also das
Modul, das Linphone bis 5.4 mitbrachte und dessen Entfernung diesen ganzen ADR
ausgelöst hat. Nach der Umstellung: **3 Millisekunden.**

**Was daran hing, war keine Lampe.** Der Ring-Report erreichte das Gerät nie
rechtzeitig, und deshalb tat „das Headset aus der Ladeschale nehmen" nichts —
das Gerät wusste in dem Moment nicht, dass ein Anruf klingelt. Dieselbe
Bedienung funktioniert in Bria an demselben Headset, und das war der Hinweis,
der zählte: **wenn ein anderes Programm es kann, ist es kein Gerätefehler.**

**Zwei Nebenwirkungen, beide erwünscht.**

- Das Echofenster aus dem zweiten Nachtrag bleibt, wird aber wieder zu dem, was
  es sein sollte: ein Fenster von Millisekunden. Bei 83 Sekunden hätte es jeden
  Tastendruck verworfen. **Eine Schutzregel, deren Fenster von einer Latenz
  abhängt, ist nur so gut wie die Latenz.**
- `_wanted` beginnt bei −1 statt 0. Der Riegel in `SetState` schreibt nur bei
  einer Änderung; mit 0 als Anfang war die erste Meldung „alles aus" keine, und
  das Gerät blieb beim Anbinden in dem Zustand, in dem nipp es vorfand — ohne
  definierte Lampen und ohne die Messung, wie schnell dieses Gerät antwortet.
  Jetzt steht sie nach jedem Start im Protokoll, noch vor dem ersten Anruf.

**Und die Reihenfolge war kein Zufall.** Ohne die Protokollierung der Dauer aus
dem zweiten Nachtrag wäre dieser dritte nicht möglich gewesen: „das Headset
läutet weiter" und „aus der Ladeschale nehmen tut nichts" sind zwei Meldungen,
die nach zwei Fehlern klingen. Dieselbe Zahl in derselben Zeile hat sie zu einem
gemacht. **Erst messen, dann deuten** — beim Rufton war es dieselbe Lehre.

### Vierter Nachtrag vom 10.09.2026 — nipp teilt dieses Gerät mit anderen Programmen

**Der Befund.** „Wenn ich per Microsoft Teams in einem Meeting bin und dann ein
Anruf auf nipp reinkommt, fliege ich aus dem Meeting." Auf Rückfrage: **schon
beim Läuten**, ohne dass eine Taste berührt wird. An allen drei Headsets — und
auch, wenn in nipp die Notebook-Karte gewählt ist.

**Die Kette.** nipp schreibt den Ring-Bericht (Usage `0x18`) auf die
Interrupt-Out-Pipe. Das Gerät **verhandelt** seinen Zustand, es spiegelt ihn
nicht (zweiter Nachtrag), ändert also seinen eigenen und meldet den Wechsel auf
der Eingangspipe zurück. Weil das Handle **geteilt** geöffnet ist
(`FILE_SHARE_READ | FILE_SHARE_WRITE`, so entschieden in dieser ADR), bekommt
Teams dieselbe Meldung. Und **Teams hat kein `HookWatch`**: es weiss nicht, dass
nipp gerade geschrieben hat, also ist der Zustandswechsel für Teams ein
Tastendruck des Benutzers — im Meeting bedeutet der auflegen.

**Das ist derselbe Fehler wie im ersten Nachtrag, eine Prozessgrenze weiter.**
Dort erfand nipp sich selbst Ereignisse, weil es die eigene Absicht in den
Gerätespiegel schrieb. Hier erfindet nipp Ereignisse **für ein anderes
Programm**. *Ein geteiltes Handle heisst geteilte Wirkung.*

**Belegt ist, dass Teams am selben Interface hängt** — im Protokoll vom
10.09.2026, eine Minute vor einem Termin, der im Kalender als Online-Meeting
steht:

    13:29:25  Headset-Bericht 2: Usages 0x2A 0x20, Gabel abgenommen=true
    13:29:25  Gabeltaste am Headset ohne Anruf - ohne Wirkung

**Nicht belegt ist die Kette bis zum Abbruch selbst.** Bei einem von acht
Ring-Berichten folgte 305 ms später ein Audiogerätewechsel, bei den übrigen
sieben nicht. Deshalb steht die Messung in der Testmatrix (T147–T150) und nicht
in dieser Begründung.

**Entscheidung. Ein Ausgangsbericht ist keine Lampe, sondern eine Mitteilung an
ein Gerät, das nipp mit anderen Programmen teilt.** Er ist nie folgenlos — also
nur, wenn nipp einen eigenen Anlass hat. Die Regel steht als reine Funktion in
`HeadsetSignalGate`, mit Tests, wie `HeadsetPolicy` und `HookWatch`:

- **Kein leerer Bericht, solange nipp diesem Gerät noch nie etwas gemeldet
  hat.** Damit entfallen der Bericht beim Anbinden und der bei jedem
  Audiogerätewechsel — am 10.09.2026 dreizehn Stück, ohne dass ein Anruf
  existierte. Läuft beim Anbinden ein eigenes Gespräch, geht er weiter hinaus:
  genau der Fall, für den er gebaut war.
- **Kein Ring-Bericht, solange das Gerät fremdbelegt ist**
  (`HookWatch.Fremdbelegung`: das Gerät meldet abgenommen, nipp hat keinen
  Anruf, und keine eigene Meldung ist unterwegs oder im Echofenster).
- **Off-Hook nach dem Annehmen geht hinaus.** Nimmt der Benutzer den nipp-Anruf
  an, während er in Teams sitzt, hat er entschieden; dass Teams dann das Gerät
  verliert, ist die Folge seiner Wahl und kein Fehler. Der Ring ist die eine
  Meldung, die ein fremdes Gespräch trifft, **bevor** jemand entschieden hat.
- **Und ein gemeldeter Audiogerätewechsel legt das Gerät nicht mehr ab, wenn es
  dasselbe ist** (`HidTelephonyDevice.ErsterTelefoniePfad`, prüfendes
  `CreateFile` mit `access=0`, auf einem eigenen Thread — nicht auf dem, der
  `Core.Iterate()` bedient). Bisher endete jedes Anbinden in einem Bericht.

**Was von Nachtrag 3 gilt und was verschoben wird.** Der Mechanismus
`_wanted = -1` **bleibt** — das Gate sitzt davor. Von den zwei Begründungen
dort:

- *„das Gerät aus einem alten Zustand zurücksetzen"* betrifft seit dem ersten
  Nachtrag nur noch **eine Lampe**, weil kein Gespräch mehr am Gerätezustand
  hängt. Der Bericht baut den Zustand ohnehin jedes Mal vollständig neu auf; der
  erste Bericht eines Anrufs **ist** das Zurücksetzen — nur in dem Moment, in
  dem es nützt statt ein fremdes Gespräch zu treffen. **Der Preis, benannt:**
  stürzt nipp mitten im Gespräch ab, kann die Off-Hook-Lampe bis zum nächsten
  Anruf stehen bleiben.
- *„einmal je Start messen, wie lange ein Bericht braucht"* bleibt gültig und
  wandert auf den ersten Bericht eines Anrufs. Die Dauer steht dort schon.

**Der teure Weg, und warum nicht.** Eine WASAPI-Sitzungsprüfung
(`IAudioSessionManager2`: aktive Renderer-Sitzung eines fremden Prozesses?) wäre
die präzise Antwort auf die **falsche Frage**. Was Teams herauswirft, ist der
HID-Pfad; eine aktive Audiositzung sagt nichts darüber, ob ein Programm am
Call-Control-Interface hängt. Spotify, ein Browser-Tab, ein Systemton — alles
aktive Sitzungen, und jede würde T84 und T80b abschalten; „die Lampe geht nicht
mehr an" wäre die nächste Meldung. Dazu setzt sie die Zuordnung HID-Gerät →
Audio-Endpunkt voraus, also die unten offene Frage, und wäre die erste Zeile
COM-Interop dieser Art im Repo. **Ein Fehlurteil des billigen Wegs kostet dagegen
nur eine Lampe.**

**Was die gewählte Erkennung nicht sieht — vollständig, damit es nicht später
als Überraschung auftaucht.**

1. Ein Gerät, das seinen Gabelzustand nur als **Momentan-Taster** meldet: dann
   gibt es keinen gehaltenen Zustand zu lesen. Am Engage 75 vorhanden, für Link
   400 und PRO 9470 offen.
2. Ein Meeting, das Teams **nicht als HID-Anruf** führt. Dann trägt allein die
   erste Regel.
3. Ein Meeting, das **schon lief**, als nipp startete — der Spiegel beginnt bei
   „aufgelegt". Der Auswegkandidat `HidD_GetInputReport` ist wieder die
   Control-Pipe, die 83 Sekunden gekostet hat: nicht in diesem Schritt. Als Test
   festgeschrieben (`Ohne_jeden_Geraetereport_wird_keine_Fremdbelegung_behauptet`).
4. Ein **Off-Hook, der am Gerät hängen bleibt**, nachdem das fremde Gespräch
   vorbei ist: am 10.09.2026 stand der Spiegel von 13:29 bis 14:01 auf
   abgenommen. In diesem Fenster fällt die Ring-Lampe aus, und mit ihr T80b.
5. **Wer es ist.** Die Erkennung sagt „nicht wir", nie „Teams".

**Offen, und die Wurzel des Notebook-Falls.** `Open` nimmt das **erste** Gerät
mit Telefonieseite, ohne Bezug zum in nipp gewählten Audiogerät — nipp schreibt
also auf das Jabra-Dongle, auch wenn es selbst über die Notebook-Karte
telefoniert. Der ehrliche Weg wäre die Container-ID
(`DEVPKEY_Device_ContainerId` gegen die des Audio-Endpunkts); ein Namensvergleich
wäre billig und würde im Zweifel T80b am eigenen Gerät abschalten. Bis dahin
nennt das Protokoll, **an welchem von wie vielen** Telefoniegeräten nipp hängt
(3112). Erst **T152** messen, dann entscheiden.

**Und wenn Teams ohne vorangehenden Bericht herausfliegt**, liegt es nicht am
Schreiben, sondern am geteilten Handle selbst oder am blossen Öffnen mit
`GENERIC_WRITE`. Dann trägt keine dieser Massnahmen, und ein Schalter, der das
Anbinden ganz unterlässt, wäre die Folgeentscheidung. Ein solcher Schalter wurde
für diesen Durchgang **bewusst nicht gebaut**: er wäre eine Abweichung von §22.5
(„der Zustand des Geräts folgt dem Gespräch") und träfe T80b und T84, und
solange die Automatik nicht am Gerät geprüft ist, weiss niemand, ob er nötig
ist. Die Protokollzeilen sagen es dann.

---

## ADR-027 — Anruferkontext im Journal: bei Bedarf abrufen, nichts speichern

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §20.3, §21.1, §21.2, docs/plans/ALLTAG-PLAN.md §5

**Kontext.** Aus dem Alltag kam der Wunsch, im Anrufjournal zu sehen, was das CRM und das Gesprächsjournal zu einem Anruf wissen. Heute zeigt die Liste Nummer, Dauer, Uhrzeit und Ergebnis (§20.3); der Anruferkontext existiert nur **während** des Gesprächs.

Dafür gibt es zwei Wege, und sie unterscheiden sich nicht im Aufwand, sondern in dem, was danach auf der Festplatte liegt.

**Entscheidung: beim Anklicken neu abrufen. Es wird nichts gespeichert.**

Ein Journaleintrag klappt auf, und erst dann werden die Quellen gefragt — mit demselben Dienst, derselben Karte und demselben Zwischenspeicher wie während eines Gesprächs.

**Begründung.**

*Der Grund steht schriftlich.* Dem das API-Team des Journals wurde in `docs/integrations/prompt-journal.md` zugesagt: „Die Antwort bleibt höchstens fünf Minuten im Arbeitsspeicher und wird nie auf die Platte geschrieben." Der andere Weg — beim Anruf mitspeichern — bricht diese Zusage.

*Und der Unterschied ist nicht klein.* Eine lokale Datenbank mit den Gesprächszusammenfassungen und Stimmungsbewertungen aller Anrufe eines Jahres ist etwas grundlegend anderes als eine Karte, die beim Klingeln kurz erscheint, auch wenn beide dieselben Daten zeigen. Sie überlebt jedes Zeitlimit, wandert bei einem Gerätewechsel mit, und niemand hat ihr zugestimmt. §21.2 zieht dieselbe Grenze: die Anrufliste darf einen extern aufgelösten **Namen** speichern — einen Namen, nicht einen Gesprächsinhalt.

*Was der gewählte Weg kostet, ist benannt.* Ein Eintrag von vor drei Monaten zeigt den heutigen Stand, nicht den von damals. Ohne Netz oder mit abgeschalteter Quelle bleibt die Stelle leer. Beides ist hinnehmbar; das Zweite bekommt eine Begründung statt eines leeren Kastens.

**Konsequenz.**
- Aufklappbereich am Eintrag statt einer neuen Seite: die Liste bleibt sichtbar, und der Rückweg entfällt.
- **Nur eine Anfrage gleichzeitig.** Wer durch die Liste klickt, löste sonst je Eintrag einen Abruf aus; der vorige wird abgebrochen — dasselbe Muster wie bei der Suche mit ihrem Generationszähler. Darin liegt fast der ganze Aufwand.
- **Der Test gehört vor den Code:** nach dem Benutzen des Journals steht in `history.db` keine Gesprächszusammenfassung. Er unterscheidet diese Entscheidung von der anderen, und er ist wertlos, wenn er erst danach geschrieben wird.
- §21 gehört um diesen Absatz ergänzt.

---

## ADR-026 — Die Wähltastatur gehört zum Kontakte-Tab

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §20.1, ADR-014

**Kontext.** Die Wähltastatur bleibt heute geöffnet, wenn jemand auf Anrufliste, Mailbox oder Einstellungen wechselt. Dort nimmt sie rund 200 Pixel und damit ein Drittel des Fensters, ohne gebraucht zu werden — der Anrufliste bleiben dann vier statt acht Zeilen.

**Entscheidung: die Wähltastatur wird beim Verlassen des Kontakte-Tabs eingeklappt und beim Zurückkommen wieder geöffnet.** Ihr Zustand bleibt also erhalten, nur ihre Sichtbarkeit hängt am Tab.

**Begründung.** Das Nummernfeld selbst steht über allen Tabs und bleibt es auch; gewählt werden kann weiterhin von überall. Was die Tastatur hinzufügt, ist eine Eingabehilfe für genau das Feld — und in der Anrufliste tippt niemand Ziffern. Der Einwand, ein Bedienelement dürfe nicht von selbst verschwinden, wiegt hier weniger: es verschwindet nicht, es kommt beim Zurückkommen wieder, und der Gewinn sind drei bis vier zusätzliche Zeilen in jeder Liste.

**Konsequenz.**
- Der gespeicherte Zustand (`Advanced.ShowDialpad`) bleibt die Absicht des Benutzers; der Tabwechsel überschreibt sie nicht.
- Der Testfall T76 („Wähltastatur offen, beenden, starten") gilt weiter für den Kontakte-Tab.

---

## ADR-025 — Das Nummernfeld bleibt bei lokalen Quellen

**Datum:** 06.09.2026 · **Status:** **angenommen**, bestätigt ADR-014 · **Bezug:** §8.1, §21, ADR-014

**Kontext.** Die Vorschlagsliste unter dem Nummernfeld zeigt ab zwei Zeichen Treffer aus Team-Nebenstellen, Outlook und dem Anrufverlauf — nicht aus dem CRM. Das Suchfeld im Kontakte-Tab dagegen fragt alle eingerichteten Quellen. Zwei Felder mit verschiedener Reichweite, und nichts sagt das.

**Entscheidung: es bleibt dabei. Stattdessen sagt der Platzhalter des Suchfelds, was es kann.**

**Begründung.** Die Vorschlagsliste erscheint während des Tippens und muss sofort da sein. Eine Netzabfrage je Tastendruck kostet Zeit und belastet fremde Systeme mit Anfragen, die niemand angefordert hat — genau die Überlegung, mit der ADR-014 das Kontakt-Suchfeld aus der Hauptansicht entfernt hat. Wer bewusst in fremden Systemen sucht, tut das im Suchfeld; dass es das kann, muss man ihm nur sagen.

**Konsequenz.**
- Der Platzhalter nennt die Reichweite, statt sie zu verschweigen.
- Wenn sich im Alltag zeigt, dass die Trennung stört, ist das der Anlass, es neu zu entscheiden — nicht diese Entscheidung.

---

## ADR-024 — Wahlwiederholung: die fünf zuletzt gewählten Nummern

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §20 (Ergänzung), docs/plans/ALLTAG-PLAN.md §4

**Kontext.** Weder §20 noch §8 kennen eine Wahlwiederholung. Aus dem Alltag kam der Wunsch danach. Die Daten liegen vollständig vor: `CallHistoryStore` speichert jeden Anruf mit Richtung, Nummer und aufgelöstem Namen.

**Entscheidung: beim Hineinklicken in das <b>leere</b> Nummernfeld erscheinen die fünf zuletzt gewählten Nummern — nur abgehende, ohne Doppelte.** Je Zeile der aufgelöste Name, darunter die Nummer, rechts wie lange es her ist. Sobald jemand tippt, weicht die Liste den Vorschlägen.

**Begründung.** „Zuletzt gewählt" ist das, wonach gefragt war. Eingehende mitzunehmen läge nahe — wer zurückrufen will, hätte es bequemer —, ergäbe aber zwei Listen mit ähnlichem Inhalt an zwei Stellen: diese hier und das Journal mit seinen Filtern. Zwei Wege zu derselben Information verwirren mehr, als der zweite hilft.

Nur bei leerem Feld, weil die Liste sonst im Weg steht: ab dem ersten Zeichen ist die Vorschlagsliste die bessere Antwort.

**Konsequenz.**
- Keine neue Ablage, nur eine Abfrage auf `CallHistoryStore`.
- §20 gehört um diesen Absatz ergänzt — die Projektregel verlangt einen Auftrag, bevor gebaut wird.
- Testfall: Liste erscheint bei leerem Feld, verschwindet beim Tippen, Auswahl übernimmt die Nummer, keine Doppelten.

---

## ADR-023 — Weiterleiten mit Vorschlagsliste, Team und Präsenz zuerst

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §8.2 (Ergänzung), docs/plans/ALLTAG-PLAN.md §3

**Kontext.** Das Ziel einer Übergabe ist heute ein leeres Textfeld. Wer intern weiterverbindet, muss die Nebenstelle auswendig wissen — bei zehn geht das, bei dreissig nicht mehr. §8.2 beschreibt blinde und begleitete Übergabe, sagt aber nicht, wie das Ziel hineinkommt.

**Entscheidung: aus dem Textfeld wird ein Feld mit Vorschlagsliste.** Team-Nebenstellen stehen oben, mit ihrer Präsenzlampe; danach die übrigen Quellen. **Freie Eingabe bleibt möglich.**

**Begründung.** Beim Weiterverbinden ist „ist die Person überhaupt frei" die eigentliche Frage, und genau diese Information hat das Besetztlampenfeld bereits abonniert — sie kostet nichts extra und ist der Grund, warum die Team-Nebenstellen oben stehen und nicht bloss alphabetisch dazwischen.

Die freie Eingabe muss bleiben: eine externe Nummer wird ohne Umweg eingetippt. Die Liste ergänzt das Feld, sie ersetzt es nicht.

**Konsequenz.**
- `ContactStore` und Präsenz liegen vor; es ist im Kern eine Oberflächenänderung. Was Zeit kostet, ist die Anordnung auf 400 Pixel neben den beiden Übergabeschaltflächen.
- §8.2 gehört um diesen Absatz ergänzt.
- Testfall: Auswahl per Tastatur, freie Eingabe, blinde und begleitete Übergabe je einmal über die Liste.

---

## ADR-022 — Rufnummern werden im Protokoll maskiert

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §9.6, §21.2, T57

**Kontext.** §21.2 verbietet Rufnummern im Protokoll, und ein Architekturtest erzwang das — aber nur für `Services/Integrations/`. Der Telefonie-Kern schrieb in dieselbe Datei „Anruf {Handle} an +41791234567 aufgebaut", auf Stufe Information, also immer. Die Anrufliste tat dasselbe auf Debug-Stufe, und Debug ist nach CLAUDE.md der empfohlene Weg für einen SIP-Trace: eingeschaltet ist es genau dann, wenn ein Protokoll an den Support geht. Das Diagnosepaket (§9.6) nahm die Dateien ungefiltert mit.

Damit war die Zusage nicht gehalten, die wir dem das API-Team des Journals schriftlich gegeben haben, und T57 („ein Protokoll ohne Rufnummern") war nicht erfüllbar.

**Entscheidung: Rufnummern werden maskiert, nicht entfernt.** `LogMasking.Number` lässt die letzten drei Ziffern stehen (`+41791234567` wird zu `…567`). Interne Nebenstellen bis vier Ziffern bleiben vollständig lesbar. Das Diagnosepaket schickt jede Protokollzeile durch `LogMasking.Line`, auch die des SDK.

**Begründung.**

*Warum nicht ganz weglassen.* Ein Protokoll muss erkennbar machen, ob zwei Meldungen denselben Anruf betreffen. Dafür gibt es das Handle, aber es steht nicht in jeder fremden Zeile — der SIP-Trace des SDK kennt es nicht. Drei Ziffern reichen, um Zeilen zuzuordnen, und reichen nicht, um jemanden anzurufen.

*Warum Nebenstellen lesbar bleiben.* „151" ist ein Apparat im eigenen Haus, keine Person. Dieselbe Grenze zieht `PhoneNumberKey`, wenn es entscheidet, was an fremde Systeme geht. Ohne diese Ausnahme wäre jedes Protokoll über die eigene Anlage unbrauchbar — die Präsenz-Abonnements bestehen ausschliesslich aus solchen Nummern.

*Warum der Zeilenfilter grob ist.* `LogMasking.Line` maskiert jede Ziffernfolge von sieben bis fünfzehn Stellen. Damit fällt auch eine RTP-Zeitmarke. Eine Zeitmarke im Support-Ticket ist verzichtbar, eine Kundennummer nicht. Zeitstempel und IP-Adressen bleiben erhalten, weil ihre Ziffern durch Trennzeichen unterbrochen sind.

**Konsequenz.**
- `PrivacyLogTests` erzwingt die Regel für den **ganzen** Quellbaum, nicht mehr nur für ein Verzeichnis. Wer eine Protokollmeldung mit einem Argument namens `number`, `destination`, `remoteNumber` oder `caller` ergänzt, muss `LogMasking` benutzen.
- Der Test hat bei seiner Einführung eine Stelle gefunden, die beim Durchsehen von Hand übersehen worden war: die tel:-URI in `App.HandleActivation`.
- Preis: wer einen Anruf im Protokoll bis zur Nummer verfolgen will, kann das nicht mehr. Das ist der Sinn.

### Nachtrag vom 13.09.2026 — der SDK-Trace gehört dazu

**Befund.** Die Zusage galt für jede Zeile, die nipp selbst schreibt, und für
keine, die das SDK schreibt. `SdkLogBridge` reichte den Text unverändert
weiter. Auf Stufe Debug — also genau dann, wenn ein Protokoll zum Support geht
— standen im Protokoll dieser Maschine am 12.09.2026 **714 Zeilen mit
`Authorization: Digest` oder `WWW-Authenticate`** und **4 282 `From:`-Zeilen
mit Rufnummern und Anzeigenamen**. `LogMasking.Line` im Diagnosepaket half
nicht: es maskiert erst ab sieben Ziffern, und eine dreistellige Nebenstelle
blieb stehen — auch in nipps eigener Warnzeile «Abonnement 'presence' fuer
sip:905@… abgelehnt».

**Entscheidung, gefällt am 13.09.2026:** der Support darf Debug einschalten
lassen, **und deshalb wird der Trace maskiert.** `LogMasking.SipLine` läuft über
jede SDK-Zeile und tut drei Dinge: `nonce`, `cnonce` und `response` einer
Digest-Anmeldung verschwinden (`realm` und Schema bleiben), Anzeigenamen vor
einer SIP-Adresse verschwinden, und der Benutzerteil einer Adresse wird
maskiert, sobald er nur aus Ziffern besteht.

**Strenger als `Number`, und das ist der Kern.** Dort bleibt alles bis vier
Ziffern lesbar, weil eine interne Nebenstelle in einer eigenen Protokollzeile
die Zuordnung erst möglich macht. Im Trace ist eine nackte Ziffernfolge im
Benutzerteil aber **immer** eine Gesprächspartei — «907» in einem `From:` ist
die Aussage «wer hat mit wem». Eine Kontokennung wie `151bv2` ist keine
Rufnummer und bleibt: ohne sie liesse sich bei mehreren Konten kein
Anmeldeproblem mehr zuordnen.

**Der Preis, offen genannt.** Eine dreistellige Nebenstelle wird vollständig
maskiert — die letzten drei Ziffern zu zeigen wäre bei drei Ziffern keine
Maskierung. Wer im Protokoll nachsehen will, **welche** Nebenstelle die Anlage
abgelehnt hat, sieht das nicht mehr; Anzahl, Grund und SIP-Code bleiben.
`tools/Test-Blf.ps1` läuft gegen ein maskiertes Protokoll unverändert durch
(geprüft am 13.09.2026, 95 290 Zeilen) — die Skripte suchen nach
Zustandsverläufen und Codes, nicht nach Nummern.

**Was bewusst lesbar bleibt:** Zustandsverläufe, Antwortcodes, Filterketten,
die eigene Kontokennung. Eine Maskierung, die dem Support nimmt, wofür er Debug
einschalten lässt, wird abgeschaltet und schützt dann gar nichts.

`PrivacyLogTests.Der_SDK_Trace_wird_maskiert` hält die Verdrahtung fest — sie
ist eine einzige Zeile, und wer sie entfernt, hebt die Zusage auf, ohne dass
irgendetwas anderes auffällt. **Am Gerät abzunehmen: T255 und T256.**

---

## ADR-021 — Lokalisierung bleibt zurückgestellt, die Texte stehen im Code

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §15

**Kontext.** §15 verlangt Benutzertexte in `Strings/de-CH.resw`. Es gibt diese Datei nicht — nur ein leeres Verzeichnis `Strings/de-CH/`. Die Texte stehen in XAML und C#: rund 548 literale Zeichenfolgen in XAML-Attributen und etwa 450 in C#, verteilt auf ungefähr dreissig Dateien. CLAUDE.md behauptete, die Datei sei „angelegt, aber leer".

**Entscheidung: die Texte bleiben vorerst im Code. Das ist eine bewusste Schuld, kein Versehen.**

**Begründung.** nipp hat genau eine Sprache und einen Kunden. Eine Ressourcendatei bringt in diesem Zustand keinen Nutzen, kostet aber bei jeder Textänderung einen zweiten Handgriff — und die Texte ändern sich derzeit häufig, weil jedes Review welche findet. Der Umzug ist mechanisch und lässt sich zu jedem späteren Zeitpunkt in einem Durchgang machen; er wird nicht schwieriger, wenn man wartet.

**Konsequenz.**
- Wer einen Text ändert, sucht ihn im Code. CLAUDE.md sagt das jetzt richtig.
- Die Schuld ist bezifferbar: ungefähr tausend Zeichenfolgen. Sie gehört auf die Liste vor der ersten Abgabe an einen Kunden mit einer zweiten Sprache, nicht vorher.
- Kein `x:Uid` einführen, solange das gilt — halb lokalisiert ist schlechter als gar nicht.

---

## ADR-020 — Das Besetztlampenfeld kennt „klingelt" nicht, und die Präsenz fehlt im Infobereich

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §10

**Kontext.** Zwei Abweichungen, die beide seit Wochen im Code stehen und in keinem ADR standen. Das Review vom 06.09.2026 hat sie als „abweichend ohne ADR" aufgeführt — die Kategorie, die die Projektregel ausdrücklich verbietet.

**§8.4 nennt für das Besetztlampenfeld drei Zustände: frei, klingelt, im Gespräch.** Über `presence` liefert das SDK nur Online, Busy, DoNotDisturb und Offline (docs/blf-pruefung.md). „Klingelt" kommt dort nie an; es bräuchte ein Abonnement auf `dialog` (RFC 4235), das die Anlage anders beantwortet und das nipp nicht implementiert.

**§10 nennt für das Infobereich-Menü vier Einträge: Öffnen, Präsenz setzen, Stumm, Beenden.** „Präsenz setzen" gibt es nicht.

**Entscheidung: beides bleibt, wie es ist.**

**Begründung.**

*Zu „klingelt":* der Zustand ist mit dem gewählten Mechanismus nicht zu haben, und der Wechsel auf `dialog` wäre ein Umbau der Präsenz mit einer offenen Frage an die Anlage. Was §8.4 damit erreichen will — sehen, ob jemand erreichbar ist —, leisten „frei" und „im Gespräch" bereits. Der fehlende Zwischenzustand kostet drei Sekunden Ungewissheit, keine Funktion.

*Zu „Präsenz setzen":* nipp veröffentlicht heute keine eigene Präsenz. Der Menüeintrag würde etwas anbieten, das nichts bewirkt, und §15 verbietet Bedienelemente ohne Wirkung. Der Eintrag kommt, wenn das Veröffentlichen kommt.

**Konsequenz.**
- README und §8.4 nennen weiterhin drei Zustände; die Umsetzung nennt zwei. Der Unterschied steht ab jetzt hier statt nirgends.
- Wer „klingelt" doch braucht, findet die Vorarbeit in docs/blf-pruefung.md — sie sagt, was zu prüfen wäre, bevor jemand `dialog` anfängt.
- Die drei Einträge im Infobereich bleiben. Ein vierter ohne Wirkung wäre schlechter als drei mit.

---

## ADR-019 — Ein Dutzend §9-Einstellungen bleibt der Provisionierung vorbehalten

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §9.2, §9.3, §9.4, §9.6, §11, §20

**Kontext.** Das Review vom 06.09.2026 hat rund zwölf Einstellungen gefunden, die §9 aufzählt und die im Modell und im `SettingsApplier` vollständig umgesetzt sind — aber in der Oberfläche fehlen: IPv6, RTP-Portbereich, DSCP für Signalisierung und Medien, Medienverschlüsselung als Auswahl (heute nur ein Schalter „erzwingen"), TURN samt Zugangsdaten, adaptive Bitrate, Klingelton als Dateipfad, Codec-Tabelle mit Rate und Bitrate, der Schalter für die Protokoll-Handler, „Konto aktiviert" und die Update-Prüfung.

**Entscheidung: sie bleiben erreichbar, aber nicht in der Oberfläche.** Wer sie braucht, setzt sie über ein Provisionierungsprofil (§11) oder direkt in `settings.json`. Zwei Ausnahmen: **Medienverschlüsselung als Auswahl** und **Klingelton als Datei** gehören in die Oberfläche, sobald jemand Zeit dafür hat — beide sind Entscheidungen, die ein Benutzer im Einzelfall trifft.

**Begründung.** §20 hat die Oberfläche auf 400 Pixel Breite und auf das Nötige verengt, und das ist die jüngere Anforderung. Ein DSCP-Wert oder ein RTP-Portbereich ist keine Benutzerentscheidung, sondern eine Netzentscheidung; sie wird einmal je Kunde getroffen und gehört ins Profil. Eine Einstellungsseite, die alles zeigt, was das SDK kann, ist die Seite, auf der niemand mehr findet, was er sucht.

„Konto aktiviert" fehlt auch im Modell. Es wäre ein Feld, dessen einziger Zweck darin besteht, ein Konto zu behalten, ohne es zu benutzen — dafür gibt es das Löschen, und die Zugangsdaten wären in beiden Fällen neu einzutragen.

Die Update-Prüfung (§9.6) hängt an §16.4, und §16.4 ist nicht entschieden. Sie wartet nicht auf Code, sondern auf einen Verteilungsweg.

**Konsequenz.**
- Die Werte sind vollständig im Modell und wirken; nur der Weg dorthin ist ein anderer. Der `SettingsValidator` prüft sie seit dem 06.09.2026 auch auf diesem Weg.
- `docs/provisioning.md` ist der Ort, an dem sie dokumentiert gehören.
- Wenn ein Kunde eine davon wiederholt braucht, ist das der Anlass, sie doch in die Oberfläche zu nehmen — nicht diese Entscheidung.


### Nachtrag vom 13.09.2026 — eine der zwölf war gar nicht umgesetzt

Diese Entscheidung stützte sich darauf, dass die betroffenen Einstellungen «im
Modell und im `SettingsApplier` vollständig umgesetzt» seien und nur in der
Oberfläche fehlten. Für den **SIP-Port** stimmte das nicht: `core.Transports`
kam im ganzen Telefonie-Ordner nicht vor, und die einzige Fundstelle war ein
Kommentar, der behauptete, die Änderung brauche einen Neustart. Ein Profil
konnte den Port also setzen, und es geschah nichts — der Weg, auf den ADR-019
verwiesen hat, führte ins Leere.

Behoben mit ADR-054. **Die Lehre gehört hierher:** wer eine Fähigkeit in eine
andere Ebene verweist, prüft, dass sie dort ankommt. Das ist dasselbe Muster
wie `App.SdkStatus`, `CardKind.History`, `IntegrationConfig.cards` und
`ClipResolver.DescribeCaller` — zum fünften Mal.
---

## ADR-018 — Outlook-Kontakte: COM bleibt, aber die Begründung von ADR-009 gilt nicht mehr

**Datum:** 06.09.2026 · **Status:** **angenommen**, ersetzt die Begründung von ADR-009 · **Bezug:** §8.4, ADR-009, docs/plans/ALLTAG-PLAN.md §2b

**Kontext.** ADR-009 wählte COM statt Microsoft Graph mit der Begründung, „die Entra-App-Registrierung lohnt den Aufwand nicht, solange nipp intern läuft". Darin steckte eine unausgesprochene Annahme: dass auf jedem Arbeitsplatz ein klassisches Outlook läuft.

Auf der Entwicklungsmaschine trifft sie nicht zu. Dort läuft das **neue** Outlook (`olk.exe`, `Microsoft.OutlookForWindows`) — eine WinUI-Anwendung um den Web-Client herum, **ohne COM-Automatisierung**. Es gibt kein `Outlook.Application`, das man ansprechen könnte, und keinen ROT-Eintrag. Der Weg aus §8.4 ist dort nicht kaputt, er ist nicht vorhanden. Microsoft rollt das neue Outlook als Nachfolger aus; die Frage ist nicht ob, sondern wann das jeden Arbeitsplatz betrifft.

**Entscheidung: COM bleibt vorerst, und nipp sagt jetzt, wenn es damit nichts findet.** Der Wechsel auf Graph war damit nicht entschieden, sondern vom Zeitdruck befreit.

**Nachtrag vom 06.09.2026, abends — zweimal entschieden.**

Zuerst fiel die Wahl auf Weg B (Graph über die Integrationsplattform). **Kurz darauf zurückgenommen: es bleibt bei COM, und Graph wird vorerst nicht gebaut.**

*Warum die Rücknahme hier steht und nicht gelöscht wurde.* Dass Graph erwogen und bewusst zurückgestellt wurde, ist die eigentliche Information. Ohne diesen Absatz stolpert der nächste Durchgang über dieselbe Frage, rechnet dieselben drei Wege durch und kommt womöglich zu einem anderen Ergebnis, ohne zu wissen, dass sie schon einmal beantwortet war.

*Was das kostet, offen gesagt.* Auf einem Arbeitsplatz mit dem neuen Outlook — darunter die Entwicklungsmaschine — **bleiben die persönlichen Kontakte aus.** Es gibt für sie keinen Weg, solange nur COM da ist. Die Team-Nebenstellen und die Suche im CRM sind davon unberührt und decken den Alltag weitgehend ab; das ist der Grund, warum das tragbar ist.

*Was es dafür spart.* Die Entra-App-Registrierung braucht einen Administrator, dauert Wochen und bindet jemanden ausserhalb des Projekts. OAuth2 in der Plattform sind vier bis sechs Tage Arbeit an einer Stelle, an der heute nichts drückt. Beides ist besser aufgehoben, wenn andere Dinge stehen — vor allem die Geräteabnahme des Telefons selbst.

**Der Auslöser, diese Entscheidung wieder aufzumachen**, ist nicht ein Datum, sondern ein Ereignis: **sobald der erste Arbeitsplatz ausserhalb der Entwicklung auf das neue Outlook wechselt und dort Kontakte vermisst.** Dann ist Weg B die Antwort, die Begründung dafür steht oben, und der lange Teil (die Registrierung) sollte in dem Moment sofort angestossen werden.

**Begründung.** Der Zustand vor dieser Entscheidung war nicht „unvollständig", sondern „sieht kaputt aus": die Oberfläche zeigte «Outlook (0)» ohne jeden Hinweis, und §8.4 verlangt ausdrücklich „eine verständliche Anzeige statt leerer Liste". Diese Anzeige gibt es jetzt, und sie unterscheidet die beiden Fälle — kein Outlook offen, oder nur das neue. Damit ist die Lage unvollständig statt peinlich, und die Entscheidung zwischen den drei Wegen lässt sich in Ruhe treffen.

Die drei Wege stehen mit ehrlichen Kosten in docs/plans/ALLTAG-PLAN.md §2b. Kurz: Graph nativ (3–5 Tage), Graph über die Integrationsplattform (4–6 Tage, weil ihr OAuth2 fehlt), oder klassisches Outlook voraussetzen (kostenlos, löst nichts). Wenn entschieden wird, ist **Graph über die Plattform** der richtige Weg: OAuth2 fehlt ihr ohnehin, sie wurde als generische Anbindung beauftragt (§21), und die erste OAuth2-Quelle zahlt den Aufbau für jede weitere.

**Konsequenz.**
- ADR-009 bleibt in Kraft, was die Wahl betrifft; seine **Begründung** ist überholt und durch diese ersetzt.
- `OutlookContactSource` erkennt das neue Outlook (Prozess `olk` ohne `OUTLOOK`) und nennt es. Am Gerät bestätigt.
- Die Entra-App-Registrierung braucht einen Administrator und dauert. Wenn Graph kommt, gehört sie früh angestossen — wie das Zertifikat.

---

## ADR-017 — Integrationskonfiguration in einer eigenen Datei

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §21.3, §9, §11, §17

**Kontext.** Die Integrationsplattform braucht eine Konfiguration: Datenquellen, Endpunkte, Authentifizierungsverweise, Mappings, Kartendefinitionen. Der naheliegende Ort wäre `NippSettings` und damit `settings.json` — dort steht schon alles andere.

**Entscheidung: eine eigene Datei `%APPDATA%\nipp\integrations.json` mit eigenem `IntegrationConfigStore` und eigener Schemaversion.**

**Begründung.**

*An `SettingsService.Changed` hängen fünf Empfänger.* Ein Speichern überträgt die Einstellungen auf den laufenden SDK-Core, schreibt Autostart und vier Protokoll-Handler in die Registrierung, meldet das systemweite Tastenkürzel neu an und erneuert sämtliche Präsenz-Abonnements — letzteres mit der offenen SEH-Ausnahme aus docs/plans/REVIEW.md F15. Eine korrigierte JSONPath-Zeile ist kein Grund, ins SDK zu greifen. Genau dieselbe Überlegung führte bei ADR-014 zu `SaveViewState`; hier ist die Trennung sauberer, weil es sich ohnehin um einen anderen Gegenstand handelt.

*Die Datei hat einen anderen Lebenszyklus.* `settings.json` gehört dem Benutzer und enthält Lautstärke, Gerätewahl und Fensterlage. Die Integrationsdatei gehört der Administration, wird pro Kunde gepflegt, ist um ein Vielfaches grösser und wird versioniert verteilt.

*Die Provisionierung bleibt lesbar.* Ein Profil trägt heute flache Pfad-Wert-Paare (`<set path="…" value="…"/>`). Ein Integrationspaket in dieser Form wären hunderte Zeilen. Stattdessen bekommt das Profil **einen** Verweis auf die Datei.

**Konsequenz.**
- `IntegrationConfigStore` übernimmt die bewährten Muster von `SettingsService`: atomares Schreiben über `.tmp`, kaputte Datei als `.kaputt-<Zeit>` beiseitelegen statt überschreiben, `SchemaVersion` mit `Migrate`-Haken von Anfang an, **Pfad über den Konstruktor** (`TestIsolationTests`).
- Geheimnisse stehen nie in der Datei, sondern als Verweis; der Wert liegt im bestehenden `SecretStore` unter `integration:<ref>`. Ein fehlender Wert ist kein Fehler beim Laden, sondern der Quellenzustand „übersprungen" mit einer Meldung nach §15.
- `Export`, `Import` und `Reset` der Einstellungen bleiben unverändert. Die Integrationen bekommen eigene Schaltflächen — wer Einstellungen zurücksetzt, verliert seine Anbindungen nicht.
- `PolicyService` sperrt sie unter dem Pfad `integrations`.
- Preis: zwei Dateien, zwei Exportwege, und ein Gerätewechsel braucht beide. Das ist der Preis dafür, dass eine Mapping-Änderung nicht die Telefonie anfasst.

---

## ADR-016 — JSONPath aus einem Paket, Ausdrücke aus eigener Hand

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §21.2, §21.3

**Kontext.** Zwei Sprachen werden gebraucht: eine, um Werte aus fremdem JSON zu ziehen, und eine, um daraus abgeleitete Felder zu bilden (`displayName = firstName + ' ' + lastName`). Für beides gibt es fertige Bibliotheken.

**Entscheidung. Für JSONPath das Paket `JsonPath.Net` (json-everything, MIT). Für Ausdrücke eine eigene, bewusst eingeschränkte Engine — keine Skript- oder Ausdrucksbibliothek.**

**Begründung.**

*JSONPath ist ein Standard (RFC 9535) mit Fallstricken.* Filterausdrücke, Mehrfachtreffer und Wildcards selbst zu implementieren heisst, einen Parser zu schreiben, den es schon gibt. `JsonPath.Net` setzt auf `System.Text.Json.Nodes` auf — also auf das, was ohnehin im Projekt ist —, braucht kein Newtonsoft und ist MIT-lizenziert. Die Lizenz zählt hier doppelt: die AGPL-Frage des SDK ist nicht geschlossen (`docs/licensing.md`), und eine weitere Copyleft-Abhängigkeit wäre die falsche Richtung.

*Bei den Ausdrücken ist das Gegenteil richtig.* Eine allgemeine Ausdrucksbibliothek kann in aller Regel mehr, als hier erlaubt sein darf: Typen auflösen, Methoden aufrufen, Reflection. §21.2 verbietet ausführbare Skripte, und eine Bibliothek, die man auf eine sichere Teilmenge zurückschneiden muss, ist schwerer zu verantworten als eine kleine eigene Grammatik, deren Umfang man vollständig kennt. Tokenizer, rekursiv absteigender Parser und Baumauswerter sind zusammen überschaubar und vollständig testbar.

**Konsequenz.**
- `using Json.Path` ausschliesslich unter `Services/Integrations/Mapping/`. Ein Architekturtest erzwingt es, wie §6 es für `Linphone` tut — eine Bibliothek, die überall stünde, liesse sich später nicht mehr austauschen.
- Die Ausdruckssprache kennt Literale, Feldreferenzen, Vergleiche, `&&`/`||`/`!`, `if` und eine **Whitelist** von Funktionen (`concat`, `coalesce`, `isEmpty`, `formatDate`, `formatPhone`, …). Kein Zugriff auf Typen, Dateien, Umgebung; keine Zuweisung, keine Schleife, keine Rekursion. Grenzen: 512 Token, Tiefe 32, Ergebnistext 4 KB.
- Gemischte Typen vergleichen sich nicht implizit: `'3' == 3` ist `false`. Stillschweigende Konvertierung ist die häufigste Quelle für Karten, die beim Kunden anders aussehen als beim Test.
- Parsefehler werden **beim Validieren** mit Position gemeldet, Laufzeitfehler liefern `null` plus Diagnosezeile — nie eine Ausnahme, die bis in den Anrufpfad steigt.
- Rückfallebene, falls das Paket je wegfällt: ein eigener Auswerter für `$.a.b[0].c` und `[*]` ohne Filter, etwa ein Tag Arbeit. Die Kapselung in einem Ordner ist genau dafür da.

---

## ADR-015 — Generische Integrationsplattform statt fester CRM-Anbindung

**Datum:** 06.09.2026 · **Status:** **angenommen** · **Bezug:** §21, `docs/plans/INTEGRATION-PLAN.md`

**Kontext.** Gewünscht ist, dass bei einem Anruf Angaben aus CRM, ERP und Ticketing erscheinen und dass die Kontaktsuche über mehrere Systeme läuft. Der kurze Weg wäre eine Anbindung an das eine System, das bv2 heute benutzt.

**Entscheidung: kein fest verdrahteter Connector, sondern eine konfigurierbare Plattform.** Eine Quelle wird beschrieben, nicht programmiert; der erste generische Connector spricht HTTP/REST. Umfang und Grenzen stehen in §21, die Umsetzung in `docs/plans/INTEGRATION-PLAN.md` (Phasen I0–I8).

**Begründung.** nipp ersetzt einen zugekauften Client und soll an Kunden gehen (§1, §16.3). Jeder Kunde hat ein anderes CRM. Eine feste Anbindung wäre bei der zweiten Installation wertlos, und die dritte hiesse, den Kern erneut anzufassen. Eine Beschreibung in einer Datei lässt sich dagegen provisionieren wie ein SIP-Konto.

**Die acht Entscheidungen von Dominic vom 06.09.2026**, die den Rahmen festlegen:

| Punkt | Entscheidung | Warum |
|---|---|---|
| Ort der Kontaktsuche | Suchfeld im Kontakte-Tab, sichtbar nur bei aktiver externer Suchquelle | Die Vorschlagsliste unter dem Nummernfeld ist auf fünf Treffer begrenzt (§8.1) und muss ohne Latenz reagieren. Das Suchfeld, das ADR-014 entfernt hat, kehrt damit zurück — aber für einen anderen Zweck |
| Ausgehende Anrufe | werden nachgeschlagen, Standard ein | Wer wählt, sieht Kundennummer und offene Aufträge schon während des Rufaufbaus |
| Interne Nummern | gehen nicht an externe Systeme | Für einen Kollegen auf Nebenstelle 151 hat kein CRM eine Antwort — und die Nebenstelle eines Mitarbeiters hat auf einem fremden Server nichts zu suchen |
| Konfiguration | eigene Datei | ADR-017 |
| JSONPath und Ausdrücke | Paket beziehungsweise eigene Engine | ADR-016 |
| Erstes Zielsystem | das CRM, nur Test-Mandant | Ein echtes System beweist, dass der generische Connector trägt. §13 und `CLAUDE.md`: nie gegen Kundentenants |
| Toast | bleibt unverändert | Der Toast-Pfad ist seit docs/plans/REVIEW.md §8 am Gerät noch nicht abgenommen (T06). Dort jetzt etwas zu ändern hiesse, eine offene Abnahme zu verschieben |
| Anrufliste | darf extern aufgelöste Namen speichern | Eine Liste mit Namen statt Nummern ist der halbe Nutzen der Anbindung. Der Preis ist ein Fremddatum in `history.db` für die Dauer der Aufbewahrung (Standard 365 Tage) — steht in `docs/` |

**Konsequenz.**
- Neuer Namespace `Nipp.Core.Services.Integrations` mit einer Schichtgrenze wie §6: **weder SDK noch WinUI**, erzwungen durch `IntegrationBoundaryTests`.
- Kein eigenes Projekt im ersten Ausbau. Ein `Nipp.Integrations.csproj` würde die Grenze stärker durchsetzen, kostet aber Build-Aufwand auf der ARM64-Maschine (ADR-001) ohne heutigen Nutzen. Der Namespace samt Test genügt und lässt sich später ohne Codeänderung herauslösen.
- `Contact` bekommt drei zusätzliche Angaben (Quellenkennung, E-Mail, Herkunftsliste) und `ContactSourceKind` einen dritten Wert `External`. `OutlookContactSource` wird **nicht angefasst** — die Migration ist ein Adapter über den bestehenden Zwischenspeicher, kein Umbau der COM-Klasse.
- Die Datenschutzlage ändert sich: mit einer aktiven Quelle verlässt die Rufnummer den Arbeitsplatz. Das ist der Grund für die Standardwerte oben, für Caches nur im Arbeitsspeicher und dafür, dass Protokoll und Diagnosepaket weder Nummern noch Suchtexte noch Antwortinhalte enthalten.
- **Grenze zum Bestand:** Phase I3 berührt den Anrufpfad und setzt voraus, dass T06 am Gerät abgenommen ist (docs/plans/REVIEW.md §8.3). Vorher wird dort nichts gebaut.

---

## ADR-014 — Bedienung der Einstellungen und der Kontaktliste

**Datum:** 05.09.2026 · **Status:** **angenommen** · **Bezug:** §8.4, §9.6, §20.1

**Kontext.** Nach dem ersten Arbeiten mit der fertigen Oberfläche kamen sieben
Wünsche von Dominic. Fünf davon stehen so nicht in `NIPP-BUILD.md` und brauchen
deshalb eine Begründung; zwei sind Korrekturen innerhalb des Vorgegebenen.

**Was nicht in der Spezifikation steht.**

| Punkt | Warum trotzdem |
|---|---|
| **Team per Ziehen sortieren** | §8.4 kennt die Team-Liste, nicht ihre Ordnung. Wer zehn Nebenstellen hat, ruft drei davon täglich an — alphabetisch stehen die irgendwo. Die Reihenfolge war schon immer im Modell (`List<TeamExtension>`), sie wurde nur beim Laden wegsortiert. |
| **Abschnitte Team/Outlook zuklappen** | §8.4 verlangt „beide Quellen getrennt sichtbar". Bei über hundert Outlook-Kontakten in einem 400 Pixel breiten Fenster heisst getrennt sichtbar in der Praxis: einen davon wegklappen können. |
| **Bestehende Konten und Nebenstellen bearbeiten** | §9.1 nennt Hinzufügen und Entfernen. Einen Anzeigenamen zu korrigieren hiess bisher, das Konto neu anzulegen — mit Passwort. Für einen Supporter im Kundeneinsatz (§1) ist das die falsche Reihenfolge. |
| **Einstellungen ausgeben und einlesen** | §9.6 kennt nur den Diagnose-Export. Ein Gerätewechsel bedeutete bisher, zwei Dutzend Felder abzutippen. |
| **Alles zurücksetzen** | Steht nirgends. Wer sich verstellt hat, hatte nur den Weg über das Löschen von `settings.json` — und verlor dabei die Konten. |

**Zwei Entscheidungen darin, die nicht offensichtlich sind.**

*Die Ausgabedatei enthält **keine** Passwörter.* Sie liegen im `SecretStore`
unter DPAPI, gebunden an Benutzerkonto und Rechner — mitexportiert wären sie
auf dem Zielgerät wertlos und in der Datei ein Klartext-Schlüsselbund. §10 sagt
„niemals Klartext", und eine Ausgabedatei wird herumgereicht, angehängt, in ein
Ticket gelegt. Nach dem Einlesen wird das Passwort einmal neu eingetragen; steht
als Hinweis in der Datei selbst. Wer dieselben Konten auf demselben Gerät schon
hatte, merkt nichts davon — die Geheimnisse werden aus dem vorhandenen Speicher
ergänzt.

*Zurücksetzen lässt die Konten stehen.* Alles andere geht auf die Vorgaben aus
§9 zurück, die SIP-Konten samt Passwörtern und die Anrufliste bleiben. Der
Grund ist praktisch: nach einem Zurücksetzen soll nipp weiter telefonieren, ohne
dass sich jemand neu anmeldet. Wer die Konten los werden will, entfernt sie
einzeln — dort steht die Rückfrage, die dazugehört.

**Was innerhalb der Spezifikation liegt und deshalb keine Abweichung ist.**
Das Kontakt-Suchfeld zu entfernen: §8.4 schreibt keines vor, und das Nummernfeld
durchsucht nach §8.1 bereits Kontakte und Anrufliste. **Vorbehalt:** §8.1 deckelt
die Vorschlagsliste auf fünf Treffer. Wer „Me" tippt und vierzig Meier im
Adressbuch hat, sieht fünf davon und hat keinen zweiten Weg mehr. Bleibt so,
weil die Zahl aus der Spezifikation kommt; fällt es im Alltag auf, ist es hier zu
ändern. Ebenso keine Abweichung: die Präsenz-Lampe nach rechts zu setzen — §8.4
verlangt Farbe **und** Text, nicht eine Reihenfolge.

**Konsequenz.**
- Die Reihenfolge des Teams wird über `SaveViewState` abgelegt, nicht über
  `Save`. An `Changed` hängen fünf Empfänger; einer davon erneuert sämtliche
  Präsenz-Abonnements, und genau dort steht die offene SEH-Ausnahme aus
  docs/plans/REVIEW.md F15. Eine umsortierte Liste ist kein Grund, ins SDK zu greifen.
  **F15 ist damit umgangen, nicht behoben** — beim Speichern in den
  Einstellungen läuft die Kette weiterhin.
- `ContactStore.Sort` sortiert das Team nicht mehr alphabetisch. Käme je eine
  zweite Team-Quelle dazu, braucht es dort eine Rangkarte.
- Sortiert wird nur in einem eigenen Modus (Umschalter in der Kopfzeile). In
  einer Liste, in der ein Doppelklick wählt, ruft dauerhaftes Ziehen sonst
  irgendwann die falsche Person an.
- Legt ein Provisionierungsprofil `contacts` fest, ist der Umschalter nicht da:
  der nächste Profilabruf überschriebe die Reihenfolge ohnehin (§17).
- Zwei neue Felder in `AdvancedSettings` (`ShowTeamContacts`,
  `ShowOutlookContacts`). Kein Schemawechsel — §9 erhöht die Version nur, wenn
  ein Feld seine Bedeutung ändert.

---

## ADR-013 — Der globale Hotkey nimmt an und legt auf

**Datum:** 05.09.2026 · **Status:** angenommen · **Bezug:** §9.6, §10, T26

**Kontext.** §9.6 führt den Eintrag „Globaler Hotkey **Annehmen/Auflegen**", die Testmatrix T26 verlangt „nimmt an und legt auf". Gebaut war etwas anderes: das Kürzel holte nur das Fenster nach vorn. Der Kommentar im Code beschrieb das als Absicht („der Sinn der Sache ist, aus einer beliebigen Anwendung heraus eine Nummer zu wählen") — eine stille Abweichung ohne ADR.

**Entscheidung.** Das Kürzel entscheidet nach Lage:

| Zustand | Wirkung |
|---|---|
| Ein Anruf klingelt | annehmen |
| Ein Gespräch läuft | auflegen |
| Sonst | nipp nach vorn holen, Zeiger ins Eingabefeld |

**Konsequenz.**
- §9.6 und T26 sind erfüllt, ohne dass das Kürzel seinen bisherigen Zweck verliert: solange nicht telefoniert wird, verhält es sich wie vorher.
- **Preis:** während eines Gesprächs holt das Kürzel nipp nicht mehr nach vorn, sondern legt auf. Das ist bei einem Softphone die geläufige Belegung (Headset-Tasten tun dasselbe), aber es ist eine Änderung: wer das Fenster im Gespräch sucht, nimmt das Symbol im Infobereich.
- Ein Fehler beim Ausführen wird abgefangen und protokolliert — eine Ausnahme aus diesem Ereignishandler nähme sonst die App mit.

---

## ADR-012 — Provisionierung: https vorausgesetzt, Bezugsquelle nur aus der Auslieferung

**Datum:** 05.09.2026 · **Status:** angenommen · **Bezug:** §11, §17, ADR-010

**Kontext.** Ein Provisionierungsprofil legt Konten, Zugangsdaten, Team-Nebenstellen und Einstellungen fest. Bis zum 05.09.2026 galt: `http` wird geholt und nur protokolliert („manche Anlagen stehen nur intern und ohne TLS da"), und ein Profil durfte über den Pfad `advanced.provisioning-uri` auch die Adresse festlegen, von der künftige Profile kommen.

**Das Problem.** Beides zusammen ergibt eine dauerhafte Übernahme: Wer im Netz zwischen Arbeitsplatz und Server antworten kann, liefert ein Profil mit eigenen Konten *und* schreibt die Bezugsquelle auf den eigenen Server um. Ab dann ist der ursprüngliche Server aus dem Spiel, und die Telefonie des Arbeitsplatzes gehört jemand anderem. Für ein Werkzeug, das Rufnummern wählt und Zugangsdaten hält, ist das kein hinnehmbares Restrisiko.

**Entscheidung.**

1. **Ein Profil wird nur über https geholt.** Ausnahme: die Einstellung „Unverschlüsselt zulassen" (§9.6, Standard **aus**), die ein Supporter bewusst setzen muss.
2. **Zwei Einstellungen darf ein Profil aus dem Netz nicht setzen** — `advanced.provisioning-uri` und `advanced.allow-insecure-provisioning`. Die mitgelieferte Konfiguration unter `%PROGRAMDATA%` darf es weiterhin: sie kommt mit der Installation und ist damit so vertrauenswürdig wie die Installation selbst.

**Konsequenz.**
- Ein Kunde ohne TLS-Webserver braucht einen zusätzlichen Handgriff. Das ist gewollt: die Entscheidung soll bewusst fallen, nicht beiläufig.
- Der Weg für die Erstverteilung bleibt offen — `nipp-factory.xml` kann die Provisioning-Adresse setzen.
- `nippprov schema` kennt den neuen Pfad und markiert die beiden geschützten.
- Abgesichert durch `ProvisioningServiceTests`.

**Nachtrag zum selben Durchgang, ohne eigene ADR, weil es ein Fehler war und keine Entscheidung:** Ein Profil ohne Passwort — der in `docs/provisioning.md` empfohlene Weg — ersetzte die Kontoliste durch Konten mit *leerem* Passwort. Das über DPAPI hinterlegte blieb liegen und wurde nicht mehr gelesen, das Konto registrierte sich nie. Behoben: fehlt das Passwort im Profil, gilt das lokal hinterlegte.

---

## ADR-011 — Automatische Aussteuerung ab Werk aus

**Datum:** 05.09.2026 · **Status:** angenommen · **Bezug:** §9.4

**Kontext.** §9.4 führt „Automatische Aussteuerung, Schalter, Standard **ein**". Zwei Befunde dazu:

1. Die Einstellung wurde **nirgends** auf den Core übertragen. Der Schalter stand im Modell, in der Diagnose und in keinem Code, der etwas bewirkt.
2. Die Dokumentation des SDK sagt zu `Core.AgcEnabled` wörtlich: *„This algorithm is very experimental, not usable in its current state."*

**Entscheidung.** Der Schalter wird angeschlossen und wirkt jetzt. Der **Standard steht auf aus**, abweichend von §9.4.

**Konsequenz.** Ein Schalter, der ab Werk eine Funktion einschaltet, von der ihr Hersteller abrät, wäre keine Vorgabe, sondern eine Falle — insbesondere, weil sich das Ergebnis erst im Gespräch zeigt und dann nach einem Fehler der Anlage klingt. Wer die Aussteuerung will, schaltet sie ein; der Hinweis auf den Zustand des Algorithmus steht in der Oberfläche daneben.

---

## ADR-010 — Provisionierung über ein eigenes Format statt `Core.ProvisioningUri`

**Datum:** 05.09.2026 · **Status:** angenommen · **Bezug:** §9 (Tabelle „Provisionierung"), §17, AP8.2

**Kontext.** §9 nennt für die Provisionierung `Core.ProvisioningUri`. Damit holt das SDK die Konfiguration selbst und schreibt sie in seine eigene `linphonerc`.

nipp führt seine Einstellungen aber nicht in der `linphonerc`, sondern in `settings.json`, und überträgt sie über den `SettingsApplier` in den laufenden Core (AP5.5). Das war keine Stilfrage, sondern die Voraussetzung dafür, dass die Einstellungsseite überhaupt funktioniert: sie liest und schreibt typisierte Werte, nicht Textzeilen einer INI-Datei.

**Das Problem.** Beide Wege nebeneinander ergeben zwei Wahrheiten. Ein Profil, das über `ProvisioningUri` direkt in die `linphonerc` schreibt, wird beim nächsten Speichern der Oberfläche stillschweigend überschrieben — der Administrator setzt einen Wert, der Benutzer öffnet einmal die Einstellungen, und die Vorgabe ist weg. Umgekehrt könnte niemand erklären, warum eine Einstellung in der Oberfläche anders aussieht als sie wirkt.

Dazu kommt: Das Linphone-Format kennt nur, was das SDK kennt. Team-Nebenstellen, gesperrte Felder, Erscheinungsbild, Anrufliste — nichts davon lässt sich darin ausdrücken.

**Entscheidung.** Ein eigenes XML-Format (`<nipp-provisioning>`), gelesen von `ProvisioningParser`, angewendet von `ProvisioningService` über denselben Weg wie jede andere Einstellung. `Core.ProvisioningUri` bleibt ungenutzt.

**Konsequenz.**
- Eine Wahrheit: alles läuft durch `settings.json` und den `SettingsApplier`.
- Das Format kann alles ausdrücken, was nipp kennt — auch das, was das SDK nicht kennt.
- `Nipp.Provisioning` (`nippprov`) erzeugt und prüft Profile mit **demselben Parser**, den die App verwendet. Ein Profil, das `nippprov pruefen` durchgeht, geht auch in der App durch.
- **Preis:** Wer Linphone-Provisioning bereits einsetzt, kann seine Profile nicht weiterverwenden. Für bv2 trifft das nicht zu — es gab bisher gar keine Provisionierung.
- Das XML kommt von einem Webserver, ist also Eingabe von aussen: DTD-Verarbeitung ist abgeschaltet und es gibt keinen `XmlResolver` (XXE), abgesichert durch einen Test.

---

## ADR-004 — Zwischenbefund packaged vs. unpackaged (Vorarbeit zu AP2.4)

**Datum:** 04.09.2026 · **Status:** **abgelöst durch ADR-008** (nachgetragen am 07.09.2026) · **Bezug:** §4, §14.3

> **Warum das Statusfeld drei Tage falsch stand.** ADR-008 trägt seit dem
> 04.09.2026 „Löst ADR-004 ab" in seiner Kopfzeile, und der letzte Absatz
> dieser ADR sagte selbst, was noch fehlte: der Entwicklermodus. Der ist
> gesetzt, `Add-AppxPackage -Register` läuft, und der packaged-Weg steht als
> Rezept in `docs/packaging.md`. Es war nur niemand zurückgekommen, um das
> Feld zu ändern — ein ADR mit dem Status „offen" wird gelesen wie eine
> unerledigte Aufgabe.
>
> **Eine Vermutung dieser ADR hat sich als genau umgekehrt erwiesen.** Hier
> stand: „ohne packaged gibt es keine Toasts nach §8.6". Gemessen am
> 07.09.2026 gilt das Gegenteil — **unpackaged** bekommt Toasts, **packaged**
> nicht. Der Grund und der Stand der Behebung stehen im Nachtrag zu ADR-008.

**Kontext.** §4 verlangt, die Packaging-Frage in M1 zu klären, weil MSIX und native Bibliotheken eine klassische Bruchstelle sind. Beim Abschluss von M0 fiel der erste Teil dieser Antwort ungeplant an — noch ohne jedes SDK, also mit einer nackten WinUI-App.

**Beobachtung am 04.09.2026** (ARM64-Maschine, x64-Build, Windows App SDK 2.4.0):

| Variante | Ergebnis |
|---|---|
| **unpackaged** (`WindowsPackageType=None`) | **läuft.** Fenster erscheint, Titel „nipp", Log wird geschrieben |
| **packaged** (`dotnet run` mit Debug-Identität) | **scheitert.** `System.Runtime.InteropServices.COMException (0x80040154) REGDB_E_CLASSNOTREG` in `DeploymentManagerAutoInitializer` beim Erzeugen von `DeploymentInitializeOptions` |

Die naheliegende Erklärung — die x64-Variante der `Microsoft.WindowsAppRuntime.2` fehle auf der ARM64-Maschine — wurde **geprüft und ausgeschlossen**: 2.4.0.0 liegt in x86, x64 und arm64 vor, VCLibs ebenso.

**Auffällig ist stattdessen ein Versionsgeflecht im NuGet-Paket.** `Microsoft.WindowsAppSDK` 2.4.0 zieht Unterpakete mit auseinanderlaufenden Versionen:

```
Microsoft.WindowsAppSDK            2.4.0
Microsoft.WindowsAppSDK.Runtime    2.4.0
Microsoft.WindowsAppSDK.Foundation 2.3.9   <- der fehlschlagende Auto-Initializer
Microsoft.WindowsAppSDK.WinUI      2.3.6
Microsoft.WindowsAppSDK.Base       2.0.4
```

Der Fehler entsteht in `Foundation 2.3.9` gegen eine installierte Runtime 2.4.0.0. Ob das die Ursache ist oder nur ein Symptom des Debug-Identitäts-Mechanismus, ist **noch nicht geklärt**.

**Was das für AP2.4 bedeutet.** Der Doppeltest ist damit nicht vorweggenommen, aber die Ausgangslage hat sich verschoben: der packaged-Pfad ist schon **ohne** native DLLs kaputt. Beim Untersuchen also erst diesen Fehler auflösen, bevor die DLL-Kette dazukommt — sonst überlagern sich zwei unabhängige Ursachen und das Fehlerbild wird unlesbar.

Zu prüfende Ansätze, in dieser Reihenfolge:
1. Echtes MSIX bauen und mit `Add-AppxPackage -Register` registrieren, statt sich auf die Debug-Identität von `dotnet run` zu verlassen.
2. Die Unterpaketversionen explizit angleichen (`PackageReference` auf `Microsoft.WindowsAppSDK.Foundation` mit passender Version) oder eine SDK-Version wählen, deren Geflecht in sich stimmt.
3. `WindowsAppSDKSelfContained=true` — die App bringt die Runtime mit, keine Framework-Abhängigkeit. Für die Auslieferung an Kundenumgebungen (§16.3) ohnehin attraktiv, kostet Paketgrösse.

**Vorläufige Konsequenz:** M0 wurde unpackaged verifiziert. Das ist für M0 zulässig — die Akzeptanz verlangt einen Start, kein Paket. Ab P7 hängt aber Substanz daran: ohne packaged gibt es keinen `AppNotificationManager` und damit keine Toasts nach §8.6, sondern eine eigene Toast-Lösung (+2 bis 3 Tage, siehe `packaging.md`).

### Nachtrag 04.09.2026 — die Versionsvermutung war falsch

Nach dem Einbinden des SDK habe ich das erzeugte MSIX-Layout untersucht. Zwei Ergebnisse:

**Das Paket-Layout ist vollständig und korrekt.** Der Payload-Teil aus `build/Linphone.Sdk.targets` greift: `liblinphone.dll`, `mediastreamer2.dll`, `lib/mediastreamer/plugins/libmswasapi.dll` und alle **8 von 8** belr-Grammatiken liegen im Layout, das registriert werden würde. Der Bruch aus §14.3 — native DLLs kommen nicht ins Paket — tritt hier also **nicht** ein.

**Und das Manifest enthält die richtige Framework-Referenz:**

```xml
<PackageDependency Name="Microsoft.WindowsAppRuntime.2" MinVersion="2.4.0.0" />
```

Das ist genau die installierte Version. **Damit ist die oben vermutete Ursache widerlegt** — das auseinanderlaufende Versionsgeflecht im NuGet (`Foundation 2.3.9` neben `Runtime 2.4.0`) ist offenbar normal und nicht der Grund für `REGDB_E_CLASSNOTREG`.

**Die verbleibende Erklärung:** `dotnet run` startete die App ohne wirksame Paketidentität. Die WinRT-Klassen des DeploymentManagers sind ohne Identität nicht aktivierbar, und genau das meldet `REGDB_E_CLASSNOTREG`. Es war also kein Konfigurationsfehler, sondern der falsche Starttest — die Debug-Identität von `dotnet run` hat nicht gegriffen.

**Offen bleibt allein die Registrierung.** `Add-AppxPackage -Register` scheitert an `0x80073CFF`: Windows verlangt den Entwicklermodus (`AllowDevelopmentWithoutDevLicense`), Sideloading allein genügt nicht. Sobald der gesetzt ist, ist AP2.4 in einem Durchgang abschliessbar — die Vorarbeit steht.

Ansatz 2 (Versionen angleichen) und Ansatz 3 (`WindowsAppSDKSelfContained`) aus der Liste oben sind damit **nicht mehr nötig** und werden nicht weiter verfolgt.

---

## ADR-003 — Windows App SDK 2.4.0 statt 1.x

**Datum:** 04.09.2026 · **Status:** angenommen · **Bezug:** §4, §2

**Kontext.** §4 gibt „Windows App SDK (aktuellste stabile 1.x)" vor. Das offizielle Microsoft-Projekttemplate für WinUI 3 legt jedoch `Microsoft.WindowsAppSDK` **2.4.0** an. Damit stand die Frage, ob auf 1.x zurückgestuft werden muss — die harte Nebenbedingung ist §2: **Windows 10 22H2 muss unterstützt bleiben.**

**Befundlage, recherchiert am 04.09.2026.**

| Punkt | Ergebnis |
|---|---|
| Aktuelle Versionen | 2.x-Linie: **2.4.0 vom 13.08.2026** (Servicing-Ende 29.04.2027). 1.x: letzter Patch 1.8.260804001, Support-Level „Maintenance" |
| **Support-Ende 1.x** | **09.09.2026** — fünf Tage nach diesem Entscheid. 1.7 ist seit 18.03.2026 out of support |
| **Windows-10-Minimum in 2.x** | **unverändert Windows 10 1809 (17763)** — 22H2 (19045) ist abgedeckt. 2.x ist *nicht* Windows-11-only |
| Änderungen 1.x → 2.x | überwiegend Versionierung (erstes SemVer-Major, Package Family Name folgt der Major-Version), keine Architekturänderung. Anheben der `PackageReference` genügt |
| `AppNotificationManager` | in beiden Linien vorhanden und **unverändert**, keine Deprecation. Buttons über `AppNotificationBuilder.AddButton`, COM-Aktivierung startet die App auch bei geschlossenem Fenster — die Anforderung aus §8.6 bleibt erfüllt |
| Entfallen in 2.x | App Content Search (`Microsoft.Windows.Search.AppContentIndex`) — von nipp nicht gebraucht |
| Deprecated in 2.x | `DependencyObject.Dispatcher`, `Window.Current`, `FocusManager.GetFocusedElement`; `SystemBackdropHost` → `SystemBackdropElement`. Beim Schreiben von UI-Code beachten |

**Entscheidung.** Windows App SDK **2.4.0**, TFM `net8.0-windows10.0.26100.0`, `SupportedOSPlatformVersion` **10.0.19041.0**. Eine Neuentwicklung auf 1.x zu setzen, die in fünf Tagen aus dem Support fällt, wäre die schlechtere Wahl — und 1.x bringt beim Windows-10-Support keinen Vorteil, weil das Minimum identisch ist.

**Konsequenz.**
- §4 der Spezifikation ist an dieser Stelle überholt; die Klammer „1.x" ist als „aktuellste stabile" zu lesen.
- Die deprecated APIs oben werden im UI-Code nicht verwendet.
- **Rest-Unsicherheit, ausdrücklich vermerkt:** die dedizierte OS-Support-Matrix von Microsoft ist auf dem Stand 15.10.2025 und listet keine 2.x-Zeile mit Windows 10 22H2. API-seitig läuft 2.x auf 19045, formal ist es eine Grauzone — Windows 10 22H2 ist selbst ausserhalb des Microsoft-Supports. **Vor Freigabe ein echter Smoke-Test auf Windows 10 19045**, mit Toast samt Buttons und dem Laden der nativen DLL-Kette. Als Testfall T35/T36 in `test-matrix.md` aufgenommen.
- **Zusätzlich zu prüfen (WindowsAppSDK#6268, offen):** packaged WinUI-3-Apps stürzen unter Windows 10 (17763, 19044) beim Start **als Administrator** ab; unpackaged ist nicht betroffen. Getestet mit 1.8.5, Status für 2.x unklar. Als T37 aufgenommen und in AP2.4 zu berücksichtigen.

---

## ADR-009 — Outlook über COM-Interop statt Microsoft Graph

**Datum:** 04.09.2026 · **Status:** angenommen · **Bezug:** §8.4, §16.6

**Kontext.** §8.4 gibt vor: „erst Microsoft Graph über den angemeldeten Benutzer, wenn ein Entra-App-Consent vorliegt; Rückfallebene ist COM-Interop gegen ein lokal laufendes Outlook. Graph ist die Vorgabe, COM nur wenn Graph nicht verfügbar ist."

**Entscheidung von Dominic am 04.09.2026: COM-Interop wird der Standard.** Graph wird nicht gebaut.

**Begründung.** Graph verlangt eine Entra-App-Registrierung samt Consent-Prozess. Der Aufwand steht in keinem Verhältnis zum Nutzen, solange nipp intern läuft und Outlook auf den Arbeitsplätzen ohnehin gestartet ist.

**Konsequenz.**
- §8.4 ist an dieser Stelle überholt; in `NIPP-BUILD.md` Rev. 4 nachgezogen.
- `OutlookContactSource` spricht COM. Der Zugriff bleibt hinter einer Abstraktion, damit Graph später nachgerüstet werden kann, ohne die Kontaktverwaltung anzufassen — vorgebaut wird er aber nicht (§2).
- **Drei Dinge, die COM mitbringt und die in P6 behandelt werden müssen**, weil sie sonst beim Kunden auffallen:
  1. **Outlook muss laufen.** Ist es geschlossen, gibt es keine Kontakte. Das braucht eine verständliche Anzeige statt einer leeren Liste — nach §15 mit Grund und Abhilfe.
  2. **COM ist synchron und langsam.** §8.4 verlangt ausdrücklich „nie synchron im UI-Thread laden"; mit dem 20-ms-Iterate-Timer auf demselben Thread (§6, §14.1) ist das doppelt zwingend. Der Abruf läuft auf einem Worker, das Ergebnis kommt über den Dispatcher zurück.
  3. **Terminalserver und mehrere Outlook-Profile** sind mit COM heikel. Wenn nipp dort eingesetzt werden soll, ist das vor P6 zu klären.
- Der Cache von 12 Stunden aus §8.4 bleibt — bei COM wird er wichtiger, nicht unwichtiger.

---

## ADR-008 — Packaging: MSIX (packaged). Das M1-Gate ist vollständig bestanden

**Datum:** 04.09.2026 · **Status:** angenommen, **mit einem offenen Punkt seit 07.09.2026** · **Löst ADR-004 ab** · **Bezug:** §4, §12 (M1), §14.3

> **Nachtrag 07.09.2026 — der Titel stimmt, der Alltag läuft trotzdem
> unpackaged.**
>
> **Der Befund:** `AppNotificationManager.Register()` scheiterte packaged mit
> `0x80004005 (E_FAIL)`. Ein eingehender Anruf gab bei geschlossenem Fenster
> **kein Zeichen** — und weil nipp im Infobereich lebt (§10), ist das der
> Normalfall. Unpackaged legt `Register()` die COM-Einträge selbst an und
> funktioniert. Deshalb läuft der Alltag unpackaged; das war eine Folge, keine
> Wahl.
>
> **Aufgefallen ist es, weil ein Wechsel des Startwegs wie ein Fehler im Code
> aussah:** ab dem ersten `shell:appsFolder`-Start stand „Benachrichtigungen
> nicht verfügbar" im Protokoll. Wer einen Rückschritt sucht, vergleicht
> deshalb zuerst, **wie** gestartet wurde.
>
> **Behoben, aber nicht fertig — der Stand vom 07.09.2026 abends, gemessen:**
>
> Im Manifest fehlten **zwei** Erweiterungen, die zusammengehören und dieselbe
> CLSID tragen müssen: ein `com:ComServer` mit einer `com:Class` und
> `desktop:Extension Category="windows.toastNotificationActivation"`. Beide
> sind jetzt eingetragen (`Package.appxmanifest`, GUID
> `2599E06F-CCE5-4248-B039-F2D88FCC2898` — er muss **stabil** bleiben, Windows
> merkt sich die Zuordnung).
>
> **Was das gebracht hat, belegt in zwei Schritten:**
>
> | Beobachtung | vorher | nachher |
> |---|---|---|
> | `AppNotificationManager.Register()` | `0x80004005` (E_FAIL) | `0x80070490` (ERROR_NOT_FOUND) |
> | Benachrichtigungsplattform (Ereignis 2413) | kein Eintrag | „Eine Anwendung wurde registriert … **Der Vorgang wurde erfolgreich beendet**" |
>
> Die Erweiterungen waren also **nötig und nicht ausreichend**. Die Plattform
> nimmt nipp jetzt an; `Register()` selbst wirft weiter. Die verbleibende
> Ursache liegt innerhalb des Aufrufs und ist eine eigene Untersuchung wert —
> geprüft und ausgeschlossen sind bereits: fehlende Namensräume (die CLSID
> steht im registrierten Manifest am Installationsort), ein veralteter
> Registrierungs-Zwischenspeicher (Paket entfernt und zweimal frisch
> registriert), und eine fehlende CLSID in HKCU/HKLM (bei MSIX erwartbar, die
> Registrierung ist virtualisiert — also kein Hinweis in eine Richtung).
>
> **Warum die Manifest-Änderung trotzdem bleibt:** sie ist durch den
> wandernden Fehlercode als notwendig belegt, sie ist Voraussetzung für jede
> weitere Untersuchung, und unpackaged kann sie nicht schaden — dort wird das
> Manifest nicht gelesen. Nach der Änderung packaged **und** unpackaged
> gestartet; unpackaged meldet unverändert „Benachrichtigungen angemeldet".
>
> **Offen und auf der Testmatrix als T110:** packaged mit funktionierendem
> Toast. Bis dahin gilt für die Auslieferung: **unpackaged**, weil §8.6 ohne
> Toast nicht erfüllt ist und ein Softphone, das einen eingehenden Anruf nicht
> anzeigt, seinen Zweck verfehlt.

**Kontext.** §4 verlangt die Packaging-Entscheidung in M1, weil MSIX und native Bibliotheken eine klassische Bruchstelle sind, und §12 macht sie zum Akzeptanzkriterium. Der Doppeltest aus AP2.4 ist jetzt durchgeführt.

**Ergebnis, beides am 04.09.2026 verifiziert:**

| Variante | Ergebnis |
|---|---|
| **unpackaged** (`WindowsPackageType=None`, EXE direkt) | **läuft.** Log: `Linphone SDK geladen: Version 5.5.0, 8 Grammatiken, 2 Plugins` |
| **packaged** (MSIX registriert, Start über `shell:appsFolder\bv2.nipp_b06ws4ca5ehbg!App`) | **läuft.** Fenster erscheint, dieselbe Logzeile |

**Entscheidung: MSIX (packaged).** Das ist die in §4 bevorzugte Variante, und sie funktioniert — es gibt keinen Grund für die Rückfallebene. Damit bleiben alle Windows-Integrationen aus §10 auf dem einfachen Weg:

- `AppNotificationManager` für die Toasts nach §8.6, statt einer eigenen Toast-Lösung (die +2 bis 3 Tage gekostet hätte)
- Protokoll-Handler `tel:`, `sip:`, `callto:` über das Manifest, statt über HKCU
- Autostart über `StartupTask`, statt über die Registry
- Firewall-Capability im Manifest

**Was den Fehler aus ADR-004 verursacht hat.** Nicht das Versionsgeflecht im NuGet und nicht eine fehlende Framework-Referenz — beides war korrekt. `dotnet run` startete die App **ohne wirksame Paketidentität**, und ohne Identität sind die WinRT-Klassen des DeploymentManagers nicht aktivierbar; genau das meldet `REGDB_E_CLASSNOTREG`. Der Test war falsch, nicht die Konfiguration.

**Lehre für die Zukunft:** eine packaged App wird über ihre Paketidentität gestartet (`Add-AppxPackage -Register` und dann `shell:appsFolder\<PackageFamilyName>!App`), nicht durch Aufruf der EXE und nicht über `dotnet run`. In `docs/packaging.md` als Prüfschritt festgehalten.

**Voraussetzung auf jeder Entwicklungsmaschine:** der Entwicklermodus (`AllowDevelopmentWithoutDevLicense = 1` unter `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock`). Ohne ihn scheitert die Registrierung unsignierter Pakete mit `0x80073CFF` — Sideloading allein genügt nicht. Am 04.09.2026 auf dieser Maschine gesetzt.

**Konsequenz für P7 und P9.**
- P7 kann ohne Umwege gebaut werden; die Aufwandsschätzung von 3–5 Tagen bleibt.
- `build/Linphone.Sdk.targets` bringt die native Kette über `AppxPackagePayload` ins Paket — verifiziert: `liblinphone.dll`, `mediastreamer2.dll`, `lib/mediastreamer/plugins/libmswasapi.dll` und 8 von 8 belr-Grammatiken liegen im registrierten Layout.
- **Der Fallstrick aus §14.3 ist damit umgangen, aber nicht verschwunden:** er kommt zurück, sobald jemand eine DLL hinzufügt, ohne die Targets-Datei zu pflegen. Deshalb steht die Prüfliste in `packaging.md`.
- Für P9 bleibt die Signatur offen (§16.2). Ein selbstsigniertes Paket genügt zum Entwickeln, nicht zum Verteilen.

---

## ADR-007 — Medienverschlüsselung: SRTP anbieten, aber nicht erzwingen

**Datum:** 04.09.2026 · **Status:** angenommen · **Bezug:** §9.3, §14.7 · **Löst Punkt 1 aus ADR-006**

**Kontext.** §9.3 gibt als Auslieferungszustand vor: Medienverschlüsselung **SRTP** und „Verschlüsselung erzwingen: **ein**". Der erste Testanruf in AP2.3 verhandelte trotz gesetztem SRTP `None`, weil die Gegenstelle keines anbot.

**Auskunft von Dominic am 04.09.2026: die aktuelle Telefonanlage von bv2 hat noch keine Verschlüsselung.**

**Entscheidung.** Der Auslieferungszustand wird:

| Feld | §9.3 verlangt | Neu | Begründung |
|---|---|---|---|
| Medienverschlüsselung | SRTP | **SRTP** (unverändert) | wird angeboten; sobald die Anlage es kann, greift es ohne Änderung am Client |
| Verschlüsselung erzwingen | **ein** | **aus** | mit „ein" wäre heute **kein einziges Gespräch** möglich |

Das ist bewusst *opportunistische* Verschlüsselung: nipp bietet SRTP an und benutzt es, wenn die Gegenseite mitkommt, und telefoniert sonst unverschlüsselt weiter. Der Schalter bleibt vorhanden und bedienbar — er steht nur nicht auf „ein".

**Konsequenz.**
- §9.3 ist an dieser Stelle überholt. In `NIPP-BUILD.md` Rev. 3 nachgezogen, mit Verweis auf diese ADR.
- **Der Zustand muss im Gespräch sichtbar sein.** §8.2 verlangt ohnehin einen Chip für die Verschlüsselung; er wird jetzt tragend, weil unverschlüsselte Gespräche der Regelfall sind. Die Tokens dafür stehen bereits (`EncryptionSecureBrush`, `EncryptionInsecureBrush`) — und nach §8.4 nie nur über Farbe, sondern mit Text.
- **Kein stilles Zurückfallen.** Wenn ein Benutzer „erzwingen" später einschaltet und ein Gespräch deshalb scheitert, muss die Meldung den Grund nennen (§14.7, §15). Der Fehlermeldungs-Katalog aus AP3.7 braucht diesen Fall.
- Wenn die Anlage Verschlüsselung bekommt, ist die Umstellung eine Zeile im Provisioning-Profil (§11) — kein neues Release. Genau dafür ist das Profil da.
- **Nicht als Sicherheitsversprechen dokumentieren.** In der Installationsanleitung (P9) gehört ein klarer Satz dazu, dass Gespräche gegen die aktuelle Anlage unverschlüsselt laufen. Ein Client, der einen SRTP-Schalter zeigt, weckt sonst eine Erwartung, die die Anlage nicht erfüllt.

---

## ADR-006 — Drei Befunde aus dem ersten echten Gespräch (AP2.3)

**Datum:** 04.09.2026 · **Status:** **Punkt 1 und 2 erledigt, Punkt 3 offen** (nachgetragen am 07.09.2026) · **Bezug:** §9.3, §9.4, §14.7

> **Nachtrag 07.09.2026 — wo die drei Punkte stehen.**
>
> **Punkt 1 (SRTP erzwingen): erledigt durch ADR-007.** Der trägt „Löst Punkt 1
> aus ADR-006" in seiner Kopfzeile. Ihre Auskunft vom 04.09.2026 — die Anlage
> hat noch keine Verschlüsselung — hat entschieden: SRTP wird angeboten, nicht
> erzwungen. Der Schalter (`NatMedia.EncryptionMandatory`) steht auf aus, und
> `SettingsApplier` protokolliert eine Warnung, wenn ihn jemand einschaltet.
>
> Die Frage, die hier gestellt war — *bietet `pbx.example.ch` SRTP für interne
> Gespräche an?* — ist damit **nicht** beantwortet, aber auch nicht mehr
> blockierend: die Entscheidung ging den sicheren Weg. Sie wird wieder
> interessant, wenn die Anlage Verschlüsselung bekommt; dann ist es laut
> ADR-007 eine Zeile im Provisionierungsprofil.
>
> **Punkt 2 (Echounterdrückung bei 8 kHz): erledigt, und zwar auf dem zweiten
> der beiden angebotenen Wege.** Gewählt wurde nicht „ein Filter, der 8 kHz
> beherrscht", sondern die ehrliche Anzeige. Es gibt
> `ISipService.IsEchoCancellationEffective`, das ViewModel gibt es als
> `IsEchoEffective` weiter, und in den Einstellungen steht neben dem Schalter:
>
> > „Bei Gesprächen mit 8-kHz-Codecs (PCMU, PCMA — also bei den meisten
> > externen Anrufen) schaltet sich die Echounterdrückung selbst ab. Das ist
> > eine Eigenschaft des SDK, keine Fehlfunktion."
>
> Das ist genau, was dieser Punkt verlangte: *„eine Zusage, die bei jedem
> Externgespräch nicht gilt, ist schlechter als eine ehrliche Anzeige."*
> AP5.7 ist abgeschlossen.
>
> **Punkt 3 (Jitter 499 ms, WASAPI-Pufferfehler): offen, und er bleibt es.**
> Als **T38** in der Testmatrix, dort mit dem Vermerk „offen — unter Emulation
> 499 ms Jitter, verworfene RTP-Pakete, Pufferfehler". Diese ADR sagt selbst,
> was zuerst zu tun ist: auf echter x64-Hardware gegenprüfen, **bevor** daraus
> eine Fehlersuche im Code wird. Mit der Entscheidung in ADR-001 (07.09.2026:
> keine x64-Maschine auf unbestimmte Zeit) ist das **auf unbestimmte Zeit
> vertagt** — bewusst, mit demselben Fälligkeitsdatum: vor der ersten
> Kundenabgabe.

**Kontext.** AP2.3 ist erfolgreich: REGISTER 200 OK gegen den Test-Trunk über UDP, ausgehender Anruf zu einer externen Mobilnummer, `StreamsRunning`, Audio in beide Richtungen von Dominic bestätigt, Anruf sauber beendet. **M1 ist damit erfüllt.** Im SDK-Log stehen aber drei Dinge, die nicht zu §9 passen und vor P4/P5 entschieden werden müssen.

### 1. Die Medienverschlüsselung war `None`, obwohl SRTP gesetzt war

Der Spike setzt `core.MediaEncryption = MediaEncryption.SRTP` (SRTP ist laut Core unterstützt). Verhandelt wurde trotzdem:

```
Verhandelt: Codec PCMU, Verschlüsselung None
```

Die Gegenstelle hat SRTP nicht angeboten, und weil das Mandatory-Flag nicht gesetzt war, fiel das SDK stillschweigend auf unverschlüsselt zurück.

**Das ist genau der Fall aus §14.7, nur von der anderen Seite.** §9.3 gibt vor: Medienverschlüsselung SRTP **und** „Verschlüsselung erzwingen: ein". Mit dieser Vorgabe wäre dieser Anruf **hart gescheitert** — ein Anruf ins Mobilnetz über den Trunk.

**Zu klären mit Dominic:** Bietet `pbx.example.ch` SRTP für interne Gespräche an, und ist der unverschlüsselte Weg nur bei externen Zielen unvermeidlich? Davon hängt ab, ob „erzwingen" als Standard tragbar ist oder ob es pro Konto konfigurierbar sein muss. **Solange das offen ist, darf „erzwingen" nicht unbesehen als Standard ausgeliefert werden** — sonst telefoniert niemand nach draussen und der Client gilt als kaputt, genau wie §14.7 warnt.

### 2. Die Echounterdrückung war während des Gesprächs abgeschaltet

```
mediastreamer-warning: Echo canceller does not support sampling rate 8000Hz,
                       so it has been disabled
```

Verhandelt wurde **PCMU** (8 kHz) — bei externen Anrufen über den Trunk der Normalfall. Der Echo-Canceller unterstützt diese Rate nicht und schaltet sich selbst ab. §9.4 führt „Echounterdrückung: ein" als Standard, und `EchoCancellationEnabled` war auch `true` — sie war trotzdem wirkungslos.

**Folge:** Bei jedem Gespräch mit 8-kHz-Codec gibt es keine Echounterdrückung. Für ein Softphone, das im Alltag über Lautsprecher benutzt wird, ist das erheblich. Die Einstellung aus §9.4 verspricht dann etwas, das nicht eintritt.

**Zu prüfen in P5 (AP5.7):** ob `EchoCancellerFilterName` (derzeit leer) einen Filter zulässt, der 8 kHz beherrscht, oder ob die Anzeige in der Oberfläche den tatsächlichen Zustand melden muss statt nur den Wunsch. Notfalls gehört ein Hinweis in die Einstellungen — eine Zusage, die bei jedem Externgespräch nicht gilt, ist schlechter als eine ehrliche Anzeige.

### 3. Jitter von 499 ms, verworfene RTP-Pakete und WASAPI-Pufferfehler

Gemessen im laufenden Gespräch: RTT 23 ms (gut), Verlust 0,00 % (gut), Download 80 kbit/s (plausibel für PCMU) — aber **Jitter 499,5 ms**. Dazu im Log:

```
ortp-error: rtp_parse: discarding too old packet (seq=…)          [~35x]
ortp-error: Jitter buffer stays unconverged for one second, reset it.
bctbx-error: mswasapi: Could not get buffer from the MSWASAPI audio output
             interface 960 [0x88890006]
bctbx-error: mswasapi: cannot write output buffer of 960 samples,
             not enough space [960=9600-8640]
bctbx-warning: mswasapi: changing output rate to 8000 Hz is not supported
               by the device. Keep 48000 Hz
```

Das Gerät kann kein 8 kHz und bleibt bei 48 kHz Stereo — es wird also durchgehend resampled. Zusammen mit den Pufferfehlern und dem unkonvergierten Jitter-Puffer ergibt das ein Bild, das **auf die x64-Emulation auf ARM64 deutet** (ADR-001), nicht auf einen Defekt im Code: Audio-Timing ist genau der Bereich, in dem Emulation auffällt.

Hörbar war das Gespräch trotzdem.

**Konsequenz:** Dieser Punkt ist auf echter x64-Hardware gegenzuprüfen, **bevor** daraus eine Fehlersuche im Code wird. Als Testfall T38 in `test-matrix.md` aufgenommen. Sollten die Werte dort ebenso aussehen, ist es ein echtes Thema für P4 (AP4.8) und die nichtfunktionalen Ziele aus §2.

### Nebenbefunde ohne Handlungsbedarf

- `LinphoneCore has video disabled … but video policy is to start the call with video` — `VideoCaptureEnabled`/`VideoDisplayEnabled` allein genügen nicht, die `VideoActivationPolicy` muss mit. Im Spike korrigiert, gehört in AP3.2.
- `Registrierung -> Failed (Unauthorized)` beim Herunterfahren — Folge eines Reihenfolgefehlers im Spike: `ClearAllAuthInfo()` vor `Stop()` entzieht dem abschliessenden REGISTER (Expires=0) die Zugangsdaten. Korrigiert.
- `bctbx_file_open: Error opening '…\/.linphone.ecstate'` — doppelter Trennzeichen im Pfad, weil das SDK an ein Verzeichnis mit Backslash-Ende noch einen Slash hängt. Harmlos, aber ein Hinweis: Verzeichnispfade **ohne** abschliessendes Trennzeichen übergeben.
- `Unable to subscribe to the conference event package (RFC 4575)` und `[LIME] No identity key available` — Chatroom- und Verschlüsselungsfunktionen für Nachrichten, beide laut §2 ausgeschlossen. Kein Handlungsbedarf.
- **Verhandelt wurde PCMU, nicht Opus**, obwohl Opus aktiv und erstrangig ist. Bei einem Ziel im Mobilnetz erwartbar. Bestätigt §14.10: die Codec-Reihenfolge ist im SIP-Log zu prüfen, nicht am UI-Zustand.

---

## ADR-005 — Zurück zu Weg C (Prebuilt-ZIP): der NuGet-Feed trägt keinen Build

**Datum:** 04.09.2026 · **Status:** angenommen · **Löst ab:** ADR-002 · **Bezug:** §5

**Kontext.** ADR-002 hatte Weg A (NuGet) zum Standard gemacht, weil der Feed anonym lesbar ist und `LinphoneSDK.Windows` 5.5.18 stable führt. Beides stimmt weiterhin. Die ADR enthielt aber schon den Vorbehalt, dass die GitLab-Verbindung unzuverlässig war — nur mit Wiederholungen kamen die Abfragen durch.

**Was passiert ist.** Der erste echte `dotnet restore` gegen den Feed ist nach **1.76 Minuten** gescheitert:

```
Ein Verbindungsversuch ist fehlgeschlagen ... (gitlab.linphone.org:443)
"FindPackagesByIdAsync" wird für die Quelle ... wiederholt.      [3x]
Response status code does not indicate success: 500 (Internal Server Error).
error NU1301: Fehler beim Abrufen von Informationen zu "LinphoneSDK.Windows"
```

NuGet hat dreimal wiederholt und dann aufgegeben — erst Timeouts, am Ende ein HTTP 500 vom Server. Das ist nicht ein einzelner Aussetzer, sondern das Muster, das schon bei der Recherche und bei den Metadatenabfragen auftrat.

**Entscheidung.** **Weg C** (offizielles Prebuilt-ZIP `linphone-sdk-win64-5.5.18.zip` von `download.linphone.org`) wird der Standard. Der Host war bei jeder Prüfung stabil erreichbar. Das ZIP wird einmal geholt, liegt lokal unter `sdk/` (gitignoriert, nicht eingecheckt) und der Build hängt danach an keiner fremden Verfügbarkeit mehr.

**Konsequenz.**
- Der Feed bleibt in `nuget.config` eingetragen, aber nichts hängt mehr an ihm. Wer ihn nutzen will, kann — verlassen soll sich darauf niemand.
- **Reproduzierbarkeit** wird damit zur Handarbeit: Version, SHA256 und Bezugsdatum gehören nach `sdk-setup.md`, sonst weiss in sechs Monaten niemand, welcher Stand im Build war. Das ersetzt, was der NuGet-Restore automatisch geleistet hätte.
- Versionswechsel sind kein Zeilenwechsel mehr in der `.csproj`, sondern ein neuer Download plus Prüfsumme. Bei einem SDK, das laut §5 ohnehin nur bewusst angehoben wird, ist das vertretbar.
- **Nebeneffekt, der für uns spricht:** das ZIP-Layout zeigt die Plugin-Struktur (`lib/mediastreamer/plugins/`) offen, statt sie hinter einem NuGet-Targets-Mechanismus zu verstecken. Für den Fallstrick aus §14.2 ist das ein Vorteil, kein Nachteil — das Kopierskript wird explizit statt implizit.
- Für ein späteres CI heisst das: das ZIP gehört in einen eigenen, kontrollierten Speicher (Artefakt-Cache oder Paketablage von bv2), nicht bei jedem Lauf frisch von `download.linphone.org`.

---

## ADR-002 — Weg A (NuGet) statt Weg C (Prebuilt-ZIP) für die SDK-Beschaffung

**Datum:** 04.09.2026 · **Status:** **abgelöst durch ADR-005** (am selben Tag, nach dem ersten echten Restore) · **Bezug:** §5

**Kontext.** `NIPP-BUILD.md` Rev. 2 empfahl Weg C (Prebuilt-ZIP) als Standard für M1, weil die Erreichbarkeit der GitLab-NuGet-Registry bei der Recherche nicht verifizierbar war und das M1-Gate nicht an einer Registry-Frage hängen sollte.

**Prüfung am 04.09.2026 von der Entwicklungsmaschine aus.** Der Feed `https://gitlab.linphone.org/api/v4/projects/411/packages/nuget/index.json` antwortet mit HTTP 200 und ist **anonym lesbar**, ohne Deploy-Token. Er liefert einen gültigen NuGet-Service-Index v3.0.0. Enthaltene Pakete: `LinphoneSDK`, `LinphoneSDK.Windows`, `LinphoneSDK.Xamarin`. `LinphoneSDK.Dotnet` existiert nicht (404) — die Angabe in §5 ist damit bestätigt. `LinphoneSDK.Windows` führt 300 Versionen, davon 59 stabil, **einschliesslich `5.5.18`** — genau der in §5 vorgegebenen Version.

**Entscheidung.** Weg A wird der Standard. `nuget.config` ist entsprechend eingerichtet, mit `packageSourceMapping`, sodass `LinphoneSDK*` ausschliesslich aus dem Linphone-Feed kommt und nicht versehentlich das veraltete Paket von nuget.org (3.12.0.273, 2017) gezogen wird.

**Konsequenz.**
- Versionswechsel sind ein Zeilenwechsel in der `.csproj` statt ein manueller ZIP-Tausch.
- Weg C bleibt dokumentierte Rückfallebene, inhaltlich verifiziert, ohne Aufwand.
- **Vorbehalt:** die GitLab-Verbindung war unzuverlässig — mehrere Abrufe liefen in Timeouts, erst Wiederholungen kamen durch. NuGet-Restore kann sporadisch scheitern. Wenn das den Alltag stört, ist Weg C der ruhigere Weg; diese ADR ist dann zu überarbeiten.
- **Offen:** der `.nuspec`-Endpunkt der Registry antwortet mit 404, der Paketinhalt ist erst beim Restore in P2 einsehbar. Der Moniker-Widerspruch aus §5 (`win` vs. `netcore45`) bleibt bis dahin ungeklärt.

---

## ADR-001 — ARM64-Entwicklungsmaschine für ein x64-Produkt

**Datum:** 04.09.2026 · **Status:** **angenommen — Option 3, als bewusst getragenes Risiko** (entschieden am 07.09.2026) · **Bezug:** §2, §4, §14.4

> **Entscheidung vom 07.09.2026: es steht auf unbestimmte Zeit keine
> x64-Maschine zur Verfügung.**
>
> Damit ist **Option 3** gewählt — „alles hier, inklusive Verifikation" —, und
> das ist bewusst die Option, die diese ADR am 04.09.2026 am schwächsten
> bewertet hat. Die Empfehlung lautete Option 1; sie ist nicht abgelehnt
> worden, sondern **nicht durchführbar**, und der Unterschied gehört
> hingeschrieben: Option 1 stand drei Tage als Absichtserklärung da, während
> faktisch Option 3 gelebt wurde. Eine Empfehlung, die niemand einlösen kann,
> verschleiert den Zustand.
>
> **Was damit ausdrücklich als ungemessen gilt.** Nicht „vermutlich in
> Ordnung", sondern ungemessen:
>
> | Was | Stand |
> |---|---|
> | **Die nichtfunktionalen Ziele aus §2** — Kaltstart < 3 s, < 180 MB, < 6 % CPU, INVITE→Toast < 400 ms | **ungemessen.** Der einzige Wert, der vorliegt, sind 247 MB aus einem Debug-Build unter Emulation (AP7.8, T38) |
> | **Das M1-Gate** | unter Emulation abgenommen. ADR-006 nennt es erfüllt, diese ADR bestreitet seine Beweiskraft — beides steht, und der Widerspruch ist jetzt benannt statt aufgelöst |
> | **ADR-006 Punkt 3** — 499 ms Jitter, verworfene RTP-Pakete, WASAPI-Pufferfehler | **Verdacht, unbestätigt.** Er deutet auf die Emulation, ist aber nie gegengeprüft (T38) |
> | **AP9.5** — Testmatrix auf frischem Win 11 und Win 10 22H2 | offen |
>
> **Wann das fällig wird: vor der ersten Kundenabgabe.** Ein Softphone, dessen
> Audio-Timing nie auf der Zielplattform gemessen wurde, ist kein
> Auslieferungszustand — und die vier Zielwerte aus §2 sind Zusagen, nicht
> Wünsche. Bis dahin ist das Risiko getragen, nicht verschwunden.
>
> **Was jetzt hilft und nichts kostet:** T38 und AP7.8 sind so beschrieben,
> dass sie an einem einzigen Nachmittag auf einer geliehenen x64-Maschine
> abzuarbeiten sind. Wer eine in die Hand bekommt — ein Kundengerät bei einer
> Installation, ein Testrechner —, sollte sie dafür benutzen. Es braucht kein
> eigenes Gerät, nur einmal Zugang.
>
> **Was diese Entscheidung nicht ist:** eine Aussage darüber, dass nipp auf x64
> schlechter läuft. Emulation fällt bei Audio-Timing am ehesten auf; die
> Befunde aus ADR-006 Punkt 3 sind genau dort. Es ist gut möglich, dass auf
> echter Hardware nichts davon auftritt — nur weiss es niemand.

**Kontext.** `NIPP-BUILD.md` §4 legt x64 only fest und schliesst ARM64 als Ziel aus. Das ist richtig: das linphone-sdk liefert für Windows ausschliesslich `win64`-Binaries. Die vorgefundene Entwicklungsmaschine ist jedoch **ARM64** (Windows 11 Build 26200); ausserdem fehlen .NET SDK und Visual Studio vollständig. Befund in `docs/environment.md`.

**Zur Entscheidung stehende Optionen.**

1. *Hier entwickeln, auf x64-Hardware verifizieren.* Code und Build hier, aber das M1-Gate (P2) und die Messungen (AP7.8) auf einer echten x64-Maschine.
2. *Vollständig auf x64-Hardware umziehen.* Sauberste Variante, braucht die Maschine.
3. *Alles hier, inklusive Verifikation.* Billigste Variante, aber das wichtigste Gate des Projekts würde auf der falschen Plattform abgenommen.

**Bewertung.** x64 läuft auf ARM64-Windows emuliert, Bauen und Telefonieren sind also möglich. Zwei Dinge werden dadurch aber unzuverlässig:

- Die nichtfunktionalen Zielwerte aus §2 (Kaltstart < 3 s, < 180 MB, < 6 % CPU, INVITE→Toast < 400 ms) sind unter Emulation nicht aussagekräftig.
- Das M1-Gate verliert Beweiskraft: ein Fehlschlag unter Emulation beweist nicht, dass es auf echter x64-Hardware scheitert — und ein Erfolg nicht das Gegenteil. WASAPI-Audio in einem emulierten Prozess ist der unsicherste Punkt.

**Empfehlung.** Option 1.

**Konsequenz, sobald entschieden:** hier nachtragen und `docs/environment.md` sowie `CLAUDE.md` anpassen.
