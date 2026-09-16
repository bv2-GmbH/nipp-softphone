# Die Shell im Gespräch — und eine Nummer, die erst nach dem Neustart erschien

**Angelegt am 16.09.2026.** Zwei Aufträge von Dominic, die nichts miteinander
zu tun haben ausser der Datei, in der sie landen:

- **A** — eine neu eingetragene Mobilnummer erscheint in den Kontakten erst
  nach einem Neustart von nipp.
- **B** — im breiten Layout soll ein laufendes Gespräch **nur die linke
  Spalte** füllen; rechts sollen die Nebenstellen mit ihren Lampen stehen
  bleiben und anklickbar sein.

Bezug: NIPP-BUILD.md §8.2 (Gesprächsansicht), §8.4 (Kontakte), §23 (breites
Layout); ADR-042 (Gruppe und Reihenfolge), ADR-047 und ADR-052 (das breite
Layout), ADR-060 (die Gegenprobe), `docs/plans/BREITBILD-PLAN.md`.

---

# Teil A — Die Nummer, die nicht erscheint

## Die Ursache steht fest, sie ist eine Zeile

`ShellViewModel.RefreshTeamContacts` baut die Team-Zeilen neu, ersetzt die
Sammlung aber nur, wenn `SameContacts` einen Unterschied sieht. Und
`SameContacts` vergleicht **genau zwei Dinge**:

```csharp
if (!string.Equals(current[i].Contact.Id, wanted[i].Contact.Id, …))      return false;
if (!string.Equals(current[i].Contact.Group, wanted[i].Contact.Group, …)) return false;
```

Die Kennung ist `TeamContactSource.IdOf` — `team:{zähler}:{kurzwahl}`. **Eine
hinzugefügte Mobilnummer ändert weder die Kennung noch die Gruppe.** Der
Vergleich meldet «gleich», `Replace` wird übersprungen, die alte Zeile bleibt
stehen. Erst ein Neustart baut die Liste von Grund auf.

**Der Mechanismus dahinter ist in Ordnung und tut genau das, wofür er gebaut
wurde:** `OnSettingsChanged` ruft `ReloadTeam()` und `RefreshTeamContacts()`,
der `ContactStore` liest die Einstellungen frisch. Der Fehler sitzt allein in
der Bremse davor.

## Was ausserdem betroffen ist — und nie gemeldet wurde

Dieselbe Lücke verschluckt **jede** Änderung, die nicht Kurzwahl oder Gruppe
ist:

| Geändert | Wird sichtbar? | Warum |
|---|---|---|
| Kurzwahl | **ja** | steckt in der Kennung |
| Gruppe | **ja** | wird ausdrücklich verglichen |
| Reihenfolge | **ja** | die Kennungen stehen dann anders |
| **Mobilnummer** | **nein** | der gemeldete Fall |
| **Name** | **nein** | nie gemeldet, gleiche Ursache |
| **SIP-Adresse** | **nein** | und daran hängt die **Lampe** |

Der letzte Fall ist der unangenehmste: wer die SIP-Adresse einer Nebenstelle
korrigiert, sieht die Zeile unverändert **und** bekommt weiter keine Präsenz —
ohne jeden Hinweis, dass die Korrektur nicht angekommen ist.

## Warum es der dritte Anlauf ist, und was das heisst

Der Vergleich stand ursprünglich nur auf der Kennung. ADR-042 hat die **Gruppe**
nachgetragen, weil sonst ein Zug über die Gruppengrenze stumm blieb. Jetzt
fehlt der Inhalt. **Ein Vergleich, der Feld für Feld nachgerüstet wird, ist
beim nächsten Feld wieder zu grob** — und das ist genau die Lehre aus ADR-060:
*die Gegenprobe ist die wichtigere Hälfte, eine zu grobe Bremse verschluckt
den echten Wechsel.*

**Also nicht ein drittes Feld anhängen, sondern den angezeigten Inhalt
vergleichen.**

## A1 — Der Vergleich

Verglichen wird, was die Zeile **zeigt**: Kennung, Gruppe, Anzeigename,
SIP-Adresse und die **Folge der Nummern** (Nummer und Art, in Reihenfolge).

**Zwei Fallen, beide vermeidbar:**

1. **Nicht `record`-Gleichheit nehmen.** `Contact` ist ein `sealed record`, aber
   `Numbers` ist eine `IReadOnlyList<ContactNumber>` — die vergleicht sich nach
   **Referenz**. Zwei frisch gebaute, inhaltlich gleiche Kontakte wären damit
   immer «ungleich», und die Liste würde bei jeder Einstellungsänderung neu
   aufgebaut. Das kostet Auswahl, Bildlauf und trifft einen laufenden
   Ziehvorgang.
