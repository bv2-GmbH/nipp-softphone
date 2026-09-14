# Drei Meldungen aus dem Alltag — 13.09.2026

**Anlass:** drei Wünsche aus der Benutzung, gemeldet am 13.09.2026 nach dem
Abschluss von Welle 2.

**Stand vor diesem Plan:** Zweig `review-umsetzung`, HEAD `68f6013`, Build ohne
Warnungen, 1190 Komponententests und 34 Architekturtests grün, CI auf echter
x64-Hardware bestätigt.

**Vorgehen:** erst der Befund mit Datei und Zeile, dann die Entscheidung, dann
der Weg. Wo eine Entscheidung schon gefallen ist, steht sie dabei; wo eine
offen ist, steht sie am Schluss und **nicht** stillschweigend als Annahme im
Text.

---

## Umsetzungsstand (13.09.2026)

**Alle drei sind gebaut, je als eigener Commit, gepusht.** Build ohne
Warnungen, **1198 Komponententests** und **34 Architekturtests** grün.

| | Was | Stand |
|---|---|---|
| **B** | Vorschau im Karten-Designer | **umgesetzt** — ADR-061. Die Ursache lag nicht im Zeichnen, sondern in der gekürzten Anzeigefassung; **derselbe Fehler stand ein zweites Mal auf der Einstellungsseite** |
| **C** | Mailbox-Reiter | **umgesetzt** — ADR-062. Vollständig, bis in die SDK-Schicht und die Provisionierung. Abweichung von der Spezifikation an acht Stellen vermerkt |
| **A** | Gliederungsebene bei den Nebenstellen | **umgesetzt** — ADR-063. Der äussere Aufklapper ist weg; `ShowTeamContacts` ebenfalls, wie entschieden |

**Drei neue Zeilen für den Gerätetag: T287, T288, T289.**

**Was dabei zusätzlich gefunden wurde:**

- **B:** Die Einstellungsseite reichte dieselbe gekürzte Antwort weiter wie
  der Designer — der Fehler stand an **zwei** Stellen. Der Name `RawResponse`
  war die halbe Ursache: er versprach «roh» und war es nicht. Jetzt heissen
  die Felder `ResponsePreview` und `ResponseBody`.
- **B:** Unterwegs sah es so aus, als müsste der Schnappschuss ausgeschaltete
  Quellen überspringen. **Drei Tests haben das zurückgeholt**, und sie hatten
  recht: eine Vorlage wird nie eingeschaltet ausgeliefert (ADR-040),
  eingeschaltet wird *nach* dem Testabruf.
- **C:** `RedirectAsync` hatte als einzigen Aufrufer den Toast-Knopf und ist
  mitgegangen — eine Fähigkeit ohne Aufrufer ist das Muster, das dieses
  Projekt siebenmal bezahlt hat.
- **A:** Bei genau **einer** Gruppe wurde gar kein Gruppenkopf gezeichnet.
  Ohne Korrektur hätte ein solcher Arbeitsplatz danach nichts mehr zuklappen
  können.

**Offen geblieben, und ehrlich als solches vermerkt:** über dem
Sortier-Umschalter bleibt ein Streifen Luft. Woher er kommt, ist **nicht
gemessen**; er stand vorher unter der Expander-Kopfzeile genauso da.

---

## Kurzfassung

| | Was | Aufwand | Kern der Sache |
|---|---|---|---|
| **A** | Eine Gliederungsebene weniger bei den Nebenstellen | **S** | Der äussere Aufklapper «Nebenstellen (10)» fällt weg. **Die Gruppen darunter sind bereits klappbar und merken sich ihren Zustand** — es ist eine Ebene zu entfernen, keine zu bauen |
| **B** | Die Vorschau im Karten-Designer zeigt echte Daten | **M** | Der Fehler ist gefunden: die Antwort wird **für die Anzeige gekürzt** und ist danach kein gültiges JSON mehr. Der Leser wirft, **der Fänger kehrt still zurück** — und die Erfolgsmeldung erscheint trotzdem |
| **C** | Der Mailbox-Reiter fällt weg | **M** | Drei Reiter statt vier. Der Reiter ist heute der **einzige** Weg zu «Mailbox anrufen» und die **einzige** Anzeige für wartende Nachrichten; beides fällt mit ihm |

