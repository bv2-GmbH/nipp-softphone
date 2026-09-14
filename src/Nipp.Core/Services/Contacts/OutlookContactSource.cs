using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Nipp.Core.Services.Contacts;

/// <summary>
/// Persönliche Kontakte aus dem lokal laufenden Outlook (AP6.3, ADR-009).
///
/// <b>Abweichung von AP6.3:</b> dort steht „Graph zuerst, COM als
/// Rückfallebene". Entschieden wurde das Gegenteil (ADR-009): Graph verlangt
/// eine App-Registrierung in Entra, Consent durch die Administration und einen
/// Anmeldefluss — für eine reine Namensauflösung am Arbeitsplatz ist das
/// unverhältnismässig, und nipp braucht keine Kontakte aus der Cloud, sondern
/// die, die der Benutzer vor sich hat. Kein Graph heisst auch: kein Token, das
/// irgendwo liegt.
///
/// Zwei Dinge, die dabei zählen:
/// <list type="bullet">
///   <item>
///     <b>Späte Bindung.</b> Keine Interop-Assembly, keine Abhängigkeit von
///     einer Outlook-Version. Ohne Outlook liefert die Quelle schlicht nichts.
///   </item>
///   <item>
///     <b>Eigener STA-Thread.</b> Outlook ist ein STA-COM-Server. Aus dem
///     Threadpool (MTA) gerufen funktioniert es oft, aber nicht verlässlich —
///     und ein hängender COM-Aufruf hängt dann einen Poolthread auf.
///   </item>
/// </list>
/// </summary>
public sealed class OutlookContactSource : IContactSource
{
    /// <summary>
    /// Outlooks eigener Bezeichner für den Kontakteordner
    /// (<c>olFolderContacts</c>). Als Zahl, weil bei später Bindung keine
    /// Enumeration zur Verfügung steht.
    /// </summary>
    private const int FolderContacts = 10;

    /// <summary>Outlooks <c>olContact</c>. Verteilerlisten und Termine haben andere Werte.</summary>
    private const int ItemClassContact = 40;

    /// <summary>
    /// Obergrenze. Ein Adressbuch mit mehreren zehntausend Einträgen würde den
    /// Start spürbar verzögern, ohne dass jemand so weit scrollt — gesucht wird
    /// über das Suchfeld, und eingehende Nummern löst <see cref="ClipResolver"/>
    /// auf.
    /// </summary>
    private const int MaxContacts = 5000;

    /// <summary>
    /// Wie lange auf Outlook gewartet wird, bevor nipp ohne Kontakte
    /// weitermacht.
    ///
    /// Waren 30 Sekunden, sind jetzt 60: bei einem <b>kalten</b> Outlook —
    /// COM startet es dann selbst — dauerte ein Durchlauf über 151 Einträge
    /// gemessene 32 Sekunden. Die Grenze griff also genau dann, wenn sie am
    /// wenigsten sollte.
    ///
    /// Sie ist trotzdem nötig: ein Outlook mit einem offenen modalen Dialog
    /// antwortet nie.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ILogger<OutlookContactSource> _logger;

    /// <summary>
    /// Das Ergebnis eines Durchlaufs, der erst nach der Zeitgrenze fertig
    /// wurde. Der nächste <see cref="LoadAsync"/> nimmt es ohne Wartezeit.
    /// </summary>
    private IReadOnlyList<Contact>? _lateResult;

    public OutlookContactSource(ILogger<OutlookContactSource> logger) => _logger = logger;

    /// <summary>
    /// Ein abgebrochener Durchlauf ist doch noch fertig geworden.
    ///
    /// Wer das abonniert, sollte neu laden — die Daten liegen dann bereit und
    /// kosten nichts mehr. Ohne dieses Ereignis bliebe die Kontaktliste leer,
    /// bis jemand von Hand aktualisiert, obwohl alles längst da ist.
    /// </summary>
    public event EventHandler? LateResultAvailable;

    public ContactSourceKind Kind => ContactSourceKind.Outlook;

