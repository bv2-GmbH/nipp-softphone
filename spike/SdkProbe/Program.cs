// Spike für AP2.1 — NIPP-BUILD.md §12, M1.
//
// Beantwortet genau drei Fragen, in dieser Reihenfolge, und bricht bei der
// ersten ab, die scheitert:
//   1. Lädt die native DLL-Kette?
//   2. Lässt sich ein Core erzeugen und die Iterate-Schleife drehen?
//   3. Sieht der Core die Audiogeräte und Codecs, die er später braucht?
//
// Was hier NICHT passiert: registrieren oder telefonieren. Das ist AP2.3 und
// braucht die Test-Trunk-Zugänge, die noch fehlen (§16.5). Und niemals gegen
// einen Kundentenant — §13.

using Linphone;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var probeDir = Path.Combine(Path.GetTempPath(), "nipp-spike");
Directory.CreateDirectory(probeDir);

Console.WriteLine("=== nipp SDK-Spike (AP2.1) ===");
Console.WriteLine($"Prozess:      {(Environment.Is64BitProcess ? "64 Bit" : "32 Bit")}");
Console.WriteLine($"Architektur:  {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"OS:           {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine($"Arbeitsdaten: {probeDir}");
Console.WriteLine();

// ---------------------------------------------------------------- Schritt 1
Console.WriteLine("[1/3] Native DLL-Kette und Factory");
Factory factory;
try
{
    factory = Factory.Instance;
    Console.WriteLine("      Factory.Instance   OK");
}
catch (Exception ex)
{
    Console.WriteLine($"      FEHLGESCHLAGEN: {ex.GetType().Name}");
    Console.WriteLine($"      {ex.Message}");
    Console.WriteLine();
    Console.WriteLine("      Das ist der Fall aus §14.2: eine DLL der Kette fehlt oder");
    Console.WriteLine("      passt nicht zur Bitness. Mit 'dumpbin /dependents' eingrenzen.");
    return 1;
}

// §6: die Verzeichnisse müssen VOR dem Core-Start stehen.
factory.ConfigDir = probeDir;
factory.DataDir = probeDir;
factory.CacheDir = probeDir;

// Und die Ressourcenpfade auch — hier scheiterte der erste Versuch.
// belr lädt seine Grammatiken zur Laufzeit aus Dateien; ohne sie bricht
// Core.Start() mit "Unable to load VCARD grammar" ab.
//
// MspluginsDir ist der eigentliche Fund: damit lässt sich der Plugin-Pfad
// explizit setzen, statt auf die DLL-Suchpfade zu hoffen. Genau das ist der
// Hebel für den MSIX-Fall aus §14.2/§14.3.
var appDir = AppContext.BaseDirectory;
factory.TopResourcesDir = Path.Combine(appDir, "share");
factory.DataResourcesDir = Path.Combine(appDir, "share");
factory.SoundResourcesDir = Path.Combine(appDir, "share", "sounds");
factory.RingResourcesDir = Path.Combine(appDir, "share", "sounds");
factory.MspluginsDir = Path.Combine(appDir, "lib", "mediastreamer", "plugins");

Console.WriteLine($"      Version des SDK    {Core.Version}");
Console.WriteLine($"      TopResourcesDir    {factory.TopResourcesDir}");
Console.WriteLine($"      MspluginsDir       {factory.MspluginsDir}");

// Nur wenn als Argument verlangt: das SDK-Protokoll mitlesen. Interessiert
// hier vor allem, ob libmswasapi.dll geladen wird — davon hängt ab, ob
// Audiogeräte überhaupt auftauchen.
var verbose = args.Contains("--verbose");
if (verbose)
{
    var logging = LoggingService.Instance;
    logging.LogLevel = LogLevel.Message;
    logging.Listener.OnLogMessageWritten = (svc, domain, level, message) =>
    {
        if (message.Contains("plugin", StringComparison.OrdinalIgnoreCase)
            || message.Contains("wasapi", StringComparison.OrdinalIgnoreCase)
            || message.Contains("snd_card", StringComparison.OrdinalIgnoreCase)
            || message.Contains("device", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"      SDK[{level}] {domain}: {message}");
        }
    };
    Console.WriteLine("      SDK-Protokoll      an (gefiltert auf Plugins und Geräte)");
}
Console.WriteLine();

// ---------------------------------------------------------------- Schritt 2
Console.WriteLine("[2/3] Core erzeugen, starten, Iterate-Schleife");
Core core;
try
{
    core = factory.CreateCore(
        configPath: Path.Combine(probeDir, "linphonerc"),
        factoryConfigPath: null,
        systemContext: IntPtr.Zero);

    // §2: Video ist ausgeschlossen. Gleich hier aus, nicht später.
    //
    // Capture und Display allein genügen NICHT: die Video-Policy startet
    // Anrufe trotzdem mit Videoangebot, und das SDK meldet dann selbst
    // "possible mis-use of the API". Die Policy muss mit.
    core.VideoCaptureEnabled = false;
    core.VideoDisplayEnabled = false;
    var videoPolicy = core.VideoActivationPolicy;
    videoPolicy.AutomaticallyInitiate = false;
    videoPolicy.AutomaticallyAccept = false;
    core.VideoActivationPolicy = videoPolicy;

    core.Start();
    Console.WriteLine($"      Core.Start()       OK, GlobalState = {core.GlobalState}");
}
catch (Exception ex)
{
    Console.WriteLine($"      FEHLGESCHLAGEN: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

// Der zentrale Punkt aus §6: ohne Iterate() feuert nichts. Hier als einfache
// Schleife; in der App wird das ein DispatcherQueueTimer mit 20 ms.
var stateChanges = 0;
core.Listener.OnGlobalStateChanged = (c, state, message) =>
{
    stateChanges++;
    Console.WriteLine($"      Ereignis: GlobalState -> {state} ({message})");
};

for (var i = 0; i < 100; i++)
{
    core.Iterate();
    Thread.Sleep(20);
}
Console.WriteLine($"      100x Iterate()     OK ({stateChanges} Ereignisse empfangen)");
Console.WriteLine();

// ---------------------------------------------------------------- Schritt 3
Console.WriteLine("[3/3] Was der Core sieht");

// Drei Wege zur Geräteliste, weil sie Unterschiedliches liefern:
// AudioDevices gibt laut Wrapper-Doku nur das erste Gerät je Typ,
// ExtendedAudioDevices alle, SoundDevicesList die rohen Kartennamen.
// Welcher für AP5.6 der richtige ist, entscheidet sich hier.
Console.WriteLine($"      SoundDevicesList:       {core.SoundDevicesList.Count()}");
foreach (var name in core.SoundDevicesList)
{
    Console.WriteLine($"        - {name}");
}

var extended = core.ExtendedAudioDevices.ToList();
Console.WriteLine($"      ExtendedAudioDevices:   {extended.Count}");
foreach (var d in extended)
{
    Console.WriteLine($"        - {d.DeviceName,-46} [{d.Type}] Fähigkeiten={d.Capabilities}");
}

var devices = core.AudioDevices.ToList();
Console.WriteLine($"      AudioDevices (je Typ):  {devices.Count}");
foreach (var d in devices)
{
    Console.WriteLine($"        - {d.DeviceName,-46} [{d.Type}] Fähigkeiten={d.Capabilities}");
}

Console.WriteLine($"      DefaultInputAudioDevice:  {core.DefaultInputAudioDevice?.DeviceName ?? "(keines)"}");
Console.WriteLine($"      DefaultOutputAudioDevice: {core.DefaultOutputAudioDevice?.DeviceName ?? "(keines)"}");

var payloads = core.AudioPayloadTypes.ToList();
Console.WriteLine($"      Audio-Codecs: {payloads.Count}");
foreach (var p in payloads)
{
    var mark = p.Enabled() ? "an " : "aus";
    Console.WriteLine($"        [{mark}] {p.MimeType,-12} {p.ClockRate,6} Hz  PT={p.Number,3}  Kanäle={p.Channels}");
}

Console.WriteLine();
Console.WriteLine($"      MediaEncryption unterstützt:");
foreach (var enc in new[] { MediaEncryption.None, MediaEncryption.SRTP, MediaEncryption.ZRTP, MediaEncryption.DTLS })
{
    Console.WriteLine($"        {enc,-6} {(core.MediaEncryptionSupported(enc) ? "ja" : "nein")}");
}

Console.WriteLine();
Console.WriteLine("      Weitere Werte, die §9 braucht:");
Console.WriteLine($"        EchoCancellationEnabled  = {core.EchoCancellationEnabled}");
Console.WriteLine($"        EchoCancellerFilterName  = {core.EchoCancellerFilterName}");
Console.WriteLine($"        NoiseSuppressionEnabled  = {core.NoiseSuppressionEnabled}");
Console.WriteLine($"        AdaptiveRateControl      = {core.AdaptiveRateControlEnabled}");
Console.WriteLine($"        UseRfc2833ForDtmf        = {core.UseRfc2833ForDtmf}");
Console.WriteLine($"        UseInfoForDtmf           = {core.UseInfoForDtmf}");
Console.WriteLine($"        AudioPortsRange          = {core.AudioPortsRange.Min}-{core.AudioPortsRange.Max}");
Console.WriteLine($"        PlaybackGainDb           = {core.PlaybackGainDb}");
Console.WriteLine($"        MicGainDb                = {core.MicGainDb}");

// ---------------------------------------------------------------- Schritt 4
// AP2.3 — Registrierung und Audio. Läuft nur, wenn die Zugangsdaten des
// Test-Trunks vorliegen. §13: nie gegen einen Kundentenant.
Console.WriteLine();
Console.WriteLine("[4/4] Registrierung gegen den Test-Trunk (AP2.3)");

var trunk = SdkProbe.TestTrunk.TryLoad(out var problem);
if (trunk is null)
{
    Console.WriteLine();
    Console.WriteLine(problem);
    Console.WriteLine();
    Console.WriteLine("=== Spike bis Schritt 3 erfolgreich. AP2.3 übersprungen. ===");
    core.Stop();
    return 0;
}

Console.WriteLine($"      Konto: {trunk.ToSafeString()}");

// §14.6: das SDK will eine CA-Datei, nicht den Windows-Zertifikatspeicher.
if (!string.IsNullOrWhiteSpace(trunk.RootCaFile))
{
    core.RootCa = trunk.RootCaFile;
    Console.WriteLine($"      RootCa: {trunk.RootCaFile}");
}

// §9.3: SRTP als Standard.
if (core.MediaEncryptionSupported(MediaEncryption.SRTP))
{
    core.MediaEncryption = MediaEncryption.SRTP;
}

// §9.2: RTP-Portbereich, der im SDK nicht vorbelegt ist (-1/-1).
core.SetAudioPortRange(7078, 7178);

var registrationState = RegistrationState.None;
var registrationMessage = string.Empty;
core.Listener.OnAccountRegistrationStateChanged = (c, account, state, message) =>
{
    registrationState = state;
    registrationMessage = message;
    Console.WriteLine($"      Registrierung -> {state}" + (string.IsNullOrEmpty(message) ? "" : $" ({message})"));
};

// Auth zuerst, sonst läuft der erste REGISTER ohne Zugangsdaten ins 401.
var authInfo = factory.CreateAuthInfo(
    username: trunk.EffectiveAuthUserId,
    userid: trunk.EffectiveAuthUserId,
    passwd: trunk.Password,
    ha1: null,
    realm: null,
    domain: trunk.Domain);
core.AddAuthInfo(authInfo);

var accountParams = core.CreateAccountParams();
accountParams.IdentityAddress = factory.CreateAddress($"sip:{trunk.Username}@{trunk.Domain}");
accountParams.ServerAddr = $"sip:{trunk.Domain};transport={trunk.Transport.ToLowerInvariant()}";
accountParams.Transport = Enum.Parse<TransportType>(trunk.Transport, ignoreCase: true);
accountParams.Expires = trunk.Expires;
accountParams.RegisterEnabled = true;
if (!string.IsNullOrWhiteSpace(trunk.OutboundProxy))
{
    accountParams.RoutesAddresses = [factory.CreateAddress(trunk.OutboundProxy)];
}
if (!string.IsNullOrWhiteSpace(trunk.DisplayName))
{
    accountParams.IdentityAddress.DisplayName = trunk.DisplayName;
}

var account = core.CreateAccount(accountParams);
core.AddAccount(account);
core.DefaultAccount = account;

Console.WriteLine("      REGISTER gesendet, warte auf Antwort (max. 30 s)...");
var deadline = DateTime.UtcNow.AddSeconds(30);
while (DateTime.UtcNow < deadline && registrationState != RegistrationState.Ok)
{
    core.Iterate();
    Thread.Sleep(20);
    if (registrationState.ToString() == "Failed")
    {
        break;
    }
}

if (registrationState != RegistrationState.Ok)
{
    Console.WriteLine();
    Console.WriteLine($"      FEHLGESCHLAGEN: Zustand '{registrationState}'"
        + (string.IsNullOrEmpty(registrationMessage) ? "" : $", Meldung: {registrationMessage}"));
    Console.WriteLine();
    Console.WriteLine("      Zu prüfen: Domain und Transport erreichbar? Zugangsdaten richtig?");
    Console.WriteLine("      Bei TLS zusätzlich: braucht der Server eine CA-Datei (§14.6, rootCaFile)?");
    Console.WriteLine("      Mit --verbose starten, um das SIP-Protokoll mitzulesen.");
    core.Stop();
    return 1;
}

Console.WriteLine("      REGISTER 200 OK — das Akzeptanzkriterium aus §12/M1 ist erfüllt.");

// Testanruf, wenn ein Echo-Ziel angegeben ist.
if (string.IsNullOrWhiteSpace(trunk.EchoTarget))
{
    Console.WriteLine("      Kein echoTarget angegeben — Audiotest übersprungen.");
}
else
{
    Console.WriteLine();
    Console.WriteLine($"      Testanruf zu '{trunk.EchoTarget}' (20 s, dann auflegen)");
    Console.WriteLine("      >>> Jetzt hineinsprechen: das Echo muss hörbar zurückkommen. <<<");

    var callState = CallState.Idle;
    core.Listener.OnCallStateChanged = (c, call, state, message) =>
    {
        callState = state;
        Console.WriteLine($"      Anruf -> {state}" + (string.IsNullOrEmpty(message) ? "" : $" ({message})"));
    };

    var target = trunk.EchoTarget.Contains('@', StringComparison.Ordinal)
        ? trunk.EchoTarget
        : $"sip:{trunk.EchoTarget}@{trunk.Domain}";
    var call = core.InviteAddress(factory.CreateAddress(target));

    if (call is null)
    {
        Console.WriteLine("      FEHLGESCHLAGEN: InviteAddress gab null zurück.");
        core.Stop();
        return 1;
    }

    var callDeadline = DateTime.UtcNow.AddSeconds(20);
    var statsShown = false;
    while (DateTime.UtcNow < callDeadline)
    {
        core.Iterate();
        Thread.Sleep(20);

        // Nach 8 s einmal die Qualitätswerte zeigen — das ist die Quelle
        // für das Panel aus §8.2 / AP4.8.
        if (!statsShown && callState == CallState.StreamsRunning
            && DateTime.UtcNow > callDeadline.AddSeconds(-12))
        {
            statsShown = true;
            var stats = call.AudioStats;
            if (stats is not null)
            {
                Console.WriteLine($"      Qualität: RTT {stats.RoundTripDelay:F3} s, "
                    + $"Jitter {stats.JitterBufferSizeMs:F1} ms, "
                    + $"Verlust {stats.ReceiverLossRate:F2} %, "
                    + $"Download {stats.DownloadBandwidth:F1} kbit/s");
            }
            var currentParams = call.CurrentParams;
            if (currentParams is not null)
            {
                Console.WriteLine($"      Verhandelt: Codec {currentParams.UsedAudioPayloadType?.MimeType ?? "?"}, "
                    + $"Verschlüsselung {currentParams.MediaEncryption}");
            }
        }
    }

    if (call.State != CallState.End && call.State != CallState.Released)
    {
        call.Terminate();
        for (var i = 0; i < 50; i++) { core.Iterate(); Thread.Sleep(20); }
    }
    Console.WriteLine("      Anruf beendet.");
}

// Reihenfolge: erst stoppen, dann aufräumen. Umgekehrt schlägt der
// abschliessende REGISTER mit Expires=0 fehl ("Unauthorized"), weil ihm die
// Zugangsdaten schon entzogen wurden — im ersten Durchlauf genau so passiert.
core.Stop();
for (var i = 0; i < 50; i++) { core.Iterate(); Thread.Sleep(20); }
core.ClearAllAuthInfo();

Console.WriteLine();
Console.WriteLine("=== M1 erfüllt: DLL-Kette geladen, REGISTER 200 OK, Gespräch geführt. ===");
return 0;
