// Etc2Transcoder.cs
// DDS (BC1/BC2/BC3/BC7 or uncompressed) -> KTX1 (ETC2), stream based.
//   decode  : BCnEncoder.NET (managed, no native deps)  -> RGBA8
//   resize  : optional 50% box filter (managed)
//   encode  : etc2native.dll (etc2comp fork) RGBA8 -> ETC2
//   wrap    : KTX1 written here in C#
//
// NuGet:  BCnEncoder.Net  (>= 2.2.1)
// Native: etc2native.dll / libetc2native.so  next to the app (or runtimes/<rid>/native)
//
// Mapping (forceRgba8 == false):
//   DXT1 (no alpha) -> ETC2_RGB
//   DXT1 (alpha)    -> ETC2_RGBA1
//   DXT3/DXT5/BC7   -> ETC2_RGBA8
//   uncompressed    -> ETC2_RGBA8   (only if forceTranscodeUncompressed)
// forceRgba8 == true forces ETC2_RGBA8 for everything.
// forceResize halves W/H (and regenerates the mip chain when the source had one).

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;

namespace Etc2
{
    public enum Etc2Status
    {
        Transcoded = 0,
        NotCompressed = 1,     // uncompressed source, left untouched
        Skipped = 2,           // intentionally filtered out (e.g. onlyBc7 and source isn't BC7); left untouched
        ErrorInput = -1,       // not a DDS / truncated
        ErrorUnhandled = -2,   // a format we don't handle
        ErrorEncode = -3,      // native ETC2 encode failed
    }

    /// <summary>ETC2 target, matching etc2native's format ints and the KTX GL enums.</summary>
    internal enum Etc2Format { Rgb = 0, Rgba1 = 1, Rgba8 = 2 }

    public static class Etc2Transcoder
    {
        private const string Lib = "etc2native";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int etc2_encode_rgba8(
            byte[] rgba, int width, int height, int format, float effort, int errMetric, int jobs,
            out IntPtr outBits, out int outBytes, out int extW, out int extH);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void etc2_free(IntPtr p);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr etc2_version();

        /// <summary>Returns the native library version string (confirms which etc2native is loaded).</summary>
        public static string NativeVersion()
        {
            var p = etc2_version();
            return p == IntPtr.Zero ? "" : (Marshal.PtrToStringAnsi(p) ?? "");
        }

        // ---- GL enums for the KTX1 header ----
        private const uint GL_RGB = 0x1907, GL_RGBA = 0x1908;
        private const uint GL_ETC2_RGB = 0x9274, GL_ETC2_RGBA1 = 0x9276, GL_ETC2_RGBA8 = 0x9278;

