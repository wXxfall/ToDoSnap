using System.IO;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using BitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace TodoSnap.Helpers;

/// <summary>
/// Bridges WPF imaging (<see cref="BitmapSource"/>) with the WinRT imaging stack
/// that the OCR engine requires (<see cref="SoftwareBitmap"/>), and with raw PNG
/// bytes for the online Vision API.
/// </summary>
public static class ImageHelper
{
    /// <summary>Encode a WPF bitmap to PNG bytes.</summary>
    public static byte[] ToPngBytes(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Convert a WPF bitmap to a Bgra8 SoftwareBitmap suitable for
    /// <c>OcrEngine.RecognizeAsync</c>. Goes through an in-memory PNG so the
    /// WinRT <see cref="BitmapDecoder"/> handles all the pixel-format / DPI details.
    /// </summary>
    public static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource source)
    {
        byte[] png = ToPngBytes(source);

        using var ras = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(ras))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        ras.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(ras);
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied);
    }
}
