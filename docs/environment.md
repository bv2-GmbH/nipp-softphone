# Entwicklungsumgebung

Erhoben am 04.09.2026 auf der Maschine, auf der `C:\dev_claude\nipp` liegt (AP0.1 aus `docs/plans/IMPLEMENTATION-PLAN.md`).

## Befund

| Werkzeug | Erwartet laut §4 | Vorgefunden | Status |
|---|---|---|---|
| Prozessorarchitektur | x64 | **ARM64** | ⚠️ **Abweichung, siehe unten** |
| Betriebssystem | Windows 10 22H2 / 11, x64 | Windows 11, Build 10.0.26200, ARM64 | ⚠️ |
| .NET SDK | 8.0 (LTS) | **keines installiert** | ❌ fehlt |
| .NET Runtime | — | WindowsDesktop 6.0.29 und 8.0.30 | ✔️ nur Runtime, reicht nicht zum Bauen |
| Visual Studio 2022 | erforderlich | **nicht installiert** (kein vswhere) | ❌ fehlt |
| Windows App SDK Runtime | aktuelles 1.x | 1.1 bis **1.8** (8000.946.1701.0) installiert | ✔️ Runtime vorhanden |
| Git | — | 2.52.0.windows.1 | ✔️ |
| winget | — | 1.29.290 | ✔️ |

## Die Abweichung: ARM64-Entwicklungsmaschine für ein x64-Produkt

`NIPP-BUILD.md` §4 legt **x64 only** fest und schliesst ARM64 als Ziel aus — richtig, denn das linphone-sdk liefert für Windows ausschliesslich `win64`-Binaries, kein arm64-Paket. Die Entwicklungsmaschine ist aber ARM64.

Das ist kein Widerspruch, den man wegkonfigurieren kann, sondern eine Entscheidung mit Folgen:

- **Bauen und Ausführen sind möglich.** Windows 11 auf ARM64 emuliert x64-Prozesse. Ein x64-Build von nipp läuft, lädt die x64-DLL-Kette und kann telefonieren.
- **Performance-Messungen sind hier wertlos.** Die nichtfunktionalen Zielwerte aus §2 (Kaltstart < 3 s, < 180 MB Leerlauf, < 6 % CPU im Gespräch, INVITE→Toast < 400 ms) sind unter Emulation nicht aussagekräftig. AP7.8 braucht echte x64-Hardware.
- **M1 wird schwerer zu beurteilen.** Das Gate in §12 prüft, ob die native DLL-Kette lädt. Ein Fehlschlag unter Emulation beweist nicht, dass es auf echter x64-Hardware auch scheitert — und ein Erfolg beweist nicht das Gegenteil. Der Doppeltest packaged/unpackaged verliert dadurch an Beweiskraft.
- **WASAPI unter Emulation** ist der unsicherste Punkt: Audioaufnahme und -wiedergabe über einen emulierten Prozess ist der Bereich, in dem Emulation am ehesten auffällt.

**Empfehlung war:** Bau- und Codearbeit hier, aber **M1 (P2) und die Messungen in AP7.8 auf echter x64-Hardware** verifizieren.

### Entschieden am 07.09.2026: es gibt keine x64-Maschine

Die Empfehlung oben ist **nicht durchführbar** — eine x64-Maschine steht auf unbestimmte Zeit nicht zur Verfügung. Damit gilt **Option 3** aus ADR-001: alles hier, einschliesslich der Verifikation. Das ist bewusst die Variante, die ADR-001 am schwächsten bewertet hat, und sie ist als getragenes Risiko festgehalten, nicht als Lösung.

**Was dadurch ungemessen ist** — nicht „vermutlich in Ordnung", sondern ungemessen:

| Was | Stand |
|---|---|
| Kaltstart < 3 s, < 180 MB, < 6 % CPU, INVITE→Toast < 400 ms (§2) | **ungemessen.** Der einzige Wert ist 247 MB aus einem Debug-Build unter Emulation |
| Das M1-Gate | unter Emulation abgenommen; ADR-001 bestreitet dessen Beweiskraft, ADR-006 nennt es erfüllt. Der Widerspruch ist benannt, nicht aufgelöst |
| 499 ms Jitter, verworfene RTP-Pakete, WASAPI-Pufferfehler (ADR-006 Punkt 3) | **Verdacht, unbestätigt.** T38 |
| Testmatrix auf frischem Win 11 und Win 10 22H2 (AP9.5) | offen |

**Fällig vor der ersten Kundenabgabe.** Ein Softphone, dessen Audio-Timing nie auf der Zielplattform gemessen wurde, ist kein Auslieferungszustand.

**Und es braucht kein eigenes Gerät.** T38 und AP7.8 sind so beschrieben, dass sie an einem Nachmittag auf einer geliehenen x64-Maschine abzuarbeiten sind — ein Kundengerät bei einer Installation, ein Testrechner. Wer einmal Zugang hat, sollte ihn dafür benutzen.

## Was zum Bauen fehlt

1. **.NET 8 SDK** — auf ARM64-Windows gibt es beide Varianten. Welche installiert wird, hängt an der Entscheidung oben:
   - *x64-SDK:* alles läuft emuliert, dafür ist die Umgebung der Zielplattform am nächsten.
   - *ARM64-SDK:* nativ schnell, baut x64-Ziele per `RuntimeIdentifier=win-x64` per Cross-Build; das Ausführen des x64-Ergebnisses läuft trotzdem emuliert.
2. **Visual Studio 2022** mit den Workloads „.NET-Desktopentwicklung" und „Windows-Anwendungsentwicklung", oder ersatzweise Build Tools plus Windows SDK 10.0.19041+. Für WinUI 3 wird zusätzlich die **Windows App SDK C#-Projektvorlagen**-Komponente gebraucht.

Beides sind Systeminstallationen und werden nicht ohne Freigabe durchgeführt.

## Ergebnis AP0.3 — SDK-Beschaffung geklärt

Am 04.09.2026 von dieser Maschine aus geprüft:

| Prüfung | Ergebnis |
|---|---|
| `https://gitlab.linphone.org/api/v4/projects/411/packages/nuget/index.json` | **HTTP 200, anonym lesbar**, gültiger NuGet-Service-Index v3.0.0 |
| Pakete im Feed | `LinphoneSDK`, **`LinphoneSDK.Windows`**, `LinphoneSDK.Xamarin` — `LinphoneSDK.Dotnet` gibt es nicht (404), wie in §5 vermerkt |
| `LinphoneSDK.Windows` Versionen | 300 gesamt, 59 stabil — **`5.5.18` als stabile Version vorhanden** |
| `https://download.linphone.org/releases/windows/sdk/` | HTTP 200, erreichbar |

**Damit ist Weg A (NuGet) bestätigt und wird der Standard**, nicht Weg C. Der Feed ist ohne Token lesbar und führt genau die Version, die §5 vorgibt. Weg C (Prebuilt-ZIP) bleibt die Rückfallebene — er ist inhaltlich verifiziert und kostet nichts, wenn Weg A stockt.

Zwei Beobachtungen fürs Protokoll:
- Die GitLab-Verbindung ist **unzuverlässig**: mehrere Abrufe liefen in Timeouts, erst Wiederholungen kamen durch. Für den Build heisst das: NuGet-Restore kann sporadisch scheitern. Wenn das im Alltag stört, ist das Prebuilt-ZIP der ruhigere Weg.
- Der `.nuspec`-Endpunkt der GitLab-Registry antwortet mit 404. Der Paketinhalt (Zielframework-Moniker, Wrapper, DLL-Layout) ist damit erst beim tatsächlichen Restore in P2 zu sehen — der Moniker-Widerspruch aus §5 (`win` vs. `netcore45`) bleibt bis dahin offen.
