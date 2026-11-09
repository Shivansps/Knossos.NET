using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.Classes
{
    /// <summary>Preserves a text file's encoding, BOM and newline style when saving edits.</summary>
    public sealed class TextFileDocument
    {
        private byte[] original;
        private readonly byte[] preamble;
        public string Path { get; }
        public Encoding FileEncoding { get; }
        public string NewLine { get; }
        public string Text { get; private set; }
        public string EncodingLabel => FileEncoding.EncodingName + (preamble.Length > 0 ? " · BOM" : " · no BOM");
        private TextFileDocument(string path, byte[] bytes, Encoding encoding, int bom)
        {
            Path = path; original = bytes; FileEncoding = encoding;
            preamble = bytes.Take(bom).ToArray();
            Text = encoding.GetString(bytes, bom, bytes.Length - bom);
            NewLine = Text.Contains("\r\n") ? "\r\n" : Text.Contains('\r') ? "\r" : "\n";
        }
        public static async Task<TextFileDocument> OpenAsync(string path, CancellationToken token = default)
        {
            var bytes = await File.ReadAllBytesAsync(path, token);
            Encoding encoding; int bom = 0;
            bool Starts(params byte[] prefix) => bytes.AsSpan().StartsWith(prefix);
            if (Starts(0xff, 0xfe, 0, 0)) { encoding = new UTF32Encoding(false, false, true); bom = 4; }
            else if (Starts(0, 0, 0xfe, 0xff)) { encoding = new UTF32Encoding(true, false, true); bom = 4; }
            else if (Starts(0xef, 0xbb, 0xbf)) { encoding = new UTF8Encoding(false, true); bom = 3; }
            else if (Starts(0xff, 0xfe)) { encoding = new UnicodeEncoding(false, false, true); bom = 2; }
            else if (Starts(0xfe, 0xff)) { encoding = new UnicodeEncoding(true, false, true); bom = 2; }
            else
            {
                // Detect common BOM-less UTF-16 before trying UTF-8, whose decoder accepts NUL bytes.
                var pairs = bytes.Length / 2;
                var oddZeros = Enumerable.Range(0, pairs).Count(i => bytes[i * 2 + 1] == 0);
                var evenZeros = Enumerable.Range(0, pairs).Count(i => bytes[i * 2] == 0);
                if (pairs > 1 && oddZeros > pairs / 2 && evenZeros * 4 < pairs) encoding = new UnicodeEncoding(false, false, true);
                else if (pairs > 1 && evenZeros > pairs / 2 && oddZeros * 4 < pairs) encoding = new UnicodeEncoding(true, false, true);
                else
                {
                    encoding = new UTF8Encoding(false, true);
                    try { encoding.GetString(bytes); }
                    catch (DecoderFallbackException)
                    {
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        encoding = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    }
                }
            }
            return new TextFileDocument(path, bytes, encoding, bom);
        }
        public async Task SaveAsync(string text, CancellationToken token = default)
        {
            var current = await File.ReadAllBytesAsync(Path, token);
            if (!current.SequenceEqual(original)) throw new IOException("The file changed outside the editor. Reopen it before saving to avoid overwriting those changes.");
            var normalized = Regex.Replace(text, @"\r\n|\r|\n", NewLine);
            var bytes = preamble.Concat(FileEncoding.GetBytes(normalized)).ToArray();
            var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, Path, true);
                original = bytes; Text = text;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
