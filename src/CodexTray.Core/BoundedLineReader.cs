using System.Text;

namespace CodexTray.Core;

/// <summary>Enforces a limit while reading, before an untrusted line can be allocated in full.</summary>
internal sealed class BoundedLineReader(TextReader reader, int maximumLineLength)
{
    private readonly char[] _buffer = new char[4096];
    private int _position;
    private int _length;
    private bool _skipLineFeed;

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder(Math.Min(maximumLineLength, _buffer.Length));
        while (true)
        {
            if (_position == _length)
            {
                _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                _position = 0;
                if (_length == 0)
                {
                    return line.Length == 0 ? null : line.ToString();
                }
            }

            var character = _buffer[_position++];
            if (_skipLineFeed)
            {
                _skipLineFeed = false;
                if (character == '\n')
                {
                    continue;
                }
            }

            if (character is '\r' or '\n')
            {
                _skipLineFeed = character == '\r';
                return line.ToString();
            }

            if (line.Length == maximumLineLength)
            {
                throw new InvalidDataException($"App Server output line exceeds {maximumLineLength} characters.");
            }

            line.Append(character);
        }
    }
}
