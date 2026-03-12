namespace Makana.Journaling.Replay;

public interface ISourceIterator
{
    object? Next();

    void Cancel();
}

public static class SourceIterationRunner
{
    public static void Drain(ISourceIterator iterator)
    {
        try
        {
            while (iterator.Next() is not null)
            {
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("BAIL", ex);
        }

        iterator.Cancel();
    }
}
