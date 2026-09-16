# Lehren, die nicht im Code stehen können

Die teuren Stellen dieses Projekts, gruppiert — was jemanden Stunden oder
Tage gekostet hat und woran man es beim nächsten Mal erkennt. Jede stand
hier schon einmal als flache Liste von vierundvierzig Punkten; was man
darin nicht findet, hilft niemandem.

**Bis zum 13.09.2026 stand das alles in `CLAUDE.md`**, zusammen mit den
Grenzen, den Befehlen und dem Stand — auf 1554 Zeilen. Eine Datei, die man
vor jeder Änderung lesen soll, muss lesbar bleiben; sie ist deshalb auf die
Regeln zusammengezogen, und was Erfahrung ist, steht hier.

Verwandte Dokumente: `docs/decisions.md` (warum etwas so entschieden wurde),
`docs/stand.md` (was zuletzt passiert ist), `CLAUDE.md` (die Regeln selbst).

---


Die teuren Stellen, gruppiert. Jede stand hier schon einmal als flache
Liste von vierundvierzig Punkten — was man darin nicht findet, hilft
niemandem.

### Telefonie: Zustände, Anrufe und das SDK

- **Eine Ausnahme aus einem SDK-Callback hat keinen Fänger.** Die Callbacks
  kommen aus `linphone_core_iterate` über einen Reverse-P/Invoke-Rahmen — der
  `try` in `SipPumpHost.OnTick` liegt **ausserhalb** davon und sieht sie nie.
  An `CallStateChanged` hingen acht Abonnenten, darunter `ContentFrame.Navigate`
  und der Schreibzugriff auf die Anrufliste; **`LinphoneException` wurde im
  ganzen `src/` nirgends gefangen**, obwohl `Pause`, `Resume`, `SendDtmf` und
  `Terminate` sie werfen. «Stumm» drücken, während die Gegenseite auflegt, war
  ein Absturz — `SipService` vergisst ein Gespräch bei `End`, also **bevor** die
  Oberfläche nachzieht, und die Knöpfe bleiben einen Wimpernschlag klickbar.
  Seit ADR-053 liegt die Grenze an drei Stellen, und `ExceptionBoundaryTests`
  hält sie.
- **Ein Ereignis, das niemand abonniert, ist kein Ereignis.**
  `OnTransferStateChanged` war nicht angemeldet, und `TransferAsync`
  protokollierte «übergeben» **vor** jeder Antwort der Anlage; der Kommentar
  daneben gab sogar zu, dass der Wrapper den Rückgabewert verschluckt. Ein 403
  auf den REFER war damit unsichtbar: kein Fehlertext, das Gespräch blieb je
  nach Anlage gehalten stehen, und im Protokoll stand Erfolg. **Wer eine
  Handlung anstösst, deren Ergebnis später kommt, protokolliert «angestossen»
  und wartet auf die Antwort.**
- **Was ein Zustand des SDK bedeutet, wird über seinen Namen entschieden**, nicht
  über den Enum-Wert (`TransferOutcomes`, `MapKnownRegistrationStatus`). Zwei
  Gründe: die Regel bleibt ohne SDK prüfbar, und ein Wert, den diese Fassung
  nicht kennt, fällt in den Zwischenfall statt in «gescheitert». Bei
  `Refreshing` hat genau das eine halbe Sekunde lang eine rote Lampe erzeugt.

- **Eine Meldung ist ein Auftrag, keine Auskunft — und zweimal am selben Tag
  dieselbe Form** (13.09.2026, ADR-060). Auf einem Arbeitsplatz mit **zwei
  aktiven Netzwegen** hielt die Anmeldung nicht: das Konto lief im Sekundentakt
  `Ok → Progress → Failed → Ok`, die Präsenz aller zehn Nebenstellen stand auf
  «offline». Der Mobilfunkadapter wechselte seine Adresse von selbst, und
  `ConnectivityMonitor` reichte **jedes** Windows-Netzereignis an das SDK weiter
  — auch wenn sich nichts geändert hatte. **Jede solche Meldung ist dort eine
  Neuregistrierung**, und die Anlage sah einen Anmeldesturm.
  **Die Entprellung fing das nicht**, und das war kein Versäumnis, sondern eine
  andere Frage: sie fasst zusammen, was in zwei Sekunden kommt — hier kam es
  über Minuten. Was fehlte, war nicht ein längeres Fenster, sondern die Frage,
  **ob sich überhaupt etwas geändert hat**.
- **Und im selben Protokoll eine Kette ohne Ende.** Alle neun Millisekunden
  derselbe Block: gespeichert, auf den Core übertragen, Kürzel angemeldet,
  Erscheinungsbild gesetzt — und wieder gespeichert. An `Changed` hängen vier
  Empfänger, und einer schrieb zurück. **248 MB Protokoll in sechs Minuten.**
  Wer die Kette auslöst, ist die falsche Frage: sie konnte entstehen, weil
  niemand geprüft hat, ob es etwas zu schreiben gibt. Diese Prüfung gehört an
  die **eine** Stelle, durch die jeder Schreibweg läuft — nicht in vier
  Empfänger, die sich daran erinnern müssten.
- **Die Gegenprobe ist bei so einer Bremse die wichtigere Hälfte.** Beim
  Wechsel WLAN → LAN bleibt «erreichbar» true, und trotzdem *muss* neu
  registriert werden: der Contact-Header trägt sonst die alte Adresse. Eine
  Bremse, die nur die Erreichbarkeit vergleicht, verschluckt genau den Fall,
  für den es den Dienst gibt. Deshalb gehören die **lokalen Adressen** in den
  Vergleich.
- **Ein Test, der aus dem falschen Grund rot ist, hat trotzdem recht.** Der
  erste Test der Schleifenbremse zählte zwei Meldungen statt einer — nicht
  wegen der Bremse, sondern weil `MarkUserChanges` die Benutzermarkierungen aus
  dem **übergebenen** Objekt nahm. Wer ein `NippSettings` speichert, das er
  einen Augenblick früher aus `Current` abgeleitet hat, bringt eine veraltete
  Liste mit und **löscht damit, was der Benutzer angefasst hat** — beim
  nächsten Start hätte das Profil wieder gewonnen (ADR-054).
- **Ein entprellter Wert ist beim Anwenden alt.** `ConnectivityMonitor` fror
  die Erreichbarkeit beim Ereignis ein und meldete sie zwei Sekunden später an
  das SDK. Beim Wechsel WLAN → LAN meldete Windows fünfzehn Ereignisse in
  neunzig Sekunden; die Meldungen stauten sich auf dem blockierten UI-Thread,
  und **die älteste kam zuletzt an** — nipp sagte dem SDK „kein Netz“, 1,7
  Sekunden nachdem das Netz wieder da war. Folge: Registrierung zweimal
  verloren, jedes Präsenz-Abo mit `481 Call/transaction does not exist`
  quittiert. **Wer entprellt, liest den Zustand im Moment des Meldens** — und
  lässt nur eine Meldung zur Zeit hinein (`SemaphoreSlim`), weil `Cancel()`
  nichts mehr ausrichtet, sobald der Aufruf schon auf dem UI-Thread wartet.
  Gemessen am 08.09.2026, geprüft in `ConnectivityMonitorTests`.
- **Zwei je für sich richtige Entscheidungen ergeben nicht automatisch ein
  richtiges Ergebnis.** ADR-044 legte den Fokus beim Klingeln auf «Annehmen» —
  richtig, denn die zeitkritischste Handlung war die am schlechtesten
  erreichbare. `MainWindow` holte das Fenster beim Klingeln nach vorn —
  nachvollziehbar, denn nipp lebt im Infobereich. **Zusammen hiess das: wer in
  einer anderen Anwendung tippte, bekam nipp vor die Nase, und die nächste
  Leertaste nahm den Anruf an.** Keine der beiden Stellen war für sich falsch,
  und keine kannte die andere. Die Spezifikation sagte es übrigens wörtlich —
  §8.6 nennt den Vordergrund beim *Klick auf Annehmen*. **Wer zwei Regeln an
  zwei Stellen setzt, prüft sie einmal gemeinsam** (ADR-049).
- **Eine Antwort, zwei Wirkungen — das ist die gute Form der Wiederverwendung.**
  Seit ADR-046 ist das Nummernfeld auch das Suchfeld, und die Frage «ist das
  überhaupt wählbar?» stellt sich zweimal: bei der Eingabetaste und bei der
  Frage, welche Liste steht. Sie steht einmal im Kern
  (`NumberNormalizer.IsDialable`, die Gegenfrage zu `Normalize`) und wird
  zweimal gelesen. **Zwei Antworten wären zwei Gelegenheiten, sie
  auseinanderlaufen zu lassen** (ADR-049, ADR-051).
