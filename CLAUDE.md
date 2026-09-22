# nipp — Projektkontext

Windows-Softphone (WinUI 3, .NET 8, x64) auf Basis des Linphone SDK 5.5.18.

**Diese Datei sind die Regeln** — was gilt, wenn jemand an nipp arbeitet.
Sie ist am 13.09.2026 auf genau das zusammengezogen worden (W2.7, Befund A8);
bis dahin standen hier auch der Stand jedes Arbeitstages und siebenhundert
Zeilen Erfahrung, und eine Datei, die man vor jeder Änderung lesen soll, muss
lesbar bleiben.

| Was | Wo |
|---|---|
| Was nipp tun soll | **`NIPP-BUILD.md`** — die Spezifikation. Im Zweifel dort nachlesen |
| Warum etwas davon abweicht | **`docs/decisions.md`** — ADR-001 bis ADR-069, dazu **ADR-072 als Entwurf**: die sechs offenen Befunde der Runde A1 mit ihren Optionen, **nicht entschieden** |
| **Was zuletzt passiert ist** | **`docs/stand.md`** — Meilenstein, was am Gerät aussteht, Chronologie |
| **Was Erfahrung ist, nicht Regel** | **`docs/lehren.md`** — die teuren Stellen, gruppiert |
| Wo das Projekt insgesamt steht | **`docs/plans/REVIEW-2026-09-12.md`** — 83 Befunde auf fünf Achsen, Massnahmen in Wellen |
| **Was noch offen ist** | **`docs/plans/BEWEIS-PLAN.md`** — der Gerätetag (W2.8) und die Tests für die SDK-Schicht (W2.1), in Runden nach Rüstzeug. **Die Schreibtisch-Runde A1 läuft seit dem 17.09.2026** und hat in fünfzehn Messrunden 79 Zeilen abgenommen; die zwölf Befunde der ersten fünf (A1-1 bis A1-12) sind **alle erledigt** (21.09.2026), die beiden der sechsten (A1-13, A1-14) ebenfalls — **repariert und nachgemessen am 22.09.2026**; aus der siebten ist **A1-16 behoben** (zehn Listenvorlagen ohne Namen, `ListenNamenTests` hält es fest), während **A1-15 ein falscher Alarm war** (der falsche Knopf, eine Stunde nach dem Push aufgeklärt). Dort steht auch die Einordnung aller S-Zeilen. aus der achten steht **A1-17** (ein Fluent-Pinsel unter der Schriftschwelle, 23 Stellen), aus der neunten ist **A1-18 behoben** (eine Vorlage mit Zugangsschlüssel kam durch) und **A1-19 zur Hälfte** — die Wurzel repariert, die Prüflücke bei zehn `async void` offen. Gezählt: **147 offen von 309**, davon **14 am Schreibtisch** — seit dem 22.09.2026 ohne die vier Outlook-Zeilen, die dort nie herstellbar waren (neuer Rüstzeug-Code `O`, ADR-018) — **und die Zahl ist ab jetzt nachrechenbar**: `Zaehle-Matrix.ps1` in den Testaufbauten trägt die Zählregel, denn die bisher fortgeschriebenen 190 von 303 liessen sich aus der Matrix nicht herstellen. **Repariert wird gesammelt, nicht mitten in einer Messrunde** — sonst prüft die halbe Runde gegen einen anderen Build als die andere; deshalb kamen die Reparaturen erst, als die Messrunden durch waren |
| Was am 14.09.2026 gebaut wurde | `docs/plans/ZIEHVORSCHAU-PLAN.md` (ADR-066) und `docs/plans/HEADSET-FREMDBELEGUNG-PLAN.md` (ADR-068) — beide mit ihren Messungen im Protokoll; **offen ist dort H5**, der Notausgang als Einstellung |
| Pläne und Reviews | `docs/plans/` — zwanzig Stück, von `IMPLEMENTATION-PLAN.md` bis `OEFFENTLICH-PLAN.md`. **Laufend sind fünf:** `BEWEIS-PLAN.md` (A1 läuft), `ZIEHVORSCHAU-PLAN.md` (V8: T292–T296 sind bestanden, **offen bleiben der Zug auf einen Gruppenkopf und hell bei 150 %**), `HEADSET-FREMDBELEGUNG-PLAN.md` (offen: H5 und T300), `AUDIOQUALITAET-PLAN.md` (**das lange Gespräch ist beantwortet, A8** — offen bleiben A7, die Senderichtung und T38) und `SHELL-IM-GESPRAECH-PLAN.md` (offen: T313–T317). Der Rest ist abgeschlossen |
| Der Einstieg für einen Tag am Gerät | `ABNAHME-ALLTAG.md` |

## Grenzen
- `using Linphone` ausschliesslich in `src/Nipp.Core/Services/Telephony/`.
  Ein Architekturtest erzwingt das.
- ViewModels kennen keine SDK-Typen, nur eigene Modelle.
- `Core.Iterate()` läuft im UI-Thread (DispatcherQueueTimer, 20 ms).
  Nichts Blockierendes in SDK-Callbacks.
- **Eine Ausnahme kommt nie in den nativen Rahmen zurück** (ADR-053). Die
  Callbacks des SDK kommen über einen Reverse-P/Invoke-Rahmen; der `try` in
  `SipPumpHost.OnTick` liegt **ausserhalb** davon. Deshalb drei Stellen:
  `CallbackGuard.Run` um **jeden** Callback in `SipEventBridge`,
  `SipService.Sdk(…)` um **jeden** Aufruf ins SDK (es übersetzt
  `LinphoneException` — ViewModels kennen keine SDK-Typen), und `GuardAsync`
  um **jeden** `async void`. `ExceptionBoundaryTests` erzwingt alle drei.
  `App.OnUnhandledException` setzt bewusst **kein** `Handled`.
  **Vierte Stelle, seit dem 22.09.2026 belegt: der Zugriff auf die
  Anrufliste** (`CallHistoryStore.Guarded`). Dort war das Muster verdreht —
  die private `…Core`-Methode trug den Schutz und rief sich selbst, der
  öffentliche Weg lief ungeschützt —, und ein Klick auf einen verpassten
  Anruf bei schreibgeschützter `history.db` beendete nipp mit `0xc000027b`.
  **Öffentlich ist die Methode mit dem `Guarded`-Aufruf, privat die mit dem
  Körper**; `Query` macht es seit jeher vor. **Ein Schutz, der an einer toten
  Methode hängt, sieht aus wie ein funktionierender** — der Compiler warnt
  nicht, und jeder Test auf einer heilen Datei bleibt grün.
- **Was der Benutzer selbst eingestellt hat, überschreibt kein Profil**
  (ADR-054). `NippSettings.UserOverrides` führt die Pfade; eingetragen wird an
  **einer** Stelle, in `SettingsService.Write`. Das Profil speichert über
  `SaveFromProfile` und vermerkt nichts — sonst gälte sein eigener Wert sofort
  als Benutzerentscheidung. Eine **Sperre** gewinnt immer und löscht die
  Markierung. Welche Pfade es gibt, steht in `ProvisioningCatalog` und **nur
  dort**; `nippprov` liest dieselbe Liste.
- `src/Nipp.Core/Services/Integrations/` kennt **weder das SDK noch WinUI**
  (§21.2). `using Json.Path` steht nur unter `Integrations/Mapping/`.
  `IntegrationBoundaryTests` erzwingt beides.
