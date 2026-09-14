# Abnahme M2 und M3 — die Tests, die einen Menschen brauchen

Stand 04.09.2026. Sieben Punkte, alle gegen `pbx.example.ch`. Alles andere an M2 und M3 ist gebaut und im Log nachgewiesen.

**Nie gegen einen Kundentenant** (§13). Und: wenn dasselbe Konto gleichzeitig auf einem Tischtelefon registriert ist, verhält sich die Anlage anders — vorher sauber halten (§14.11).

## Vorbereitung

```powershell
# 1. Bauen und starten
.\build.ps1 build src\Nipp.App -c Debug -p:WindowsPackageType=None
.\src\Nipp.App\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\Nipp.App.exe

# 2. In einem zweiten Fenster das Log live mitlesen
.\tools\Watch-NippLog.ps1
```

Die Wählseite zeigt unten den Verlauf der Sitzung — für die meisten Fälle reicht das. Das Log braucht es für die Fälle, bei denen das Fenster nicht im Vordergrund ist.

---

## M2 — vier Punkte

### A1 · Eingehender Anruf
Von einem anderen Apparat die Nebenstelle **151** anrufen.

**Erwartet:** nipp wechselt selbstständig auf „Gespräch", die Schaltfläche *Annehmen* erscheint, nach dem Annehmen ist Audio in beide Richtungen da. Im Log: `Eingehender Anruf … von …`, dann `Connected`.

*Der Toast nach §8.6 kommt erst in P7 — bis dahin muss das Fenster offen sein.*

### A2 · Netzwerkwechsel (T12)
Im Gespräch **nicht** nötig: WLAN trennen und wieder verbinden, oder VPN auf- und abbauen.

**Erwartet:** im Log `Netzwerkwechsel erkannt`, zwei Sekunden später `Netzwerkzustand an das SDK gemeldet`, danach eine neue Registrierung. Der Zustand in der Navigation geht kurz auf „Registrierung läuft…" und wieder auf „Registriert".

*Die zwei Sekunden sind Absicht: Windows meldet bei einem Wechsel mehrere Ereignisse, und jedes einzeln durchzureichen sähe für die Anlage wie ein Anmeldesturm aus.*

### A3 · Standby und Resume (T13)
Rechner in den Standby, aufwecken.

**Erwartet:** `System geht in den Standby`, nach dem Aufwachen `System ist aufgewacht, Registrierung wird erneuert` und wieder `Registered`.

### A4 · 20 Anrufe in Folge
Zwanzigmal dieselbe Nummer anrufen und wieder auflegen. Geht zügig mit Enter im Nummernfeld.

**Erwartet:** kein Absturz, kein Einfrieren, und im Log **keine wachsende Zahl von Überläufen**. Ein einzelner Überlauf beim Start ist bekannt und in Ordnung; steigt die Zahl mit jedem Anruf, steckt eine blockierende Operation in einem Callback (§14.1) — dann bitte melden, das wäre ein echter Fund.

---

## M3 — drei Punkte

### A5 · Blindes Weiterleiten
Ein Gespräch annehmen, unter *Weiterleiten* ein Ziel eintragen, **Blind übergeben**.

**Erwartet:** das Ziel klingelt, das eigene Gespräch endet sofort. Im Log `weitergeleitet an … (blind)`.

### A6 · Begleitetes Weiterleiten und Makeln
1. Erstes Gespräch annehmen oder aufbauen.
2. Zurück auf *Wählen*, zweites Ziel anrufen — das erste geht automatisch auf Halten.
3. Auf *Gespräch*: beide erscheinen unter „Zwei Gespräche". **Makeln** schaltet um.
4. Beim zweiten ankündigen, dann **Begleitet übergeben**.

**Erwartet:** Makeln schaltet hörbar um (das gehaltene Gespräch ist stumm), die Übergabe verbindet die beiden anderen miteinander, nipp ist aus beiden Gesprächen draussen.

*Ein dritter Anruf muss mit einer klaren Meldung abgelehnt werden (§8.2) — gern mitprüfen.*

### A7 · Aufnahme
Im Gespräch **Aufnahme** einschalten, etwas sprechen, ausschalten, auflegen.

**Erwartet:**
- Die Warnleiste **„Aufnahme läuft"** erscheint ganz oben und ist nicht zu übersehen (§8.2 — Mitschneiden ohne Kenntnis der Gegenseite ist in der Schweiz strafbar).
- Unter `%LOCALAPPDATA%\nipp\recordings` liegt eine WAV im Schema `JJJJ-MM-TT_HHMMSS_<Nummer>.wav`.
- Die Datei lässt sich abspielen und beide Seiten sind hörbar.

---

## Nebenbei mitprüfen

Kostet nichts, wenn man ohnehin dran ist:

| Was | Erwartet |
|---|---|
| Codec- und Verschlüsselungs-Chip | zeigt den verhandelten Codec und **„unverschlüsselt"** im Klartext (ADR-007) |
| Qualitätspanel | RTT, Jitter, Verlust, Bandbreite, aktualisiert im Sekundentakt |
| Nummer `044 512 84 30` eintippen | unter dem Feld erscheint „Wird gewählt als: +41445128430" |
| Nebenstelle `151` eintippen | erscheint „Internes Ziel — wird unverändert gewählt" |
| Stumm | Gegenseite hört nichts mehr, Schaltfläche bleibt eingerastet |
| Fenster schliessen | **beendet nipp derzeit** — das Infobereich-Symbol nach §10 kommt erst in P7 |

## Ergebnisse

Bitte pro Punkt eine Zeile: **läuft** / **läuft nicht, weil …**. Ich trage sie in `docs/test-matrix.md` ein und behebe, was auffällt.

Was hier nicht steht, weil es die Emulation verfälscht: Jitter- und Speicherwerte. Die gehören nach AP7.8 auf echte x64-Hardware (T38).
