using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Matarillo.IO
{
    /// <summary>
    /// Reads bytes according to a certain protocol.
    /// </summary>
    public class ProtocolReader : IDisposable
    {
        private const Int32 _DefaultBufferSize = 4096;
        private Stream _stream;
        private List<byte[]> _buffers;
        private int _headPos;
        private int _tailPos;
        private int _lengthRead;
        private readonly int _bufferSize;
        private readonly bool _leaveOpen;

        /// <summary>
        /// Initializes a new instance of the ProtocolReader class with a default buffer size of 4096 bytes.
        /// </summary>
        /// <param name="input">The input stream.</param>
        public ProtocolReader(Stream input)
            : this(input, _DefaultBufferSize, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ProtocolReader class with a default buffer size of 4096 bytes, and optionally leaves the stream open.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="leaveOpen">true to leave the stream open after the ProtocolReader object is disposed; otherwise, false.</param>
        public ProtocolReader(Stream input, bool leaveOpen)
            : this(input, _DefaultBufferSize, leaveOpen)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ProtocolReader class with the specified buffer size.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="bufferSize">The buffer size in bytes.</param>
        public ProtocolReader(Stream input, int bufferSize)
            : this(input, bufferSize, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ProtocolReader class with the specified buffer size, and optionally leaves the stream open.
        /// </summary>
        /// <param name="input">The input stream.</param>
        /// <param name="bufferSize">The buffer size in bytes.</param>
        /// <param name="leaveOpen">true to leave the stream open after the ProtocolReader object is disposed; otherwise, false.</param>
        public ProtocolReader(Stream input, int bufferSize, bool leaveOpen)
        {
            _stream = input ?? throw new ArgumentNullException(nameof(input));
            _bufferSize = (bufferSize > 0) ? bufferSize : throw new ArgumentOutOfRangeException(nameof(bufferSize));
            _leaveOpen = leaveOpen;
        }

        /// <summary>
        /// Reads bytes from the current stream into a byte array until the specified separator occurs,
        /// and advances the current position to the next of the separator.
        /// The byte array that is returned does not contain the separator.
        /// </summary>
        /// <param name="separator">The sequence of bytes which behaves as a separator.</param>
        /// <returns>
        /// A byte array containing data read from the underlying stream.
        /// This might be less than the number of bytes requested if the end of the stream is reached.
        /// </returns>
        public async Task<byte[]> ReadToSeparatorAsync(byte[] separator)
        {
            if (_stream == null) throw new ObjectDisposedException(nameof(BaseStream));
            if (separator == null) throw new ArgumentNullException(nameof(separator));
            if (separator.Length == 0) throw new ArgumentException("separator must not be empty.", nameof(separator));
            var separatorLength = separator.Length;
            var lengthPrepared = _lengthRead;
            var bufferIndex = 0;
            var bufferPos = _headPos;
            while (true)
            {
                while (lengthPrepared < separatorLength)
                {
                    lengthPrepared += await FillBufferAsync();
                }
                int count;
                if (FindSeparator(separator, bufferIndex, bufferPos, out count))
                {
                    return Slice(count, separatorLength);
                }
                lengthPrepared -= count;
                bufferIndex = _buffers.Count - 1;
                bufferPos = _tailPos - separatorLength + 1;
                if (_tailPos < separatorLength)
                {
                    bufferIndex -= 1;
                    bufferPos += _bufferSize;
                }
            }
        }

        private async Task<int> FillBufferAsync()
        {
            var lastBuffer = default(byte[]);
            if (_buffers.Count > 0 && _tailPos < _bufferSize)
            {
                lastBuffer = _buffers[_buffers.Count - 1];
            }
            else
            {
                lastBuffer = new byte[_bufferSize];
                _buffers.Add(lastBuffer);
                _tailPos = 0;
            }
            var length = await _stream.ReadAsync(lastBuffer, _tailPos, lastBuffer.Length - _tailPos);
            _lengthRead += length;
            _tailPos += length;
            return length;
        }

        private bool FindSeparator(byte[] separator, int bufferIndex, int bufferPos, out int count)
        {
            count = 0;
            var separatorLength = separator.Length;
            var bufferIndexMin = bufferIndex;
            var bufferIndexMax = (_tailPos < separatorLength) ? (_buffers.Count - 1) : (_buffers.Count);
            for (var i = bufferIndexMin; i < bufferIndexMax; i++)
            {
                var posMin = (i == bufferIndexMin) ? bufferPos : 0;
                var posMax = (i == bufferIndexMax - 1)
                    ? ((_tailPos < separatorLength) ? (_bufferSize + _tailPos - separatorLength + 1) : (_tailPos - separatorLength + 1))
                    : _bufferSize;
                for (var j = posMin; j < posMax; j++)
                {
                    if (IsMatched(separator, i, j))
                    {
                        return true;
                    }
                    count++;
                }
            }
            return false;
        }

        private bool IsMatched(byte[] separator, int bufferIndex, int bufferPos)
        {
            for (var i = 0; i < separator.Length; i++)
            {
                var b = (bufferPos + i < _bufferSize)
                    ? _buffers[bufferIndex][bufferPos + i]
                    : _buffers[bufferIndex + 1][bufferPos + i - _bufferSize];
                if (b != separator[i])
                {
                    return false;
                }
            }
            return true;
        }

        private byte[] Slice(int copyLength, int chopLength)
        {
            var dest = new byte[copyLength];
            var destPos = 0;
            var copying = true;
            var newBuffers = new List<byte[]>();
            var newHeadPos = 0;
            var newLengthRead = 0;
            foreach (var src in _buffers)
            {
                if (copying)
                {
                    var srcPos = (src == _buffers[0] ? _headPos : 0);
                    var srcLen = src.Length - srcPos;
                    var destLen = dest.Length - destPos;
                    var copyLen = (srcLen < destLen) ? srcLen : destLen;
                    Array.Copy(src, srcPos, dest, destPos, copyLen);
                    destPos += copyLen;
                    if (destPos == dest.Length)
                    {
                        copying = false;
                        newHeadPos = srcPos + copyLen + chopLength;
                        if (newHeadPos < src.Length)
                        {
                            newBuffers.Add(src);
                            newLengthRead = src.Length - newHeadPos;
                        }
                        else
                        {
                            newLengthRead = src.Length - newHeadPos;
                            newHeadPos -= src.Length;
                        }
                    }
                }
                else
                {
                    newBuffers.Add(src);
                    newLengthRead += (src == _buffers[_buffers.Count - 1]) ? _tailPos : src.Length;
                }
            }
            _buffers = newBuffers;
            _headPos = newHeadPos;
            _lengthRead = newLengthRead;
            return dest;
        }

        /// <summary>
        /// Reads the specified number of bytes from the current stream into a byte array
        /// and advances the current position by that number of bytes.
        /// </summary>
        /// <param name="count">The number of bytes to read. This value must be 0 or a non-negative number or an exception will occur.</param>
        /// <returns>
        /// A byte array containing data read from the underlying stream.
        /// This might be less than the number of bytes requested if the end of the stream is reached.
        /// </returns>
        public async Task<byte[]> ReadBytesAsync(int count)
        {
            if (_stream == null) throw new ObjectDisposedException(nameof(BaseStream));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            while (_lengthRead < count)
            {
                await FillBufferAsync();
            }
            return Slice(count, 0);
        }

        /// <summary>
        /// Exposes access to the underlying stream of the ProtocolReader.
        /// </summary>
        /// <value>
        /// The underlying stream associated with the ProtocolReader.
        /// </value>
        public virtual Stream BaseStream
        {
            get
            {
                return _stream;
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the ProtocolReader class and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stream copyOfStream = _stream;
                _stream = null;
                if (copyOfStream != null && !_leaveOpen)
                    copyOfStream.Dispose();
            }
            _stream = null;
            _buffers = null;
        }

        /// <summary>
        /// Releases all resources used by the current instance of the ProtocolReader class.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