- **Was nicht protokolliert wird, ist im Zweifel nicht passiert — oder doch.**
  Nach drei nicht zustande gekommenen Gesprächen liess sich nicht entscheiden,
  ob niemand gedrückt hatte oder ob der Druck verlorenging: `AcceptAsync`
  schrieb keine Zeile. Der SIP-Trace zeigt nur, was **beim SDK ankam** — genau
  die offene Frage. Jede Benutzerhandlung, die eine Zustandsänderung auslöst,
  gehört ins Protokoll, mit Kennung und ohne Nummer.
- **Nicht auf Zustandsübergänge navigieren, sondern auf die Anrufkennung.**
  Welche Zustände das SDK in welcher Reihenfolge meldet, ist für ein- und
  ausgehende Anrufe verschieden: ein eingehender wird bereits klingelnd
  angelegt, ein ausgehender läuft `Dialing → Ringing → Connected`. Eine Liste
  erlaubter Übergänge muss beide kennen — **drei Anläufe haben je eine
  Richtung vergessen**, und die Gesprächsansicht erschien mal bei eingehenden,
  mal bei ausgehenden Anrufen nicht. `MainWindow` merkt sich jetzt, welche
  Anrufe die Ansicht schon geholt haben (`_announcedCalls`); jeder holt sie
  genau einmal. Von der Reihenfolge unabhängig, und §8.2 bleibt erfüllt.
- **Eine Zustandsregel gehört an das Ereignis, nicht in die Empfänger.** Die
  Frage „beginnt hier ein Anruf zu klingeln?" stand wörtlich gleich in
  `MainWindow` und im `ToastService` — und war an beiden Stellen falsch. Ein
  eingehender Anruf war dadurch **unsichtbar**: kein Toast, und weil nipp im
  Infobereich lebt, gar kein Zeichen. Zwei Kopien einer Regel sind zwei
  Gelegenheiten, sie falsch zu haben, und eine Gelegenheit, nur die eine zu
  korrigieren. Sie steht jetzt einmal an `CallStateEventArgs` und ist prüfbar.
- **Ein neuer Anruf hat keinen Vorzustand — das muss `null` heissen.**
  `SipService.Track()` legt einen eingehenden Anruf schon mit
  `CallStatus.Incoming` an. Wird derselbe Wert als „previous" gemeldet, sieht
  jeder Empfänger, der auf den Übergang prüft, einen Anruf, der schon immer
  geklingelt hat. Der Übergang findet dann nie statt.
- **Nichts Zustandsänderndes aus einem SDK-Callback.** `Call.Accept` aus
  `OnCallStateChanged` heraus lässt das SDK sofort die nächsten Zustände
  melden, mitten im laufenden Aufruf; der äussere Rahmen schreibt danach
  seinen veralteten Zustand zurück. Vormerken und im nächsten `Pump()`
  ausführen.
- **`FriendList` nie ersetzen.** `linphone.db` hat `UNIQUE (name)` auf
  `friends_list`; ein zweites `AddFriendList` gleichen Namens wirft eine
  SEH-Ausnahme. Einmal anlegen (oder per `GetFriendListByName` übernehmen),
  dann nur `AddFriend`/`RemoveFriend`.
- **SDK-Adressen mit `AsStringUriOnly()` vergleichen**, nicht `AsString()` —
  letzteres trägt den Anzeigenamen. `SipUri.Same` normalisiert beide Seiten.
- **`RegistrationState.Refreshing` ist kein Fehler.** Nur ein ausdrücklicher
  Fehlerzustand ist einer; Unbekanntes wird protokolliert, nicht alarmiert.
- **SIP-Trace sehen:** Protokollierung auf Debug stellen, dann leitet
  `SdkLogBridge` die SDK-Meldungen samt SIP-Nachrichten ins Log.
  `tools\Test-Blf.ps1` wertet den letzten Start aus.
- **Ein blockierender Aufruf im Startpfad sieht wie ein Erfolg aus.**
  `HidD_SetOutputReport` wartet auf die Bestätigung des Geräts. Auf dem
  UI-Thread heisst das: **nipp startet nicht** — kein Fenster, keine Anmeldung,
  und die letzte Protokollzeile ist „Headset-Tasten angebunden". Sie liest sich
  wie ein abgeschlossener Schritt und ist die Stelle, an der alles stehenblieb.
  Wer einen Aufhänger sucht, schaut auf die **erste fehlende** Zeile: fehlten
  Klingelton und Kontoanmeldung, liegt es davor. Suchen und Schreiben am
  Headset laufen jetzt auf eigenen Threads.

### Audio, Töne und Geräte

- **Die Klänge des SDK liegen tiefer, als der Name sagt:**
  `share/sounds/linphone/` und `share/sounds/linphone/rings/`. Zeigt
  `RingResourcesDir` auf `share/sounds`, findet das SDK seinen Klingelton
  nicht — und ein eingehender Anruf klingelt lautlos.
- **Das SDK hat zwei Geräte-APIs, und die Töne lesen die alte.** nipp wählt
  Geräte über `AudioDevice` (`UseForCall`, `UseForRinging`); der Tonspieler
  greift auf `sound_conf.play_sndcard` zu. Die war leer, `ring_sndcard` nicht —
  deshalb klingelte es bei eingehenden Anrufen und **das Freizeichen beim
  Wählen war stumm**. Im Protokoll stand der Ton als gespielt: Datei offen,
  Stream läuft. Nur endete die Filterkette in `MSVoidSink` statt
  `MSWASAPIWrite`. `SettingsApplier.ApplyToneCards` zieht die alte API nach.
  Wer einen Ton nicht hört, vergleicht die Filterkette mit der eines Tons, der
  funktioniert — der Unterschied steht in der letzten Zeile.