**Reihenfolge:** B, dann C, dann A. B ist ein Fehler und geht vor; C berührt
`ShellPage.xaml` grossflächig; A fasst dieselbe Datei an und käme sonst in
Konflikt.

---

## A — Eine Gliederungsebene weniger bei den Nebenstellen

### Der Befund

Die Liste hat heute **zwei** Klappebenen übereinander:

| Ebene | Was | Wo |
|---|---|---|
| aussen | Ein `Expander` mit der Kopfzeile «Nebenstellen (10)» | `ShellPage.xaml:1177-1195` (`TeamGroup`), Text aus `ShellViewModel.TeamHeader` (`ShellViewModel.cs:223`) |
| innen | Je Gruppe («Team», «Dienste») ein Kopf zum Auf- und Zuklappen | `ShellPage.xaml:1288-1305`, Zustand in `ContactGroupRow.IsExpanded` (`ContactGroupRow.cs:139`) |

**Die innere Ebene kann bereits alles, was gewünscht ist.** Sie klappt auf und
zu (`ShellPage.xaml.cs:1217-1233`), und der Zustand überlebt den Neustart:
`ShellViewModel.SaveGroupExpansion` (`ShellViewModel.cs:858-876`) schreibt die
zugeklappten Namen nach `Advanced.CollapsedTeamGroups` (`NippSettings.cs:528`)
— über `SaveViewState`, löst also keine Neuanmeldung aus.

**Und die Vorgabe für Nebenstellen ohne Gruppe steht auch schon so, wie
gewünscht:** `TeamGroups.NameOf` gibt die erste Gruppe zurück, wenn eine
Nebenstelle keine eigene hat oder auf eine verweist, die es nicht mehr gibt
(`TeamGroups.cs:46-65`); die erste Gruppe heisst ab Werk «Team»
(`TeamGroups.cs:29`, `NippSettings.cs:149`). **Hier ist nichts zu bauen.**

Zwei Stellen stehen der Änderung im Weg:

1. **`var koepfe = namen.Count > 1;`** (`ShellViewModel.cs:757`). Bei genau
   **einer** Gruppe wird heute kein Gruppenkopf gezeichnet — der äussere
   Aufklapper trug die Überschrift ja schon. Fällt er weg, hätte ein
   Arbeitsplatz mit nur einer Gruppe **gar keinen** Kopf mehr und könnte nicht
   mehr zuklappen.
2. **Das breite Layout hat den Expander gar nicht.** Dort steht die Kopfzeile
   als schlichter Text (`TilesHeaderText`, `ShellPage.xaml:1637-1640`,
   gesetzt in `ShellPage.xaml.cs:923`), und die Kacheln tragen dieselben
   Gruppenköpfe (`ShellPage.xaml:1745-1770`, **derselbe Handler**). Beide
   Ansichten müssen nach der Änderung gleich aussehen.

### Die Entscheidung

- **Der äussere Aufklapper fällt weg.** «Team» und «Dienste» stehen direkt da,
  jede für sich auf- und zuklappbar, Zustand gespeichert.
- **Ein Gruppenkopf wird immer gezeichnet**, auch bei einer einzigen Gruppe.
- **Ungruppierte Nebenstellen stehen in «Team»** — das ist der Ist-Zustand und
  bleibt unverändert.

### Der Weg

1. `ShellViewModel.cs:757` — `koepfe` entfällt; `ContactGroupRow` bekommt den
   Kopf immer. Damit wird der vierte Parameter von `ContactGroupRow`
   (`showHeader`) überflüssig; er wird entfernt statt auf `true` verdrahtet —
   ein Parameter, der nur noch einen Wert annimmt, ist eine Frage, die niemand
   mehr stellt.
2. `ShellPage.xaml:1177-1195` — der `Expander` `TeamGroup` weicht einem
   schlichten Rahmen. **Der Zählerkopf «Nebenstellen (10)» verschwindet
   ersatzlos**; die Anzahl steht danach je Gruppe im Gruppenkopf
   (`ContactGroupRow.Header`, `ContactGroupRow.cs:143`).
