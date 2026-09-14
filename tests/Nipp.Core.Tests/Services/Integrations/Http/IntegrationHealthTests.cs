using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Time.Testing;
using Nipp.Core.Services.Integrations.Http;

namespace Nipp.Core.Tests.Services.Integrations.Http;

/// <summary>
/// Der Schutzschalter je Quelle (§21.2).
///
/// <b>Beide Fälle hier treten beim Kunden auf, nicht im Test.</b> Ein Server,
/// der weg ist, bleibt es meist eine Weile — ihn bei jedem Anruf erneut zu
/// fragen kostet je Anruf die volle Zeitgrenze, und die Karte wartet sichtbar
/// auf etwas, das absehbar nicht kommt. Ein Server, der 429 sagt, sagt
/// ausdrücklich „frag später"; wer weiterfragt, verlängert die Sperre.
/// </summary>
public sealed class IntegrationHealthTests
{
    private readonly FakeTimeProvider _zeit = new();

    private IntegrationHealth Gesundheit() => new(_zeit);

    [Fact]
    public void Eine_unbekannte_Quelle_darf_gefragt_werden() =>
        Assert.True(Gesundheit().IsAvailable("crm"));

    /// <summary>
    /// Fünf und nicht einer: ein einzelner Fehler ist ein Zucken im Netz, und
    /// eine Quelle danach für eine Minute abzuschalten wäre schlimmer als das
    /// Problem.
    /// </summary>
    [Fact]
    public void Ein_einzelner_Fehler_schaltet_nichts_ab()
    {
        var health = Gesundheit();

        health.ReportFailure("crm");

        Assert.True(health.IsAvailable("crm"));
    }

    [Fact]
    public void Nach_mehreren_Fehlern_wird_die_Quelle_pausiert()
    {
        var health = Gesundheit();

        for (var i = 0; i < IntegrationHealth.FailuresBeforeBreak; i++)
        {
            health.ReportFailure("crm");
        }

        Assert.False(health.IsAvailable("crm"));
        Assert.NotNull(health.RemainingBreak("crm"));
    }

    [Fact]
    public void Nach_der_Pause_wird_wieder_gefragt()
    {
        var health = Gesundheit();

        for (var i = 0; i < IntegrationHealth.FailuresBeforeBreak; i++)
        {
            health.ReportFailure("crm");
        }

        _zeit.Advance(IntegrationHealth.BreakDuration + TimeSpan.FromSeconds(1));

        Assert.True(health.IsAvailable("crm"));
    }

    /// <summary>
    /// Der Schutzschalter ist eine Vorsichtsmassnahme, keine Strafe: eine
    /// Antwort setzt alles zurück.
    /// </summary>
    [Fact]
    public void Ein_Erfolg_setzt_alles_zurueck()
    {
        var health = Gesundheit();

        for (var i = 0; i < IntegrationHealth.FailuresBeforeBreak - 1; i++)
        {
            health.ReportFailure("crm");
        }

        health.ReportSuccess("crm");

        // Nach dem Erfolg beginnt die Zählung von vorn — ein weiterer Fehler
        // darf nicht sofort in die Pause führen.
        health.ReportFailure("crm");

        Assert.True(health.IsAvailable("crm"));
    }

    [Fact]
    public void Quellen_werden_getrennt_gezaehlt()
    {
        var health = Gesundheit();

        for (var i = 0; i < IntegrationHealth.FailuresBeforeBreak; i++)
        {
            health.ReportFailure("crm");
        }

        Assert.False(health.IsAvailable("crm"));
        Assert.True(health.IsAvailable("erp"));
    }

    // --- Ratenbegrenzung ---

    /// <summary>
    /// Ein <c>429</c> pausiert <b>sofort</b>, nicht erst nach fünf. Der Server
    /// hat ausdrücklich darum gebeten.
    /// </summary>
    [Fact]
    public void Ein_429_pausiert_sofort()
    {
        var health = Gesundheit();

        health.ReportRateLimited("crm", TimeSpan.FromSeconds(30));

        Assert.False(health.IsAvailable("crm"));

        _zeit.Advance(TimeSpan.FromSeconds(31));

        Assert.True(health.IsAvailable("crm"));
    }

    [Fact]
    public void Ohne_Angabe_gilt_die_uebliche_Dauer()
    {
        var health = Gesundheit();

        health.ReportRateLimited("crm", retryAfter: null);

        _zeit.Advance(IntegrationHealth.BreakDuration - TimeSpan.FromSeconds(1));
        Assert.False(health.IsAvailable("crm"));

        _zeit.Advance(TimeSpan.FromSeconds(2));
        Assert.True(health.IsAvailable("crm"));
    }

    /// <summary>
    /// Eine Stunde Pause ist möglich und für ein Softphone unbrauchbar — nach
    /// einer Stunde ist die Sitzung eine andere.
    /// </summary>
    [Fact]
    public void Eine_sehr_lange_Pause_wird_gedeckelt()
    {
        var health = Gesundheit();

        health.ReportRateLimited("crm", TimeSpan.FromHours(1));

        _zeit.Advance(IntegrationHealth.MaxRetryAfter + TimeSpan.FromSeconds(1));

        Assert.True(health.IsAvailable("crm"));
    }

    [Fact]
    public void RetryAfter_wird_als_Sekundenzahl_gelesen()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), IntegrationHealth.RetryAfterOf(response));
    }

    [Fact]
    public void Ohne_429_gibt_es_kein_RetryAfter()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        Assert.Null(IntegrationHealth.RetryAfterOf(response));
    }

    [Fact]
    public void Die_verbleibende_Pause_laesst_sich_abfragen()
    {
        var health = Gesundheit();

        health.ReportRateLimited("crm", TimeSpan.FromSeconds(30));

        var rest = health.RemainingBreak("crm");

        Assert.NotNull(rest);
        Assert.InRange(rest.Value.TotalSeconds, 29, 30);
    }
}
