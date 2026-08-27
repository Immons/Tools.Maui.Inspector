using System.IO.Compression;
using System.Text;

namespace Immons.Tools.Maui.Inspector.Features.Memory.HeapDumps;

/// <summary>
/// Heap-dump reports as they are kept in the app: gzipped. They are type histograms and chains —
/// text that compresses roughly tenfold — and the app holds several so the panel can compare them.
/// </summary>
internal static class ReportStore
{
    public static byte[] Pack(string json)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes, 0, bytes.Length);
        }
        return packed.ToArray();
    }

    public static string Unpack(byte[] packed)
    {
        using var source = new MemoryStream(packed);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var text = new StreamReader(gzip, Encoding.UTF8);
        return text.ReadToEnd();
    }
}
