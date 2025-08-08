namespace PhosphorMP.Extensions
{
    public class RestrictedFileStream : Stream
    {
        private readonly FileStream _baseStream;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public RestrictedFileStream(string path, FileMode mode, long start, long length)
        {
            _baseStream = new FileStream(path, mode, FileAccess.ReadWrite);
            _start = start;
            _length = length;

            if (_baseStream.Length < start + length)
                throw new ArgumentOutOfRangeException(nameof(length), "Region exceeds file bounds.");

            _baseStream.Seek(_start, SeekOrigin.Begin);
            _position = 0;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _length;
        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value > _length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
                _baseStream.Position = _start + value;
                // TODO: Fix
                
                /*
                 System.ObjectDisposedException: Cannot access a closed file.
                   at System.IO.FileStream.set_Position(Int64 value)
                   at PhosphorMP.Extensions.RestrictedFileStream.set_Position(Int64 value) in /home/memfrag/Documents/GitHub/Phosphor/PhosphorMP/Extensions/RestrictedFileStream.cs:line 35
                   at PhosphorMP.Parser.MidiTrack.ParseEventsBetweenTicks(Int64 startingTick, Int64 endingTick) in /home/memfrag/Documents/GitHub/Phosphor/PhosphorMP/Parser/MidiTrack.cs:line 35
                   at PhosphorMP.Parser.MidiFile.ParseEventsBetweenTicks(Int64 startingTick, Int64 endingTick) in /home/memfrag/Documents/GitHub/Phosphor/PhosphorMP/Parser/MidiFile.cs:line 90
                   at PhosphorMP.Logic.PlaybackLogic() in /home/memfrag/Documents/GitHub/Phosphor/PhosphorMP/Logic.cs:line 61
                   at PhosphorMP.Program.Main() in /home/memfrag/Documents/GitHub/Phosphor/PhosphorMP/Program.cs:line 30
                 */
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position + count > _length)
                count = (int)(_length - _position);

            _baseStream.Position = _start + _position;
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _position += bytesRead;
            return bytesRead;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_position + count > _length)
                throw new IOException("Write exceeds restricted region.");

            _baseStream.Position = _start + _position;
            _baseStream.Write(buffer, offset, count);
            _position += count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPos;
            switch (origin)
            {
                case SeekOrigin.Begin:
                    newPos = offset;
                    break;
                case SeekOrigin.Current:
                    newPos = _position + offset;
                    break;
                case SeekOrigin.End:
                    newPos = _length + offset;
                    break;
                default:
                    throw new ArgumentException("Invalid SeekOrigin.");
            }

            if (newPos < 0 || newPos > _length)
                throw new IOException("Seek out of range.");

            _position = newPos;
            _baseStream.Position = _start + _position;
            return _position;
        }

        public override void Flush() => _baseStream.Flush();

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Cannot set length on a restricted stream.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _baseStream.Dispose();
            base.Dispose(disposing);
        }
    }
}