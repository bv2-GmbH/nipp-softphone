# UX-Review, zweite Runde — was nach den drei Phasen übrig ist

**Datum:** 12.09.2026 · **Grundlage:** alle vier XAML-Ansichten samt
Code-behind, das Designer-Fenster, das Infobereich-Symbol, der Toast und die
ViewModels in `Nipp.Core` · **Befunde:** 20

Das erste Review (`UX-REVIEW.md`, 12.09.2026) hat fünfundzwanzig Befunde
gefunden, vierundzwanzig sind umgesetzt. **Dieses Review wiederholt keinen
davon.** Es sucht dort, wo die Umsetzung neue Kanten erzeugt hat, und an den
Stellen, die das erste Review nicht angeschaut hat: das Designer-Fenster, das
Infobereich-Menü, und das Zusammenspiel zwischen Fenster, Toast und Tastatur.

> **Zur Grundlage.** Gelesen wurde der Code, nicht eine laufende Instanz. Ein
> Start hätte nipp an der Telefonanlage angemeldet und wäre in den laufenden
> Betrieb hineingegangen; das war es nicht wert. **Drei Befunde brauchen
> deshalb eine Gegenprobe am Gerät** — sie sind unten ausdrücklich als solche
> gekennzeichnet.

> **Zum Umfang.** Die Projektregel «Kein Feature ohne Auftrag aus
> NIPP-BUILD.md» gilt hier mit. Von den zwanzig Befunden sind **sechzehn
> Korrekturen** an Gebautem, **drei** verlangen einen ADR (C7, C15, C16), und
> **einer** ist ausdrücklich als Vorschlag ohne Auftrag gekennzeichnet.

---

## Umsetzungsstand

| Phase | Inhalt | Stand |
|---|---|---|
| **1 — Die scharfen Kanten** | C1, C2, C3, C4, C10 | **umgesetzt** (ADR-049), T222–T230 |
| **2 — Der Alltag** | C6, C7, C8, C9, C18, C19, C20 | **umgesetzt** (ADR-050), T231–T240 |
| **3 — Die Struktur** | C5, C11, C12, C13, C14, C17 | **umgesetzt** (ADR-051), T241–T250 |

**Achtzehn von zwanzig umgesetzt. Am Gerät abgenommen ist nichts davon** —
T222 bis T250.

**Zwei bleiben offen, und beide mit Grund:**

- **C16** (die Mailbox in der Navigation) — **zurückgestellt wegen eines
  Widerspruchs, den dieser Bericht nicht gesehen hat.** Fassung A wollte die
  Mailbox-Fläche verstecken, solange keine Nummer hinterlegt ist. Genau dort
  steht aber der Knopf «Mailboxnummer eintragen», den C8 in derselben Runde
  repariert — die Fläche zu verstecken nähme den einzigen Weg weg, sie
  einzurichten. Fassung B ist die bessere Struktur und berührt §20.1,
  `ShellSection` und vier Tests.
- **C15** («Nicht stören») — **wartet auf einen Entscheid.** Es ist der einzige
  Befund der Runde, der eine neue Fähigkeit vorschlägt statt etwas
  zurechtzurücken. Die Hälfte seines Anlasses ist mit C2 weg: das Fenster
  springt beim Klingeln nicht mehr nach vorn. Was bleibt, ist der Klingelton in
  einer Besprechung.

**Und eine Korrektur an diesem Bericht selbst.** C5 empfahl, die Netztreffer in
die Vorschlagsliste wachsen zu lassen und `SearchBody` aufzulösen. Beim Lesen
des Suchdienstes zeigte sich der bessere Weg: **lokale Quellen werden dort
ohnehin vor dem Debounce gefragt**, die Trefferliste ist also genauso schnell —
und sie kann mehr. Umgekehrt kennt nur die Vorschlagsliste die Anrufliste.
Umgesetzt ist deshalb eine Regel statt eines Umbaus: **eine Nummer zeigt die
Vorschlagsliste, ein Name die Trefferliste** — dieselbe Unterscheidung, die C4
der Eingabetaste gegeben hat.

---

## 1 · Die wichtigsten Erkenntnisse

1. **Es gibt wieder einen Weg, Arbeit lautlos zu verlieren — und es ist
   derselbe Bautyp wie B1.** Der Karten-Designer führt `HasUnsavedChanges`,
   zeigt es nirgends an und prüft es beim Schliessen nicht. Wer das Fenster
   über das Kreuz verlässt, verliert eine halbe Stunde Kartenarbeit ohne
   Rückfrage und ohne Hinweis. **Die Eigenschaft ist gebaut und nicht
   angeschlossen — zum fünften Mal in diesem Projekt.**

2. **Zwei je für sich richtige Entscheidungen ergeben zusammen einen
   ungewollten Anruf.** ADR-044 hat den Fokus beim Klingeln auf «Annehmen»
   gelegt (B10, richtig). `MainWindow` holt das Fenster beim Klingeln
   zusätzlich in den Vordergrund. Zusammen heisst das: wer gerade in Word
   tippt, bekommt das nipp-Fenster vor die Nase, und die nächste Leertaste
   oder Eingabetaste **nimmt den Anruf an**. §8.6 verlangt den Vordergrund
   ausdrücklich beim *Klick auf Annehmen*, nicht beim Klingeln.

3. **Ein Feld mit zwei Bedeutungen, und die Eingabetaste kennt nur eine.**
   Seit ADR-046 ist das Nummernfeld auch das Suchfeld. Wer «Meier» tippt und
   Enter drückt, löst einen Anruf an `sip:Meier@…` aus — der
   `NumberNormalizer` reicht benannte Ziele absichtlich unverändert durch. Der
   Benutzer hat gesucht und bekommt einen fehlgeschlagenen Anruf.

4. **Dieselbe Eingabe erzeugt zwei Trefferlisten übereinander.** Unter dem
   Feld stehen die lokalen Vorschläge, im Bereich darunter die Suchtreffer —
   und die enthalten dieselben lokalen Kontakte noch einmal, weil
   `LocalSnapshotSearchProvider` aus demselben `ContactStore` liest. Derselbe
   Kollege steht zweimal auf dem Bildschirm, in zwei Listen mit zwei
   verschiedenen Gesten: oben übernimmt ein Klick die Nummer, unten wählt ein
   Doppelklick. **Das ist die Doppelanzeige, die ADR-046 bei den zwei
   Suchfeldern aufgelöst hat — sie ist eine Ebene tiefer gewandert.**

5. **Aufnehmen kostet einen Klick und liegt neben dem Knopf, den man am
   häufigsten drückt.** «Stumm», «Halten» und «Aufnahme» stehen als drei
   gleich grosse, gleich aussehende Umschalter nebeneinander. Einer davon
   startet eine Aufzeichnung, die in der Schweiz ohne Kenntnis der Gegenseite
   strafbar ist (Art. 179ter StGB). Die Warnung erscheint **danach**. Ein
   Fehlgriff ist hier kein Ärgernis, sondern ein Rechtsproblem.

6. **Ein Drittel des Inhaltsbereichs geht an eine Tastatur, die auf einem PC
   niemand braucht.** `ShowDialpad` steht auf `true`. Bei 400 × 660 bleiben
   der Kontaktliste damit rund 250 Pixel — fünf Zeilen, verteilt auf zwei
   Abschnitte mit Kopfzeilen. Ohne die Tastatur wären es zehn. Zum Wählen hat
   jeder Arbeitsplatz eine Tastatur; für Sprachmenüs gibt es die eigene
   Zehnertastatur in der Gesprächsansicht.

