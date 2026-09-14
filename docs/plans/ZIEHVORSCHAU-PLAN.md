# Ziehvorschau — die anderen weichen aus, während gezogen wird

> **Stand 14.09.2026: umgesetzt (ADR-066).** V1 bis V7 sind gebaut, 1237
> Komponententests grün. **Offen ist V8** — die Prüfung am Gerät, T292 bis
> T296. Die Bauart hat sich gegenüber dem ersten Entwurf an einer Stelle
> geändert: das Ablegeziel ist die **Liste**, nicht die Zeile (F6).

Stand 13.09.2026. Auftrag von Dominic: beim Ziehen einer Nebenstelle — als
Zeile wie als Kachel — soll sichtbar sein, **wo** sie landet, indem die anderen
Einträge ausweichen, solange der Zeiger unterwegs ist.

Vorgänger: ADR-065 (der Ziehvorgang gehört uns, nicht WinUI), ADR-042,
ADR-041. Betroffen: `ShellPage.xaml(.cs)`, `TeamLayout`, `ContactGroupRow`,
`ShellViewModel`.

---

## 1. Was heute geschieht

Gezogen wird seit ADR-065 selbst: `OnTeamPointerPressed` merkt den Druck,
nach acht Pixeln startet `StarteZug` das `StartDragAsync` am Vorlagenelement.
Während des Zugs passiert **an der Liste nichts**. Die einzige Rückmeldung:

- `DragUIOverride.Caption` — «Hierhin verschieben» am Zeiger,
- `Einfassen(sender, true)` — das Element unter dem Zeiger geht auf
  `Opacity = 0.55`.

Wohin die Zeile kommt, rechnet erst `OnTeamRowDrop`: Index der Zielzeile in
ihrer Gruppe, plus eins, wenn in der unteren (bei Kacheln: rechten) Hälfte
losgelassen wurde. Dann `MoveContactToGroupCommand` → `TeamLayout.Move` →
`ApplyTeamLayout`.

**Das heisst: bis zum Loslassen sieht man nichts.** Der halbdurchsichtige
Nachbar sagt «hier irgendwo», nicht «zwischen diese beiden».

## 2. Die Bauart — und warum diese

Drei Wege stehen zur Wahl:

| | Was man sieht | Aufwand | Bewertung |
|---|---|---|---|
| **A — echte Vorschau** | Die gezogene Zeile steht schon dort; die anderen sind ausgewichen | mittel | **gewählt** |
| B — Einfügemarke | Eine Linie zwischen zwei Zeilen, sonst bewegt sich nichts | klein | erfüllt den Auftrag nicht: «die anderen sollen sich verschieben» |
| C — Lücke | Eine leere Kachel oder Zeile klafft an der Zielstelle, die gezogene bleibt stehen | mittel | braucht eine Platzhalter-Instanz im Datenmodell — eine `ContactRow`, die kein Kontakt ist |

**Gewählt ist A**, und der Grund ist nicht nur das Aussehen: bei A bleibt es
**eine** Rechnung. Die Vorschau ist dann der Auftrag — beim Loslassen wird
nicht noch einmal gerechnet, sondern geschrieben, was dasteht. `ShellViewModel`
hat dafür bereits die parameterlose Fassung `ApplyTeamLayout()`, die aus der
Anzeige liest. Bei B und C bliebe die Zielstelle zweimal ausgerechnet — einmal
fürs Zeichnen, einmal fürs Schreiben —, und das ist die Doppelwahrheit, die
dieses Projekt wiederholt bezahlt hat.

Wichtig, damit es eine Stelle bleibt: **die Vorschau darf nicht neben
`TeamLayout.Move` stehen, sondern an ihrer Stelle.** `OnTeamRowDrop` rechnet
danach keinen Index mehr.

## 3. Die fünf Stellen, an denen A scheitern kann

**V1 hat sie gemessen** — am 14.09.2026, 07:14 bis 07:16, mit einer Sonde im
gebauten Fenster: sieben Züge, 67 Vorschauschritte, 136 `DragLeave`. Die
Ergebnisse stehen bei jeder Stelle. Zwei waren richtig, einer war falsch
gedacht, und einer ist grösser als erwartet.

### F0 — Trägt die Bauart überhaupt? **Ja (gemessen)**