    /// <summary>
    /// Ob Outlook auf dieser Maschine installiert ist. Geprüft wird die
    /// Registrierung der Klasse, nicht ob Outlook läuft — COM startet es bei
    /// Bedarf selbst.
    /// </summary>
    public bool IsAvailable => Type.GetTypeFromProgID("Outlook.Application") is not null;

    /// <summary>
    /// Ob Outlook gerade läuft. §8.4 verlangt eine verständliche Anzeige,
    /// wenn nicht — nicht das Starten von Outlook im Hintergrund.
    /// </summary>
    public static bool IsRunning => AttachToRunningOutlook() is not null;

    /// <summary>
    /// Warum keine Kontakte kamen, als fertiger Satz für die Oberfläche — oder
    /// <c>null</c>, wenn alles in Ordnung ist.
    ///
    /// <para><b>§8.4 verlangt das ausdrücklich:</b> „Outlook muss laufen (sonst
    /// verständliche Anzeige statt leerer Liste)". Diese Anzeige gab es nur im
    /// Protokoll. In der Oberfläche stand «Outlook (0)», und das sieht aus, als
    /// sei nipp defekt — genau der Eindruck, den §8.4 verhindern wollte.</para>
    ///
    /// <para>Fertig formuliert und nicht als Aufzählungswert: die Abhilfe
    /// unterscheidet sich je Fall, und nur diese Klasse weiss, welcher vorliegt
    /// (§15). Dasselbe Vorgehen wie beim Gerätewechsel in
    /// <c>AudioDevicesChangedEventArgs</c>.</para>
    /// </summary>
    public string? StatusHint { get; private set; }

    /// <summary>
    /// Ob nur das <b>neue</b> Outlook läuft.
    ///
    /// <para>Das neue Outlook (<c>olk.exe</c>, «Microsoft.OutlookForWindows»)
    /// ist eine WinUI-Anwendung um den Web-Client herum und bietet <b>keine
    /// COM-Automatisierung</b> an. Es gibt kein <c>Outlook.Application</c>, das
    /// man ansprechen könnte. Der Weg aus §8.4 ist dort nicht kaputt, sondern
    /// nicht vorhanden — und wer die Meldung „Outlook ist nicht geöffnet" liest,
    /// während Outlook offen vor ihm steht, sucht an der falschen Stelle.</para>
    /// </summary>
    private static bool OnlyNewOutlookRunning()
    {
        try
        {
            var classic = System.Diagnostics.Process.GetProcessesByName("OUTLOOK");

            foreach (var process in classic)
            {
                process.Dispose();
            }

            if (classic.Length > 0)
            {
                return false;
            }

            var modern = System.Diagnostics.Process.GetProcessesByName("olk");

            foreach (var process in modern)
            {
                process.Dispose();
            }

            return modern.Length > 0;
        }
        catch (Exception)
        {
            // Die Prozessliste zu lesen kann in eingeschränkten Umgebungen
            // scheitern. Dann bleibt es beim allgemeinen Hinweis.
            return false;
        }
    }

    public Task<IReadOnlyList<Contact>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            ContactLog.OutlookMissing(_logger);

            StatusHint = OnlyNewOutlookRunning()
                ? "Das neue Outlook stellt keine Kontakte für andere Programme bereit. "
                    + "Siehe Einstellungen, Abschnitt Kontakte."
                : "Auf diesem Rechner ist kein Outlook eingerichtet, das Kontakte "
                    + "bereitstellen kann.";

