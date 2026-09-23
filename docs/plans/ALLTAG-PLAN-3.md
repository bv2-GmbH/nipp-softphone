# Drei Meldungen aus dem Alltag — 23.09.2026

**Anlass:** drei Meldungen aus der Benutzung, am Abend des ersten Tages an der
Anlage. Alle drei sind Bedienung, keine Telefonie.

**Stand vor diesem Plan:** Zweig `main`, HEAD `e80c3f4`, Arbeitsbaum sauber.

**Vorgehen:** erst der Befund mit Datei und Zeile, dann die Entscheidung, dann
der Weg. Was noch nicht gemessen ist, steht als Gegenprobe da und nicht als
Annahme im Text.

**Prüfzeilen:** T324 bis T329 in `docs/test-matrix.md`, alle mit Rüstzeug **S** —
der ganze Plan ist am Schreibtisch nachzumessen, kein Anruf, kein Gerät.

---

## Die drei Meldungen

| | Meldung | Befund | Stand |
|---|---|---|---|
| **A** | Ein Suchtreffer mit zwei Nummern zeigt die zweite nicht, und auswählen geht nicht | Der Trefferliste fehlt **eine Zeile XAML**: `SelectionChanged` | **umgesetzt und gemessen** — T324, T325 |
| **B** | Beim Bearbeiten einer Nebenstelle muss man ganz nach unten scrollen | Das Formular steht unter einer ungedeckelten Liste, und der Stift bewegt weder Bildlauf noch Fokus. **Zweimal**: Konten und Nebenstellen | **umgesetzt und gemessen** — T326 |
| **C** | Die Liste der zuletzt gewählten Nummern bleibt offen, wenn man in ein anderes Fenster klickt | Es gibt **keinen** Auslöser, der sie schliesst — ausser einem zweiten Druck auf denselben Knopf | **umgesetzt und gemessen** — T327 bis T329 |

---

## Umsetzungsstand (23.09.2026, am selben Abend)

**Alle drei sind gebaut.** Build ohne Warnungen, **1274 Komponententests** und
**37 Architekturtests** grün. Danach an der laufenden Anwendung nachgemessen —
mit einer **echten Maus**, nicht nur über UI Automation, und das war bei C
entscheidend: ein `InvokePattern` bewegt den Fokus gar nicht und hätte die
Falle aus ADR-044 nie gestellt.

**Sechs Prüfzeilen, sechs bestanden** (T324–T329). Die Matrix steht damit auf
**137 offen von 319** — nachgerechnet mit `Zaehle-Matrix.ps1`, nicht
fortgeschrieben.

**Was die Messung darüber hinaus beantwortet hat:**

- **T325 war die offene Frage des Plans, und die Antwort ist: die Quelle ist in
  Ordnung.** Ein Treffer mit «· +1 Nummer» war zu finden, die Anbietervorlage
  bildet beide Nummern ab. **Die Ursache der Meldung lag vollständig in der
  Oberfläche** — die fehlende Zeile XAML, sonst nichts. Dass die meisten
  Kontakte dieser Quelle nur eine Nummer haben, machte den Befund schwerer
  auffindbar, war aber nicht seine Ursache.
- **Bei B war die Hälfte schon sichtbar, bevor der Stift gedrückt wurde:** mit
  elf Nebenstellen stand `TeamNameBox` im UIA-Baum auf `sichtbar = False`,
  also ausserhalb des Fensters. Nach dem Klick steht es bei Y=1153 im Bild, mit
  dem Fokus darin.
- **Die Gegenprobe zu ADR-045 ist gelaufen:** nach «Abbrechen» stehen in
  `settings.json` unverändert 1 Konto, 11 Nebenstellen und 6 `UserOverrides`.
  Der programmgesteuerte Fokuswechsel löst `ApplyEdits` aus und hat nichts
  verloren.

**Zwei Kommentare, die etwas Falsches behaupteten, sind mitgezogen** —
`ShowRecentlyDialed` («gerufen, wenn das Nummernfeld den Fokus bekommt») und
`HideRecentlyDialed` («wenn das Feld den Fokus verliert»). Beide beschrieben
einen Zustand, den §22.2 abgelöst hatte; der zweite beschrieb obendrein genau
die Mechanik, die dieser Plan erst gebaut hat.

