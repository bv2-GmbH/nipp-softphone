# Fremde Bestandteile

nipp verwendet die folgenden Komponenten. Diese Datei liegt dem Paket bei und
wird bei jeder neuen Abhängigkeit ergänzt — die Regel dafür steht in
`docs/licensing.md`: **permissiv (MIT, Apache-2.0, BSD) oder gar nicht.**

Eine Ausnahme gibt es, und sie ist die wichtigste Zeile hier: das linphone-sdk
steht unter **AGPLv3**. Was daraus folgt, steht unter „Der Sonderfall" weiter
unten.

---

## Laufzeitbestandteile

| Komponente | Version | Lizenz | Wofür |
|---|---|---|---|
| **linphone-sdk** (liblinphone, mediastreamer2, belle-sip, ortp, belr, bctoolbox) | 5.5.18 | **AGPLv3** oder kommerziell | SIP, Medien, Codecs, Echounterdrückung |
| Microsoft.WindowsAppSDK / WinUI 3 | 2.4.0 | MIT | Oberfläche, Anwendungslebenszyklus |
| Microsoft.Extensions.DependencyInjection, .Logging, .Options, .Primitives | 8.x | MIT | Grundgerüst |
| Microsoft.Data.Sqlite | 8.x | MIT | Anrufliste (`history.db`) |
| System.Security.Cryptography.ProtectedData | 8.x | MIT | DPAPI-Ablage der Geheimnisse |
| CommunityToolkit.Mvvm | 8.x | MIT | ViewModels |
| H.NotifyIcon.WinUI | 2.2.0 | MIT | Symbol im Infobereich |
| JsonPath.Net, Json.More.Net (json-everything) | 3.0.2 / 3.0.1 | MIT | JSONPath der Integrationsplattform (ADR-016) |
| Serilog, Serilog.Extensions.Logging, Serilog.Sinks.File | 4.2.0 / 8.0.0 / 6.0.0 | Apache-2.0 | Protokolldateien |
| **Velopack** | 1.2.0 | MIT | Installer und Update-Verteilung (ADR-038, ADR-039) |
| Microsoft.Win32.SystemEvents | 8.x | MIT | Erscheinungsbild und Energiezustand des Systems |
| .NET 8 Runtime (mitgeliefert, self-contained) | 8.x | MIT | Laufzeit |

Nur in Tests, **nicht ausgeliefert**: xunit (Apache-2.0), NSubstitute (BSD-3),
coverlet (MIT), Microsoft.Extensions.TimeProvider.Testing (MIT).

---

## Der Sonderfall: linphone-sdk

Das SDK ist **dual lizenziert** — AGPLv3 oder eine kostenpflichtige proprietäre
Lizenz von Belledonne Communications SARL, Grenoble. nipp linkt dagegen und
liefert die nativen Bibliotheken mit.

**Stand heute (04.09.2026, bestätigt am 07.09.2026): nipp läuft ausschliesslich
intern bei bv2 und wird nicht weitergegeben.** Copyleft greift bei der
Weitergabe; eine interne Installation ist keine. Deshalb ist heute nichts
offenzulegen.

**Das ändert sich in dem Moment, in dem ein Setup das Haus verlässt** — an einen
Kunden, einen Auftragnehmer oder eine Tochtergesellschaft. Dann gilt eines von
beidem:

- der vollständige Quelltext von nipp wird unter AGPLv3 angeboten, oder
- es liegt eine kommerzielle Lizenz von Belledonne vor.

Der Weg dorthin steht in `docs/plans/RELEASE-PLAN.md` (R10) und `docs/licensing.md`. **Wer
ein Setup weitergeben will, geht zuerst dorthin.**

Bezugsquelle und Prüfsumme des verwendeten SDK: `docs/sdk-setup.md`.
Kontakt für die kommerzielle Lizenz: `https://www.linphone.org/en/contact/`.

**Build-Schalter mit Lizenzwirkung**, beide stehen richtig und dürfen beim
Selbstbau nicht umgestellt werden: `ENABLE_GPL_THIRD_PARTIES` = **NO**,
`ENABLE_NON_FREE_FEATURES` = **OFF**. G.729 (bcg729) bleibt deaktiviert.

---

## Lizenztexte

Die vollständigen Texte liegen bei den Paketen selbst (NuGet-Cache
beziehungsweise `sdk/extracted/linphone-sdk/`) und sind öffentlich abrufbar:

- MIT: `https://opensource.org/license/mit`
- Apache-2.0: `https://www.apache.org/licenses/LICENSE-2.0`
- BSD-3-Clause: `https://opensource.org/license/bsd-3-clause`
- AGPL-3.0: `https://www.gnu.org/licenses/agpl-3.0.txt`

© 2026 bv2 GmbH. nipp selbst ist **nicht** unter einer freien Lizenz
veröffentlicht — siehe oben.