- **Early Media ohne Audio ist Stille, und das SDK meldet keinen Fehler.**
  Schickt die Anlage auf einen ausgehenden Anruf ein `183 Session Progress`
  **mit SDP**, geht linphone in `OutgoingEarlyMedia` und hält den empfangenen
  Strom für die Audioquelle — es spielt dann **keinen eigenen Rufton**. Sendet
  die Anlage anschliessend kein RTP, hört man nichts. Im Protokoll fehlt
  `startRingbackTone` einfach; erkennbar ist es am Jitter-Puffer, der nie
  konvergiert („stays unconverged for one second"). Einen Schalter dagegen gibt
  es nicht: `set_ringback` setzt nur die Datei,
  `set_remote_ringback_tone` ist der Ton **für die Gegenseite**, und
  `set_ring_during_incoming_early_media` gilt nur eingehend. nipp spielt
  deshalb selbst (`RingbackWatch`, ADR-029).
- **„Es kommt Audio an“ heisst nicht „man hoert etwas“.** `RingbackWatch`
  schwieg, sobald mehr als 5 kbit/s hereinkamen — in der Annahme, das sei das
  Laeuten der Anlage. Am 08.09.2026 lief der eigene Rufton deshalb
  **402 Millisekunden** und war dann aus, ohne dass sich der Anrufzustand
  geaendert haette; zu hoeren war nichts, und der Jitter-Puffer konvergierte
  nie. Ein Strom aus Stille ist kein Laeuten. Die Regel prueft jetzt
  zusaetzlich den **Pegel** (`Call.PlayVolume`, dBm0) und schweigt nur bei
  hoerbarem Empfang; ist der Pegel nicht messbar, entscheidet wie bisher die
  Bandbreite. **Und die Stopp-Zeile nennt jetzt die Messwerte** — vorher
  stand dort „gestartet“ und 402 ms spaeter „beendet“, ohne jeden Hinweis
  auf den Grund. **Am Gerät bestätigt am 08.09.2026** (T78b, T138): der Ton
  läuft jetzt 2,3 s und endet mit `Empfang 73.5 kbit/s, Pegel -5.0 dBm0` —
  also mit echtem Audio. **Diese Messwerte haben die Erklärung vom Verdacht
  zum Beleg gemacht**; ohne die erweiterte Protokollzeile wäre es bei
  „geht jetzt“ geblieben, und niemand wüsste, warum.
- **Ein Messwert, der einmal pro Sekunde entsteht, kann keine Entscheidung nach
  800 ms tragen.** `RingbackWatch` fragte `Call.AudioStats.DownloadBandwidth` —
  ein **Sekundenmittel**, das liblinphone einmal pro Sekunde neu setzt, davor
  0. Am 10.09.2026 legte nipp deshalb zweimal seinen eigenen Rufton über den
  Anfang des Anlagentons, **168 und 193 ms lang**, einmal 200 ms *nachdem* der
  fremde Strom begonnen hatte. Und die Karenzzeit war zusätzlich zu kurz: der
  Strom setzte 0,91 und 1,04 s nach dem Läuten ein, entschieden wurde nach 0,8
  — **wer zu früh entscheidet, sieht auch mit dem besten Messgerät nichts.**
  Jetzt entscheidet `CallStats.RtpPacketRecv` (pro Paket geführt), die
  Karenzzeit läuft ab dem SIP-Ereignis, und ein *ankommender* Strom bekommt
  fünf Sekunden statt 1,4. **Die naheliegende Erklärung war übrigens falsch:**
  nicht die Kadenz 1 s / 4 s — über 5,07 s kamen 252 Pakete, der Strom war
  lückenlos. Nachtrag zu ADR-029.
- **Und die Zeile, die es erklären sollte, behauptete eine Ursache, die sie
  nicht gemessen hatte:** „die Gegenstelle schickt Early Media ohne Audio" —
  beide Male gab es Audio. **Eine solche Protokollzeile ist schlechter als
  keine**, weil sie die Suche in die falsche Richtung schickt. Sie nennt jetzt
  Grund, Paketzahl und Wartezeit.
- **Ein Rufzustand, den es nie gab, kann nicht abgenommen werden.** T78 galt
  als bestanden, weil „der Rufton hörbar" war — die geprüften Anrufe waren
  aber intern und gingen `Dialing → Connected`. Ohne `Ringing` gibt es keinen
  Rufton, also war nichts geprüft. Wer einen Ton abnimmt, prüft **zuerst den
  Zustandsverlauf**, nicht das Gehör: `tools\Test-Ton.ps1` liest ihn samt
  Filterkette aus dem Protokoll.
- **Die sanften Klingeltöne des SDK sind nicht abspielbar.**
  `soft_as_snow`, `notes_of_the_optimistic` und die vier anderen liegen als
  `.mkv` vor, und `bcmatroska2.dll` fehlt im win64-Prebuilt. Deshalb stand die
  alte Telefonglocke da — sie war neben `toy-mono.wav` die einzige WAV. Eigene
  Klänge kommen jetzt aus `tools\Build-Sounds.py`; wer einen Ton einträgt, der
  nicht `.wav` ist, bekommt ein Klingeln, das stumm bleibt.
- **Die alte und die neue Geräte-API des SDK benennen dasselbe Gerät
  verschieden.** `Core.DefaultOutputAudioDevice.DeviceName` liefert
  „Kopfhörer (Jabra Link 400)", `Core.SoundDevicesList` dieselbe Karte als
  „WASAPI: Kopfhörer (Jabra Link 400)". Der Setter `Core.PlaybackDevice`
  **wirft** bei einem Namen, den er nicht kennt — ein blind übergebener
  Anzeigename endet also in einer Ausnahme und einem stummen Ton.
  `ToneCardChooser` liefert deshalb mehrere Kandidaten, und der Aufrufer nimmt
  den ersten, der sich setzen lässt. Am Gerät bestätigt: seither steht dort
  `WASAPI: Default Playback` statt `Default Playback`.
- **Die Filterstatistik des SDK ist das Messgerät für Audioprobleme, und
  niemand hatte je hineingesehen.** Am 16.09.2026 war „teilweise starkes
  Rauschen" gemeldet; gefunden wurde die Ursache nicht am Gerät, sondern in
  den Zeilen `FILTER USAGE STATISTICS`, die auf Debug ohnehin im Protokoll
  stehen. Dort stand `MSNoiseSuppressor` mit **max 77,24 ms je Tick** — bei
  einem Ticker, der alle **10 ms** läuft. Die Folge steht drei Zeilen weiter
  im selben Protokoll: `Ticker: We are late of 136 miliseconds`, dann
  `Could not get buffer from the MSWASAPI audio output interface`, dann ein
  Jitterpuffer, der von 40 auf 154 ms springt. **Wer ein Audioproblem sucht,
  liest zuerst diese Tabelle** — sie nennt den Filter, der den Tick überzieht,
  und damit meistens schon die Antwort.
- **„Kostet 80 Prozent" und „ist zu langsam" sind nicht dasselbe.** Derselbe
  Filter stand in der CPU-Spalte immer bei 77 bis 87 % — aber sein **Mittel**
  lag bei 0,7 bis 0,9 ms von 10 ms, und in vier von sechs Gesprächen blieb
  auch sein Maximum unter 6,2 ms, darunter eines über acht Minuten. Er ist
  also normalerweise harmlos; **nur zweimal schoss er auf fast genau denselben
  Wert** (77,24 und 77,76 ms). Bei einem Mittel von 0,7 ms ist das Faktor 110
  — kein Rechenaufwand, sondern ein Stillstand, vermutlich der x64-Emulator.
  Die erste Fassung dieses Befundes schrieb „kostet 77 bis 87 Prozent der
  Rechenzeit"; das war wörtlich richtig (die Spalte nennt den Anteil **an der
  Kette**) und hätte beinahe zur falschen Konsequenz geführt, nämlich den
  Filter im Standard abzuschalten. **Ein Mittelwert und ein Maximum
  beantworten verschiedene Fragen.**
- **Die Rauschunterdrückung wirkt nur auf das, was gesendet wird.** In der
  Kette steht sie ausschliesslich in der Senderichtung
  (`MSWASAPIRead → MSNoiseSuppressor → … → MSRtpSend`); empfangen wird über
  `MSRtpRecv → MSUlawDec → MSAudioMixer → MSGenericPLC → MSAudioFlowControl →
  MSDtmfGen → MSResample → MSWASAPIWrite` — **kein Rauschfilter darin**. Wer
  den Schalter gegen ein Rauschen im eigenen Hörer betätigt, ändert nichts.
  Einstellbar ist daran auch nichts weiter: `msnoisesuppressor.h` kennt genau
  zwei Methoden, Bypass ein und Bypass aus. Seit dem 16.09.2026 sagt die
  Beschreibung in den Einstellungen, worauf der Schalter wirkt — dieselbe
  Antwort, die ADR-006 Punkt 2 der Echounterdrückung gegeben hat.

### WinUI und XAML

- **Ein Dienst, der auf das Netz wartet, meldet danach vom falschen
  Thread.** `UpdateService` feuerte sein `Changed` nach einem
  `await … ConfigureAwait(false)`; im ViewModel wurde daraus ein
  `OnPropertyChanged` an eine gebundene Oberfläche, und das ist in WinUI
  kein Fehlverhalten, sondern ein **Absturz**. Gemeldet am 08.09.2026 von
  einem Windows-10-Arbeitsplatz: „stürzt ab, wenn ich nach Updates
  suche“. **Hier konnte es nicht auffallen** — auf der
  Entwicklungsmaschine ist nipp nicht installiert, der Dienst kehrt vor dem
  ersten `await` zurück, und der Threadwechsel findet nie statt. Der Pfad
  lief erst auf dem Zielrechner vollständig. Wer aus dem Kern heraus
  meldet, fängt den `SynchronizationContext` ein und postet dorthin, wie
  `ShellViewModel` und `ConnectivityMonitor` es tun.


- **`GroupStyle.Panel` wird von den virtualisierenden Panels gar nicht
  gelesen.** Die Team-Kacheln standen untereinander, jede über die volle Breite
  gestreckt — das Raster lag im `GroupStyle.Panel`, das `ItemsPanel` war ein
  `ItemsStackPanel`, und gezeichnet hat das `ItemsStackPanel`.
  `ItemsStackPanel` und `ItemsWrapGrid` tragen die Gruppierung **eingebaut**
  (`GroupHeaderPlacement`); `GroupStyle.Panel` gehört zur alten, nicht
  virtualisierenden Gruppierung. **Der Kommentar daneben behauptete seit
  ADR-047 das Gegenteil** («ein gruppiertes GridView virtualisiert NUR so») und
  war genau die Begründung, die Stelle nicht anzufassen — es sah aus wie zu
  wenig Platz und war das falsche Panel. Gemessen am 12.09.2026, ADR-052:
  **eine Erklärung, die niemand gemessen hat, ist eine Behauptung.**
- **Zwei Sternspalten mit Höchstbreiten verschenken den Rest.** WinUI verteilt
  Sternplatz gleichmässig und kürzt **erst danach** an `MaxWidth` — den frei
  gewordenen Anteil gibt es nicht weiter (anders als WPF). Links `*` mit
  `MaxWidth=480`, rechts `*` mit `MaxWidth=1070`: auf 1920 Pixeln blieben
  **468 tot**. Wer eine feste und eine wachsende Spalte will, macht die feste
  **absolut** (ADR-052). **Und der Spaltenabstand gilt auch für eine Spalte der
  Breite 0** — im einspaltigen Zustand ist er ein toter Streifen am Rand.
