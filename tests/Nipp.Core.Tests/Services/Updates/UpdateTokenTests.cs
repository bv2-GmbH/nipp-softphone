using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nipp.Core.Services.Settings;
using Nipp.Core.Services.Updates;
using Velopack.Sources;

namespace Nipp.Core.Tests.Services.Updates;

/// <summary>
/// ADR-083: das Update-Token ist abgeschafft — und es wird nie mehr gesendet.
///
/// <para><b>Warum das ein Test sein muss.</b> Schickt nipp nach dem Widerruf
/// des Tokens auf GitHub noch das alte mit, antwortet GitHub auch für
/// öffentliche Ressourcen mit 401 «Bad credentials», und die Update-Prüfung
/// bräche auf jedem Arbeitsplatz still, der noch eines gespeichert hat. Ein
/// stiller Ausfall, den erst der nächste Release-Tag bemerkt.</para>
///
/// <para>Kein Zugriff auf das Benutzerprofil: der <see cref="SecretStore"/>
/// liegt unter dem Temp-Verzeichnis.</para>
/// </summary>
public sealed class UpdateTokenTests : IDisposable
{
    private const string AltesToken = "github_pat_nicht_mehr_gueltig";

    private readonly string _directory;
    private readonly SecretStore _secrets;

    public UpdateTokenTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nipp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        _secrets = new SecretStore(
            NullLogger<SecretStore>.Instance,
            Path.Combine(_directory, "secrets.dat"));
    }

    [Theory]
    [InlineData(UpdateChannel.Stable)]
    [InlineData(UpdateChannel.Beta)]
    public void Die_Quelle_traegt_nie_ein_Token_auch_wenn_eines_im_SecretStore_liegt(
        UpdateChannel channel)
    {
        _secrets.Set(ObsoleteUpdateToken.Key, AltesToken);

        var source = VelopackUpdateGateway.CreateSource(channel);

        // AccessToken ist bei Velopack geschuetzt (protected): per Reflexion
        // lesen, denn genau dieser Wert geht als Authorization-Kopfzeile raus.
        var eigenschaft = typeof(GithubSource).GetProperty(
            "AccessToken",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(eigenschaft);
        Assert.True(string.IsNullOrEmpty(eigenschaft!.GetValue(source) as string));
        Assert.Equal(new Uri(VelopackUpdateGateway.RepositoryUrl), source.RepoUri);
        Assert.Equal(channel == UpdateChannel.Beta, source.Prerelease);
    }

    [Fact]
    public void Das_Gateway_kennt_den_SecretStore_nicht_mehr()
    {
        // Die Konstruktoren sagen es: ohne SecretStore kann das Gateway kein
        // Token lesen, auch nicht aus Versehen.
        var konstruktoren = typeof(VelopackUpdateGateway).GetConstructors();

        Assert.All(
            konstruktoren,
            static k => Assert.DoesNotContain(
                k.GetParameters(), static p => p.ParameterType == typeof(SecretStore)));
    }

    [Fact]
    public void Aufraeumen_loescht_den_Eintrag_genau_einmal_und_meldet_es()
    {
        _secrets.Set(ObsoleteUpdateToken.Key, AltesToken);
        _secrets.Set("sip:151@pbx.example.ch", "bleibt");
        var log = new ListLogger();

        var erstes = ObsoleteUpdateToken.Remove(_secrets, log);
        var zweites = ObsoleteUpdateToken.Remove(_secrets, log);

        Assert.True(erstes);
        Assert.False(zweites);
        Assert.Null(_secrets.Get(ObsoleteUpdateToken.Key));
        Assert.Equal("bleibt", _secrets.Get("sip:151@pbx.example.ch"));

        // Eine Zeile, Information, ohne den Wert — und beim zweiten Mal still.
        var zeile = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Information, zeile.Level);
        Assert.DoesNotContain(AltesToken, zeile.Message);
    }

    [Fact]
    public void Aufraeumen_ohne_Eintrag_bleibt_still_und_legt_nichts_an()
    {
        var log = new ListLogger();

        Assert.False(ObsoleteUpdateToken.Remove(_secrets, log));

        Assert.Empty(log.Entries);
        Assert.False(File.Exists(_secrets.StorePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record Entry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new Entry(logLevel, formatter(state, exception)));
    }
}
