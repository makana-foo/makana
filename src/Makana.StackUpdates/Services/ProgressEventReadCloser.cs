using Makana.StackUpdates.Pulumi;

namespace Makana.StackUpdates.Services;

public sealed class ProgressEventReadCloser(
    string progressType,
    string id,
    string message,
    long total,
    Stream inner,
    Action<EngineEvent> emitEvent) : Stream
{
    private readonly string _progressType = progressType;
    private readonly string _id = id;
    private readonly string _message = message;
    private readonly long _total = total;
    private readonly Stream _inner = inner;
    private readonly Action<EngineEvent> _emitEvent = emitEvent;
    private long _completed;

    public override bool CanRead => _inner.CanRead;

    public override bool CanSeek => _inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _inner.Read(buffer, offset, count);
        _completed += bytesRead;
        _emitEvent(new EngineEvent(
            Sequence: 0,
            Timestamp: 0,
            ProgressEvent: new ProgressEvent(
                Type: _progressType,
                Id: _id,
                Message: _message,
                Completed: _completed,
                Total: _total,
                Done: false)));
        return bytesRead;
    }

    public override void Flush()
        => _inner.Flush();

    public override long Seek(long offset, SeekOrigin origin)
        => _inner.Seek(offset, origin);

    public override void SetLength(long value)
        => _inner.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
