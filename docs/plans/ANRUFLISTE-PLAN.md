# Anrufliste und Designer — Plan

**Stand:** 07.09.2026 abends · Zweig `review-umsetzung`
**Anlass:** Sechs Wünsche aus dem Alltag: die Anrufliste vergisst nicht, was
schon gesehen wurde; der Kontextbereich unter ihr ist die einzige Karte, die
sich nicht einrichten lässt; und im Designer fehlen zwei Bausteine.
**Bezug:** `NIPP-BUILD.md` §8.3, §20.1, §20.3, §21, §21.6 ·
`EINRICHTUNG-PLAN.md` K4 · ADR-027, ADR-032, ADR-034

---

## Umsetzungsstand

**Alle sieben Phasen sind gebaut** (07.09.2026 abends). Build ohne Warnungen,
**917 Komponententests** (vorher 887) plus 16 Architekturtests grün.

| Phase | Stand |
|---|---|
| **L0** „Gesehen" in der Datenbank | erledigt — Schemafassung 2 |
| **L1** Fett in der Liste, Abzeichen zieht nach | erledigt — über `HistoryRow` |
| **L2** `call.*` im Schnappschuss | erledigt — sechs Felder |
| **L3** Karte der Anrufliste | erledigt — `CardKind.History` angeschlossen |
| **L4** Der Bereich klappt auf | erledigt — **Bauart B** |
| **L5** Designer: Abstand, Beschriftung, Übernehmen | erledigt |
| **L6** Doku, ADRs, Testmatrix | erledigt |

**Vier Tests haben beim ersten Lauf angeschlagen, und jeder zu Recht:**

- **`HistoryStoresNoContextTests`** — die Anrufliste hatte eine neue Spalte
  bekommen. Der Wächter tat genau das, wofür er da ist; erweitert wurde er
  **mit Begründung** (`seen_at` ist ein Zeitstempel, kein Gesprächsinhalt), nicht
  umgangen.
- **`FieldCatalogTests`** — `feldnamen.md` kannte die sechs `call.*`-Felder
  nicht. Die Doku wird aus dem Katalog erzeugt, und der Test hält beides
  zusammen.
- **`Die_Kartenuebersicht_nennt_alle_drei_Arten`** — jetzt vier. **Dieser Test
  war die Stelle, an der B3 hätte auffallen können:** er zählte drei Arten und
  war grün, während `CardKind.History` seit I4 unbenutzt im Modell stand.
- Ein eigener neuer Test behauptete, das Übernehmen der Gesprächskarte in den
  Toast melde „zu viele Textzeilen". Sie hat nur zwei Textzeilen — gemeldet
  werden die **Felder**, die im Toast nicht erscheinen. Die Behauptung war
  falsch, nicht der Code.

**Was beim Umsetzen dazukam und nicht im Plan stand:**

- **Die Wanderung fragt die Tabelle, nicht die Fassungsnummer.** Eine frisch
  angelegte Datei bringt `seen_at` schon aus `CREATE TABLE` mit, steht aber auf
  `user_version 0`. Ein blindes `ALTER TABLE` wäre dort ein „duplicate column
  name" — beim **ersten Start auf einem neuen Gerät**, also ausgerechnet dort,
  wo nichts zu wandern war. Jetzt `PRAGMA table_info`, und zwei Tests decken
  beide Richtungen ab.
- **Eine Doppelwahrheit ist dabei verschwunden statt entstanden.** Die Wörter
  „verpasst", „angenommen", „besetzt" standen im `HistoryDetailConverter` der
  Oberfläche; mit `call.outcome` hätte es sie zweimal gegeben. Sie stehen jetzt
  einmal in `CallOutcomeText`, und der Konverter ruft sie.
- **`CallFacts` trägt Ergebnis und Richtung als Text**, nicht als
  `CallOutcome`/`CallDirection`. Der Integrationskern soll sich ohne
  Codeänderung herauslösen lassen (ADR-015); ein Verweis auf die Anrufliste
  zöge sie mit.

**Am Gerät abzunehmen: T111 bis T119.** Die drei wichtigsten sind **T112** (der
Zustand überlebt einen Neustart), **T113** (der Bereich wächst, und die Liste
behält ihre Virtualisierung — mit Tab-Wechsel prüfen) und **T116** (eine kaputte
Karte darf nur sich selbst kosten).

