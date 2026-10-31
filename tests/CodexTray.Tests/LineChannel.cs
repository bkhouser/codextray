using System.Text;
using System.Threading.Channels;

namespace CodexTray.Tests;

internal sealed class LineChannel
{
    private readonly Channel<string> _channel = Channel.CreateUnbounded<string>();

    public TextReader Reader => new ChannelReaderAdapter(_channel.Reader);

    public TextWriter Writer => new ChannelWriterAdapter(_channel.Writer);

    public ValueTask<string> ReadAsync(CancellationToken cancellationToken = default) => _channel.Reader.ReadAsync(cancellationToken);

    public ValueTask WriteAsync(string line, CancellationToken cancellationToken = default) => _channel.Writer.WriteAsync(line, cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();

    private sealed class ChannelReaderAdapter(ChannelReader<string> reader) : TextReader
    {
        private string _current = string.Empty;
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            if (_position == _current.Length)
            {
                var line = await ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    return 0;
                }

                _current = line + "\n";
                _position = 0;
            }

            var count = Math.Min(buffer.Length, _current.Length - _position);
            _current.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }

    private sealed class ChannelWriterAdapter(ChannelWriter<string> writer) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override Task WriteLineAsync(string? value)
        {
            return writer.WriteAsync(value ?? string.Empty).AsTask();
        }

        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            return writer.WriteAsync(buffer.ToString(), cancellationToken).AsTask();
        }

        public override Task FlushAsync() => Task.CompletedTask;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