3. `ShellPage.xaml.cs:799` und `:923` — die beiden Zuweisungen von
   `ViewModel.TeamHeader` entfallen, ebenso `TeamHeaderText` und
   `TilesHeaderText` im XAML (`:1192-1195`, `:1637-1640`).
4. `ShellViewModel.cs:223` — `TeamHeader` wird gelöscht, mitsamt der Meldung
   in `:664`. **Die Begründung in ADR-044**, warum der Abschnitt
   «Nebenstellen» heisst und nicht «Team», wird im ADR als überholt vermerkt.
5. `IsTeamExpanded` (`ShellViewModel.cs:419-426`) und
   `Advanced.ShowTeamContacts`: **bleiben**, denn im schmalen Layout entscheidet
   diese Eigenschaft weiterhin, ob der ganze Abschnitt Platz bekommt — er
   konkurriert dort mit der Wähltastatur und der Anrufliste. Die Bindung
   wandert vom `Expander` an die Sichtbarkeit des Rahmens. **Alternative, falls
   das nicht gewollt ist: siehe offene Frage 1.**
6. Sortiermodus prüfen: `ShouldBeExpanded` hält im Sortiermodus alle Gruppen
   offen (`ShellViewModel.cs:827-830`). Das bleibt und wird nach dem Umbau
   einmal am gebauten Fenster nachgesehen — eine zugeklappte Gruppe beim
   Ziehen verlöre ihre Einträge aus der gespeicherten Reihenfolge
   (`TeamLayout.From` zählt zugeklappte Gruppen über `All` mit,
   `TeamLayout.cs:60`).

### Risiko

**Gering, aber flächig in einer Datei, die zwei Layouts bedient.** Der Fehler
wäre kein Absturz, sondern ein Unterschied zwischen schmal und breit — genau
die Ungleichheit, die ADR-048 einmal beseitigt hat. Deshalb beide Ansichten am
gebauten Fenster nebeneinander ansehen.

### Definition of Done

- Im schmalen **und** im breiten Layout stehen «Team» und «Dienste» als
  einzige Überschriften; «Nebenstellen (n)» kommt nirgends mehr vor.
- Eine Gruppe zuklappen, nipp neu starten: sie ist immer noch zu.
- Mit **einer** Gruppe: der Kopf ist da und klappt.
- `ContactGroupRowTests` und `TeamGroupsTests` grün; die Tests, die
  `showHeader` kennen, sind mitgezogen.
- Am Gerät: **T287** (unten).

---

## B — Die Vorschau im Karten-Designer zeigt echte Daten

### Der Befund

**Die Ursache ist gefunden, und sie liegt nicht dort, wo man sie sucht.** Der
Renderpfad ist in Ordnung: `CardLayoutEngine.Build` erzeugt bei jedem Lauf neue
Abschnittsobjekte (`CardLayoutEngine.cs:144-161`), `CardModel` ist ein `record`
mit Referenzvergleich auf den Abschnitten, die `DependencyProperty` in
`CardView` feuert also, und `CardView.Rebuild()` läuft
(`CardView.cs:35-69`). **Es wird neu gezeichnet — nur mit den alten Daten.**

Die Kette bricht drei Schritte vorher:

1. `IntegrationTester` gibt die Antwort als **Anzeigefassung** zurück:
   `RawResponse: Preview(result.Body)` (`IntegrationTester.cs:184`).
   `Preview` schneidet bei 8192 Zeichen hart ab und hängt `… (gekürzt)` an
   (`:193-205`). Der Kommentar dort sagt es selbst: «Sie ist zum Ansehen da,
   **nicht zum Weiterverarbeiten**» (`:18-22`).
2. `CardDesignerViewModel` reicht genau diesen Text weiter:
   `_samples.SetFromLiveCall(quelle.Id, rohdaten)` (`:971-975`).
