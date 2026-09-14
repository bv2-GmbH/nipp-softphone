# Paket und Auslieferung

Wie aus dem Quellcode ein installierbares nipp wird (P9, §16).

> **Seit dem 07.09.2026 abends wird nicht mehr MSIX ausgeliefert, sondern ein
> Velopack-Setup** (ADR-038). Der Weg dorthin steht in **`docs/updates.md`**;
> das Skript heisst `build\Release-Nipp.ps1`.
>
> **Diese Datei bleibt gültig** — für den MSIX-Weg, der weiterhin gebaut wird
> und auf zwei Dinge wartet: **T110** (packaged bekommt keine Toasts) und
> **AP9.2** (Zertifikat; ohne es lässt sich ein MSIX beim Kunden gar nicht
> installieren). Für Intune-Umgebungen (§16.3) ist MSIX dann wieder die bessere
> Wahl.
>
> Alles unten über **Symbole, das gemeinsame Ausgabeverzeichnis und die
> Registrierung** gilt unverändert für beide Wege.

## Kurzfassung

```powershell
.\build\Pack-Nipp.ps1 -Version 1.0.0.0
```

Das Ergebnis liegt unter `dist\` als signiertes `.msix`.

Das Skript tut vier Dinge: Version ins Manifest schreiben, Release bauen und paketieren, **prüfen, dass die native Linphone-Kette vollständig im Paket liegt**, und signieren.

Der dritte Punkt ist der wichtige. §14.2 nennt genau diese Bruchstelle: eine fehlende native DLL fällt erst beim ersten Start auf dem Zielrechner auf, mit einer Meldung, die nicht sagt, welche fehlt. Das Skript öffnet das fertige Paket und sieht nach — sechs Kernbibliotheken und die `belr`-Grammatiken unter `share\belr\grammars`. Fehlt etwas, bricht es ab, statt ein Paket auszuliefern, das nicht startet.

## Warum MSIX

Entschieden in [ADR-008](decisions.md). Kurz: Paketidentität. Ohne sie funktionieren drei Dinge nicht, die §8 und §10 verlangen — Toasts über den `AppNotificationManager`, Single-Instance über `AppInstance.FindOrRegisterForKey`, und Protokoll-Handler, die sich bei der Deinstallation sauber zurückbauen.

Unpackaged läuft nipp trotzdem; `WindowsIntegration` legt Autostart und Protokolle dann über `HKCU` an. Das ist die Rückfallebene zum Entwickeln, nicht die Auslieferung.

## Signatur

**Zum Entwickeln** legt das Skript selbstsigniert an. Auf dem Testrechner muss das Zertifikat dann unter „Vertrauenswürdige Personen" liegen:

```powershell
# Auf der Baumaschine exportieren
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq 'CN=bv2 GmbH'
Export-Certificate -Cert $cert -FilePath nipp-dev.cer

# Auf dem Testrechner, als Administrator
Import-Certificate -FilePath nipp-dev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

**Für die Auslieferung** braucht es ein echtes Code-Signing-Zertifikat (§16.2, AP9.2):

```powershell
.\build\Pack-Nipp.ps1 -Version 1.0.0.0 -CertificateThumbprint <Fingerabdruck>
```

> Der `Publisher` im Manifest und der Antragsteller im Zertifikat müssen **buchstabengleich** sein. Stimmen sie nicht überein, lehnt Windows die Installation ab — mit einer Meldung, die den Grund nicht nennt. Das ist der häufigste Fehler beim ersten Signieren.

## Versionsschema

Vierteilig, `Haupt.Neben.Korrektur.Build`, wie MSIX es verlangt. Der letzte Teil ist bei einer Auslieferung `0`; MSIX vergleicht Versionen numerisch, ein niedrigerer Wert lässt sich nicht als Update installieren.

Die Version steht **nur** im Manifest und wird vom Skript dorthin geschrieben. Zwei Stellen, die dieselbe Version führen, laufen auseinander.

## Installieren