---

## A — Die zweite Nummer eines Suchtreffers

### Befund

Alles dafür ist gebaut, angeschlossen ist es nicht.

- `ContactRow.Choices` trägt **alle** Nummern des Kontakts, mit deutscher
  Benennung ihrer Art; `MoreNumbersHint` setzt «+1 Nummer» hinter die zweite
  Zeile.
- Die Trefferzeile `NippSearchResultTemplate`
  (`src/Nipp.App/Views/ShellPage.xaml:358`) trägt den aufklappbaren Bereich
  bereits — `SearchRowDetails` hängt an
  `x:Load="{x:Bind IsDetailExpanded, Mode=OneWay}"` und füllt sich aus
  `NippContactDetailTemplate`, derselben Vorlage wie bei Team und Outlook.
- `ShellPage.UpdateContactDetails` (`ShellPage.xaml.cs:1628`) zählt
  `SearchResultList` schon mit auf, `ShellViewModel.ReattachContactDetails`
  ebenfalls.

**Was fehlt, ist die Zeile, die das Ganze auslöst.** `TeamList`
(`ShellPage.xaml:1362`) und `OutlookList` (`:1434`) tragen
`SelectionChanged="OnContactSelectionChanged"`, **`SearchResultList` (`:1493`)
nicht.** Ein Klick auf einen Suchtreffer wählt die Zeile aus und tut sonst
nichts; `ToggleContactDetails` wird nie gerufen, der Bereich klappt nie auf.

**Und der Kommentar behauptet das Gegenteil.**
`ShellViewModel.ContactDetails.cs` sagt ausdrücklich: „die Zeile klappt auf,
für Nebenstellen wie für Outlook-Kontakte **und Suchtreffer**". Das ist genau
die Sorte Satz, die CLAUDE.md meint — er hat die Stelle beschrieben, die
niemand mehr nachgesehen hat.

**Erreichbar ist die zweite Nummer heute nur über den Doppelklick:**
`CallOrAsk` (`ShellPage.xaml.cs:562`) baut bei mehreren Nummern ein Menü. Das
ist ein Weg, aber er **wählt sofort** — wer nur nachsehen will, welche Nummern
es gibt, muss ein Anrufmenü öffnen und wieder wegklicken.

### Entscheidung

Angeschlossen wird die vorhandene Mechanik, **nicht eine zweite gebaut**. Der
Ort des Detailbereichs ist seit ADR-048 entschieden und für alle drei Listen
derselbe; was hier fehlt, ist kein Entwurf, sondern ein Anschluss.

### Weg

1. `SelectionChanged="OnContactSelectionChanged"` an `SearchResultList`
   (`ShellPage.xaml:1493`).
2. In `OnContactSelectionChanged` (`ShellPage.xaml.cs:1589`) die
   Quersynchronisation auf Team und Outlook beschränken. Sie steht heute als
   `(ReferenceEquals(sender, TeamList) ? OutlookList : TeamList).SelectedIndex = -1`
   da und würde bei der Suchliste die Team-Auswahl löschen. **Sichtbaren
   Schaden gäbe das nicht** — `SearchBody` und `ContactsBody` schliessen sich
   aus (`ShellPage.xaml.cs:829`) —, aber es wäre eine Aussage, die nicht
   stimmt: die Suchliste hat keine Schwesterliste, die danebensteht.
3. Den Kommentar in `ShellViewModel.ContactDetails.cs` stehen lassen — er wird
   mit Punkt 1 zum ersten Mal wahr.

### Gegenprobe, die **vor** dem Bauen fällig ist

**Die Meldung könnte eine zweite Ursache haben, und die läge nicht in nipp.**
`HttpContactSearchProvider.ReadNumbers` sammelt jedes Feld, dessen Name mit
`phone` beginnt (`…/Search/HttpContactSearchProvider.cs:215`); liefert die
Vorlage des CRM nur **ein** solches Feld, hat der Kontakt in nipp auch nur
eine Nummer, und kein Aufklappen der Welt zeigt eine zweite.

