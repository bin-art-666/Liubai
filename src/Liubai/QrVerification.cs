using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using ZXing;

namespace Liubai;

internal sealed partial class QrResultWindow
{
    internal async Task VerifyAuthorizationCard(DesktopMonitor display)
    {
        using var lease=KeepOpen();
        const string value="https://open.weixin.qq.com/connect/confirm?uuid=local-verification-only";
        Open([value],display,display.Info.Work.Top);
        await Task.Delay(100);
        var card=(StackPanel)((Border)results.Children.Single()).Child;
        if(!card.Children.OfType<TextBlock>().Any(t=>t.Text=="微信登录授权"))throw new Exception("Missing WeChat authorization classification");
        if(card.Children.OfType<TextBox>().Single().Text.Contains("local-verification-only"))throw new Exception("Authorization token displayed");
        var buttons=card.Children.OfType<StackPanel>().Single();
        if(buttons.Children.OfType<Button>().Any(b=>Equals(b.Content,"用 Edge 打开")))throw new Exception("WeChat authorization still has a browser action");
        if(card.Children.OfType<TextBlock>().All(t=>!t.Text.Contains("尚未验证")))throw new Exception("Missing desktop authorization compatibility boundary");
    }
}

public sealed partial class BarWindow
{
    async Task VerifyQr()
    {
        string result = "PASS", error = ""; var checks = new List<string>(); Window? fixture = null;
        try
        {
            const string payload = "留白扫码测试 · local QR test";
            var matrix = new MultiFormatWriter().encode(payload, BarcodeFormat.QR_CODE, 320, 320,
                new Dictionary<EncodeHintType, object> { [EncodeHintType.CHARACTER_SET] = "UTF-8" });
            var bytes = new byte[320 * 320 * 4];
            for (int y = 0; y < 320; y++) for (int x = 0; x < 320; x++)
            { int i = (y * 320 + x) * 4; byte v = matrix[x, y] ? (byte)0 : (byte)255; bytes[i] = bytes[i + 1] = bytes[i + 2] = v; bytes[i + 3] = 255; }
            if (!QrDecoder.Decode(bytes, 320, 320).SequenceEqual(new[] { payload })) throw new Exception("QR decode failed");
            checks.Add("Generated QR decoded locally");
            await Task.Delay(300);
            var cancel = QrSelectionWindow.Select(); await Task.Delay(700);
            if (!QrSelectionWindow.Active) throw new Exception("Selection overlay not active");
            QrSelectionWindow.CancelActive();
            if (await cancel != null || QrSelectionWindow.Active) throw new Exception("Cancel did not release overlay");
            checks.Add("Frozen desktop overlay opened and cancelled without clipboard access");
            qrResults ??= new QrResultWindow(() => _ = ScanQr());
            qrResults.Open(new[] { payload, "https://example.com/" }, Display, AppWindow.Position.Y + AppWindow.Size.Height);
            await Task.Delay(400);
            if (!qrResults.IsOpen) throw new Exception("Results not open");
            Activate(); await Task.Delay(400);
            if (qrResults.IsOpen) throw new Exception("Results did not dismiss outside");
            checks.Add("Multiple-result card and outside dismissal");
            if(await OpenQrLink(new Uri("https://open.weixin.qq.com/connect/confirm?uuid=local-verification-only")))throw new Exception("Authorization escaped browser launch boundary");
            checks.Add("Direct browser launch boundary rejects WeChat authorization before invoking Edge");
            await qrResults.VerifyAuthorizationCard(Display);qrResults.Hide();
            checks.Add("WeChat authorization card hides ephemeral parameters, omits Edge action and describes unverified desktop authorization");
            if (Environment.GetCommandLineArgs().Contains("--interactive-qr"))
            {
                fixture = new Window { Title = "留白 · 扫码测试图" };
                var bitmap = new WriteableBitmap(320, 320);
                using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(bytes);
                bitmap.Invalidate();
                var layout = new StackPanel { Spacing = 12, Padding = new Thickness(30), Background = new SolidColorBrush(Colors.White) };
                layout.Children.Add(new TextBlock { Text = "框选下面的测试二维码", FontSize = 20, Foreground = new SolidColorBrush(Colors.Black) });
                layout.Children.Add(new Image { Source = bitmap, Width = 250, Height = 250, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left });
                fixture.Content = layout;
                var work = Display.Info.Work; double scale = Display.Dpi / 96d;
                fixture.AppWindow.MoveAndResize(new RectInt32(work.Left + 100, work.Top + 100, (int)(390 * scale), (int)(400 * scale)));
                fixture.Activate(); await Task.Delay(600);
                var selected = await QrSelectionWindow.Select().WaitAsync(TimeSpan.FromSeconds(120));
                if (selected == null) throw new Exception("Interactive selection cancelled");
                var decoded = await Task.Run(() => QrDecoder.Decode(selected.Pixels, selected.Width, selected.Height));
                if (!decoded.SequenceEqual(new[] { payload })) throw new Exception("Interactive selection decoded wrong content");
                checks.Add($"Real pointer selection decoded {selected.Width}x{selected.Height} physical pixels");
                qrResults.Open(decoded, Display, AppWindow.Position.Y + AppWindow.Size.Height);
                await Task.Delay(15000); qrResults.Hide();
            }
            if (Environment.GetCommandLineArgs().Contains("--verify-qr-edge"))
            {
                if (!await OpenQrLink(new Uri("https://example.com/"))) throw new Exception("Edge launch rejected");
                checks.Add("Edge accepted official microsoft-edge URI launch for example.com");
            }
        }
        catch (Exception ex) { result = "FAIL"; error = ex.ToString(); }
        finally
        {
            QrSelectionWindow.CancelActive(); fixture?.Close(); qrResults?.Hide();
            Directory.CreateDirectory(App.DataDirectory);
            File.WriteAllText(Path.Combine(App.DataDirectory, "qr-check.json"), System.Text.Json.JsonSerializer.Serialize(new { result, error, checks }));
            Close();
        }
    }
}