        /// <param name="quality">etc2comp effort 0..100 (higher = better/slower).</param>
        /// <param name="jobs">encoder worker threads (1 = single; you can also call this from many C# threads).</param>
        public static Etc2Status Transcode(Stream input, Stream output,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            bool onlyBc7 = false, float quality = 60f, int jobs = 1)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));
            byte[] dds = ReadAll(input);
            return TranscodeBytes(dds, output, forceRgba8, forceResize, forceTranscodeUncompressed, onlyBc7, quality, jobs);
        }

        public static Etc2Status TranscodeBytes(byte[] dds, Stream output,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            bool onlyBc7 = false, float quality = 60f, int jobs = 1)
        {
            if (!Dds.Parse(dds, out var s)) return Etc2Status.ErrorInput;
            if (!s.Handled) return Etc2Status.ErrorUnhandled;

            // Device-capability filter: transcode ONLY BC7 sources, leave everything
            // else (DXT1/3/5 and uncompressed) untouched. forceRgba8 overrides this.
            if (onlyBc7 && !forceRgba8 && s.CmpFormat != Dds.Bc.Bc7) return Etc2Status.Skipped;

            if (!s.Compressed && !(forceRgba8 || forceTranscodeUncompressed)) return Etc2Status.NotCompressed;

            // Decide target format.
            Etc2Format fmt;
            if (forceRgba8 || !s.Compressed) fmt = Etc2Format.Rgba8;
            else if (s.CmpFormat == Dds.Bc.Dxt1)
                fmt = Dds.Dxt1UsesAlpha(dds, s.DataOffset, s.Width, s.Height) ? Etc2Format.Rgba1 : Etc2Format.Rgb;
            else fmt = Etc2Format.Rgba8;   // DXT3/DXT5/BC7

            // leaveOpen:true => we never close/own the caller's stream.
            using var bw = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

            if (forceResize)
            {
                int newW = Math.Max(1, s.Width / 2), newH = Math.Max(1, s.Height / 2);
                int outMips = s.Mips > 1 ? MipCount(newW, newH) : 1;
                WriteKtxHeader(bw, fmt, newW, newH, outMips);

                byte[] full = LevelToRgba(dds, s, 0);                 // base, full res
                byte[] rgba = HalfBox(full, s.Width, s.Height, newW, newH);
                int cw = newW, ch = newH;
                for (int lv = 0; lv < outMips; lv++)
                {
                    if (!EncodeAppend(bw, rgba, cw, ch, fmt, quality, jobs)) return Etc2Status.ErrorEncode;
                    if (lv + 1 < outMips)
                    {
                        int nw = Math.Max(1, cw / 2), nh = Math.Max(1, ch / 2);
                        rgba = HalfBox(rgba, cw, ch, nw, nh); cw = nw; ch = nh;
                    }
                }
            }
            else
            {
                WriteKtxHeader(bw, fmt, s.Width, s.Height, s.Mips);
                for (int lv = 0; lv < s.Mips; lv++)
                {
                    int lw = Math.Max(1, s.Width >> lv), lh = Math.Max(1, s.Height >> lv);
                    byte[] rgba = LevelToRgba(dds, s, lv);
                    if (!EncodeAppend(bw, rgba, lw, lh, fmt, quality, jobs)) return Etc2Status.ErrorEncode;
                }
            }
            bw.Flush();
            return Etc2Status.Transcoded;
        }

        public static Task<Etc2Status> TranscodeAsync(Stream input, Stream output,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            bool onlyBc7 = false, float quality = 60f, int jobs = 1, CancellationToken ct = default)
            => Task.Run(() => Transcode(input, output, forceRgba8, forceResize, forceTranscodeUncompressed, onlyBc7, quality, jobs), ct);

        /// <summary>
        /// Transcode DDS bytes and return the KTX1 bytes, or null if the source was left
        /// untouched (NotCompressed). Throws InvalidDataException on a hard error.
        /// No streams to manage — ideal for parallel batch work.
        /// Returns (et2status, byte[]?)
        /// </summary>
        public static (Etc2Status etc2status, byte[]? bytes) TranscodeToKtxBytes(byte[] dds,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            bool onlyBc7 = false, float quality = 60f, int jobs = 1)
        {
            using var ms = new MemoryStream();
            var st = TranscodeBytes(dds, ms, forceRgba8, forceResize, forceTranscodeUncompressed, onlyBc7, quality, jobs);

            if (st != Etc2Status.Transcoded && st != Etc2Status.NotCompressed && st != Etc2Status.Skipped)
                throw new InvalidDataException($"DDS->KTX transcode failed: {st}");

            return (st, ms.ToArray());
        }

        /// <summary>
        /// Like TranscodeToKtxBytes but also reports the exact status, so callers can tell
        /// NotCompressed vs Skipped vs Transcoded without ambiguity (no bogus log entries).
        /// On Transcoded, <paramref name="ktx"/> holds the bytes; otherwise it is null.
        /// Throws InvalidDataException only on a hard error.
        /// </summary>
        public static Etc2Status TryTranscode(byte[] dds, out byte[]? ktx,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            bool onlyBc7 = false, float quality = 60f, int jobs = 1)
        {
            ktx = null;
            using var ms = new MemoryStream();
            var st = TranscodeBytes(dds, ms, forceRgba8, forceResize, forceTranscodeUncompressed, onlyBc7, quality, jobs);
            switch (st)
            {
                case Etc2Status.Transcoded: ktx = ms.ToArray(); return st;
                case Etc2Status.NotCompressed:
                case Etc2Status.Skipped: return st;
                default: throw new InvalidDataException($"DDS->KTX transcode failed: {st}");
            }
        }

        /// <summary>
        /// File-to-file. Reads the input fully (releasing its handle immediately), encodes in
        /// memory, and writes the output ONLY on success — so failed/uncompressed inputs never
        /// create or lock an output file. Safe to call from many threads on distinct paths.
        /// </summary>
        public static Etc2Status TranscodeFile(string inputPath, string outputPath,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            bool onlyBc7 = false, float quality = 60f, int jobs = 1)
        {
            byte[] dds = File.ReadAllBytes(inputPath);                 // opens + closes input handle now
            using var ms = new MemoryStream();
            var st = TranscodeBytes(dds, ms, forceRgba8, forceResize, forceTranscodeUncompressed, onlyBc7, quality, jobs);
            if (st != Etc2Status.Transcoded) return st;               // nothing written, no output file touched

            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            ms.Position = 0;
            ms.CopyTo(fs);
            return st;
        }

        // ---- decode one source level to a tight RGBA8 byte[] --------------------
        private static readonly BcDecoder _decoder = new BcDecoder();

        private static byte[] LevelToRgba(byte[] dds, in Dds.Info s, int level)
        {
            int w = Math.Max(1, s.Width >> level), h = Math.Max(1, s.Height >> level);

            if (!s.Compressed)
            {
                long off = s.LevelOffset(level);
                return Dds.UnpackUncompressed(dds, (int)off, w, h, s);
            }

            // locate this level's block data
            long blockOff = s.LevelOffset(level);
            int blockSize = Dds.BcLevelBytes(w, h, s.BlockBytes);
            var slice = new byte[blockSize];
            Buffer.BlockCopy(dds, (int)blockOff, slice, 0, blockSize);

            var format = s.CmpFormat switch
            {
                Dds.Bc.Dxt1 => Dds.Dxt1UsesAlpha(dds, blockOff, w, h)
                                    ? CompressionFormat.Bc1WithAlpha : CompressionFormat.Bc1,
                Dds.Bc.Dxt3 => CompressionFormat.Bc2,
                Dds.Bc.Dxt5 => CompressionFormat.Bc3,
                Dds.Bc.Bc7 => CompressionFormat.Bc7,
                _ => CompressionFormat.Bc1,
            };

            ColorRgba32[] pix = _decoder.DecodeRaw(slice, w, h, format);
            var rgba = new byte[(long)w * h * 4];
            for (int i = 0; i < pix.Length; i++)
            {
                rgba[i * 4 + 0] = pix[i].r;
                rgba[i * 4 + 1] = pix[i].g;
                rgba[i * 4 + 2] = pix[i].b;
                rgba[i * 4 + 3] = pix[i].a;
            }
            return rgba;
        }

        private static bool EncodeAppend(BinaryWriter bw, byte[] rgba, int w, int h,
                                         Etc2Format fmt, float quality, int jobs)
        {
            int rc = etc2_encode_rgba8(rgba, w, h, (int)fmt, quality, /*REC709*/1, Math.Max(1, jobs),
                                       out IntPtr bits, out int bytes, out _, out _);
            if (rc != 0 || bits == IntPtr.Zero || bytes <= 0) { if (bits != IntPtr.Zero) etc2_free(bits); return false; }
            try
            {
                var buf = new byte[bytes];
                Marshal.Copy(bits, buf, 0, bytes);
                bw.Write((uint)bytes);   // KTX imageSize
                bw.Write(buf);           // ETC2 level data (multiple of 8 -> already 4-aligned)
            }
            finally { etc2_free(bits); }
            return true;
        }

        private static void WriteKtxHeader(BinaryWriter bw, Etc2Format fmt, int w, int h, int mips)
        {
            uint glInternal = fmt switch
            {
                Etc2Format.Rgb => GL_ETC2_RGB,
                Etc2Format.Rgba1 => GL_ETC2_RGBA1,
                _ => GL_ETC2_RGBA8,
            };
            uint glBase = fmt == Etc2Format.Rgb ? GL_RGB : GL_RGBA;

            bw.Write(new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A });
            bw.Write(0x04030201u); // endianness
            bw.Write(0u);          // glType (compressed)
            bw.Write(1u);          // glTypeSize
            bw.Write(0u);          // glFormat (compressed)
            bw.Write(glInternal);
            bw.Write(glBase);
            bw.Write((uint)w);
            bw.Write((uint)h);
            bw.Write(0u);          // pixelDepth
            bw.Write(0u);          // arrayElements
            bw.Write(1u);          // faces
            bw.Write((uint)mips);
            bw.Write(0u);          // bytesOfKeyValueData
        }

        // 2x2 box downsample of a tight RGBA8 buffer.
        private static byte[] HalfBox(byte[] src, int sw, int sh, int dw, int dh)
        {
            var dst = new byte[(long)dw * dh * 4];
            for (int y = 0; y < dh; y++)
            {
                int y0 = y * 2, y1 = (y * 2 + 1 < sh) ? y * 2 + 1 : y0;
                for (int x = 0; x < dw; x++)
                {
                    int x0 = x * 2, x1 = (x * 2 + 1 < sw) ? x * 2 + 1 : x0;
                    int i00 = (y0 * sw + x0) * 4, i01 = (y0 * sw + x1) * 4;
                    int i10 = (y1 * sw + x0) * 4, i11 = (y1 * sw + x1) * 4;
                    int o = (y * dw + x) * 4;
                    for (int c = 0; c < 4; c++)
                        dst[o + c] = (byte)((src[i00 + c] + src[i01 + c] + src[i10 + c] + src[i11 + c] + 2) / 4);
                }
            }
            return dst;
        }

        private static int MipCount(int w, int h)
        {
            int m = 1, d = Math.Max(w, h);
            while (d > 1) { d >>= 1; m++; }
            return m;
        }

        private static byte[] ReadAll(Stream s)
        {
            if (s is MemoryStream ms && ms.TryGetBuffer(out var seg) && seg.Offset == 0 && seg.Count == seg.Array!.Length)
                return seg.Array;
            using var copy = new MemoryStream();
            s.CopyTo(copy);
            return copy.ToArray();
        }
    }

    // ---- minimal DDS parsing (header + mip layout), no external deps ------------
    internal static class Dds
    {
        public enum Bc { None, Dxt1, Dxt3, Dxt5, Bc7 }

        public struct Info
        {
            public bool Compressed, Handled, HasAlpha;
            public Bc CmpFormat;
            public int BlockBytes;          // compressed: 8/16
            public int UnitBytes;           // uncompressed: bytes/pixel (2/3/4)
            public uint RMask, GMask, BMask, AMask; // uncompressed channel bit masks (AMask==0 => no alpha)
            public int Width, Height, Mips;
            public int DataOffset;

            public long LevelOffset(int level)
            {
                long off = DataOffset;
                for (int l = 0; l < level; l++)
                {
                    int lw = Math.Max(1, Width >> l), lh = Math.Max(1, Height >> l);
                    off += Compressed ? BcLevelBytes(lw, lh, BlockBytes) : (long)lw * lh * UnitBytes;
                }
                return off;
            }
        }

        public static int BcLevelBytes(int w, int h, int blockBytes)
            => ((Math.Max(1, w) + 3) / 4) * ((Math.Max(1, h) + 3) / 4) * blockBytes;

        private static uint R32(byte[] d, int o) => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        public static bool Parse(byte[] d, out Info s)
        {
            s = default; s.Mips = 1;
            if (d == null || d.Length < 128) return false;
            if (R32(d, 0) != 0x20534444 || R32(d, 4) != 124) return false;

            s.Height = (int)R32(d, 12); s.Width = (int)R32(d, 16);
            uint mc = R32(d, 28); s.Mips = mc != 0 ? (int)mc : 1;

            int pf = 76; uint pfFlags = R32(d, pf + 4); uint fourCC = R32(d, pf + 8);
            uint bits = R32(d, pf + 12);
            uint rM = R32(d, pf + 16), gM = R32(d, pf + 20), bM = R32(d, pf + 24), aM = R32(d, pf + 28);
            s.DataOffset = 128;

            const uint DDPF_ALPHA = 0x1, DDPF_FOURCC = 0x4, DDPF_RGB = 0x40, DDPF_LUMINANCE = 0x20000, DDPF_ALPHAONLY = 0x2;

            if ((pfFlags & DDPF_FOURCC) != 0)
            {
                if (fourCC == 0x30315844) // "DX10"
                {
                    if (d.Length < 148) return false;
                    uint dxgi = R32(d, 128); s.DataOffset = 148;
                    switch (dxgi)
                    {
                        case 71: case 72: s.Compressed = true; s.CmpFormat = Bc.Dxt1; s.BlockBytes = 8; s.Handled = true; return true;
                        case 74: case 75: s.Compressed = true; s.CmpFormat = Bc.Dxt3; s.BlockBytes = 16; s.Handled = true; return true;
                        case 77: case 78: s.Compressed = true; s.CmpFormat = Bc.Dxt5; s.BlockBytes = 16; s.Handled = true; return true;
                        case 98: case 99: s.Compressed = true; s.CmpFormat = Bc.Bc7; s.BlockBytes = 16; s.Handled = true; return true;
                        // uncompressed DX10 -> express as masks
                        case 28: case 29: SetUnc(ref s, 4, 0x000000FFu, 0x0000FF00u, 0x00FF0000u, 0xFF000000u); return true; // R8G8B8A8
                        case 87: case 91: SetUnc(ref s, 4, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0xFF000000u); return true; // B8G8R8A8
                        case 88: case 93: SetUnc(ref s, 4, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, 0u);          return true; // B8G8R8X8
                        default: s.Handled = false; return true;
                    }
                }
                switch (fourCC)
                {
                    case 0x31545844: s.Compressed = true; s.CmpFormat = Bc.Dxt1; s.BlockBytes = 8; s.Handled = true; return true;  // DXT1
                    case 0x33545844: s.Compressed = true; s.CmpFormat = Bc.Dxt3; s.BlockBytes = 16; s.Handled = true; return true; // DXT3
                    case 0x35545844: s.Compressed = true; s.CmpFormat = Bc.Dxt5; s.BlockBytes = 16; s.Handled = true; return true; // DXT5
                    default: s.Compressed = true; s.Handled = false; return true; // ATI2/3Dc/FP/etc.
                }
            }

            // ---- uncompressed (RGB / RGBA / luminance), 16/24/32-bit, via bit masks ----
            if ((pfFlags & (DDPF_RGB | DDPF_LUMINANCE | DDPF_ALPHAONLY)) != 0 && (bits == 16 || bits == 24 || bits == 32))
            {
                int bpp = (int)bits / 8;
                bool hasA = (pfFlags & DDPF_ALPHA) != 0 && aM != 0;
                if ((pfFlags & DDPF_LUMINANCE) != 0)
                {
                    // L (and optional A): replicate luminance to RGB
                    int lbits = (bits == 16 && hasA) ? 8 : (int)bits;
                    uint lM = rM != 0 ? rM : (uint)((1 << lbits) - 1);
                    SetUnc(ref s, bpp, lM, lM, lM, hasA ? aM : 0u);
                    return true;
                }
                SetUnc(ref s, bpp, rM, gM, bM, hasA ? aM : 0u);
                // if masks were absent, fall back to a sane default for the bit depth
                if (s.RMask == 0 && s.GMask == 0 && s.BMask == 0)
                {
                    if (bits == 32) SetUnc(ref s, 4, 0x00FF0000u, 0x0000FF00u, 0x000000FFu, hasA ? 0xFF000000u : 0u);
                    else if (bits == 16) SetUnc(ref s, 2, 0x7C00u, 0x03E0u, 0x001Fu, 0u);
                    else SetUnc(ref s, 3, 0xFF0000u, 0x00FF00u, 0x0000FFu, 0u);
                }
                return true;
            }

            s.Handled = false; return true;
        }

        private static void SetUnc(ref Info s, int unitBytes, uint r, uint g, uint b, uint a)
        {
            s.Compressed = false; s.Handled = true; s.UnitBytes = unitBytes;
            s.RMask = r; s.GMask = g; s.BMask = b; s.AMask = a; s.HasAlpha = a != 0;
        }

        private static int Shift(uint m) { int o = 0; if (m == 0) return 0; while ((m & 1) == 0) { m >>= 1; o++; } return o; }
        private static int Bits(uint m) { int c = 0; while (m != 0) { c += (int)(m & 1); m >>= 1; } return c; }
        private static byte Chan(uint val, uint mask)
        {
            if (mask == 0) return 255;
            int sh = Shift(mask), bits = Bits(mask);
            uint v = (val & mask) >> sh;
            int maxv = (1 << bits) - 1;
            return (byte)((v * 255 + maxv / 2) / maxv);
        }

        public static byte[] UnpackUncompressed(byte[] d, int off, int w, int h, in Info s)
        {
            var rgba = new byte[(long)w * h * 4];
            int bpp = s.UnitBytes;
            for (int i = 0; i < w * h; i++)
            {
                int p = off + i * bpp;
                uint v = bpp switch
                {
                    2 => (uint)(d[p] | (d[p + 1] << 8)),
                    3 => (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16)),
                    _ => (uint)(d[p] | (d[p + 1] << 8) | (d[p + 2] << 16) | (d[p + 3] << 24)),
                };
                rgba[i * 4 + 0] = Chan(v, s.RMask);
                rgba[i * 4 + 1] = Chan(v, s.GMask);
                rgba[i * 4 + 2] = Chan(v, s.BMask);
                rgba[i * 4 + 3] = s.AMask != 0 ? Chan(v, s.AMask) : (byte)255;
            }
            return rgba;
        }

        // BC1 block scan: punch-through alpha used when color0 <= color1 and an index == 3.
        public static bool Dxt1UsesAlpha(byte[] d, long blockOff, int w, int h)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            for (int i = 0; i < bw * bh; i++)
            {
                int b = (int)blockOff + i * 8;
                int c0 = d[b] | (d[b + 1] << 8);
                int c1 = d[b + 2] | (d[b + 3] << 8);
                if (c0 <= c1)
                {
                    uint idx = (uint)(d[b + 4] | (d[b + 5] << 8) | (d[b + 6] << 16) | (d[b + 7] << 24));
                    for (int t = 0; t < 16; t++) if (((idx >> (t * 2)) & 3) == 3) return true;
                }
            }
            return false;
        }
    }
}
