using System.Threading.Channels;

namespace Makana.Journaling.Transport;

public sealed record JournalWireEntry(
    long SequenceId,
    long OperationId = 0,
    bool ElideWrite = false,
    long? RemoveNew = null,
    long? DeleteNew = null,
    long? PendingReplacementNew = null);

public sealed class JournalHttpException(int statusCode, string message)
    : InvalidOperationException(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface IJournalTicker
{
    Task WaitForTickAsync(CancellationToken cancellationToken);

    void Stop();

    void Reset(TimeSpan period);
}

public sealed class PassiveJournalTicker : IJournalTicker
{
    public Task WaitForTickAsync(CancellationToken cancellationToken)
        => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

    public void Stop()
    {
    }

    public void Reset(TimeSpan period)
    {
    }
}

public sealed class ManualJournalTicker : IJournalTicker
{
    private readonly Channel<bool> ticks = Channel.CreateUnbounded<bool>();

    public Task WaitForTickAsync(CancellationToken cancellationToken)
        => ticks.Reader.ReadAsync(cancellationToken).AsTask();

    public void Tick()
    {
        ticks.Writer.TryWrite(true);
    }

    public void Stop()
    {
    }

    public void Reset(TimeSpan period)
    {
    }
}

public sealed record PendingJournalWrite(
    JournalWireEntry Entry,
    TaskCompletionSource<Exception?> Result);

public sealed class CloudJournaler
{
    private readonly object gate = new();
    private readonly Channel<PendingJournalWrite> entries;
    private readonly Task worker;
    private readonly Dictionary<long, Task> pendingElided = [];

    private bool closed;
    private int activeCalls;
    private TaskCompletionSource zeroActive = CompletedSource();

    public CloudJournaler(
        Func<IReadOnlyList<JournalWireEntry>, Task> sender,
        int maxBatchSize,
        TimeSpan period,
        IJournalTicker? ticker = null)
    {
        entries = Channel.CreateBounded<PendingJournalWrite>(new BoundedChannelOptions(maxBatchSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });

        worker = SendBatchesAsync(
            maxBatchSize,
            period,
            entries.Reader,
            sender,
            ticker ?? new PassiveJournalTicker(),
            CancellationToken.None);
    }

    public async Task AddJournalEntryAsync(JournalWireEntry entry)
    {
        foreach (var dependencyId in GetDependencies(entry))
        {
            Task? pendingDependency;
            lock (gate)
            {
                pendingElided.TryGetValue(dependencyId, out pendingDependency);
            }

            if (pendingDependency is null)
            {
                continue;
            }

            try
            {
                await pendingDependency.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"dependent elided entry (operation {dependencyId}) failed", ex);
            }
        }

        var result = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (gate)
        {
            if (closed)
            {
                throw new InvalidOperationException("journal is already closed");
            }

            activeCalls++;
            if (activeCalls == 1)
            {
                zeroActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            await entries.Writer.WriteAsync(new PendingJournalWrite(entry, result)).ConfigureAwait(false);

            if (entry.ElideWrite)
            {
                var promise = ObserveElidedResultAsync(result.Task);
                lock (gate)
                {
                    pendingElided[entry.OperationId] = promise;
                }

                return;
            }

            var error = await result.Task.ConfigureAwait(false);
            if (error is not null)
            {
                throw error;
            }
        }
        finally
        {
            lock (gate)
            {
                activeCalls--;
                if (activeCalls == 0)
                {
                    zeroActive.TrySetResult();
                }
            }
        }
    }

    public async Task CloseAsync()
    {
        Task waitForActive;
        lock (gate)
        {
            if (closed)
            {
                waitForActive = worker;
            }
            else
            {
                closed = true;
                waitForActive = activeCalls == 0 ? Task.CompletedTask : zeroActive.Task;
            }
        }

        await waitForActive.ConfigureAwait(false);
        entries.Writer.TryComplete();
        await worker.ConfigureAwait(false);
    }

    public static async Task SendBatchesAsync(
        int maxBatchSize,
        TimeSpan period,
        ChannelReader<PendingJournalWrite> entries,
        Func<IReadOnlyList<JournalWireEntry>, Task> sender,
        IJournalTicker ticker,
        CancellationToken cancellationToken)
    {
        var batch = new List<JournalWireEntry>(maxBatchSize);
        var results = new List<TaskCompletionSource<Exception?>>(maxBatchSize);

        while (true)
        {
            if (batch.Count == 0)
            {
                if (!await entries.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return;
                }

                while (entries.TryRead(out var pendingWrite))
                {
                    batch.Add(pendingWrite.Entry);
                    results.Add(pendingWrite.Result);

                    if (batch.Count == maxBatchSize)
                    {
                        ticker.Stop();
                        await FlushAsync(batch, results, sender, ticker, period).ConfigureAwait(false);
                    }
                }

                continue;
            }

            var readReady = entries.WaitToReadAsync(cancellationToken).AsTask();
            var tickReady = ticker.WaitForTickAsync(cancellationToken);
            var completed = await Task.WhenAny(readReady, tickReady).ConfigureAwait(false);

            if (completed == tickReady)
            {
                ticker.Stop();
                await FlushAsync(batch, results, sender, ticker, period).ConfigureAwait(false);
                continue;
            }

            if (!await readReady.ConfigureAwait(false))
            {
                await FlushAsync(batch, results, sender, ticker, period).ConfigureAwait(false);
                return;
            }

            while (entries.TryRead(out var pendingWrite))
            {
                batch.Add(pendingWrite.Entry);
                results.Add(pendingWrite.Result);

                if (batch.Count == maxBatchSize)
                {
                    ticker.Stop();
                    await FlushAsync(batch, results, sender, ticker, period).ConfigureAwait(false);
                }
            }
        }
    }

    public static async Task SendBatchAsync(
        IReadOnlyList<JournalWireEntry> batch,
        Func<IReadOnlyList<JournalWireEntry>, Task> sender)
    {
        try
        {
            await sender(batch).ConfigureAwait(false);
        }
        catch (JournalHttpException ex) when (ex.StatusCode == 413 && batch.Count > 1)
        {
            var midpoint = batch.Count / 2;
            await SendBatchAsync(batch.Take(midpoint).ToArray(), sender).ConfigureAwait(false);
            await SendBatchAsync(batch.Skip(midpoint).ToArray(), sender).ConfigureAwait(false);
        }
    }

    private static async Task FlushAsync(
        List<JournalWireEntry> batch,
        List<TaskCompletionSource<Exception?>> results,
        Func<IReadOnlyList<JournalWireEntry>, Task> sender,
        IJournalTicker ticker,
        TimeSpan period)
    {
        if (batch.Count == 0)
        {
            return;
        }

        var batchToSend = batch.ToArray();
        var resultsToComplete = results.ToArray();
        batch.Clear();
        results.Clear();

        Exception? error = null;
        try
        {
            await SendBatchAsync(batchToSend, sender).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        foreach (var result in resultsToComplete)
        {
            result.TrySetResult(error);
        }

        ticker.Reset(period);
    }

    private static async Task ObserveElidedResultAsync(Task<Exception?> resultTask)
    {
        var error = await resultTask.ConfigureAwait(false);
        if (error is not null)
        {
            throw error;
        }
    }

    private static IEnumerable<long> GetDependencies(JournalWireEntry entry)
    {
        if (entry.RemoveNew is > 0)
        {
            yield return entry.RemoveNew.Value;
        }

        if (entry.DeleteNew is > 0)
        {
            yield return entry.DeleteNew.Value;
        }

        if (entry.PendingReplacementNew is > 0)
        {
            yield return entry.PendingReplacementNew.Value;
        }
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.TrySetResult();
        return source;
    }
}