2. **Die Präsenz gehört nicht hinein.** Verglichen wird `ContactRow.Contact`,
   nie die Zeile selbst: der Lampenzustand ändert sich im Sekundentakt, und
   ein Vergleich, der ihn einschliesst, baut die Liste bei jeder BLF-Meldung
   neu. Genau dafür gibt es `ContactRow` (siehe `UpdatePresence`).

## A2 — Die Tests

Im Kern, ohne Oberfläche — `ShellViewModel` ist testbar:

- Mobilnummer zu einer bestehenden Nebenstelle hinzufügen → die Zeile trägt sie.
- Name ändern → die Zeile trägt ihn.
- SIP-Adresse ändern → die Zeile trägt sie **und die Lampe hängt an der neuen**.
- **Die Gegenprobe:** eine Einstellungsänderung, die das Team nicht betrifft
  (etwa der Klingelton), lässt die Sammlung **unangetastet** — sonst ist die
  Bremse weg statt verfeinert.
- Eine BLF-Meldung ersetzt die Sammlung nicht.

**Die Gegenprobe ist hier die wichtigere Hälfte.** Ein Test, der nur zeigt,
dass die Nummer jetzt erscheint, bestünde auch, wenn jemand `SameContacts`
einfach auf `return false` setzt.

## A3 — Am Gerät

Neue Zeile **T311**: Nebenstelle in den Einstellungen um eine Mobilnummer
ergänzen, zurück zu den Kontakten, Zeile aufklappen — die Nummer steht da,
ohne Neustart. Rüstzeug `S`, gehört damit in die Schreibtisch-Runde A1 des
`BEWEIS-PLAN`.

---

# Teil B — Das Gespräch in der linken Spalte

> **Nachtrag 16.09.2026, 23:22 — zum ersten Mal am Fenster gesehen, und es
> traegt.** Breites Fenster, ausgehender Anruf: die Gespraechsansicht stand
> links, die Kacheln blieben rechts, nach dem Auflegen kam die linke Spalte
> zurueck. **Keine Ausnahme, kein Absturz, keine Zeile `[ERR] Nipp.App`.**
>
> **Was damit NICHT geprueft ist — und das ist die Haelfte, auf die es
> ankommt.** Der Anruf ging `Dialing → Ringing → Ended` in 2,6 Sekunden;
> **`Connected` gab es nie**, und `StreamsRunning` auch nicht. Geprueft ist
> also der **Aufbau und Abbau** der eingebetteten Ansicht, nicht das laufende
> Gespraech. `HasActiveCall` steht schon beim Waehlen auf wahr (`calls.Count >
> 0`), deshalb sah es richtig aus.
>
> **Das ist woertlich die Lehre aus T78** (`docs/lehren.md`): *ein Rufzustand,
> den es nie gab, kann nicht abgenommen werden.* Dort galt der Rufton als
> bestanden, obwohl die geprueften Anrufe nie `Ringing` erreichten; hier waere
> es spiegelbildlich. **T312 bleibt offen**, und T313 bis T317 sind gar nicht
> beruehrt.
>
> | Zeile | Stand |
> |---|---|
> | T312 Gespraech im breiten Fenster | **bestanden 23:28** — siehe Nachtrag unten |
> | T313 Kachel im Gespraech | offen |
> | T314 Layoutwechsel im Gespraech | offen |
> | T315 DTMF im breiten Layout | offen |
> | T316 Anruf vor dem ersten Messen | offen |
> | T317 Zwei Gespraeche breit | offen |
>
> **Nachtrag 23:28 — T312 ist bestanden, und diesmal mit `Connected`.**
> Anruf `25906373`: `Dialing → Ringing` 23:27:59, **`Connected` 23:28:02**,
> beendet 23:28:15 — **13,6 Sekunden verbunden**, im breiten Fenster, mit der
> Gespraechsansicht in der linken Spalte. **Keine Ausnahme, keine Zeile
> `[ERR] Nipp.App`, kein Absturz beim Ein- und Ausblenden.**
>
> Das Gespraech selbst war auch technisch sauber: **null Ticker-Verspaetungen**,
> Jitterpuffer 53 → 55 → 46 → 40 → 39,8 ms (sauber eingeschwungen), teuerster
> Filter `MSResample` mit **max 3,37 ms** je Tick. Der Rauschfilter ist weiter
> aus — `MSNoiseSuppressor` kommt im ganzen Abschnitt nicht vor.
>
> **Offen bleiben T313 bis T317.** Und was das Aussehen angeht, ist die Quelle
> Dominic am Fenster: das Protokoll belegt ein verbundenes Gespraech ohne
> Fehler, ueber die Darstellung sagt es nichts.