- **Was ein Druck auf die Headset-Taste bedeutet, entscheidet genau eine
  Stelle:** `HeadsetPolicy.Interpret` — aus den laufenden Anrufen, nie aus dem
  Gabelzustand des Geräts. `HandleHotkey` in `App.xaml.cs` ruft dieselbe
  Funktion. Ob eine Gerätemeldung überhaupt ein Druck war, entscheidet
  `HookWatch`; **`HidTelephonyDevice` schreibt seinen Gabelspiegel nur aus
  Gerätereports** (Nachtrag zu ADR-028).
- **nipp teilt das HID-Call-Control-Interface mit anderen Programmen. Jeder
  Ausgangsreport ist ein Eingriff in deren Gespräch** — Teams liest die
  Antwort des Geräts als Tastendruck und legt auf. Ob einer hinausgeht,
  entscheidet genau eine Stelle: `HeadsetSignalGate.Erlaubt` (ADR-028
  Nachtrag 4, ADR-068). **Auch der Bericht, der aufräumt:** am 14.09.2026 war
  es der **Abschlussbericht beim Ablehnen**, der ein Teams-Meeting beendete,
  nicht der Ring. Verschwiegene Berichte werden auch nicht zurückgenommen —
  daran hängt `jeGemeldet`.
- **Ob ein anderes Programm das Gerät hält, sagt die Audio-Sitzung, nicht der
  Gabelzustand** (ADR-068). `HookWatch.Fremdbelegung` erkennt nur einen
  fremden **Anruf**; in einem Teams-**Meeting** meldet das Jabra durchgehend
  «aufgelegt». `AudioSessionWatch` fragt stattdessen, ob ein fremder Prozess
  eine aktive Sitzung hält — **im Zweifel frei**, denn ein Fehler an der
  Audio-Schnittstelle darf kein Headset ohne Lampen bedeuten. Und **das eigene
  Gespräch schlägt die Fremdbelegung**: wer annimmt, hat entschieden. Ein
  klingelnder Anruf ist noch keine Entscheidung.
- Die Log-Klasse der Windows-Integration heisst `WindowsIntegrationLog`
  (`WindowsIntegration.cs` und `GlobalHotkeyService.cs` teilen sie sich).
  Der Name `IntegrationLog` gehört jetzt zur Integrationsplattform.
- **Der Zustand des Karten-Designers liegt im Kern**, nicht im Fenster:
  `ViewModels/CardDraft.cs` und `ViewModels/CardDesignerViewModel.cs`.
  `Nipp.App` hat kein Testprojekt, und ein Editor hat mehr Zustand als alles
  andere in nipp. `CardDesignerWindow` zeichnet und ruft.
- **Welcher Feldname was bedeutet, entscheidet genau eine Stelle:**
  `Cards/FieldCatalog.cs`. Wer eine Feldliste in einen Empfänger schreibt
  (Toast, Karte, Vorschlagsliste), baut die Doppelwahrheit wieder auf, die
  ADR-032 aufgelöst hat. `docs/integrations/feldnamen.md` wird daraus erzeugt;
  zwei Tests halten beides zusammen.
- **Wie ein Zustand heisst, entscheidet genau eine Stelle:**
  `ViewModels/CallStateCatalog.cs` und `ViewModels/AccountStateCatalog.cs`
  (ADR-044). Der Anrufzustand stand dreimal im Code, der Kontozustand viermal —
  „eingehender Anruf" gegen „Anruf wartet", „angemeldet" gegen „registriert".
  Festgelegt: **„Anmeldung" statt „Registrierung", „Gegenseite" statt
  „Gegenstelle"**; die technischen Wörter bleiben im Protokoll.
  `StateCatalogTests` hält es fest.
- **Auf der Einstellungsseite gibt es keinen „Speichern"-Knopf** (ADR-045).
  Jede Änderung wirkt, sobald sie gemacht ist; Freitext- und Zahlenfelder beim
  Verlassen des Feldes (`ApplyEdits`, gerufen aus **`FocusManager.LostFocus`**).
  Wer eine Eigenschaft hinzufügt, die nichts einstellt, trägt sie in
  `NurAnzeige` ein, ein Freitextfeld in `ErstBeimVerlassen` — **eine Sperrliste
  und keine Erlaubnisliste**, damit ein Vergessen eine überflüssige
  Schreiboperation kostet und keinen stillen Datenverlust.
  `SettingsSaveModelTests` prüft beide per Reflexion.
  **`LostFocus` und nicht `LosingFocus`, und das ist kein Detail** (Befund
  A1-7): das zweite läuft, während der Fokus noch wechselt — `TextBox` und
  `NumberBox` übertragen ihren Inhalt aber erst mit `LostFocus` in die
  Bindung. Vier Tage lang las `ApplyEdits` dort den alten Stand und schrieb
  ihn zurück.
  **Und was die Sperrliste sonst noch bedeutet:** für die neun Felder darin
  ist `ApplyEdits` der **einzige** Weg auf die Platte — `OnPropertyChanged`
  überspringt sie. Ein kaputter einziger Weg sieht aus wie ein
  funktionierender, und die zwei Reflexionstests halten die Listen
  vollständig, prüfen aber nicht, dass ein Wert ankommt. **Wer hier etwas
  ändert, misst am laufenden Programm nach.**
- **Zwei Zeitgrenzen auf dieselbe Dauer heben sich auf** (Befund A1-8). Eine
  Suche in einer fremden Quelle läuft heute durch **zwei** Stellen, die beide
  aus `Traits.Timeout` eine eigene `CancellationTokenSource` machen:
  `ContactSearchService.AskAsync` und, eine Ebene tiefer,
  `IntegrationHttpClient`. Löst die äussere zuerst aus — sie ist die ältere —,
  dann sieht es unten aus wie **«von aussen abgebrochen»**, obwohl niemand
  abgebrochen hat; und dieser Fall schweigt bewusst: keine Meldung, keine
  Protokollzeile, kein Fehlschlag für den Schutzschalter. **Beide Stellen
  tragen denselben Kommentar** über das Unterscheiden von «zu langsam» und
  «überholt», und zusammen machen sie das Gegenteil.
  **Aufgelöst am 21.09.2026 an der unteren Stelle:** der HTTP-Zugang prüft beim
  Abbruch zuerst, ob **seine eigene** Zeitgrenze ausgelöst hat — die genauere
  Aussage gewinnt. **Die obere Zeitgrenze grosszügiger zu machen war der
  naheliegende und falsche Griff:** für einen Provider ohne eigene Zeitgrenze
  ist sie die einzige, und ein Aufschlag verlängert nur die Wartezeit. Ein
  bestehender Test hat das sofort gefangen.
- **Der Name des Infobereich-Symbols ist der erste ToolTip nach `Create()`**
  (Befund A1-2). Windows hält ihn als Anzeigenamen fest und stellt ihn jedem
  späteren ToolTip voran — der vorgelesene Name ist also «erster Text» +
  «aktueller Text». Deshalb trägt der Text beim Anlegen den Namen («nipp») und
  wird **nicht** überschrieben, während `UpdateToolTip` nur noch den Zustand
  führt. Mit «nipp — …» an beiden Stellen las eine Sprachausgabe «nipp nipp —
  angemeldet».
- **Wer in WinUI auf einen Zustand wartet, hängt sich an das Ereignis, das ihn
  meldet — nicht an den nächsten Tick** (Befunde A1-6 und A1-10). Ein
  `DispatcherQueue.TryEnqueue` ist eine Wette darauf, dass ein Durchlauf
  reicht; beim Inhalt eines nie aufgeklappten `Expander` reichte er nicht, und
  das Schloss eines gesperrten Feldes fehlte beim ersten Aufklappen. Richtig
  ist das `Loaded` des Inhalts. **Dasselbe für den Fokus:** wer eine Ansicht
  neu baut, unter der der Fokus lag, setzt ihn danach selbst — sonst vergibt
  WinUI ihn weiter, und im Karten-Designer landete er im Vorschau-Textfeld, wo
  die `TextBox` das eigene Strg+Z hat.