Die eigentliche Frage, und sie stand im ersten Entwurf dieses Plans nicht
darin: Überlebt ein laufender `StartDragAsync` es, dass ihm die Sammlung unter
den Fingern umsortiert wird?

**Er überlebt es.** 67 Umhängungen über sieben Züge, davon viele über
Gruppengrenzen (`Remove` aus der einen `ObservableCollection`, `Insert` in die
andere) — kein Abbruch, keine Ausnahme, kein verlorener Zug. Fünf der sieben
Züge endeten regulär mit `Move`.

### F1 — Der Drop über der eigenen Zeile wird abgelehnt

Sobald die gezogene Zeile an die Vorschaustelle gerückt ist, liegt **sie**
unter dem Zeiger. `IstZug` liefert dann `ziel == gezogen` und gibt `false`
zurück; `AcceptedOperation` bliebe `None`, der Zeiger zeigte ein Verbotszeichen
und das Loslassen täte nichts.

Das ist die Falle, die ein naives Live-Umsortieren unbrauchbar macht. Der
Behandler muss über der **gezogenen** Zeile annehmen und nichts ändern.

**Gemessen, und schlimmer als gedacht: das ist nicht der Sonderfall, sondern
der Normalfall.** Von den fünf Zügen, die mit `Move` endeten, kamen **vier**
über der gezogenen Zeile selbst an (`drop:selbst`) und nur einer über einer
fremden (`drop:zeile`). Wer die Vorschau baut und diesen Zweig vergisst,
bekommt eine Liste, in der das Ausweichen wunderbar aussieht und das
Loslassen fast immer nichts tut.

### F2 — Die Präsenzmeldung wirft die Vorschau zurück — **nein**

**Falsch gedacht.** `OnPresenceChanged` ruft `UpdatePresence`, und das setzt
nur `row.Presence` an der bestehenden Instanz — kein `SetRows`, kein
`RebuildTeamGroups`. Genau dafür gibt es `ContactRow`. Die Präsenz ist keine
Gefahr für die Vorschau.

Die Gefahr steht woanders, und sie ist kleiner: `RebuildTeamGroups` läuft aus
`RefreshTeamContacts` (Ladelauf) und aus `OnSettingsChanged`. Beide sind
während eines Zugs selten, und beide prüfen vorher, ob sich überhaupt etwas
geändert hat — `SameContacts` und `GroupNamesMatch` sehen die Vorschau nicht,
weil sie nur `TeamContacts` und die Gruppennamen vergleichen, und die rührt
die Vorschau nicht an. Bleibt der Fall, dass währenddessen wirklich eine
Einstellung geschrieben wird; dann steht die gespeicherte Ordnung wieder da.

Das rechtfertigt keine Sperre auf Verdacht. **V7 wird gestrichen** und durch
eine Zeile in der Testmatrix ersetzt.

### F3 — Zittern — **ja, und die Ursache ist eine andere**

Vermutet war: ungleich hohe Zeilen durch einen offenen Detailbereich.
Gemessen ist etwas anderes, und es ist häufig.

**Die Vorschau springt an der Gruppengrenze hin und her.** Immer dasselbe
Muster, in vier von sieben Zügen:

    07:14:59.359  vorschau (Gruppe=1, Stelle=0)
    07:14:59.395  vorschau (Gruppe=0, Stelle=3)     36 ms später
    07:15:00.421  vorschau (Gruppe=0, Stelle=4)
    07:15:00.450  vorschau (Gruppe=1, Stelle=0)     29 ms später
    07:16:05.440  vorschau (Gruppe=0, Stelle=2)
    07:16:05.465  vorschau (Gruppe=1, Stelle=2)     25 ms später

Immer zwischen «ganz unten in Gruppe 0» und «ganz oben in Gruppe 1» — den
beiden Stellen, zwischen denen der Gruppenkopf steht. Der Grund ist die
Rückkopplung: Die Zeile wechselt in Gruppe 0, dadurch wächst Gruppe 0 und alles
darunter rutscht nach unten, unter dem stehenden Zeiger liegt wieder eine Zeile
aus Gruppe 1 — zurück. **Der Zeiger bewegt sich dabei nicht; das Layout bewegt
sich unter ihm.**

Das ist auch die Antwort: **ein Vorschauschritt braucht eine Zeigerbewegung.**
Bewegt sich der Zeiger seit dem letzten Schritt um weniger als eine Schwelle,
bleibt die Vorschau stehen — egal, was inzwischen unter ihm liegt. Damit fällt
die Rückkopplung weg, ohne dass die Vorschau träge wird: die echten Wechsel
kommen alle aus einer Bewegung.

