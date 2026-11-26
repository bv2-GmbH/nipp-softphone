using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Wie sich nipp bei Windows für die Protokolle anmeldet (ADR-082).
///
/// <para>Gemessen am 26.11.2026: mit den nackten Schlüsseln allein erschien
/// nipp weder in «Standard-Apps» noch in «App auswählen», und Teams gewann.
/// Diese Tests halten fest, <b>was</b> eingetragen wird — ob Windows es dann
/// anzeigt, ist am Gerät gemessen, nicht hier.</para>
/// </summary>
public sealed class ProtocolRegistryTests
{
    private const string Stub = @"C:\Users\x\AppData\Local\nipp\Nipp.App.exe";

    private sealed class FakeRegistry : IRegistryStore
    {
        public Dictionary<(string Key, string Name), string> Values { get; } = new();

        private static string Norm(string k) => k.ToLowerInvariant();

        public void SetValue(string keyPath, string? name, string value)
            => Values[(Norm(keyPath), name ?? string.Empty)] = value;

        public string? GetValue(string keyPath, string? name)
            => Values.TryGetValue((Norm(keyPath), name ?? string.Empty), out var v) ? v : null;

        public void DeleteValue(string keyPath, string name)
            => Values.Remove((Norm(keyPath), name));

        public void DeleteKeyTree(string keyPath)
        {
            var k = Norm(keyPath);
            foreach (var key in Values.Keys
                .Where(x => x.Key == k || x.Key.StartsWith(k + @"\", StringComparison.Ordinal)).ToList())
            {
                Values.Remove(key);
            }
        }
    }

    private static (FakeRegistry Fake, ProtocolRegistry Registry) Make()
    {
        var fake = new FakeRegistry();
        return (fake, new ProtocolRegistry(fake, Stub));
    }

    [Fact]
    public void Die_nackten_Schluessel_bleiben_als_Rueckfallweg()
    {
        var (fake, registry) = Make();
        registry.Register();

        foreach (var protocol in new[] { "tel", "sip", "sips", "callto" })
        {
            var key = $@"Software\Classes\{protocol}";
            Assert.Equal($"URL:{protocol}", fake.GetValue(key, null));
            Assert.Equal(string.Empty, fake.GetValue(key, "URL Protocol"));
            Assert.Equal($"\"{Stub}\" \"%1\"", fake.GetValue($@"{key}\shell\open\command", null));
        }
    }

    [Fact]
    public void Die_ProgID_fuehrt_denselben_Befehl_und_den_Stub()
    {
        var (fake, registry) = Make();
        registry.Register();

        Assert.Equal(string.Empty, fake.GetValue(@"Software\Classes\nipp.Url", "URL Protocol"));
        Assert.Equal("nipp", fake.GetValue(@"Software\Classes\nipp.Url\Application", "ApplicationName"));
        Assert.Equal($"\"{Stub}\" \"%1\"", fake.GetValue(@"Software\Classes\nipp.Url\shell\open\command", null));
        Assert.Equal($"\"{Stub}\",0", fake.GetValue(@"Software\Classes\nipp.Url\DefaultIcon", null));
    }

    [Fact]
    public void Die_Faehigkeiten_nennen_alle_vier_Protokolle_und_zeigen_auf_die_ProgID()
    {
        var (fake, registry) = Make();
        registry.Register();

        Assert.Equal("nipp", fake.GetValue(@"Software\bv2\nipp\Capabilities", "ApplicationName"));
        Assert.False(string.IsNullOrWhiteSpace(
            fake.GetValue(@"Software\bv2\nipp\Capabilities", "ApplicationDescription")));

        foreach (var protocol in new[] { "tel", "sip", "sips", "callto" })
        {
            Assert.Equal("nipp.Url", fake.GetValue(@"Software\bv2\nipp\Capabilities\URLAssociations", protocol));
        }
    }

    [Fact]
    public void RegisteredApplications_zeigt_auf_die_Faehigkeiten()
    {
        var (fake, registry) = Make();
        registry.Register();

        Assert.Equal(
            @"Software\bv2\nipp\Capabilities",
            fake.GetValue(@"Software\RegisteredApplications", "nipp"));
    }

    [Fact]
    public void UserChoice_wird_nie_geschrieben()
    {
        // Windows schuetzt den Schluessel per Hash, und die Wahl gehoert dem
        // Benutzer oder der IT (ADR-082).
        var (fake, registry) = Make();
        registry.Register();
        registry.Remove();
        registry.Register();

        Assert.DoesNotContain(fake.Values.Keys, k => k.Key.Contains("userchoice", StringComparison.Ordinal));
    }

    [Fact]
    public void Doppeltes_Registrieren_aendert_nichts()
    {
        var (fake, registry) = Make();
        registry.Register();
        var first = fake.Values.OrderBy(x => x.Key).ToList();

        registry.Register();

        Assert.Equal(first, fake.Values.OrderBy(x => x.Key).ToList());
    }

    [Fact]
    public void Entfernen_raeumt_alles_von_nipp_weg()
    {
        var (fake, registry) = Make();
        registry.Register();

        registry.Remove();

        Assert.Empty(fake.Values);
    }

    [Fact]
    public void Entfernen_laesst_Fremdes_stehen()
    {
        var (fake, registry) = Make();

        // Ein anderes Programm hat tel: und einen eigenen Eintrag in der Liste.
        fake.SetValue(@"Software\Classes\tel\shell\open\command", null, @"""C:\Fremd\app.exe"" ""%1""");
        fake.SetValue(@"Software\RegisteredApplications", "Fremd", @"Software\Fremd\Capabilities");
        registry.Register();
        // Registrieren hat tel: uebernommen; der Fremde Befehl wird fuer die
        // Gegenprobe zurueckgesetzt, als haette nipp ihn nie angelegt.
        fake.SetValue(@"Software\Classes\tel\shell\open\command", null, @"""C:\Fremd\app.exe"" ""%1""");

        registry.Remove();

        Assert.Equal(@"""C:\Fremd\app.exe"" ""%1""", fake.GetValue(@"Software\Classes\tel\shell\open\command", null));
        Assert.Equal(@"Software\Fremd\Capabilities", fake.GetValue(@"Software\RegisteredApplications", "Fremd"));
        Assert.Null(fake.GetValue(@"Software\RegisteredApplications", "nipp"));
    }

    [Fact]
    public void Ein_fremder_Eintrag_unter_dem_Namen_nipp_bleibt_stehen()
    {
        var (fake, registry) = Make();
        fake.SetValue(@"Software\RegisteredApplications", "nipp", @"Software\Anderes\Capabilities");

        registry.Remove();

        Assert.Equal(@"Software\Anderes\Capabilities", fake.GetValue(@"Software\RegisteredApplications", "nipp"));
    }

    [Fact]
    public void Der_Pfad_ist_der_uebergebene_und_nie_current()
    {
        // Der Stub-Pfad kommt aus WindowsIntegration.ExecutablePath
        // (ExecutablePathTests); hier genuegt: es wird genau der uebergebene
        // geschrieben, an allen vier Stellen.
        var (fake, registry) = Make();
        registry.Register();

        Assert.All(
            fake.Values.Where(v => v.Key.Key.EndsWith(@"shell\open\command", StringComparison.Ordinal)),
            v => Assert.StartsWith($"\"{Stub}\"", v.Value, StringComparison.Ordinal));
        Assert.Equal(5, fake.Values.Count(v => v.Key.Key.EndsWith(@"shell\open\command", StringComparison.Ordinal)));
        Assert.DoesNotContain(fake.Values.Values, v => v.Contains(@"\current\", StringComparison.OrdinalIgnoreCase));
    }
}