> **Stand 16.09.2026, nachts: gebaut, aber noch nicht gesehen.** Build ohne
> Warnungen, 1 252 Komponenten- und 34 Architekturtests grün — **das sagt über
> das Aussehen nichts.** `Nipp.App` hat kein Testprojekt, und der eingebettete
> Zustand braucht ein laufendes Gespräch: er ist ohne Anlage nicht auszulösen.
> **T312 bis T317 sind offen, und bis dahin ist Teil B unbewiesen.**
>
> | | Stand |
> |---|---|
> | **B1** Frame in der linken Spalte | gebaut — `CallFrame` in `ShellPage.xaml`, `ApplyCallView` entscheidet |
> | **B2** Wer navigiert | gebaut — `MainWindow.IstBreit()` liest `ShellViewModel.IsWide`, im breiten Layout wird nicht navigiert |
> | **B3** Wechsel im Gespräch | gebaut — breit → schmal navigiert `ApplyCallView` selbst auf die ganze Seite |
> | **B4** Kacheln im Gespräch | **kein Code nötig.** Der zweite Anruf mit Halten steht im `SipService` (§8.2), und der Kachel-Klick läuft durch dieselbe Kette. Es genügte, die Kacheln stehen zu lassen |
> | **B5** Tastatur | **vermutlich kein Code nötig** — siehe unten. Ungemessen |
> | **B6** Zurück-Pfeil, Anrufleiste | gebaut — der Pfeil über den Navigationsparameter, die Leiste steht in der linken Spalte und verschwindet mit ihr |
>
> **Ein Befund aus dem Bauen, der im Plan noch nicht stand:** die Meldungszeile
> der Shell (Fehler und Hinweise) steht ebenfalls in der linken Spalte und wäre
> mit ihr verschwunden. Wer im Gespräch eine dritte Nebenstelle anklickt,
> bekommt die Ablehnung aus §8.2 — und hätte sie nicht gesehen: *es passiert
> scheinbar nichts*, die Fehlerklasse, die dieses Projekt zweimal bezahlt hat.
> Sie ist jetzt von der Ausblendung ausgenommen und liegt über dem
> Gesprächsrahmen. Dasselbe gilt für den Hinweis «Audiogerät gewechselt», der
> im Gespräch *wichtiger* ist als sonst.

## Wie es heute ist

`MainWindow.OnCallStateChanged` navigiert bei einem Gespräch den
`ContentFrame` auf `ActiveCallPage` und danach zurück auf `ShellPage`. **Die
Gesprächsansicht ersetzt also die ganze Seite** — mitsamt Kacheln, Lampen und
Wählfeld. Zurück führt der Pfeil oben links; umgekehrt führt die
`ActiveCallBar` in der Shell («Zurück zum laufenden Gespräch») wieder hinein.
Beides bleibt im schmalen Layout genau so.

## Was daraus wird (entschieden am 16.09.2026)

```
┌────────────────────┬──────────────────┐
│  ▼ Gespräch        │   Nebenstellen   │
│   Anna Muster      │  ┌────┐  ┌────┐  │
│   02:14            │  │ ●  │  │ ●  │  │
│  [Stumm] [Halten]  │  └────┘  └────┘  │
│  [Weiterleiten]    │  ┌────┐  ┌────┐  │
│  [DTMF] [Aufnahme] │  │ ●  │  │ ●  │  │
│  [   Auflegen   ]  │  └────┘  └────┘  │
└────────────────────┴──────────────────┘
```

| | Entscheidung |
|---|---|
| **Links** | **Nur das Gespräch.** Die linke Spalte wird ganz zur Gesprächsansicht; Wählfeld und Anrufliste sind währenddessen nicht erreichbar |
| **Rechts** | Die Nebenstellen-Kacheln mit Lampen, **bedienbar**: ein Klick startet einen **zweiten Anruf**, das laufende Gespräch geht auf Halten. Weiterleiten läuft weiter über den bestehenden Weg in der Gesprächsansicht |
| **Schmal** | **Unverändert** — Gesprächsansicht füllt das Fenster |

## B1 — Wo die Gesprächsansicht dann lebt