3. `TestSampleStore.SetFromLiveCall` ruft `JsonNode.Parse(rawResponse)`
   (`TestSampleStore.cs:65`). Bei abgeschnittenem JSON wirft das — **und der
   Fänger kehrt still zurück** (`:71-76`). Weder `_samples` noch
   `_fromLiveCall` werden gesetzt.

**Folge:** Die Vorschau zeigt weiter die erfundene Beispielantwort, und die
Zeile darunter sagt weiter «Vorschau mit erfundenen Beispieldaten»
(`CardDesignerViewModel.cs:1082-1083`).

**Und es sieht aus, als hätte es geklappt.** `gelungen++` steht **nach**
`SetFromLiveCall`, ohne dessen Erfolg zu prüfen
(`CardDesignerViewModel.cs:974`). Die Statuszeile meldet «1 von 1 Quellen haben
geantwortet» (`:995-998`), und weil `gelungen > 0` gilt, läuft `Refresh()`
(`:1004`) und zeichnet dieselben Beispieldaten noch einmal. **Eine Meldung, die
einen Erfolg behauptet, den sie nicht geprüft hat** — dasselbe Muster wie die
Protokollzeile über Early Media, zum dritten Mal.

Drei weitere Stellen, an denen still nichts ankommt:

- `IsSuccess` schliesst **404** ein (`IntegrationTester.cs:37`). Ein 404 gilt
  als Erfolg; sein Rumpf ersetzt die Probe oder wird verworfen, und `gelungen`
  zählt trotzdem.
- `TestSampleStore.BuildSnapshot` läuft über **alle** Quellen ohne
  `Enabled`-Prüfung (`:142-148`), während der Abruf nur eingeschaltete fragt
  (`CardDesignerViewModel.cs:935-937`). Eine ausgeschaltete Quelle zeigt
  deshalb dauerhaft Beispieldaten — und `CanLookup` (`:250-251`) prüft
  `Enabled` ebenfalls nicht, der Knopf ist also aktiv und der Lauf endet auf
  `:939-944`.
- Ein Mapping, das sich nicht übersetzen lässt, fällt kommentarlos aus dem
  Schnappschuss (`TestSampleStore.cs:167-170`).

**Warum kein Test das bemerkt:**
`CardDesignerViewModelTests.cs:872-894` ruft `SetFromLiveCall` **direkt** mit
sauberem JSON. Der Weg über `IntegrationTester.Preview` wird in keinem Test
durchlaufen — geprüft ist die Stelle, an der es funktioniert.

### Die Entscheidung

- **Der Tester liefert die Antwort zweimal: gekürzt zum Ansehen, ungekürzt zum
  Weiterverarbeiten.** Die Kürzung bleibt, wo sie hingehört — in der Anzeige.
- **`SetFromLiveCall` sagt, ob es geklappt hat**, statt still zurückzukehren.
- **Gezählt wird, was übernommen wurde**, nicht was geantwortet hat.
- Eine Grösse bleibt begrenzt: der ungekürzte Rumpf ist bereits durch die
  Grössengrenze des HTTP-Clients gedeckelt (§21.2) — hier kommt keine neue
  Schranke dazu, aber auch keine weg.

### Der Weg

1. `IntegrationTester.cs:184` — `TestResult` bekommt neben `RawResponse`
   (Anzeige, gekürzt) ein Feld für den vollständigen Rumpf. Die bestehenden
   Anzeigestellen bleiben auf `RawResponse`.
2. `TestSampleStore.SetFromLiveCall` (`:60-76`) — Rückgabewert `bool`, und der
   `catch` protokolliert den Grund, statt ihn zu schlucken. **Ein stiller
   Fänger ist die Lücke, die dieses Projekt mehrfach bezahlt hat.**
3. `CardDesignerViewModel.cs:971-975` — der ungekürzte Rumpf geht hinein, und
   `gelungen++` hängt am Rückgabewert. Wenn eine Quelle antwortet, aber nichts
   übernommen werden konnte, sagt die Statuszeile das: «… geantwortet, die
   Antwort liess sich aber nicht lesen».
