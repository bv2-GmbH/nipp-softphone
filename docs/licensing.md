# Lizenzlage

**Status: nipp steht unter der AGPLv3.** Entschieden am 11.09.2026 als Teil von
**ADR-040**; die Lizenz liegt als `LICENSE` im Repo, die Kurzfassung als
`NOTICE`.

**Vollzogen ist damit die Wahl, nicht der Schritt.** Der Quelltext liegt noch in
einem privaten Repo; öffentlich wird er mit dem Repo-Wechsel (`docs/plans/RELEASE-PLAN.md`
R10). Bis dahin gilt: die Lizenz steht, das Repo ist vorbereitet, und was noch
fehlt, steht unten unter „Was bis zum Öffentlichmachen noch zu tun ist".

---

## Warum AGPLv3 — und was die Alternative gewesen wäre

Das linphone-sdk ist **dual lizenziert: AGPLv3 oder eine kostenpflichtige
proprietäre Lizenz** von Belledonne Communications SARL. Ein Closed-Source-
Client, der an Kunden ausgeliefert wird, ist mit AGPLv3 nicht vereinbar — es sei
denn, der komplette Quelltext von nipp wird offengelegt.

Drei Wege standen offen, und zwei sind verworfen:

| Weg | Stand |
|---|---|
| **Kommerzielle Lizenz kaufen** | verworfen. Kosten unbekannt, keine öffentliche Preisliste, Verhandlung mit Belledonne nötig — und der einzige Gewinn wäre, den Quelltext nicht zeigen zu müssen |
| **Nur intern einsetzen** | war der Zustand bis zum 11.09.2026. Er hielt die Frage offen, statt sie zu beantworten, und sperrte jede Abgabe ausser Haus |
| **AGPLv3 offenlegen** | **gewählt.** Kostenlos, sofort wirksam, und es beantwortet die Frage endgültig |

**Was das kostet, offen gesagt:** der Quelltext ist öffentlich, einschliesslich
der Provisionierungslogik. Was er **nicht** mehr enthält, ist die Anbindung an
die bv2-eigenen Systeme — sie ist seit ADR-040 keine Codestelle mehr, sondern
eine importierbare Vorlagendatei, die im privaten Vorlagen-Repo liegt. Damit
senkt die Lösung den Preis der Offenlegung, statt ihn nur zu tragen.

**Die Netzwerk-Klausel der AGPLv3** zielt auf Software, mit der Dritte über ein
Netzwerk interagieren. Ein Softphone auf dem Arbeitsplatz eines Mitarbeiters
ist das nicht — bei einer künftigen Server- oder Mandantenkomponente wäre es zu
prüfen.

---

## Was bis zum Öffentlichmachen noch zu tun ist

Die vollständige Liste steht in `docs/plans/RELEASE-PLAN.md` (R10). Kurz:

1. **Geheimnisse über alle Commits prüfen** (`gitleaks` oder `trufflehog`) —
   nicht nur über den Arbeitsbaum.
2. **`README.md` für Fremde**: was nipp ist, wie es gebaut wird, wie das SDK
   beschafft wird, unter welcher Lizenz es steht.
3. ~~**Der SDK-Quelltext als Spiegel**~~ — **erledigt am 14.09.2026, und zwar
   ohne Spiegel.** nipp verändert das SDK nicht; es wird als unveränderte
   Fassung von Belledonne eingebunden. Damit ist deren Veröffentlichung der
   entsprechende Quelltext, und ein Verweis darauf genügt (GPLv3 §6(d):
   «an einem benannten Ort»). Version, Quelltext-Ort und Binärpaket stehen
   in `NOTICE`; beide Orte waren am 14.09.2026 erreichbar.
   **Der Verweis trägt nur, solange das SDK unverändert bleibt** — wer es
   selbst baut (Weg B in `docs/sdk-setup.md`), muss den veränderten
   Quelltext mitliefern.
4. **Der Repo-Wechsel selbst.** Die Update-Quelle ist eine Konstante im Code
   (`VelopackUpdateGateway.RepositoryUrl`, dazu `build/Release-Nipp.ps1`): ein
   Repo unter neuem Namen heisst, dass installierte Arbeitsplätze weiter im
   alten suchen.