- **`DragItemsCompleted` sagt nicht, wohin gezogen wurde.** Die Ereignisdaten
  tragen die gezogenen Zeilen und `DropResult` — keinen Zielindex, keine
  Gruppe. **Und der Zustand der Sammlungen beantwortet die Frage auch nicht:**
  hier stand bis zum 13.09.2026, WinUI habe sie bereits umgebaut, bevor das
  Ereignis feuert. Das kam aus ADR-042, war nie gemessen und ist falsch — bei
  einer gruppierten `CollectionViewSource` endet jeder Drop mit `None`, und
  umgebaut wird gar nichts. **Zum dritten Mal dieselbe Lehre, und diesmal
  stand die Behauptung in einem ADR** und hat sich von dort in vier weitere
  Dokumente fortgeschrieben. Wer das Ziel wissen will, wertet den Zug selbst
  aus (ADR-065).
- **Ein gruppiertes `GridView` startet keinen Zug.** Dieselben Zieh-Ereignisse,
  die in einem `ListView` feuern, kommen im Kachelraster gar nicht an — weder
  `DragItemsStarting` der Liste noch `DragStarting` eines Elements mit
  `CanDrag`, und auch nicht ohne Cross-Slide. Der Weg, der in beiden Ansichten
  trägt: den Zug **selbst** starten — Druck merken, nach acht Pixeln
  `StartDragAsync` am Vorlagenelement (ADR-065).
- **Ein `ListViewItem` markiert `PointerPressed` als behandelt** — für seine
  Auswahl —, und ein behandeltes Ereignis steigt nicht zur Liste auf. Ein
  `PointerPressed="…"` an der Liste bekommt deshalb **nie** einen Druck auf
  eine Zeile zu sehen; eine Protokollzeile im Behandler blieb stumm. Nur
  `AddHandler(PointerPressedEvent, …, handledEventsToo: true)` sieht ihn.
- **Wer nach einem Neuaufbau eine alte Instanz sucht, findet nichts — und ein
  «nichts» sieht aus wie «anders».** Die Frage «hat die Zeile die Gruppe
  gewechselt» wurde zweimal falsch beantwortet: einmal über einen
  Referenzvergleich, einmal über die Suche nach der gezogenen Zeile in den
  neuen Gruppen. Beide Male lautete die Antwort immer «ja». Die Antwort kennt
  das Ablegeziel — dort wird sie festgehalten.
- **Eine Kennung, die einen Index enthält, ist keine.** `IdOf(member, index)`
  war die Zuordnung zwischen Zeile und gespeichertem Eintrag — nach dem ersten
  Umsortieren zeigte jede auf die falsche. Der zweite Ziehvorgang schlug
  deshalb fehl, **ohne Fehlermeldung**: die Zuordnung fand nichts, das Ergebnis
  war „unverändert", und die Prüfung „nichts zu speichern" traf zu. Sichtbar
  war es erst nach einem Neustart. Wer eine Kennung braucht, nimmt etwas aus
  dem **Inhalt** (ADR-042).
- **`ThemeService.Attach` merkt sich genau ein Wurzelelement.** Ein zweites
  Fenster, das es ruft, **entzieht dem ersten das Erscheinungsbild**: ein
  Systemwechsel kommt danach nur noch im neuen Fenster an und nach seinem
  Schliessen nirgends mehr. WinUI kennt kein anwendungsweites Erscheinungsbild
  — es hängt am Element. Ein zweites Fenster setzt `RequestedTheme` selbst und
  hört auf `EffectiveThemeChanged`.
- **Ein `record` in einer Liste liest sich für die Sprachausgabe als
  `ToString()`.** Ohne `AutomationProperties.Name` war der Name einer
  Katalogzeile die ganze Aufstellung — Endpunkte, Mapping-Typen und die
  vollständige Beispielantwort, mehrere Tausend Zeichen. Gerade weil ein
  `record` ein hilfreiches `ToString()` hat, ist er dort unbrauchbar. Gemessen
  über UI-Automation, nicht vermutet.
- **Zwei ListViews in einem ScrollViewer verlieren die Virtualisierung.** Sie
  bekommen unendliche Höhe angeboten und erzeugen jede ihrer Zeilen. Bei den
  Kontakten wären das über hundert auf einmal, auf demselben Thread, der alle
  20 ms `Core.Iterate()` bedient. Deshalb ein `Grid` mit `Auto`/`*`-Zeilen und
  eine gedeckelte Höhe für die Team-Liste — jede Liste scrollt selbst. Wer die
  äussere Struktur ändert, prüft das am Gerät nach: der Fehler ist kein
  Absturz, sondern ein Ruckeln beim Wechsel auf den Kontakte-Tab.
- **`NavigationCacheMode.Required` und Verdrahtung im Konstruktor vertragen
  sich nicht.** Mit Zwischenspeicher läuft der Konstruktor genau einmal; wer
  dort abonniert und in `OnUnloaded` abmeldet, hat nach der ersten Navigation
  eine taube Seite, die aus dem Speicher zurückkommt und nichts mehr
  mitbekommt. Abonnements gehören in `OnLoaded`, gelöst wird in `OnUnloaded`
  — nur `Loaded` und `Unloaded` selbst bleiben dauerhaft verbunden.
- **`BitmapImage` löst kein `file:`-URI auf.** Ein Bild aus dem
  Ausgabeverzeichnis kommt über `ms-appx:///…` herein — das funktioniert auch
  unpackaged. Ein Fehlschlag meldet sich nur über `ImageFailed`, sonst bleibt
  die Stelle einfach leer.
- **`Windows.*` ist innerhalb von `Nipp.App` verdeckt.** Der eigene Namespace
  `Nipp.App.Windows` gewinnt: `Windows.UI.Color` wird zu
  `Nipp.App.Windows.UI.Color` und der Compiler meldet einen fehlenden
  Assemblyverweis. `global::Windows.…` oder ein Alias. Dieselbe Falle wie
  `Nipp.Core` gegen `Linphone.Core`.
- **Ein Themenwörterbuch heraussuchen heisst nicht, das eigene zu finden.**
  `XamlControlsResources` steht in `App.xaml` vor `Tokens.xaml` und bringt
  eigene ThemeDictionaries für „Light", „Dark" und „HighContrast" mit. Wer die
  MergedDictionaries nur nach dem Themennamen durchsucht, bekommt das der
  Steuerelemente — und darin steht kein einziger unserer Schlüssel.
  `ThemeService` prüft deshalb zusätzlich auf einen bekannten Schlüssel.
  Verschärfend kam der werfende Indexer dazu: `dictionary[key]` wirft bei
  einem fehlenden Schlüssel, und weil das aus dem Konstruktor von
  `MainWindow` läuft, **startete nipp gar nicht mehr** — kein Fenster, keine
  Meldung, nur ein Eintrag im Protokoll. `TryGetValue` nehmen.
- **XAML-Ressourcen werden erst zur Laufzeit aufgelöst.** Ein fehlender
  Schlüssel ist ein Absturz beim Öffnen der Seite, kein Build-Fehler.
  `XamlResourceTests` prüft alle `StaticResource`-Verweise. Konverter gehören
  nach `App.xaml`. Typfehler (`x:Double` an `GridLength`) findet der Test
  nicht.
- **Ein Ressourcenwörterbuch gilt nur für den Teilbaum darunter.** Ein Stil in
  `<Grid.Resources>` ist für ein Geschwister-Grid nicht sichtbar — auch wenn
  `XamlResourceTests` zufrieden ist, denn der Schlüssel existiert ja. Beim
  Umbau der Gesprächsansicht bekam eine zweite Knopfreihe denselben Stil wie
  die erste, und die Seite liess sich danach **gar nicht mehr öffnen**:
  `XamlParseException` aus dem Konstruktor, mitten im SDK-Callback, bei einem
  Telefon also genau dann, wenn ein Anruf kommt. Aufgefallen ist es erst beim
  Anrufen am Gerät. `XamlResourceScopeTests` prüft das jetzt mit einem
  XML-Parser: Definitionsort gegen Verwendungsort im Baum.
- **`AppWindow` rechnet in physischen Pixeln**, XAML in logischen.
  `WindowPlacement` multipliziert mit `GetDpiForWindow/96`; ohne das ist ein
  400er-Fenster bei 150 % nur 267 logische Pixel breit.

### Windows-Integration: Infobereich, HID, Symbole