Der Detailbereich bleibt trotzdem ein Thema: `OnIsTeamReorderModeChanged` ruft
`CloseContactDetails`, aber **nur beim Einschalten** — ein Klick auf eine Zeile
im laufenden Sortiermodus klappt sie wieder auf, denn weder
`ToggleContactDetails` noch `OnContactSelectionChanged` fragen den Modus. Der
Kommentar an `IsTeamReorderMode` behauptet «kein offener Detailbereich» als
eine von drei Regeln; **diese Regel gibt es nicht.** Sie ist nachzutragen —
und der Kommentar zu berichtigen.

### F4 — `Einfassen` leckt

`Einfassen` setzt `Opacity` direkt am Element der Vorlage. Die Container
werden virtualisiert und **wiederverwendet** — wird eine Zeile unter dem Zeiger
weggescrollt oder weggeräumt, bleibt `DragLeave` aus, und die 0.55 bleiben an
einem Element hängen, das später eine andere Zeile zeigt. Das ist schon heute
ein latenter Fehler; mit ständigem Umsortieren wird er der Normalfall.

Mit A ist `Einfassen` ohnehin überflüssig: die Rückmeldung ist die Vorschau
selbst. Was bleibt, ist ein Dimmen der **gezogenen** Zeile — und das hängt an
der Zeile, nicht am Container, also an einer Eigenschaft von `ContactRow`.

### F6 — Züge, die stumm ins Leere gehen — **gemessen, Ursache belegt**

Nicht gesucht, beim Messen aufgefallen, und der Auslöser der ganzen Sache: die
Meldung «ich kann es nicht in eine andere Gruppe verschieben».

| Messreihe | Züge | mit `Move` | mit `None` |
|---|---|---|---|
| Sonde V1 (07:14–07:16) | 7 | 5 | 2 (einer davon Escape) |
| regulär (07:53) | 6 | 4 | 2 |
| Sonde V1b (08:01–08:02) | 16 | 15 | **1** |

**Die erste Zahl in diesem Abschnitt hiess «jeder dritte Zug» und war zu hoch
gegriffen** — sie stammte aus zwei Stichproben von sieben und sechs Zügen, in
denen gewollte Abbrüche mitzählten. Die grösste Reihe sagt **1 von 16**. Über
alle 29 Züge: fünf ohne Ergebnis, davon mindestens einer gewollt.

**Der Zug funktioniert also, und er speichert:** in der V1b-Reihe standen 16
Zügen **14 Schreibvorgänge** gegenüber — einer fehlt wegen des `None`, einer,
weil die Zeile an ihren alten Platz zurückgezogen wurde und `ApplyTeamLayout`
dann richtigerweise nichts schreibt. Die gespeicherte Reihenfolge war danach
eine andere als vorher. **ADR-065 trägt.**

**Die Ursache des Fehlschlags ist jetzt belegt** — die Sonde hielt beim Zugende
fest, wann zuletzt ein Ablegeziel den Zeiger gesehen hatte:

    08:01:42.180  Ziehvorgang endete ohne Verschiebung (Ergebnis None)
    08:01:42.183  Zugsonde Ende ohne Verschiebung (letztes Ziel zeile, vor 703 ms)

**703 ms lang sah kein Ablegeziel den Zeiger.** Beim Loslassen stand er weder
über einer Zeile noch über einem Gruppenkopf — er stand im toten Gebiet
dazwischen: Abstände, Padding der Liste, der Streifen zwischen zwei Gruppen,
der Rand des Kachelrasters. Ein Beleg, kein Dutzend; aber er passt genau zur
Bauart, und es gibt keinen zweiten Kandidaten.

**Das entscheidet die Bauart:** das Ablegeziel gehört **an die Liste**
(`ListView`/`GridView` mit `AllowDrop`), die die Stelle aus der Zeigerposition
rechnet, statt an jede Zeile einzeln. Dann gibt es kein totes Gebiet mehr, die
Vorschau steht immer, und was sie zeigt, gilt auch beim Loslassen — **eine
Lösung für die Vorschau und für den stummen Fehlschlag zugleich.**

Der Fehlschlag bleibt trotzdem zu melden: Ein Zug, der nichts bewirkt, sagt es
heute niemandem. `QuietFailures` ist dafür da.

