// InnoArchive.cs
//
// Managed wrapper around the native "innowrap" library (a parser-only build of
// innoextract). The native side ONLY parses the Inno Setup headers and returns
// per-file metadata; this class owns the installer stream, feeds bytes to the
// parser through callbacks, and performs ALL decompression with SharpCompress.
//
// Dependencies: SharpCompress (https://github.com/adamhathcock/sharpcompress).
//
// Native library name resolves to:  innowrap.dll (Windows) / libinnowrap.so
// (Linux/Android) / libinnowrap.dylib (macOS), via NativeLibrary search rules.
//
// Typical use:
//     using var archive = new InnoArchive(File.OpenRead(gogSetupExe));
//     foreach (var f in archive.Files) Console.WriteLine(f.Name);
//     var vp = archive.FindFile("root_fs2.vp");
//     using var outFs = File.Create(Path.Combine(dest, "root_fs2.vp"));
//     archive.ExtractTo(vp, outFs);

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace Knossos.NET.Classes.Inno
{
    public sealed class InnoExtractException : Exception
    {
        public InnoExtractException(string message) : base(message) { }
    }

    /// <summary>Compression of a chunk's payload (matches innoextract's enum).</summary>
    public enum InnoCompression { Stored = 0, Zlib = 1, BZip2 = 2, Lzma1 = 3, Lzma2 = 4, Unknown = 5 }

    /// <summary>Per-file pre-compression transform (matches innoextract's enum).</summary>
    public enum InnoFilter { None = 0, Instruction4108 = 1, Instruction5200 = 2, Instruction5309 = 3, Zlib = 4 }

    public enum InnoChecksumType { None = 0, Adler32 = 1, Crc32 = 2, Md5 = 3, Sha1 = 4, Sha256 = 5 }

    /// <summary>Metadata describing one file inside the installer.</summary>
    public sealed class InnoFile
    {
        /// <summary>Lowercased basename, e.g. "root_fs2.vp". Convenient for matching.</summary>
        public string Name { get; init; } = "";
        /// <summary>Full install destination path as stored, e.g. "{app}\\data\\root_fs2.vp".</summary>
        public string Path { get; init; } = "";
        /// <summary>Final (post-filter) uncompressed size in bytes.</summary>
        public ulong Size { get; init; }

        /// <summary>Whole-file checksum (over the concatenation of all parts' output).</summary>
        internal InnoChecksumType ChecksumType;
        internal byte[] Checksum = Array.Empty<byte>();

        /// <summary>
        /// One or more parts to extract and concatenate. Non-GOG files have exactly one
        /// part; GOG Galaxy files are split into many deflated parts.
        /// </summary>
        internal List<InnoPart> Parts = new();

        public int PartCount => Parts.Count;

        public override string ToString() => $"{Name} ({Size} bytes, {Parts.Count} part(s))";
    }

    // One part = one chunk in the .exe (embedded) or a .bin slice (external).
    internal sealed class InnoPart
    {
        public uint FirstSlice;
        public uint LastSlice;
        public ulong ChunkOffset;
        public ulong ChunkSize;
        public InnoCompression Compression;
        public int Encryption;
        public ulong FileOffset;   // offset within the decompressed chunk
        public ulong FileSize;     // pre-filter size within the decompressed chunk
        public InnoFilter Filter;
    }

    public sealed class InnoArchive : IDisposable
    {
        // ------------------------------------------------------------------ //
        //  Native interop
        // ------------------------------------------------------------------ //
        private const string Lib = "innowrap";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long ReadFn(IntPtr user, ulong offset, IntPtr buf, long len);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate long InflateFn(IntPtr user, int op, long handle, int method, IntPtr inPtr, long inLen, IntPtr outPtr, long outCap);

        // Field order MUST match struct innowrap_file in innowrap.cpp exactly.
        // Pointer-free + naturally aligned, so the layout is identical on 32- and 64-bit.
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeFile
        {
            public ulong size;
            public int part_count;
            public int checksum_type;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] checksum;
        }

        // Field order MUST match struct innowrap_part in innowrap.cpp exactly.
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePart
        {
            public ulong chunk_offset;
            public ulong chunk_size;
            public ulong file_offset;
            public ulong file_size;
            public uint first_slice;
            public uint last_slice;
            public int compression;
            public int encryption;
            public int filter;
            public int reserved;
        }

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr innowrap_version();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr innowrap_open(ReadFn readCb, InflateFn inflateCb, IntPtr user, ulong totalSize, out int err);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int innowrap_file_count(IntPtr ctx);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong innowrap_data_offset(IntPtr ctx);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint innowrap_slices_per_disk(IntPtr ctx);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr innowrap_base_filename(IntPtr ctx);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int innowrap_get_file(IntPtr ctx, int index, out NativeFile outFile);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int innowrap_get_part(IntPtr ctx, int fileIndex, int partIndex, out NativePart outPart);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr innowrap_file_name(IntPtr ctx, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr innowrap_file_path(IntPtr ctx, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern void innowrap_close(IntPtr ctx);

        // ------------------------------------------------------------------ //
        //  State
        // ------------------------------------------------------------------ //
        private readonly Stream _stream;
        private readonly bool _leaveOpen;
        private readonly object _io = new();
        private IntPtr _ctx;
        private ulong _dataOffset;

        // External-slice (.bin) support.
        private readonly string? _installerPath;   // needed to locate sibling .bin files
        private uint _slicesPerDisk = 1;
        private string _baseFilename = "";
        private SliceSource? _slices;

        // Keep delegates rooted so the GC doesn't collect them while native code holds pointers.
        private readonly ReadFn _readCb;
        private readonly InflateFn _inflateCb;

        // Reusable scratch for the read callback + live header decoders for the inflate callback.
        private byte[] _readScratch = new byte[1 << 16];
        private readonly Dictionary<long, Stream> _inflaters = new();
        private long _nextInflateHandle;

        private List<InnoFile> _files = new();
        public IReadOnlyList<InnoFile> Files => _files;
        public ulong DataOffset => _dataOffset;
        /// <summary>True when file data lives in external .bin slices rather than inside the .exe.</summary>
        public bool IsExternal => _dataOffset == 0;

        public static string Version => Marshal.PtrToStringUTF8(innowrap_version()) ?? "";

        /// <summary>
        /// Open an installer by path. Required for external (.exe + .bin) installers so the
        /// sibling .bin slices can be located; works for single-.exe installers too.
        /// </summary>
        public InnoArchive(string installerPath)
            : this(File.OpenRead(installerPath), leaveOpen: false, installerPath) { }

        /// <param name="stream">A seekable, readable stream over the installer .exe.</param>
        /// <param name="leaveOpen">If false, the stream is disposed with this object.</param>
        public InnoArchive(Stream stream, bool leaveOpen = false) : this(stream, leaveOpen, null) { }

        private InnoArchive(Stream stream, bool leaveOpen, string? installerPath)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek || !stream.CanRead) throw new ArgumentException("Stream must be seekable and readable.");
            _stream = stream;
            _leaveOpen = leaveOpen;
            _installerPath = installerPath;
            _readCb = ReadCallback;
            _inflateCb = InflateCallback;

            ulong total = (ulong)stream.Length;
            _ctx = innowrap_open(_readCb, _inflateCb, IntPtr.Zero, total, out int err);
            if (_ctx == IntPtr.Zero || err != 0)
                throw new InnoExtractException("innowrap_open failed (not an Inno Setup installer, or parse error).");

            _dataOffset = innowrap_data_offset(_ctx);
            _slicesPerDisk = Math.Max(1u, innowrap_slices_per_disk(_ctx));
            _baseFilename = Marshal.PtrToStringUTF8(innowrap_base_filename(_ctx)) ?? "";
            LoadFileList();
        }

        private void LoadFileList()
        {
            int n = innowrap_file_count(_ctx);
            var list = new List<InnoFile>(n);
            for (int i = 0; i < n; i++)
            {
                if (innowrap_get_file(_ctx, i, out NativeFile nf) != 0) continue;

                var parts = new List<InnoPart>(nf.part_count);
                for (int p = 0; p < nf.part_count; p++)
                {
                    if (innowrap_get_part(_ctx, i, p, out NativePart np) != 0) continue;
                    parts.Add(new InnoPart
                    {
                        FirstSlice = np.first_slice,
                        LastSlice = np.last_slice,
                        ChunkOffset = np.chunk_offset,
                        ChunkSize = np.chunk_size,
                        Compression = (InnoCompression)np.compression,
                        Encryption = np.encryption,
                        FileOffset = np.file_offset,
                        FileSize = np.file_size,
                        Filter = (InnoFilter)np.filter,
                    });
                }

                list.Add(new InnoFile
                {
                    Name = Marshal.PtrToStringUTF8(innowrap_file_name(_ctx, i)) ?? "",
                    Path = Marshal.PtrToStringUTF8(innowrap_file_path(_ctx, i)) ?? "",
                    Size = nf.size,
                    ChecksumType = (InnoChecksumType)nf.checksum_type,
                    Checksum = nf.checksum ?? Array.Empty<byte>(),
                    Parts = parts,
                });
            }
            _files = list;
        }

        /// <summary>Find a file by lowercased basename (e.g. "root_fs2.vp"). Null if absent.</summary>
        public InnoFile? FindFile(string nameLower)
        {
            foreach (var f in _files)
                if (string.Equals(f.Name, nameLower, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        // ------------------------------------------------------------------ //
        //  Native callbacks
        // ------------------------------------------------------------------ //
        private long ReadCallback(IntPtr user, ulong offset, IntPtr buf, long len)
        {
            try
            {
                if (len <= 0) return 0;
                lock (_io)
                {
                    if (_readScratch.Length < len) _readScratch = new byte[len];
                    _stream.Seek((long)offset, SeekOrigin.Begin);
                    int total = 0;
                    while (total < len)
                    {
                        int got = _stream.Read(_readScratch, total, (int)(len - total));
                        if (got <= 0) break;
                        total += got;
                    }
                    Marshal.Copy(_readScratch, 0, buf, total);
                    return total;
                }
            }
            catch { return -1; }
        }

        // Streaming header-block decompression.
        //   op 0 OPEN  : method 1 = zlib, 2 = inno LZMA1; `in` is the full block. Returns a handle.
        //   op 1 READ  : produce up to outCap bytes into outPtr from the decoder `handle`.
        //   op 2 CLOSE : dispose decoder `handle`.
        // Streaming (rather than decode-all) is required because inno LZMA1 streams carry no
        // end-of-stream marker; we decode only what the parser pulls, exactly like innoextract.
        private long InflateCallback(IntPtr user, int op, long handle, int method, IntPtr inPtr, long inLen, IntPtr outPtr, long outCap)
        {
            try
            {
                switch (op)
                {
                    case 0: // OPEN
                    {
                        var input = new byte[inLen];
                        if (inLen > 0) Marshal.Copy(inPtr, input, 0, (int)inLen);
                        Stream dec = method switch
                        {
                            1 => new ZlibStream(new MemoryStream(input), CompressionMode.Decompress),
                            2 => OpenInnoLzma1(input),
                            _ => throw new InnoExtractException($"unexpected header compression method {method}")
                        };
                        long h = _nextInflateHandle++;
                        _inflaters[h] = dec;
                        return h;
                    }

                    case 1: // READ
                    {
                        if (!_inflaters.TryGetValue(handle, out var dec)) return -1;
                        if (outCap <= 0) return 0;
                        if (_readScratch.Length < outCap) _readScratch = new byte[outCap];
                        int got;
                        try { got = dec.Read(_readScratch, 0, (int)outCap); }
                        catch { got = 0; } // tolerate a decode-end overrun: report EOF
                        if (got <= 0) return 0;
                        Marshal.Copy(_readScratch, 0, outPtr, got);
                        return got;
                    }

                    case 2: // CLOSE
                    {
                        if (_inflaters.TryGetValue(handle, out var dec))
                        {
                            _inflaters.Remove(handle);
                            dec.Dispose();
                        }
                        return 0;
                    }

                    default:
                        return -1;
                }
            }
            catch { return -1; }
        }

        // Header-block LZMA1: 5-byte properties header + raw LZMA1, decoded on demand
        // (outputSize = -1; we never force it to EOF, so the missing end marker is harmless).
        private static Stream OpenInnoLzma1(byte[] input)
        {
            if (input.Length < 5) throw new InnoExtractException("inno LZMA1 header too short.");
            var props = new byte[5];
            Array.Copy(input, 0, props, 0, 5);
            var ms = new MemoryStream(input, 5, input.Length - 5);
            return InnoSharpCompat.Lzma1(props, ms, input.Length - 5, -1);
        }

        // ------------------------------------------------------------------ //
        //  Extraction
        // ------------------------------------------------------------------ //
        private static readonly byte[] ChunkMagic = { 0x7a, 0x6c, 0x62, 0x1a }; // "zlb\x1a"

        /// <summary>
        /// Decompress one file out of the installer and write it to <paramref name="output"/>.
        /// For GOG Galaxy installers the file is reassembled from many deflated parts.
        /// All decompression happens here in managed code via SharpCompress.
        /// </summary>
        public void ExtractTo(InnoFile file, Stream output)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (file.Parts.Count == 0)
                throw new InnoExtractException($"{file.Name} has no data parts.");

            var hasher = new RunningChecksum(file.ChecksumType);

            foreach (InnoPart part in file.Parts)
            {
                if (part.Encryption != 0)
                    throw new InnoExtractException("Encrypted installers are not supported by this build.");
                ExtractPart(part, output, hasher);
            }

            // Verify the whole-file checksum over the concatenation of all parts.
            if (file.ChecksumType != InnoChecksumType.None)
            {
                byte[] actual = hasher.Final();
                byte[] expected = file.Checksum;
                for (int i = 0; i < actual.Length; i++)
                    if (actual[i] != expected[i])
                        throw new InnoExtractException($"Checksum mismatch for {file.Name} ({file.ChecksumType}).");
            }
        }

        // Extract a single part: open its chunk, decompress, skip to the part's offset,
        // read FileSize bytes, apply the part filter, stream the result to `output` while
        // feeding the running whole-file checksum. Large parts are NOT buffered whole.
        private void ExtractPart(InnoPart part, Stream output, RunningChecksum hasher)
        {
            long span = checked((long)part.ChunkSize + 4);   // 4-byte magic + payload
            Stream chunkStream = OpenChunkStream(part, span);
            try
            {
                Span<byte> magic = stackalloc byte[4];
                ReadExact(chunkStream, magic);
                if (!magic.SequenceEqual(ChunkMagic))
                    throw new InnoExtractException("Chunk magic mismatch (\"zlb\\x1a\" expected).");

                long needed = checked((long)(part.FileOffset + part.FileSize));
                Stream decompressed = part.Compression switch
                {
                    InnoCompression.Stored => chunkStream,
                    InnoCompression.Zlib   => new ZlibStream(chunkStream, CompressionMode.Decompress),
                    InnoCompression.BZip2  => InnoSharpCompat.BZip2(chunkStream),
                    InnoCompression.Lzma1  => MakeInnoLzma1Stream(chunkStream, needed),
                    InnoCompression.Lzma2  => MakeInnoLzma2Stream(chunkStream, needed),
                    _ => throw new InnoExtractException($"Unsupported chunk compression {part.Compression}.")
                };
                try
                {
                    SkipExact(decompressed, (long)part.FileOffset);

                    // Bound the decompressed chunk to this part's pre-filter payload.
                    using var payload = new BoundedStream(decompressed, (long)part.FileSize);

                    switch (part.Filter)
                    {
                        case InnoFilter.None:
                            PumpToOutput(payload, output, hasher);
                            break;

                        case InnoFilter.Zlib:
                            // GOG Galaxy parts are deflated; inflate then write. The
                            // whole-file checksum is over the INFLATED bytes.
                            using (var zs = new ZlibStream(payload, CompressionMode.Decompress))
                                PumpToOutput(zs, output, hasher);
                            break;

                        case InnoFilter.Instruction4108:
                            WriteFiltered(payload, output, hasher, b => InnoExeFilters.Decode4108(b));
                            break;
                        case InnoFilter.Instruction5200:
                            WriteFiltered(payload, output, hasher, b => InnoExeFilters.Decode5200(b, false));
                            break;
                        case InnoFilter.Instruction5309:
                            WriteFiltered(payload, output, hasher, b => InnoExeFilters.Decode5200(b, true));
                            break;

                        default:
                            throw new InnoExtractException($"Unsupported file filter {part.Filter}.");
                    }
                }
                finally
                {
                    if (!ReferenceEquals(decompressed, chunkStream)) decompressed.Dispose();
                }
            }
            finally
            {
                chunkStream.Dispose();
            }
        }

        // Copy a stream to output while updating the running checksum (no full buffering).
        private static void PumpToOutput(Stream src, Stream output, RunningChecksum hasher)
        {
            var buf = new byte[1 << 16];
            int got;
            while ((got = src.Read(buf, 0, buf.Length)) > 0)
            {
                hasher.Update(buf.AsSpan(0, got));
                output.Write(buf, 0, got);
            }
        }

        // Instruction (exe) filters need the whole part in memory; these parts are small.
        private static void WriteFiltered(Stream payload, Stream output, RunningChecksum hasher,
                                          Func<byte[], byte[]> decode)
        {
            byte[] raw = ReadAll(payload);
            byte[] decoded = decode(raw);
            hasher.Update(decoded);
            output.Write(decoded, 0, decoded.Length);
        }

        // Returns a forward-only stream at the chunk's first byte (the magic), bounded to
        // `span` bytes, reading from the .exe (embedded) or the .bin slices (external).
        private Stream OpenChunkStream(InnoPart part, long span)
        {
            if (!IsExternal)
            {
                long start = checked((long)(_dataOffset + part.ChunkOffset));
                return new WindowStream(_stream, start, span, _io);
            }

            if (_installerPath == null)
                throw new InnoExtractException(
                    "This installer stores its data in external .bin slices. Open it with " +
                    "new InnoArchive(installerExePath) so the .bin files can be located.");

            _slices ??= new SliceSource(
                Path.GetDirectoryName(Path.GetFullPath(_installerPath)) ?? ".",
                Path.GetFileNameWithoutExtension(_installerPath),
                _baseFilename,
                _slicesPerDisk);

            return new SliceStream(_slices, part.FirstSlice, (long)part.ChunkOffset, span);
        }

        // ------------------------------------------------------------------ //
        //  SharpCompress helpers
        // ------------------------------------------------------------------ //
        // Chunk LZMA1: identical 5-byte header, but we know exactly how many output
        // bytes we need (outputSize), so the decoder stops cleanly.
        private static Stream MakeInnoLzma1Stream(Stream input, long outputSize)
        {
            var props = new byte[5];
            ReadExact(input, props);
            return InnoSharpCompat.Lzma1(props, input, -1, outputSize);
        }

        // Chunk LZMA2: single dict-size property byte, then raw LZMA2.
        private static Stream MakeInnoLzma2Stream(Stream input, long outputSize)
        {
            int b = input.ReadByte();
            if (b < 0) throw new InnoExtractException("inno LZMA2 header missing.");
            var props = new byte[] { (byte)b };
            return InnoSharpCompat.Lzma2(props, input, -1, outputSize);
        }

        // ------------------------------------------------------------------ //
        //  Checksums (incremental, so multi-hundred-MB files aren't buffered)
        // ------------------------------------------------------------------ //
        private sealed class RunningChecksum
        {
            private readonly InnoChecksumType _type;
            private readonly IncrementalHash? _ih;
            private uint _crc = 0xFFFFFFFF;
            private uint _adlerA = 1, _adlerB = 0;

            public RunningChecksum(InnoChecksumType type)
            {
                _type = type;
                _ih = type switch
                {
                    InnoChecksumType.Md5    => IncrementalHash.CreateHash(HashAlgorithmName.MD5),
                    InnoChecksumType.Sha1   => IncrementalHash.CreateHash(HashAlgorithmName.SHA1),
                    InnoChecksumType.Sha256 => IncrementalHash.CreateHash(HashAlgorithmName.SHA256),
                    _ => null
                };
            }

            public void Update(ReadOnlySpan<byte> data)
            {
                switch (_type)
                {
                    case InnoChecksumType.Md5:
                    case InnoChecksumType.Sha1:
                    case InnoChecksumType.Sha256:
                        _ih!.AppendData(data);
                        break;
                    case InnoChecksumType.Crc32:
                        foreach (byte by in data) _crc = CrcTable[(_crc ^ by) & 0xFF] ^ (_crc >> 8);
                        break;
                    case InnoChecksumType.Adler32:
                        const uint MOD = 65521;
                        foreach (byte by in data) { _adlerA = (_adlerA + by) % MOD; _adlerB = (_adlerB + _adlerA) % MOD; }
                        break;
                }
            }

            public byte[] Final() => _type switch
            {
                InnoChecksumType.Md5 or InnoChecksumType.Sha1 or InnoChecksumType.Sha256 => _ih!.GetHashAndReset(),
                InnoChecksumType.Crc32   => Le32(_crc ^ 0xFFFFFFFF),
                InnoChecksumType.Adler32 => Le32((_adlerB << 16) | _adlerA),
                _ => Array.Empty<byte>()
            };
        }

        private static byte[] Le32(uint v) => new[] { (byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24) };

        private static readonly uint[] CrcTable = BuildCrcTable();
        private static uint[] BuildCrcTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        // ------------------------------------------------------------------ //
        //  Stream utilities
        // ------------------------------------------------------------------ //
        private static void ReadExact(Stream s, Span<byte> buf)
        {
            int off = 0;
            while (off < buf.Length)
            {
                int got = s.Read(buf.Slice(off));
                if (got <= 0) throw new EndOfStreamException();
                off += got;
            }
        }

        private static void SkipExact(Stream s, long count)
        {
            if (count <= 0) return;
            var buf = new byte[Math.Min(count, 1 << 16)];
            long left = count;
            while (left > 0)
            {
                int want = (int)Math.Min(left, buf.Length);
                int got = s.Read(buf, 0, want);
                if (got <= 0) throw new EndOfStreamException();
                left -= got;
            }
        }

        private static byte[] ReadCount(Stream s, long count)
        {
            var outBuf = new byte[count];
            long off = 0;
            while (off < count)
            {
                int got = s.Read(outBuf, (int)off, (int)Math.Min(count - off, int.MaxValue));
                if (got <= 0) throw new EndOfStreamException();
                off += got;
            }
            return outBuf;
        }

        private static byte[] ReadAll(Stream s)
        {
            using var ms = new MemoryStream();
            var buf = new byte[1 << 16];
            int got;
            while ((got = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, got);
            return ms.ToArray();
        }

        public void Dispose()
        {
            if (_ctx != IntPtr.Zero) { innowrap_close(_ctx); _ctx = IntPtr.Zero; }
            foreach (var d in _inflaters.Values) { try { d.Dispose(); } catch { } }
            _inflaters.Clear();
            _slices?.Dispose();
            if (!_leaveOpen) _stream.Dispose();
            GC.SuppressFinalize(this);
        }

        ~InnoArchive() { if (_ctx != IntPtr.Zero) innowrap_close(_ctx); }

        // ------------------------------------------------------------------ //
        //  External slice (.bin) reading
        // ------------------------------------------------------------------ //
        // Opens and caches .bin slice files and exposes their data regions. Each slice is
        // [8-byte magic "idska16/32\x1a"][4-byte LE size][data...]; `size` is the whole file.
        private sealed class SliceSource : IDisposable
        {
            private static readonly byte[][] Magics =
            {
                new byte[] { (byte)'i',(byte)'d',(byte)'s',(byte)'k',(byte)'a',(byte)'1',(byte)'6',0x1a },
                new byte[] { (byte)'i',(byte)'d',(byte)'s',(byte)'k',(byte)'a',(byte)'3',(byte)'2',0x1a },
            };
            public const int HeaderSize = 12; // 8 magic + 4 size

            private readonly string _dir, _base, _base2;
            private readonly uint _slicesPerDisk;
            private readonly Dictionary<long, (FileStream fs, long size)> _open = new();

            public SliceSource(string dir, string baseName, string baseName2, uint slicesPerDisk)
            { _dir = dir; _base = baseName; _base2 = baseName2 ?? ""; _slicesPerDisk = Math.Max(1u, slicesPerDisk); }

            // {base}-{slice+1}.bin, or {base}-{major}{letter}.bin when slicesPerDisk > 1.
            public static string SliceFileName(string baseName, long slice, uint slicesPerDisk)
            {
                if (slicesPerDisk == 1) return $"{baseName}-{slice + 1}.bin";
                long major = slice / slicesPerDisk + 1;
                char minor = (char)('a' + (int)(slice % slicesPerDisk));
                return $"{baseName}-{major}{minor}.bin";
            }

            public (FileStream fs, long size) Get(long slice)
            {
                if (_open.TryGetValue(slice, out var e)) return e;

                string path = Locate(slice) ?? throw new InnoExtractException(
                    $"Could not find slice {slice}: {SliceFileName(_base, slice, _slicesPerDisk)}" +
                    (string.IsNullOrEmpty(_base2) ? "" : $" or {SliceFileName(_base2, slice, _slicesPerDisk)}"));

                var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hdr = new byte[HeaderSize];
                int off = 0; while (off < HeaderSize) { int g = fs.Read(hdr, off, HeaderSize - off); if (g <= 0) break; off += g; }
                if (off < HeaderSize || !MagicOk(hdr))
                    throw new InnoExtractException($"Bad slice magic in {Path.GetFileName(path)}");
                long size = (uint)(hdr[8] | (hdr[9] << 8) | (hdr[10] << 16) | (hdr[11] << 24));
                if (size > fs.Length || size < HeaderSize)
                    throw new InnoExtractException($"Bad slice size in {Path.GetFileName(path)}");
                e = (fs, size);
                _open[slice] = e;
                return e;
            }

            private static bool MagicOk(byte[] hdr)
            {
                foreach (var m in Magics)
                {
                    bool ok = true;
                    for (int i = 0; i < 8; i++) if (hdr[i] != m[i]) { ok = false; break; }
                    if (ok) return true;
                }
                return false;
            }

            private string? Locate(long slice)
            {
                foreach (var bn in new[] { _base, _base2 })
                {
                    if (string.IsNullOrEmpty(bn)) continue;
                    string name = SliceFileName(bn, slice, _slicesPerDisk);
                    string p = Path.Combine(_dir, name);
                    if (File.Exists(p)) return p;
                    // case-insensitive fallback
                    if (Directory.Exists(_dir))
                        foreach (var f in Directory.EnumerateFiles(_dir))
                            if (string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
                                return f;
                }
                return null;
            }

            public void Dispose()
            {
                foreach (var e in _open.Values) { try { e.fs.Dispose(); } catch { } }
                _open.Clear();
            }
        }

        // Forward-only reader over a chunk that starts at an absolute offset in slice `firstSlice`
        // and may continue into subsequent slices (resuming after each slice's 12-byte header).
        private sealed class SliceStream : Stream
        {
            private readonly SliceSource _src;
            private long _slice;
            private long _filePos;      // absolute position within the current .bin
            private long _sliceSize;    // declared size of the current .bin
            private FileStream _fs;
            private long _left;         // bytes still to serve for this chunk

            public SliceStream(SliceSource src, long firstSlice, long chunkOffset, long length)
            {
                _src = src; _slice = firstSlice; _left = length;
                var (fs, size) = _src.Get(_slice);
                _fs = fs; _sliceSize = size; _filePos = chunkOffset;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_left <= 0 || count <= 0) return 0;

                long avail = _sliceSize - _filePos;
                if (avail <= 0)
                {
                    // Continue in the next slice, right after its 12-byte header.
                    _slice++;
                    var (fs, size) = _src.Get(_slice);
                    _fs = fs; _sliceSize = size; _filePos = SliceSource.HeaderSize;
                    avail = _sliceSize - _filePos;
                    if (avail <= 0) return 0;
                }

                int want = (int)Math.Min(Math.Min((long)count, avail), _left);
                _fs.Seek(_filePos, SeekOrigin.Begin);
                int got = _fs.Read(buffer, offset, want);
                if (got <= 0) return 0;
                _filePos += got; _left -= got;
                return got;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
            // The underlying FileStreams are owned by SliceSource; don't dispose them here.
        }

        // Test seam: build a cross-slice chunk reader directly (used by unit tests).
        internal static Stream OpenSliceStreamForTest(string dir, string baseName, string baseName2,
                                                      uint slicesPerDisk, long firstSlice, long chunkOffset, long length)
            => new SliceStream(new SliceSource(dir, baseName, baseName2, slicesPerDisk), firstSlice, chunkOffset, length);

        internal static string SliceFileNameForTest(string baseName, long slice, uint slicesPerDisk)
            => SliceSource.SliceFileName(baseName, slice, slicesPerDisk);

        // Test seam: an embedded (in-.exe) archive over `data`, bypassing native open.
        private InnoArchive(Stream data, ulong dataOffset)
        {
            _stream = data; _leaveOpen = true; _installerPath = null;
            _readCb = ReadCallback; _inflateCb = InflateCallback;
            _dataOffset = dataOffset; _files = new List<InnoFile>();
        }
        internal static InnoArchive ForEmbeddedTest(Stream data, ulong dataOffset) => new InnoArchive(data, dataOffset);
        internal void ExtractForTest(InnoFile f, Stream output) => ExtractTo(f, output);
        internal static InnoFile MakeFileForTest(ulong size, InnoChecksumType ct, byte[] cksum, List<InnoPart> parts)
            => new InnoFile { Size = size, ChecksumType = ct, Checksum = cksum, Parts = parts };
        internal static InnoPart MakePartForTest(ulong chunkOffset, ulong chunkSize, ulong fileSize, InnoCompression comp, InnoFilter filter)
            => new InnoPart { ChunkOffset = chunkOffset, ChunkSize = chunkSize, FileOffset = 0, FileSize = fileSize, Compression = comp, Filter = filter, FirstSlice = 0, LastSlice = 0 };

        // Reads at most `limit` bytes from an underlying (forward-only) stream, then EOF.
        // Does not dispose the underlying stream.
        private sealed class BoundedStream : Stream
        {
            private readonly Stream _base;
            private long _left;
            public BoundedStream(Stream baseStream, long limit) { _base = baseStream; _left = limit; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_left <= 0 || count <= 0) return 0;
                int want = (int)Math.Min(count, _left);
                int got = _base.Read(buffer, offset, want);
                if (got > 0) _left -= got;
                return got;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }

        /// <summary>A read-only view over [start, start+length) of a shared, seekable base stream.</summary>
        private sealed class WindowStream : Stream
        {
            private readonly Stream _base;
            private readonly long _start, _length;
            private readonly object _io;
            private long _pos;

            public WindowStream(Stream baseStream, long start, long length, object io)
            { _base = baseStream; _start = start; _length = length; _io = io; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= _length) return 0;
                int want = (int)Math.Min(count, _length - _pos);
                lock (_io)
                {
                    _base.Seek(_start + _pos, SeekOrigin.Begin);
                    int got = _base.Read(buffer, offset, want);
                    _pos += got;
                    return got;
                }
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _length;
            public override long Position { get => _pos; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }
}