- **`GetActiveObject` will eine CLSID, keine ProgID.** Mit einer Zeichenfolge
  als erstem Parameter liest die Funktion die ersten sechzehn Bytes des
  Textes als CLSID, scheitert immer, und das Ergebnis ist von „läuft nicht"
  nicht zu unterscheiden. Erst `CLSIDFromProgID`, dann `GetActiveObject`.
- **Ein natives Handle überlebt sein verwaltetes Objekt nicht.**
  `using var icon = new Icon(path); return icon.Handle;` gibt das Symbol frei,
  bevor der Infobereich es zeichnet — `Icon.Dispose` ruft `DestroyIcon`.
  Zurück kommt eine Zahl, die auf nichts zeigt: **nipp stand im Infobereich
  ohne Symbol**, und das Protokoll meldete Erfolg. Tückisch daran ist, dass
  ein freigegebenes Handle erst ungültig wird, wenn Windows den Platz neu
  vergibt — es sah mal richtig aus und mal nicht. Wer ein Handle weitergibt,
  hält das Objekt am Leben, solange es benutzt wird; beim Austausch wird das
  alte erst **nach** dem Wechsel freigegeben.
- **«Beenden» aus dem Infobereich lief fünf Tage lang ins Leere, und die
  Ursache war die letzte Anweisung.** Das Herunterfahren lief vollständig durch
  — bis «Core gestoppt» im Protokoll —, und der Prozess blieb trotzdem liegen;
  jeder folgende Build scheiterte an MSB3027, obwohl nichts mehr offen war.
  **`Application.Exit()` auf dem Thread des Infobereich-Symbols ist ein stiller
  Leerlauf:** die XAML-Nachrichtenschleife läuft auf dem
  `[STAThread]`-Hauptthread weiter, und der ist der einzige Vordergrundthread
  des Prozesses. Der Aufruf muss über die `DispatcherQueue` gehen, wie der
  Nachbarpfad «Öffnen». **Die Korrektur vom 07.09.2026 erfasste
  `OpenRequested` und übersah `ExitRequested`** — acht Zeilen Kommentar
  darüber erklärten, warum es nötig ist. **Verraten hat es die Uhr:** zwischen
  der letzten Abbauzeile und dem Zugriff des Wächters lagen achtmal konstant
  6,8 Sekunden, während der ganze Abbau **eine** dauert. Es hing nicht in einem
  Dienst, es hing in der letzten Anweisung. Behoben am 12.09.2026, am Gerät
  abzunehmen als T134.
- **`Windows.Media.Devices.CallControl` gibt es auf dem Desktop nicht.** Die
  Klasse steht in den Metadaten, hat `AnswerRequested`/`HangUpRequested` und
  kompiliert fehlerfrei; zur Laufzeit scheitern `FromId` **und** `GetDefault`
  mit `0x80040111` (`CLASS_E_CLASSNOTAVAILABLE`). Der naheliegende Ersatz
  `Windows.Devices.HumanInterfaceDevice` sperrt genau die Usage Page aus, um
  die es geht — Telefonie (0x0B) ist reserviert. Bleibt Win32-HID über
  SetupAPI und `hid.dll` (ADR-028). Der Prozess heisst übrigens **`Nipp.App`**,
  nicht `nipp` — `Stop-Process -Name nipp` trifft nichts, und der Build
  scheitert danach weiter an MSB3027.
- **Ein geteiltes Handle heisst geteilte Wirkung — und derselbe Fehler eine
  Prozessgrenze weiter.** „Bin ich in einem Teams-Meeting und es klingelt auf
  nipp, fliege ich aus dem Meeting", schon beim Läuten. nipp schreibt den
  Ring-Report, das Gerät verhandelt seinen Zustand und meldet den Wechsel
  zurück, und weil das Handle geteilt geöffnet ist, sieht Teams dieselbe
  Meldung — **ohne `HookWatch`**, also als Tastendruck des Benutzers. Im
  Meeting heisst das auflegen. Belegt ist, dass Teams am selben Interface hängt
  (beim Beitritt meldete das Engage 75 off-hook, ohne dass nipp etwas geschickt
  hatte); **die Kette bis zum Abbruch ist gemessen worden, nicht bewiesen** —
  einer von acht Ring-Reports. Deshalb: **ein Ausgangsreport ist keine Lampe,
  sondern eine Mitteilung an ein Gerät, das nipp mit anderen teilt.** Er geht
  nur bei eigenem Anlass hinaus (`HeadsetSignalGate`), und der Ring schweigt bei
  Fremdbelegung. Damit entfielen auch die 13 Reports vom 10.09.2026, die ohne
  jeden Anruf hinausgingen — beim Start und bei **jedem** Audiogerätewechsel,
  weil jedes Anbinden in einem Report endete. ADR-028 Nachtrag 4.
- **Ein Zustandsspiegel, den auch die eigene Absicht beschreibt, erfindet
  Ereignisse.** `HeadsetCallControl.PushState` zog den Gabelzustand des Headsets
  nach, indem es ihn mit `SyncHook(imGespraech)` in genau das Feld schrieb, aus
  dem der Lese-Thread seine Flanken ableitet. Damit stand dort „abgenommen",
  sobald ein Gespräch verbunden war — **ohne dass das Gerät je eine Taste
  gemeldet hatte**. Und weil der Zustand aus der *Abwesenheit* der Hook-Usage
  abgeleitet wurde, war der nächste gewöhnliche Eingangsreport eine Flanke nach
  unten, die es physisch nie gab: **10 ms nach jedem Annehmen legte nipp wieder
  auf**, bei ausgehenden Anrufen 331 ms nach dem Verbinden. Der Beweis steckte
  im Abstand und darin, dass es auch bei einer Annahme **im Toast** geschah, wo
  niemand eine Taste berührt hatte. Ein Spiegel dessen, was ein Gerät gemeldet
  hat, wird nur aus Gerätemeldungen gefüllt; was nipp will, ist eine
  **Erwartung mit Ablauf** (`HookWatch`) — nicht dasselbe Feld. Gemessen am
  09.09.2026, Nachtrag zu ADR-028.
- **`HidD_SetOutputReport` ist der falsche Weg für Output-Reports — es hängt.**
  Am Jabra Engage 75 gemessen: **10,9 s, 13,8 s, 21,3 s und 83,2 s** für einen
  Report von drei Byte. Die Funktion nimmt die Control-Pipe; der reguläre Weg
  ist `WriteFile` über die Interrupt-Out-Pipe, und genau den ging auch HIDAPI —
  das Modul, dessen Entfernung aus dem SDK ADR-028 überhaupt ausgelöst hat.
  Nach der Umstellung: **3 Millisekunden**. Ein eigenes Handle dafür, denn ein
  `FileStream` ist nicht threadsicher und im Lese-Thread steht ein blockierender
  `Read`.
- **Und was daran hing, war keine Lampe.** Der Ring-Report erreichte das Gerät
  nie rechtzeitig — deshalb tat „das Headset aus der Ladeschale nehmen" nichts:
  das Gerät wusste nicht, dass es klingelt. Dazu läutete es nach dem Annehmen
  weiter, und die Taste wirkte „verzögert und unkontrollierbar". **Drei
  Meldungen aus dem Alltag, ein Aufruf.** Der Hinweis, der zählte: dieselbe
  Bedienung funktioniert in Bria am selben Headset — **wenn ein anderes
  Programm es kann, ist es kein Gerätefehler.**
- **Eine Schutzregel, deren Fenster von einer Latenz abhängt, ist nur so gut wie
  die Latenz.** Solange ein eigener Report unterwegs ist und 800 ms darüber
  hinaus, gilt jede Gabelmeldung als Antwort des Geräts und nicht als Absicht
  (`HookWatch`) — richtig bei 3 ms, und bei 83 Sekunden hätte dasselbe Fenster
  jeden Tastendruck verworfen. Die Protokollzeile nennt deshalb die **Dauer**:
  ohne sie waren „läutet weiter" und „nimmt nicht ab" zwei Fehler statt einer.
- **Und `HookSwitch` (0x20) ist an diesem Gerät kein Tastendruck, sondern ein
  Zustand, den das Gerät mitverhandelt.** Auf „ein Gespräch läuft" hin gibt das
  Engage 75 seinen Off-Hook-Zustand **auf**. Die Annahme, ein Gerät spiegele
  den gemeldeten Zustand, war der zweite Anlauf und auch falsch: die Antwort kam
  in der Gegenrichtung. **Was trägt, ist nicht „was hat das Gerät gemeldet",
  sondern „habe ich gerade selbst hineingeredet"** — das kann nipp beantworten,
  ohne über das Gerät etwas zu wissen.
