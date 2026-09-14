# BREITBILD-PLAN — die zweite Spalte

**Auftrag von Dominic, 12.09.2026 abends.** nipp ist für 400 Pixel gebaut
(§20.1). Auf einem breiten Fenster — Vollbild auf einem 24-Zöller — soll es
den Platz nutzen statt ihn zu deckeln, und die Team-Nebenstellen sollen als
**Kacheln** stehen, nicht als Liste. Dazu ein zweiter Wunsch, der unabhängig
von der Breite gilt: **ein angeklickter Kontakt klappt in der Zeile auf**,
statt seine Angaben ganz unten zu zeigen.

> **Nachtrag vom 12.09.2026 (ADR-052): die Deckelung ist ganz weg.** Alles
> unten Beschriebene steht so gebaut da, mit drei Änderungen: der Inhalt füllt
> das Fenster (schmal wie breit), die linke Spalte ist im breiten Layout
> **fest** 480 statt gedeckelt — zwei Sternspalten mit Höchstbreiten liessen
> rund 480 Pixel verfallen —, und die Umschaltleiste steht breit nur noch unter
> der linken Spalte. Wer unten «die Deckelung wandert» liest, liest den Stand
> vom 12.09.2026.

> **Das ist eine Abweichung von §20.1 mit Auftrag.** ADR-046 hat die
> Inhaltsbreite am 12.09.2026 auf 480 Pixel gedeckelt und dabei ausdrücklich
> festgehalten: *„Wer den Platz nutzen wollte, bräuchte eine zweite Spalte —
> das wäre eine Abweichung von §20.1 und ein eigener Entscheid."* Dies ist
> dieser Entscheid. Die Deckelung bleibt, sie gilt künftig **je Spalte**.

---

## Umsetzungsstand

| Etappe | Was | Stand |
|---|---|---|
| **W0** | Auftrag und Entscheidung: §23 in NIPP-BUILD.md, ADR-047, ADR-048 | **gebaut** |
| **W1** | Der Layoutzustand liegt im Kern — Schwelle, Hysterese, Sektionsregeln | **gebaut** |
| **W2** | Zwei Spalten in `ShellPage`, die Deckelung wandert in die Spalte | **gebaut** |
| **W3** | Die Team-Kacheln: gruppiertes Raster statt Liste | **gebaut** |
| **W4** | Der Detailbereich klappt in der Zeile auf (ADR-048) | **gebaut** |
| **W5** | Ohne Maus und ohne Augen: Tastatur, Sprachausgabe, Tokens | **gebaut** |
| **W6** | Die Fensterlage: ein maximiertes Fenster kommt maximiert zurück | **gebaut** |
| **W7** | Tests, Doku, Testmatrix **T211 bis T221** | **gebaut** |

**Stand 12.09.2026: alles gebaut, nichts am Gerät abgenommen.** 1082
Komponententests und 23 Architekturtests grün (+18 gegenüber dem 12.09.2026),
Build ohne Warnungen. Was zu prüfen ist, steht als **T211 bis T221** in
`docs/test-matrix.md`.

**Zwei Entscheidungen sind unterwegs anders ausgefallen als im Entwurf:**

- **Die Schwelle liegt bei 960 statt 900.** Die erste Rechnung hatte Rahmen und
  Bildlaufleiste des Kachelbereichs vergessen; bei 900 hätte rechts **eine**
  Kachelspalte gestanden, und dafür ist die Liste die bessere Form. Gerechnet:
  24 + 480 + 12 + rund 30 + zwei Zellen à 200 = 946.
- **Die Kachel trägt zwei Nummernzeilen, nicht beliebig viele.** Ein
  `ItemsWrapGrid` misst die erste Kachel und gibt allen anderen dasselbe Mass;
  ungleich hohe Kacheln ergäben ein Raster mit Löchern. Die dritte Nummer steht
  hinter «+1 Nummer». Das ist die einzige Einschränkung gegenüber «alles
  sichtbar», und sie ist erzwungen, nicht gewählt.