### Einzeln

```powershell
Add-AppxPackage -Path nipp_1.0.0.0_x64.msix
```

### Unbeaufsichtigt verteilen (AP9.4)

Über Intune, SCCM oder ein Anmeldeskript. Für ein Skript:

```powershell
Add-AppxPackage -Path \\server\software\nipp\nipp_1.0.0.0_x64.msix -ForceApplicationShutdown
```

`-ForceApplicationShutdown` beendet ein laufendes nipp, bevor es ersetzt wird. Ohne das schlägt ein Update fehl, solange jemand die App offen hat — und offen ist sie den ganzen Tag.

Für alle Benutzer eines Rechners (braucht Administratorrechte):

```powershell
Add-AppxProvisionedPackage -Online -PackagePath nipp_1.0.0.0_x64.msix -SkipLicense
```

### Auslieferungszustand mitgeben

Die Datei `build\nipp-factory.xml` gehört bei der Verteilung nach

```
%PROGRAMDATA%\bv2\nipp\nipp-factory.xml
```

Sie wird bei jedem Start gelesen und legt Codecs, Ports und Standardverhalten fest. Konten enthält sie nicht — die kommen aus dem Kundenprofil, siehe [provisioning.md](provisioning.md).

Ein MSIX kann keine Dateien nach `%PROGRAMDATA%` schreiben; das muss das Verteilskript tun:

```powershell
$target = Join-Path $env:ProgramData 'bv2\nipp'
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item \\server\software\nipp\nipp-factory.xml $target -Force
```

## Firewall (AP7.7, §14.5)

Das Manifest deklariert `internetClient` und `privateNetworkClientServer` — das ist, was nipp braucht: SIP nach aussen, RTP im lokalen Netz.

Windows fragt trotzdem beim ersten Start nach der Freigabe, und ein Benutzer ohne Administratorrechte kann sie nicht erteilen. **Bei einer verwalteten Verteilung deshalb vorab freigeben:**

```powershell
New-NetFirewallRule -DisplayName 'nipp (SIP/RTP)' `
    -Direction Inbound -Action Allow -Profile Domain,Private `
    -Program '%ProgramFiles%\WindowsApps\bv2.nipp_1.0.0.0_x64__<Hash>\Nipp.App.exe'
```

Der Pfad enthält den Paket-Hash und ändert sich mit jeder Version. Verlässlicher ist eine Regel auf die Ports:

```powershell
New-NetFirewallRule -DisplayName 'nipp RTP' `
    -Direction Inbound -Action Allow -Protocol UDP -LocalPort 7078-7178 `
    -Profile Domain,Private
```

Der Portbereich stammt aus §9.2 und lässt sich im Profil ändern (`network.rtp-port-min` / `-max`) — dann gehört die Regel mit angepasst.

## Packaged testen — der Prüfschritt, der in M1 gefehlt hat

Eine packaged App wird über ihre **Paketidentität** gestartet. Ein Aufruf der EXE oder `dotnet run` scheitert mit `REGDB_E_CLASSNOTREG`, weil die WinRT-Klassen der Runtime ohne Identität nicht aktivierbar sind — das sieht wie ein Konfigurationsfehler aus, ist aber nur der falsche Test (ADR-008).

```powershell
# 1. Voraussetzung: Entwicklermodus, sonst 0x80073CFF
#    HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock
#    AllowDevelopmentWithoutDevLicense = 1

# 2. Registrieren (Layout aus dem Build, keine Signatur nötig)
Add-AppxPackage -Register `
  src\Nipp.App\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\AppxManifest.xml

# 3. Starten
Start-Process "shell:appsFolder\bv2.nipp_b06ws4ca5ehbg!App"