7. **Im Gespräch gibt es kein einziges Tastenkürzel.** Stummschalten ist der
   häufigste Griff eines Softphone-Benutzers und nur mit der Maus erreichbar —
   und nur, wenn das Fenster gerade vorne steht. Teams und Zoom haben dafür
   seit Jahren ein systemweites Kürzel; nipp hat einen globalen Hotkey, der
   annimmt und auflegt, aber nicht stummschaltet.

8. **Zwei Abkürzungen aus ADR-045 führen nicht ganz dorthin, wohin sie
   zeigen.** «Mailboxnummer eintragen» springt in die Kontogruppe, setzt den
   Fokus auf **Benutzername** und öffnet ein **leeres Anlegen-Formular** — die
   Mailboxnummer steht in einem zugeklappten Unter-Aufklapper eines
   *bestehenden* Kontos. Der Knopf, der das Suchen ersparen sollte, lässt den
   Benutzer an der richtigen Gruppe stehen und weitersuchen.

9. **Drei Knöpfe tun stillschweigend nichts.** «Stumm schalten» im
   Infobereich-Menü ohne laufendes Gespräch, «Entfernen» bei der letzten
   Gruppe, und der Aufnahme-Eintrag der Anrufliste bei einem Anruf ohne
   Aufnahme (letzterer meldet es wenigstens). Ein Klick ohne Wirkung und ohne
   Wort ist von einem Fehler nicht zu unterscheiden — es ist dieselbe Lücke,
   die dieses Projekt bei HID-Reports und beim Infobereich-Symbol schon
   zweimal teuer bezahlt hat.

10. **Das breite Layout hat eine Hälfte ohne Suche.** Rechts stehen vierzig
    Kacheln, und der einzige Weg zu einer bestimmten ist Scrollen: das
    Nummernfeld links filtert die Trefferliste, nicht das Kachelraster. Bei
    acht Nebenstellen fällt das nicht auf; bei vierzig ist die Kachelform dann
    schlechter als die Liste, die sie ersetzt hat.

---

## 2 · Gesamteindruck

Bewertet wird der Stand **nach** ADR-044 bis ADR-048. Zum Vergleich steht die
Bewertung des ersten Reviews daneben.

| | Vorher | Jetzt | Begründung |
|---|---|---|---|
| Verständlichkeit | 7 | **8** | Die Zustandskataloge haben die Begriffe vereinheitlicht, die Fehlertexte nennen Ursache und Abhilfe. Abzug: «Weiterleiten» heisst im Gespräch anders als im Kontextmenü der Anrufliste, und die Zeile «sucht auch in …» erklärt nicht, was mit der Eingabetaste passiert |
| Einfachheit | 4 | **6** | Zwei Ebenen in den Einstellungen haben viel gebracht. Es bleiben 64 Eingabeelemente und 37 Schaltflächen auf **einer** Seite, und die Integrationen — ein Administrationswerkzeug mit JSON-Editor — stehen weiter im Benutzerteil |
| Navigation | 7 | **7** | Vier Bereiche, sichtbar markiert, Strg+1 bis 4. Unverändert: kein Escape, kein Alt+Links, und der Rückweg sieht auf jeder Seite ein bisschen anders aus |
| Visuelle Hierarchie | 6 | **7** | Die Deckelung je Spalte hält die Zeile zusammen, die Statustöne sind geprüft. Abzug: drei gleichwertige Umschalter, von denen einer aufzeichnet; und die Kachelspalte wächst ohne Obergrenze |
| Effizienz | 5 | **6** | Wählen, Zurückrufen und Weiterleiten sind kurz. Im Gespräch endet die Effizienz an der Maus, und ein Drittel der Hauptansicht geht an die Wähltastatur |
| Konsistenz | 4 | **6** | Ein Speichermodell auf der Einstellungsseite, ein Detailbereich, ein Wortschatz. Dagegen: der Designer hat «Speichern» und «Verwerfen», also die zweite Speicherregel im selben Programm |
| Fehlervermeidung | 5 | **5** | Die Dialoge sind vorbildlich und einheitlich. Dagegen der Datenverlust im Designer, das versehentliche Annehmen, der Aufnahmeknopf ohne Rückfrage und die Enter-Taste auf einem Namen. **Netto keine Verbesserung** |
| Barrierefreiheit | 5 | **7** | Kontextmenüs per Tastatur, Zahlen in den Namen der Umschaltleiste, Kontraste geprüft, Klickziele auf 32 Pixel. Abzug: kein Kürzel im Gespräch, und die Nummernknöpfe im Detailbereich sagen nur die Nummer statt «Mobil anrufen» |

**Die Zahl, die zählt, ist die unveränderte.** Fehlervermeidung steht nach drei
Umsetzungsphasen genauso da wie davor — die alten Löcher sind gestopft, und in
denselben Wochen sind drei neue entstanden. Alle drei folgen demselben Muster:
**ein Weg wurde gebaut und seine Kehrseite nicht mitgedacht.**

---

## 3 · Die Befunde nach Schadenshöhe

| # | Befund | Prio | Aufwand |
|---|---|---|---|
| **C1** | Designer-Fenster schliessen verliert die Arbeit, ohne Rückfrage | **P0** | S |
| **C2** | Das Fenster springt beim Klingeln nach vorn — mit dem Fokus auf «Annehmen» | **P0** | S |
| **C3** | Aufnehmen startet mit einem Klick, neben dem Stummschalter | **P1** | S |
| **C4** | Enter auf einem Namen wählt den Namen | **P1** | S |
| **C5** | Zwei Trefferlisten für dieselbe Eingabe, übereinander | **P1** | M |
| **C6** | Wähltastatur standardmässig an, kostet ein Drittel der Liste | **P1** | S |
| **C7** | Kein Tastenkürzel im Gespräch, kein systemweites Stummschalten | **P1** | M |
| **C8** | «Mailboxnummer eintragen» landet im falschen Formular | **P1** | S |
| **C9** | Kein Escape, kein Alt+Links — der Rückweg hängt an einem Pfeil | **P1** | S |
| **C10** | Drei Knöpfe tun stillschweigend nichts | **P2** | S |
| **C11** | Das Kachelraster hat keine Suche | **P2** | M |
| **C12** | Zwei Speichermodelle im selben Programm | **P2** | S |
| **C13** | Die Integrationen stehen im Benutzerteil | **P2** | S |
| **C14** | Der Weg zur Anruferkarte ist vier Ebenen tief | **P2** | S |
| **C15** | Kein «Nicht stören» | **P2** | M |
| **C16** | Die Mailbox belegt ein Viertel der Hauptnavigation für eine Zahl | **P2** | M |
| **C17** | Die Kachelspalte wächst ohne Obergrenze | **P3** | S |
| **C18** | Die Anrufliste zeigt die Nummer nicht, die sie zurückruft | **P3** | S |
| **C19** | Die Nummernknöpfe im Detailbereich sagen nicht, was sie tun | **P3** | S |
| **C20** | Der Sortiermodus erklärt sich nicht | **P3** | S |

---

## 4 · Die Befunde im Einzelnen

### C1 — Das Designer-Fenster schliessen verliert die Arbeit · P0 · S

**Problem.** `CardDesignerViewModel.HasUnsavedChanges` wird an drei Stellen
gesetzt und an **einer** gelesen: in `OnSaveClick`, um zu entscheiden, ob das
Fenster danach zugeht. `OnClosed` prüft sie nicht, es gibt kein
`AppWindow.Closing` mit Rückfrage, und weder der Fenstertitel noch die
Schaltflächenleiste zeigen an, dass etwas offen ist.

**Auswirkung.** Eine Karte hat zwanzig Bausteine, und der Designer ist das
Werkzeug, in dem man eine halbe Stunde sitzt. Wer das Fenster über das Kreuz
verlässt — die geläufigste Geste, um ein Fenster loszuwerden —, verliert alles,
lautlos. Beim nächsten Öffnen steht die alte Karte da, und nichts erklärt es.