**Woran man es sieht, ohne den Code zu lesen:** steht in der zweiten Zeile des
Treffers hinter der Nummer «+1 Nummer», hat nipp beide (T325). Steht es nicht
da, ist die **Anbietervorlage** zu ergänzen — sie liegt im privaten
Vorlagen-Repo, nicht hier (ADR-040).

Beide Ursachen können zugleich vorliegen. Punkt A wird unabhängig davon
gebaut: der Anschluss fehlt so oder so.

---

## B — Das Formular kommt zum Benutzer, nicht umgekehrt

### Befund

Zwei Gruppen der Einstellungsseite stellen das Formular **unter** die Liste,
die es bearbeitet, und beide Listen sind ungedeckelt:

| Gruppe | Liste | Formular beginnt bei | Abstand |
|---|---|---|---|
| SIP-Konten | `SettingsPage.xaml:149` | `AccountFormTitle`, `:208` | eine Zeile je Konto |
| Team-Nebenstellen | `:603` | `TeamNameBox`, `:675` | **eine bis vier Zeilen je Nebenstelle** |

Eine Nebenstelle belegt bis zu fünf Textzeilen (Name, Nummer, SIP-Adresse,
Mobil, Gruppe — jede nur, wenn sie gesetzt ist). Bei den vierzig Nebenstellen,
mit denen hier geprüft wird, liegen zwischen dem Stift und dem Feld, das er
füllt, gut tausend Pixel.

**Und der Stift bewegt nichts.** `OnEditTeamMemberClick`
(`SettingsPage.xaml.cs:224`) und `OnEditAccountClick` (`:307`) führen das
Kommando aus und kehren zurück — kein Bildlauf, kein Fokus. Wer drückt, sieht
nichts passieren und muss selbst suchen.

**Der Hausvorschlag steht schon in derselben Datei:**
`IntegrationSourceList` (`:1114`) trägt `MaxHeight="180"`, und das Detail steht
direkt darunter. Derselbe Fall, dort gelöst.

**Was sonst noch angesehen wurde** (die Meldung fragte danach):

- **Anruferkarte** (`:490`): in Ordnung. Bearbeiten öffnet ein eigenes Fenster,
  der Grund steht dabei.
- **Codecs** (`:951`): in Ordnung, feste kurze Liste ohne Formular.
- **Integrationsquellen** (`:1112`) und **Testfelder** (`:1371`): gedeckelt.
- **Zugangsdaten** (`SecretList`, `:1244`): ungedeckelt, aber ohne Formular
  darunter — die Felder **sind** die Liste. Kein Handlungsbedarf.

Es sind also genau die zwei.

### Entscheidung

**Ein Formular je Gruppe, und es bleibt, wo es ist.** Ein Dialog fürs
Bearbeiten wäre der naheliegende Griff und baut die Doppelwahrheit ein, die
dieses Projekt an anderen Stellen gerade aufgelöst hat: dann stünde das
Formular zweimal da — einmal unten fürs Hinzufügen, einmal im Dialog fürs
Bearbeiten —, und beim nächsten Feld würde eines davon vergessen.

Stattdessen drei kleine Schritte, die zusammen den Weg kurz machen.

### Weg

1. **Beide Listen deckeln.** Ein `ItemsControl` scrollt nicht von selbst,
   also je in einen `ScrollViewer` mit `MaxHeight` — Konten 200, Nebenstellen
   260 (logische Pixel; die Maschine hier steht auf 150 %). Damit steht das
   Formular **immer** unmittelbar unter der Liste, unabhängig davon, wie viele
   Einträge es gibt. Das ist die Hälfte, die auch das Hinzufügen kürzer macht.