# 4. Wieder entfernen
Get-AppxPackage -Name bv2.nipp | Remove-AppxPackage
```

**Nach jeder Änderung an der DLL-Liste gegenprüfen**, dass die native Kette im registrierten Layout liegt — der Fallstrick aus §14.3 ist umgangen, nicht verschwunden:

```powershell
$out = "src\Nipp.App\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64"
Test-Path "$out\liblinphone.dll"
Test-Path "$out\lib\mediastreamer\plugins\libmswasapi.dll"
(Get-ChildItem "$out\share\belr\grammars" -Filter *.belr).Count   # muss 8 sein
```

## Ein neues Logo wird nicht sichtbar — die drei Stellen, die es festhalten

Belegt am 07.09.2026: nach „Neues Logo in allen sechzehn Rollen" (391c8db)
zeigte die **angepinnte** App in der Taskleiste weiter das alte. Im Code war
alles richtig — die gebaute EXE enthielt alle sieben Stufen des neuen
`AppIcon.ico` byteidentisch. Festgehalten wurde es an drei anderen Stellen:

**1. Der Symbolcache des Explorers.** Ein Pin unter
`%AppData%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar`
ist eine `.lnk` mit `IconLocation = ,0` — sie zeigt also auf das Symbol der
EXE, aber Explorer liest es nicht neu, solange der Pfad gleich bleibt.

```powershell
# Pin lösen (von Hand in der Taskleiste), dann:
ie4uinit.exe -show
Stop-Process -Name explorer -Force   # Explorer startet selbst neu
# und neu anpinnen
```

**2. Ein verwaistes registriertes Paket.** `Get-AppxPackage bv2.nipp` zeigte
auf `…\win-x64` — ein Verzeichnis, in dem inzwischen **kein
`AppxManifest.xml` mehr lag**, weil der letzte Build unpackaged war. Windows
behält in diesem Zustand seine Kopie der Kachelbilder aus der Zeit der
Registrierung, und die stammte aus einem Build, in dem `Assets\` leer war.

```powershell
Get-AppxPackage -Name bv2.nipp | Select-Object InstallLocation
Test-Path "$($(Get-AppxPackage -Name bv2.nipp).InstallLocation)\AppxManifest.xml"
# Fehlt es: entweder packaged neu bauen und neu registrieren
#           oder das Paket entfernen (Get-AppxPackage … | Remove-AppxPackage)
```

**3. Fehlende Zielgrössen.** Für eine angepinnte packaged App liest Windows
nicht `Square44x44Logo.png`, sondern
`Square44x44Logo.targetsize-<N>_altform-unplated.png`. Davon lag lange nur die
**24er** im Paket, weshalb Windows für einen 32-Pixel-Platz hochskalierte.
`tools\Build-Icons.py` erzeugt die Familie seit dem 07.09.2026 vollständig.

**Beides erledigt `tools\Reset-IconCache.ps1`** — es entfernt eine verwaiste
Registrierung, leert den Symbolcache und startet den Explorer neu. **nipp danach
neu starten:** sein Symbol im Infobereich kommt nach einem Explorer-Neustart
nicht von selbst zurück (T97).

**Reihenfolge, wenn ein Symbol nicht nachkommt:** zuerst prüfen, **wie**
gestartet wurde (packaged über `shell:appsFolder`, unpackaged über die EXE) —
davon hängt ab, welche der drei Stellen überhaupt zuständig ist.

## Unpackaged immer mit `-t:Rebuild` bauen

Kostete am 04.09.2026 zweimal Fehlersuche. Das Fehlerbild sieht aus wie ein Codefehler, ist aber ein Buildartefakt:

```
System.TypeInitializationException: The type initializer for '<Module>' threw an exception.
 ---> System.Runtime.InteropServices.COMException (0x80040154) REGDB_E_CLASSNOTREG
   at ...DeploymentManagerAutoInitializer.cs:line 44
   at .cctor()