---

## Lizenzen der übrigen Abhängigkeiten

Das linphone-sdk ist der einzige kritische Fall. Alle anderen Pakete sind permissiv lizenziert und stehen einer künftigen kommerziellen Auslieferung nicht im Weg:

| Paket | Lizenz | Wofür |
|---|---|---|
| CommunityToolkit.Mvvm, Microsoft.Extensions.*, Microsoft.Data.Sqlite, Microsoft.WindowsAppSDK, System.Security.Cryptography.ProtectedData | MIT | Grundgerüst, DI, Protokollierung, Anrufliste, DPAPI |
| Serilog samt Sinks | Apache-2.0 | Protokolldateien |
| H.NotifyIcon.WinUI | MIT | Infobereich-Symbol |
| xunit, NSubstitute, coverlet | Apache-2.0 / BSD-3 / MIT | nur Tests, nicht ausgeliefert |
| **JsonPath.Net** 3.0.2 und **Json.More.Net** 3.0.1 (json-everything) | **MIT** | JSONPath für die Integrationsplattform (ADR-016) |

**Regel für neue Abhängigkeiten:** permissiv (MIT, Apache-2.0, BSD) oder gar nicht. Eine zweite Copyleft-Abhängigkeit neben dem SDK würde die kaufmännische Frage unnötig verdoppeln. Vor der Aufnahme eines Pakets die Lizenz prüfen und hier eintragen.

---

## Kein technischer Ausweg

Geprüft am 04.09.2026:

- **PJSIP / pjsua2** ist GPLv2+ oder kommerziell (licensing@teluu.com). Verschiebt das Copyleft nur zu einem anderen Anbieter, löst es nicht.
- **SIPSorcery** ist BSD-3 mit Zusatzklausel und damit closed-source-taugliche — hat aber **keine echte Echounterdrückung** und kein vergleichbares Codec-Ökosystem. Für ein produktives Softphone mit den Audioanforderungen aus §9.4 nicht ausreichend.

Die Frage ist kaufmännisch zu klären, nicht technisch zu umgehen.

## Kontaktweg

- `https://www.linphone.org/en/contact/`
- Developer-Formular: `https://linphone.typeform.com/to/kCg6gOWV`
- Belledonne Communications SARL, Grenoble · +33 9 52 63 65 05

## Build-Schalter mit Lizenzwirkung

Beide stehen im SDK richtig und dürfen beim Selbstbau (Weg B) nicht umgestellt werden:

| Schalter | Stand | Wirkung beim Einschalten |
|---|---|---|
| `ENABLE_GPL_THIRD_PARTIES` | **NO** | zieht GPL-Komponenten von Dritten hinein |
| `ENABLE_NON_FREE_FEATURES` | **OFF** | aktiviert AMR und H.264, beide mit eigenen Lizenzfragen |

Ausserdem bleibt **G.729** (bcg729) deaktiviert — §3 und §9.5. Die Kernpatente sind abgelaufen, aber ein nicht gebrauchter Codec gehört nicht in den Build.

## Stand der Punkte

- [x] **Entscheidung getroffen am 04.09.2026: vorerst nur intern.** Abgelöst.
- [x] **Entscheidung getroffen am 11.09.2026: AGPLv3, Quelltext offengelegt**
  (ADR-040). `LICENSE` und `NOTICE` liegen im Repo.
- [ ] Repo öffentlich gemacht am: _______ (`docs/plans/RELEASE-PLAN.md` R10)
- [x] **SDK-Quelltext: kein Spiegel nötig** — Verweis auf Belledonne in `NOTICE`, entschieden am 14.09.2026 (unverändertes SDK, GPLv3 §6(d))

**Eine Anfrage an Belledonne ist damit nicht mehr nötig.** Der Kontaktweg unten
bleibt für den Fall stehen, dass jemand die Entscheidung eines Tages umdrehen
will — dann ist sie zuerst wieder aufzurollen.
