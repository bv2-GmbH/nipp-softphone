# Review der Oberfläche — Befund und Plan

Stand 05.09.2026, Build `9fa8df1` plus Navigations-Fix für die Einstellungen. Geprüft am laufenden Programm mit Screenshots (unter `docs/review/2026-09-05/`), dazu Log und XAML.

Massstab: **kompakt, ohne dass jemand an der Fenstergrösse ziehen muss.** Fenster 400 × 780 logische Pixel, auf dieser Maschine bei 150 % also 600 × 1170 physisch.

---

## 1. Was auffällt — auf einen Blick

| Nr. | Befund | Art | Gewicht |
|---|---|---|---|
| F1 | **Einstellungen stürzen ab** — `AccountStateTextConverter` fehlt | Fehler | blockiert |
| F2 | „Registration refreshing" wird als **Fehler** protokolliert und angezeigt | Fehler | hoch |
| F3 | „Einstellungen" in der Leiste **abgeschnitten** („Einstellunger") | Fehler | sichtbar |
| F4 | Chevron der Tastatur zeigt **verkehrt** | Fehler | klein |
| F5 | Fenster **780 hoch** — passt bei 150 % nicht auf einen 1080p-Bildschirm | Fehler | hoch |
| K1 | Tastatur offen → **nur 4 Kontaktzeilen** sichtbar | Kompaktheit | hoch |
| K2 | Tastatur 64-px-Tasten, 280 px hoch — ein Drittel des Fensters | Kompaktheit | hoch |
| K3 | Eigene Zeile nur für „⌄ Tastatur" | Kompaktheit | mittel |
| K4 | Listenzeilen 70 px hoch | Kompaktheit | mittel |
| K5 | Umschaltleiste 70 px, Nummernfeld 60 px, Kontoauswahl 40 px + Ränder | Kompaktheit | mittel |
| D1 | Untertitel der Kontakte inkonsistent (mal Firma, mal Nummer) | Design | mittel |
| D2 | Team und Outlook nicht sichtbar getrennt (§8.4) | Design | mittel |
| D3 | Anrufliste zeigt nur Uhrzeit — gestern und heute sehen gleich aus | Design | mittel |
| D4 | Gesprächsansicht noch im **Desktop-Zuschnitt** (Padding 24, Keypad neben Qualität) | Design | hoch |
| D5 | Kein Fokus im Nummernfeld beim Start | Design | klein |
| D6 | Kontakte laden 3 s ohne sichtbaren Fortschritt | Design | klein |
| D7 | Doppelte BLF-Synchronisation beim Start | Aufräumen | klein |

---

## 2. Fehler

### F1 · Einstellungen stürzen ab

```
[FTL] Unbehandelte Ausnahme: Cannot find a Resource with the Name/Key
      AccountStateTextConverter [Line: 87 Position: 49]
```

`SettingsPage.xaml` Zeile 87 bindet an einen Konverter, den es nicht gibt — weder in `DisplayConverters.cs` noch in `App.xaml`. Die Seite stirbt beim ersten Messen, die App mit ihr. **Das ist der Grund, warum sich die Einstellungen nach dem Navigations-Fix immer noch „nicht öffnen".**

Der Konverter soll aus `AccountStatus` einen Text machen: Zustand plus Meldung („Registriert", „Fehlgeschlagen: …"). §8.4 verlangt den Zustand als Text neben der LED.

**Massnahme:** `AccountStateTextConverter` schreiben, in `App.xaml` eintragen. Danach alle XAML-Dateien auf `StaticResource`-Schlüssel prüfen, die nirgends definiert sind — ein Skript oder ein Architekturtest, der XAML-Ressourcenschlüssel gegen `App.xaml` und `Tokens.xaml` abgleicht. Derselbe Fehler ist im Projekt schon einmal aufgetreten (Konverter im falschen Ressourcenbaum).

### F2 · „Registration refreshing" ist kein Fehler

