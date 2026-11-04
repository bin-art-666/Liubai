using Liubai;
using ZXing;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
byte[] Make(string text, int size, bool inverted = false, bool rotated = false)
{
    var matrix = new MultiFormatWriter().encode(text, BarcodeFormat.QR_CODE, size, size,
        new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" });
    var pixels = new byte[size * size * 4];
    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
    {
        bool black = rotated ? matrix[y, size - x - 1] : matrix[x, y];
        byte v = black != inverted ? (byte)0 : (byte)255;
        int i = (y * size + x) * 4; pixels[i] = pixels[i + 1] = pixels[i + 2] = v; pixels[i + 3] = 255;
    }
    return pixels;
}
foreach (var text in new[] { "https://example.com/a?b=1#c", "留白 · 随手扫码", "WIFI:T:WPA;S:Example;P:test;;" })
    foreach (var settings in new[] { (100, false, false), (320, false, false), (240, true, false), (240, false, true) })
        Check(QrDecoder.Decode(Make(text, settings.Item1, settings.Item2, settings.Item3), settings.Item1, settings.Item1).SequenceEqual(new[] { text }), "decode " + settings);
var canvas = Enumerable.Repeat((byte)255, 800 * 300 * 4).ToArray();
var one = Make("first", 240); var two = Make("second", 240);
for (int y = 0; y < 240; y++)
{
    Buffer.BlockCopy(one, y * 240 * 4, canvas, ((y + 30) * 800 + 30) * 4, 240 * 4);
    Buffer.BlockCopy(two, y * 240 * 4, canvas, ((y + 30) * 800 + 500) * 4, 240 * 4);
}
Check(QrDecoder.Decode(canvas, 800, 300).Order().SequenceEqual(new[] { "first", "second" }), "multiple QR");
var inverted = Make("light-on-dark", 240, inverted: true);
var mixed = (byte[])canvas.Clone();
for (int y = 0; y < 240; y++) Buffer.BlockCopy(inverted, y * 240 * 4, mixed, ((y + 30) * 800 + 500) * 4, 240 * 4);
Check(QrDecoder.Decode(mixed, 800, 300).Order().SequenceEqual(new[] { "first", "light-on-dark" }), "mixed QR polarities");
for (int y = 0; y < 240; y++) Buffer.BlockCopy(one, y * 240 * 4, mixed, ((y + 30) * 800 + 500) * 4, 240 * 4);
Check(QrDecoder.Decode(mixed, 800, 300).SequenceEqual(new[] { "first" }), "identical codes collapse to one result");
Check(QrDecoder.Decode(QrDecoder.Crop(canvas, 800, 300, 500, 30, 240, 240), 240, 240).SequenceEqual(new[] { "second" }), "crop offset/stride");
Check(QrDecoder.Decode(Enumerable.Repeat((byte)255, 80 * 80 * 4).ToArray(), 80, 80).Length == 0, "blank image");
foreach (string value in new[] { "https://example.com", "http://localhost/a", "https://例子.测试/a" }) Check(QrDecoder.WebUri(value) != null, "web URI");
foreach (string value in new[] { "javascript:alert(1)", "file:///C:/test", "ms-settings:", "microsoft-edge:https://example.com", "WIFI:T:WPA;S:test;;", "example.com", " https://example.com", "https://example.com\n", "ordinary text" }) Check(QrDecoder.WebUri(value) == null, "non-web result");
try { QrDecoder.Crop(canvas, 800, 300, -1, 0, 40, 40); throw new Exception("negative accepted"); } catch (ArgumentOutOfRangeException) { checks++; }
try { QrDecoder.Decode(new byte[4], int.MaxValue, 2); throw new Exception("oversize accepted"); } catch (ArgumentException) { checks++; }
Console.WriteLine($"PASS: {checks} QR checks");
