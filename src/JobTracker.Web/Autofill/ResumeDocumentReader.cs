using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace JobTracker.Web.Autofill;

internal static class ResumeDocumentReader
{
    internal const int MaximumBytes = 5 * 1024 * 1024;
    private const int MaximumPages = 10;
    private const int MaximumCharacters = 80_000;
    private const int MaximumXmlBytes = 1024 * 1024;
    private const string WordMain = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string StrictWordMain = "http://purl.oclc.org/ooxml/wordprocessingml/main";

    public static string Read(byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return extension switch
            {
                ".pdf" when bytes.AsSpan().StartsWith("%PDF-"u8) => ReadPdf(bytes, cancellationToken),
                ".docx" when bytes.AsSpan().StartsWith("PK\u0003\u0004"u8) => ReadDocx(bytes, cancellationToken),
                _ => throw new ResumeImportException("Choose a valid PDF or Word (.docx) résumé."),
            };
        }
        catch (Exception error) when (error is not (ResumeImportException or OperationCanceledException or OutOfMemoryException))
        {
            // Document parsers can throw several exception types on malformed input. Their
            // messages can include source content, so keep them out of the response and logs.
            throw new ResumeImportException("This résumé could not be read. Export an unlocked PDF or DOCX and try again.");
        }
    }

    private static string ReadPdf(byte[] bytes, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(bytes, new ParsingOptions
        {
            MaxStackDepth = 64,
            FilterProvider = new ResumePdfFilterProvider(cancellationToken),
        });
        if (document.IsEncrypted)
        {
            throw new ResumeImportException("Export an unlocked copy of the PDF and try again.");
        }
        if (document.NumberOfPages > MaximumPages)
        {
            throw new ResumeImportException("Choose a résumé with 10 PDF pages or fewer.");
        }

        var text = new StringBuilder();
        Dictionary<string, string> contactLinks = [];
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageNumber);
            if (page.Letters.Count > MaximumCharacters)
            {
                throw new ResumeImportException("This résumé contains too much text to import.");
            }
            AppendText(text, ContentOrderTextExtractor.GetText(page));
            if (pageNumber == 1)
            {
                contactLinks = ReadPdfContactLinks(page, cancellationToken);
            }
        }
        if (contactLinks.Count > 0)
        {
            AppendText(text, "Contact details");
            foreach (var (label, target) in contactLinks)
            {
                AppendText(text, $"{label}: {target}");
            }
        }
        return text.ToString();
    }

    private static Dictionary<string, string> ReadPdfContactLinks(Page page, CancellationToken cancellationToken)
    {
        IReadOnlyList<Hyperlink> links;
        try
        {
            links = page.GetHyperlinks();
        }
        catch (PdfDocumentFormatException)
        {
            // A malformed optional annotation should not discard readable page text.
            return [];
        }

        var contact = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var link in links)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (link.Bounds.Bottom < page.Height * .75)
            {
                continue;
            }
            var label = link.Text?.Trim().ToLowerInvariant() switch
            {
                "linkedin" => "LinkedIn",
                "github" => "GitHub",
                "portfolio" or "website" or "personal website" => "Portfolio",
                _ => null,
            };
            if (label is not null && Uri.TryCreate(link.Uri, UriKind.Absolute, out var uri)
                && uri.AbsoluteUri.Length <= 500 && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0)
            {
                contact.TryAdd(label, uri.AbsoluteUri);
            }
        }
        return contact;
    }

    private static string ReadDocx(byte[] bytes, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        if (archive.Entries.Count > 256
            || archive.GetEntry("[Content_Types].xml") is null
            || archive.GetEntry("word/document.xml") is not { } main)
        {
            throw new ResumeImportException("Choose a valid Word (.docx) résumé.");
        }

        var headers = archive.Entries
            .Where(entry => entry.FullName.StartsWith("word/header", StringComparison.Ordinal)
                && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)
                && entry.FullName.Count(character => character == '/') == 1)
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal).Take(3);
        var text = new StringBuilder();
        foreach (var entry in headers.Append(main))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var part = ReadXml(entry, cancellationToken);
            if (part.Root is not { } root || root.Name.NamespaceName is not (WordMain or StrictWordMain))
            {
                throw new ResumeImportException("Choose a valid Word (.docx) résumé.");
            }

            var links = ReadLinks(archive.GetEntry($"word/_rels/{entry.Name}.rels"), cancellationToken);
            XNamespace word = root.Name.Namespace;
            foreach (var paragraph in root.Descendants(word + "p"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pieces = paragraph.Descendants()
                    .Where(node => node.Ancestors(word + "p").FirstOrDefault() == paragraph)
                    .Select(node => node.Name.LocalName switch
                    {
                        "t" when node.Name.Namespace == word => node.Value,
                        "tab" when node.Name.Namespace == word => " ",
                        "br" when node.Name.Namespace == word => "\n",
                        _ => string.Empty,
                    });
                AppendText(text, string.Concat(pieces));
                foreach (var hyperlink in paragraph.Descendants(word + "hyperlink"))
                {
                    var id = hyperlink.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "id")?.Value;
                    if (id is not null && links.TryGetValue(id, out var url))
                    {
                        var label = string.Concat(hyperlink.Descendants(word + "t").Select(node => node.Value));
                        AppendText(text, $"{label}: {url}");
                    }
                }
            }
        }
        return text.ToString();
    }

    private static Dictionary<string, string> ReadLinks(ZipArchiveEntry? entry, CancellationToken cancellationToken)
    {
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry is null)
        {
            return links;
        }

        foreach (var relationship in ReadXml(entry, cancellationToken).Root?.Elements() ?? [])
        {
            var type = (string?)relationship.Attribute("Type");
            var id = (string?)relationship.Attribute("Id");
            var target = (string?)relationship.Attribute("Target");
            if (id is not null && target?.Length <= 500 && type?.EndsWith("/hyperlink", StringComparison.Ordinal) == true
                && Uri.TryCreate(target, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0)
            {
                links.TryAdd(id, target);
            }
        }
        return links;
    }

    private static XDocument ReadXml(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        using var source = entry.Open();
        using var bounded = ReadBounded(source, MaximumXmlBytes, cancellationToken);
        using var reader = XmlReader.Create(bounded, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlBytes,
        });
        return XDocument.Load(reader);
    }

    private static void AppendText(StringBuilder text, string value)
    {
        if (text.Length + value.Length + 1 > MaximumCharacters)
        {
            throw new ResumeImportException("This résumé contains too much text to import.");
        }
        text.Append(value).Append('\n');
    }

    internal static MemoryStream ReadBounded(Stream source, int maxBytes, CancellationToken cancellationToken)
    {
        var target = new MemoryStream();
        try
        {
            Span<byte> buffer = stackalloc byte[8192];
            int count;
            while ((count = source.Read(buffer)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (target.Length + count > maxBytes)
                {
                    throw new ResumeImportException("This résumé expands beyond the size limit. Export a simpler copy and try again.");
                }
                target.Write(buffer[..count]);
            }
            target.Position = 0;
            return target;
        }
        catch
        {
            target.Dispose();
            throw;
        }
    }
}

internal sealed class ResumeImportException(string message) : Exception(message);