- **Und die Bedeutung eines Tastendrucks gehört nicht an den Gerätezustand.**
  Dieselbe Wurzel hatte T82 („jeder zweite Druck falsch", nachdem ein Gespräch
  in der Oberfläche endete). Beides ist weg, seit die Frage „was heisst dieser
  Druck" allein aus den laufenden Anrufen beantwortet wird — klingelt es,
  annehmen; sonst das Gespräch im Vordergrund auflegen. Der globale Hotkey
  machte das seit immer so und hat nie ein Gespräch verloren; die Regel stand
  zweimal im Code, und die Fassung am Headset war die falsche. Jetzt einmal, in
  `HeadsetPolicy.Interpret`. Nebenwirkung, die zählt: ob ein Gerät seine Taste
  als Schalter oder als Momentan-Taster meldet, ist gleichgültig — und nipp
  läuft an drei Headsets.
- **Über empfangene HID-Reports stand nie eine Zeile im Protokoll.** Deshalb
  war „die Taste tut nichts" nicht von „hier kommt gar nichts an" zu
  unterscheiden, und der Fehler lag zwei Tage im Log ohne Spur. Auf Debug
  steht jetzt jeder Ein- und Ausgangsreport da, samt der Frage, ob das Gerät
  Usage `0x20` überhaupt führt; `tools\Test-Headset.ps1` wertet es aus und
  prüft den Befund gegen. **Dieselbe Lücke wie beim Symbol im Infobereich** —
  zum zweiten Mal.
- **Ein bestandenes „Auflegen" belegt keine funktionierende Taste.** T79 galt
  seit dem 07.09.2026 als bestanden, T80 (Annehmen) hatte nie ein Ergebnis —
  und in **keinem** Protokoll dieses Projekts steht je „Annehmen am Headset
  gedrueckt". Die Taste traf zufällig eine der beiden Richtungen. Wer eine
  Taste abnimmt, löst **beide** Bedeutungen einmal aus.
- **Ein HID-Gerät nummeriert seine Reports selbst.** Beim Jabra Link 400 ist
  die Report-Kennung 2. Kennung, Tasten und Lampen kommen aus dem
  Report-Deskriptor (`HidP_GetButtonCaps`, `HidP_GetUsages`,
  `HidP_SetUsages`), nie aus dem Quelltext. Und die Off-Hook-Lampe ist keine
  Anzeige: das Gerät führt seinen eigenen Gabelzustand, und wer ihn nicht
  nachzieht, hat ab dem ersten in der Oberfläche beendeten Gespräch **jeden
  zweiten Tastendruck falsch**.
- **Ein Klick auf das Symbol im Infobereich ist kein Mausereignis.** Der
  Doppelklick öffnete das Fenster nicht mehr, und der Handler dafür war da:
  `MouseEventReceived` mit `IconLeftDoubleClick`. Er trifft nur nie ein.
  H.NotifyIcon registriert das Symbol als `NOTIFYICON_VERSION_4`
  (`TrayIcon.Version` steht auf `Vista`, mit **privatem** Setter — nicht
  umstellbar), und in diesem Modus schickt die Shell keine Mausnachrichten
  mehr, sondern `NIN_SELECT`. Die Bibliothek meldet das als
  **`KeyboardEvent.Select` über `KeyboardEventReceived`** — ein ganz anderes
  Ereignis; einen Doppelklick gibt es dort begrifflich nicht. Nachgemessen,
  indem die Nachrichten von Hand an das Nachrichtenfenster geschickt wurden
  (`CallbackMessageId` ist `0x400`, nicht `0x800`): `WM_LBUTTONDBLCLK` ergibt
  `IconDoubleClick` **und** `IconLeftDoubleClick`, `NIN_SELECT` ergibt beim
  Mausereignis **nichts**. Beide Ereignisse sind jetzt abonniert, und jedes
  empfangene steht auf Debug im Protokoll — ohne das war „der Doppelklick tut
  nichts" nicht von „hier kommt gar nichts an" zu unterscheiden.
- **Und es kommt nicht auf dem UI-Thread an.** H.NotifyIcon führt für sein
  Symbol ein eigenes Nachrichtenfenster mit eigener Pumpe. `AppWindow.Show()`
  von dort aus wirft `COMException: „Unzulässiges Fenster. Es gehört zu einem
  anderen Thread."` — und das Fenster bleibt weg. `App` reicht den Aufruf
  jetzt über die `DispatcherQueue`, wie `OnRedirectedActivation` es von Anfang
  an tat. Verraten hat es erst eine Protokollzeile im Rückfallpfad: der
  Win32-Weg (`ShowWindow` + `SetForegroundWindow`) hatte das Fenster gezeigt
  und dabei aufgeschrieben, dass der reguläre Weg gescheitert war. **Ein Weg,
  dessen Erfolg niemand prüft, fällt genau dann aus, wenn er gebraucht wird** —
  und dieser hier ist der einzige zurück ins Fenster (§10).
- **Beim Messen von Fenstersichtbarkeit ist `Process.MainWindowHandle` eine
  Falle.** Für einen Prozess ohne sichtbares Hauptfenster liefert es **0**, und
  `IsWindowVisible(0)` ist immer `false`. Damit „bewies" ein Test zweimal einen
  Fehler, den es nicht gab (`ShowFromTray` sei kaputt). Das Handle gehört über
  `EnumWindows` und die Fensterklasse geholt (`WinUIDesktopWin32WindowClass`
  für das Fenster, `H.NotifyIcon_*` für das Nachrichtenfenster) — oder vor dem
  Verstecken einmal gemerkt.
- **Ein Logo, das nicht nachkommt, liegt an drei Stellen fest — keine davon
  ist der Code.** Der Explorer-Symbolcache (ein angepinnter Verweis liest das
  Symbol nicht neu, solange der Pfad gleich bleibt), ein **verwaistes
  registriertes Paket** (zeigt auf ein Verzeichnis ohne `AppxManifest.xml`,
  weil der letzte Build unpackaged war — Windows behält dann seine gecachten
  Kachelbilder), und fehlende Zielgrössen: für eine angepinnte **packaged** App
  liest Windows `Square44x44Logo.targetsize-<N>_altform-unplated.png`, nicht
  `Square44x44Logo.png`. Davon lag lange nur die 24er im Paket. Die Reihenfolge
  bei der Suche: zuerst prüfen, **wie** gestartet wurde. Rezepte in
  `docs/packaging.md`.

### Konfiguration, Karten und Modelle

- **Ein `[JsonConverter]` an einer Eigenschaft schlägt den Konverter aus den
  Optionen.** Die Enums der Integrationsdatei trugen
  `[JsonConverter(typeof(JsonStringEnumConverter))]` **ohne** Benennungsregel,
  während der Store seine Optionen mit `CamelCase` aufsetzte — nipp schrieb
  `"Bearer"` und `"ActiveExpanded"`, wo jede Vorlage `"bearer"` zeigt.
  **Aufgefallen wäre das nie:** Enums werden unabhängig von der Schreibweise
  gelesen, eine von Hand geschriebene Vorlage lief, eine ausgegebene Datei
  lief, und dass beide verschieden aussahen, merkte nur, wer sie
  nebeneinanderlegte. Jetzt `CamelCaseEnumConverter`, und die JSON-Optionen
  liegen an **einer** Stelle (`IntegrationJson`) statt in zwei Kopien, von
  denen einer der Enum-Konverter fehlte.
- **`WhenWritingNull` verliert die Bedeutung von `null`, wenn die Vorgabe nicht
  `null` ist.** `CardField.EmptyText = null` heisst „die Zeile verschwindet";
  die Vorgabe ist `"—"`. Beim Speichern fiel das `null` weg, beim Lesen griff
  die Vorgabe — **eine Karte änderte auf dem Weg durch die Datei ihr
  Verhalten**, und die mitgelieferten setzen `null` sechsmal. Dieselbe Falle
  steckte in `SearchContactsCapability.TimeoutMs`. Wer ein Feld hat, bei dem
  `null` etwas bedeutet und die Vorgabe etwas anderes ist, schreibt
  `[JsonIgnore(Condition = JsonIgnoreCondition.Never)]` daran.
- **Ein Rückweg, der seinen Eingang unverändert ausgibt, passt auf alles.**
  `DraftValueMode.Field` gibt seinen Text unverändert aus und war damit ein
  gültiger Rückweg für **jeden** Ausdruck — `Expression` war nie erreichbar.
  Zeichengleich war es trotzdem, der Fehler also unsichtbar; erst der
  Eigenschaftenbereich hätte `concat(a, ' ', b)` als „Feld" mit diesem Namen
  angeboten. Wer eine Zeichenkette in Formulare zerlegt, prüft den Rückweg
  **zeichengleich** — und muss dann noch prüfen, dass die Kandidaten nicht
  alles fressen.