**Das Hauptrisiko ist ausgeräumt:** `x:Load` in einem `DataTemplate` erzeugt
die erwartete Lade-Mechanik — der XAML-Compiler schreibt `FindName` beim
Aufklappen und `XamlMarkupHelper.UnloadObject` beim Zuklappen. Der Rückfall aus
W4 wird nicht gebraucht. **Die Laufzeitmessung bleibt: T220.**

---

## Was heute dasteht

`ShellPage.xaml` ist **eine** Rasterspalte mit sechs Zeilen (Konto, Eingabe,
Wähltastatur, Inhalt, Meldungen, Umschaltleiste). Der Inhaltsbereich enthält
vier Panels übereinander, von denen `Refresh()` genau eines sichtbar schaltet:
`HistoryPanel`, `ContactsPanel`, `VoicemailPanel`, `PlaceholderPanel`. Das
Ganze steckt in einem Raster mit `MaxWidth="{StaticResource
NippContentMaxWidth}"` — 480.

Die Kontakte stehen in zwei Aufklappbereichen (Team, Outlook), das Team als
**eine** gruppierte `ListView` über eine `CollectionViewSource` (ADR-041), und
darunter ein Detailbereich für alle drei Listen (ADR-046).

**Drei Stellen tragen den Umbau, und keine davon wird ersetzt:**

- `ContactGroupRow` — die Gruppen samt Auf- und Zuklappzustand. Ein Raster
  liest dieselbe `CollectionViewSource`.
- `TeamLayout.From` — liest die Ordnung aus dem Zustand der Sammlungen und
  nicht aus dem Ziehereignis (ADR-042). Das gilt für ein `GridView` wörtlich
  gleich; **das Umsortieren übersteht den Umbau unverändert.**
- `ShellViewModel.ExpandedContact` — welche Zeile offen ist, steht schon im
  Kern. W4 ändert nur, **wo** sie gezeichnet wird.

---

## Das Zielbild

### Schmal (unter 960 Pixeln) — unverändert

Wie heute, mit einer Ausnahme: der Detailbereich klappt in der Zeile auf
(W4).

### Breit (ab 960 Pixeln)

```
┌──────────────────────┬─────────────────────────────────────┐
│ Konto            ▼   │  Team (4)                       ▾   │
│ [Nummer………]    [📞]  │  ┌──────────┐┌──────────┐┌────────┐ │
│ [Wähltastatur]       │  │● Anna M. ││● Beat B. ││○ Cara C│ │
│                      │  │ Support  ││ Verkauf  ││ Verkauf│ │
│ Anrufliste           │  │ 📞 21    ││ 📞 22    ││ 📞 23  │ │
│  • Meier     09:12   │  │ 📞 079…  ││          ││ 📞 079…│ │
│  • 044…      08:40   │  └──────────┘└──────────┘└────────┘ │
│  • Huber    gestern  │  Support (2)                    ▾   │
│  ▾ Kontext des       │  ┌──────────┐┌──────────┐           │
│    gewählten Anrufs  │  │● Dani D. ││● Eva E.  │           │
│                      │  └──────────┘└──────────┘           │
├──────────────────────┴─────────────────────────────────────┤
│ [Kontakte] [Anrufe] [Mailbox] [Einstellungen]              │
└────────────────────────────────────────────────────────────┘
```

**Die Umschaltleiste steuert die linke Spalte** — Anrufe, Kontakte, Mailbox.
Die Team-Kacheln stehen rechts **immer**, unabhängig davon, was links gewählt
ist. Das ist die kleinste Änderung an einem eingeübten Bedienbild: alle vier
Bereiche bleiben erreichbar, sie bekommen nur einen anderen Platz.

