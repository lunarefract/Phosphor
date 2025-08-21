using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace PhosphorMP.Extensions
{
    public class MemoryMappedFileStream : Stream
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly long _length;
        private long _position;

        public MemoryMappedFileStream(string path)
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("File not found", path);

            _length = fileInfo.Length;
            _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, _length, MemoryMappedFileAccess.Read);
            _position = 0;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;
            if (_position + count > _length) count = (int)(_length - _position);

            _accessor.ReadArray(_position, buffer, offset, count);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentException("Invalid SeekOrigin.")
            };

            if (newPos < 0 || newPos > _length)
                throw new IOException("Seek out of range.");

            _position = newPos;
            return _position;
        }

        public override void Flush() { /* nothing to flush, read-only */ }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _accessor.Dispose();
                _mmf.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