### F5 — Reihenfolge von `DragLeave` und `DragOver` — **gemessen**

`DragLeave` kommt **nach** dem `DragOver`, das bereits umgehängt hat, und es
kommt oft: **136 Mal gegen 67 Vorschauschritte**, also doppelt so häufig, und
mehrfach hintereinander für dieselbe Bewegung.

Damit ist die Frage entschieden: **im `DragLeave` wird nichts zurückgesetzt.**
Ein Zurücksetzen dort löschte die Vorschau, die das vorangegangene `DragOver`
gerade gesetzt hat. Der Behandler wird leer — `Einfassen` fällt ohnehin weg
(F4).

## 4. Wo der Zustand liegt

Im Kern, nicht im Fenster — `Nipp.App` hat kein Testprojekt, und ein
Ziehvorgang mit Herkunft, Vorschaustelle und Abbruch ist Zustand.

    src/Nipp.Core/ViewModels/TeamDragPreview.cs

      Start(gruppen, zeile)     Herkunft merken (Gruppe und Stelle), Vorschau beginnen
      MoveTo(gruppe, stelle)    idempotent — steht sie schon dort, geschieht nichts
      Cancel()                  exakt an die Herkunftsstelle zurück
      Changed                   ob sich gegenüber dem Start etwas geändert hat

`MoveTo` schreibt `Rows` **und** `All` der betroffenen Gruppen — `Rows` per
`Move` auf der `ObservableCollection` (nicht `Clear`/`Add`: das verlöre
Bildlaufposition und Auswahl, und zwar bei jeder Zeigerbewegung). Dafür braucht
`ContactGroupRow` eine eigene Methode; `SetRows` ist der falsche Weg, es räumt
zu viel.

`Cancel()` läuft in `StarteZug`, wenn `StartDragAsync` nicht mit `Move` endet —
Escape, Loslassen ausserhalb, abgebrochener Zug.

Getestet wird in `Nipp.Core.Tests`: Start → mehrfach `MoveTo` → `Cancel`
stellt die Ausgangsordnung **exakt** wieder her; Start → `MoveTo` → Übernahme
ergibt in `TeamLayout.From` die erwartete Ordnung, auch mit einer zugeklappten
Gruppe (das ist T165).

## 5. Die Runden

**V1 — messen, bevor gebaut wird. ✔ erledigt am 14.09.2026**, Ergebnisse in
Abschnitt 3 und 8. Die Bauart trägt; die Sonde ist wieder auszubauen (sie
schreibt nichts und ist als `SONDE V1` markiert: `AppLog.DragProbe`,
`Vorschau`, `VorschauZurueck`, `GruppeVonRows`, `GruppenIndex`,
`_herkunftsstelle`, `_vorschauSchritte`).

**V1b — warum enden Züge stumm?** (F6) **✔ erledigt am 14.09.2026.** Antwort:
beim Loslassen stand kein Ablegeziel unter dem Zeiger (703 ms seit dem
letzten). Damit steht fest, dass das Ablegeziel an die Liste gehört und nicht
an die einzelne Zeile — **das ist jetzt Teil von V4.**

**V2 — `TeamDragPreview` im Kern**, samt Tests. Noch nichts am Fenster.
`GruppeVonRows` aus der Sonde geht mit: `GruppeVon` sucht auch in `All` und
liefert während der Vorschau die Herkunftsgruppe zurück, obwohl die Zeile
längst woanders steht.

**V3 — `ContactGroupRow.Vorschau(zeile, stelle)`** — `Rows` per `Move`, `All`
mitgeführt, ohne `Clear`.

**V4 — das Fenster hängt sich an, und das Ablegeziel wandert an die Liste**
(F6). `AllowDrop` an `TeamList` und `TeamTiles`; ihr `DragOver` rechnet die
Stelle aus der Zeigerposition und ruft `MoveTo`. Die Ziele an Zeile und Kopf
entfallen damit — **es gibt kein totes Gebiet mehr**, und was die Vorschau
zeigt, gilt beim Loslassen überall. **F1 fällt damit nebenbei weg:** ob unter
dem Zeiger die gezogene Zeile liegt, spielt keine Rolle mehr, wenn nicht mehr
die Zeile das Ziel ist.