- **Eine Farbe aus `Resource(...)` ist eine Momentaufnahme des Themas**
  (Befund A1-11). Der Helfer löst **einmal** auf und liefert einen festen
  `SolidColorBrush`; beim Themenwechsel bleibt er stehen. Im Karten-Designer
  stand deshalb schwarze Schrift auf dunkelblauem Grund, nachdem das Fenster im
  dunklen Thema gebaut und danach auf hell gestellt worden war. **Erben oder
  binden, nicht setzen** — auf einer Akzentfläche heisst das: den Vordergrund
  gar nicht angeben, dann führt ihn der Knopfstil dem Thema nach.
- **Wo der Fokus liegt, sagt nicht, womit gedrückt wurde** (ADR-044,
  Nachtrag vom 21.09.2026). **Windows setzt den Fokus beim Mausklick auf den
  Knopf, bevor `Click` feuert** — eine Abfrage «liegt der Fokus schon hier?»
  ist danach für Maus und Tastatur gleich wahr. Die Wähltastatur hing daran
  und verlor nach jedem Mausklick die nächste getippte Ziffer (Befund A1-12).
  Wer die Herkunft eines Drucks braucht, fragt **`Button.FocusState`**
  (`Keyboard` gegen `Pointer`) an der Taste selbst — sie ist die einzige
  Stelle, die es noch weiss, und sie gibt es als `KeypadPress` weiter.
- **Eine Zustandsfarbe eines Bedienelements entsteht im Inhalt, nicht über
  WinUI-Ressourcenschlüssel** (ADR-067). Sechs überschriebene Schlüssel in
  `Button.Resources` — der dokumentierte Weg für Lightweight-Styling — haben
  nipp **siebenmal beim blossen Überfahren des Auflegen-Knopfes beendet**:
  `0xc000027b` in `combase.dll`, ohne verwaltete Ausnahme, ohne `crash.txt`,
  ohne eine Zeile im Protokoll. Flach, als Verweis und in `ThemeDictionaries`
  — alle drei Formen stürzten ab. Der Knopf ist jetzt durchsichtig, die Farbe
  trägt ein Rahmen darin, und die Rückmeldung macht dessen Deckkraft.
  **Ein Absturz ohne `crash.txt` ist der Hinweis auf einen WinRT-Rahmen** —
  gesucht wird er im Ereignisprotokoll (Modulname und Ausnahmecode), nicht im
  eigenen.
- **Ein Statuston färbt Schrift und Rand, nie die Fläche** (ADR-044). Als
  Fläche erbt der Text seine Farbe vom Thema und stand im Dunkeln fast weiss
  auf `#FCE100` — rund 1,4:1. Als Vordergrund sind dieselben Pinsel in beiden
  Themen geprüft.
  **Dasselbe gilt für jede Akzentfläche, auf der Text steht** — auch für die
  Auswahlfarbe einer Liste. Am 17.09.2026 an den Pixeln gemessen (Befund
  A1-11): auf einer ausgewählten Zeile des Karten-Designers schaltet die
  Beschriftung korrekt um (10,47:1 dunkel, 5,67:1 hell), **die Zeile darunter
  nicht** — sie behält ihre Sekundärfarbe und steht bei **1,16:1**, in beiden
  Themen. Wer einen zweiten Text auf eine Auswahlfläche setzt, gibt ihm einen
  eigenen Pinsel, der mitschaltet.
- **Der Detailbereich der Kontakte steht an einer Stelle** — seit ADR-048 **in
  der Zeile**, für alle drei Listen gleich. Der Ort hat dreimal gewechselt
  (ADR-041 unten, ADR-042 in der Zeile nur fürs Team, ADR-046 wieder unten);
  was jedes Mal zählte, war **nicht der Ort, sondern dass es einer ist**. Wer
  ihn wieder nur für eine Liste anders macht, baut die Ungleichheit neu auf.
  Der Leistungseinwand aus ADR-042 hängt an `x:Load`: ohne das steht der
  Detailteil in **jeder** der 137 Outlook-Zeilen im Baum.
- **Ob eine Eingabe gewählt werden kann, entscheidet genau eine Stelle:**
  `NumberNormalizer.IsDialable` (ADR-049) — die Gegenfrage zu `Normalize`, aus
  denselben Regeln. Seit ADR-046 ist das Nummernfeld auch das Suchfeld, und
  **dieselbe Antwort trägt zwei Wirkungen**: was die Eingabetaste tut, und
  welche der beiden Listen steht (ADR-051). Wer eine dritte Stelle für «ist das
  eine Nummer?» baut, baut die Doppelwahrheit wieder auf.
- **Ein systemweites Kürzel hat genau eine Bedeutung, und die steht je Rolle an
  einer Stelle:** `HeadsetPolicy.Interpret` für Annehmen und Auflegen,
  `App.HandleMuteHotkey` fürs Stummschalten (ADR-050). `GlobalHotkeyService`
  trägt dafür eine `HotkeyRole` je Registrierung und reicht sie im Ereignis
  weiter — **er entscheidet nichts.**
- **Wie breit das Fenster ist, entscheidet genau eine Stelle:**
  `ShellViewModel.ApplyWidth` (ADR-047, §23) — mit Schwelle **960** und
  Hysterese **40**. **Beide Zahlen gelten für die Breite der Seite, nicht für
  die des Fensters**, und das ist rund 15 bis 20 logische Pixel Unterschied:
  am 13.09.2026 gemessen kippt es ins breite Layout erst zwischen **974 und
  980** Fensterbreite und zurück zwischen **935 und 930**. Wer zum Prüfen das
  Fenster auf 960 zieht, sieht das schmale Layout und meldet einen Fehlschlag,
  der keiner ist. Die Seite meldet nur ihre Breite und zeichnet, was folgt;
  ein `AdaptiveTrigger` stünde daneben und könnte die Sektionsregel
  («Nebenstellen links nur im schmalen Layout») nicht mitrechnen, ohne sie ein
  zweites Mal auszuschreiben. **Die Deckelung aus ADR-046 ist seit ADR-052
  weg:** der Inhalt füllt das Fenster, und breit ist die linke Spalte **fest**
  480 statt gedeckelt — zwei Sternspalten mit Höchstbreiten liessen den
  Restplatz verfallen. `ApplyLayout` zeichnet nur, `ApplyWidth` entscheidet.
- **Wo ein laufendes Gespräch steht, entscheidet genau eine Stelle:**
  `ShellPage.ApplyCallView` — aus **zwei** Eigenschaften, `IsWide` und
  `HasActiveCall`. **Breit** steht das Gespräch in der linken Spalte (der
  `CallFrame` nimmt dieselbe `ActiveCallPage` auf, kein zweites XAML),
  während rechts die Nebenstellen mit ihren Lampen stehen bleiben und
  anklickbar sind; **schmal** navigiert `MainWindow` wie bisher auf die
  ganze Seite. **`MainWindow` liest das Layout, es entscheidet es nicht**
  (`IstBreit()` fragt `ShellViewModel`) — sonst stünde die Schwelle aus
  ADR-047 ein zweites Mal da. Wird das Fenster **während** eines Gesprächs
  schmal, navigiert `ApplyCallView` selbst: diesen Wechsel merkt sonst
  niemand, denn `MainWindow` navigiert nur bei Beginn und Ende.
  **Zwei Dinge bleiben dabei stehen, und beide mit Grund:** die
  Meldungszeile (`MessagePanel`) — wer im Gespräch eine dritte Nebenstelle
  anklickt, bekommt die Ablehnung aus §8.2, und ohne diese Ausnahme
  passierte scheinbar nichts —, und der Hinweis auf ein gewechseltes
  Audiogerät, der im Gespräch **wichtiger** ist als sonst. Die übrige linke
  Spalte wird **über die Spalte** ausgeblendet und nicht über eine
  Namensliste, damit ein später hinzugefügtes Element nicht vergessen wird.
  Plan und offene Prüfungen: `docs/plans/SHELL-IM-GESPRAECH-PLAN.md`.