4. `TestSampleStore.BuildSnapshot` (`:142-148`) — ausgeschaltete Quellen
   überspringen, damit Vorschau und Abruf denselben Satz Quellen sehen.
   `CanLookup` (`CardDesignerViewModel.cs:250-251`) prüft `Enabled` mit.
5. `CardView.cs:53-58` — heute verschwindet die Karte ganz, wenn ein Abruf
   leere Felder liefert (`Visibility = Collapsed`). Das bleibt so, **aber der
   Designer sagt es**: die Zeile unter der Vorschau nennt «die Antwort enthält
   keines der Felder dieser Karte» statt nichts.

### Risiko

**Mittel.** `TestSampleStore` ist Singleton (`App.xaml.cs:1283`) und speist
auch die Kartenvorschau ausserhalb des Designers. Ein Fehler hier zeigt
falsche Daten in einer Karte, die im Gespräch steht. Deshalb: die drei
Zusagen aus §21.2 bleiben ausdrücklich unberührt — **kein Cache auf der
Platte**, die Probe lebt im Arbeitsspeicher, und die Vorschau ist nie leer,
weil jede Vorlage eine erfundene Beispielantwort mitbringt.

### Definition of Done

- Eine Nummer eingeben, «Abrufen» drücken: die Vorschau zeigt die Werte aus
  der Antwort, und die Zeile darunter sagt «echter Abruf» statt «erfundene
  Beispieldaten».
- Eine Antwort über 8192 Zeichen wird ebenso übernommen — **das ist der
  Befund**, und dafür gibt es einen Test mit einer langen Antwort.
- Eine Quelle, die 404 liefert, meldet das als solches und ersetzt die Probe
  nicht.
- Scheitert die Übernahme, steht der Grund in der Statuszeile **und** im
  Protokoll — ohne Rufnummer und ohne Antwortinhalt (§21.2, ADR-022).
- `CardDesignerViewModelTests` um den Weg **über den Tester** erweitert, nicht
  nur um den direkten Aufruf.
- Am Gerät: **T288** (unten).

---

## C — Der Mailbox-Reiter fällt weg

### Der Befund

Die Umschaltleiste hat vier Spalten (`ShellPage.xaml:1847-1852`), der
Mailbox-Reiter sitzt in der dritten (`:1919-1965`). Dazu gehören:

| Was | Wo |
|---|---|
| Der Reiter samt Abzeichen | `ShellPage.xaml:1919-1965` |
| Der Inhaltsbereich | `ShellPage.xaml:1469-1505` (`VoicemailPanel`) |
| Strg+3 | `ShellPage.xaml:493`, `ShellPage.xaml.cs:599-601` |
| Anzeige und Zustand | `ShellPage.xaml.cs:1619-1642`, `:1723`, `:1726`, `:1732` |
| Der Bereich im Kern | `ShellViewModel.cs:1814` (`ShellSection.Voicemail`) |
| Die Zahl der Nachrichten | `ShellViewModel.cs:118-119`, gefüllt aus `:1333-1334` |
| «Mailbox anrufen» | `ShellViewModel.cs:499-524`, `VoicemailAddress` `:526-550` |

**Zwei Dinge, die der Befund zutage bringt und die vor dem Löschen zu wissen
sind:**

1. **Der Reiter ist der einzige Weg zu «Mailbox anrufen».** Der Befehl
   `CallVoicemailCommand` hat genau einen Aufrufer: den Knopf im
   `VoicemailPanel` (`ShellPage.xaml:1491`). Es gibt **kein** Tastenkürzel und
   **keinen** Eintrag im Infobereich — `TrayIconHost` enthält zum Thema
   Mailbox keine einzige Zeile. Fällt der Reiter, fällt die Funktion mit.
2. **Der Reiter ist die einzige Anzeige für wartende Nachrichten.** Die Kette
   von der Anlage bis ins Fenster ist vollständig gebaut — `SipService.cs:573`
   setzt `accountParams.VoicemailAddress` und abonniert damit MWI,
   `SipEventBridge.cs:183-194` meldet es weiter, `ShellViewModel.cs:1333`
   führt `VoicemailCount` — und ihre **einzige** sichtbare Wirkung sind das
   Abzeichen (`ShellPage.xaml.cs:1723`) und der Text im Panel (`:1626-1634`).

