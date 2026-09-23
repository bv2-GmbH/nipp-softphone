# Vermitteln — ein Weg für beide Arten

> **Stand 23.09.2026, 21:55: gebaut.** Der Umbau ist umgesetzt und in
> **ADR-073** festgehalten — «Zuerst anrufen» ruft das ausgewählte Ziel an,
> «Jetzt übergeben» erscheint, sobald das zweite Gespräch steht, und die
> `InfoBar` mit dem Umweg ist entfallen. **Am Gerät abgenommen ist nichts
> davon:** T321 bis T323 stehen offen. Von den vier offenen Entscheidungen
> unten sind zwei getroffen (ADR ja; der Rückweg bleibt bei `PlaceCallAsync`),
> zwei offen: **externe Ziele** und **was die Vorschlagsliste anbietet**.

**Angelegt am 23.09.2026**, am Abend des ersten Tages an der Anlage. Auftrag
von Dominic, nachdem das begleitete Vermitteln im echten Betrieb zwar
funktioniert hat, aber über einen Umweg, den niemand von selbst findet.

Bezug: NIPP-BUILD.md §8.2 (Weiterleiten, blind und begleitet),
`docs/test-matrix.md` T07, T08, T320, `docs/stand.md` (23.09.2026, abends).

---

## Der Auftrag, im Wortlaut

> Das Handling für das Vermitteln gefällt mir nicht so recht. Für
> unattendant Vermittlung ist das super. Für attendant möchte ich das gleich
> haben. Bin im Call, klicke auf weiterleiten, suche mir den internen
> Teilnehmer aus und hab dann die Wahl direkt weiterleiten oder zuerst
> anrufen. Wenn ich zuerst anrufen wähle, kann ich dann nach dem Gespräch den
> Anruf per Button übergeben/weiterleiten.

**Das ist kein neues Feature.** §8.2 verlangt beide Arten, und beide sind
gebaut. Was sich ändert, ist der Weg dorthin — und zwar für **eine** der
beiden.

## Was heute dasteht — gemessen, nicht vermutet

Gelesen am 23.09.2026 in `ActiveCallPage.xaml` (Zeilen um 690–750) und
`ActiveCallViewModel.cs`:

Der Weiterleiten-Bereich der Gesprächsansicht hat **schon alles, was der
Auftrag verlangt** — bis auf einen Schritt:

| Was | Stand |
|---|---|
| Eingabefeld für das Ziel | **da** |
| Vorschlagsliste mit den internen Teilnehmern, **mit Präsenzpunkt** | **da** |
| Knopf **«Sofort abgeben»** (`TransferBlindCommand`) | **da, und er tut genau das Richtige** |
| Knopf **«Erst ankündigen»** (`TransferAttendedCommand`) | **da — aber er ruft das gewählte Ziel nicht an** |
| Knopf zum Übergeben, wenn das zweite Gespräch steht | **fehlt** (es ist derselbe Knopf) |

**Die eine Stelle, an der es hakt:** `CanTransferAttended => Calls.Count > 1`.
Der Knopf ist grau, solange nur ein Gespräch läuft, und die `InfoBar` daneben
sagt, was zu tun ist:

> «Für die begleitete Übergabe zuerst ein zweites Gespräch aufbauen: oben
> zurück zur Wähltastatur, das Ziel anrufen, ankündigen. Dann hier übergeben.»

Der Benutzer hat also gerade ein Ziel ausgewählt — und wird aufgefordert, die
Ansicht zu verlassen, das Ziel **noch einmal** zu suchen und selbst zu wählen.
**Die Auswahl, die er eben getroffen hat, wird dabei nicht benutzt.**

`TransferAttendedAsync` ruft `TransferAsync(call.Handle, string.Empty,
TransferMode.Attended)` — mit **leerer** Zieladresse. Der Dienst sucht sich
das Ziel selbst, nämlich das zweite bestehende Gespräch:

```csharp
var other = _calls.Values
    .FirstOrDefault(c => c.Handle != callHandle
        && c.Info.Status is CallStatus.Connected or CallStatus.OnHold)
    ?? throw new InvalidOperationException("Begleitetes Weiterleiten braucht …");
```

**Der Dienst ist damit richtig gebaut und bleibt, wie er ist.** Er setzt zwei
bestehende Gespräche zusammen, und das ist genau, was SIP an dieser Stelle
tut. Was fehlt, liegt **darüber**: niemand baut das zweite Gespräch auf.

## Was gebaut werden soll

Ein Ablauf, drei Schritte, ohne die Ansicht zu verlassen:

