using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Knossos.NET.Classes
{
    /// <summary>
    /// Parses APNG files and exposes a streaming composer that renders frames
    /// on demand — no pre-composed pixel arrays are kept in memory.
    ///
    /// RAM cost at runtime:
    ///    Raw compressed IDAT/fdAT bytes for all frames (~same as file size)
    ///    One canvas buffer: W × H × 4 bytes  (single live copy, reused every frame)
    ///    One extra canvas snapshot only while a DISPOSE_PREVIOUS frame is active
    ///
    /// Usage:
    ///            var apng = APNGHelper.ReadApng(_previewStream!);
    ///            if (apng != null && apng.Frames.Count > 0)
    ///            {
    ///                var composer = apng.CreateComposer();
    ///                var localCts = _cts;
    ///                Task.Factory.StartNew(async () =>
    ///                {
    ///                    do
    ///                    {
    ///                        if (localCts?.IsCancellationRequested == true) break;
    ///                        var bmp = composer.NextFrame(out int delayMs);
    ///                        var old = ImageSource;
    ///                        ImageSource = bmp;
    ///                        await Task.Delay(delayMs);
    ///                        old?.Dispose();
    ///                    } while (true);
    ///                    composer.Dispose();
    ///                 });
    ///             }
    /// </summary>
    public static class APNGHelper
    {
        private static readonly byte[] PngSignature =
            { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private const byte DisposeOpNone       = 0;
        private const byte DisposeOpBackground = 1;
        private const byte DisposeOpPrevious   = 2;
        private const byte BlendOpSource       = 0;
        private const byte BlendOpOver         = 1;

        /// <summary>
        /// One APNG frame: only the raw compressed bytes and metadata.
        /// Pixels are decoded on demand in <see cref="ApngComposer.NextFrame"/>.
        /// </summary>
        public sealed class ApngRawFrame
        {
            public int    Width, Height, X, Y;
            public int    DelayNum, DelayDen;
            public byte   DisposeOp, BlendOp;
            public byte[] CompressedData = Array.Empty<byte>(); // merged zlib IDAT payload

            public int DelayMs
            {
                get
                {
                    int den = DelayDen == 0 ? 100 : DelayDen;
                    return Math.Max(1, (int)Math.Round(DelayNum * 1000.0 / den));
                }
            }
        }

        /// <summary>
        /// Parsed APNG: canvas metadata + list of raw (compressed) frames.
        /// Pixel decoding is deferred until <see cref="CreateComposer"/> is used.
        /// </summary>
        public sealed class ApngFile
        {
            public int                       CanvasWidth    { get; init; }
            public int                       CanvasHeight   { get; init; }
            public int                       NumPlays       { get; init; } // 0 = loop forever
            public byte                      ColorType      { get; init; }
            public (byte R, byte G, byte B)? TransparentRgb { get; init; }
            public List<ApngRawFrame>        Frames         { get; init; } = new();

            /// <summary>
            /// Creates a new independent composer for this APNG.
            /// Each composer has its own canvas; call this once per animation instance.
            /// </summary>
            public ApngComposer CreateComposer() => new ApngComposer(this);
        }

        /// <summary>
        /// Stateful on-the-fly APNG composer.
        /// Maintains one canvas buffer and decodes+composites frames one at a time.
        /// Call <see cref="NextFrame"/> each tick; dispose the returned bitmap after display.
        /// </summary>
        public sealed class ApngComposer : IDisposable
        {
            private readonly ApngFile _apng;
            private readonly byte[]   _canvas;      // RGBA8888, single live copy
            private byte[]?           _prevCanvas;  // only alive during DISPOSE_PREVIOUS frames
            private int               _frameIndex;
            private bool              _disposed;

            internal ApngComposer(ApngFile apng)
            {
                _apng   = apng;
                _canvas = new byte[apng.CanvasWidth * apng.CanvasHeight * 4];
            }

            /// <summary>Total number of frames in the animation.</summary>
            public int FrameCount => _apng.Frames.Count;

            /// <summary>Current frame index (0-based).</summary>
            public int FrameIndex => _frameIndex;

            /// <summary>Canvas width.</summary>
            public int CanvasWidth  => _apng.CanvasWidth;

            /// <summary>Canvas height.</summary>
            public int CanvasHeight => _apng.CanvasHeight;

            /// <summary>
            /// Composes and returns the next animation frame as a WriteableBitmap.
            /// Automatically wraps around to frame 0 after the last frame.
            /// The CALLER must dispose the returned bitmap after displaying it.
            /// </summary>
            /// <param name="delayMs">Display duration for this frame in milliseconds.</param>
            public WriteableBitmap NextFrame(out int delayMs)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ApngComposer));

                var raw = _apng.Frames[_frameIndex];
                delayMs = raw.DelayMs;

                // Snapshot canvas before compositing if we'll need to restore it afterward
                if (raw.DisposeOp == DisposeOpPrevious)
                    _prevCanvas = (byte[])_canvas.Clone();

                // Decode this frame's compressed pixels → RGBA8888
                byte[] frameRgba = DecodeFrame(raw, _apng.ColorType, _apng.TransparentRgb);

                // Composite onto the persistent canvas
                CompositeFrame(_canvas, _apng.CanvasWidth, frameRgba, raw);

                // Wrap canvas pixels into a WriteableBitmap for display
                var bmp = CanvasToWriteableBitmap(_canvas, _apng.CanvasWidth, _apng.CanvasHeight);

                AdvanceCanvasState(raw);
                _frameIndex = (_frameIndex + 1) % _apng.Frames.Count;
                return bmp;
            }

            // ─────────────────────────────────────────────────────────────────────
            //  render directly into an existing WriteableBitmap.
            //  This avoids allocating a new WriteableBitmap (and its W*H*4 buffer)
            //  on every frame. The target MUST be sized exactly
            //  CanvasWidth x CanvasHeight and use PixelFormat.Rgba8888 /
            //  AlphaFormat.Unpremul. After calling this, invalidate the visual
            //  that displays the bitmap so the new pixels are repainted.
            // ─────────────────────────────────────────────────────────────────────
            public void RenderNextFrameTo(WriteableBitmap target, out int delayMs)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ApngComposer));
                if (target == null) throw new ArgumentNullException(nameof(target));
                if (target.PixelSize.Width  != _apng.CanvasWidth ||
                    target.PixelSize.Height != _apng.CanvasHeight)
                {
                    throw new ArgumentException(
                        $"Target size {target.PixelSize} doesn't match canvas " +
                        $"({_apng.CanvasWidth}x{_apng.CanvasHeight}).", nameof(target));
                }

                var raw = _apng.Frames[_frameIndex];
                delayMs = raw.DelayMs;

                if (raw.DisposeOp == DisposeOpPrevious)
                    _prevCanvas = (byte[])_canvas.Clone();

                byte[] frameRgba = DecodeFrame(raw, _apng.ColorType, _apng.TransparentRgb);
                CompositeFrame(_canvas, _apng.CanvasWidth, frameRgba, raw);

                // Copy the canvas straight into the existing locked framebuffer.
                using (var fb = target.Lock())
                    Marshal.Copy(_canvas, 0, fb.Address, _canvas.Length);

                AdvanceCanvasState(raw);
                _frameIndex = (_frameIndex + 1) % _apng.Frames.Count;
            }

            /// <summary>Resets the animation back to the first frame and clears the canvas.</summary>
            public void Reset()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(ApngComposer));
                Array.Clear(_canvas, 0, _canvas.Length);
                _prevCanvas = null;
                _frameIndex = 0;
            }

            /// <summary>
            /// Allocates a new WriteableBitmap matching this composer's canvas, suitable
            /// as the reusable target for <see cref="RenderNextFrameTo"/>.
            /// </summary>
            public WriteableBitmap CreateMatchingBitmap() =>
                new WriteableBitmap(
                    new PixelSize(_apng.CanvasWidth, _apng.CanvasHeight),
                    new Vector(96, 96),
                    PixelFormat.Rgba8888,
                    AlphaFormat.Unpremul);

            private void AdvanceCanvasState(ApngRawFrame raw)
            {
                switch (raw.DisposeOp)
                {
                    case DisposeOpBackground:
                        ClearRegion(_canvas, _apng.CanvasWidth, raw.X, raw.Y, raw.Width, raw.Height);
                        break;
                    case DisposeOpPrevious:
                        Buffer.BlockCopy(_prevCanvas!, 0, _canvas, 0, _canvas.Length);
                        _prevCanvas = null;
                        break;
                    // DisposeOpNone: leave canvas as-is
                }
            }

            public void Dispose()
            {
                _disposed   = true;
                _prevCanvas = null;
            }
        }

        /// <summary>
        /// Returns true if <paramref name="pngStream"/> is an APNG (has acTL chunk).
        /// Does not close the stream. Throws if not a valid PNG.
        /// </summary>
        public static bool IsApng(Stream pngStream)
        {
            if (pngStream == null || !pngStream.CanRead)
                throw new ArgumentException("Invalid stream.");

            long savedPos = pngStream.Position;
            try
            {
                using var br = new BinaryReader(pngStream, Encoding.ASCII, leaveOpen: true);
                if (!BytesEqual(br.ReadBytes(8), PngSignature))
                    throw new Exception("Not a PNG file.");

                while (pngStream.Position < pngStream.Length)
                {
                    int    len  = ReadBE32(br);
                    string type = ReadChunkType(br);
                    if (type == "acTL") return true;
                    pngStream.Seek(len + 4, SeekOrigin.Current);
                    if (type == "IEND") break;
                }
                return false;
            }
            finally { pngStream.Seek(savedPos, SeekOrigin.Begin); }
        }

        /// <summary>
        /// Parses an APNG stream. Stores only the raw compressed frame bytes —
        /// no pixels are decoded. Returns null on error.
        /// Does not close the stream.
        /// </summary>
        public static ApngFile? ReadApng(Stream stream)
        {
            try
            {
                stream.Seek(0, SeekOrigin.Begin);
                using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

                if (!BytesEqual(br.ReadBytes(8), PngSignature))
                    throw new Exception("Not a PNG file.");

                byte[]?          ihdrData  = null;
                byte[]?          trnsData  = null;
                int              numPlays  = 0;
                var              frames    = new List<ApngRawFrame>();
                ApngRawFrame?    cur       = null;
                var              idatBufs  = new List<byte[]>();

                while (stream.Position < stream.Length)
                {
                    int    len  = ReadBE32(br);
                    string type = ReadChunkType(br);
                    byte[] data = br.ReadBytes(len);
                    br.ReadBytes(4); // CRC — skip

                    switch (type)
                    {
                        case "IHDR": ihdrData = data; break;
                        case "tRNS": trnsData = data; break;

                        case "acTL":
                            numPlays = ReadBE32(data, 4);
                            break;

                        case "fcTL":
                            if (cur != null && idatBufs.Count > 0)
                            {
                                cur.CompressedData = MergeByteArrays(idatBufs);
                                frames.Add(cur);
                                idatBufs = new List<byte[]>();
                            }
                            cur = ParseFctl(data);
                            break;

                        case "IDAT":
                            if (cur != null) idatBufs.Add(data);
                            break;

                        case "fdAT":
                            if (cur != null && data.Length > 4)
                            {
                                var payload = new byte[data.Length - 4];
                                Buffer.BlockCopy(data, 4, payload, 0, payload.Length);
                                idatBufs.Add(payload);
                            }
                            break;

                        case "IEND":
                            if (cur != null && idatBufs.Count > 0)
                            {
                                cur.CompressedData = MergeByteArrays(idatBufs);
                                frames.Add(cur);
                            }
                            goto doneReading;
                    }
                }
                doneReading:

                if (ihdrData == null || frames.Count == 0)
                    throw new Exception("No frames found.");

                int  canvasW   = ReadBE32(ihdrData, 0);
                int  canvasH   = ReadBE32(ihdrData, 4);
                byte colorType = ihdrData[9];

                (byte R, byte G, byte B)? transparentRgb = null;
                if (trnsData != null && colorType == 2 && trnsData.Length >= 6)
                    transparentRgb = (trnsData[1], trnsData[3], trnsData[5]);

                return new ApngFile
                {
                    CanvasWidth    = canvasW,
                    CanvasHeight   = canvasH,
                    NumPlays       = numPlays,
                    ColorType      = colorType,
                    TransparentRgb = transparentRgb,
                    Frames         = frames
                };
            }
            catch (Exception ex)
            {
                Log.Add(Log.LogSeverity.Error, "APNGHelper.ReadApng", ex);
                return null;
            }
        }

        // Frame decode

        private static byte[] DecodeFrame(
            ApngRawFrame raw,
            byte colorType,
            (byte R, byte G, byte B)? transparentRgb)
        {
            // Decompress zlib payload (skip 2-byte header: CMF + FLG)
            byte[] pixels;
            using (var ms  = new MemoryStream(raw.CompressedData, 2, raw.CompressedData.Length - 2))
            using (var ds  = new DeflateStream(ms, CompressionMode.Decompress))
            using (var buf = new MemoryStream())
            {
                ds.CopyTo(buf);
                pixels = buf.ToArray();
            }

            int w   = raw.Width;
            int h   = raw.Height;
            int bpp = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 3 };

            var rgba    = new byte[w * h * 4];
            var prevRow = new byte[w * bpp];
            int stride  = w * bpp + 1; // +1 for filter byte per row

            for (int y = 0; y < h; y++)
            {
                byte filter = pixels[y * stride];
                var  row    = new byte[w * bpp];
                Buffer.BlockCopy(pixels, y * stride + 1, row, 0, row.Length);
                ApplyPngFilter(filter, row, prevRow, bpp);

                for (int x = 0; x < w; x++)
                {
                    int  src = x * bpp;
                    int  dst = (y * w + x) * 4;
                    byte r, g, b, a;

                    switch (colorType)
                    {
                        case 2: // RGB
                            r = row[src]; g = row[src + 1]; b = row[src + 2];
                            a = transparentRgb.HasValue
                                && r == transparentRgb.Value.R
                                && g == transparentRgb.Value.G
                                && b == transparentRgb.Value.B
                                ? (byte)0 : (byte)255;
                            break;
                        case 6: // RGBA
                            r = row[src]; g = row[src+1]; b = row[src+2]; a = row[src+3];
                            break;
                        case 4: // Grayscale + alpha
                            r = g = b = row[src]; a = row[src + 1];
                            break;
                        default: // Grayscale
                            r = g = b = row[src]; a = 255;
                            break;
                    }

                    rgba[dst] = r; rgba[dst+1] = g; rgba[dst+2] = b; rgba[dst+3] = a;
                }

                Buffer.BlockCopy(row, 0, prevRow, 0, row.Length);
            }

            return rgba;
        }

        private static void ApplyPngFilter(byte filter, byte[] row, byte[] prev, int bpp)
        {
            switch (filter)
            {
                case 1: // Sub
                    for (int i = bpp; i < row.Length; i++)
                        row[i] = (byte)(row[i] + row[i - bpp]);
                    break;
                case 2: // Up
                    for (int i = 0; i < row.Length; i++)
                        row[i] = (byte)(row[i] + prev[i]);
                    break;
                case 3: // Average
                    for (int i = 0; i < row.Length; i++)
                    {
                        byte left = i >= bpp ? row[i - bpp] : (byte)0;
                        row[i] = (byte)(row[i] + ((left + prev[i]) >> 1));
                    }
                    break;
                case 4: // Paeth
                    for (int i = 0; i < row.Length; i++)
                    {
                        byte left   = i >= bpp ? row[i - bpp] : (byte)0;
                        byte upLeft = i >= bpp ? prev[i - bpp] : (byte)0;
                        row[i] = (byte)(row[i] + PaethPredictor(left, prev[i], upLeft));
                    }
                    break;
                // case 0 None: no-op
            }
        }

        private static byte PaethPredictor(byte a, byte b, byte c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        // Canvas ops

        private static void CompositeFrame(
            byte[] canvas, int canvasW, byte[] frameRgba, ApngRawFrame raw)
        {
            int fw = raw.Width, fh = raw.Height, ox = raw.X, oy = raw.Y;

            for (int y = 0; y < fh; y++)
            {
                int rowSrc = y * fw * 4;
                int rowDst = (oy + y) * canvasW * 4 + ox * 4;

                if (raw.BlendOp == BlendOpSource)
                {
                    // Fast path: direct replace, one copy per row
                    Buffer.BlockCopy(frameRgba, rowSrc, canvas, rowDst, fw * 4);
                }
                else
                {
                    // APNG_BLEND_OP_OVER: alpha composite per pixel
                    for (int x = 0; x < fw; x++)
                    {
                        int  fi = rowSrc + x * 4;
                        int  ci = rowDst + x * 4;
                        byte sA = frameRgba[fi + 3];

                        if (sA == 0)   continue; // fully transparent: skip
                        byte sR = frameRgba[fi], sG = frameRgba[fi+1], sB = frameRgba[fi+2];

                        if (sA == 255) // fully opaque: fast replace
                        {
                            canvas[ci] = sR; canvas[ci+1] = sG;
                            canvas[ci+2] = sB; canvas[ci+3] = 255;
                        }
                        else           // partial: blend
                        {
                            float sa  = sA / 255f;
                            float da  = canvas[ci+3] / 255f;
                            float oa  = sa + da * (1f - sa);
                            if (oa > 0f)
                            {
                                float dsa = da * (1f - sa);
                                float inv = 1f / oa;
                                canvas[ci]   = (byte)((sR * sa + canvas[ci]   * dsa) * inv);
                                canvas[ci+1] = (byte)((sG * sa + canvas[ci+1] * dsa) * inv);
                                canvas[ci+2] = (byte)((sB * sa + canvas[ci+2] * dsa) * inv);
                                canvas[ci+3] = (byte)(oa * 255f);
                            }
                        }
                    }
                }
            }
        }

        private static void ClearRegion(byte[] canvas, int canvasW, int x, int y, int w, int h)
        {
            for (int row = 0; row < h; row++)
                Array.Clear(canvas, ((y + row) * canvasW + x) * 4, w * 4);
        }

        private static WriteableBitmap CanvasToWriteableBitmap(byte[] canvas, int w, int h)
        {
            var bmp = new WriteableBitmap(
                new PixelSize(w, h),
                new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);
            using var fb = bmp.Lock();
            Marshal.Copy(canvas, 0, fb.Address, canvas.Length);
            return bmp;
        }

        // Chunk helpers

        private static ApngRawFrame ParseFctl(byte[] d) => new ApngRawFrame
        {
            // layout: seq(4) w(4) h(4) x(4) y(4) delayNum(2) delayDen(2) disposeOp(1) blendOp(1)
            Width     = ReadBE32(d,  4),
            Height    = ReadBE32(d,  8),
            X         = ReadBE32(d, 12),
            Y         = ReadBE32(d, 16),
            DelayNum  = ReadBE16(d, 20),
            DelayDen  = ReadBE16(d, 22),
            DisposeOp = d[24],
            BlendOp   = d[25]
        };

        private static byte[] MergeByteArrays(List<byte[]> blocks)
        {
            int total = 0;
            foreach (var b in blocks) total += b.Length;
            var merged = new byte[total];
            int offset = 0;
            foreach (var b in blocks)
            {
                Buffer.BlockCopy(b, 0, merged, offset, b.Length);
                offset += b.Length;
            }
            return merged;
        }

        private static string ReadChunkType(BinaryReader br) =>
            Encoding.ASCII.GetString(br.ReadBytes(4));

        private static int ReadBE32(BinaryReader br)
        {
            var b = br.ReadBytes(4);
            if (BitConverter.IsLittleEndian) Array.Reverse(b);
            return BitConverter.ToInt32(b, 0);
        }

        private static int ReadBE32(byte[] b, int offset = 0)
        {
            byte[] tmp = new byte[4];
            Buffer.BlockCopy(b, offset, tmp, 0, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(tmp);
            return BitConverter.ToInt32(tmp, 0);
        }

        private static int ReadBE16(byte[] b, int offset = 0)
        {
            byte[] tmp = new byte[2];
            Buffer.BlockCopy(b, offset, tmp, 0, 2);
            if (BitConverter.IsLittleEndian) Array.Reverse(tmp);
            return (int)BitConverter.ToUInt16(tmp, 0);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