### Die Entscheidung

**Reiter, Abzeichen, Inhaltsbereich und die Anzeige wartender Nachrichten
fallen weg. Die Mailboxnummer bleibt eine Einstellung am Konto.**

Damit bleiben drei Reiter: **Kontakte, Anrufe, Einstellungen** — Strg+1 bis
Strg+3.

**Was mit «Mailbox anrufen» geschieht, ist offene Frage 2** (unten). Der Befehl
wird **nicht** stillschweigend mitgelöscht: er ist eine Telefoniefunktion und
keine Ansicht, und ihn ohne Entscheid zu entfernen hiesse, eine Fähigkeit
wegzunehmen, nach der niemand gefragt hat.

**Was ausdrücklich bleibt** — es hängt nicht am Reiter:

- **Der Toast-Knopf «Mailbox»**, mit dem ein eingehender Anruf auf die Mailbox
  umgeleitet wird (`ToastService.cs:324-330`, `:397-404`, `:468-472`). Das ist
  eine Handlung am laufenden Anruf, keine Ansicht.
- **Das Feld «Mailboxnummer»** in den Einstellungen
  (`SettingsPage.xaml:255-258`, `SettingsViewModel.cs:150-154`) — der
  Toast-Knopf braucht es.
- **Das `voicemail`-Attribut der Provisionierung**
  (`ProvisioningParser.cs:135`, `ProvisioningProfile.cs:84`,
  `ProvisioningService.cs:369`) und `ProvisioningParserTests.cs:56`.

### Der Weg

1. **`ShellPage.xaml`** — Reiter (`:1919-1965`) und Panel (`:1469-1505`) raus,
   die vierte Spalte der Leiste entfällt (`:1847-1852` auf drei), `SettingsTab`
   wandert auf `Grid.Column="2"`, Tooltip «Einstellungen (Strg+3)». Der
   Kommentar über der Leiste (`:1802-1830`) nennt danach drei Knöpfe.
2. **`ShellPage.xaml`** — der `KeyboardAccelerator` für die Vier entfällt
   (`:494`).
3. **`ShellPage.xaml.cs`** — die Drei zeigt auf `SettingsTab` (`:599-605`),
   `OnSetupVoicemailClick` (`:641-650`) entfällt, und in `Refresh()` fallen
   `:1619-1642`, `:1723`, `:1726`, `:1732` weg.
4. **`ShellViewModel.cs`** — `ShellSection.Voicemail` (`:1814`),
   `VoicemailCount` (`:118`), das Abo auf `MessageWaitingChanged` (`:288`,
   `:1333-1334`, `:1743`) und der Sonderfall in `OnSectionChanged`
   (`:1440-1446`) entfallen. `CallVoicemailCommand`, `VoicemailAddress` und
   `HasVoicemailAddress` **bleiben bis zum Entscheid zu Frage 2**.
5. **`SettingsPage.xaml.cs`** — das Sprungziel zur Mailbox (`:1314-1325`,
   `:1358`, `:1419`) hat nach Schritt 3 keinen Aufrufer mehr. Es wird entfernt,
   **nicht auf Vorrat behalten**: eine Fähigkeit ohne Aufrufer ist genau das
   Muster, das dieses Projekt siebenmal bezahlt hat.
6. **Die MWI-Kette im SDK** (`SipEventBridge.cs:53-55/183-194`,
   `SipService.cs:203/395`, `TelephonyLog.cs:114-115`) und
   `accountParams.VoicemailAddress` (`SipService.cs:573-577`): **bleiben.**
   Das Abo kostet nichts, es ist Teil der Kontoeinrichtung, und der Weg zurück
   zu einer Anzeige wäre sonst ein Neubau. **Aber es steht dann eine gebaute
   Kette ohne Empfänger da** — deshalb wird das als ADR festgehalten, damit es
   beim nächsten Durchgang nicht als neuer Fund gemeldet wird.