- **Wie der Gesprächspartner heisst, entscheidet genau eine Stelle:**
  `Integrations/Context/CallPartyResolver.cs` (ADR-043) — und sie beantwortet
  **zwei** Fragen, die nicht dieselbe sind: `NameOf` gibt einen Namen oder
  nichts (**nie eine Nummer**), `Describe` gibt immer etwas (**nie leer**). Die
  Auflösung ist **richtungsneutral**; „Anrufer" war der Denkfehler, der bei
  ausgehenden Anrufen die blosse Nummer stehen liess. `Nipp.App` trägt kein
  Auflösungswissen, und `CallInfo` hat kein `DisplayLabel` mehr.
- **Gruppe und Reihenfolge der Team-Kontakte werden zusammen geschrieben:**
  `TeamOrder.ApplyLayout` (ADR-042), und es endet mit `TeamGroups.Normalize` —
  die Invariante aus ADR-041 ist damit eine Eigenschaft des Ergebnisses und
  kein Vertrag, an den sich Aufrufer erinnern müssen. Die Kennung einer
  Nebenstelle (`TeamContactSource.IdOf`) hängt am **Inhalt**, nie am Platz;
  `IdsOf` ist die einzige Stelle, die den Zähler bildet.
- **Der Ziehvorgang gehört uns, nicht WinUI** (ADR-065). Angefangen wird er in
  `ShellPage`: die Liste merkt sich das Drücken und startet nach acht Pixeln
  `StartDragAsync` am Element der Vorlage — **darunter bleibt es ein Klick**.
  **Der eingebaute Weg von WinUI ist keiner** — gemessen: mit gruppierter
  `CollectionViewSource` endet jeder Drop mit `None`, und im Kachelraster
  startet gar kein Zug. Und die Zeigerereignisse hängen über
  `AddHandler(…, handledEventsToo: true)` an der Liste, weil das
  `ListViewItem` den Druck als behandelt markiert.
- **Die Vorschau ist der Auftrag, und das Ablegeziel ist die Liste**
  (ADR-066). Beim Überfahren wird die Zeile **wirklich** umgehängt
  (`TeamDragPreview` im Kern); beim Loslassen wird nichts mehr gerechnet,
  sondern geschrieben, was dasteht (`ApplyTeamLayout()`). Wer daneben eine
  zweite Rechnung fürs Zeichnen baut, baut die Doppelwahrheit wieder auf.
  **`AllowDrop` steht an `TeamList` und `TeamTiles`, nicht an der Zeile** —
  zwischen den Zeilen lag totes Gebiet, und ein Zug, der dort endete, ging
  stumm verloren (gemessen: *letztes Ziel Zeile, vor 703 ms*). Der
  **Gruppenkopf** bleibt ein eigenes Ziel, weil eine leere Gruppe keine Zeile
  hat. **Ein Vorschauschritt braucht eine Zeigerbewegung** (`VorschauSchwelle`)
  — ohne sie zittert es an der Gruppengrenze, weil das Layout unter dem
  stehenden Zeiger wandert. Was gezogen wird, zeigt `ContactRow.DragOpacity`
  — **an der Zeile gebunden, nie am Container gesetzt**: die Listen
  virtualisieren. `TeamLayout.Move` bleibt für das **Kontextmenü**, den Weg
  ohne Maus.
- **`using Velopack` steht nur unter `Services/Updates/` und in
  `Nipp.App/Program.cs`** (ADR-039, `UpdateBoundaryTests`). Der `UpdateService`
  selbst kennt keinen Velopack-Typ — deshalb laufen seine Tests ohne Netz.
- **Der Einstiegspunkt ist `Nipp.App/Program.cs`, nicht die generierte Main.**
  `DISABLE_XAML_GENERATED_MAIN` schaltet sie ab; `VelopackApp.Build().Run()`
  muss vor jeder WinUI-Initialisierung laufen. Die Startlogik selbst bleibt die
  des XAML-Compilers (`XamlGeneratedProgram.XamlGeneratedMain()`) — nachgebaut
  wäre sie eine zweite Wahrheit. **Nichts Wartendes davor**, sonst startet nipp
  nicht.
- **Der Startanlass hat zwei Haken, und einer allein genügt nicht.**
  `OnFirstRun` feuert nach einer **Installation**, `OnRestarted` nach einem
  **Update**; beide setzen nur `Program.Anlass`, und `App` zeigt daraufhin die
  Sprechblase, sobald das Symbol im Infobereich steht — angezeigt wird in den
  Haken selbst nichts, sie liegen vor WinUI. Wer nur den ersten setzt, schweigt
  nach **jedem** Update; am 14.09.2026 fiel das erst auf, als der Dialog weg
  war und gar nichts mehr zu sehen blieb.
- **Ein Update läuft still, und dass nipp sich beendet, ist nipps Aufgabe.**
  `ApplyUpdatesAndRestart` bringt ein eigenes Fortschrittsfenster **mit
  OK-Knopf** mit, und dieser Knopf bricht die Installation ab. Deshalb
  `WaitExitThenApplyUpdates(…, silent: true, restart: true)`: der Vorgang
  wartet auf das Ende des Prozesses. Herbeigeführt wird es über
  `UpdateService.RestartRequested`, das `App` in die eigene Abmeldung führt —
  ein Prozess, der seine DLLs offen hält, lässt jedes Update scheitern.
- **Eine Anbietervorlage ist genau eine Datei, und der Leser lehnt ab** (ADR-040).
  Mitgeliefert wird nur `Catalog/templates/custom-rest.json`; alles andere kommt
  über „API-Anbieter importieren" und liegt unter `%APPDATA%\nipp\connectors`.
  **Was in einer fremden Datei steht, hat niemand geprüft:** ein Wert, der wie
  ein Geheimnis aussieht, ist ein Ablehnungsgrund, und `Enabled` wird beim Lesen
  auf `false` normalisiert. Wer das umgeht, gibt Zugangsschlüssel weiter oder
  lässt nipp beim ersten Anruf eine Adresse fragen, die niemand gesehen hat.
- **Die Namen der beiden bv2-eigenen Systeme stehen nicht mehr im Repo**, und
  `PublicRepositoryTests` hält das fest. Wer eine Erfahrung aufschreibt, nennt
  die **Art** des Systems („das CRM", „das Gesprächsjournal") und die
  Beispielkennung (`crm`, `journal`) — der Erfahrungswert gehört erhalten, der
  Systemname nicht. **Seit dem 13.09.2026 stimmt der Satz auch:** der Wächter
  sah die dreizehn Pläne im Wurzelverzeichnis nie, und beim Umzug nach
  `docs/plans/` meldete er 67 Stellen (ADR-059). Dasselbe gilt für
  `pbx.example.ch` — der Hostname der Anlage gehört nicht ins Repo.

## Wo gearbeitet wird (seit 14.09.2026)

Das Repo ist **öffentlich**: `github.com/bv2-GmbH/nipp-softphone`, Zweig
`main`, lokal als `origin`. Das alte, private Repo ist **archiviert** und
liegt lokal als `archiv` — dort steht die volle Historie der ersten 181
Commits, und dort liegt die Brücke 0.9.5, die installierte Arbeitsplätze auf
das neue Repo führt.

