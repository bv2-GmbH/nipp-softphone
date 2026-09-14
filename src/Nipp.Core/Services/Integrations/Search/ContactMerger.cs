using Nipp.Core.Services.Contacts;
using Nipp.Core.Services.Integrations.Config;

namespace Nipp.Core.Services.Integrations.Search;

/// <summary>
/// Führt Treffer aus mehreren Quellen zusammen (§21.4).
/// </summary>
public interface IContactMerger
{
    /// <summary>
    /// Fügt neue Treffer in die bestehende Liste ein und führt zusammen, was
    /// nachweislich dieselbe Person ist.
    /// </summary>
    /// <param name="existing">Was schon in der Liste steht.</param>
    /// <param name="incoming">Was eine weitere Quelle geliefert hat.</param>
    /// <param name="settings">Welche Merkmale gelten sollen.</param>
    IReadOnlyList<Contact> Merge(
        IReadOnlyList<Contact> existing,
        IReadOnlyList<Contact> incoming,
        MergeSettings settings);
}

/// <summary>
/// Die vorsichtige Zusammenführung (§21.4).
///
/// <b>Der Fehler, der hier vermieden werden muss, ist nicht die doppelte
/// Zeile.</b> Zwei Zeilen für dieselbe Person sind unschön. Eine Zeile für
/// zwei Personen führt dazu, dass jemand die falsche Nummer anruft — und er
/// merkt es erst, wenn sich jemand anderes meldet. Deshalb wird nur
/// zusammengeführt, was sich nachweisen lässt:
///
/// <list type="bullet">
///   <item>
///     <b>Gleiche Rufnummer.</b> Verglichen mit
///     <see cref="ClipResolver.IsSameNumber"/> — dieselbe Regel, nach der
///     auch ein eingehender Anruf einem Kontakt zugeordnet wird. Sie
///     vergleicht über die Ziffern und von hinten, und zwar erst ab sieben
///     Stellen: eine dreistellige Nebenstelle passte sonst auf jede Nummer,
///     die zufällig so endet.
///   </item>
///   <item>
///     <b>Gleiche E-Mail-Adresse.</b> Eindeutig, wo vorhanden.
///   </item>
///   <item>
///     <b>Name und Firma</b> — <b>ab Werk aus</b>. In einer Firma mit zwei
///     Mitarbeitern gleichen Namens ist das falsch, und bei einer Zentrale,
///     an der viele hängen, erst recht.
///   </item>
/// </list>
///
/// <b>Was beim Zusammenführen gewinnt.</b> Der Kontakt der Quelle mit der
/// kleineren Prioritätszahl bleibt der Anzeigename; die Nummern werden
/// vereinigt, ohne Doppelte. Die Herkünfte werden gesammelt — §21.4 verlangt,
/// dass nachvollziehbar bleibt, woher ein Kontakt stammt, und die Oberfläche
/// zeigt es an der Zeile.
/// </summary>
public sealed class ContactMerger : IContactMerger
{
    public IReadOnlyList<Contact> Merge(
        IReadOnlyList<Contact> existing,
        IReadOnlyList<Contact> incoming,
        MergeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled || existing.Count == 0)
        {
            return [.. existing, .. incoming];
        }

        var result = new List<Contact>(existing);

        // Nummern, die mehreren Personen gehören, taugen nicht als Identität —
        // siehe SharedNumbers.
        var shared = SharedNumbers([.. existing, .. incoming]);

        foreach (var candidate in incoming)
        {
            var index = IndexOfMatch(result, candidate, settings, shared);

            if (index < 0)
            {
                result.Add(candidate);
                continue;
            }

            result[index] = Combine(result[index], candidate);
        }