- **Nicht alles, was konfigurierbar sein soll, lässt sich lesbar
  konfigurieren.** Der Toast setzt seine erste Zeile aus drei Werten zusammen
  und lässt Teile weg; als Karte wären das drei
  `concat(if(isEmpty(...)))`-Ungetüme, im Editor nur als „Ausdruck"
  bearbeitbar. Die bewährte Fassung bleibt deshalb Code, und eine eingerichtete
  Karte **ersetzt** sie (ADR-034). Der Grundsatz „die mitgelieferten Karten
  sind gewöhnliche Beschreibungen" gilt für die beiden anderen Arten
  unverändert.
- **Eine Angabe, die nur die erste nimmt, versteckt den Rest vollständig.**
  `Contact.PrimaryNumber` ist `Numbers[0]`. `ContactRow` gab nur die weiter,
  und damit war die Mobilnummer eines CRM-Kontakts in der ganzen
  Oberfläche unerreichbar — Liste **und** Vorschlagsliste im Wählfeld. Das
  Mapping war die ganze Zeit richtig; gesucht wurde der Fehler dort. Wenn ein
  Modell eine Liste führt und die Oberfläche ein Feld zeigt, ist die Frage
  nicht ob, sondern wo etwas verlorengeht.
- **Ein Name, der wie eine Antwort klingt, ist keine.**
  `CallInfo.DisplayLabel` hiess wie die Antwort auf „wie heisst der Anrufer",
  stand am zentralsten Typ der Telefonie — und kannte keine Kontakte. Sein
  Kommentar sagte es sogar („solange kein Kontakt aufgelöst ist"), nur las ihn
  niemand mehr: der Name allein reichte, um ihn überall einzusetzen. Bei
  eingehenden Anrufen kaschierte der Anzeigename der Anlage die Lücke, bei
  **ausgehenden** stand deshalb die blosse Nummer in Kopfzeile, Makel-Liste,
  Toast und Infobereich. **Wer einer Eigenschaft einen Namen gibt, der mehr
  verspricht als sie hält, baut die nächste Fehlersuche ein.** Gelöscht mit
  ADR-043; die Frage beantwortet jetzt `CallPartyResolver`, und zwar zweimal
  verschieden — „Name oder nichts" und „nie leer" sind nicht dieselbe Frage.
- **Ein Windows-Toast nimmt drei Textzeilen, nicht mehr.** Plus die
  Attributionszeile, die klein und grau darunter steht. Was nicht passt,
  schneidet Windows mitten im Wort ab. Und was darin steht, **bleibt im
  Benachrichtigungscenter liegen**, bis jemand es wegklickt — bei
  Gesprächsinhalten ist das eine Entscheidung, keine Anzeige (ADR-030,
  ADR-027). Der Toast wird deshalb beim Anrufende entfernt.

### Bauen, Tests und Werkzeug

- **Ein Test, der Fehlalarme liefert, wird abgeschaltet und schützt dann
  nichts.** Der erste Anlauf von `UserTextTests` las ganze Zeilen und meldete
  zwei Dutzend Kommentare; der zweite suchte `ae|oe|ue` als Buchstabenfolge und
  meldete «zuerst», «Steuer» und «genauer». Was trägt, ist eine **Wortliste**
  der Formen, die in diesem Projekt wirklich aufgetreten sind — sie hat keine
  Fehlalarme und wächst um einen Eintrag, wenn jemand eine neue findet.
  Dasselbe beim Kontrasttest: 4,5:1 für **alles** meldete Trennlinien, also
  ordnet jetzt eine Tabelle jede Farbe ausdrücklich ein.
- **Ein Skript, das Code schreibt, frisst Backslashes.** Beim Einsetzen eines
  Regex-Musters wurde aus `` ein **Backspace-Zeichen** (0x08), und das Muster
  stand danach buchstäblich als `@"<BS>(nonce|…)"` im Quelltext. Es passte auf
  nichts, der Build war grün, und die Tests meldeten nur «nicht maskiert».
  Gefunden hat es erst `cat -A` auf der Attributzeile. **Wer ein Muster über
  ein Skript einsetzt, liest die erzeugte Zeile byteweise nach** — nicht das
  Skript.
- **`SqliteConnection.Dispose` schliesst die Datei nicht, es gibt sie an den
  Pool zurück.** Eine kaputte `history.db` liess sich deshalb nicht zur Seite
  legen: `File.Move` traf auf ein offenes Handle. Der Test dazu lief **einzeln
  grün und im vollen Lauf rot** — genau das Zeitverhalten, das ein Pool
  erzeugt. `SqliteConnection.ClearPool` vor dem Zug.

- **Ein Editor gehört in den Kern, nicht ins Fenster.** `Nipp.App` hat kein
  Testprojekt, und ein Editor hat mehr Zustand als alles andere: Auswahl,
  Einfügen, Verschieben, Rückgängig, Prüfung, Vorschau. Rückgängig über
  **Schnappschüsse** und nicht über umgekehrte Befehle — eine Karte hat zwanzig
  Bausteine, ein Schnappschuss kostet nichts, und man kann ihn nicht falsch
  zurücklegen.
- **Eine `.gitignore`-Regel kann Quellcode verschlucken, und lokal merkt es
  niemand.** `secrets/` stand dort für Zugangsdaten und traf den Quellordner
  `Services/Integrations/Secrets/` mit: **`IntegrationSecrets.cs` war seit ihrem
  ersten Tag nie eingecheckt.** Hier baute alles — die Datei lag ja da. Der
  erste frische Klon (GitHub Actions, 07.09.2026) brach mit zehn Compilerfehlern
  ab, und **keiner zeigte auf die fehlende Datei**: gemeldet wurden die
  Verwender eines Namensraums, den es dort nicht gab. Wer so eine Meldung sieht,
  prüft zuerst `git ls-files`, nicht die Verwender. Muster für Ablageordner
  gehören verankert (`/secrets/`), und eine Ausnahme mit `!` hilft nicht, wenn
  schon das Verzeichnis ausgeschlossen ist. `RepositoryCompletenessTests` prüft
  das jetzt.
- **Eine Ersetzung über das ganze Repo trifft auch Bezeichner.** Beim
  Entfernen zweier Systemnamen (ADR-040) wurde aus dem Namen eines Testfeldes
  mitten im Code ein ganzer Satz — der Compiler fing es, aber erst nach sechzig
  Dateien. Und was er
  **nicht** fängt, ist der Fliesstext: „nicht aus das CRM", „die das
  CRM-Suche". Wer so etwas macht, baut danach einmal, liest den Diff auf
  Grammatik durch und lässt einen Wächter zurück (`PublicRepositoryTests`) —
  sonst kommt der Name innerhalb eines Monats zurück.
- **`settings.json` ist PascalCase, `integrations.json` camelCase.**
  `SettingsService` setzt keine Benennungsregel, `IntegrationJson` schon. Ein
  Test, der eine „alte settings.json" in camelCase nachbaut, ist deshalb grün,
  ohne je eine echte Datei zu beschreiben: er lädt schlicht null Einträge, und
  genau das prüft er dann. Wer eine Datei nachbaut, sieht vorher in eine echte.
- **`System.Threading.Lock` gibt es erst ab .NET 9.** Hier ist .NET 8 LTS
  festgelegt (§4); `private readonly Lock _gate = new()` baut nicht. `object`
  nehmen.
- **`dotnet format` auf Solution-Ebene richtet in diesem Repo Schaden an.** Es
  zieht in `SipService.cs` einen `using`-Alias über den Kommentar, der ihn
  erklärt, und entfernt BOMs in sechzig unbeteiligten Dateien. Nur die eigenen
  Dateien formatieren.
- **Das deutsche Anführungszeichenpaar endet mit einem geraden `"` — und das
  beendet den String.** In diesem Projekt stehen Benutzertexte in XAML und C#
  (ADR-021), und geschrieben werden sie als `„Text"`. Das Schlusszeichen ist
  ASCII: in einem C#-Literal endet dort die Zeichenkette, in einem
  XAML-Attribut das Attribut. Der Compiler meldet dann `CS1002 ; erwartet`
  beziehungsweise `WMC9997`, und **beide Meldungen zeigen auf die Folgezeile**,
  nicht auf das Zeichen. Am 11.09.2026 viermal zugeschlagen. In Meldungen, die
  im Code stehen, deshalb `«…»` nehmen — in Markdown und in XML-Kommentaren
  bleibt `„…"` richtig.
- **Kein Backslash in einer `[LoggerMessage]`-Vorlage.** Der Quellgenerator
  übernimmt den Text unverändert in ein Literal, und `\s` ist dort eine
  ungültige Escapesequenz (CS1009). Schrägstriche nehmen.
- **Eine Wanderung fragt die Tabelle, nicht die Fassungsnummer.** `history.db`
  hat am 07.09.2026 ihre erste bekommen (`seen_at`, Fassung 2). Der naheliegende
  Weg — bei `user_version < 2` ein `ALTER TABLE` — ist an einer Stelle falsch,
  die man beim Schreiben nicht sieht: eine **frisch angelegte** Datei bringt die
  Spalte schon aus `CREATE TABLE` mit und steht trotzdem auf `user_version 0`.
  Das `ALTER TABLE` wäre dort ein „duplicate column name" — beim **ersten Start
  auf einem neuen Gerät**, also ausgerechnet dort, wo nichts zu wandern war.
  `PRAGMA table_info` fragen; der Weg ist dann unabhängig davon, wie die Datei
  in ihren Zustand gekommen ist. Und: die Anrufliste ist die einzige
  Nutzerdatei, die nipp **nicht** wiederherstellen kann — vor dem ersten Lauf
  am Gerät kopieren.
- **Eine Protokollzeile, die eine Ursache nahelegt, die sie nicht gemessen hat,
  ist schlechter als keine — zum zweiten Mal.** Seit nipp zwei systemweite
  Kürzel anmeldet, stand untereinander: «Tastenkuerzel Ctrl+Shift+A ist bereits
  von einer anderen Anwendung belegt» und «Tastenkuerzel abgeschaltet». Die
  zweite Zeile gehörte zum **zweiten** Kürzel — dem leeren Stummkürzel, das
  nipp erwartungsgemäss nicht anmeldet — und las sich wie die Folge der ersten.
  Wer mehrere gleichartige Dinge protokolliert, schreibt in **jede** Zeile,
  welches gemeint ist; und zwar in der Sprache der Oberfläche («stumm
  schalten»), nicht als Bezeichner, denn das Protokoll landet beim Support.
  Dasselbe Muster wie beim Rufton, wo eine Zeile «die Gegenstelle schickt Early
  Media ohne Audio» behauptete, obwohl beide Male Audio da war.
- **Ein Wächtertest, der anschlägt, wird begründet erweitert und nicht
  umgangen.** `HistoryStoresNoContextTests` zählt die erlaubten Spalten der
  Anrufliste auf und meldete `seen_at`. Er hatte recht zu fragen; die Antwort
  („ein Zeitstempel, kein Gesprächsinhalt — ADR-027 ist nicht berührt") steht
  jetzt als Kommentar an der Liste. Wer sie ohne Satz erweitert, nimmt der
  nächsten Änderung die Prüfung.
- **Tests nie auf `%APPDATA%` oder `%LOCALAPPDATA%`.** Ein `dotnet test` hat
  das SIP-Konto des Benutzers samt Passwort gelöscht. `SettingsService`,
  `SecretStore` und `DiagnosticsBundle` nehmen den Pfad per Konstruktor;
  `TestIsolationTests` erzwingt es.
- **Unpackaged immer mit `-t:Rebuild`** bauen — ein inkrementeller Build
  stirbt mit REGDB_E_CLASSNOTREG (docs/packaging.md).

### Packaged und unpackaged

**Seit ADR-038 sind es drei Wege, nicht zwei:** packaged (MSIX), unpackaged
(der Entwicklungsalltag) und **publish self-contained** (die Auslieferung).
Wer der nativen Kette ein Ziel hinzufügt, bedenkt alle drei — die publish-
Lücke war grün gebaut und hätte erst beim Kunden gefehlt.

**Packaged und unpackaged teilen ein Ausgabeverzeichnis und räumen sich gegenseitig ab.** Belegt am
07.09.2026:

| Build | `AppxManifest.xml` | `Assets\` |
|---|---|---|
| `build src\Nipp.App -c Debug -t:Rebuild` (packaged) | da | **war leer** |
| dasselbe mit `-p:WindowsPackageType=None` (unpackaged) | **weg** | gefüllt |

Das kostete alle Symbole gleichzeitig — Infobereich, Titelleiste und
Startmenü —, weil `bin\...\win-x64\` die Paketwurzel bei
`Add-AppxPackage -Register` ist. Sie lagen vorher nur dort, weil ein früherer
unpackaged Build sie hinterlassen hatte. Die neun einzelnen `Content`-Zeilen im
csproj sind jetzt ein Sammeleintrag mit `CopyToOutputDirectory` und nehmen auch
`TrayLight.ico`, `TrayDark.ico` und die `nipp-*.png` mit, die vorher gar nicht
darin standen. **Wer die Lösung baut, baut danach den Weg neu, den er
benutzt.**

**Der Alltag läuft unpackaged, und das war eine Folge, keine Wahl: packaged
bekommt keine Toasts.** `AppNotificationManager.Register()` scheiterte dort mit
`0x80004005` (`E_FAIL`), weil im Manifest zwei Erweiterungen fehlten.
Unpackaged legt `Register()` die COM-Einträge selbst an und funktioniert.

**Aufgefallen ist es, weil ein Wechsel des Startwegs wie ein Fehler im Code
aussah:** ab dem ersten `shell:appsFolder`-Start stand „Benachrichtigungen
nicht verfügbar" im Protokoll, und ein eingehender Anruf hätte bei geschlossenem
Fenster **kein Zeichen** gegeben. Wer einen Rückschritt sucht, vergleicht
deshalb zuerst, **wie** gestartet wurde — die Zeile „Autostart regelt der
StartupTask des Pakets" verrät packaged.

**Stand 07.09.2026 abends: die beiden Erweiterungen sind eingetragen, und es
geht trotzdem noch nicht.** `com:ComServer` mit einer `com:Class` und
`desktop:Extension Category="windows.toastNotificationActivation"` gehören
zusammen und müssen dieselbe CLSID tragen; beide stehen jetzt im Manifest. Der
Fehler ist dadurch von `0x80004005` auf `0x80070490` (ERROR_NOT_FOUND)
gewandert, und die Benachrichtigungsplattform registriert nipp jetzt
erfolgreich (Ereignis 2413) — die Erweiterungen waren also **nötig und nicht
ausreichend**. Die verbleibende Ursache liegt in `Register()` selbst;
ausgeschlossen sind fehlende Namensräume, ein veralteter Registrierungscache
und eine fehlende CLSID in der Registrierung (bei MSIX virtualisiert, also kein
Hinweis). Offen als **T110**, Details im Nachtrag zu ADR-008.

**Packaged testen:** `Add-AppxPackage -Register <out>\AppxManifest.xml`, dann
`Start-Process "shell:appsFolder\bv2.nipp_b06ws4ca5ehbg!App"`. Nie über
`dotnet run` oder die EXE direkt — ohne Paketidentität scheitert die Runtime
mit REGDB_E_CLASSNOTREG.


---

## Integrationsplattform: was beim Anbinden zu lernen war


- **Das Anmeldeschema ist je Quelle verschieden, und die Kopfzeile lügt.**
  das CRM verlangt `Authorization: Token`, das Gesprächsjournal `Bearer` — und **beide**
  senden bei `401` dasselbe `WWW-Authenticate: Bearer`. Dafür gibt es
  `auth.scheme` (Standard `Bearer`). Das Schema gehört nie in den
  Geheimniswert, sonst steckt ein Teil des Protokolls in der DPAPI-Ablage, wo
  es niemand vermutet.
- **Eine Vorlage wird nie eingeschaltet ausgeliefert**, sondern nach dem
  Hinzufügen per Testabruf geprüft und dann eingeschaltet. Ein Test erzwingt
  das — seit dem 07.09.2026 auch für den Katalog.
- **Gegen die echte Antwort testen, nicht gegen die vereinbarte.**
  `ExpectedResponseTests` hält beides. Ein Befund steht darin fest:
  `summary_short` von das Gesprächsjournal ist nicht kurz geschrieben, sondern die lange
  Fassung bei 120 Zeichen abgeschnitten, mitten im Wort.
- **Eine Rufnummer ist keine Zahl.** `decimal.TryParse("+41791234567")`
  gelingt — das `+` ist ein Vorzeichen. `ContextValue` wandelt Text deshalb
  nirgends stillschweigend um; wer eine Zahl will, schreibt `as: number`.
- **Ein Objekt ist nicht dasselbe wie ein fehlendes Feld.** Sonst ist
  `isEmpty($.contact)` immer wahr, und jede Antwort gilt als leer.
- **Datum nur in ISO-Form annehmen.** `06.09.2026` ist anderswo der 9. Juni.