**Empfehlung.** Drei Dinge, alle klein:

1. `AppWindow.Closing` abfangen, `args.Cancel = true` setzen, solange
   `HasUnsavedChanges` gilt, und den bestehenden Rückfragedialog zeigen:
   **«Änderungen an der Karte verwerfen?»** mit «Verwerfen» /
   «Weiterbearbeiten», `DefaultButton = Close` wie bei allem Destruktiven.
2. Den Fenstertitel um einen Punkt ergänzen, solange etwas offen ist —
   `Anruferkarte •` statt `Anruferkarte`. Das ist die Windows-Konvention und
   kostet eine Zeile.
3. «Speichern» hervorheben (`AccentButtonStyle`), solange es etwas zu speichern
   gibt.

**Und die Lehre daneben.** Das ist das fünfte Mal — nach `CardKind.History`,
`IntegrationConfig.cards`, `ClipResolver.DescribeCaller` und `App.SdkStatus` —,
dass eine Fähigkeit gebaut und nicht angeschlossen wurde. Bei den vier davor
fehlte eine Anzeige; hier fehlt eine Schutzregel, und das kostet Arbeit statt
Auffindbarkeit. **Ein Test, der jede öffentliche Eigenschaft der ViewModels
gegen ihre Verwender zählt, hätte alle fünf gefunden** — die Fassung dafür steht
unten unter «Grössere strukturelle Verbesserungen».

---

### C2 — Das Fenster springt beim Klingeln nach vorn · P0 · S

**Problem.** `MainWindow.OnCallStateChanged` ruft bei jedem eingehenden Anruf
`ShowFromTray()`, und das ist voller Fokusraub: `AppWindow.Show()`,
`presenter.Restore()`, `Activate()` und als Rückfall `SetForegroundWindow`.
Gleichzeitig navigiert die Seite zur Gesprächsansicht, wo `FocusPrimaryAction`
den Fokus auf «Annehmen» legt (ADR-044, B10).

**Auswirkung.** Zwei Dinge, und das zweite ist das schlimmere:

- Wer gerade in einer anderen Anwendung arbeitet, verliert beim Klingeln den
  Fokus. Was danach getippt wird, landet in nipp.
- **Die nächste Leertaste oder Eingabetaste nimmt den Anruf an.** Niemand hat
  etwas angeklickt.

**Und es steht so nicht in der Spezifikation.** §8.6 sagt: «Klick auf Annehmen:
App in den Vordergrund, direkt in die Gesprächsansicht.» Der Vordergrund gehört
an das *Annehmen*, nicht an das Klingeln. Genau dafür gibt es den Toast — er
läuft im Szenario `IncomingCall`, bleibt stehen, bis jemand reagiert, und
erscheint auch über einer Vollbildanwendung.

**Empfehlung.**

1. Beim Klingeln **nicht** `Activate()`. Wer das Fenster sehen will, klickt den
   Toast oder das Symbol im Infobereich.
2. Wenn das Fenster ohnehin offen ist, darf nipp in die Gesprächsansicht
   navigieren — nur ohne den Fokus zu holen.
3. Der Fokus auf «Annehmen» bleibt richtig, sobald das Fenster **auf
   Benutzerwunsch** vorne steht.
4. Braucht es die alte Fassung doch — etwa auf Arbeitsplätzen, wo Toasts
   ausfallen —, dann als Einstellung unter «Start und Bedienung»: **«Fenster
   zeigen, wenn es klingelt»**, Standard aus.

**Am Gerät zu prüfen:** die Gegenprobe, dass ein eingehender Anruf bei
geschlossenem Fenster weiterhin sichtbar ist (das ist T06, bestanden am
07.09.2026 — hier geht es darum, dass er es **ohne** `Activate()` bleibt).

---

### C3 — Aufnehmen startet mit einem Klick, neben dem Stummschalter · P1 · S

**Problem.** In der Gesprächsansicht stehen «Stumm», «Halten» und «Aufnahme»
als drei gleich grosse `ToggleButton` nebeneinander, gleiches Aussehen,
gleicher Stil, gleiche Spaltenbreite. Ein Klick auf den dritten startet die
Aufzeichnung sofort; die Warnleiste «Dieses Gespräch wird aufgezeichnet»
erscheint danach.

**Auswirkung.** «Stumm» ist der Knopf, den man im Gespräch am häufigsten und am
schnellsten drückt — oft ohne hinzusehen, weil man gerade hustet oder jemand ins
Büro kommt. Zwei Spalten daneben liegt eine Handlung, die in der Schweiz ohne
Kenntnis der Gegenseite strafbar ist (Art. 179ter StGB) und eine Datei auf der
Platte hinterlässt. **Die drei sind visuell gleichwertig, obwohl einer von ihnen
eine andere Art von Konsequenz hat als die anderen beiden.**

**Empfehlung.** Nicht die Bestätigung bei jedem Mal — das wäre für den, der
bewusst aufzeichnet, eine Zumutung. Sondern:

1. **Beim ersten Start je Gespräch eine Rückfrage:** «Gespräch aufzeichnen? Die
   Gegenseite muss davon wissen.» mit «Aufzeichnen» / «Abbrechen»,
   `DefaultButton = Close`. Innerhalb desselben Gesprächs danach nicht mehr.
2. Den Knopf aus der Dreierreihe herausnehmen: «Stumm» und «Halten» bleiben
   nebeneinander, «Aufnahme» wandert in die zweite Reihe zu «Weiterleiten» und
   «Tastentöne» — zu den Handlungen, die man bewusst sucht.
3. Solange die Aufnahme läuft, färbt der Knopf Schrift und Rand im
   Aufnahmerot (`RecordingIndicatorBrush`) — **nie die Fläche**, ADR-044 gilt.

---

### C4 — Enter auf einem Namen wählt den Namen · P1 · S

**Problem.** Seit ADR-046 ist das Nummernfeld auch das Suchfeld: jede Eingabe
geht an `ContactQuery` und damit an die Quellen. Die Eingabetaste ist aber
unverändert verdrahtet — `OnNumberBoxKeyDown` führt `DialCommand` aus, sofern
`CanDial` gilt, und das gilt bei jedem nicht-leeren Text. Der
`NumberNormalizer` reicht benannte Ziele absichtlich unverändert durch (er darf
aus «112» kein «+41112» machen), also geht ein `INVITE sip:Meier@…` hinaus.

**Auswirkung.** Der Benutzer hat gesucht und bekommt eine Fehlermeldung über
einen Anruf, den er nicht wollte. Das ist die häufigste Art, wie ein Feld mit
zwei Bedeutungen kaputtgeht: die Standardtaste kann nur eine davon bedienen.

**Empfehlung.** Die Eingabetaste entscheidet nach dem, was im Feld steht:

- **Sieht die Eingabe wählbar aus** (Ziffern, `+`, `*`, `#`, eine SIP-Adresse):
  wählen, wie bisher.
- **Sieht sie nach einem Namen aus** und es gibt Treffer: in die Trefferliste
  springen, nicht wählen — dieselbe Regel, aus der die Vorschlagsliste schon
  heute nur übernimmt statt zu wählen («ein Fehlgriff in einer Liste, die beim
  Tippen aufspringt, wäre sonst ein Anruf bei der falschen Person»).
- **Sieht sie nach einem Namen aus und es gibt keine Treffer:** `DialIssue`
  füllen — **«Das ist keine Nummer. Einen Treffer auswählen oder eine Nummer
  eingeben.»** Der Knopf ist dann grau *mit* Grund, wie ADR-045 es verlangt.