Der Gruppenkopf bleibt als Ziel bestehen — bei einer **leeren** Gruppe ist er
die einzige Stelle, an der sie überhaupt getroffen werden kann.

**V4a — die Zeigerbewegung als Bedingung** (F3). Ein Vorschauschritt setzt
voraus, dass sich der Zeiger seit dem letzten um mindestens eine Schwelle
bewegt hat. Die Schwelle ist **zu messen**, nicht zu raten; als Anhalt die
acht Pixel, ab denen aus einem Druck ein Zug wird (`ZugSchwelle`). Ohne diese
Bedingung zittert es an jeder Gruppengrenze, und das ist kein Randfall: in
vier von sieben gemessenen Zügen trat es auf.

**V5 — der Drop rechnet nicht mehr.** `OnTeamRowDrop` und `OnTeamGroupDrop`
setzen nur noch `_zielgruppe` und `Handled`; geschrieben wird über
`ApplyTeamLayout()` (die parameterlose Fassung) am Ende von `StarteZug`.
`Einfassen` fällt weg. Der Index-Teil von `MoveContactToGroup` bleibt für das
**Kontextmenü** bestehen — das ist der zweite, gleichwertige Weg (ADR-042), und
er hat keine Vorschau.

**V6 — die gezogene Zeile dimmen.** Über eine Eigenschaft an `ContactRow`, mit
Bindung in beiden Vorlagen. Nicht am Container (F4).

**~~V7 — F2 absichern.~~** **Gestrichen nach V1:** die Präsenzmeldung fasst die
Gruppen nicht an, eine Sperre wäre eine Vorkehrung gegen eine Gefahr, die es so
nicht gibt (Abschnitt 3, F2). Ersetzt durch eine Zeile in der Testmatrix.

**V7 — der Detailbereich im Sortiermodus.** Er wird nur beim Einschalten
geschlossen, ein Klick öffnet ihn wieder (F3, zweiter Teil). Entweder die Regel
nachtragen, die der Kommentar schon behauptet, oder den Kommentar berichtigen.
**Nicht beides offen lassen** — ein Kommentar, der eine Regel behauptet, die es
nicht gibt, ist genau der Befund, den CLAUDE.md dreimal aufführt.

**V8 — am Gerät prüfen**, beide Ansichten, hell und dunkel, 150 %: Ziehen
innerhalb der Gruppe, zwischen Gruppen, auf einen Gruppenkopf, in eine leer
gewordene Gruppe, Abbruch mit Escape, zweimal hintereinander (T176), einmal
mit zugeklappter Gruppe (T165).

## 6. Was danach zu schreiben ist

- **ADR-066** — «Die Vorschau ist der Auftrag». Was ADR-065 über das
  Ablegeziel sagt, gilt danach nicht mehr wörtlich: die Zielstelle entsteht
  beim Überfahren, nicht beim Loslassen. ADR-065 bleibt in seiner Wahl gültig
  (der Zug gehört uns), sein Satz über `OnTeamRowDrop` wird ersetzt.
- **CLAUDE.md**, der ADR-065-Absatz unter «Grenzen»: «Wohin die Zeile kommt,
  entscheidet das Ablegeziel … und rechnet `TeamLayout.Move`» → die Vorschau
  entscheidet, der Drop schreibt nur.
- **docs/test-matrix.md** — T175 bis T177 tragen die Vorschau in ihrer
  Erwartung; dazu eine neue Zeile für den Abbruch mit Escape **mit** laufender
  Vorschau (die Ordnung muss exakt zurückkommen) und eine für den Zug über
  einer leeren Gruppe.
- **docs/stand.md** — Chronologie.

## 7. Was dieser Plan nicht tut

- **Kein Ziehen im Karten-Designer** (T101, K4). Anderer Baum, andere Vorlagen.
- **Keine Animation.** Die Einträge springen an ihre neue Stelle; ein weiches
  Gleiten wäre ein zweiter Schritt und braucht im virtualisierenden Panel eine
  eigene Messung.
- **Kein zweiter Weg neben dem Kontextmenü.** «In Gruppe verschieben» bleibt,
  wie es ist — für eine Sprachausgabe ist Ziehen kein Weg.

## 8. Protokoll

**14.09.2026, 07:14–07:16 — V1, Sonde im gebauten Fenster.** Sieben Züge im
Sortiermodus, 67 Vorschauschritte, 136 `DragLeave`. Die Sonde hängte die
gezogene Zeile beim Überfahren wirklich um und schrieb nichts; am Ende jedes
Zugs stellte sie zurück.