            return Task.FromResult<IReadOnlyList<Contact>>([]);
        }

        // Ein Durchlauf, der nach der letzten Zeitgrenze doch noch fertig
        // wurde, liegt hier bereit — dann kostet dieser Aufruf nichts.
        if (Interlocked.Exchange(ref _lateResult, null) is { } ready)
        {
            ContactLog.OutlookLateResultUsed(_logger, ready.Count);
            return Task.FromResult(ready);
        }

        var completion = new TaskCompletionSource<IReadOnlyList<Contact>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                completion.TrySetResult(ReadContacts(cancellationToken));
            }
            catch (Exception ex)
            {
                // Bewusst alles: COM meldet Fehler als COMException, aber auch
                // als InvalidCastException, RuntimeBinderException oder schlicht
                // NullReferenceException, wenn ein Ordner fehlt. Keiner davon
                // darf nipp anhalten (§8.4).
                ContactLog.OutlookFailed(_logger, ex.Message);
                completion.TrySetResult([]);
            }
        })
        {
            IsBackground = true,
            Name = "nipp-outlook",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return WaitWithTimeoutAsync(completion.Task, cancellationToken);
    }

    private async Task<IReadOnlyList<Contact>> WaitWithTimeoutAsync(
        Task<IReadOnlyList<Contact>> work,
        CancellationToken cancellationToken)
    {
        var finished = await Task.WhenAny(work, Task.Delay(Timeout, cancellationToken))
            .ConfigureAwait(false);

        if (finished != work)
        {
            ContactLog.OutlookTimedOut(_logger, (int)Timeout.TotalSeconds);

            // Der STA-Thread läuft weiter. Sein Ergebnis wird aufgehoben und
            // gemeldet, statt es wegzuwerfen — genau das ist am 05.09.2026
            // passiert: Outlook war zwei Sekunden nach der Grenze fertig, und
            // die Kontaktliste blieb trotzdem leer.
            _ = work.ContinueWith(
                completed =>
                {
                    if (completed.Result.Count == 0)
                    {
                        return;
                    }

                    _lateResult = completed.Result;
                    ContactLog.OutlookLateResult(_logger, completed.Result.Count);
                    LateResultAvailable?.Invoke(this, EventArgs.Empty);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return [];
        }

        return await work.ConfigureAwait(false);
    }

    /// <summary>
    /// Der eigentliche COM-Durchlauf. Läuft ausschliesslich auf dem STA-Thread
    /// aus <see cref="LoadAsync"/>.
    /// </summary>
    private List<Contact> ReadContacts(CancellationToken cancellationToken)
    {
        var type = Type.GetTypeFromProgID("Outlook.Application");

        if (type is null)
        {
            return [];
        }

        object? application = null;
        object? session = null;
        object? folder = null;
        object? items = null;

        try
        {
            application = AttachToRunningOutlook();

            if (application is null)
            {
                // §8.4 ausdruecklich: „Outlook muss laufen (sonst
                // verstaendliche Anzeige statt leerer Liste)". Vorher stand
                // hier ein Activator.CreateInstance — das STARTET Outlook als
                // unsichtbaren Prozess, kann eine Profilabfrage aufwerfen und
                // brauchte im Kaltstart gemessene 32 Sekunden.
                ContactLog.OutlookNotRunning(_logger);

                StatusHint = OnlyNewOutlookRunning()
                    ? "Das neue Outlook stellt keine Kontakte für andere Programme bereit. "
                        + "Siehe Einstellungen, Abschnitt Kontakte."
                    : "Outlook ist nicht geöffnet. nipp startet es nicht selbst — Outlook "
                        + "öffnen, dann hier auf «Kontakte neu einlesen» klicken.";

                return [];
            }

            dynamic app = application;
            session = app.Session;

            dynamic mapi = session;
            folder = mapi.GetDefaultFolder(FolderContacts);

            dynamic contactsFolder = folder;
            items = contactsFolder.Items;

            var result = new List<Contact>();

            dynamic collection = items;
            int count = collection.Count;

            // Der Indexer ist einsbasiert — Outlook, nicht .NET.
            for (var i = 1; i <= count && result.Count < MaxContacts; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                object? item = null;

                try
                {
                    item = collection[i];

                    if (ToContact(item) is { } contact)
                    {
                        result.Add(contact);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Ein einzelner unlesbarer Eintrag (Verteilerliste,
                    // beschädigter Datensatz) kostet nicht die ganze Liste.
                    ContactLog.OutlookItemSkipped(_logger, i, ex.Message);
                }
                finally
                {
                    Release(item);
                }
            }

            ContactLog.OutlookLoaded(_logger, result.Count, count);

            // Gelungen: der Hinweis von vorhin gilt nicht mehr.
            StatusHint = null;

            return result;
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(session);
            Release(application);
        }
    }

    /// <summary>
    /// Verbindet sich mit einem <b>laufenden</b> Outlook.
    ///
    /// <c>GetActiveObject</c> gibt es in .NET (Core) nicht mehr, deshalb der
    /// Weg über die COM-Laufzeit direkt. Ein nicht laufendes Outlook liefert
    /// <c>null</c> — und genau das ist gewollt.
    /// </summary>
    private static object? AttachToRunningOutlook()
    {
        try
        {
            // <b>Zwei Schritte, und der erste ist nicht optional.</b>
            // GetActiveObject erwartet als ersten Parameter einen Zeiger auf
            // eine CLSID, keine Zeichenfolge. Wird stattdessen die ProgID
            // uebergeben, deutet die Funktion die ersten sechzehn Bytes des
            // Textes als CLSID — das trifft nie zu, der Aufruf scheitert
            // immer, und das Ergebnis sieht genauso aus wie „Outlook laeuft
            // nicht". Genau so war es hier zuerst gebaut.
            if (CLSIDFromProgID("Outlook.Application", out var clsid) != 0)
            {
                return null;
            }

            return GetActiveObject(ref clsid, IntPtr.Zero, out var instance) == 0
                ? instance
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    [DllImport("ole32.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
    private static extern int CLSIDFromProgID(
        [MarshalAs(UnmanagedType.LPWStr)] string progId,
        out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid clsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    /// <summary>
    /// Macht aus einem Outlook-Element einen <see cref="Contact"/> — oder
    /// nichts, wenn es keine wählbare Nummer hat. Ein Kontakt ohne Nummer ist
    /// für ein Telefon wertlos.
    /// </summary>
    private static Contact? ToContact(object? item)
    {
        if (item is null)
        {
            return null;
        }

        dynamic entry = item;
        int itemClass = entry.Class;

        if (itemClass != ItemClassContact)
        {
            return null;
        }

        var numbers = new List<ContactNumber>();
        AddNumber(numbers, TryRead(() => (string?)entry.BusinessTelephoneNumber), ContactNumberKind.Business);
        AddNumber(numbers, TryRead(() => (string?)entry.MobileTelephoneNumber), ContactNumberKind.Mobile);
        AddNumber(numbers, TryRead(() => (string?)entry.HomeTelephoneNumber), ContactNumberKind.Home);
        AddNumber(numbers, TryRead(() => (string?)entry.Business2TelephoneNumber), ContactNumberKind.Other);

        if (numbers.Count == 0)
        {
            return null;
        }

        var company = TryRead(() => (string?)entry.CompanyName);
        var name = TryRead(() => (string?)entry.FullName) ?? company ?? numbers[0].Number;

        var id = TryRead(() => (string?)entry.EntryID)
            ?? name.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture);

        return new Contact(
            Id: id,
            DisplayName: name,
            Numbers: numbers,
            Source: ContactSourceKind.Outlook,
            SipAddress: null,
            Company: company);
    }

    /// <summary>
    /// Liest eine Eigenschaft, die es je nach Outlook-Version und Datensatz
    /// geben kann oder nicht. Fehlt sie, ist das kein Fehler.
    /// </summary>
    private static string? TryRead(Func<string?> read)
    {
        try
        {
            var value = read();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void AddNumber(List<ContactNumber> numbers, string? value, ContactNumberKind kind)
    {
        if (value is { Length: > 0 })
        {
            numbers.Add(new ContactNumber(value, kind));
        }
    }

    /// <summary>
    /// Gibt einen COM-Verweis frei. Ohne das bleibt Outlook nach dem Beenden
    /// von nipp als unsichtbarer Prozess stehen — ein klassischer und für den
    /// Benutzer sehr ärgerlicher Fehler.
    /// </summary>
    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.FinalReleaseComObject(comObject);
        }
    }
}