Die Prüfung selbst gehört in den Kern, neben den Normalizer: `NumberNormalizer`
weiss bereits, was «adressartig» ist (`IsAddressLike`, `KeepDialCharacters`) —
die Frage «ist das überhaupt wählbar?» ist dieselbe Prüfung mit umgekehrtem
Vorzeichen und gehört an dieselbe Stelle.

---

### C5 — Zwei Trefferlisten für dieselbe Eingabe, übereinander · P1 · M

**Problem.** Wer einen Namen tippt, sieht gleichzeitig:

- **die Vorschlagsliste** direkt unter dem Feld (`SuggestionPanel`, höchstens
  fünf, aus Kontakten und Anrufliste, Klick übernimmt die Nummer);
- **die Trefferliste** im Kontaktbereich darunter (`SearchBody`, aus allen
  Quellen — und die lokale Quelle liest denselben `ContactStore`, Doppelklick
  wählt).

Derselbe Kollege steht also zweimal auf dem Bildschirm, untereinander, mit zwei
verschiedenen Bedeutungen für dieselbe Geste.

**Auswirkung.** Beim ersten Mal fragt man sich, was der Unterschied ist. Beim
zweiten Mal klickt man auf den falschen und wundert sich, dass nichts passiert
(die obere Liste übernimmt nur). Es ist genau der Fehlschluss, den ADR-025 und
ADR-046 abwechselnd bekämpft haben — **er ist von zwei Feldern auf zwei Listen
gewandert**.

**Empfehlung.** Eine Liste, zwei Tiefen. Die Vorschlagsliste unter dem Feld ist
der richtige Ort für beides, und sie kann es schon fast:

1. Die lokalen Treffer stehen dort sofort — unverändert, das ist die Zusage aus
   ADR-025.
2. Die Netztreffer **wachsen in dieselbe Liste hinein**, sobald sie da sind, mit
   dem Herkunftsabzeichen, das die Trefferliste heute schon zeichnet
   (`SourceLabel`).
3. Der Kontaktbereich darunter bleibt, was er ist: Team und Outlook. Er wird
   **nicht** mehr ersetzt.
4. Die Zeile «wird gefragt …» und der Ring wandern mit in den Kopf der
   Vorschlagsliste.

**Dann fällt `SearchBody` ganz weg** — eine Liste, ein Kontextmenü, ein
Auswahlverhalten. Aufwand M, weil die Vorschlagsliste heute auf fünf Einträge
gedeckelt ist und eine Höhe bekommen muss; der Gegenwert ist, dass der ganze
Zweig `RefreshSearch` mit seinen vier Sichtbarkeitsregeln verschwindet.

**Vorbehalt:** ob die beiden Listen wirklich gleichzeitig stehen, ist am Code
abgelesen (`SuggestionPanel.Visibility` hängt an `HasSuggestions`,
`SearchBody.Visibility` an `IsSearchActive` — die beiden schliessen sich
nirgends aus). **Am Gerät gegenzuprüfen**, bevor umgebaut wird.

---

### C6 — Die Wähltastatur kostet ein Drittel der Liste · P1 · S

**Problem.** `AdvancedSettings.ShowDialpad` steht auf `true`. Bei der
Standardgrösse 400 × 660 gehen ab: Titelleiste 32, Seitenrand 16, Kontozeile
mit Zustandstext rund 56, Nummernfeld 40, **Wähltastatur rund 200**,
Umschaltleiste 52, Abstände. Der Kontaktliste bleiben rund 250 Pixel — und die
teilt sie sich noch mit zwei Abschnittsköpfen.

**Auswirkung.** Fünf sichtbare Kontaktzeilen statt zehn, auf dem Bildschirm,
der den ganzen Arbeitstag offensteht. Und das für ein Bedienelement, das auf
einem Arbeitsplatz-PC redundant ist: Ziffern tippt man auf der Tastatur, und
für Sprachmenüs gibt es die eigene Zehnertastatur in der Gesprächsansicht.

**Empfehlung.** `ShowDialpad = false` als Vorgabe. Der Umschalter bleibt, wo er
ist (im Feld rechts), der Zustand wird weiterhin gemerkt — wer die Tastatur
will, schaltet sie einmal ein und hat sie für immer. **Eine Zeile Änderung, und
die Liste verdoppelt sich.**

*Mit Touch-Geräten im Feld ist das eine andere Rechnung. Solange nipp auf
Arbeitsplätzen mit Maus und Tastatur läuft, trägt die Vorgabe «aus».*

---

### C7 — Kein Tastenkürzel im Gespräch · P1 · M · **braucht einen ADR**

**Problem.** Die Gesprächsansicht hat kein einziges `KeyboardAccelerator`.
Stumm, Halten, Auflegen und die Tastentöne sind ausschliesslich mit der Maus
erreichbar — und nur, solange das Fenster vorne steht. Der globale Hotkey
(`GlobalHotkeyService`) deckt Annehmen und Auflegen ab, mehr nicht.

**Auswirkung.** Stummschalten ist im Alltag der häufigste Griff. Heute heisst
er: Fenster suchen (es lebt im Infobereich), nach vorne holen, hinsehen,
klicken. Das sind drei Sekunden für etwas, das eine halbe dauern sollte — und
in der halben Sekunde steckt der Sinn.

**Empfehlung.** Zwei Ebenen, und die zweite ist die wichtige:

1. **Im Fenster** (klein, konventionell): `Strg+M` stumm, `Strg+H` halten,
   `Strg+E` auflegen. Als `KeyboardAccelerators` am Wurzelraster der
   Gesprächsansicht, wie Strg+1 bis 4 in der Hauptansicht.
2. **Systemweit** ein zweiter Hotkey nur fürs Stummschalten, einstellbar neben
   dem bestehenden unter «Start und Bedienung» — Vorschlag
   `Strg+Umschalt+M`, dasselbe Kürzel, das Teams benutzt. Der Dienst dafür ist
   gebaut; es kommt eine Registrierung und ein Zweig in `HandleHotkey` dazu.

**ADR nötig**, weil §22.5 genau einen systemweiten Hotkey vorsieht. Der Entscheid
ist klein, aber er weitet den Auftrag — und die Regel «was ein Tastendruck
bedeutet, entscheidet genau eine Stelle» muss dabei halten: der zweite Hotkey
bekommt seine eigene Bedeutung in `HeadsetPolicy`, er wird nicht in die
bestehende hineingerechnet.

---

### C8 — «Mailboxnummer eintragen» landet im falschen Formular · P1 · S

**Problem.** Der Knopf im leeren Mailbox-Bereich ruft
`OpenSettings(Sprungziel.Konto)`. Das klappt die Gruppe «SIP-Konten» auf und
setzt den Fokus auf **Benutzername**. Die Mailboxnummer steht aber

- in einem zugeklappten Unter-Aufklapper («Weitere Angaben zum Konto»),
- der zum **Anlegen**-Formular gehört, das leer dasteht,
- während die gesuchte Angabe zu einem **bestehenden** Konto gehört, das man
  erst über den Stift-Knopf in der Liste darüber zum Bearbeiten öffnen muss.

**Auswirkung.** Der Knopf war die Antwort auf B2 («kein Hinweistext führt
dorthin, wohin er verweist»). Er führt in die richtige *Gruppe* und lässt den
Benutzer dort weitersuchen — bei einem Formular, das so aussieht, als solle er
ein zweites Konto anlegen. Wer das tut, legt ein Konto mit denselben Angaben an
(das ersetzt zwar das bestehende, aber niemand weiss das im Moment des
Ausfüllens).

**Empfehlung.** Ein drittes Sprungziel, `Sprungziel.Mailbox`, das drei Dinge
tut, die alle schon gebaut sind:

1. `EditAccountCommand` auf das gewählte Konto ausführen — das Formular steht
   dann als «Konto bearbeiten» mit gefüllten Feldern da;
2. den Unter-Aufklapper «Weitere Angaben zum Konto» öffnen;
3. den Fokus in das Feld **Mailboxnummer** setzen.

**Und die Gegenprobe für das ganze Muster:** jeder `InfoBar.ActionButton` und
jeder Hinweisknopf gehört einmal durchgeklickt mit der Frage «steht der Cursor
danach in dem Feld, von dem der Satz sprach?». Betroffen sind ausser diesem
mindestens «Konto einrichten» (landet richtig) und «Nebenstelle anlegen»
(landet richtig) — das Muster stimmt also, dieser eine Fall bricht es.

---

### C9 — Der Rückweg hängt an einem Pfeil · P1 · S

**Problem.** Weder die Einstellungsseite noch die Gesprächsansicht reagieren auf
`Escape` oder `Alt+Links`. Beide haben oben links einen Pfeilknopf, und das ist
der einzige Weg. Die Titelleiste könnte den eingebauten Zurück-Knopf zeigen
(`TitleBar.IsBackButtonVisible`), steht aber auf `False`.

**Auswirkung.** Escape ist das, was jeder Windows-Benutzer drückt, um aus einer
Seite herauszukommen; Alt+Links ist die Standardnavigation. Beide tun nichts.
Wer mit der Tastatur arbeitet, muss sich zum Pfeil zurücktabben — und auf der
Einstellungsseite liegt er hinter der ganzen aufgeklappten Gruppe.

**Empfehlung.** Ein `KeyboardAccelerator` am Wurzelraster beider Seiten für
`Escape` und für `Alt+Left`, beide auf dieselbe Methode wie der Pfeilknopf. Vier
Zeilen XAML je Seite.

**Eine Abwägung dazu:** Escape darf ein laufendes Gespräch nicht beenden. Auf
der Gesprächsansicht heisst «zurück» ausdrücklich «zurück zur Wähltastatur, das
Gespräch läuft weiter» — genau das steht schon als Kurzinfo am Pfeil, und genau
das soll Escape tun.

---

### C10 — Drei Knöpfe tun stillschweigend nichts · P2 · S

**Problem.** Drei Stellen, dasselbe Muster:

1. **«Stumm schalten» im Infobereich-Menü.** `ToggleMuteAsync` sucht ein
   verbundenes Gespräch und kehrt zurück, wenn keines da ist. Der Kommentar
   nennt es ausdrücklich als Absicht («statt eine Fehlermeldung zu zeigen, die
   niemand gerufen hat»). Der Eintrag zeigt ausserdem nie, **ob** gerade stumm
   ist.
2. **«Entfernen» bei der letzten Gruppe.** `OnRemoveGroupClick` kehrt bei
   `Groups.Count <= 1` zurück, bevor der Dialog überhaupt aufgeht. Der Grund
   («ohne sie gäbe es keine Standardgruppe») ist richtig und steht nur im Code.
3. **Die Geräteliste im Infobereich-Menü** steht ohne ein Wort darüber: nach
   einem Trennstrich folgen «✓ Windows-Standard» und drei Gerätenamen. Wofür
   sie gelten — Wiedergabe, Mikrofon, Klingeln? — sagt nichts.

**Auswirkung.** Ein Klick ohne Wirkung und ohne Wort ist von einem Fehler nicht
zu unterscheiden. Dieses Projekt hat dieselbe Lücke bei den HID-Reports und beim
Infobereich-Symbol zweimal teuer bezahlt; hier trifft sie den Benutzer statt den
Entwickler.

**Empfehlung.**

1. Den Menüeintrag nach dem Zustand beschriften und abschalten, wenn nichts
   läuft: **«Stumm schalten»** / **«Stummschaltung aufheben»** /
   ausgegraut. `PopupMenuItem` kennt `Enabled`; bringt die Bibliothek es nicht
   mit, kommt der Eintrag ohne Gespräch gar nicht ins Menü — dieselbe Regel, die
   `PlaybackMenuItems` bei einem einzigen Gerät schon anwendet.
2. «Entfernen» ausgrauen, solange nur eine Gruppe da ist, und die Kurzinfo
   sagen lassen, warum: **«Die letzte Gruppe bleibt — sie nimmt die Einträge
   ohne eigene Gruppe auf.»**
3. Vor die Geräteliste einen ausgegrauten Titeleintrag **«Wiedergabe»** setzen.

---

### C11 — Das Kachelraster hat keine Suche · P2 · M

**Problem.** Im breiten Layout stehen die Nebenstellen rechts als Kacheln, nach
Gruppen gegliedert. Das Nummernfeld links durchsucht Kontakte und Quellen und
zeigt die Treffer **in der linken Spalte** — das Raster rechts bleibt
unverändert stehen.

**Auswirkung.** Bei acht Nebenstellen ist das richtig: man sieht sie alle, und
die Kachel ersetzt «anklicken, aufklappen, lesen» durch «hinsehen». Bei
vierzig (T215) ist Scrollen der einzige Weg zu einer bestimmten — und dann ist
das Raster schlechter als die Liste, die es ersetzt hat. Dazu kommt: wer links
einen Kollegen sucht und findet, sieht ihn gleichzeitig rechts in seiner Kachel
stehen. Zwei Antworten auf dieselbe Frage, nebeneinander.

**Empfehlung.** Das Kachelraster filtert mit, sobald links gesucht wird:

- Steht etwas im Nummernfeld, zeigt das Raster nur noch die Nebenstellen, auf
  die es passt — Gruppen ohne Treffer verschwinden.
- Über dem Raster steht dann eine Zeile: **«3 von 40 · Filter aufheben»**.
- Die Trefferliste links zeigt in diesem Fall **keine** Team-Kontakte mehr:
  sie stehen rechts, und zweimal dieselbe Zeile in einem Fenster ist genau die
  Doppelanzeige, die ADR-047 beim Kachelbereich vermieden hat.

Das ist dieselbe Regel wie `ShowTeamInLeftColumn`, nur eine Ebene tiefer, und
sie gehört an dieselbe Stelle: in `ShellViewModel`, nicht ins XAML.

---

### C12 — Zwei Speichermodelle im selben Programm · P2 · S

**Problem.** ADR-045 hat auf der Einstellungsseite den «Speichern»-Knopf
abgeschafft: jede Änderung wirkt sofort. Der Karten-Designer — erreichbar von
genau dieser Seite, zwei Klicks entfernt — hat «Speichern», «Verwerfen» und
einen Zustand dazwischen.

**Auswirkung.** Derselbe Benutzer lernt im selben Programm zwei Regeln, und die
zweite kostet ihn Arbeit (siehe C1). Wer gelernt hat, dass Änderungen sofort
wirken, schliesst den Designer, wenn er fertig ist.

**Empfehlung.** Der Designer **behält** sein Modell — ein Editor mit
Rückgängig, Vorschau und zwanzig Bausteinen ist keine Einstellungsseite, und
ADR-045 nennt genau diesen Unterschied nicht, weil es ihn damals nicht gab. Was
fehlt, ist, dass der Unterschied **sichtbar** wird:

1. C1 umsetzen (Rückfrage beim Schliessen, Punkt im Titel).
2. Die Fussleiste des Designers mit einer Zeile ergänzen, solange etwas offen
   ist: **«Noch nicht gespeichert.»** — in Beschriftungsgrösse, neben den
   Knöpfen.

Damit sagt das Fenster selbst, welche Regel gerade gilt, statt sie
vorauszusetzen.

---

### C13 — Die Integrationen stehen im Benutzerteil · P2 · S