7. **Doku:** `docs/test-matrix.md:355` (T193) und `:376` (T213) nennen die
   Mailbox als Bereich; beide werden richtiggestellt. ADR-046 und ADR-052
   sprechen von vier Reitern; das wird im neuen ADR als überholt vermerkt.

### Risiko

**Mittel, und das Risiko liegt nicht im Löschen.** Ein vergessener
`x:Name`-Verweis im Code-behind ist ein Compilerfehler, kein stiller Fehler —
`Nipp.App` hat kein Testprojekt, aber der Compiler deckt genau diesen Fall ab.
Gefährlich ist die **Umnummerierung**: die Drei bedeutet danach etwas anderes,
und `ShellSection` wird aus einem `Tag`-String geparst
(`ShellViewModel.cs:472-484`). Ein zurückgebliebenes `Tag="Voicemail"` würde
still nichts tun. Deshalb prüft der Umbau alle vier `Tag`-Werte gemeinsam.

### Definition of Done

- Drei Reiter: Kontakte, Anrufe, Einstellungen. Die Kürzel stimmen mit den
  Tooltips überein, und das vierte tut nichts.
- Kein Verweis auf Voicemail oder Mailbox mehr in `ShellPage.xaml(.cs)` und in
  `ShellViewModel` — ausser dem, was Frage 2 offenlässt.
- Der Toast-Knopf «Mailbox» funktioniert unverändert, und das Feld
  «Mailboxnummer» steht weiter in den Einstellungen.
- Build ohne Warnungen, alle Tests grün; `ProvisioningParserTests` unberührt.
- Am Gerät: **T289** (unten).

---

## Reihenfolge, Abhängigkeiten, Aufwand

| Schritt | Hängt ab von | Aufwand | Eigener Commit |
|---|---|---|---|
| **B** Vorschau im Designer | — | M | ja |
| **C** Mailbox-Reiter weg | — | M | ja |
| **A** Gliederungsebene weniger | C (dieselbe Datei) | S | ja |
| Doku, ADR, Testmatrix | A, B, C | S | mit dem jeweiligen Schritt |

**B geht vor**, weil es ein Fehler ist und die beiden anderen Wünsche sind.
**C vor A**, weil beide `ShellPage.xaml` grossflächig anfassen und C die
Umschaltleiste umbaut, während A die Kontaktliste darüber ändert.

---

## Neue Zeilen für die Testmatrix

| Zeile | Was |
|---|---|
| **T287** | Nebenstellen: «Team» und «Dienste» einzeln zuklappen, nipp neu starten — beide bleiben zu. **Schmal und breit prüfen**, und mit nur einer Gruppe |
| **T288** | Karten-Designer: eine Nummer eingeben, abrufen — die Vorschau zeigt die echten Werte, die Zeile darunter sagt «echter Abruf». **Mit einer Antwort über 8192 Zeichen**, das ist der Befund. Dabei prüfen, dass keine Rufnummer im Protokoll steht |
| **T289** | Drei Reiter, die Kürzel dazu, und der Toast-Knopf «Mailbox» leitet weiterhin um |

---

## Was dieser Plan bewusst **nicht** tut

- **Die MWI-Kette wird nicht abgebaut** (C, Schritt 6). Sie bleibt als Abo am
  Konto bestehen, obwohl sie nach diesem Umbau keinen Empfänger mehr hat. Der
  Abbau wäre ein Eingriff in die Kontoeinrichtung für einen Gewinn von null
  Zeilen im Alltag.
- **Die Kürzung der Anzeigefassung bleibt** (B). 8192 Zeichen sind für ein
  Textfeld richtig; falsch war nur, dieselbe Fassung weiterzuverarbeiten.
- **`IsTeamExpanded` bleibt** (A, Schritt 5), solange Frage 1 nicht anders
  entschieden ist.

---

## Die zwei Fragen sind entschieden (13.09.2026)

1. **Der Abschnitt «Nebenstellen» ist als Ganzes nicht mehr klappbar** — die
   einzelnen Gruppen reichen. Damit fällt `IsTeamExpanded`
   (`ShellViewModel.cs:419-426`) samt `Advanced.ShowTeamContacts`
   (`NippSettings.cs`) weg, nicht nur der Aufklapper. **Wer Platz braucht,
   klappt die Gruppen einzeln zu**, und dieser Zustand wird weiterhin
   gespeichert.