**Der öffentliche Zweig hat eine eigene, frische Historie** (erster Commit
`c4da665`): in 60 der alten Commits standen zwei firmeneigene Systemnamen und
der Hostname der Anlage. Deshalb gibt es keine Verbindung zwischen den beiden
Linien — `review-umsetzung` und `main-archiv-06-09` sind die alten, sie
bleiben liegen.

**Was das für jede Änderung heisst:** `PublicRepositoryTests` ist kein
Formalismus, sondern die letzte Kontrolle vor der Öffentlichkeit. **Seit dem
17.09.2026 greift sie auch in der CI** — bis dahin nie, weil der Build vorher
an der SDK-Prüfsumme abbrach (ADR-069). Trotzdem vor jedem Push
`.\build.ps1 test Nipp.sln` laufen lassen: der Test kostet Sekunden, und ein
Systemname, der erst auf GitHub auffällt, steht dort schon. Wer
über die verbotenen Muster schreibt — in einem ADR, einem Plan, einer
zitierten Protokollzeile — setzt sie zusammen, statt sie auszuschreiben.
Der Test hat am 14.09.2026 den Plan erwischt, der genau das erklärt.

## Befehle
- Bauen: `.\build.ps1 build Nipp.sln -c Debug` — **das pruefen Compiler und
  Tests, es erzeugt aber keine startbare App.** Wer nipp danach startet,
  sieht es **sofort sterben**, mit `REGDB_E_CLASSNOTREG` im
  Ereignisprotokoll und **ohne eine einzige Zeile im eigenen Protokoll** —
  der Fehler faellt im Modul-Initialisierer des Windows App SDK, vor jedem
  eigenen Code. **Zum Starten gehoert der unpackaged Build:**

      .\build.ps1 --% build src\Nipp.App -c Debug -p:WindowsPackageType=None -t:Rebuild

  **Beides gehoert dazu**: ohne `-t:Rebuild` warnt `build.ps1`, und die App
  stirbt trotzdem. Am 16.09.2026 hat genau diese Zeile eine halbe Stunde
  gekostet — gesucht wurde der Fehler im frisch umgebauten XAML, und er lag
  im Build-Befehl, der hier stand. Details: docs/packaging.md.
- Tests: `.\build.ps1 test Nipp.sln`
- Formatieren: **nur die eigenen Dateien** —
  `dotnet format --include <pfad> ...`. Ein `dotnet format` auf die ganze
  Projektmappe zieht in `SipService.cs` einen `using`-Alias über den
  Kommentar, der ihn erklärt, und entfernt BOMs in sechzig unbeteiligten
  Dateien.
- Setup bauen: `.\build\Release-Nipp.ps1 -Version 0.9.0 -Channel stable`
  (Velopack, ADR-038; mit `-Publish` nach GitHub, `-UploadRepo` wählt das Ziel
  und steht auf dem öffentlichen Repo). Details: docs/updates.md.
  **Ein Fix am Update-Pfad liefert sich nie selbst aus:** den Vorgang führt die
  **laufende** Fassung aus, die Wirkung zeigt sich erst beim übernächsten
  Schritt — und was das **Setup** betrifft, sieht nur, wer den Installer
  wirklich startet (`docs/plans/RELEASE-PLAN.md`). Wer das nicht bedenkt, baut
  drei Fassungen und hält den Fix für kaputt.
- Oberfläche maschinell prüfen: `. .\tools\Test-Ui.ps1` — liest den
  UIA-Baum (`Get-NippTree`), bedient Elemente (`Invoke-NippElement`) und zeigt,
  was ein Bildschirmleser vorlesen würde (`Test-NippAccessibleNames`).
  Ruckeln, Farben und abgeschnittene Beschriftungen bleiben am Menschen.
  **Setzen ist nicht Tippen:** `Set-NippText` schreibt über `ValuePattern`, und
  das klemmt einen Wert ausserhalb des Bereichs auf das Maximum **und
  übernimmt ihn** — beim Tippen bleibt er stehen und wird gar nicht
  gespeichert. Wer eine Eingabeprüfung so prüft, misst das Bedienelement statt
  der Regel. **Und es schreibt in die echten Einstellungen** — am 14.09.2026
  hat es so den SIP-Port des Benutzers verstellt. Vorher sichern.
  Es kann ausserdem ein **zweites Fenster** ansprechen (`Get-NippWindow
  -Titel`, für den Karten-Designer — sonst misst man stillschweigend das
  Hauptfenster), liefert je Element das **Rechteck** und ob es überhaupt
  dargestellt wird, und zieht das Fenster auf eine **logische** Breite
  (`Set-NippWindowSize`). **Die Rechtecke sind physisch, die Matrix ist
  logisch** — `Get-NippSkalierung` nennt den Faktor (hier 150 %).
  **Nach einer Eingabe eine Sekunde warten, bevor die Datei gelesen wird.**
  Ein über die Oberfläche geänderter Wert steht nicht im selben Atemzug in
  `settings.json`. Vom 17. bis zum 21.09.2026 kam er bei neun Feldern
  **gar nicht** an (Befund A1-7, behoben); seither kommt er, aber nicht
  sofort. Wer zu früh liest, misst den alten Stand und meldet einen Befund,
  den es nicht gibt.
- **nipp beenden, dann die Datei schreiben, dann starten** — in dieser
  Reihenfolge. **nipp schreibt `settings.json` beim Beenden vollständig
  zurück**, und eine Änderung an der laufenden Datei ist danach weg
  (gemessen am 17.09.2026: vierzig von Hand eingetragene Nebenstellen waren
  nach dem Neustart wieder zehn). Das gilt für jede Prüfung, die eine
  Konfiguration einspielt. Sauber beendet wird über das Infobereich-Menü,
  nicht über `Stop-Process`; das Symbol ist über UI Automation erreichbar
  (`Shell_TrayWnd`, Button `nipp angemeldet`) — **der Name trägt den Zustand,
  nicht nur «nipp»**, und ein `-like 'nipp*'` erwischt zuerst das angeheftete
  Taskleistensymbol. Bis zum 21.09.2026 hiess er «nipp nipp — angemeldet»
  (Befund A1-2); wer ein Werkzeug an diesen Namen hängt, sucht besser über den
  **Zustand** als über die Schreibweise.
- **Wer ein Provisionierungsprofil einspielt, sichert die Geheimnisdatei mit**
  (Befund A1-9). Ein Profil mit `<accounts>` **ersetzt die Kontenliste und
  nimmt die gespeicherten Passwörter mit**; `settings.json` zurückzuspielen
  holt sie nicht zurück, und nipp meldet danach «Zugangsdaten abgelehnt». Die
  Datei liegt neben `history.db` unter `%LOCALAPPDATA%`.
  **Das hat zweimal eine Anmeldung gekostet — am 17. und am 21.09.2026**, das
  zweite Mal, obwohl diese Warnung schon dastand und von demselben geschrieben
  war, der sie dann übersah. **Deshalb steht die Vorsicht seit dem 21.09.2026
  nicht mehr nur hier:** `Sichern-Und-Zuruecksetzen.ps1` im Aufbau sichert die
  Datei mit und spielt sie zuerst zurück, und **nipp sagt es jetzt selbst** —
  fehlt das Passwort, nennt die Anmeldemeldung genau das statt
  «Zugangsdaten abgelehnt». Wer einen eigenen Weg nimmt, denkt trotzdem daran.