```

**Was beobachtet wurde, zweimal reproduziert:** Ein **inkrementeller** Build mit `-p:WindowsPackageType=None` erzeugt eine App, die beim Start im Modul-Initialisierer stirbt — er ruft den **DeploymentManager** (packaged) statt des **Bootstrappers** (unpackaged). Ein `-t:Rebuild` mit denselben Parametern erzeugt eine App, die läuft. Beim ersten Mal war vorher packaged gebaut worden, beim zweiten Mal nicht — der Variantenwechsel ist also nicht die einzige Bedingung.

**Was nicht belegt ist:** warum der inkrementelle Build die falsche Variante behält. Der generierte Initialisierer liegt nicht als sichtbare `.g.cs` im `obj`, sondern kommt aus dem NuGet-Paket `Microsoft.WindowsAppSDK.Foundation`; welche Bedingung MSBuild dort auswertet, wurde nicht weiter verfolgt. Die Regel unten wirkt zuverlässig, die Ursache ist nicht abschliessend geklärt.

**Abhilfe:** unpackaged immer mit `-t:Rebuild`. Kostet etwa 15 Sekunden mehr und spart die Suche.

```powershell
# unpackaged
.\build.ps1 build src\Nipp.App -c Debug -p:WindowsPackageType=None -t:Rebuild

# packaged
.\build.ps1 build src\Nipp.App -c Debug -t:Rebuild
```

**Wie man die Ausnahme überhaupt zu sehen bekommt:** `dotnet run` gibt sie auf der Konsole aus. Ein Start der EXE nicht — dort bleibt nur der generische .NET-Fehlercode. Das ist der schnellste Weg zur Ursache, wenn nipp wortlos verschwindet.

## Aktualisieren

`Add-AppxPackage` mit einer höheren Version ersetzt die bestehende Installation. Einstellungen unter `%APPDATA%\nipp` und die Anrufliste unter `%LOCALAPPDATA%\nipp` bleiben erhalten — sie liegen ausserhalb des Pakets.

Eine automatische Update-Prüfung in der App gibt es nicht (§16.4 ist offen). Die Verteilung übernimmt, wer auch die anderen Anwendungen verteilt.

## Vor der Auslieferung prüfen (AP9.5)

Auf einem **frischen** Windows 11 und einem Windows 10 22H2, nicht auf der Baumaschine:

- [ ] Installation ohne Administratorrechte (nach Import des Zertifikats)
- [ ] Erster Start: Fenster erscheint, Symbol im Infobereich da
- [ ] Konto einrichten, REGISTER kommt durch
- [ ] Ausgehendes Gespräch mit Audio in beide Richtungen
- [ ] Eingehendes Gespräch: Toast erscheint, auch bei geschlossenem Fenster
- [ ] `tel:`-Link aus dem Browser wählt direkt
- [ ] Zweiter Programmstart aktiviert die erste Instanz
- [ ] Neustart: Autostart greift, nipp startet minimiert
- [ ] Update auf eine höhere Version, Einstellungen bleiben
- [ ] Deinstallation: Protokoll-Handler und Autostart sind weg

Die Messwerte aus §2 gehören auf echte x64-Hardware, nicht in eine Emulation (ADR-001) — Ergebnisse nach `docs/performance.md`.

## Lizenz vor der Auslieferung klären (AP9.6)

Das Linphone SDK steht unter AGPLv3 oder einer kommerziellen Lizenz. **Solange nipp intern bei bv2 bleibt, stellt sich die Frage nicht.** Vor der ersten Abgabe an einen Kunden schon, und dann verbindlich — Einzelheiten in `docs/licensing.md`.

## Offen, und nicht von hier zu entscheiden (§16)

- **Verteilweg.** MSIX über Intune oder ein klassisches MSI/EXE — nicht jede Kundenumgebung hat Intune. Für bv2 intern reicht MSIX.
- **Update-Mechanismus (§16.4).** App-Installer-URL, eigene Prüfung oder Verteilung über Intune. Bis das entschieden ist, gibt es keine Update-Prüfung in der App.
- **Signaturzertifikat (§16.2).** Fehlt noch. **Die Beschaffung dauert Wochen — früh anstossen.** Ohne echtes Zertifikat ist das Paket selbstsigniert und nur auf vorbereiteten Rechnern installierbar.