2. **Der Stift bringt das Formular ins Bild und den Fokus hinein.** In
   `OnEditTeamMemberClick` nach dem Kommando `TeamNameBox.StartBringIntoView()`
   und `TeamNameBox.Focus(FocusState.Programmatic)`, in `OnEditAccountClick`
   dasselbe mit `UsernameBox`.
   **Zwei Dinge sind dabei zu beachten:**
   - Der Fokuswechsel löst `FocusManager.LostFocus` aus und damit
     `ViewModel.ApplyEdits()` (`SettingsPage.xaml.cs:1441`). Das ist harmlos —
     es schreibt den Stand zurück, der ohnehin gilt —, aber es ist die Stelle,
     an der ADR-045 hängt, und sie wird am laufenden Programm nachgemessen,
     nicht nur im Test.
   - Die Gruppe ist beim Klick schon offen; ein `TryEnqueue` auf den nächsten
     Durchlauf wäre trotzdem die falsche Wette (Befunde A1-6 und A1-10).
     Gebraucht wird hier keiner: der Baum steht.
3. **Eine Überschrift über dem Team-Formular**, die sagt, was es gerade ist.
   Bei den Konten steht sie seit jeher da und wird nachgeführt
   (`SettingsPage.xaml.cs:343`: «Konto bearbeiten» gegen «Konto hinzufügen»),
   beim Team gibt es sie nicht. Wortlaut analog: «Nebenstelle hinzufügen»
   gegen «Nebenstelle bearbeiten». Der Knopf darunter trägt den Zustand schon
   (`TeamActionLabel`, «Hinzufügen» gegen «Übernehmen»), aber er steht am
   **Ende** des Formulars — also hinter allem, was man liest, bevor man
   stutzig wird.

---

## C — Die Wahlwiederholung geht nur zu, wenn man denselben Knopf nochmal drückt

### Befund

`HideRecentlyDialed` (`ShellViewModel.cs:1707`) trägt den Kommentar „Räumt die
Liste weg, wenn das Feld den Fokus verliert". **Gerufen wird sie aus genau
einer Stelle:** `OnRecentClick` (`ShellPage.xaml.cs:313`), als Umschalter am
Verlaufsknopf. Es gibt in `ShellPage` weder einen Fokus- noch einen
Fensterbehandler.

Dasselbe eine Zeile höher: `ShowRecentlyDialed` (`:1691`) sagt „gerufen, wenn
das Nummernfeld den Fokus bekommt (§22.2)". Auch das stimmt nicht mehr — seit
§22.2 geht die Liste **auf Klick** auf, und der Kommentar beschreibt den
Zustand davor. **Zwei Kommentare, die beide eine Mechanik behaupten, die es
nicht gibt**; beide gehören mitgezogen.

**Die Folge im Alltag:** die Liste liegt über den Kontakten und schiebt sie
weg. Wer sie aufmacht, in ein anderes Fenster wechselt und zurückkommt, findet
sie unverändert offen vor.

**Was die Meldung sonst noch sagt** — «eigentlich sollte die Liste nur aufgehen,
wenn ich den Knopf anklicke» — **gilt schon**: `ShowRecentlyDialed` hat genau
einen Aufrufer, und das ist der Knopf. Zu tun ist nur das Gegenstück.

### Entscheidung

**Drei Auslöser, eine Stelle, die entscheidet.** Was „zumachen" heisst, steht
weiter im Kern (`HideRecentlyDialed` prüft, dass das Feld leer ist); die Seite
sagt nur, **wann**.

**Die Grenze ist bewusst gezogen:** geschlossen wird nur die
Wahlwiederholung — die Liste bei **leerem** Feld. Die Vorschläge, die beim
Tippen aufgehen (AP4.3), bleiben unberührt; sie gehören zu dem, was im Feld
steht, und entstehen mit dem nächsten Tastendruck ohnehin neu. Diese Grenze
trägt `HideRecentlyDialed` schon von sich aus. Soll sie anders liegen, ist das
eine eigene Entscheidung und kein Nebeneffekt dieser.

### Weg

Eine private `SchliesseWahlwiederholung()` in `ShellPage`, die
`ViewModel.HideRecentlyDialed()` ruft, und ein Helfer
`LiegtInDerWahlwiederholung(DependencyObject?)`, der den Elternbaum
hinaufgeht und gegen drei Elemente prüft: `NumberBox`, `RecentButton`,
`SuggestionPanel`. Daran hängen drei Auslöser:

1. **Das Fenster verliert den Vordergrund.** `Window.Activated` mit
   `WindowActivationState.Deactivated`. Ans Fenster kommt die Seite über
   `((App)Application.Current).MainWindowRef` (`App.xaml.cs:57`, `internal`,
   dieselbe Assembly). **Abgemeldet wird im `Unloaded`** — die Seite hat den
   Haken schon (`ShellPage.xaml.cs:223`), und ein Ereignis am Fenster
   überlebt sie sonst, dieselbe Falle wie bei `FocusManager.LostFocus` in
   `SettingsPage.OnUnloaded`.
2. **Der Fokus verlässt Feld, Knopf und Liste.** `FocusManager.LostFocus`,
   statisch und anwendungsweit, wie in `SettingsPage`. Geprüft wird
   `e.NewFocusedElement`.
   **Der `RecentButton` muss in die Ausnahme**, und das ist kein Detail:
   Windows setzt den Fokus beim Mausklick auf den Knopf, **bevor** `Click`
   feuert (ADR-044, Nachtrag vom 21.09.2026). Stünde er nicht darin, schlösse
   der Fokusverlust die Liste, `OnRecentClick` sähe danach
   `HasSuggestions == false` und machte sie sofort wieder auf — **der Knopf
   ginge nie wieder zu.** Genau dieselbe Falle, die die Wähltastatur eine
   Ziffer gekostet hat.
3. **Ein Klick auf eine Fläche, die den Fokus gar nicht bewegt.** Ein Klick
   auf einen nicht fokussierbaren Bereich lässt den Fokus stehen, Auslöser 2
   greift dort also nicht. Dafür `PointerPressed` am Seitenrahmen, angehängt
   über `AddHandler(…, handledEventsToo: true)` — dasselbe Muster und
   derselbe Grund wie beim Ziehen (ADR-065): die Listen markieren den Druck
   als behandelt.

**Was dabei weiter funktionieren muss** (T328): ein Klick **in** die Liste
übernimmt die Nummer, ohne zu wählen — der Fokus landet auf dem
`ListViewItem` innerhalb von `SuggestionPanel`, also greift keiner der drei
Auslöser, bevor `OnSuggestionTapped` gelaufen ist.

---

## Was am Schluss zu prüfen ist

| | Zeile | Worum es geht |
|---|---|---|
| A | **T324** | Suchtreffer anklicken → der Bereich klappt in der Zeile auf, beide Nummern einzeln wählbar |
| A | **T325** | Gegenprobe an der Quelle: steht «+1 Nummer» überhaupt da? |
| B | **T326** | Stift an einer Nebenstelle → Formular sichtbar, Fokus im Feld, Überschrift nennt den Zustand |
| C | **T327** | Wahlwiederholung offen, in ein anderes Fenster klicken → zu |
| C | **T328** | Gegenprobe: der Klick **in** die Liste übernimmt weiter, der Knopf schliesst weiter |
| C | **T329** | Gegenprobe: die Vorschläge beim Tippen bleiben, wo sie sind |

Dazu, nicht als Zeile, aber vor dem Commit: `.\build.ps1 test Nipp.sln` —
einschliesslich `PublicRepositoryTests`, `UserTextTests` (die neue Überschrift
aus B3 ist Benutzertext, also Umlaute und `«…»`) und der beiden
Reflexionstests aus ADR-045, falls B eine Eigenschaft berührt.

---

## Was dieser Plan nicht tut

- **Er baut keine dritte Stelle für «welche Nummer?».** Menü (Doppelklick),
  Detailbereich (Klick) und Kachel (breites Layout) bedienen sich alle aus
  `ContactRow.Choices`; A schliesst den zweiten davon an, mehr nicht.
- **Er verschiebt das Formular der Einstellungen nicht in einen Dialog.** Die
  Begründung steht unter B.
- **Er rührt die Vorschläge beim Tippen nicht an.** Die Begründung steht
  unter C.
- **Er ändert nichts an der Anbietervorlage des CRM.** Sollte T325 zeigen,
  dass die Quelle nur eine Nummer liefert, ist das eine Änderung im privaten
  Vorlagen-Repo und gehört nicht hierher (ADR-040).
