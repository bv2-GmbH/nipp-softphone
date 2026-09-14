using System.Reflection;
using Nipp.Core.Services.Windows;

namespace Nipp.Core.Tests.Services.Windows;

/// <summary>
/// Die Angaben im Info-Bereich der Einstellungen (§9, §16.2).
///
/// <para>Wenig zu prüfen, aber das Wenige zählt: die Version wird bei einem
/// Support-Fall vorgelesen, und bis zum 07.09.2026 gab es sie zur Laufzeit
/// überhaupt nicht.</para>
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void Die_Version_kommt_aus_dem_Attribut_der_Assembly()
    {
        var version = AppInfo.VersionOf(typeof(AppInfo).Assembly);

        Assert.NotEqual("unbekannt", version);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    /// <summary>
    /// Das SDK hängt an <c>AssemblyInformationalVersion</c> gern den
    /// Quellstand an („0.1.0+3f2a1c"). Im Info-Bereich soll eine
    /// Versionsnummer stehen, keine Commit-Kennung.
    /// </summary>
    [Fact]
    public void Ein_Quellstand_hinter_dem_Plus_wird_abgeschnitten()
    {
        var version = AppInfo.VersionOf(new FakeAssembly("1.2.3+3f2a1cabcdef"));

        Assert.Equal("1.2.3", version);
    }

    [Fact]
    public void Ohne_Attribut_gilt_die_dreiteilige_Assemblyversion()
    {
        var version = AppInfo.VersionOf(new FakeAssembly(informational: null));

        Assert.Equal("4.5.6", version);
    }

    [Fact]
    public void Die_Versionszeile_nennt_die_Buildart()
    {
        // Sie hat zweimal Fehlersuche gekostet: packaged bekommt keine Toasts.
        Assert.Contains(AppInfo.IsPackaged ? "packaged" : "unpackaged", AppInfo.VersionLine, StringComparison.Ordinal);
        Assert.StartsWith("nipp ", AppInfo.VersionLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Die_Kontaktangaben_stehen_an_einer_Stelle()
    {
        Assert.Equal("kontakt@bv2.ch", AppInfo.ContactMail);
        Assert.StartsWith("https://", AppInfo.Website, StringComparison.Ordinal);
        Assert.Contains("bv2 GmbH", AppInfo.Copyright, StringComparison.Ordinal);
    }

    /// <summary>
    /// Eine Assembly, die nur die zwei Angaben liefert, um die es geht.
    /// <c>Assembly</c> ist abstrakt und lässt sich so ohne Attrappenrahmen
    /// beantworten.
    /// </summary>
    private sealed class FakeAssembly(string? informational) : Assembly
    {
        public override AssemblyName GetName() => new("Fake") { Version = new Version(4, 5, 6, 7) };

        // Rückgabe als Attribute[] und nicht als object[]: die Runtime castet
        // darauf, und ein object[] endet in einer InvalidCastException.
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) =>
            attributeType == typeof(AssemblyInformationalVersionAttribute) && informational is not null
                ? new Attribute[] { new AssemblyInformationalVersionAttribute(informational) }
                : Array.Empty<Attribute>();
    }
}