**Problem.** ADR-046 hat die Einstellungen in zwei Ebenen geteilt: was der
Benutzer angeht, steht oben; Netzwerk, Codecs, Provisionierung, Diagnose und
Sichern stehen unter «Für Administratoren». **Die Gruppe «Integrationen» steht
oben** — mit Basisadresse, Zeitgrenze, `Authorization`-Schema, Zugangsschlüssel
und einem Textfeld für rohes JSON («Endpunkte und Felder»).

**Auswirkung.** Der Benutzerteil trägt damit das technischste Werkzeug des ganzen
Programms. Wer die Einstellungen nach «Klingelton» durchsieht, kommt an
«Schema der Authorization-Kopfzeile» vorbei — das ist genau die Gleichrangigkeit,
die B7 aufgelöst hat, nur an einer Stelle, die dabei übersehen wurde.

**Empfehlung.** Die Gruppe zerlegen, nicht verschieben:

- **Oben bleibt**, was den Benutzer angeht: welche Quellen es gibt, ihr
  Ein-/Aus-Schalter, ihr Zustand, und **«Anruferkarte»** (siehe C14).
- **Nach unten wandert** alles, was eine Quelle einrichtet: «Quelle
  hinzufügen», «API-Anbieter importieren», «Verbindung und Anmeldung»,
  «Verbindung testen», «Endpunkte und Felder (JSON)», «Wann nachgeschlagen
  wird», Ausgeben/Einlesen.

Die Trennung liegt schon im Text: der Satz über der Gruppe («Externe Systeme
für Anruferkontext und Kontaktsuche. Ohne eingerichtete Quelle ändert sich
nichts an nipp») ist eine Benutzererklärung — alles darunter ist Administration.

---

### C14 — Der Weg zur Anruferkarte ist vier Ebenen tief · P2 · S

**Problem.** Die Anruferkarte ist das, was der Benutzer bei **jedem** Anruf
sieht. Der Weg dorthin: Einstellungen → Integrationen aufklappen → «Anruferkarte»
aufklappen → «Bearbeiten …» → eigenes Fenster.

**Auswirkung.** Vier Ebenen für die Ansicht mit der höchsten Sichtbarkeit im
Programm — und drei davon heissen nach dem technischen Unterbau, nicht nach dem,
was man ändern will.

**Empfehlung.** «Anruferkarte» wird eine **eigene Gruppe erster Ebene**, direkt
unter «Darstellung» — dorthin gehört sie inhaltlich. Sie zeigt weiterhin die
drei Kartenarten mit ihrer Zusammenfassung und den Knopf «Bearbeiten …». Die
Gruppe «Integrationen» behält, was sie einrichtet (C13).

Zusätzlich, und das ist die eigentliche Abkürzung: **ein Kontextmenü-Eintrag auf
der Karte selbst.** Wer im Gespräch auf die Anruferkarte rechtsklickt, bekommt
«Karte bearbeiten …». Das ist der Moment, in dem einem auffällt, dass eine Zeile
fehlt — und heute muss man sich das bis zum Ende des Gesprächs merken.

---

### C15 — Kein «Nicht stören» · P2 · M · **braucht einen ADR**

**Problem.** nipp läuft den ganzen Tag im Infobereich, klingelt auf dem
gewählten Klingelgerät, zeigt einen Toast im Szenario `IncomingCall` (der stehen
bleibt, bis jemand reagiert) und holt heute zusätzlich das Fenster nach vorn
(C2). Es gibt keinen Weg, das für eine Stunde auszuschalten, ausser nipp zu
beenden — und dann klingelt es gar nicht mehr, auch nicht leise.

**Auswirkung.** In einer Besprechung, bei einer Präsentation oder in einem
Teams-Meeting bleibt nur: beenden und daran denken, es wieder zu starten. Wer
es vergisst, verpasst die Anrufe ohne Eintrag in der Anrufliste.

**Empfehlung.** Klein anfangen, im Infobereich-Menü:

- **«Stumm für 1 Stunde»** / **«Stumm bis morgen»** / **«Stummschaltung
  aufheben»**, mit der verbleibenden Zeit im Eintrag.
- Wirkung: kein Klingelton, kein Fenster nach vorn. **Der Toast bleibt** —
  er ist stumm (`MuteAudio()`) und stört niemanden, und so steht ein verpasster
  Anruf trotzdem sofort sichtbar da.
- Das Symbol im Infobereich zeigt den Zustand, wie es heute schon zwischen
  angemeldet und nicht angemeldet unterscheidet.

**ADR nötig** — das steht in keinem Paragrafen von NIPP-BUILD.md. Es ist auch
der einzige Befund dieses Reviews, der eine neue Fähigkeit vorschlägt statt
etwas zurechtzurücken; wenn der Auftrag ihn nicht will, fällt er weg, und C2
allein löst schon die Hälfte des Problems.

---

### C16 — Die Mailbox belegt ein Viertel der Hauptnavigation · P2 · M

**Problem.** Die Umschaltleiste hat vier Flächen: Kontakte, Anrufe, Mailbox,
Einstellungen. Der Bereich «Mailbox» zeigt im Regelfall ein Symbol, einen Satz
(«Keine neuen Nachrichten.») und einen Knopf.

**Auswirkung.** Ein Viertel der Hauptnavigation für eine Zahl, die meistens
Null ist. Zum Vergleich: «Kontakte» und «Anrufe» sind die beiden Bereiche, in
denen der Benutzer den ganzen Tag steht, und beide sind auf 400 Pixeln zu eng
(siehe C6).

**Empfehlung.** Zwei Fassungen, je nach Geschmack des Auftraggebers:

**Fassung A (klein).** Die Mailbox bleibt ein Bereich, wird aber erst
angeboten, wenn dem gewählten Konto eine Mailboxnummer hinterlegt ist. Ohne sie
verschwindet die Fläche, und die drei übrigen werden breiter — «Einstellungen»
passt dann auch unter 400 Pixeln ohne Kürzung.

**Fassung B (richtig).** Die Mailbox wird **keine Fläche mehr**, sondern eine
Zeile ganz oben in der Anrufliste: **«2 neue Nachrichten auf der Mailbox ·
Anhören»**, mit demselben Abzeichen wie heute. Sie steht dort, wo man ohnehin
nach verpassten Anrufen sucht, und die Leiste hat drei Flächen. Der Bereich
«Mailbox» und `VoicemailPanel` fallen weg.

**ADR nötig**, weil §20.1 die vier Flächen nennt.

---

### C17 — Die Kachelspalte wächst ohne Obergrenze · P3 · S

**Problem.** Im breiten Layout ist die linke Spalte auf 480 gedeckelt, die
rechte nimmt den Rest (`Width="*"`). Auf einem maximierten Fenster von 1920
Pixeln sind das rund 1400 — sieben Kacheln nebeneinander. Bei acht Nebenstellen
steht dann eine Reihe voll und darunter Leere über zwei Drittel der
Fensterbreite.

**Empfehlung.** `TilesColumn` bekommt eine `MaxWidth` — gerechnet aus
Kachelbreite mal fünf plus Rand, also rund 1060 —, und das Raster bleibt links
in seiner Spalte stehen. Wer breiter zieht, bekommt Rand statt Streuung.
Dieselbe Begründung wie ADR-046 für die Zeile, nur für das Raster.

> **Umgesetzt und einen Tag später zurückgenommen (ADR-052).** Der Befund war
> richtig beobachtet und falsch zugeordnet: dass nebeneinander nicht sieben,
> sondern nur vier Kacheln standen, lag nicht an zu viel Breite. **Zwei
> Sternspalten, von denen eine an ihrer Höchstbreite abgeschnitten wird,
> verschenken den Rest** — auf 1920 Pixeln waren das 468. Die Spalte ist jetzt
> wieder ungedeckelt, die linke dafür fest.