**Im Bereich „Kontakte" zeigt die linke Spalte dann nur noch Outlook und die
Suchtreffer.** Das Team steht rechts; zweimal dieselbe Liste in einem Fenster
wäre die Doppelanzeige, wegen der ADR-046 das Suchfeld zusammengelegt hat.
Fällt der Team-Abschnitt links weg, bekommt Outlook die ganze Höhe — die
Höhenlogik aus `RefreshContactSections` regelt das bereits.

---

## W0 — Auftrag und Entscheidung

**§23 in `NIPP-BUILD.md`** („Nachtrag Rev. 9 — breites Layout"), als Ergänzung
zu §20.1 und nicht als Ersatz: das schmale Format bleibt die Vorgabe und der
Normalfall, das breite tritt ab einer Schwelle hinzu.

**ADR-047 — Zwei Spalten ab 900 Pixeln, die Nebenstellen als Kacheln.**
Begründung, Schwelle, Hysterese, und warum die Deckelung aus ADR-046 bleibt.

**ADR-048 — Der Detailbereich klappt in der Zeile auf.** Er **revidiert
ADR-046 in diesem Punkt** und nimmt den Ort aus ADR-042 zurück — aber für
**alle drei Listen**, und damit fällt der Grund weg, aus dem ADR-046 ihn
herausgezogen hat: nicht der Ort war falsch, sondern dass er für Team und
Outlook verschieden war.

> **Der Leistungsgrund aus ADR-042 bleibt richtig und ist lösbar.** Über
> hundert Outlook-Zeilen dürfen nicht jede einen ausklappbaren Teil
> mitbringen. `x:Load` erzeugt den Teilbaum erst, wenn die Bedingung wahr
> wird — es ist also nie mehr als **ein** Detailbereich im Baum, egal wie
> viele Zeilen die Liste hat. Das ist der Weg, den ADR-042 nicht geprüft hat.

---

## W1 — Der Layoutzustand liegt im Kern

`Nipp.App` hat kein Testprojekt. Ein Layout, das sich an einer Schwelle
umstellt, hat Zustand — und Zustand gehört in den Kern, aus demselben Grund
wie beim Karten-Designer.

**Neu in `Nipp.Core/ViewModels/`:**

```
public enum ShellLayout { Narrow, Wide }
```

und in `ShellViewModel` (eigene Teildatei `ShellViewModel.Layout.cs`, wie
`ShellViewModel.Search.cs`):

- `ShellLayout Layout { get; private set; }`
- `void ApplyWidth(double logischeBreite)` — die einzige Stelle, die
  entscheidet.
- `bool ShowTeamInLeftColumn => Layout == ShellLayout.Narrow;`
- `bool ShowTiles => Layout == ShellLayout.Wide;`

**Die Schwelle: 960 logische Pixel, mit Hysterese zurück bei 920.**

Gerechnet: 24 Seitenrand + linke Spalte 480 (die Deckelung aus ADR-046) + 12
Spaltenabstand + Rahmen und Bildlaufleiste des Kachelbereichs (rund 30) + zwei
Kachelzellen à 200 = 946, aufgerundet auf 960. Darunter stünde rechts **eine**
Kachelspalte — dafür ist die Liste die bessere Form.

> Der erste Entwurf hatte 900 und dabei Rahmen und Bildlaufleiste vergessen.
> Nachgerechnet vor dem Bauen; der Unterschied sind genau die 30 Pixel, die
> eine zweite Kachelspalte möglich machen.

**Die Hysterese ist kein Feinschliff.** Wer das Fenster an der Schwelle
zieht, baut sonst bei jedem Pixel die halbe Seite neu auf — auf dem Thread,
der alle 20 ms `Core.Iterate()` bedient. Vierzig Pixel Abstand zwischen
Hinein und Hinaus, und die Umschaltung passiert einmal.

**Getestet wird im Kern:** Schwelle hinauf, Schwelle hinunter, der Bereich
dazwischen (keine Änderung), und dass `ApplyWidth` bei gleichbleibendem
Ergebnis **kein** `PropertyChanged` meldet — sonst zeichnet jede
Grössenmeldung die Seite neu.

**Verdrahtet wird über `SizeChanged` der Seite**, nicht über einen
`AdaptiveTrigger`. Zwei Gründe: der Zustand muss ohnehin im Kern stehen, und
ein `VisualState` kann die Sichtbarkeit der Team-Kacheln nicht mit der
Sektionsregel verrechnen, ohne die Regel ein zweites Mal auszuschreiben.

---

## W2 — Zwei Spalten in `ShellPage`

**Ein Baum, nicht zwei.** Ein zweites XAML für das breite Format wäre die
Doppelwahrheit, die dieses Projekt an vier Stellen teuer bezahlt hat: beim
nächsten Mal wird einer von beiden geändert.

Das äussere Raster bekommt eine zweite Spalte, und die Elemente wandern per
`VisualStateManager` zwischen den Zellen — `Grid.Row`, `Grid.Column`,
`Grid.ColumnSpan` und `Visibility` als Setter, mehr braucht es nicht.

**Die Deckelung wandert, sie verschwindet nicht.** Heute steht
`MaxWidth="480"` am äusseren Raster. Künftig:

- schmal: unverändert am äusseren Raster,
- breit: **an der linken Spalte** (`ColumnDefinition Width="480"` als
  Höchstmass), die rechte Spalte bekommt den Rest.

Damit bleibt die Zusage aus ADR-046 wörtlich erfüllt: keine Zeile zerfällt in
zwei weit auseinanderliegende Hälften. Eine Kontaktzeile ist links nie breiter
als heute.

**Die Umschaltleiste bleibt unten über beide Spalten** (`ColumnSpan="2"`).
Sie ist der Ort, an dem man den Bereich wechselt, und der ändert sich nicht,
nur weil das Fenster breiter ist.

**`NavigationCacheMode.Required` ist zu bedenken:** die Seite kommt aus dem
Zwischenspeicher zurück. Der Zustand muss in `OnLoaded` **und** bei jeder
Grössenmeldung hergestellt werden, nicht im Konstruktor — dieselbe Falle, die
in CLAUDE.md unter „WinUI und XAML" steht.

---

## W3 — Die Team-Kacheln

**Ein `GridView` mit derselben gruppierten Quelle**, die die `ListView` heute
schon bekommt (`AttachTeamGroups`). Gruppenköpfe über `GroupStyle`, wie heute
— Name, Anzahl, klappbar, derselbe `OnTeamGroupHeaderClick`.

### Die Kachel

Gewählt ist **alles dauerhaft sichtbar**: Name, Gruppe beziehungsweise Firma,
Präsenz als Punkt **und** Text, und jede Nummer einzeln wählbar.

```
┌────────────────────────┐
│ ●  Anna Muster         │   ← Punkt: Präsenzfarbe
│    Support · frei      │   ← Text: §8.4, nie nur Farbe
│  📞 21          Intern │   ← je ein Knopf, wählt direkt
│  📞 079 123 45 67 Mobil│
└────────────────────────┘
        200 × 124
```

**Der Statuston färbt Schrift, Punkt und Rand — nie die Fläche** (ADR-044).
Eine Kachel, die im Gesprächszustand gelb hinterlegt ist, stünde im dunklen
Erscheinungsbild bei 1,4:1; genau deshalb ist die Regel entstanden. Der
Kachelhintergrund bleibt `CardBackgroundFillColorDefaultBrush`.

**Die Kachelgrösse ist fest, und das ist kein Schönheitsentscheid:** ein
`ItemsWrapGrid` misst die erste Kachel und gibt allen anderen dasselbe Mass.
Ungleich hohe Kacheln ergäben dort ein Raster mit Löchern. Festgelegt wird die
Höhe auf **zwei Nummernzeilen** — Nebenstelle und Handy, der Regelfall seit
ADR-041. Wer eine dritte hat, bekommt in der letzten Zeile „+1 Nummer", und
ein Klick darauf öffnet die übrigen als Flyout.

> Das ist die einzige Einschränkung gegenüber „alles sichtbar", und sie ist
> erzwungen, nicht gewählt. Wenn sich im Alltag zeigt, dass drei Nummern
> häufig sind, wird die Kachelhöhe erhöht — eine Zahl in `Tokens.xaml`.

### Was gleich bleibt

- **Ziehen zwischen Gruppen** (ADR-042): `CanReorderItems`,
  `DragItemsStarting`, `DragItemsCompleted` heissen am `GridView` gleich, und
  `TeamLayout.From` liest wie bisher den Zustand der Sammlungen. Der
  Sortier-Umschalter bleibt.
- **Kontextmenü** samt „In Gruppe verschieben" — derselbe Handler, dieselbe
  Füllung beim Öffnen.
- **Doppelklick wählt**, Einfachklick wählt aus. Hier gibt es keinen
  Detailbereich mehr: er steht ja auf der Kachel.

### Virtualisierung

Ein gruppiertes `GridView` virtualisiert **nur mit `ItemsWrapGrid` als
`GroupStyle.Panel`**.

> **Falsch, und am 12.09.2026 am Gerät widerlegt (ADR-052).** Die
> virtualisierenden Panels lesen `GroupStyle.Panel` gar nicht — das Raster
> gehört als `ItemsPanel` hin. Bis dahin zeichnete das `ItemsStackPanel`
> daneben **eine Kachel je Reihe**, über die volle Breite gestreckt. Ohne das erzeugt es jede Kachel jeder Gruppe sofort —
derselbe Fehler wie zwei `ListView`s in einem `ScrollViewer`, nur mit einem
anderen Namen. Bei zehn Nebenstellen fällt es nicht auf; bei vierzig und
einem Tab-Wechsel schon, und es wäre wieder ein Ruckeln und kein Absturz.
**Das ist T215.**

---

## W4 — Der Detailbereich klappt in der Zeile auf

Gilt **in beiden Layouts** und für **alle drei Listen** (Team, Outlook,
Suchtreffer). Der Bereich unter der Liste entfällt.

**Im Kern:** `ContactRow.IsDetailExpanded` kommt zurück — als
`[ObservableProperty]`, gesetzt **ausschliesslich** von
`ShellViewModel.ToggleContactDetails`. Eine Zeile, die das selbst täte, wüsste
nichts von der vorher offenen; die Regel „es ist höchstens einer offen" steht
an einer Stelle, wie heute.

**In der Vorlage:**

```xml
<StackPanel
    x:Name="Details"
    x:Load="{x:Bind IsDetailExpanded, Mode=OneWay}"
    ...>
```

`x:Load` und nicht `Visibility`: der Teilbaum entsteht erst beim Aufklappen
und verschwindet beim Zuklappen wieder. Über hundert Outlook-Zeilen tragen
damit **null** zusätzliche Elemente, solange niemand etwas angeklickt hat —
der Einwand aus ADR-042, ausgeräumt statt umgangen.

**Rückfall, falls `x:Load` im `DataTemplate` nicht trägt:** ein
`ContentControl` in der Zeilenvorlage, dessen Inhalt nur bei der offenen Zeile
gesetzt wird. Gleiche Wirkung, etwas mehr Code im Code-behind. Entschieden
wird das **an einer Messung** (Zeit bis zur gezeichneten Liste mit 137
Kontakten, mit und ohne), nicht an einer Vermutung.

**Was zu beachten ist:** die Liste wächst beim Aufklappen unter der gewählten
Zeile. `ScrollIntoView` auf die Zeile hält sie sichtbar — dieselbe Zeile, die
`UpdateContactDetails` heute schon für das Team ruft, jetzt für alle.

**`ReattachContactDetails` bleibt unverändert nötig:** `RefreshContacts`
tauscht die Zeilen aus, und die Fahne hängt an der Instanz.

---

## W5 — Ohne Maus und ohne Augen

- **Die Kachel braucht `AutomationProperties.Name`** — sonst liest die
  Sprachausgabe `ToString()` des gebundenen Objekts. `ContactRow.AccessibleName`
  gibt es schon und nennt Name, Nummer und Präsenz; für die Kachel kommt die
  Gruppe dazu, weil das Raster keine Zeilenreihenfolge vorliest.
- **Jeder Nummernknopf auf der Kachel** bekommt einen eigenen Namen
  („Anna Muster, Mobil, 079 123 45 67 anrufen"). Ein Knopf, der nur „📞" heisst,
  ist für die Sprachsteuerung nicht adressierbar.
- **Pfeiltasten** navigieren im Raster zweidimensional (das kann `GridView`
  von selbst), **Enter** wählt, **Menütaste und Umschalt+F10** öffnen das
  Kontextmenü — das hängt am umschliessenden Raster der Kachel, nicht am
  Inhalt. Genau dieser Fehler war ADR-044 T185.
- **Der aufgeklappte Detailbereich** muss in der Tabreihenfolge direkt nach
  seiner Zeile stehen, nicht am Ende der Liste.
- **Tokens statt Zahlen im XAML:** `NippTileWidth`, `NippTileHeight`,
  `NippWideThreshold`, `NippWideHysteresis` nach `Tokens.xaml`, mit dem
  Rechenweg als Kommentar.

---

## W6 — Die Fensterlage

**Befund beim Lesen von `WindowPlacement`:** ein maximiertes Fenster kommt
nicht maximiert zurück.

- `Capture` speichert Position und Grösse, **nicht den Zustand des
  Presenters**. Ein maximiertes Fenster wird als gewöhnliches Fenster in
  Bildschirmgrösse gemerkt.
- `TryApplyRemembered` klemmt die gemerkte Breite auf
  `MaxWorkAreaFraction = 0.92`. Wer nipp maximiert schliesst, findet es beim
  nächsten Start auf 92 Prozent wieder — mit sichtbarem Rand ringsum.

Solange das schmale Fenster der Normalfall war, war das eine Kleinigkeit. Wenn
„Vollbild" der Anlass für dieses ganze Layout ist, ist es einer: **das breite
Layout wäre nach jedem Neustart knapp danebengesetzt.**

Zu tun: den Zustand mitschreiben (ein fünftes Feld in der gespeicherten
Zeichenkette, abwärtskompatibel — vier Teile lesen sich weiter), beim
Anwenden `presenter.Maximize()` statt `MoveAndResize`. Die
92-Prozent-Klemmung bleibt für nicht maximierte Fenster; sie ist richtig.

**Und die Mindestbreite bleibt 320.** Das breite Layout ist ein Angebot, kein
neuer Standard: §20.1 gilt weiter, nipp muss neben anderen Fenstern stehen
können.

---

## W7 — Tests, Doku, Testmatrix

### Im Kern (`Nipp.Core.Tests`)

| Was | Warum |
|---|---|
| Schwelle hinauf, hinunter, Hysterese dazwischen | Die eine Entscheidung des ganzen Plans |
| `ApplyWidth` meldet nur bei echtem Wechsel | Sonst zeichnet jede Grössenmeldung neu |
| `ShowTeamInLeftColumn` gegen `Layout` und `Section` | Die Regel steht einmal, nicht in der Seite |
| Genau ein `IsDetailExpanded` zur Zeit | Die Invariante aus ADR-048 |
| `ReattachContactDetails` nach `RefreshContacts` | Die Fahne hängt an der Instanz |
| `TeamLayout.From` unverändert grün | Die Gegenprobe: der Umbau auf Kacheln kostet das Umsortieren nichts |

### Am Gerät (`docs/test-matrix.md`)

| Nr. | Was |
|---|---|
| **T211** | Fenster von 400 auf Vollbild ziehen und zurück — das Layout wechselt einmal, nicht bei jedem Pixel |
| **T212** | Genau an der Schwelle langsam hin und her ziehen: **kein Flackern** (die Hysterese) |
| **T213** | Im breiten Layout die Umschaltleiste durchgehen — links wechselt der Bereich, **die Kacheln rechts bleiben stehen** |
| **T214** | Vier Gruppen, eine Kachel per Ziehen in eine andere Gruppe, **zweimal hintereinander** — die Gegenprobe zu T176 |
| **T215** | Vierzig Nebenstellen in vier Gruppen, Tab-Wechsel: **kein Ruckeln** (die Virtualisierung des Rasters) |
| **T216** | Kachel im dunklen Erscheinungsbild, Kollege im Gespräch: **Schrift und Rand tragen die Farbe, nicht die Fläche** (ADR-044) |
| **T217** | Nur mit der Tastatur: ins Raster, mit den Pfeilen zu einer Kachel, Enter wählt, Menütaste öffnet das Menü |
| **T218** | Mit der Sprachausgabe über eine Kachel: Name, Gruppe, Präsenz, jede Nummer einzeln |
| **T219** | Im schmalen Layout einen Outlook-Kontakt anklicken: er klappt **in der Zeile** auf, die Zeile bleibt sichtbar, ein zweiter Klick schliesst |
| **T220** | 137 Outlook-Kontakte, Liste durchscrollen, dabei auf- und zuklappen: kein Ruckeln — die Messung zu `x:Load` |
| **T221** | nipp maximiert beenden und neu starten: es kommt **maximiert** zurück |

### Doku

- `NIPP-BUILD.md` §23
- `docs/decisions.md` ADR-047, ADR-048
- `CLAUDE.md`: der Abschnitt über die gedeckelte Breite gilt neu je Spalte;
  ADR-046 wird in einem Punkt revidiert, das gehört unter „Grenzen"
- `README.md`: ein Satz und ein Bild

---

## Risiken, ehrlich benannt

1. **`x:Load` in einem `DataTemplate`** ist dokumentiert und wird hier an der
   heikelsten Stelle gebraucht. Der Rückfall steht in W4, und die Entscheidung
   fällt an einer Messung.
2. **Ein gruppiertes `GridView` virtualisiert nur mit `ItemsWrapGrid` als
   `GroupStyle.Panel`.** Der Fehler wäre kein Absturz, sondern ein Ruckeln —
   und damit erst am Gerät sichtbar (T215). Dieselbe Klasse Fehler, die
   CLAUDE.md für zwei `ListView`s in einem `ScrollViewer` festhält.
3. **Das Umschalten selbst kostet einen Layoutdurchlauf** auf dem Thread, der
   das SDK bedient. Es passiert beim Ziehen am Fensterrand, also nicht im
   Gespräch — trotzdem gehört ein Umschalten **während** eines laufenden
   Anrufs geprüft (Teil von T211).
4. **`NavigationCacheMode.Required`**: die Seite kommt aus dem
   Zwischenspeicher. Wer den Layoutzustand im Konstruktor herstellt, hat ihn
   nach der ersten Rückkehr aus der Gesprächsansicht falsch.
5. **Die Zeilenvorlage wird wieder schwerer** (W4) — genau das, was ADR-046
   leichter gemacht hat. Der Unterschied ist `x:Load`; ohne die Messung aus
   T220 ist das eine Behauptung.

---

## Bewusst nicht in diesem Plan

- **Die Gesprächsansicht bleibt einspaltig.** Kacheln rechts, die während
  eines Gesprächs weiterleiten, sind ein starker Griff — und sie legen die
  gefährlichste Handlung des Programms auf einen einzelnen Klick. T188 hat
  gerade erst Enter aus dem Weiterleitungsfeld genommen. Eigene Etappe, eigene
  Rückfrage, eigene Abnahme.
- **Drei Spalten.** Erst wenn die zwei im Alltag stehen.
- **Eine frei einstellbare Schwelle.** Eine Zahl, die niemand kennt und
  niemand ändert, kostet ein Eingabeelement auf einer Seite, die ADR-046
  gerade entlastet hat.
