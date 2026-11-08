using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace JobTracker.Web.Autofill;

internal sealed class ResumePdfFilterProvider(CancellationToken cancellationToken) : IFilterProvider
{
    private const int MaximumDecodedBytes = 20 * 1024 * 1024;
    private const int MaximumStreamBytes = 4 * 1024 * 1024;
    private readonly CancellationToken cancellation = cancellationToken;
    private int remainingBytes = MaximumDecodedBytes;

    public IReadOnlyList<IFilter> GetFilters(DictionaryToken dictionary) =>
        Bound(DefaultFilterProvider.Instance.GetFilters(dictionary));

    public IReadOnlyList<IFilter> GetNamedFilters(IReadOnlyList<NameToken> names) =>
        Bound(DefaultFilterProvider.Instance.GetNamedFilters(names));

    public IReadOnlyList<IFilter> GetAllFilters() => Bound(DefaultFilterProvider.Instance.GetAllFilters());

    private IFilter[] Bound(IReadOnlyList<IFilter> filters) =>
        filters.Select(filter => (IFilter)new BoundedFilter(this, filter)).ToArray();

    private sealed class BoundedFilter(ResumePdfFilterProvider owner, IFilter filter) : IFilter
    {
        public bool IsSupported => filter is FlateFilter or Ascii85Filter or AsciiHexDecodeFilter;

        public Memory<byte> Decode(
            Memory<byte> input, DictionaryToken dictionary, IFilterProvider provider, int index)
        {
            owner.cancellation.ThrowIfCancellationRequested();
            if (!IsSupported || input.Length > MaximumStreamBytes)
            {
                throw new ResumeImportException("This PDF uses an unsupported stream. Export a text PDF or DOCX and try again.");
            }
            if (filter is not FlateFilter)
            {
                var result = filter.Decode(input, dictionary, provider, index);
                Charge(result.Length);
                return result;
            }

            using var compressed = new MemoryStream(input.ToArray(), writable: false);
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            using var expanded = ResumeDocumentReader.ReadBounded(inflate,
                Math.Min(MaximumStreamBytes, owner.remainingBytes), owner.cancellation);
            Charge((int)expanded.Length);

            var parameters = DecodeParameterResolver.GetFilterParameters(dictionary, index);
            if (Number(parameters, NameToken.Predictor, 1) <= 1)
            {
                return expanded.GetBuffer().AsMemory(0, (int)expanded.Length);
            }
            if (Number(parameters, NameToken.Columns, 1) is < 1 or > 16384
                || Number(parameters, NameToken.Colors, 1) is < 1 or > 32
                || Number(parameters, NameToken.BitsPerComponent, 8) is not (1 or 2 or 4 or 8 or 16))
            {
                throw new ResumeImportException("This PDF uses an unsupported stream. Export a simpler copy and try again.");
            }

            // PdfPig applies the row predictor after decoding the deflate stream. Validate
            // the decoded length and row dimensions before letting it apply that predictor.
            return filter.Decode(input, dictionary, provider, index);
        }

        private void Charge(int bytes)
        {
            owner.remainingBytes -= bytes;
            if (owner.remainingBytes < 0)
            {
                throw new ResumeImportException("This PDF expands beyond the size limit. Export a simpler copy and try again.");
            }
        }

        private static int Number(DictionaryToken parameters, NameToken name, int fallback) =>
            parameters.TryGet<NumericToken>(name, out var number) ? number.Int : fallback;
    }
}
