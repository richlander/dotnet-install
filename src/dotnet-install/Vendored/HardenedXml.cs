// Vendored from richlander/dotnet-inspect @ 0599bfe
//   src/DotnetInspector.Core/HardenedXml.cs
// Local modification: dropped the unused LoadXmlDocument (System.Xml.XmlDocument)
// overload to keep the Native AOT surface lean; only the XDocument helpers are used.
// If a shared library is published, replace this vendored copy with a package reference.

using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace DotnetInspector.Core;

/// <summary>
/// Loads XML from untrusted package contents (.nuspec, compiler .xml docs, tool settings) with
/// DTD processing disabled. This blocks entity-expansion ("billion laughs") denial-of-service and
/// external-entity (XXE) attacks: every input we parse ships inside an attacker-controllable package.
/// </summary>
public static class HardenedXml
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Loads an <see cref="XDocument"/> from a file with DTD processing prohibited.</summary>
    public static XDocument LoadXDocument(string path)
    {
        using var reader = XmlReader.Create(path, Settings);
        return XDocument.Load(reader);
    }

    /// <summary>Parses an <see cref="XDocument"/> from a string with DTD processing prohibited.</summary>
    public static XDocument ParseXDocument(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), Settings);
        return XDocument.Load(reader);
    }
}