- **Die Aufbauten für den Gerätetag liegen neben dem Repo**, nicht darin:
  `C:\dev_claude\nipp-testaufbauten\` — ein Provisionierungsserver mit fünf
  Profilen, eine lokale REST-Attrappe (schnell, langsam, tot) und die
  Sicherungen der jeweils angefassten Dateien. Jeder Aufbau hat eine
  `LIESMICH.md`, die sagt, welche Zeile womit geprüft wird. **Vor dem
  Neubauen dort nachsehen** — die Rüstzeit ist der eigentliche Aufwand des
  Gerätetags, und sie ist für diese beiden schon bezahlt.
- MSIX (nicht ausgeliefert, wartet auf T110 und AP9.2): docs/packaging.md

Nie ein blankes `dotnet build` — siehe „Bauen auf dieser Maschine".

## Regeln
- Nullable ein, Warnungen sind Fehler.
- Benutzertexte auf Hochdeutsch (ss statt ß). Sie stehen heute **in XAML und
  C#**; eine `Strings/de-CH.resw` gibt es **nicht** — nur ein leeres
  Verzeichnis `Strings/de-CH/` (docs/plans/REVIEW.md O12, ADR-021). Wer
  einen Text ändert, sucht ihn also im Code.
- **Und mit Umlauten** (ADR-045). In Kommentaren sind `ae`/`oe`/`ue` Absicht,
  im Benutzertext waren sie eine unbemerkte Abweichung — „liess sich nicht
  oeffnen" stand an neun Stellen, „Unverschluesselt zulassen" ein halbes Jahr
  in den Einstellungen. Ebenso: kein Paragrafenverweis, keine ADR-Nummer, kein
  Repo-Pfad, kein Methodenname, kein .NET-Typname, kein nackter HTTP-Code als
  ganze Aussage, und jede Meldung sagt, was zu tun ist. Ein Rohtext darf
  bleiben, aber hinter einem eigenen Satz — dafür gibt es
  `UserMessage.WithCause`.
- **In der Oberfläche gilt `«…»`**, nicht `„…"` (W1.6). Das deutsche Paar
  schliesst hier mit einem **geraden** Anführungszeichen, und das beendet ein
  C#-Literal wie ein XAML-Attribut; in Kommentaren und Markdown bleibt `„…"`
  richtig. **`UserTextTests` erzwingt beide Regeln** — über den sichtbaren
  Text, nicht über ganze Zeilen. Protokollvorlagen und Ausnahmen für
  Programmierfehler sind ausdrücklich ausgenommen.
- Kein Feature ohne Auftrag aus NIPP-BUILD.md.
- Abweichungen als ADR in docs/decisions.md.
- Nie gegen Kundentenants testen — nur der Test-Trunk.
- **Eine einzige Fehlanmeldung sperrt die Adresse** (22.09.2026, T267).
  fail2ban auf der Asterisk greift sofort; danach meldet nipp nicht mehr
  «Zugangsdaten abgelehnt», sondern «Server nicht erreichbar» — und drei
  Neustarts helfen nicht, die Sperre muss **auf der Anlage** aufgehoben
  werden. Wer mit falschen Zugangsdaten prüft, nimmt den Arbeitsplatz vom
  Netz: erst am Ende eines Gerätetags, und den Zugang zur Anlage bereithalten.
- **Keine Rufnummer, kein Name, kein Suchtext und kein Antwortinhalt ins
  Protokoll** (§21.2, ADR-022). Nummern werden maskiert (`LogMasking`), und
  das Diagnosepaket nimmt sie gefiltert mit. Ein Softphone, das mitschreibt,
  wer angerufen hat und was das CRM dazu wusste, führt ein Bewegungsprofil mit
  Kundendaten. `PrivacyLogTests` prüft die Platzhalternamen.
- **Auch der SDK-Trace** (ADR-022, Nachtrag vom 13.09.2026). Auf Debug standen
  an einem Tag **714 Zeilen mit Digest-Kopfzeilen und 4 282 mit Rufnummern** im
  Protokoll — und Debug ist die Stufe, die der Support einschalten lässt.
  `LogMasking.SipLine` läuft über jede SDK-Zeile; im Trace wird ein **rein
  numerischer** Benutzerteil immer maskiert, auch dreistellig, eine
  Kontokennung wie `151bv2` bleibt lesbar.
- **Ein erwarteter Fehlschlag wird einmal je Sitzung gemeldet**
  (`QuietFailures`, W1.7). Eine stille Rückgabe ohne Protokollzeile ist die
  Lücke, die dieses Projekt zweimal bezahlt hat; jede Zeile bei jedem
  Pump-Durchlauf wäre Rauschen.
- **Eine neue Farbe in `Tokens.xaml` braucht eine Einordnung** in
  `ThemedBrushTests.Schwellen`: färbt sie Schrift (4,5:1), ein Bedienelement
  (3:1) oder eine Fläche (nicht gemessen). Ohne Eintrag ist der Test rot — die
  Entscheidung soll einmal ausdrücklich fallen, nicht stillschweigend über den
  Namen.
- **Ein Kommentar, der eine Ursache behauptet, nennt die Messung** (W2.7,
  Befund A8). Der Kommentar am `GroupStyle.Panel` erklärte seit ADR-047, warum
  ein gruppiertes GridView «NUR so» virtualisiere — niemand hatte das gemessen,
  es stimmte nicht, und der Satz war fünf Tage lang genau die Begründung, die
  Stelle nicht anzufassen. Dasselbe zweimal im Protokoll: «die Gegenstelle
  schickt Early Media ohne Audio», obwohl beide Male Audio da war.
  **Wer nicht gemessen hat, schreibt «vermutlich» hin** — oder misst.
  **Zum dritten Mal am 13.09.2026, und diesmal stand der Satz in einem ADR:**
  ADR-042 behauptete, WinUI hänge eine gezogene Zeile selbst um. Niemand hatte
  es geprüft, es stimmte nicht, und das Ziehen zwischen Gruppen hat deshalb nie
  funktioniert — gemerkt hat es niemand, weil der Fehlerfall schwieg (ADR-065).
  **Ein ADR ist keine Messung.**
- **Eine gemessene Zahl kann richtig sein und trotzdem die falsche Frage
  beantworten** (16.09.2026, `docs/plans/AUDIOQUALITAET-PLAN.md`). Der
  Rauschfilter stand in der Filterstatistik des SDK bei «77 bis 87 Prozent» —
  das ist der Anteil **an der Filterkette**, und daraus wurde erst «der Filter
  ist zu teuer». Sein **Mittel** lag bei 0,7 bis 0,9 ms von 10 ms, sein
  Maximum in vier von sechs Gesprächen unter 6,2 ms; nur zweimal schoss es auf
  77 ms. Das ist kein Rechenaufwand, sondern ein Stillstand — und die
  Konsequenz aus der ersten Lesart (den Filter im Standard abschalten) hätte
  jeden ausgelieferten Arbeitsplatz verschlechtert. **Wer eine Zahl zitiert,
  sagt dazu, worauf sie sich bezieht** — und ein Mittelwert und ein Maximum
  beantworten nie dieselbe Frage.
- **Was an ein System weitergereicht wird, das darauf handelt, wird vorher
  verglichen** (ADR-060). Eine Meldung an `Core.NetworkReachable` ist keine
  Auskunft, sondern ein Auftrag — sie kostet eine Neuregistrierung; ein
  `Changed` der Einstellungen ist es auch, denn daran hängen vier Empfänger.
  Beide prüfen jetzt an **einer** Stelle, ob sich überhaupt etwas geändert hat:
  `ConnectivityMonitor.Signatur` (Erreichbarkeit **und** lokale Adressen) und
  `SettingsService.Write` (der Text, der auf die Platte ginge). **Die
  Gegenprobe ist dabei die wichtigere Hälfte** — eine zu grobe Bremse
  verschluckt den echten Netzwechsel.