```
Im Gespräch → «Weiterleiten» → Ziel aussuchen (Liste mit Präsenz)
                                      │
                    ┌─────────────────┴─────────────────┐
              «Sofort abgeben»                   «Zuerst anrufen»
              (wie heute, blind)                        │
                                          erstes Gespräch auf Halten,
                                          das Ziel wird angerufen
                                                        │
                                              ankündigen, sprechen
                                                        │
                                              «Jetzt übergeben»
```

**Die Knöpfe heissen nach dem, was sie tun** (ADR-044, W1.6): «Sofort abgeben»
bleibt. «Erst ankündigen» wird zu **«Zuerst anrufen»** — denn das ist der
nächste Schritt, nicht die Absicht. Der Knopf, der danach erscheint, heisst
**«Jetzt übergeben»**.

**Der dritte Schritt braucht keinen eigenen Ort.** Steht das zweite Gespräch,
ist die Gesprächsansicht ohnehin auf zwei Gespräche eingestellt (Makeln, §8.2);
der Übergabeknopf gehört dorthin, wo die beiden Gespräche stehen.

## Was das berührt

| Datei | was |
|---|---|
| `ActiveCallViewModel.cs` | `TransferAttendedAsync` baut künftig den zweiten Anruf auf, statt eine Ausnahme zu fangen. `CanTransferAttended` trennt sich in zwei Bedingungen: **anrufen** kann man mit einem Ziel und einem Gespräch, **übergeben** erst mit zweien |
| `ActiveCallPage.xaml` | der zweite Knopf wechselt Beschriftung und Aufgabe; die `InfoBar` mit dem Umweg **entfällt** |
| `ShellViewModel.DialAsync` | **nicht** anfassen. Der zweite Anruf geht über den Dienst, nicht über die Wähltastatur — sonst hängt die Übergabe an einer Ansicht, die im Gespräch gar nicht sichtbar ist |
| `SipService.TransferAsync` | **unverändert.** Er setzt zwei Gespräche zusammen und tut das richtig |

## Was noch entschieden werden muss

1. **Braucht es einen ADR?** §8.2 verlangt beide Arten und bekommt beide. Der
   Weg ändert sich, die Fähigkeit nicht. **Vorschlag: ja, ein kurzer** — weil
   die heutige Aufteilung («der Benutzer baut das zweite Gespräch selbst auf»)
   eine bewusste Entscheidung war, die hier zurückgenommen wird, und weil
   `CanTransferAttended` sonst beim nächsten Lesen wieder wie ein Fehler
   aussieht.
2. **Was passiert, wenn das Ziel nicht abnimmt?** Der Rückweg muss stehen:
   zweites Gespräch beenden, erstes zurückholen. Das erste Gespräch darf dabei
   **nicht** stumm auf Halten liegenbleiben — dieselbe Falle, die
   `PlaceCallAsync` mit `pausedForThisCall` schon einmal gelöst hat.
3. **Darf «Zuerst anrufen» ein externes Ziel wählen?** Der Auftrag nennt den
   **internen** Teilnehmer. Technisch spricht nichts dagegen; die Frage ist,
   ob die Vorschlagsliste im Weiterleiten-Feld externe Nummern überhaupt
   anbietet.
4. **Was, wenn schon zwei Gespräche laufen?** Dann ist «Zuerst anrufen» nicht
   möglich (§8.2) — und der Grund gehört an den Knopf, nicht in eine Meldung
   nach dem Klick. **Das ist die Stelle, an der der 23.09.2026 hängengeblieben
   ist:** es gibt heute drei verschiedene Zwei-Gespräche-Texte, und nur zwei
   davon hinterlassen eine Spur.

## Prüfzeilen

In `docs/test-matrix.md` eingetragen (dieselbe Regel wie überall: der Plan
begründet eine Zeile, die Matrix ist der Ort, an dem sie steht):

- **T321** — der ganze Ablauf: im Gespräch weiterleiten, Ziel aussuchen,
  «Zuerst anrufen», ankündigen, «Jetzt übergeben». Das Ziel spricht danach mit
  dem ursprünglichen Anrufer, und nipp hat **kein** Gespräch mehr.
- **T322** — das Ziel nimmt **nicht** ab: zweites Gespräch beenden, das erste
  ist zurück und **hörbar**, nicht stumm auf Halten.
- **T323** — «Sofort abgeben» tut unverändert, was es heute tut (die
  Gegenprobe: diese Etappe darf T07 nicht kosten).

## Reihenfolge

Nach dem Gerätetag, nicht mitten hinein. Die Etappe fasst die Gesprächsansicht
an, und die steht gerade in der Abnahme (T313–T317, `SHELL-IM-GESPRAECH-PLAN.md`).
**Wer beides gleichzeitig ändert, weiss bei jedem Befund nicht, woher er kommt.**
