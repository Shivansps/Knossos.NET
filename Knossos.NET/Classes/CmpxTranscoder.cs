using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Knossos.NET.Classes
{
    /// <summary>
    /// Result of a transcode attempt.
    /// </summary>
    public enum CmpxTranscodeStatus
    {
        /// <summary>A KTX1/ETC2 texture was produced and written to the output stream.</summary>
        Transcoded = 0,
        /// <summary>The source DDS was uncompressed; nothing was written. Caller should leave the asset as-is.</summary>
        NotCompressed = 1,
        /// <summary>Input was not a valid/parseable DDS.</summary>
        ErrorInput = -1,
        /// <summary>Compressed, but the format is not one we handle (only BC1/2/3/7).</summary>
        ErrorUnhandledFormat = -2,
        /// <summary>Compressonator failed during conversion.</summary>
        ErrorConvert = -3,
        /// <summary>Native allocation failed.</summary>
        ErrorAlloc = -4,
    }

    /// <summary>
    /// Thin, self-contained wrapper around the native 'cmpx' library.
    /// Converts a DDS into a KTX1 container holding ETC2 data. All I/O is stream based;
    /// nothing touches the filesystem on the native side.
    ///
    /// Target mapping (forceRgba8 == false):
    ///   DXT1 (no alpha) -> ETC2_RGB
    ///   DXT1 (alpha)    -> ETC2_RGBA1   (1-bit punch-through)
    ///   DXT3/DXT5/BC7   -> ETC2_RGBA8   (EAC)
    ///   uncompressed    -> ETC2_RGBA8   (only when forceTranscodeUncompressed is set)
    /// forceRgba8 == true forces ETC2_RGBA8 for every source.
    /// forceResize == true downscales the result 50% (and regenerates the mip chain).
    ///
    /// Native library file name resolved by the runtime:
    ///   Windows: cmpx.dll      Android/Linux: libcmpx.so
    /// </summary>
    public static class CmpxTranscoder
    {
        private const string Lib = "cmpx";

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int cmpx_transcode_dds_to_ktx(
            byte[] dds, UIntPtr ddsLen,
            int forceRGBA8, int forceResize, int forceTranscodeUncompressed,
            out IntPtr outKtx, out UIntPtr outLen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void cmpx_free(IntPtr p);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr cmpx_version();

        /// <summary>Returns the native library version string, useful to confirm the .so/.dll loaded.</summary>
        public static string NativeVersion()
        {
            var p = cmpx_version();
            return p == IntPtr.Zero ? "" : (Marshal.PtrToStringAnsi(p) ?? "");
        }

        /// <summary>
        /// Read a DDS from <paramref name="input"/>, transcode, and (on success) write a KTX1 to
        /// <paramref name="output"/>. If the DDS is uncompressed and neither <paramref name="forceRgba8"/>
        /// nor <paramref name="forceTranscodeUncompressed"/> is set, returns
        /// <see cref="CmpxTranscodeStatus.NotCompressed"/> and writes nothing.
        /// </summary>
        /// <param name="input">Source stream positioned at the start of the DDS.</param>
        /// <param name="output">Destination stream for the KTX1 bytes (only written on success).</param>
        /// <param name="forceRgba8">If true, always target ETC2_RGBA8 (compressed AND uncompressed sources).</param>
        /// <param name="forceResize">If true, downscale output 50% (and regenerate the mip chain when the source had one).</param>
        /// <param name="forceTranscodeUncompressed">If true, uncompressed DDS are transcoded to ETC2_RGBA8 instead of being ignored.</param>
        public static CmpxTranscodeStatus Transcode(Stream input, Stream output,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (output == null) throw new ArgumentNullException(nameof(output));

            byte[] dds = ReadAll(input);
            return TranscodeBytes(dds, output, forceRgba8, forceResize, forceTranscodeUncompressed);
        }

        /// <summary>
        /// Same as <see cref="Transcode"/> but takes the DDS already in a byte array.
        /// </summary>
        public static CmpxTranscodeStatus TranscodeBytes(byte[] dds, Stream output,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false)
        {
            if (dds == null) throw new ArgumentNullException(nameof(dds));
            if (output == null) throw new ArgumentNullException(nameof(output));

            IntPtr outPtr = IntPtr.Zero;
            try
            {
                int rc = cmpx_transcode_dds_to_ktx(
                    dds, (UIntPtr)dds.Length,
                    forceRgba8 ? 1 : 0, forceResize ? 1 : 0, forceTranscodeUncompressed ? 1 : 0,
                    out outPtr, out UIntPtr outLen);

                if (rc != 0)
                    return (CmpxTranscodeStatus)rc;   // NotCompressed (1) or a negative error

                long len = checked((long)outLen.ToUInt64());
                if (outPtr == IntPtr.Zero || len <= 0)
                    return CmpxTranscodeStatus.ErrorAlloc;

                // Copy native KTX bytes into the managed output stream in chunks.
                const int chunk = 1 << 16;
                byte[] buf = new byte[(int)Math.Min(chunk, len)];
                long offset = 0;
                while (offset < len)
                {
                    int n = (int)Math.Min(buf.Length, len - offset);
                    Marshal.Copy(outPtr + (int)offset, buf, 0, n);
                    output.Write(buf, 0, n);
                    offset += n;
                }
                return CmpxTranscodeStatus.Transcoded;
            }
            finally
            {
                if (outPtr != IntPtr.Zero) cmpx_free(outPtr);
            }
        }

        /// <summary>
        /// Convenience overload returning the KTX bytes (or null if the source was left untouched).
        /// Throws <see cref="InvalidDataException"/> on a real error.
        /// </summary>
        public static byte[]? TranscodeToBytes(byte[] dds,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false)
        {
            using var ms = new MemoryStream();
            var status = TranscodeBytes(dds, ms, forceRgba8, forceResize, forceTranscodeUncompressed);
            return status switch
            {
                CmpxTranscodeStatus.Transcoded => ms.ToArray(),
                CmpxTranscodeStatus.NotCompressed => null,
                _ => throw new InvalidDataException($"DDS->KTX transcode failed: {status}")
            };
        }

        public static Task<CmpxTranscodeStatus> TranscodeAsync(
            Stream input, Stream output,
            bool forceRgba8 = false, bool forceResize = false, bool forceTranscodeUncompressed = false,
            CancellationToken ct = default)
            => Task.Run(() => Transcode(input, output, forceRgba8, forceResize, forceTranscodeUncompressed), ct);

        private static byte[] ReadAll(Stream s)
        {
            if (s is MemoryStream msIn && msIn.TryGetBuffer(out var seg) && seg.Offset == 0 && seg.Count == seg.Array!.Length)
                return seg.Array;            // zero-copy fast path
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