| Frage | Ergebnis |
|---|---|
| **F0** Überlebt der Zug das Umsortieren der Sammlung? | **Ja.** 67 Umhängungen, viele über Gruppengrenzen, kein Abbruch. Fünf von sieben Zügen endeten mit `Move` |
| **F1** Wird der Drop über der eigenen Zeile abgelehnt? | **Ja — und das ist der Normalfall:** vier von fünf erfolgreichen Drops kamen als `drop:selbst` an, nur einer über einer fremden Zeile |
| **F2** Wirft die Präsenzmeldung die Vorschau zurück? | **Nein.** `UpdatePresence` setzt nur `row.Presence`, kein `SetRows`. Die Vermutung im ersten Entwurf war falsch |
| **F3** Zittert die Vorschau? | **Ja, an der Gruppengrenze**, in vier von sieben Zügen, mit 25 bis 50 ms zwischen Hin und Zurück. Nicht wegen ungleicher Zeilenhöhen, sondern weil das Layout unter dem stehenden Zeiger wandert |
| **F5** Kommt `DragLeave` vor oder nach `DragOver`? | **Danach**, und doppelt so oft (136 gegen 67). Im `DragLeave` darf nichts zurückgesetzt werden |

Zwei Züge endeten mit `None`: einer davon war der Abbruch mit Escape (gewollt,
die Zeile kam an ihren Platz zurück). Beim zweiten steht **nicht** fest, was er
war — ein `drop:kopf` steht in keinem der sieben Züge, und ob überhaupt auf
einem Gruppenkopf abgelegt wurde, ist ungeklärt. **Offen, in V8 nachzuholen.**

**Ebenfalls offen:** ob die Kacheln dabei waren. Beide Ansichten benutzen
dieselben Behandler, und das Protokoll unterscheidet sie nicht — eine Sonde,
die das messen soll, muss die Ansicht mitschreiben.

**14.09.2026, 07:53 — Gegenprobe ohne Sonde.** Anlass war die Meldung, ein
Wechsel in eine andere Gruppe sei nicht möglich. Sechs Züge mit der regulären
Fassung, davon vier mit Schreibvorgang («Team-Reihenfolge geaendert (10
Nebenstellen)», «Gruppe gewechselt (1 Nebenstellen)») und zwei stumm mit
`None`.

- **Der Gruppenwechsel funktioniert** (ADR-065 trägt). Dass die gespeicherte
  Datei danach Platz für Platz unverändert war, liegt an vier Wechseln, die
  sich aufhoben — hin und zurück.
- **Aber ein Drittel der Züge tut nichts, und zwar stumm** (F6). In der
  Sondenmessung dieselbe Quote. Das ist der wahrscheinliche Kern der
  Beschwerde, und es ist ein eigener Befund, kein Nebenbefund der Vorschau.
- **Lehre für die Messung selbst:** «funktioniert nicht» und «funktioniert
  meistens» sehen am Fenster gleich aus. Ohne die Zählung im Protokoll wäre
  daraus «geht nicht» geworden — und die Suche hätte an der falschen Stelle
  begonnen.

**14.09.2026, 08:01–08:02 — V1b, die Messung zu F6.** 16 Züge auf der
regulären Fassung, mit einer Sonde, die beim Zugende festhält, wann zuletzt ein
Ablegeziel den Zeiger gesehen hat.

- **15 Züge mit `Move`, 14 Schreibvorgänge**, die gespeicherte Reihenfolge
  danach eine andere. Der eine Zug ohne Schreiben ist der an den alten Platz
  zurückgezogene — `ApplyTeamLayout` schreibt dort richtigerweise nichts.
- **Ein Zug ohne Ergebnis**, und die Sonde nennt den Grund: *letztes Ziel
  zeile, vor 703 ms*. Beim Loslassen stand der Zeiger über keinem Ablegeziel.
- **Und die Quote aus den ersten beiden Reihen war zu hoch gegriffen** — 1 von
  16 statt «jeder dritte». Zwei Stichproben von sieben und sechs Zügen, in
  denen gewollte Abbrüche mitzählten, taugen nicht für eine Quote. Notiert,
  weil dieselbe Rechnung in diesem Projekt schon einmal eine Ursache erfunden
  hat.