- **Wer eine Fähigkeit entfernt, streicht ihre Testzeilen im selben Commit.**
  ADR-062 hat den Mailbox-Reiter am 13.09.2026 entfernt — und am selben Abend
  standen in `docs/test-matrix.md` noch **eine ganze Zeile** dafür und **vier
  weitere**, die die Mailbox in ihrer Erwartung nannten. Dazu zwei Zeilen, die
  ältere ADRs überholt hatten, ohne dass jemand sie strich; bei einer stand die
  Streichung sogar im Fliesstext über der Tabelle, nur nicht in der Zeile.
  **Wer das am Gerät prüft, meldet Fehlschläge, die keine sind** — und das ist
  teurer als der Strich, der hier fehlte. Eine gestrichene Zeile trägt `~~…~~`,
  den Grund und die Zeile, die sie ersetzt.
- **Wer in einem Plan eine Prüfzeile formuliert, trägt sie im selben Commit in
  `docs/test-matrix.md` ein.** Am 17.09.2026 standen **vierzehn** Zeilen
  (T304 bis T317) in zwei Plänen und in der Matrix gar nicht — **T312 galt
  derweil als bestanden**, im Plan, nicht in der Matrix. Wer die Matrix
  abarbeitet, hätte das Gespräch im breiten Fenster nie geprüft, und die
  Zählung «wie viel ist offen» war vier Tage lang zu niedrig. Ein Plan
  **begründet** eine Zeile; die Matrix ist die Stelle, an der sie **steht** —
  das ist dieselbe Regel wie überall sonst hier, und sie gilt für Prüfzeilen
  genauso wie für Feldnamen und Zustandswörter.
- **Eine Zahl in einer Testzeile nennt die Grösse, die sie meint.** «ab 960
  Pixeln» ist keine Angabe, solange nicht dasteht, ob Fenster oder Seite
  gemeint ist — siehe die Schwelle oben. Dasselbe gilt für logisch gegen
  physisch, sobald die Maschine nicht auf 100 % steht.
- **Abstände stehen als Name da, nicht als Zahl** (ADR-058). Die Skala ist
  `NippGapHair` (1), `NippGapSmall` (3), `NippGapMedium` (6), `NippGapLarge`
  (8), `NippGapSection` (12); `SpacingTokenTests` erzwingt beides — kein
  Literal, und genau diese fünf Stufen. Ein eigenes Token gibt es nur, wo
  Fluent nichts hat: die Eckradien kommen aus `ControlCornerRadius` und
  `OverlayCornerRadius`.

## SDK beschaffen (nach einem frischen Klon nötig)
Das Linphone SDK liegt **nicht im Repo** (299 MB, gitignoriert). Der
GitLab-NuGet-Feed ist unzuverlässig (ADR-005) — deshalb das Prebuilt-ZIP, und
seit dem 17.09.2026 **aus dem eigenen Abhängigkeits-Repo** (ADR-069):

    https://github.com/bv2-GmbH/nipp-build-deps/releases/download/linphone-sdk-5.5.18/linphone-sdk-win64-5.5.18.zip
    -> nach sdk/ herunterladen, nach sdk/extracted/ entpacken

**Warum nicht mehr von `download.linphone.org`:** dort lag ab dem 07.09.2026
eine **andere Datei** unter derselben Adresse und derselben Versionsnummer —
198 342 Bytes mehr, ein stilles Neuablegen desselben Release. Damit brach jeder
CI-Lauf im öffentlichen Repo an der Prüfsumme ab, **siebzehn Läufe lang vom
ersten Commit an**, und zwar bevor `PublicRepositoryTests` lief: die letzte
Kontrolle vor der Öffentlichkeit hat seit dem Repo-Wechsel nur noch lokal
gegriffen. **Die Prüfsumme nachzuziehen wäre die falsche Antwort gewesen** —
sie ist die Kontrolle dagegen, dass ein unbesehen verändertes SDK in den Build
kommt (dieselbe Überlegung wie ADR-040). Jetzt liegt das geprüfte ZIP
unverändert unter eigener Kontrolle, mit derselben Prüfsumme wie bisher.

**Beim nächsten SDK-Wechsel** wird die neue Fassung erst geholt, angesehen und
gegen die alte verglichen; dann liegt sie im Abhängigkeits-Repo, und erst dann
wandern Version, Adresse und Prüfsumme in `ci.yml`, `release.yml` und
`docs/sdk-setup.md` — **drei Stellen, die zusammen gepflegt werden.**

SHA256 und Layout in docs/sdk-setup.md. Der Wrapper landet dann unter
`sdk/extracted/linphone-sdk/win64/share/linphonecs/LinphoneWrapper.cs`
und ist die einzige Wahrheit über die API — Abweichungen von der
Spezifikation stehen in docs/sdk-api-notes.md.

## Bauen auf dieser Maschine
**Windows steht hier auf 150 %.** Der Bildschirm ist physisch 2880 × 1800 und
damit logisch 1920 × 1200 — und XAML rechnet in **logischen** Pixeln. Wer eine
Fenstergrösse misst oder setzt, rechnet um; 830 physische Pixel sind logisch
553. Jede Zahl in `docs/test-matrix.md` ist logisch gemeint.

Die Entwicklungsmaschine ist ARM64, das x64-SDK liegt deshalb unter
`C:\Program Files\dotnet\x64` und ist vom `dotnet` im PATH **nicht**
sichtbar. Immer `.\build.ps1 <args>` benutzen, nie ein blankes `dotnet` —
das Skript löst den richtigen Host auf und funktioniert auf echter
x64-Hardware unverändert.

**Argumente mit `-p:` oder `-t:` brauchen den Stopp-Parser `--%`:**

    .\build.ps1 --% build src\Nipp.App -c Debug -p:WindowsPackageType=None -t:Rebuild

Ohne ihn bindet PowerShell `-p:` an einen eigenen Parameter und bricht mit
*parameter name 'p' is ambiguous* ab.

**Läuft nipp gerade, scheitert der Build** am gesperrten `Nipp.Core.dll`
(MSB3027). Erst beenden — über das Symbol im Infobereich, nicht über das
Fensterkreuz: Schliessen beendet nipp nicht (§10).

**Beendet sich nipp scheinbar nicht**, ist der Prozess `Nipp.App` im
**Task-Manager** zu prüfen, nicht das Symbol im Infobereich. Der Grund dafür
war `Application.Exit()` auf dem falschen Thread; behoben am 12.09.2026, die
Geschichte steht in `docs/lehren.md`. Seit ADR-038 ist das mehr als lästig:
ein Prozess, der seine DLLs offen hält, lässt jedes Update scheitern.

## Zweite Arbeitslinie: Integrationsplattform (§21)

Eine **generische** Anbindung an CRM, ERP, Ticketing und beliebige REST-APIs
für Anruferkontext und Kontaktsuche. Auftrag: NIPP-BUILD.md §21 (Rev. 6) und
**§21.6** (Rev. 8). Phasen und Zielarchitektur: `docs/plans/INTEGRATION-PLAN.md`.
Einrichtung und Karten: `docs/plans/EINRICHTUNG-PLAN.md`. Entscheidungen: ADR-015 bis
ADR-017, ADR-032 bis ADR-037, **ADR-040**.

Gebaut ist sie unter `Services/Integrations/`; **was davon steht, sagt
`docs/stand.md`** — eine Regeldatei führt keinen eigenen Umsetzungsstand
(W2.7). **Die Vorlagen der beiden bv2-eigenen Quellen sind seit ADR-040 nicht
mehr im Repo**, sondern liegen als importierbare Dateien im privaten
Vorlagen-Repo; ohne sie steht im Katalog nur „Eigene REST-API".