        return result;
    }

    /// <summary>
    /// Nummern, die <b>innerhalb einer Quelle</b> mehreren Einträgen gehören —
    /// typischerweise die Hauptnummer einer Firma.
    ///
    /// <para><b>Der Fall, der das nötig macht.</b> Zwei Personen derselben Firma
    /// tragen im CRM beide die Zentrale als Geschäftsnummer. Nach der reinen
    /// Regel „eine gemeinsame Nummer heisst dieselbe Person" wurden sie mit
    /// demselben Outlook-Kontakt zu <b>einer</b> Zeile verschmolzen — mit dem
    /// Namen der einen und der Mobilnummer der anderen. Die zweite Person
    /// verschwand aus der Liste, und niemand hätte sie dort gesucht.</para>
    ///
    /// <para><b>Warum je Quelle und nicht über alle.</b> Dass dieselbe Nummer in
    /// zwei <i>verschiedenen</i> Quellen steht, ist der Normalfall und genau
    /// das, wonach das Zusammenführen sucht — auch wenn die Namen dort
    /// verschieden geschrieben sind („Hans Muster" und „H. Muster"). Erst wenn
    /// <b>eine</b> Quelle dieselbe Nummer bei zwei Einträgen führt, sagt sie
    /// damit selbst: das ist keine Nummer, die eine Person bezeichnet.</para>
    ///
    /// <para>Der Vergleich ist quadratisch. Das ist vertretbar: es geht um ein
    /// Suchergebnis, nicht um ein Adressbuch — die Menge ist auf wenige Dutzend
    /// begrenzt (§21.3).</para>
    /// </summary>
    private static List<string> SharedNumbers(IReadOnlyList<Contact> all)
    {
        var shared = new List<string>();

        foreach (var group in all.GroupBy(c => c.EffectiveSourceId, StringComparer.Ordinal))
        {
            // Je Nummer zählen, bei wie vielen Einträgen dieser Quelle sie steht.
            var seen = new List<(string Number, int Count)>();

            foreach (var contact in group)
            {
                // Innerhalb eines Eintrags zählt eine Nummer nur einmal:
                // Geschäft und Zentrale können identisch notiert sein.
                var counted = new List<string>();

                foreach (var number in contact.Numbers)
                {
                    if (string.IsNullOrWhiteSpace(number.Number)
                        || counted.Exists(c => ClipResolver.IsSameNumber(c, number.Number)))
                    {
                        continue;
                    }

                    counted.Add(number.Number);

                    var index = seen.FindIndex(e => ClipResolver.IsSameNumber(e.Number, number.Number));

                    if (index < 0)
                    {
                        seen.Add((number.Number, 1));
                    }
                    else
                    {
                        seen[index] = (seen[index].Number, seen[index].Count + 1);
                    }
                }
            }

            foreach (var (number, count) in seen)
            {
                if (count > 1 && !shared.Exists(x => ClipResolver.IsSameNumber(x, number)))
                {
                    shared.Add(number);
                }
            }
        }

        return shared;
    }

    /// <summary>
    /// Sucht den Eintrag, der nachweislich dieselbe Person bezeichnet.
    ///
    /// <b>Der erste Treffer gewinnt und es wird nicht weitergesucht.</b> Wenn
    /// ein Kandidat auf zwei bestehende Einträge passt, ist mindestens eine
    /// der beiden Zuordnungen falsch — und drei Einträge zu einer Zeile zu
    /// verschmelzen macht den Fehler nur grösser.
    /// </summary>
    private static int IndexOfMatch(
        IReadOnlyList<Contact> existing,
        Contact candidate,
        MergeSettings settings,
        List<string> sharedNumbers)
    {
        for (var i = 0; i < existing.Count; i++)
        {
            var other = existing[i];

            // Dieselbe Quelle führt nie zusammen: wenn ein CRM zwei Einträge
            // liefert, hat es dafür seine Gründe, und nipp weiss sie nicht.
            if (string.Equals(other.EffectiveSourceId, candidate.EffectiveSourceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (settings.ByPhone && SharesNumber(other, candidate, sharedNumbers))
            {
                return i;
            }

            if (settings.ByEmail && SharesEmail(other, candidate))
            {
                return i;
            }

            if (settings.ByNameAndCompany && SharesNameAndCompany(other, candidate))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool SharesNumber(Contact a, Contact b, List<string> sharedNumbers) =>
        a.Numbers.Any(left => b.Numbers.Any(right =>
            ClipResolver.IsSameNumber(left.Number, right.Number)
            && !sharedNumbers.Exists(s => ClipResolver.IsSameNumber(s, left.Number))));

    private static bool SharesEmail(Contact a, Contact b) =>
        a.Email is { Length: > 0 } left
        && b.Email is { Length: > 0 } right
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Name <b>und</b> Firma, beide vorhanden und beide gleich. Der Name
    /// allein genügt nie — „Peter Meier" gibt es mehrfach.
    /// </summary>
    private static bool SharesNameAndCompany(Contact a, Contact b) =>
        a.Company is { Length: > 0 } firstCompany
        && b.Company is { Length: > 0 } secondCompany
        && string.Equals(firstCompany.Trim(), secondCompany.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.DisplayName.Trim(), b.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Vereinigt zwei Kontakte. <paramref name="kept"/> ist der bestehende
    /// Eintrag und behält seinen Platz in der Liste — die Reihenfolge folgt
    /// der Priorität der Quellen, und die stand schon fest, bevor der zweite
    /// Treffer kam.
    /// </summary>
    private static Contact Combine(Contact kept, Contact added)
    {
        var numbers = new List<ContactNumber>(kept.Numbers);

        foreach (var number in added.Numbers)
        {
            // Nur wirklich neue Nummern: dieselbe Nummer in zwei
            // Schreibweisen ist eine Nummer, und zwei Zeilen mit
            // „044 512 84 30" und „+41445128430" wären nur verwirrend.
            if (!numbers.Any(existing => ClipResolver.IsSameNumber(existing.Number, number.Number)))
            {
                numbers.Add(number);
            }
        }

        var origins = new List<ContactOrigin>(kept.AllOrigins);

        foreach (var origin in added.AllOrigins)
        {
            if (!origins.Any(o =>
                string.Equals(o.SourceId, origin.SourceId, StringComparison.Ordinal)
                && string.Equals(o.ExternalId, origin.ExternalId, StringComparison.Ordinal)))
            {
                origins.Add(origin);
            }
        }

        return kept with
        {
            Numbers = numbers,

            // Was der bestehende Eintrag nicht weiss, darf der neue beisteuern
            // — aber nichts überschreiben. Der Anzeigename der Quelle mit der
            // höheren Priorität bleibt.
            Company = kept.Company ?? added.Company,
            Email = kept.Email ?? added.Email,
            SipAddress = kept.SipAddress ?? added.SipAddress,
            OpenUri = kept.OpenUri ?? added.OpenUri,
            Origins = origins,
        };
    }
}
