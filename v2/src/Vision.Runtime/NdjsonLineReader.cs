using System.Text;
using System.Runtime.InteropServices;

namespace VisionSystem.Runtime;

internal sealed class NdjsonLineReader(Stream stream, int maximumLineBytes)
{
    private readonly byte[] _readBuffer = new byte[4096];
    private readonly List<byte> _pending = new(4096);

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var newlineIndex = _pending.IndexOf((byte)'\n');
            if (newlineIndex >= 0)
            {
                if (newlineIndex > maximumLineBytes)
                {
                    throw new InvalidDataException($"Protocol line exceeds {maximumLineBytes} bytes.");
                }

                return TakeLine(newlineIndex);
            }

            if (_pending.Count > maximumLineBytes)
            {
                throw new InvalidDataException($"Protocol line exceeds {maximumLineBytes} bytes.");
            }

            var read = await stream.ReadAsync(_readBuffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_pending.Count == 0)
                {
                    return null;
                }

                return TakeLine(_pending.Count);
            }

            for (var index = 0; index < read; index++)
            {
                _pending.Add(_readBuffer[index]);
            }
        }
    }

    private string TakeLine(int length)
    {
        var actualLength = length > 0 && _pending[length - 1] == (byte)'\r' ? length - 1 : length;
        var line = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(_pending)[..actualLength]);
        var removeCount = length < _pending.Count && _pending[length] == (byte)'\n' ? length + 1 : length;
        _pending.RemoveRange(0, removeCount);
        return line;
    }
}
