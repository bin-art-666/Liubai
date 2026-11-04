using ZXing;
using ZXing.Common;

namespace Liubai;

internal static class QrDecoder
{
    internal const long MaximumPixels = 32_000_000;
    internal static string[] Decode(byte[] bgra, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > MaximumPixels || bgra.LongLength != (long)width * height * 4)
            throw new ArgumentException("Invalid QR image dimensions.");
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = new[] { BarcodeFormat.QR_CODE } }
        };
        var source = new RGBLuminanceSource(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
        // The QR multiple-reader does not apply TryInverted consistently. Decode
        // both polarities explicitly, also supporting light/dark codes together.
        return (reader.DecodeMultiple(source) ?? []).Concat(reader.DecodeMultiple(source.invert()) ?? [])
            .Select(r => r.Text).Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static Uri? WebUri(string text) => text == text.Trim() && !text.Any(char.IsControl)
        && Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
        && !string.IsNullOrEmpty(uri.Host) ? uri : null;

    // Coordinates are physical pixels relative to the frozen virtual desktop.
    internal static byte[] Crop(byte[] pixels, int width, int height, int x, int y, int w, int h)
    {
        if (x < 0 || y < 0 || w < 1 || h < 1 || x > width - w || y > height - h
            || pixels.LongLength != (long)width * height * 4) throw new ArgumentOutOfRangeException(nameof(x));
        var result = new byte[checked(w * h * 4)];
        for (int row = 0; row < h; row++) Buffer.BlockCopy(pixels, checked(((y + row) * width + x) * 4), result, row * w * 4, w * 4);
        return result;
    }
}