**Sie hängt an genau einer Stelle an ihrem Frame:** `OnBackClick` ruft
`Frame.GoBack()` beziehungsweise `Frame.Navigate(typeof(ShellPage))`. Sonst
nirgends — 969 Zeilen Code-Behind und kein weiterer Zugriff. **Und im breiten
Layout hat dieser Pfeil keine Bedeutung mehr:** wohin er führt, steht ja schon
daneben.

Damit sind zwei Wege gangbar:

| | Weg | Aufwand | Risiko |
|---|---|---|---|
| **(a)** | Ein **`Frame`** in der linken Spalte, in den `ActiveCallPage` navigiert wird | klein — die Seite bleibt, wie sie ist | verschachtelte Navigationsgeschichte; **zwei Instanzen**, wenn der äussere Frame ebenfalls navigiert |
| **(b)** | Den Inhalt in ein **`UserControl`** (`ActiveCallView`) herausziehen; `ActiveCallPage` wird ein dünner Wirt für den schmalen Fall | gross — 820 Zeilen XAML und 969 Zeilen Code-Behind umziehen | mechanisch, aber breit: alles, was am Seitentyp hängt (Tastatur, Fokus, `OnNavigatedTo`), wird angefasst |

**Empfehlung: (a), und zwar zuerst.** Sie beantwortet die offenen Fragen
unten mit einem lauffähigen Stand, statt sie im Voraus zu entscheiden. Wenn
sich dabei zeigt, dass der innere Frame quer liegt, ist (b) immer noch da —
umgekehrt wäre der grosse Schnitt schon getan und die Erkenntnis teuer
bezahlt. **Bedingung für (a):** der äussere Frame navigiert im breiten Layout
**nicht** zur `ActiveCallPage`, sonst steht sie zweimal im Baum.

## B2 — Wer entscheidet, wohin navigiert wird

Heute entscheidet `MainWindow.OnCallStateChanged` allein aus «gibt es ein
Gespräch». Künftig braucht es dazu das Layout — und **das Layout entscheidet
`ShellViewModel.ApplyWidth`, an genau einer Stelle** (ADR-047, CLAUDE.md).
`MainWindow` liest es von dort und baut keine zweite Regel.

**Die Stelle, an der es klemmt:** die Breite wird von der `ShellPage`
gemessen. Steht sie nicht im Frame, misst niemand — und genau das passiert
heute während eines Gesprächs. Im neuen Zustand bleibt die Shell im breiten
Layout stehen, also misst sie weiter; **beim Start in ein Gespräch hinein**
(Anruf kommt, bevor die Shell je stand) gibt es aber keinen gemessenen Wert.
Dann gilt der zuletzt bekannte, und beim ersten Messen wird umgeschaltet.
**Das ist zu prüfen, nicht anzunehmen** — B7.

## B3 — Der Wechsel mitten im Gespräch

Wer das Fenster während eines Gesprächs über die Schwelle zieht, wechselt
zwischen zwei Darstellungen, die es beide gibt:

- **schmal → breit:** äusserer Frame zurück auf `ShellPage`, Gespräch links.
- **breit → schmal:** äusserer Frame auf `ActiveCallPage`.

**Beide Male darf das Gespräch nichts merken.** Audio, Dauer, Stummschaltung
und eine laufende Aufnahme hängen am `SipService` und am
`ActiveCallViewModel`, nicht an der Seite — das ist die Zusicherung, auf der
das ruht, und sie gilt es zu prüfen (T314).

Die Schwelle ist **960 logische Pixel mit 40 Hysterese**, und sie gilt für die
**Seitenbreite**, nicht die Fensterbreite: gemessen kippt es zwischen 974 und
980 Fensterbreite. Wer zum Prüfen auf 960 zieht, meldet einen Fehlschlag, der
keiner ist (CLAUDE.md).

## B4 — Die Kacheln im Gespräch

Ein Klick startet einen zweiten Anruf; das laufende Gespräch geht auf Halten.
**Das ist der Weg, den es schon gibt** (§8.2 nennt den zweiten Anruf, und mehr
als zwei wird mit klarer Meldung abgelehnt) — hier kommt nur ein zweiter
Auslöser dazu.

**Zu klären, bevor es gebaut wird:** was ein Klick tut, wenn schon **zwei**
Gespräche stehen. Die Ablehnung gibt es, aber sie wurde nie von einer Kachel
aus ausgelöst. Kein Vorratsbau — nur sicherstellen, dass die vorhandene
Meldung erscheint und nicht eine Ausnahme.

## B5 — Die Tastatur, und das ist die unangenehme Stelle

