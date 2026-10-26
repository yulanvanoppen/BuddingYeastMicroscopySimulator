using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace YeastSim
{
    /// <summary>
    /// Streams a multi-page, uncompressed, 16-bit greyscale TIFF (one page per frame).
    /// </summary>
    /// <remarks>
    /// Pixel data of all pages is written back to back as frames arrive (constant memory); the
    /// directory of every page and an ImageJ description (<c>images=N frames=N</c>) are appended
    /// in <see cref="Dispose"/>. This is the layout ImageJ/Fiji expects for a time series, and any
    /// TIFF reader can follow the page chain. Python: <c>tifffile.imread(path)</c> gives an array
    /// of shape (frames, height, width).
    /// </remarks>
    public sealed class TiffStackWriter : IDisposable
    {
        private const long FirstDataOffset = 8;

        private readonly FileStream _stream;
        private readonly BinaryWriter _writer;
        private readonly int _width;
        private readonly int _height;
        private readonly List<long> _dataOffsets = new List<long>();
        private readonly byte[] _row;
        private bool _disposed;

        public int Pages => _dataOffsets.Count;

        public TiffStackWriter(string path, int width, int height)
        {
            _width = width;
            _height = height;
            _row = new byte[width * 2];
            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            _writer = new BinaryWriter(_stream);

            // Little-endian header; the first directory offset is filled in on Dispose.
            _writer.Write((byte)'I');
            _writer.Write((byte)'I');
            _writer.Write((ushort)42);
            _writer.Write(0u);
        }

        /// <summary>Appends one page. <paramref name="pixels"/> is row-major with row 0 at the top.</summary>
        public void AddPage(ushort[] pixels)
        {
            if (pixels.Length != _width * _height) throw new ArgumentException("Page size mismatch.", nameof(pixels));

            _stream.Seek(FirstDataOffset + (long)_dataOffsets.Count * _width * _height * 2, SeekOrigin.Begin);
            _dataOffsets.Add(_stream.Position);
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    ushort v = pixels[y * _width + x];
                    _row[2 * x] = (byte)v;
                    _row[2 * x + 1] = (byte)(v >> 8);
                }
                _writer.Write(_row);
            }
            _writer.Flush();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            int pages = _dataOffsets.Count;
            if (pages > 0)
            {
                _stream.Seek(FirstDataOffset + (long)pages * _width * _height * 2, SeekOrigin.Begin);

                byte[] description = Encoding.ASCII.GetBytes($"ImageJ=1.11a\nimages={pages}\nframes={pages}\n\0");
                long descriptionOffset = _stream.Position;
                _writer.Write(description);
                if ((_stream.Position & 1) != 0) _writer.Write((byte)0);   // directories start on a word boundary

                long previousNextField = 4;   // header field holding the first directory offset
                for (int page = 0; page < pages; page++)
                {
                    long ifdOffset = _stream.Position;
                    bool first = page == 0;
                    _writer.Write((ushort)(first ? 11 : 10));
                    WriteTag(254, 4, 1, 0);                                         // NewSubfileType
                    WriteTag(256, 4, 1, (uint)_width);                              // ImageWidth
                    WriteTag(257, 4, 1, (uint)_height);                             // ImageLength
                    WriteTag(258, 3, 1, 16);                                        // BitsPerSample
                    WriteTag(259, 3, 1, 1);                                         // Compression: none
                    WriteTag(262, 3, 1, 1);                                         // Photometric: BlackIsZero
                    if (first) WriteTag(270, 2, (uint)description.Length, (uint)descriptionOffset);   // ImageDescription
                    WriteTag(273, 4, 1, (uint)_dataOffsets[page]);                  // StripOffsets
                    WriteTag(277, 3, 1, 1);                                         // SamplesPerPixel
                    WriteTag(278, 4, 1, (uint)_height);                             // RowsPerStrip
                    WriteTag(279, 4, 1, (uint)(_width * _height * 2));              // StripByteCounts
                    long nextField = _stream.Position;
                    _writer.Write(0u);                                              // next directory (patched below)

                    long end = _stream.Position;
                    _stream.Seek(previousNextField, SeekOrigin.Begin);
                    _writer.Write((uint)ifdOffset);
                    _stream.Seek(end, SeekOrigin.Begin);
                    previousNextField = nextField;
                }
            }

            _writer.Flush();
            _writer.Dispose();
            _stream.Dispose();
        }

        private void WriteTag(ushort tag, ushort type, uint count, uint value)
        {
            _writer.Write(tag);
            _writer.Write(type);
            _writer.Write(count);
            if (type == 3 && count == 1)
            {
                _writer.Write((ushort)value);
                _writer.Write((ushort)0);
            }
            else _writer.Write(value);
        }
    }
}