---

## 0. Worum es geht

Die Wünsche, in der Reihenfolge, in der sie gestellt wurden:

1. Ein verpasster Anruf, den ich **einmal angeklickt** habe, soll die Zahl im
   Abzeichen kleiner machen.
2. Noch nicht angeklickt heisst **fett**, angeklickt heisst **normal**.
3. Die **Info-Karte** in der Anrufliste soll sich über den Designer anpassen
   lassen.
4. Und die Einstellungen einer **anderen Karte übernehmen** können.
5. Der **Platz** für die Info-Karte soll ausgenutzt werden — sie soll sich
   aufklappen, mit dem Anruftitel.
6. Im Designer: **Abstand oder Linie** zum Trennen von Bereichen, und die
   **Beschriftung ein- und ausblendbar** („Hans Muster" statt „Name: Hans
   Muster").

Fünf davon sind neu zu bauen. **Wunsch 6 ist halb schon da:** die Trennlinie
gibt es (`CardDivider`, Knopf „Linie" im Designer,
`CardDesignerWindow.xaml:152`) — sie ist ein Strich von einem Pixel mit vier
Pixeln Luft und fällt zwischen zwei Zeilen kaum auf. Neu zu bauen ist der
**Abstand**; die Linie bleibt, wie sie ist.

---

## 1. Befunde am Ist-Zustand

| # | Befund | Wo |
|---|---|---|
| **B1** | **Die Anrufliste kennt kein „gesehen".** `CountMissed()` zählt *alle* verpassten Anrufe der Aufbewahrungsfrist — nach einem Jahr steht dort eine dreistellige Zahl, die nie kleiner wird, ausser man löscht die Liste. Das Abzeichen misst damit nicht „ungesehen", sondern „jemals verpasst" | `CallHistoryStore.cs:228` |
| **B2** | **`CallHistoryEntry` ist ein unveränderlicher `record` in einer `ObservableCollection`.** Eine Zeile, die von fett auf normal wechseln soll, hat keinen Weg, das zu melden. Ein Austausch des Eintrags in der Sammlung löst `SelectionChanged` erneut aus — mitten im Handler, der ihn gerade gesetzt hat | `ShellViewModel.cs:156`, `ShellPage.xaml.cs:685` |
| **B3** | **Die Kartenart `History` gibt es seit I4 und sie ist nirgends angeschlossen.** `CardKind.History` steht im Modell mit dem Kommentar „Noch nicht verwendet", `DefaultCards.All` kennt sie nicht, `ReloadCards()` listet drei Arten statt vier. **Dieselbe Lücke wie B1 im K-Plan:** die Fähigkeit ist gebaut, aber nicht verdrahtet | `CardDefinition.cs:22`, `DefaultCards.cs:161`, `IntegrationSettingsViewModel.cs:443` |
| **B4** | **Der Kontextbereich baut seine Felder selbst.** `ApplyHistoryContext` läuft über den Schnappschuss und erzeugt `CallerCardField` mit `Humanize(name)` — also maschinelle Beschriftungen aus dem Feldnamen. Das ist genau die Rückfallebene, die auf der Gesprächskarte am 07.09. als Fehler auffiel („Letzte arbeit zeile"), hier aber der **Normalfall** | `ShellViewModel.HistoryDetails.cs:214` |
| **B5** | **Der Bereich hat eine feste Höhe von 220 Pixeln** und sitzt in einer `Auto`-Zeile unter der Liste. Er wächst nicht, wenn die Liste kurz ist, und schrumpft nicht, wenn sie lang ist. Bei drei Feldern steht er halb leer, bei zehn scrollt er in einem Kasten, während darüber Platz frei ist | `ShellPage.xaml:703` |
| **B6** | **Ein Feld zeigt seine Beschriftung immer.** `BuildField` legt zwei Spalten an, die erste mit `MinWidth = 110` — auch bei leerer Beschriftung bleiben die 110 Pixel stehen. „Nur den Wert" ist heute nicht beschreibbar | `CardView.cs:194` |
| **B7** | **Der Designer kann nur von der eigenen Art lesen.** `CardDesignerViewModel` bekommt eine `CardKind` und holt genau deren Definition. Wer die Gesprächskarte in der Anrufliste wiederhaben will, baut sie nach | `CardDesignerViewModel.cs:97` |
| **B8** | **Auf der Karte gibt es keine Angaben zum Anruf selbst.** Der Namensraum `call` ist im Validator als reserviert geführt (`IntegrationConfigValidator.cs:47`), aber `ContextSnapshot.Resolve` kennt nur `number.*` und die Quellen. Eine Karte in der Anrufliste kann Zeit, Dauer und Ergebnis nicht ansprechen | `ContextSnapshot.cs:194` |

---

## 2. Entscheidungen

Drei gehören als ADR nach `docs/decisions.md`, eine bleibt offen zur Wahl.

### ADR-035 (Vorschlag): „Gesehen" steht in der Datenbank, nicht im Arbeitsspeicher

Ein Zustand, der einen Neustart nicht überlebt, ist für ein Abzeichen
unbrauchbar: nipp läuft im Infobereich und wird selten beendet — aber wenn, ist
die Zahl wieder da, die man weggeklickt hat. Also eine Spalte `seen_at`, und
damit **Schemafassung 2**.

Das ist die erste echte Wanderung dieser Datei. `StampSchemaVersion` ist genau
dafür angelegt worden und hatte bis heute nichts zu tun; hier bekommt sie ihren
Schritt (`ALTER TABLE calls ADD COLUMN seen_at TEXT`). Ein
`CREATE TABLE IF NOT EXISTS` allein würde eine bestehende Datei **unberührt**
lassen und beim ersten Zugriff auf die neue Spalte werfen — der Fall, für den
die Stelle gebaut wurde.

**Datenschutz:** `seen_at` ist ein Zeitstempel, kein Inhalt. ADR-022 und
ADR-027 sind nicht berührt.

### ADR-036 (Vorschlag): Die Anrufliste bekommt eine Karte, der Kopf bleibt Rahmen

Der Kontextbereich wird zur vierten Kartenart. Was **nicht** in die Karte
wandert: die Kopfzeile mit Name, Uhrzeit, Ergebnis und dem Schliessen-Kreuz.

Der Grund ist derselbe wie beim Toast (ADR-034): eine Angabe, die sagt,
**welcher Eintrag gerade offen ist**, darf nicht wegkonfigurierbar sein. Der
Bereich steht unter der Liste und nicht in der Zeile — nimmt man ihm den Titel,
zeigt er Werte ohne Bezug. Alles darunter ist gewöhnliche Kartenbeschreibung.

### ADR-037 (Vorschlag): Abstand als Baustein mit drei Grössen, nicht mit Pixeln

`CardSpacer` mit `Small` / `Medium` / `Large` (4 / 12 / 24). Keine Zahl:
§21.2 beschreibt Layout über Struktur und nicht über Pixelkoordinaten, und ein
Feld für Pixel wäre die erste Stelle, an der eine Konfigurationsdatei
Bildschirmmasse setzt. Drei Stufen decken „Luft zwischen zwei Zeilen",
„Bereichswechsel" und „deutliche Trennung" ab.

Die Beschriftung wird über `CardField.ShowLabel` (Vorgabe `true`) abgeschaltet,
**nicht** über eine leere Beschriftung. Zwei Gründe: die leere Zeichenkette
liesse offen, ob jemand die Beschriftung vergessen oder abgewählt hat, und die
Beschriftung wird auch bei abgeschalteter Anzeige noch gebraucht — als
`AutomationProperties.Name` am Wert. Eine Sprachausgabe soll „Firma: Muster AG"
sagen, auch wenn dasteht „Muster AG" (§8.4).

### Offen: wie weit klappt der Bereich auf?

Drei Bauarten, eine Empfehlung:

| | Was passiert | Kosten |
|---|---|---|
| **A** | Aufklappbereich **in der Zeile** | Verworfen — die Begründung steht schon im XAML: die Zeilen brauchen einen Chevron, die Liste bricht bei jedem Öffnen um, und der Kontext steht jedes Mal woanders |
| **B** *(empfohlen)* | Der Bereich unter der Liste **wächst**: Liste behält `*` mit `MinHeight`, der Bereich bekommt bis zu 60 % der Panelhöhe, mit eigenem Scrollbereich und Schliessen-Kreuz | Zwei Zeilendefinitionen und ein `SizeChanged`. Die Liste behält ihre Virtualisierung, weil sie in ihrer eigenen Zeile bleibt |
| **C** | Der Eintrag **ersetzt** die Liste (Detailansicht mit Zurück) | Der ganze Platz, aber die Liste ist weg — Durchklicken durch mehrere verpasste Anrufe wird zum Hin und Her |

**B** hält die Entscheidung von damals („eine feste Stelle statt jedes Mal
woanders") und erfüllt trotzdem den Wunsch. Wer C will, sagt es vor L4 — danach
ist es ein zweiter Umbau.

**Und eine Falle, die hier scharf ist:** zwei Listen in einem gemeinsamen
`ScrollViewer` verlieren die Virtualisierung (CLAUDE.md, „WinUI und XAML"). Der
Kartenbereich bekommt deshalb seinen **eigenen** `ScrollViewer` in einer in der
Höhe begrenzten Zeile; die Anrufliste behält ihren. Am Gerät nachzuprüfen, weil
der Fehler kein Absturz ist, sondern ein Ruckeln.

---

## 3. Die Phasen

Sieben, in dieser Reihenfolge. L0–L1 und L5 sind voneinander unabhängig; L3
braucht L2, L4 braucht L3.

### L0 — „Gesehen" in der Datenbank

**Ziel:** Die Datenbank weiss, welcher verpasste Anruf schon angesehen wurde.

| Datei | Was |
|---|---|
| `Services/History/CallHistoryStore.cs` | `SchemaVersion = 2`, Wanderung 1 → 2 mit `ALTER TABLE`; `MarkSeen(long id)`; `MarkAllSeen()`; `CountMissed()` zählt nur noch `outcome = Missed AND seen_at IS NULL`; `Read()` liest die Spalte |
| `Services/History/CallHistoryEntry.cs` | `DateTimeOffset? SeenAt = null` am Ende der Parameterliste; `bool IsNew => Outcome == CallOutcome.Missed && SeenAt is null` |

**Warum nur verpasste Anrufe „neu" sind:** Sonst wäre jeder selbst gewählte
Anruf fett, und die Fettschrift sagte etwas anderes als das Abzeichen daneben.
Zwei Anzeigen für denselben Sachverhalt, die sich unterscheiden — das ist die
Bauart, die schon einmal einen unsichtbaren eingehenden Anruf gekostet hat.

**Abnahme:** `CallHistoryStoreTests` — eine Datei mit Fassung 1 und Einträgen
darin bekommt die Spalte und behält ihre Zeilen; `MarkSeen` senkt `CountMissed`
um genau eins; ein zweites `MarkSeen` auf dieselbe Kennung ändert nichts;
angenommene und ausgehende Anrufe werden von `MarkSeen` nicht angerührt.

### L1 — Fett in der Liste, und das Abzeichen zieht nach

**Ziel:** Ein Klick auf einen verpassten Anruf macht ihn normal und die Zahl um
eins kleiner.

Der Weg dorthin ist B2 — und der ist der eigentliche Aufwand dieser Phase.
`CallHistoryEntry` bleibt ein unveränderlicher `record`; die Liste bindet
künftig auf **`HistoryRow : ObservableObject`**, wie `TeamContacts` schon auf
`ContactRow` bindet. Das Muster ist im Haus, und es löst das Problem an der
richtigen Stelle: die Zeile meldet ihre Änderung selbst, statt dass die Sammlung
ihren Eintrag austauscht und dabei die Auswahl umwirft.

| Datei | Was |
|---|---|
| `ViewModels/HistoryRow.cs` *(neu)* | Hüllt `CallHistoryEntry`, reicht `DisplayLabel`, `Outcome`, `StartedAt` durch, hält `[ObservableProperty] bool IsNew` |
| `ViewModels/ShellViewModel.cs` | `History` wird `ObservableCollection<HistoryRow>`; `ToggleHistoryDetails` ruft `_history.MarkSeen`, setzt `row.IsNew = false` und `MissedCount = _history.CountMissed()`; Befehle (`CallBack`, `AddToTeam`, `OpenRecording`) nehmen `HistoryRow` |
| `ViewModels/ShellViewModel.Search.cs` | Die Vorschlagsliste liest aus `Query(...)`, nicht aus `History` — prüfen und gegebenenfalls nachziehen |
| `Views/ShellPage.xaml` | `x:DataType="vm:HistoryRow"`; `FontWeight` der Namenszeile über einen Konverter an `IsNew` |
| `Views/ShellPage.xaml.cs` | Sechs Handler: `SelectedItem as HistoryRow` |
| `Converters/DisplayConverters.cs` | `BoolToFontWeightConverter` (fett / normal) |

**Dazu ein Menüpunkt „Alle als gesehen markieren"** im Kontextmenü der Liste.
Er kostet vier Zeilen und ist der einzige Weg, ein Abzeichen mit dreissig alten
Einträgen loszuwerden, ohne die Anrufliste zu löschen.

**Abnahme:** Ein Test im Kern über `ShellViewModel` — Zeile auswählen, `IsNew`
ist falsch und `MissedCount` um eins kleiner; zweite Auswahl derselben Zeile
ändert die Zahl nicht. Die Fettschrift selbst ist am Gerät zu sehen (T111).

### L2 — `call.*` im Schnappschuss

**Ziel:** Eine Karte kann Zeit, Dauer, Ergebnis und Richtung des Anrufs
ansprechen.

`ContextSnapshot` bekommt eine optionale `CallFacts` — **genau so, wie `Number`
schon eine ist.** Kein synthetisches Quellenfragment mit der Kennung `call`:
das erschiene in `SourcesByPriority`, würde bei `role()` mitgewichtet und stünde
in der Zustandsliste als Quelle, die niemand eingerichtet hat.

| Datei | Was |
|---|---|
| `Context/ContextSnapshot.cs` | `record CallFacts(DateTimeOffset StartedAt, TimeSpan? Duration, CallOutcome Outcome, CallDirection Direction)`; `Resolve` kennt `call.startedAt`, `call.time`, `call.date`, `call.duration`, `call.outcome`, `call.direction` |
| `Cards/FieldCatalog.cs` | Sechs Einträge in der Gruppe „Anruf" — damit stehen sie in der Palette des Designers und in `feldnamen.md` |
| `docs/integrations/feldnamen.md` | wird erzeugt, nicht geschrieben |

**Datum nur in ISO-Form** und Dauer als Text (`mm:ss`): `06.09.2026` ist
anderswo der 9. Juni, und eine Dauer als Zahl liesse `> 300` zu, was auf einer
Karte niemand meint.

**Abnahme:** `ContextSnapshotTests` — jeder der sechs Pfade löst auf, ein
unbekannter `call.*`-Pfad ist `Null` und kein Fehler; `FieldCatalogTests` hält
Katalog und erzeugte Doku zusammen (der Test steht schon).

### L3 — Die Karte der Anrufliste

**Ziel:** Der Kontextbereich zeichnet eine Karte, und die Karte lässt sich im
Designer bearbeiten.

| Datei | Was |
|---|---|
| `Cards/CardDefinition.cs` | Kommentar an `CardKind.History` — sie ist jetzt verwendet |
| `Cards/DefaultCards.cs` | `History` als mitgelieferte Definition, in `All` aufgenommen. Inhalt: Firma, Art, letzte Arbeit mit Kollege, letztes Gespräch, offene Punkte — über `role()` und `anyOf()`, **ohne Quellennamen** (der Fehler vom 07.09.). Ohne Namenszeile: die steht im Kopf des Bereichs |
| `ViewModels/IntegrationSettingsViewModel.cs` | Vierte Art in `ReloadCards()` und `KindName` |
| `ViewModels/ShellViewModel.HistoryDetails.cs` | `HistoryContextFields` entfällt; stattdessen `CardModel HistoryCard`, gebaut mit `CardLayoutEngine.Build(_cards.For(CardKind.History), snapshot)`. `HistoryContextSources` und der Platzhalter bleiben, wie sie sind |
| `ViewModels/ShellViewModel.cs` | `CardResolver? cards = null` als optionaler Konstruktorparameter — dasselbe Muster wie `search` und `callerContext`; auf `CardResolver.Changed` neu bauen |
| `Views/ShellPage.xaml` | Der `ItemsControl` über `HistoryContextFields` weicht einer `CardView`; `ActionInvoked` wird wie in `ActiveCallPage` verdrahtet (Wählen, Kopieren, Adresse öffnen) |

**Was das kostet und warum es trotzdem richtig ist:** Die Karte wird bei jeder
Antwort einer Quelle neu gebaut, auf dem Thread, der alle 20 ms
`Core.Iterate()` bedient. Das ist dieselbe Last wie in der Gesprächsansicht —
ein Dutzend Zeilen, zwei- bis viermal je Abruf — und dort seit dem 06.09. am
Gerät bestätigt.

**ADR-027 bleibt unangetastet:** abgerufen wird beim Aufklappen, gespeichert
wird nichts. `HistoryStoresNoContextTests` muss unverändert grün bleiben; wenn
diese Phase ihn anfasst, ist etwas falsch.

**Abnahme:** `CardConfigurationTests` — die mitgelieferte `History`-Karte
übersetzt fehlerfrei und nennt keine Quellenkennung; `CardResolver` liefert für
`History` eine Karte statt einer leeren; ein Rundlauf durch
`IntegrationConfigStore` über alle **vier** Arten.

### L4 — Der Bereich klappt auf

**Ziel:** Bauart **B** aus Abschnitt 2.

| Datei | Was |
|---|---|
| `Views/ShellPage.xaml` | Zeilen des `HistoryPanel`: Filter `Auto`, Liste `*` mit `MinHeight`, Kartenbereich `Auto` mit gebundener `MaxHeight`. Kopfzeile: Name fett, Uhrzeit und Ergebnis klein daneben, Schliessen-Kreuz rechts. Eigener `ScrollViewer` für die Karte |
| `Views/ShellPage.xaml.cs` | `MaxHeight` bei `SizeChanged` auf 60 % nachziehen (gerechnet wird auf `ActualHeight`, also logisch — `AppWindow` und seine physischen Pixel kommen hier nicht vor); das Kreuz ruft `CollapseHistoryDetails` und leert die Auswahl |

**Abnahme:** am Gerät (T113, T114). Im Kern nichts zu prüfen — das ist
Geometrie.

### L5 — Der Designer: Abstand, Beschriftung, Übernehmen

Drei unabhängige Stücke, ein Fenster.

**5a — Abstand als Baustein**

| Datei | Was |
|---|---|
| `Cards/CardDefinition.cs` | `CardSpacer` mit `CardSpacerSize`, `[JsonDerivedType(..., "spacer")]` |
| `Cards/CardModel.cs` | `CardSpacerModel(Key, Visible, CardSpacerSize)` |
| `Cards/CardLayoutEngine.cs` | Übersetzen und Bauen — neben `CardDivider`, gleiche Behandlung |
| `Cards/CardDefinitionValidator.cs` | Im Toast harmlos wie die Linie: nicht zählen, nicht melden |
| `Controls/Cards/CardView.cs` | `Border` mit Höhe, ohne Hintergrund; `AutomationProperties.AccessibilityView = Raw` — ein Abstand hat für eine Sprachausgabe nichts zu sagen |
| `ViewModels/CardDraft.cs` | `DraftElementKind.Spacer`, Hin- und Rückweg, `Headline` = „Abstand" |
| `Windows/CardDesignerWindow.xaml(.cs)` | Knopf „Abstand" neben „Linie"; im Eigenschaftenbereich die Grösse |

**5b — Beschriftung ein- und ausblenden**

| Datei | Was |
|---|---|
| `Cards/CardDefinition.cs` | `CardField.ShowLabel` (`true`) |
| `Cards/CardModel.cs`, `Cards/CardLayoutEngine.cs` | `CardFieldModel.ShowLabel` durchreichen |
| `Controls/Cards/CardView.cs` | Ohne Beschriftung **eine** Spalte statt zweier — die 110 Pixel `MinWidth` müssen weg, sonst rückt der Wert trotzdem ein. Die Beschriftung wandert als `AutomationProperties.Name` an den Wert |
| `ViewModels/CardDraft.cs` | `DraftElement.ShowLabel`, im Hin- und Rückweg |
| `Windows/CardDesignerWindow.xaml(.cs)` | Haken „Beschriftung zeigen" neben dem Haken für den Gedankenstrich |

**Die Falle steht schon einmal in CLAUDE.md und trifft hier wieder:**
`ShowLabel` hat die Vorgabe `true`, ein `bool` wird von `WhenWritingNull` nicht
verschluckt — das ist zu prüfen, nicht anzunehmen. Der Rundlauftest über eine
Karte mit `showLabel: false` gehört dazu; genau diese Sorte Feld hat beim
`emptyText` eine Karte auf dem Weg durch die Datei ihr Verhalten ändern lassen.

**5c — Von einer anderen Karte übernehmen**

| Datei | Was |
|---|---|
| `ViewModels/CardDesignerViewModel.cs` | `IReadOnlyList<CardKind> CopySources` (die anderen drei Arten, je mit „eigene"/„mitgeliefert"); `[RelayCommand] void CopyFrom(CardKind source)` — übernimmt **nur die Abschnitte**, behält Kennung, Name und Art der bearbeiteten Karte, geht durch `Record()` und ist damit rückgängig zu machen |
| `Windows/CardDesignerWindow.xaml(.cs)` | Knopf „Von anderer Karte übernehmen …" mit Auswahl |

**Kennung und Name bleiben — und das ist kein Detail:** Würde die Kopie ihre
Kennung mitbringen, stünden zwei Karten mit derselben in der Datei, und der
Validator meldete „Die Kennung kommt mehrfach vor". Der Benutzer hätte einen
Befund für etwas, das er nicht getan hat.

**Nach dem Übernehmen kann die Karte Befunde haben** — der Toast nimmt drei
Textzeilen, die Gesprächskarte hat mehr. Das ist richtig so: Speichern ist
gesperrt, unten steht, was zu viel ist, und es lässt sich löschen. Ein
Übernehmen, das stillschweigend kürzt, wäre schlimmer.

**Abnahme (L5):** `CardDesignerViewModelTests` — Übernehmen behält Kennung und
Art, ist ein Undo-Schritt, und aus der Gesprächskarte in den Toast übernommen
sperrt es das Speichern mit dem Hinweis auf drei Zeilen.
`CardLayoutEngineTests` — Abstand und `showLabel` überstehen den Rundlauf
zeichengleich. `ToastCardTests` — ein Abstand in einer Toast-Karte wird
übergangen, nicht gemeldet.

### L6 — Doku, ADRs, Testmatrix

| Datei | Was |
|---|---|
| `docs/decisions.md` | ADR-035, ADR-036, ADR-037 |
| `docs/integrations/karten.md` | Vierte Kartenart, Abstand, `showLabel`, Übernehmen, `call.*` |
| `docs/integrations/feldnamen.md` | neu erzeugt (L2) |
| `docs/test-matrix.md` | T111 bis T119 |
| `CLAUDE.md` | Meilenstein, „Vorgeschichte", und in „Lehren": die Wanderung der Anrufliste — die erste, die `StampSchemaVersion` wirklich braucht |
| `ABNAHME-ALLTAG.md` | Was am Gerät neu zu beachten ist |
| dieses Dokument | Umsetzungsstand ganz vorn, wie im K-Plan |

---

## 4. Was am Gerät zu prüfen ist

Neu auf der Testmatrix, im Anschluss an T110:

| # | Fall | Erwartung |
|---|---|---|
| **T111** | Zwei Anrufe verpassen lassen, dann **einen** anklicken | Das Abzeichen geht von 2 auf 1, die angeklickte Zeile ist normal, die andere bleibt fett |
| **T112** | Danach nipp **beenden und neu starten** | Die Zahl ist 1 und nicht 2. Das ist der eigentliche Test von ADR-035 — im Arbeitsspeicher hätte alles davor auch funktioniert |
| **T113** | Einen Eintrag mit viel Kontext öffnen, dann einen ohne | Der Bereich wächst und schrumpft; die Liste bleibt bedienbar und behält ihre Mindesthöhe. **Auf den Kontakte-Tab wechseln und scrollen** — ruckelt es, hat die Liste ihre Virtualisierung verloren |
| **T114** | Bereich bei **150 % Skalierung**, hell und dunkel | Kopfzeile, Karte und Schliessen-Kreuz sind vollständig sichtbar |
| **T115** | Karte der Anrufliste im Designer ändern, speichern, Eintrag öffnen | Es steht da, was im Designer stand — und nach einem Neustart weiterhin |
| **T116** | Diese Karte **kaputt machen** (`coalesce((` von Hand in `integrations.json`) | Der Bereich zeigt die mitgelieferte Karte, in den Einstellungen steht ein Befund mit `cards[...]`. **Und die Anrufliste selbst funktioniert weiter** — eine kaputte Karte darf nur sich selbst kosten (wie T104) |
| **T117** | **Gesprächskarte in die Anrufliste übernehmen** | Die Abschnitte sind da, Kennung und Name der Anruflisten-Karte sind geblieben, ein Rückgängig stellt den alten Stand her |
| **T118** | Gesprächskarte in den **Toast** übernehmen | Speichern ist gesperrt mit dem Hinweis auf drei Textzeilen; nach dem Löschen der überzähligen Zeilen geht es |
| **T119** | Abstand und Beschriftung: eine Zeile ohne Beschriftung, darunter ein grosser Abstand | Der Wert steht **links am Rand** und nicht eingerückt; **Sprachausgabe prüfen** — sie muss die Beschriftung trotzdem nennen |

T111 bis T113 gehen ohne Anlage, wenn man verpasste Anrufe von Hand in die
Datenbank schreibt — für T112 ist das sogar der schnellere Weg.

---

## 5. Risiken

| Risiko | Warum es zählt | Was dagegen |
|---|---|---|
| **Die Wanderung der Datenbank** | Die Anrufliste eines Jahres ist die einzige Nutzerdatei, die nipp nicht wiederherstellen kann | Ein Test, der eine echte Fassung-1-Datei mit Zeilen öffnet. Und vor der ersten Ausführung am Gerät `history.db` kopieren |
| **`SelectionChanged` und der Zeilenaustausch** | Ein Austausch mitten im Handler wirft die Auswahl um — der Kontextbereich schlösse sich beim Anklicken wieder | `HistoryRow` als Hülle; die Sammlung wird nicht angefasst |
| **Virtualisierung** | Der Fehler ist kein Absturz, sondern ein Ruckeln beim Wechsel auf den Kontakte-Tab, auf demselben Thread, der das SDK bedient | T113 ausdrücklich mit Tab-Wechsel |
| **Fünf neue Felder in der Kartenbeschreibung** | Jedes ist eine Stelle, an der eine Karte auf dem Weg durch die Datei etwas verliert | Rundlauf über alle vier Arten, mit `showLabel: false` und `emptyText: null` in derselben Karte |
| **`Nipp.App` hat kein Testprojekt** | L4 ist reine Oberfläche und nur am Gerät prüfbar | Deshalb liegt alles andere im Kern — und L4 ist bewusst die kleinste Phase |

---

## 6. Reihenfolge und Umfang

| Phase | Umfang | Hängt an |
|---|---|---|
| **L0** „Gesehen" in der Datenbank | klein | — |
| **L1** Fett und Abzeichen | **mittel** — die Hülle zieht sechs Handler und drei Befehle nach | L0 |
| **L2** `call.*` | klein | — |
| **L3** Karte der Anrufliste | **mittel** | L2 |
| **L4** Aufklappen | klein, aber nur am Gerät abzunehmen | L3 |
| **L5** Designer (a/b/c) | **mittel** — drei unabhängige Stücke | — |
| **L6** Doku, ADRs, Matrix | klein | alle |

Ein Commit je Phase, wie bei K0–K6. **L0/L1 und L5 sind voneinander
unabhängig** — wer die Wünsche einzeln abnehmen will, fängt mit L0/L1 an: das
ist der Wunsch, der jeden Tag auffällt.

**Vor L4 zu entscheiden:** Bauart B oder C (Abschnitt 2).