2. **Alles zur Mailbox fällt weg.** Nicht nur der Reiter: auch «Mailbox
   anrufen», der Toast-Knopf «Mailbox», das Einstellungsfeld «Mailboxnummer»,
   das `voicemail`-Attribut der Provisionierung und die ganze MWI-Kette vom
   SDK bis ins Fenster. **Das ist eine Abweichung von der Spezifikation** und
   braucht deshalb einen ADR — siehe unten.

### Was das für C zusätzlich bedeutet

Der Abschnitt «Was ausdrücklich bleibt» oben ist damit **überholt**. Es bleibt
nichts. Zusätzlich zu entfernen sind:

| Was | Wo |
|---|---|
| «Mailbox anrufen» | `ShellViewModel.cs:499-524`, `VoicemailAddress` `:526-548`, `HasVoicemailAddress` `:550`, Nachführen `:1458-1460`, `:1715-1717` |
| Der Toast-Knopf «Mailbox» | `ToastService.cs:324-330`, Farbregel `:353`, `VoicemailAddressFor` `:375-393`, `RedirectToVoicemailAsync` `:397-404`, Auslösung `:468-472`, Klassenkommentar `:16` |
| Das Einstellungsfeld | `SettingsPage.xaml:229-231`, `:255-258`; `SettingsViewModel.cs:76`, `:150-154`, `:1093`, `:1114`, `:1132` |
| Die Kontoeinstellung | `AccountSettings.VoicemailAddress` (`ISipService.cs:357-358`) |
| Das MWI-Abo am Konto | `SipService.cs:573-577` |
| Die MWI-Kette | `SipEventBridge.cs:53-55`, `:98`, `:183-194`, `:339`; `SipService.cs:203`, `:395`; `TelephonyEvents.cs:64-68`; `TelephonyLog.cs:114-115` |
| Provisionierung | `ProvisioningParser.cs:135`, `ProvisioningProfile.cs:84`, `ProvisioningService.cs:369`, **und `ProvisioningParserTests.cs:56`** |

**Der einzige Punkt, der dabei Sorgfalt braucht:** eine bestehende
`settings.json` trägt die Mailboxnummer. Nach dem Entfernen des Feldes liest
System.Text.Json sie als unbekanntes Feld und verwirft sie beim nächsten
Speichern — **die eingetragene Nummer ist dann weg**. Das ist die Folge des
Entscheids und kein Fehler; es gehört in den ADR, damit niemand es später als
Datenverlust sucht.

### Abweichung von der Spezifikation

`NIPP-BUILD.md` verlangt die Mailbox an sieben Stellen: §2 (Umfang, Zeilen
45/47), die Verzeichnisliste (`:275`), **§8.5 als eigener Abschnitt**
(`:365-368`), §8.6 (Toast-Knöpfe, `:372`), die Einstellungstabelle (`:397`),
**M5 samt Akzeptanzkriterium** (`:537-539`), die manuelle Testmatrix (`:561`)
und das Layout in §23 (`:684-685`, `:975`).

Die Spezifikation wird **nicht umgeschrieben**, aber sie darf auch nicht
weiter etwas verlangen, das es nicht mehr gibt: an jeder dieser Stellen kommt
ein kurzer Vermerk auf den ADR. **Eine Spezifikation, die eine Fähigkeit
fordert, die bewusst entfernt wurde, ist die nächste Fehlersuche.**

---

## Was sich durch die Entscheide an der Reihenfolge ändert

Nichts an der Reihenfolge — **B, dann C, dann A** —, aber C wird grösser: es
reicht jetzt vom Fenster über die Einstellungen und die Provisionierung bis in
die SDK-Schicht. Es bleibt trotzdem **ein** Commit, weil ein halb entfernter
Bereich schlimmer ist als ein grosser Diff: die MWI-Kette ohne Anzeige wäre
genau die gebaute, nicht angeschlossene Fähigkeit, die dieses Projekt
siebenmal bezahlt hat.
