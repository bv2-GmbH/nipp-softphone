# Einrichtung und Karten — Plan

**Stand:** 07.09.2026 nachmittags · Zweig `review-umsetzung`
**Anlass:** Die Einstellungen für das CRM und das Gesprächsjournal taugen nicht zum
Einrichten weiterer APIs, und die Anruferkarte lässt sich nicht selbst
zusammenstellen.
**Bezug:** `NIPP-BUILD.md` §21 · `INTEGRATION-PLAN.md` I7, **I9**, **I12** ·
ADR-015 bis ADR-017, ADR-030

---

## Umsetzungsstand

**Alle sechs Phasen sind gebaut** (07.09.2026 nachmittags), in sechs Commits auf
`review-umsetzung`. Build ohne Warnungen, **887 Komponententests** (vorher 723)
plus 16 Architekturtests grün.

| Phase | Stand | Commit |
|---|---|---|
| **K0** Karten in der Konfiguration | erledigt | `K0: Karten stehen jetzt in der Konfiguration` |
| **K1** Feldkatalog, `role()`, `anyOf()` | erledigt | `K1: Feldkatalog …` |
| **K2** Connector-Katalog, Zusammenführen | erledigt | `K2: Connector-Katalog …` |
| **K3** Quellen-Oberfläche | erledigt | `K3: Quellen einrichten an einem Ort …` |
| **K4** Karten-Designer | erledigt, **ohne Ziehen** (T101) | `K4: der Karten-Designer` |
| **K5** Toast als Kartenart | erledigt, **anders als geplant** — siehe unten | `K5: der Toast als Kartenart …` |
| **K6** Dokumentation und ADRs | erledigt | dieser Commit |

**Was anders gelöst wurde als hier geplant, und warum:**