---

### C18 — Die Anrufliste zeigt die Nummer nicht, die sie zurückruft · P3 · S

**Problem.** Eine Zeile zeigt `DisplayLabel` (Name, wenn aufgelöst), darunter
«Ergebnis · Dauer» und rechts die Zeit. Die Rufnummer steht nirgends — sie ist
nur über «Nummer kopieren» im Kontextmenü zu erfahren.

**Auswirkung.** Ein Kollege hat Festnetz und Mobil. In der Anrufliste steht
zweimal sein Name, und welcher Eintrag welche Nummer war, ist nicht zu sehen.
Ein Doppelklick ruft zurück — man weiss vorher nicht, wohin.

**Empfehlung.** Die Nummer in die zweite Zeile, hinter Ergebnis und Dauer:
**«Verpasst · 0:00 · +41 79 123 45 67»**. `HistoryDetailConverter` baut die Zeile
ohnehin zusammen. Bei einem Eintrag ohne aufgelösten Namen steht die Nummer schon
oben — dann entfällt sie hinten, wie die Attributionszeile des Toasts es
handhabt.

---

### C19 — Die Nummernknöpfe sagen nicht, was sie tun · P3 · S

**Problem.** Im aufgeklappten Detailbereich einer Kontaktzeile trägt jeder
Nummernknopf `AutomationProperties.Name="{x:Bind Display}"` — also nur die
Nummer. Eine Sprachausgabe liest «plus vier eins sieben neun …» und sagt nicht,
dass ein Druck darauf anruft. Im Menü von `CallOrAsk` ist es richtig gelöst
(«Mobil anrufen, +41 79 …»).

**Empfehlung.** Denselben Wortlaut auch hier: `$"{KindLabel} anrufen,
{Display}"`. Der Wert steht in `ContactNumberChoice` bereit; es ist eine Zeile
in der gemeinsamen Vorlage `NippContactNumberTemplate` und wirkt damit an allen
vier Orten gleichzeitig.

---

### C20 — Der Sortiermodus erklärt sich nicht · P3 · S

**Problem.** Der Umschalter am Abschnittskopf trägt das Symbol E8CB und die
Kurzinfo «Reihenfolge ändern». Solange er an ist, ändert sich an der Liste
sichtbar nichts ausser der Ziehbarkeit — kein Griff an der Zeile, keine
Erklärung, und «doppelt anklicken ruft an» gilt weiter.

**Auswirkung.** Wer ihn versehentlich einschaltet, merkt es erst, wenn eine
Zeile verrutscht. Wer ihn absichtlich einschaltet, weiss nicht, ob er
funktioniert hat.

**Empfehlung.** Solange der Modus an ist, steht über der Liste eine Zeile:
**«Zeilen ziehen, um sie umzusortieren. Fertig»** — «Fertig» als Verweis, der
den Modus wieder ausschaltet. Das ist derselbe Bau wie der Filterkopf der
Anrufliste und kostet nichts.

---

## 5 · Abläufe, heute und im Vorschlag

### Einen Kunden aus dem CRM anrufen

**Heute** — Nummernfeld → Namen tippen → zwei Listen erscheinen, eine unter dem
Feld, eine im Bereich darunter → raten, welche → Enter drücken → **Anruf an
«Meier» schlägt fehl** → Fehlermeldung lesen → zurück in die untere Liste →
doppelt klicken.

**Vorschlag** — Nummernfeld → Namen tippen → **eine** Liste, lokale Treffer
sofort, Netztreffer wachsen hinein → Pfeil-runter, Enter.

*Entfallen: die zweite Liste, das Raten, der fehlgeschlagene Anruf. (C4, C5)*

---

### Ein Gespräch stummschalten

**Heute** — Fenster suchen (es steht im Infobereich) → anklicken →
Gesprächsansicht → «Stumm» treffen, zwei Spalten neben «Aufnahme».

**Vorschlag** — `Strg+Umschalt+M`, ohne hinzusehen.

*Entfallen: drei Handgriffe und die Gefahr, den Nachbarknopf zu treffen.
(C3, C7)*

---

### Die Mailboxnummer nachtragen

**Heute** — Mailbox-Bereich → «Mailboxnummer eintragen» → Einstellungen,
Kontogruppe offen, Fokus auf «Benutzername» → merken, dass das das
Anlegen-Formular ist → hochscrollen → Stift am Konto → Formular füllt sich →
«Weitere Angaben zum Konto» aufklappen → Mailboxnummer eintragen → Feld
verlassen.

**Vorschlag** — Mailbox-Bereich → «Mailboxnummer eintragen» → Cursor steht im
Feld → tippen → Feld verlassen.

*Entfallen: vier Handgriffe und ein Formular, das etwas anderes tut, als es
soll. (C8)*

---

### Eine Zeile auf der Anruferkarte ändern

**Heute** — Gespräch beenden (der Weg führt durch die Einstellungen) →
Einstellungen → Integrationen → Anruferkarte → Bearbeiten … → ändern →
**Speichern nicht vergessen**, sonst ist alles weg.

**Vorschlag** — auf der Karte rechtsklicken → «Karte bearbeiten …» → ändern →
Fenster schliessen → «Speichern?» → fertig.

*Entfallen: drei Ebenen und der stille Datenverlust. (C1, C14)*

---

### Ein eingehender Anruf, während man arbeitet

**Heute** — es klingelt → **das nipp-Fenster springt nach vorn**, Fokus auf
«Annehmen» → was man gerade tippte, landet in nipp, und die nächste Leertaste
nimmt den Anruf an.

**Vorschlag** — es klingelt → der Toast steht da, das Fenster bleibt, wo es war
→ «Annehmen» im Toast **oder** nipp anklicken.

*Entfallen: der Fokusraub und das versehentliche Annehmen. (C2)*

---

## 6 · Impact/Effort-Matrix

**Quick Wins** — hoher Nutzen, kleiner Aufwand

| | |
|---|---|
| **C1** | Rückfrage beim Schliessen des Designers, Punkt im Titel |
| **C2** | Kein `Activate()` beim Klingeln |
| **C6** | `ShowDialpad = false` als Vorgabe — eine Zeile, und die Liste verdoppelt sich |
| **C8** | Drittes Sprungziel für die Mailboxnummer |
| **C9** | Escape und Alt+Links auf beiden Seiten |
| **C4** | Enter entscheidet nach dem Feldinhalt |
| **C10** | Drei stille Knöpfe beschriften, ausgrauen oder weglassen |
| **C19** | Ein Wortlaut in einer gemeinsamen Vorlage |
| **C3** | Rückfrage beim ersten Aufnehmen je Gespräch |

**Strategic Improvements** — hoher Nutzen, grösserer Aufwand