Die Gesprächsansicht fängt Zeichen ab und schickt sie als **DTMF**
(`OnPageCharacterReceived`). Rechts stehen künftig Kacheln, die Fokus
annehmen, durchblättert werden und auf die Eingabetaste reagieren.

**Damit kollidieren zwei Bedeutungen derselben Taste**, und zwar genau dann,
wenn es darauf ankommt — im Sprachmenü einer Hotline, während man nebenbei
sieht, wer frei ist. Eine `4` gehört ins Menü, wenn der Fokus links steht, und
darf keine Kachel auswählen; umgekehrt darf die Eingabetaste auf einer Kachel
keinen Ton senden.

**Die Regel, die dafür gilt:** DTMF nimmt Zeichen nur an, solange der Fokus in
der linken Spalte liegt.

**Nachtrag vom 16.09.2026: dafür ist vermutlich kein Code nötig.**
`CharacterReceived` hängt am `Grid` **innerhalb** der Gesprächsansicht, und ein
Routed Event steigt vom fokussierten Element im Baum auf. Liegt der Fokus auf
einer Kachel, führt sein Weg nicht durch dieses Grid — das Zeichen erreicht die
Gesprächsansicht gar nicht. Die gewünschte Regel fiele damit aus der Struktur.

**Das ist gelesen und nicht gemessen** (CLAUDE.md: wer nicht gemessen hat,
schreibt «vermutlich» hin). **T315 muss es zeigen**, und bis dahin wird hier
nichts gebaut: eine Fokusregel auf Verdacht wäre eine zweite Wahrheit neben
der, die der Baum schon herstellt.

## B6 — Was klein bleibt

- **Die `ActiveCallBar`** («Zurück zum laufenden Gespräch») ist im breiten
  Layout sinnlos — das Gespräch steht ja links. Ausblenden, wenn breit.
- **Der Zurück-Pfeil** in der Gesprächsansicht ebenso.
- **Die Präsenz** läuft unverändert: `BlfService` abonniert die Nebenstellen
  ohnehin, und `UpdatePresence` ändert nur die betroffene Zeile.

## B7 — Was am Gerät zu prüfen ist

| Nr. | Rüstzeug | Was | Erwartung |
|---|---|---|---|
| T312 | P | **Gespräch im breiten Fenster** | Links das Gespräch, rechts die Kacheln mit Lampen. Kein Wählfeld links |
| T313 | P | **Kachel anklicken, während ein Gespräch läuft** | Zweiter Anruf, erstes Gespräch auf Halten. Bei schon zwei Gesprächen die vorhandene Meldung, keine Ausnahme |
| T314 | P | **Fenster über die Schwelle ziehen, während ein Gespräch läuft** | Die Darstellung wechselt, das Gespräch nicht: Audio, Dauer, Stumm und eine laufende Aufnahme laufen durch. Beide Richtungen |
| T315 | P | **DTMF im breiten Layout** | Ziffern gehen ins Gespräch, solange der Fokus links steht. Auf einer Kachel wählt die Eingabetaste, ohne einen Ton zu senden |
| T316 | P | **Anruf kommt, bevor die Shell je stand** (nipp startet minimiert, erster Anruf) | Die Ansicht steht richtig — und wenn erst die zweite Messung sie richtigstellt, darf das nicht sichtbar flackern |
| T317 | S | **Zwei Gespräche im breiten Layout** | Aktiv und gehalten sind beide sichtbar und umschaltbar, wie in der Vollseite |

---

## Die Reihenfolge

**Teil A zuerst, und getrennt committen.** Er ist klein, die Ursache steht,
und er hat mit Teil B nichts zu tun — zusammen in einem Commit wäre beides
schlechter zu lesen und ein Zurücknehmen unmöglich.

**Teil B in der Reihenfolge B1 → B2 → B3 → B5 → B4 → B6.** Der Aufbau
(B1/B2/B3) muss stehen, bevor die Feinheiten Sinn ergeben; **B5 vor B4**, weil
die Tastaturregel die unangenehmste Stelle ist und nicht ans Ende gehört, wo
sie unter Zeitdruck gerät.

## Was dieser Plan nicht tut

- **Kein Weiterleiten per Kachel.** Das war die andere Wahl bei B4 und ist
  ausdrücklich nicht getroffen worden. Wenn sich im Alltag zeigt, dass der
  Umweg über die Gesprächsansicht stört, ist das ein eigener Entscheid — mit
  dem Alltag als Begründung und nicht mit einer Vermutung von heute.
- **Keine dritte Spalte**, kein «sehr breit». `ShellLayout` hat bewusst zwei
  Werte (ADR-047).
- **Nichts am schmalen Layout.**