- **K5.** Der Plan wollte die mitgelieferte Toast-Fassung als Karte nachbauen
  („wörtlich über `role()`"). Das geht nicht: eine Karte besteht aus
  **unabhängigen** Zeilen, der Toast setzt seine erste aus drei Werten
  **zusammen** und lässt Teile weg. Als Karte wären das drei
  `concat(if(isEmpty(...)))`-Ungetüme — technisch richtig, im Designer nur als
  „Ausdruck" bearbeitbar, für niemanden lesbar. Jetzt: die bewährte
  Zusammensetzung bleibt Code, eine eingerichtete Karte **ersetzt** sie
  (ADR-034). Die Abnahme des Plans ist damit erfüllt, und zwar durch
  Konstruktion: `ToastComposerTests` läuft unverändert.
- **K3, Fähigkeiten.** Der Plan sah „je einzeln ein/aus" vor. Eine Fähigkeit
  **ist** ihr Block in der Konfiguration (§21.3) — sie abzuschalten hiesse, ihn
  zu entfernen, und damit Endpunkt und Mapping zu löschen. Ein Schalter, der
  bei jedem Umlegen die Arbeit einer halben Stunde wegwirft, ist kein Komfort.
  Sie werden **angezeigt**; geschaltet wird die Quelle.
- **K4, Ziehen.** Nicht gebaut. Eingefügt wird über Doppelklick und einen
  Knopf; das Ziehen steht als **T101** auf der Testmatrix. Ein halb
  funktionierendes Ziehen wäre schlimmer als keines.
- **K2, Ablage der Vorlagen.** Statt einer Kopie im Quellbaum bettet das csproj
  **dieselben** Dateien aus `docs/integrations/` ein. Produktressourcen in
  `docs/` sind unübliche Lage; zwei Dateien mit demselben Inhalt wären
  schlimmer, weil sie driften.

**Am Gerät abzunehmen: T98 bis T109.** Die drei wichtigsten sind **T99**
(Designer während eines Gesprächs), **T104** (eine kaputte Karte darf nur sich
selbst kosten) und **T105** (Toast-Karte — dabei **T90 bis T92 mitfahren**, der
Weg ist angefasst worden).

**Neun Befunde**, die beim Umsetzen aufgefallen sind, stehen in den Commits und
in `CLAUDE.md` unter „Lehren". Die fünf, die ohne Tests unsichtbar geblieben
wären: die Enum-Schreibweise in der ausgegebenen Datei, `emptyText: null` das
das Speichern nicht überlebte, `ThemeService.Attach` mit einem Wurzelelement,
`DraftValueMode.Field` als Rückweg für jeden Ausdruck, und die Sprachausgabe,
die den `ToString()` eines `record` vorliest.

---

## 0. Worum es geht

Drei Wünsche, in der Reihenfolge, in der sie gestellt wurden:

1. Die Einstellungen für die beiden Quellen sind nicht gut.
2. Später sollen **andere** APIs dazukommen — das CRM und das Gesprächsjournal sind
   bv2-eigen und nur heute die einzigen.
3. Die Werte auf der Anruferkarte sollen **selbst angeordnet und ausgewählt**
   werden können.

Und die Bedingung über allem: **einfach einzurichten bleiben.**

Der dritte Wunsch trifft keine schlechte Bedienung, sondern eine Lücke — siehe
B1. Das ist der wichtigste Befund dieses Plans.

---

## 1. Befunde am Ist-Zustand

| # | Befund | Wo |
|---|---|---|
| **B1** | **Karten sind überhaupt nicht konfigurierbar.** `IntegrationConfig` hat kein `cards`, obwohl `INTEGRATION-PLAN.md` D.2 die Datei genau so beschreibt. `CallerCardViewModel.CompileCard()` nimmt immer `DefaultCards.For(ActiveExpanded)` | `Config/IntegrationConfig.cs`, `ViewModels/CallerCardViewModel.cs:154` |
| **B2** | **Einlesen ersetzt die ganze Datei.** `TryImport` endet in `_store.Save(config)`. Nach `crm.json` ist das Gesprächsjournal weg; eine zweite Quelle geht nur über Handarbeit im JSON. Das ist die Stelle, die *andere APIs anbinden* heute verhindert | `IntegrationSettingsViewModel.TryImport` |
| **B3** | **Das Feld Verweis ist freier Text** und hängt an keiner Auswahl. Wer ein Token eintragen will, muss vorher im JSON nachlesen, dass es `crm` heisst. Eine ausgewählte Quelle ändert an diesem Block nichts | `SettingsPage.xaml:798` |
| **B4** | **Es gibt kein Quelle hinzufügen.** Die Vorlagen liegen unter `docs/integrations/` — Dokumentation, nicht Produkt. Eine neue Quelle entsteht nur über eine Datei von aussen | `docs/integrations/*.json` |
| **B5** | **Drei Geschwister-Aufklapper** (Liste · Zugangsdaten · Test), die sich alle auf *die ausgewählte Quelle* beziehen, aber nur einer benutzt die Auswahl. Und das Feld Suchbegriff ist **tot**, sobald eine Quelle `lookupByPhone` hat: der Testabruf nimmt dann immer die Nummer | `SettingsPage.xaml:718–876`, `TestAsync` |
| **B6** | **Testantworten werden weggeworfen.** Sie stehen nur im ViewModel. Eine Kartenvorschau hätte damit nichts zu zeigen | `IntegrationSettingsViewModel.TestResponse` |
| **B7** | **Die mitgelieferte Karte nennt bv2-Quellen.** `crm.*` und `memory.*` stehen in `DefaultCards` neben `crm.*`/`erp.*`. Bei einem Kunden trifft keine dieser Zeilen — genau der Fehler, der schon einmal eine leere Karte ergab | `Cards/DefaultCards.cs` |
| **B8** | **Zwei Orte entscheiden, wer anruft.** Der `ToastComposer` hat fünf fest verdrahtete Feldlisten, die Karte hat ihre `coalesce`-Ausdrücke. Dieselbe Frage, zwei Antworten — dieselbe Bauart wie *beginnt hier ein Anruf zu klingeln*, die schon einmal an beiden Stellen falsch war | `Cards/ToastComposer.cs` |
| **B9** | **Es gibt keinen Feldkatalog.** Der `ToastComposer` verweist im Kommentar auf `docs/integrations/einrichten.md` für die Feldnamen — **dort steht keiner.** Der Verweis ist gebrochen | `ToastComposer.cs`, `docs/integrations/einrichten.md` |

Nichts davon ist ein Fehler in der Architektur. `CardDefinition`,
`CardLayoutEngine` und `CardView` tragen alles, was hier gebraucht wird; sie
sind nur nie an die Konfiguration und an eine Oberfläche angeschlossen worden.

---

## 2. Auftragslage

**Das ist kein Feature ohne Auftrag, sondern das Vorziehen von I9 und I12.**

§21.5 nimmt einen visuellen Karten-Designer und produktspezifische Connectoren
ausdrücklich aus dem **ersten** Ausbau heraus; §21.4 nennt den Designer eine
spätere Ausbaustufe. `INTEGRATION-PLAN.md` führt beide als I9 und I12 mit dem
Vermerk, dass sie **keinen Umbau des Kerns** verlangen. Genau das wird jetzt
eingelöst.

Zu schreiben:

- **`NIPP-BUILD.md` Rev. 8, §21.6 „Einrichtung und Karten (07.09.2026)"** —
  der Auftrag selbst. Ohne ihn gilt die Regel aus CLAUDE.md: kein Feature ohne
  Auftrag.
- **ADR-032** Karten stehen in der Konfiguration; der Editor ist ein eigenes
  Fenster. Begründung: 400 Pixel tragen eine Palette nicht, und die Karte
  bleibt trotzdem 400 Pixel breit — die Vorschau muss das zeigen.
- **ADR-033** Der Connector-Katalog liegt **eingebaut** in nipp, bv2-Einträge
  sind gekennzeichnet. Folge: ein Kunde sieht die Namen CRM und
  Gesprächsjournal im Katalog. Das ist bewusst in Kauf genommen — der Preis der
  Alternative war, dass wir selbst wieder über Dateien einrichten.
- **ADR-034** Der Toast wird eine Kartenart. Ersetzt die **Feldlisten** aus
  ADR-030, nicht dessen Regeln: drei Zeilen plus Attribution, sofort und dann
  ersetzt, Entfernen beim Anrufende — das bleibt alles.
- **ADR-030** bekommt den Vermerk, dass seine Umsetzung durch ADR-034 abgelöst
  ist. Dasselbe Muster wie ADR-009 gegen ADR-018.

---

## 3. Zielbild

Zwei Hälften mit einem gemeinsamen Bestandteil.

```
        ┌──────────────────────── Feldkatalog ────────────────────────┐
        │  Kanonische Feldnamen, deutsche Beschriftungen, Rollen      │
        │  role('name')  ·  anyOf('a','b')  ·  number.*  contacts.*   │
        └───────────────┬─────────────────────────┬───────────────────┘
                        │                         │
        ┌───────────────▼──────────┐   ┌──────────▼──────────────────┐
        │  Quellen einrichten      │   │  Karten zusammenstellen     │
        │  (im Einstellungsmenü)   │   │  (eigenes Fenster)          │
        │                          │   │                             │
        │  Katalog → hinzufügen    │   │  Palette links              │
        │  Detail je Quelle        │   │  Kartenfläche rechts        │
        │  Token mit Herkunft      │   │  Vorschau mit Testdaten     │
        │  Test → dann einschalten │   │  Rückgängig/Wiederholen     │
        │  Zusammenführen, nie     │   │  Gespräch · Eingehend ·     │
        │  ersetzen                │   │  Toast                      │
        └──────────────────────────┘   └─────────────────────────────┘
```

### Das Einstellungsmenü danach

```
▾ Integrationen
  ┌────────────────────────────────────────────────────┐
  │ CRM                Anruferkontext, Kontaktsuche ●  │  ← Zustand statt Text
  │ Gesprächsjournal   Anruferkontext               ●  │
  │ Zeiterfassung ERP  Zugangsdaten fehlen         ⚠ [→]│  ← §15: mit Abhilfe
  └────────────────────────────────────────────────────┘
      [ + Quelle hinzufügen ]        [ Ausgeben ] [ Einlesen ]

  ▾ Anruferkarte
      Gespräch      · 7 Zeilen                [ Bearbeiten … ]
      Eingehend     · 2 Zeilen                [ Bearbeiten … ]
      Toast         · 3 Zeilen                [ Bearbeiten … ]
```

Ein Klick auf eine Quelle öffnet das **Detail dieser Quelle** — nicht drei
Aufklapper daneben, die von der Auswahl nichts wissen. Bearbeiten öffnet den
Designer.

---

## 4. Phasen

### K0 — Karten in der Konfiguration

*Fundament. Ohne Oberfläche, und trotzdem schon nützlich: danach lässt sich
eine Karte über Ausgeben/Einlesen ändern.*

- `IntegrationConfig.Cards : List<CardDefinition>` — die Form aus D.2, nicht
  eine neue.
- **`CardDefinitionValidator`** neben dem bestehenden Validator, mit denselben
  Befunden aus Pfad und Abhilfe: Grenzen aus `CardLayout` (16 Abschnitte, 24
  Zeilen, 12 Elemente), Summe der `span` je Zeile höchstens 6, jeder Ausdruck
  durch den `ExpressionParser`, `openUrl` nur `http`/`https`, unbekannter
  Bausteintyp → Befund statt Absturz, **je Kartenart höchstens eine** Karte,
  `CardKind.Toast` höchstens drei Textzeilen.
- **`CardResolver`**: Art → Karte aus der Konfiguration, sonst
  `DefaultCards.For(art)`. `CallerCardViewModel.CompileCard()` fragt ihn. Eine
  kaputte Karte fällt auf die mitgelieferte zurück **und** erzeugt einen Befund
  in den Einstellungen — das ist die Abnahme, die I4 schon verlangt hat: eine
  Karte mit Fehler in der Definition zeigt einen Hinweis statt abzustürzen.

> **Zuerst dieser Test.** `CardDefinition.Kind` trägt ein eigenes
> `[JsonConverter(typeof(JsonStringEnumConverter))]` **ohne** Benennungsregel,
> der Store serialisiert mit `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`.
> Das Attribut am Typ gewinnt. Es ist also offen, ob in der Datei
> `"activeExpanded"` oder `"ActiveExpanded"` landet — und D.2 schreibt die
> kleine Form. Ein Rundlauf-Test gegen die Beispieldatei aus dem Plan klärt
> das, bevor irgendwer eine Karte von Hand schreibt.

**Tests:** Rundlauf `CardDefinition` → JSON → `CardDefinition` für beide
`DefaultCards` und die Beispielkarte aus D.2; Rückfall bei kaputter Karte;
Auswahl je Art; Span-Summe; Toast-Deckel; Ausgeben/Einlesen nimmt Karten mit.

---

### K1 — Feldkatalog und zwei Ausdrucksfunktionen

*Der gemeinsame Bestandteil. Er macht die Palette möglich **und** räumt B7, B8
und B9 auf.*

- **`Cards/FieldCatalog.cs`** in `Nipp.Core`: kanonischer Feldname → deutsche
  Beschriftung → Art → **Rolle**. Die Rollen sind genau die, die der
  `ToastComposer` heute in fünf Arrays führt: `name`, `company`, `type`,
  `work`, `summary`, `colleague`.
- Eingebaute Namensräume kommen dazu, damit die Palette vollständig ist:
  `number.e164`, `number.national`, `number.digits`, `contacts.displayName`,
  `contacts.company`, `contacts.source`.
- Die Palette entsteht aus **zwei** Quellen: den kanonischen Einträgen (immer
  mit Beschriftung) und den **gefundenen** — den Mapping-Schlüsseln jeder
  eingerichteten Quelle. Ein Schlüssel, der im Katalog steht, bekommt dessen
  Beschriftung; ein eigener wird als eigenes Feld gezeigt, aber gezeigt.
- **`role('name')`** als neue Ausdrucksfunktion: die erste Quelle **nach
  Priorität**, die ein Feld mit einer kanonischen Bezeichnung dieser Rolle hat.
  Dazu **`anyOf('contactName', 'displayName')`** für den Fall ohne Rolle. Damit
  werden die mitgelieferten Karten **quellenunabhängig** — genau das, was B7
  fehlt — und der `ToastComposer` verliert seine Feldlisten (B8).
  - Dafür nötig: `ContextSnapshot` muss seine Quellen **in
    Prioritätsreihenfolge** hergeben. Heute ist `Sources` ein Dictionary ohne
    Ordnung; das Fragment bekommt die Priorität mit, oder der Schnappschuss
    eine geordnete Liste. Kleine Änderung, aber sie gehört hierher und nicht
    in K5.
- **`docs/integrations/feldnamen.md`** — die Tabelle, auf die der
  `ToastComposer` schon verweist. Damit ist B9 geschlossen.

**Tests:** jeder Name aus den heutigen `ToastComposer`-Arrays steht im Katalog
(kein stiller Verlust); `role()` nimmt die Quelle mit der kleineren Priorität;
`role()` ohne Treffer ist leer und nicht Fehler; beide bv2-Vorlagen mappen
ausschliesslich auf katalogisierte oder ausdrücklich eigene Namen.

---

### K2 — Connector-Katalog und Zusammenführen

*Hier stirbt B2 und B4.*

- **`Services/Integrations/Catalog/`** mit `ConnectorTemplate`: Kennung,
  Anzeigename, **Herkunft** (`bv2` oder `generic`), Beschreibung, die
  `DataSourceDefinition`, optionale Karten, **Beispielantwort** und — wichtig —
  je Geheimnis eine **Beschriftung und ein Herkunftshinweis** (CRM →
  Profil → API-Token; nur Lesezugriff genügt).
- Die Vorlagen liegen als **`EmbeddedResource` in `Nipp.Core`**:
  `crm.json`, `journal.json`, `custom-rest.json` (leer, mit
  Kommentaren). Sie sind damit **eine** Wahrheit; ein Test hält die Kopien
  unter `docs/integrations/` deckungsgleich, sonst driften sie auseinander.
- Der Katalog kennzeichnet `bv2`-Einträge in der Oberfläche als **intern bv2**.
  Entschieden am 07.09.2026, ADR-033.
- **`IntegrationConfigStore.AddOrReplaceSource(...)`**: fügt genau **eine**
  Quelle ein und lässt die anderen unberührt. Gleiche Kennung → fragen
  (ersetzen, oder als `crm-2` anlegen).
- **Vorlagen bleiben abgeschaltet.** Die Regel gilt weiter und jetzt auch für
  den Katalog: hinzugefügt wird `enabled: false`, eingeschaltet wird **nach**
  einem erfolgreichen Testabruf. Der Assistent erzwingt diese Reihenfolge, ein
  Test erzwingt sie am Artefakt.
- Das ganze File einlesen bleibt — für die Provisionierung —, bekommt aber eine
  Rückfrage, die **beim Namen nennt**, wie viele Quellen es ersetzt.
- **Testdaten:** `TestSampleStore`, **nur im Arbeitsspeicher**, Prozesslaufzeit.
  §21.2 verbietet einen Cache auf der Platte, und eine echte Antwort ist die
  Kundenkarte eines echten Anrufers. Für eine Vorschau auf einem frischen Gerät
  liefert die Vorlage ihre **Beispielantwort** mit — die ist erfunden und darf
  auf der Platte liegen. Ein echter Testabruf schlägt sie im Speicher.

**Tests:** Hinzufügen verliert keine andere Quelle; jede eingebaute Vorlage ist
`enabled: false`; jede eingebaute Vorlage validiert ohne Befund; jedes
`secretRef` einer Vorlage hat Beschriftung und Hinweis; Doku-Kopie gleich
Ressource; Beispielantwort ergibt beim Mapping nicht-leere Felder.

---

### K3 — Die Quellen-Oberfläche neu

*B3 und B5.*

- Gruppe Integrationen: Liste mit Zustand, darunter **+ Quelle hinzufügen**
  (öffnet den Katalog als `ContentDialog`) und Ausgeben/Einlesen.
- Ein Klick auf eine Quelle öffnet ihr **Detail** — ein Ort statt drei
  Geschwister:
  - **Verbindung**: Basisadresse, Zeitgrenze, grösste Antwort.
  - **Anmeldung**: Art, Schema (`Bearer` / `Token` / …, mit dem Hinweis aus
    CLAUDE.md, dass die `WWW-Authenticate`-Kopfzeile lügt), und das Token in
    einer `PasswordBox`, die **an das `secretRef` dieser Quelle** gebunden ist
    — kein freier Verweis mehr. Daneben der Herkunftshinweis aus der Vorlage.
  - **Fähigkeiten**: welche eingerichtet sind, je einzeln ein/aus.
  - **Test**: nur die Felder, die die Quelle wirklich hat — und wenn sie beide
    Fähigkeiten hat, werden **beide** geprüft, nicht nur die erste.
  - **Erweitert**: das JSON **dieser Quelle** bearbeiten, mit Prüfung beim
    Übernehmen. Nicht mehr die ganze Datei.
- Fehlt ein Geheimnis, steht die Abhilfe an der Zeile und führt hin (§15).

---

### K4 — Der Karten-Designer

*Eigenes Fenster, Palette links, Kartenfläche rechts. Das grösste Stück
Oberfläche, das nipp bisher bekommen hat — deshalb die Aufteilung unten.*

**Die tragende Entscheidung: die Logik liegt in `Nipp.Core`.** `Nipp.App` hat
kein Testprojekt; was dort liegt, ist nur am Gerät prüfbar. Beim
`ToastComposer` war das schon einmal der Grund, Logik in den Kern zu ziehen,
und ein Designer hat mehr Zustand als alles andere in nipp.

- **`Nipp.Core/ViewModels/CardDesignerViewModel.cs`** hält **alles**:
  - `CardDraft` — ein veränderlicher Spiegel von `CardDefinition` aus
    `ObservableCollection`s (Abschnitte → Zeilen → Spalten → Elemente) mit
    `FromDefinition` / `ToDefinition`.
  - Auswahl, Einfügen, Verschieben, Entfernen, `span` ändern.
  - **Rückgängig/Wiederholen** als Stapel kleiner umkehrbarer Schritte,
    mindestens 50 tief.
  - Laufende Prüfung über den `CardDefinitionValidator`.
  - `Preview` als `CardModel`, gebaut mit `CardLayoutEngine` aus dem
    Schnappschuss der Testdaten.
- **`Nipp.App/Windows/CardDesignerWindow.xaml`** bleibt dumm: Palette links,
  Fläche rechts (die Vorschau **ist** `CardView` — dasselbe Steuerelement wie
  im Gespräch, damit die Vorschau nicht lügt), Eigenschaften unten, zwei Knöpfe
  für Rückgängig, Speichern/Verwerfen.
- **Ziehen ist strukturell, nicht pixelweise**: ein Feld aus der Palette fällt
  *vor* eine Zeile, *hinter* eine Zeile oder *in* eine Spalte — nie auf eine
  Koordinate. §21.2 begründet das schon für die Definition selbst. Beim
  Fallenlassen entsteht ein `field` mit der Beschriftung aus dem Katalog.
- **Eigenschaften je Bausteinart**: Beschriftung, Stil, `MaxLines`,
  `EmptyText`, Ton — und `VisibleWhen` über einen **Baukasten** für die
  häufigen Fälle (nur wenn ein Wert da ist → `!isEmpty(x)`) plus ein
  Ausdrucksfeld für den Rest, live geprüft.
- **Erster Treffer aus …** ist ein eigener Wertmodus: mehrere Felder aus der
  Palette ankreuzen, gespeichert wird `coalesce(a, b, c)`. Ohne das wäre die
  wichtigste Zeile der Karte — der Name — nicht ohne Tippen zu bauen.
- **Fenstermechanik.** Hier liegen die Narben aus CLAUDE.md, alle drei:
  - Das Fenster entsteht über die **`DispatcherQueue`** des Hauptfensters. Ein
    Aufruf von einem Fremdthread endet in *Unzulässiges Fenster*.
  - `AppWindow` rechnet in **physischen** Pixeln. `WindowPlacement`
    wiederverwenden, nicht neu rechnen.
  - Höchstens ein Designer; ein zweiter Aufruf holt den bestehenden nach vorn.
  - **Das Schliessen des Designers darf am Hauptfenster nichts abmelden.**
    Genau dieser Fehler in acht Zeilen hat den eingehenden Anruf unsichtbar
    gemacht. Eigener `Closed`-Behandler, eigener Zustand, kein Griff in
    `MainWindow`.
  - **Ein Anruf zählt mehr als der Designer.** Kommt einer, holt das
    Hauptfenster die Gesprächsansicht wie immer nach vorn (§10, T06). Der
    Designer wird nie das Hauptfenster und hält nichts auf.
  - `Core.Iterate()` läuft auf demselben Thread. Die Vorschau baut deshalb
    **inkrementell** neu — die Element-Schlüssel sind dafür stabil, und
    `CardView` findet seine Steuerelemente darüber wieder.
  - Der Designer bekommt **keinen eigenen Eintrag in der Taskleiste**; er
    gehört zum Hauptfenster.

**Tests (im Kern, rund 40):** Rundlauf `FromDefinition(ToDefinition(x)) == x`
für beide `DefaultCards` und beide Vorlagenkarten; jeder Schritt ist
rückgängig; Rückgängig → Wiederholen ergibt dieselbe Definition; Einfügen an
den Rändern; `span`-Summe wird beim Verschieben eingehalten; Erster Treffer aus
erzeugt gültiges `coalesce`; ein ungültiger Ausdruck sperrt Speichern und nennt
die Stelle. **Das Fenster selbst gehört auf die Testmatrix**, nicht in
Komponententests.

---

### K5 — Der Toast als Kartenart

- `CardKind.Toast`. `ToastComposer.Compose` bekommt eine `CompiledCard` statt
  seiner Arrays: drei `text`-Zeilen plus eine Attributionszeile. Die
  Windows-Grenze wird eine Regel im Validator **und** eine Form im Designer —
  der Toast zeigt sich dort als genau drei Plätze plus Attribution, nicht als
  freie Fläche. Was nicht passt, schneidet Windows sonst mitten im Wort ab.
- Die mitgelieferte Toast-Karte gibt das heutige Verhalten **wörtlich** wieder,
  aber über `role('name')`, `role('work')`, `role('summary')` aus K1 — also
  quellenunabhängig.
- **Die Abnahme ist die Regression.** Die bestehenden `ToastComposer`-Tests
  müssen **unverändert** gegen die kartengetriebene Fassung durchlaufen. Wo
  einer angepasst werden müsste, ist ADR-030 verletzt.
- **Reihenfolge-Vorbehalt:** T90 bis T92 (Toast mit und ohne Quellen,
  Benachrichtigungscenter) sind **am Gerät noch nicht abgenommen**. K5 fasst
  also einen Weg an, der noch nicht bestätigt ist. Die Tests sind das Tor;
  danach werden T90–T92 neu gefahren, nicht als bestanden übernommen.

---

### K6 — Dokumentation und Abnahme

- `NIPP-BUILD.md` Rev. 8 §21.6; ADR-032, ADR-033, ADR-034; Vermerk an ADR-030.
- `docs/integrations/einrichten.md` **neu geschrieben**: der Weg ist jetzt
  Katalog → Token → Test → Einschalten, nicht mehr Datei → Einlesen. Der
  Abschnitt *Ein Token besorgen* bleibt, er ist gut und unberührt.
- `docs/integrations/feldnamen.md` (K1) und `docs/integrations/karten.md` neu —
  Bausteinmenge, was der Baukasten für Bedingungen kann, was nur als Ausdruck
  geht.
- `INTEGRATION-PLAN.md`: I9 und I12 auf erledigt, mit dem Datum.
- Testmatrix **T98 ff.**: Designer öffnen während ein Gespräch läuft · bei
  150 % Skalierung · hell und dunkel · Rückgängig nach 30 Schritten · Karte mit
  Fehler · Toast mit drei langen Zeilen · Quelle aus dem Katalog ohne Token ·
  **zweite Quelle hinzufügen, ohne die erste zu verlieren** (das ist B2, und es
  gehört an das Gerät).
- CLAUDE.md: Meilenstein nachziehen.

---

## 5. Reihenfolge

```
K0 ──► K1 ──► K2 ──► K3 ──► K4 ──► K5
Karten  Feld-  Kata-  Ober-  Desi-  Toast
in der  kata-  log +  flä-   gner
Datei   log    Zu-    che
               sammen-
               führen
                      K6 läuft mit, wird nicht angehängt
```

- **K1 vor K4**: ohne Katalog hat die Palette keinen Inhalt.
- **K2 vor K4**: ohne Testdaten zeigt die Vorschau nichts.
- **K0 vor allem**: ohne `cards` in der Datei hat der Designer nichts zu
  speichern.
- **K5 zuletzt**: es ist der einzige Schritt, der einen Weg anfasst, der im
  Alltag schon läuft.

**Nach K3 ist der Wunsch schon zur Hälfte erfüllt** — Quellen richten sich dann
sauber ein, und Karten lassen sich über Ausgeben/Einlesen ändern. Das ist die
Schnittlinie, an der sich anhalten liesse, falls K4 länger dauert als gedacht.
K4 ist die Bequemlichkeit, und es ist das Stück mit der weichsten Schätzung.

---

## 6. Risiken

| Risiko | Grösse | Umgang |
|---|---|---|
| `CardKind` serialisiert gross- statt kleingeschrieben und weicht von D.2 ab | klein, aber sofort | Der erste Test in K0 |
| Zwei Fenster auf dem Thread, der `Core.Iterate()` bedient | **mittel** | Vorschau inkrementell über stabile Schlüssel; am Gerät mit laufendem Gespräch prüfen |
| Der Designer ist viel Oberfläche in einer Assembly ohne Tests | **mittel** | Aller Zustand in `Nipp.Core`, das Fenster dumm. Das ist die Lehre aus dem `ToastComposer` |
| Der Designer schliesst und das Hauptfenster ist danach taub | klein, teuer | Eigener `Closed`-Behandler, kein Griff in `MainWindow`. Genau dieser Fehler hat T06 wochenlang gekostet |
| K5 fasst den Toast an, bevor T90–T92 abgenommen sind | klein | Bestehende Tests unverändert als Tor, danach neu abnehmen |
| Kunden sehen das CRM im Katalog | bekannt | Entschieden, ADR-033. Kennzeichnung intern bv2 |
| Umfang | **das eigentliche Risiko** | Schnittlinie nach K3 steht oben |

**Was unberührt bleibt:** `Services/Integrations/` kennt weiterhin weder das
SDK noch WinUI — der `CardDraft` liegt in `Nipp.Core.ViewModels`, das Fenster
in `Nipp.App`. `IntegrationBoundaryTests` und der Architekturtest über
`using Linphone` gelten unverändert. Und die Regel über allem: **Telefonieren
hängt von keiner Integration ab.** Fällt der Designer aus, klingelt und wählt
nipp weiter.