```
[ERR] Registrierung bei pbx.example.ch fehlgeschlagen: Registration refreshing.
      Zugangsdaten, Domain und Transport prüfen.
[INF] Registrierung Registered fuer "151bv2"
```

Beim Erneuern der Registrierung meldet das SDK `Refreshing`. `SipEventBridge` bildet das offenbar auf `Failed` ab, und der Fehlerkatalog hängt „Zugangsdaten prüfen" an — für einen Vorgang, der eine halbe Sekunde später erfolgreich ist. Die LED wird dabei kurz rot, und wer ins Log schaut, sucht einen Fehler, den es nicht gibt.

**Massnahme:** Mapping prüfen. `Refreshing` → `InProgress`. Nur `Failed` und `Cleared` mit Fehlertext sind Fehler.

### F3 · „Einstellungen" abgeschnitten

Vier Spalten à ~88 px, Schriftgrad 10, Text „Einstellungen" mit 13 Zeichen — passt nicht. Das Wort fängt bei „i" an.

**Massnahme:** siehe K5 — die Leiste wird ohnehin umgebaut. Bezeichnung entweder weglassen (Symbole reichen bei vier Knöpfen, Tooltip dazu) oder kürzen.

### F4 · Chevron verkehrt

Zugeklappt zeigt der Chevron nach oben, aufgeklappt nach unten. Der WinUI-`Expander` macht es umgekehrt: zugeklappt nach unten („hier lässt sich etwas aufklappen"), offen nach oben. Wer WinUI-Apps kennt, klickt intuitiv falsch.

```csharp
// ShellPage.xaml.cs:156 — Glyphen tauschen
DialpadChevron.Glyph = ViewModel.IsDialpadVisible ? "" : "";
```

Wird durch K3 vermutlich ganz überflüssig.

### F5 · Das Fenster ist zu hoch

780 logische Pixel sind bei 150 % Skalierung 1170 physische — **ein 1080p-Laptop mit 150 % ist die häufigste Konfiguration im Büro**, und dort ragt nipp unten aus dem Bildschirm. Genau der Fall, in dem jemand „an der Grösse rumspielen" muss.

**Massnahme:**
- Standardhöhe auf **660** senken (mit den Kompaktheitsmassnahmen unten passt alles hinein).
- Beim Anlegen des Fensters gegen den Arbeitsbereich des Bildschirms prüfen (`DisplayArea.WorkArea`) und auf höchstens 90 % davon begrenzen.
- Position merken und beim nächsten Start wiederherstellen — sonst steht das Fenster jedes Mal woanders.

---

## 3. Kompaktheit

### Wo die Höhe hingeht

Aus dem Screenshot mit offener Tastatur (`keypad.png`), umgerechnet auf logische Pixel:

| Bereich | heute | Anteil |
|---|---|---|
| Titelleiste | 36 | 5 % |
| Kontoauswahl + Ränder | 48 | 6 % |
| Nummernfeld + Wählen | 60 | 8 % |
| Zeile „Tastatur" | 32 | 4 % |
| Wähltastatur | 280 | **36 %** |
| Inhalt (Kontakte / Anrufe / Mailbox) | ~234 | 30 % |
| Umschaltleiste + Ränder | 78 | 10 % |
| Abstände dazwischen | ~12 | |

Die Tastatur nimmt mehr als ein Drittel; der Inhalt — das, wofür man das Fenster offen hat — bekommt weniger. Mit offener Tastatur sind **vier Kontakte** sichtbar.

### Ziel

Bei **660 px Höhe** und offener Tastatur mindestens **sechs Listenzeilen**, ohne Tastatur mindestens **zehn**. Kein Text abgeschnitten.

### Massnahmen

| Nr. | Was | Gewinn |
|---|---|---|
| **K2** | Tasten 64 → **48 px**, Abstand 8 → 6, Ziffer 22 → 20 pt, Buchstaben 10 → 9 pt. Vier Reihen: 280 → **210** | −70 |
| **K3** | Zeile „⌄ Tastatur" streichen. Stattdessen ein **Symbolknopf rechts im Nummernfeld** (Tastatur-Glyph ``), neben dem × | −32 |
| **K5a** | Nummernfeld: Schrift 22 → 20, Höhe fest **44**; Wählen-Knopf **44 × 44** statt 56 × 40 — gleich hoch wie das Feld, heute ragt er darüber hinaus | −16 |
| **K5b** | Kontoauswahl: `MinHeight="32"`, Padding reduzieren | −8 |
| **K5c** | Umschaltleiste: Knöpfe **48 hoch**, nur Symbole (18 px) mit Tooltip, oder Symbol + 9-pt-Text. Löst F3 mit | −30 |
| **K4** | Listenzeilen: Padding 6 → **3**, Avatar 32 → **28**, Untertitel bleibt. 70 → **52 px** je Zeile | +35 % Zeilen |
| **K6** | Aussenränder der Seite 12/8 → **8/6**, `RowSpacing` 8 → **6**. Inhaltskarte: Suchzeile Padding 8 → 6 | −14 |
| **K7** | Titelleiste 36 → **32** (WinUI-Standard) | −4 |

Summe: rund **−175 px** bei offener Tastatur. Bei 660 Fensterhöhe bleiben dem Inhalt ~**290 px** mit Tastatur (5–6 Zeilen à 52) und ~**500 px** ohne (9–10 Zeilen).

### Was nicht angetastet wird

Die Reihenfolge aus §20.1 — Konto, Eingabe, Wählen, Tastatur, Inhalt, Umschaltleiste — bleibt. Sie ist so bestellt, und sie ist richtig. Nur die Höhe der einzelnen Zeilen ändert sich.

---

## 4. Design

### D1 · Untertitel der Kontakte

„Adrian Schoch · +41 786672728" neben „Anna Beispiel · bv2 GmbH": mal Nummer, mal Firma, je nachdem, was Outlook hergibt. Für ein Telefon ist die Nummer die Information; die Firma hilft nur bei Namensgleichheit.

**Regel:** Untertitel ist **immer die Nummer**, formatiert (`+41 78 667 27 28` statt `+41786672728` — der `NumberNormalizer` kann das rückwärts). Firma dahinter mit `·`, wenn Platz ist, sonst weg (`TextTrimming`).

### D2 · Team und Outlook getrennt sichtbar

§8.4 verlangt „beide Quellen getrennt sichtbar". Heute ist das nur über die Sortierung gelöst — Team steht oben, aber nichts sagt, wo Team endet und Outlook beginnt. Mit 137 Outlook-Kontakten und null Team-Einträgen sieht niemand, dass es zwei Quellen gibt.

**Massnahme:** `CollectionViewSource` mit Gruppierung, Gruppenkopf „Team (3)" / „Outlook (137)" als schmale Zeile (24 px). Wenn eine Gruppe leer ist, ein Hinweis statt nichts: „Keine Team-Nebenstellen — in den Einstellungen eintragen."

### D3 · Anrufliste ohne Datum

„07:05" — heute, gestern, letzte Woche? §20.3 nennt die Uhrzeit, meint aber offensichtlich „wann". Nach einem Wochenende ist die Liste unbrauchbar.

**Regel:** heute → `07:05`, gestern → `gestern 07:05`, älter → `Mo 01.09.` in der schmalen Spalte rechts. Ein `RelativeDateConverter`.

### D4 · Gesprächsansicht im Desktop-Zuschnitt

`ActiveCallPage.xaml` verwendet `NippPagePadding` (24/20/24/24) und stellt das DTMF-Keypad **neben** das Qualitätspanel, mit 24 px Abstand. Bei 400 px Breite: 3 × 64 + 2 × 8 = 208 fürs Keypad, 24 Abstand, bleiben **120 px** für Codec, Verschlüsselung, RTT, Jitter, Paketverlust. Das wird umbrechen oder abschneiden.

Dazu stehen Gegenstelle, Dauer, Zwei-Gespräche-Karte, fünf Knöpfe, Weiterleiten-Karte mit Erklärtext, DTMF und Qualität untereinander — deutlich mehr als 660 px.

**Massnahmen:**
- Padding auf 12/8, wie die Shell.
- Keypad **unter** das Qualitätspanel, nicht daneben; dasselbe 48-px-Keypad wie in der Shell (K2).
- Qualitätspanel als **eine Zeile** Chips: `PCMU · SRTP · 23 ms · 0.0 %` — die Werte im Detail hinter einem Aufklapper.
- Weiterleiten-Erklärtext („Blind: sofort abgeben …") in einen Tooltip; im Fenster nur die zwei Knöpfe.
- `ScrollViewer` um den Inhalt, falls noch keiner da ist — **im Screenshot nicht geprüft**, weil kein Gespräch lief. Bei der Umsetzung mit einem echten Anruf aufnehmen.

### D5 · Fokus

Beim Start und beim Zurückkehren aus den Einstellungen sollte der Cursor im Nummernfeld stehen. Wer nipp öffnet, will wählen — `NumberBox.Focus(FocusState.Programmatic)` in `OnLoaded`.

### D6 · Kontakte laden ohne Rückmeldung

Nach dem Start bleibt die Kontaktliste 3 Sekunden leer (Outlook über COM), der Platzhalter sagt „Kontakte werden eingelesen …" — aber ohne `ProgressRing`. Ein stehender Text sieht nach hängender App aus.

**Massnahme:** `ProgressRing` (20 px) neben dem Text, gebunden an `IsLoadingContacts`. Und `IsLoadingContacts` muss beim **Start** gesetzt werden, nicht nur beim manuellen Neuladen — heute setzt es nur `ReloadContactsAsync`.

### D7 · Doppelte BLF-Synchronisation

```
[INF] Besetztlampenfeld: 0 Nebenstellen abonniert
[INF] Besetztlampenfeld: 0 Nebenstellen abonniert
```

`App.StartContacts` und `ShellViewModel` synchronisieren beide nach dem Laden. Harmlos, aber zwei Stellen für dieselbe Sache sind eine zu viel. Die App macht es; das ViewModel nur beim manuellen Neuladen.

### Kleinigkeiten

- **Wählen-Knopf** ist ohne Eingabe grau — richtig, aber er wirkt wie ein Schmuckelement, weil er grösser ist als das Feld daneben. Mit K5a erledigt.
- **Leerraum im Mailbox-Bereich:** Symbol, zwei Zeilen Text, Knopf — vertikal zentriert in 230 px. Ist in Ordnung, wenn der Bereich kleiner wird; bei einer grösseren Fläche lieber die Anzahl neuer Nachrichten als grosse Zahl zeigen.
- **Kontoauswahl** zeigt „151bv2" — den Benutzernamen, weil beim Anlegen kein Anzeigename eingetragen wurde. Fallback ist richtig; das Feld in den Einstellungen könnte „Anzeigename (empfohlen)" heissen statt „(optional)".
- **Log:** `Erscheinungsbild auf System gesetzt, wirksam: Default` — „Default" sagt nichts. Aufgelöst protokollieren: `wirksam: Dark`.
- **Einstellungsseite** konnte wegen F1 nicht angesehen werden. Nach dem Fix prüfen: die Codec-Zeile hat vier Spalten (Häkchen, Name, ↑, ↓), die beiden `InfoBar`s (Echounterdrückung, Verschlüsselung) haben lange Texte, und die neue Kontakte-Gruppe mit drei Eingabefeldern — alles bei 400 px Breite.

---

## 5. Reihenfolge

Vier Schritte, jeder für sich baubar und prüfbar.

### Schritt 1 — Fehler (F1, F2, F4, D7)

Der Konverter, das Refreshing-Mapping, die Glyphen, die doppelte Synchronisation. Danach: Einstellungen öffnen sich, Log ohne `ERR` beim Start.

### Schritt 2 — Fenster und Tastatur (F5, K2, K3, K7)

Fensterhöhe 660 mit Begrenzung auf den Arbeitsbereich, Position merken. Tasten 48 px, Toggle ins Nummernfeld. Titelleiste 32. Das ist der grösste Höhengewinn.

### Schritt 3 — Zeilen und Leiste (K4, K5, K6, F3, D1, D3)

Listenzeilen 52 px, Nummernzeile 44, Kontoauswahl 32, Umschaltleiste 48 mit Symbolen. Untertitel-Regel und Datumsformat gleich mit, weil dieselben Templates angefasst werden.

### Schritt 4 — Gesprächsansicht und Rest (D2, D4, D5, D6)

Gesprächsansicht aufs Smartphone-Format, Gruppierung der Kontakte, Fokus, Fortschrittsanzeige. Einstellungsseite bei 400 px durchsehen.

### Abnahme

Screenshots wie in `docs/review/2026-09-05/` erneut aufnehmen und vergleichen.
**Das Ergebnis steht unten im Abschnitt „Umsetzung"** — die Liste hier ist die ursprüngliche Vorlage und bleibt unangetastet, damit nachvollziehbar bleibt, wonach gesucht wurde:

- [ ] Einstellungen öffnen und schliessen sich, kein Absturz
- [ ] Start ohne `ERR` und ohne `FTL` im Log
- [ ] Fenster ≤ 660 logische Pixel hoch, passt auf 1080p bei 150 %
- [ ] Tastatur offen: ≥ 6 Kontaktzeilen sichtbar
- [ ] Tastatur zu: ≥ 10 Kontaktzeilen sichtbar
- [ ] Kein abgeschnittener Text in der Umschaltleiste
- [ ] Gesprächsansicht bei einem echten Anruf: alles ohne Scrollen sichtbar oder sauber scrollbar
- [ ] Team-Gruppe und Outlook-Gruppe in der Kontaktliste unterscheidbar
- [ ] Anruf von gestern zeigt „gestern", nicht nur eine Uhrzeit

---

## 6. Nicht Teil dieses Reviews

Telefoniefunktion, Audio, Registrierung — das war Gegenstand der Abnahme M2/M3 und funktioniert (der Anruf um 07:05 steht in der Liste). Hier geht es um das, was man sieht.

---

# Umsetzung — Ergebnis

Alle vier Schritte umgesetzt am 05.09.2026. Nachher-Screenshots unter
`docs/review/2026-09-05-nachher/`, die Vorher-Bilder liegen unverändert unter
`docs/review/2026-09-05/`.

## Was gemessen wurde

| | vorher | nachher | Ziel |
|---|---|---|---|
| Fensterhöhe | 780 logisch | **660** | ≤ 660 |
| … physisch bei 150 % | 1170 (passt nicht auf 1080p) | **990** | < 1080 |
| Wähltastatur | 280 px (36 % der Höhe) | **194** | — |
| Listenzeile | ~70 px | **~43** | ≤ 52 |
| Umschaltleiste | 78 px | **46** | — |
| Kontakte mit Tastatur | 4 Zeilen | **6** | ≥ 6 |
| Kontakte ohne Tastatur | 7 Zeilen | **11** | ≥ 10 |

## Abnahme

- [x] Einstellungen öffnen und schliessen sich, kein Absturz
- [x] Start ohne `ERR` und ohne `FTL` im Log — die verbliebenen Warnungen sind echt (Hotkey belegt, Zugangsdaten fehlen)
- [x] Fenster ≤ 660 logische Pixel, passt auf 1080p bei 150 %
- [x] Tastatur offen: 6 Kontaktzeilen sichtbar
- [x] Tastatur zu: 11 Kontaktzeilen sichtbar
- [x] Kein abgeschnittener Text in der Umschaltleiste
- [x] Team- und Outlook-Gruppe unterscheidbar — Kopfzeile erscheint, sobald es zwei Quellen gibt
- [x] Anruf von gestern zeigt „gestern", nicht nur eine Uhrzeit
- [ ] Gesprächsansicht bei einem echten Anruf — **offen**, siehe unten

## Was zusätzlich gefunden wurde

### Ein Testlauf löschte die Konfiguration des Benutzers

Der schwerwiegendste Fund des Tages, und er stand als bewusste Entscheidung im Kommentar der Testdatei:

> „Die Tests laufen gegen die echten Pfade unter `%APPDATA%` und `%LOCALAPPDATA%` — deshalb räumen sie vorher und nachher auf. Das ist nicht schön, aber ehrlicher als eine Abstraktion, die nur für die Tests existiert und den DPAPI-Pfad gerade nicht mitprüft."

Das Aufräumen war das Problem. Ein gewöhnliches `dotnet test` hat das eingerichtete SIP-Konto samt Passwort gelöscht; der Test *„eine kaputte Datei kostet nicht den Start"* schreibt schliesslich eine kaputte Datei — und schrieb sie dorthin, wo die echte lag. Gefunden wurde es, weil nach einem Testlauf `settings.json.kaputt-20260905-080357` mit dem Testinhalt `{ das ist kein gültiges JSON ][` im Benutzerprofil lag.

Behoben: `SettingsService` und `SecretStore` nehmen den Ablageort als optionalen Konstruktorparameter, jeder Test bekommt ein eigenes Wegwerfverzeichnis. Der DPAPI-Pfad wird weiterhin echt geprüft — die Verschlüsselung hängt am Benutzerkonto, nicht am Ablageort, es geht nichts verloren.

`TestIsolationTests` verhindert den Rückfall: kein `SpecialFolder` und keine `%APPDATA%`-Variable in Testcode.

### `AppWindow` rechnet in physischen Pixeln

Beim Umsetzen von F5 selbst hineingelaufen: `MoveAndResize(400, 660)` ergibt auf einem 150-%-Bildschirm ein Fenster von 267 × 440 **logischen** Pixeln. Die Bedienelemente wirkten riesig, der Inhaltsbereich schrumpfte auf einen Streifen. `WindowPlacement` multipliziert die Wunschmasse jetzt mit `GetDpiForWindow / 96`.

### Ein fehlender Ressourcenschlüssel bringt die App zum Absturz

F1 war kein Einzelfall, sondern eine Klasse von Fehlern: XAML löst Ressourcen erst zur Laufzeit auf, der Compiler schweigt. `XamlResourceTests` gleicht jetzt jeden `StaticResource`- und `ThemeResource`-Verweis gegen die Wörterbücher ab.

**Was der Test nicht findet:** Typfehler. Beim Umsetzen von K7 habe ich `RowDefinition.Height="{StaticResource NippTitleBarHeight}"` geschrieben — der Schlüssel existierte, war aber ein `x:Double` und keine `GridLength`. Auch das scheitert erst zur Laufzeit. Die Titelleistenhöhe steht deshalb als Zahl im XAML, mit einem Kommentar dazu.

## Was offen bleibt

| Punkt | Warum |
|---|---|
| **Gesprächsansicht am echten Anruf prüfen** | Sie ist umgebaut (Aktionen zweizeilig, DTMF und Qualität als Aufklapper, Padding 12/8), aber ohne laufendes Gespräch nicht zu beurteilen. Beim nächsten Testanruf ansehen. |
| **Kontopasswort neu eintragen** | Dem Testlauf zum Opfer gefallen, siehe oben. Einstellungen → SIP-Konten. |
| **Hotkey belegt** | `Strg+Umschalt+A` hat auf dieser Maschine eine andere Anwendung. Die Meldung steht jetzt in den Einstellungen neben dem Feld — dort ein freies Kürzel wählen. |
| **AP7.8 Messungen** | Gehören auf echte x64-Hardware (ADR-001), nicht in die Emulation. |