| | |
|---|---|
| **C5** | Eine Trefferliste statt zwei — löst `SearchBody` ganz auf |
| **C7** | Tastenkürzel im Gespräch und systemweites Stummschalten (ADR) |
| **C11** | Das Kachelraster filtert mit |
| **C13**/**C14** | Integrationen aufteilen, Anruferkarte nach oben |

**Minor Improvements** — kleiner Nutzen, kleiner Aufwand

| | |
|---|---|
| **C12** | «Noch nicht gespeichert» in der Designer-Fussleiste |
| **C17** | `MaxWidth` an der Kachelspalte |
| **C18** | Die Nummer in die zweite Zeile der Anrufliste |
| **C20** | Eine Zeile über der Liste im Sortiermodus |

**Avoid / Low Priority** — grosser Aufwand, wenig Gegenwert

| | |
|---|---|
| **C16 Fassung B** | Die Mailbox aus der Navigation nehmen. Es ist die bessere Struktur, aber sie berührt §20.1, `ShellSection`, die Testmatrix und vier Tests — und der Gewinn ist eine Fläche in der Leiste. **Fassung A** (verstecken ohne Mailboxnummer) holt neun Zehntel davon für ein Zehntel des Aufwands |
| **C15** | «Nicht stören» ist richtig und gewollt, aber es ist eine neue Fähigkeit ohne Auftrag. Wer C2 umsetzt, hat die Hälfte des Schmerzes weg und kann in Ruhe entscheiden |
| — | **B20 bleibt zurückgestellt.** Eine geführte Feldzuordnung statt des JSON-Felds lohnt weiterhin erst, wenn jemand eine Quelle ohne Vorlage anbinden soll |

---

## 7 · Was entfernt werden sollte

- **`SearchBody` samt Trefferliste, Statuszeile und Ring** — geht in der
  Vorschlagsliste auf (C5). Mit ihm fallen vier Sichtbarkeitsregeln in
  `RefreshSearch` weg.
- **Die Wähltastatur aus dem Blickfeld** — nicht löschen, aber standardmässig
  zu (C6). Sie bleibt einen Klick entfernt.
- **`Activate()` beim Klingeln** — ersatzlos (C2).
- **Der Menüeintrag «Stumm schalten» ohne Gespräch** — er soll gar nicht erst
  im Menü stehen (C10).
- **Eine Ebene im Weg zur Anruferkarte** (C14).
- **Die Mailbox-Fläche ohne hinterlegte Mailboxnummer** (C16, Fassung A).

---

## 8 · Empfohlene Zielstruktur

**Hauptansicht, schmal** — unverändert in ihrer Ordnung, nur dichter:

```
Konto + Zustand
Nummernfeld  [Verlauf] [Tastatur]  (Anrufen)
  └─ eine Vorschlagsliste: lokal sofort, Netztreffer wachsen hinein
[Wähltastatur — standardmässig zu]
Inhalt: Kontakte (Team · Outlook) | Anrufe | (Mailbox, nur mit Nummer)
Meldungen
Umschaltleiste: Kontakte · Anrufe · [Mailbox] · Einstellungen
```

**Hauptansicht, breit** — wie heute, mit einem Zusatz:

```
┌── links, 480 fest ─────────┬── rechts, der ganze Rest ───────┐
│ Konto, Nummernfeld,        │ Nebenstellen als Kacheln         │
│ Tastatur, gewählter Bereich│ ↳ filtert mit, sobald links      │
│                            │   gesucht wird                   │
└────────────────────────────┴──────────────────────────────────┘
```

**Einstellungen** — dieselben zwei Ebenen, drei Gruppen verschoben:

```
SIP-Konten
Audio
Darstellung
Anruferkarte              ← neu auf erster Ebene (aus Integrationen)
Kontakte
Quellen                   ← nur: welche, an/aus, Zustand
Start und Bedienung
Aktualisierung
Für Administratoren
  ├─ Netzwerk und Verschlüsselung
  ├─ Codecs
  ├─ Quellen einrichten   ← neu (aus Integrationen)
  ├─ Provisionierung und Diagnose
  └─ Sichern und zurücksetzen
Über
```

---

## 9 · Priorisierte Roadmap

### Phase 1 — die scharfen Kanten (ein Tag)

Alles, was heute Arbeit oder Gespräche kostet.

| | Befund |
|---|---|
| 1 | **C1** Rückfrage beim Schliessen des Designers + Punkt im Titel |
| 2 | **C2** kein `Activate()` beim Klingeln |
| 3 | **C3** Aufnahme mit Rückfrage, aus der Dreierreihe heraus |
| 4 | **C4** Enter entscheidet nach dem Feldinhalt |
| 5 | **C10** die drei stillen Knöpfe |

*Ergebnis: kein Weg mehr, Arbeit oder ein Gespräch durch einen Fehlgriff zu
verlieren.*

### Phase 2 — der Alltag (ein bis zwei Tage)

Was jeden Tag Handgriffe kostet.

| | Befund |
|---|---|
| 6 | **C6** Wähltastatur standardmässig zu |
| 7 | **C8** drittes Sprungziel für die Mailboxnummer |
| 8 | **C9** Escape und Alt+Links |
| 9 | **C7** Tastenkürzel im Gespräch + systemweites Stummschalten (**ADR**) |
| 10 | **C18**, **C19**, **C20** die drei Kleinen |

### Phase 3 — die Struktur (zwei bis drei Tage)

| | Befund |
|---|---|
| 11 | **C5** eine Trefferliste statt zwei |
| 12 | **C11** das Kachelraster filtert mit |
| 13 | **C13**/**C14** Integrationen aufteilen, Anruferkarte nach oben |
| 14 | **C12**, **C17** Designer-Fussleiste, Kachelspalte deckeln |
| 15 | **C16 Fassung A** Mailbox-Fläche nur mit Nummer |

### Offen zum Entscheid

**C15** («Nicht stören») und **C16 Fassung B** (Mailbox aus der Navigation)
weiten den Auftrag. Beide sind gut begründet, beide brauchen einen ADR, und
beide sind nach Phase 1 weniger dringend, als sie heute aussehen.

---

## 10 · Drei Gegenproben am Gerät

Dieses Review ist am Code entstanden. Drei Befunde sollten vor dem Umbau einmal
im laufenden Programm bestätigt werden:

| | Was zu sehen sein muss |
|---|---|
| **C5** | Einen Namen ins Nummernfeld tippen, der in Team **und** im CRM steht: steht er wirklich zweimal auf dem Bildschirm, oben als Vorschlag und unten als Treffer? |
| **C2** | In Word tippen, sich anrufen lassen: springt das Fenster nach vorn, und nimmt die nächste Leertaste den Anruf an? |
| **C6** | Bei 400 × 660 mit eingeschalteter Wähltastatur zählen, wie viele Kontaktzeilen sichtbar sind — und die Gegenprobe ohne sie |

---

## Was dieses Review nicht gefunden hat

Der Vollständigkeit halber, weil eine Liste von zwanzig Problemen sonst ein
falsches Bild gibt:

- **Die Fehlertexte sind weiterhin überdurchschnittlich.** Sie nennen Ursache
  und Abhilfe, sie nennen keinen Methodennamen und keinen nackten HTTP-Code, und
  sie sagen, was zu tun ist. Das ist selten.
- **Die Dialoge sind einheitlich.** Neun `ContentDialog`, alle mit
  `DefaultButton = Close` bei destruktiver Wirkung, alle mit einem Satz, der
  sagt, was verloren geht und was bleibt.
- **Die Leerzustände unterscheiden.** «Noch keine Anrufe» gegen «Keine
  passenden Anrufe» gegen «Kontakte werden eingelesen …» — mit Ring statt
  Symbol beim Laden. Das macht kaum jemand.
- **Die Begriffe stimmen jetzt überall.** `CallStateCatalog` und
  `AccountStateCatalog` halten das fest, und ein Test hält die Kataloge.
- **Die Virtualisierung ist an jeder Stelle mitgedacht** — bis hin zum
  `ItemsWrapGrid` im `GroupStyle.Panel`, das die meisten übersehen.
- **Barrierefreiheit ist kein Anhang.** Kontextmenüs per Tastatur, Zahlen in
  den Namen der Umschaltleiste, Farbe nie als einzige Aussage, Klickziele auf
  32 Pixel, Kontraste in beiden Erscheinungsbildern geprüft.

**Die Reibung sitzt nicht mehr in den Einstellungen.** Sie sitzt jetzt an drei
Rändern: dem Fenster, das sich selbst nach vorne holt; dem Feld, das zwei Dinge
gleichzeitig ist; und dem einen Fenster, das der letzte Review nicht angesehen
hat.
