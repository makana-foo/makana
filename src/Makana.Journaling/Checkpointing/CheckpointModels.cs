namespace Makana.Journaling.Checkpointing;

public enum CheckpointApplyStatus
{
    Applied = 0,
    Duplicate = 1,
}

public sealed record CheckpointApplyResult(
    CheckpointApplyStatus Status,
    int StoredSequenceNumber,
    string DeploymentJson);

public sealed record VerbatimCheckpointRequest(
    int SequenceNumber,
    int Version,
    string UntypedDeploymentJson);

public sealed record DeltaCheckpointRequest(
    int SequenceNumber,
    int Version,
    string CheckpointHash,
    string DeploymentDeltaJson);

public sealed class CheckpointSequenceOutOfOrderException(int expected, int actual)
    : InvalidOperationException($"checkpoint sequence out of order: expected {expected}, got {actual}")
{
    public int Expected { get; } = expected;
    public int Actual { get; } = actual;
}

public sealed class CheckpointSequenceConflictException(int sequenceNumber)
    : InvalidOperationException($"checkpoint sequence {sequenceNumber} was already applied with different payload")
{
    public int SequenceNumber { get; } = sequenceNumber;
}

public sealed class CheckpointDeltaBaselineMissingException()
    : InvalidOperationException("no baseline checkpoint available for delta");

public sealed class CheckpointHashMismatchException(string expected, string actual)
    : InvalidOperationException($"checkpoint hash mismatch: expected {expected}, got {actual}")
{
    public string Expected { get; } = expected;
    public string Actual { get; } = actual;
}

