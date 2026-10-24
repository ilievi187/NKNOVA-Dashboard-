using System;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nolvus.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Nolvus.Services.Lib
{
    public class LibService : ILibService
    {
        private const int MaximumRemoteImageBytes = 16 * 1024 * 1024;
        private const long MaximumRemoteImagePixels = 40_000_000;
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly UserCredentialProtector _credentialProtector;

        public LibService() : this(null, null) { }

        /// <summary>Allows credential tests to use isolated key storage without touching real user credentials.</summary>
        public LibService(string? credentialStorageDirectory) : this(credentialStorageDirectory, null) { }

        /// <summary>Allows Linux Secret Service integration tests to use an isolated item scope.</summary>
        public LibService(string? credentialStorageDirectory, string? secretServiceScope)
        {
            _credentialProtector = new UserCredentialProtector(credentialStorageDirectory, secretServiceScope);
        }

        public Image GetImageFromUrl(string url)
        {
            return DownloadSafeImage(url);
        }

        public Image GetImageFromWebStream(string imageUrl)
        {
            return DownloadSafeImage(imageUrl);
        }

        private static Image DownloadSafeImage(string url)
        {
            var bytes = DownloadImageBytesAsync(url).GetAwaiter().GetResult();
            if (!HasSupportedImageSignature(bytes))
                throw new InvalidDataException("Remote image format is not supported. Use PNG, JPEG, WebP, GIF, or BMP.");

            var info = Image.Identify(bytes);
            if (info is null || info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaximumRemoteImagePixels)
                throw new InvalidDataException($"Remote image dimensions exceed the {MaximumRemoteImagePixels} pixel limit or are invalid.");

            return Image.Load(new DecoderOptions { MaxFrames = 1 }, bytes);
        }

        private static bool HasSupportedImageSignature(ReadOnlySpan<byte> bytes)
        {
            var png = bytes.Length >= 8 &&
                      bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                      bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
            var jpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            var webp = bytes.Length >= 12 &&
                       bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                       bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
            var gif = bytes.Length >= 6 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
                      bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a';
            var bmp = bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M';

            return png || jpeg || webp || gif || bmp;
        }

        private static async Task<byte[]> DownloadImageBytesAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidDataException("Remote image URL must use HTTP or HTTPS.");
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > MaximumRemoteImageBytes)
                throw new InvalidDataException($"Remote image exceeds the {MaximumRemoteImageBytes} byte limit.");

            using var responseStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var content = new MemoryStream(declaredLength is > 0 ? (int)declaredLength.Value : 0);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                int read;
                while ((read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token).ConfigureAwait(false)) > 0)
                {
                    if (content.Length + read > MaximumRemoteImageBytes)
                        throw new InvalidDataException($"Remote image exceeds the {MaximumRemoteImageBytes} byte limit.");

                    content.Write(buffer, 0, read);
                }

                return content.ToArray();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public Image ResizeKeepAspectRatio(Image source, int width, int height)
        {
            return source.Clone(ctx =>
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(width, height),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                    Sampler = KnownResamplers.Bicubic
                }));
        }

        public Image SetImageOpacity(Image source, float opacity)
        {
            return source.Clone(ctx => ctx.Opacity(opacity));
        }

        public Image SetImageGradient(Image inputImage)
        {

            int w = inputImage.Width;
            int h = inputImage.Height;

            using var gradient = new Image<Rgba32>(w, h);
            gradient.Mutate(g =>
            {
                var brush = new LinearGradientBrush(
                    new PointF(0, 0),
                    new PointF(0, h),
                    GradientRepetitionMode.None,
                    new ColorStop(0f, Color.White),
                    new ColorStop(1f, Color.Transparent));


                g.Fill(brush);
            });

            var result = inputImage.CloneAs<Rgba32>();

            gradient.ProcessPixelRows(result, (gradientAccessor, resultAccessor) =>
            {
                for (int y = 0; y < gradientAccessor.Height; y++)
                {
                    var gradRow = gradientAccessor.GetRowSpan(y);
                    var resRow  = resultAccessor.GetRowSpan(y);

                    for (int x = 0; x < gradRow.Length; x++)
                    {
                        var r = resRow[x];
                        r.A = gradRow[x].A;
                        resRow[x] = r;
                    }
                }
            });

            return result;
        }

        public byte[] ImageToByteArray(Image image)
        {
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return ms.ToArray();
        }

        public string EncryptString(string value)
        {
            return _credentialProtector.Protect(value);
        }

        public string DecryptString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return UserCredentialProtector.IsProtectedValue(value)
                ? _credentialProtector.Unprotect(value)
                : DecryptLegacyString(value);
        }

        private static string DecryptLegacyString(string value)
        {
            string key = "1F2CAB925HFBBCAE589632FABC547892";
            string iv = "ACtEDg70CcU=";

            using var tripleDes = TripleDES.Create();
            tripleDes.Key = Convert.FromBase64String(key);
            tripleDes.IV = Convert.FromBase64String(iv);
            tripleDes.Mode = CipherMode.ECB;
            tripleDes.Padding = PaddingMode.PKCS7;

            using var decryptor = tripleDes.CreateDecryptor(tripleDes.Key, tripleDes.IV);
            var input = Convert.FromBase64String(value);

            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write))
            {
                cs.Write(input, 0, input.Length);
                cs.FlushFinalBlock();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }
}