### Die drei Regeln, die über allem stehen

- **Telefonieren hängt von keiner Integration ab.** Fällt jedes externe System
  aus, klingelt und wählt nipp unverändert.
- **Nichts Blockierendes im SDK-Callback.** Ein Lookup wird im Ereignis
  angestossen, nie erwartet — und die Gesprächsansicht erscheint sofort.
- **Kein Cache auf der Platte.** Personenbezogene Daten fremder Systeme bleiben
  im Arbeitsspeicher. Das gilt auch für die Testdaten der Kartenvorschau;
  damit sie trotzdem nie leer ist, bringt jede Vorlage eine **erfundene**
  Beispielantwort mit.

### Zur Suche

**`OutlookContactSource` ist unangetastet.** Die Migration ist ein Adapter
(`LocalSnapshotSearchProvider`), der aus dem `ContactStore` liest — Outlook
wird weiterhin nicht je Tastendruck über COM gefragt. Das Suchfeld erscheint
nur, wenn eine Quelle über das Netz sucht; sonst bliebe es ein zweiter Weg zu
demselben Adressbuch, das die Vorschlagsliste schon durchsucht (ADR-014).

### Outlook-Kontakte: es bleibt bei COM, und hier fehlen sie deshalb

Die Entwicklungsmaschine hat das **neue** Outlook (`olk.exe`,
`Microsoft.OutlookForWindows`), und das bietet **kein COM** an — es gibt kein
`Outlook.Application` und keinen ROT-Eintrag. Der Weg aus §8.4 ist dort nicht
kaputt, sondern nicht vorhanden.

Das ist entschieden und kein offener Punkt (**ADR-018**, §22.4): Microsoft
Graph wurde erwogen und **zurückgestellt**, weil die Entra-App-Registrierung
einen Administrator bindet und Wochen dauert. Team-Nebenstellen und die
die Suche im CRM decken den Alltag ab; nipp nennt in der Oberfläche den Grund,
statt eine leere Liste zu zeigen.

**Wieder aufgemacht wird die Frage**, sobald der erste Arbeitsplatz ausserhalb
der Entwicklung auf das neue Outlook wechselt und dort Kontakte vermisst. Die
Antwort ist vorbereitet — Graph über die Integrationsplattform — und die
Registrierung gehört in dem Moment sofort angestossen.

ADR-009 bleibt in seiner Wahl gültig; **seine Begründung** („lohnt sich nicht,
solange nipp intern läuft") ist durch ADR-018 ersetzt, weil sie eine Annahme
enthielt, die nicht mehr trägt.

## Umgebungsvorbehalt
Die Entwicklungsmaschine ist **ARM64**, das Produkt ist **x64 only**.
x64 läuft hier nur emuliert.

**Entschieden am 07.09.2026 (ADR-001): es gibt auf unbestimmte Zeit keine
x64-Maschine.** Damit gilt Option 3 — alles hier, einschliesslich der
Verifikation —, und das ist ein **bewusst getragenes Risiko**, keine Lösung.
Ungemessen bleiben damit: die vier nichtfunktionalen Ziele aus §2 (AP7.8),
das M1-Gate in seiner Beweiskraft, der Jitter-Befund aus ADR-006 Punkt 3
(T38) und AP9.5. **Fällig vor der ersten Kundenabgabe.**

Es braucht dafür kein eigenes Gerät: T38 und AP7.8 sind an einem Nachmittag
auf einer geliehenen Maschine abzuarbeiten. Wer einmal Zugang zu einer hat —
ein Kundengerät bei einer Installation, ein Testrechner —, sollte ihn dafür
benutzen.

**Und seit dem 16.09.2026 gibt es dafür eine konkrete Zeile.** Ein Gespräch
führen, im Protokoll die Tabelle `FILTER USAGE STATISTICS` suchen und den Wert
`max` von `MSNoiseSuppressor` ablesen: hier schiesst er sporadisch auf 77 ms
bei einem Ticker, der alle 10 ms läuft — und genau dann setzt das Audio aus.
**Bleibt er auf echter Hardware unter 6 ms, war es die Emulation** und T38 ist
beantwortet. Die ganze Kette steht in `docs/plans/AUDIOQUALITAET-PLAN.md`.

Packaged und unpackaged laufen beide (ADR-008). Voraussetzung:
Entwicklermodus (AllowDevelopmentWithoutDevLicense=1), sonst scheitert die
Paketregistrierung mit 0x80073CFF. **Ausgeliefert wird unpackaged**, solange
T110 offen ist — packaged bekommt keine Toasts, und ohne Toast ist §8.6 nicht
erfüllt. Details: docs/environment.md.

---

## Wo was steht

| Frage | Dokument |
|---|---|
| Was soll nipp tun? | **`NIPP-BUILD.md`** — die Spezifikation. Im Zweifel dort nachlesen |
| Warum weicht etwas davon ab? | **`docs/decisions.md`** — ADR-001 bis ADR-069 |
| Wo steht das Projekt insgesamt? | **`docs/plans/REVIEW-2026-09-12.md`** — 83 Befunde auf fünf Achsen, Massnahmenplan in Wellen. **`docs/plans/WELLE-0-PLAN.md`** — die fünf Schritte davor, alle umgesetzt |
| Wo hakt die Bedienung? | **`docs/plans/UX-REVIEW-2.md`** — 20 Befunde (12.09.2026), davor **`docs/plans/UX-REVIEW.md`** — 25 Befunde. Umsetzungsstand jeweils ganz vorn |
| Was passiert auf einem breiten Fenster? | **`docs/plans/BREITBILD-PLAN.md`** — zwei Spalten, Kacheln, der Detailbereich in der Zeile |
| Was ist zuletzt passiert? | **`docs/stand.md`** — der Meilenstein und die Chronologie von unten nach oben. Die Pläne selbst liegen unter `docs/plans/` |
| Wie richte ich eine Quelle ein? | `docs/integrations/einrichten.md` |
| Wie stelle ich eine Karte zusammen? | `docs/integrations/karten.md` |
| Welcher Feldname bedeutet was? | `docs/integrations/feldnamen.md` (aus dem `FieldCatalog` erzeugt) |
| Was hat uns schon einmal Tage gekostet? | **`docs/lehren.md`** — Telefonie, Audio, WinUI, Windows-Integration, Konfiguration, Bauen, packaged |
| Was ist am Gerät zu prüfen? | `docs/test-matrix.md` — was davon ohne Anlage geht, nimmt `tools/Test-Ui.ps1` ab |
| Wie liefere ich aus und verteile Updates? | **`docs/updates.md`** — Setup, Kanäle, Token, kaputtes Release. Wie es dazu kam: `docs/plans/RELEASE-PLAN.md` |
| Warum ist das Repo öffentlich, und was hing daran? | `docs/plans/OEFFENTLICH-PLAN.md` — die sechs Schritte, alle erledigt |
| Wie baue und paketiere ich? | `docs/packaging.md` (MSIX), `docs/environment.md` |
| Wo weicht die SDK-API von der Spezifikation ab? | `docs/sdk-api-notes.md` |
| Unter welcher Lizenz steht nipp? | `LICENSE` (AGPLv3), `NOTICE`, `docs/licensing.md` — und was bis zum öffentlichen Repo fehlt |
| Überblick für einen Menschen | `README.md` |

Ältere Prüfungen, auf die einzelne Befunde verweisen: `docs/review-oberflaeche.md`
(Oberfläche, 05.09.2026) und `docs/blf-pruefung.md` (Besetztlampenfeld gegen die
echte Anlage).
