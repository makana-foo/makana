namespace Makana.Journaling.Checkpointing;

public sealed class CheckpointSession
{
    private int _lastSequenceNumber;
    private string? _currentDeploymentJson;
    private readonly Dictionary<int, string> _requestSignatureBySequence = [];
    private readonly Dictionary<int, string> _deploymentBySequence = [];

    public CheckpointSession()
    {
    }

    public CheckpointSession(int lastSequenceNumber, string? currentDeploymentJson)
    {
        if (lastSequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lastSequenceNumber));
        }

        _lastSequenceNumber = lastSequenceNumber;
        _currentDeploymentJson = currentDeploymentJson;
    }

    public int LastSequenceNumber => _lastSequenceNumber;

    public string? CurrentDeploymentJson => _currentDeploymentJson;

    public CheckpointApplyResult ApplyVerbatim(VerbatimCheckpointRequest request)
    {
        var signature = $"verbatim:{request.Version}:{request.UntypedDeploymentJson}";
        if (TryHandleDuplicate(request.SequenceNumber, signature, out var duplicateResult))
        {
            return duplicateResult;
        }

        ValidateExpectedNextSequence(request.SequenceNumber);

        _currentDeploymentJson = request.UntypedDeploymentJson;
        _lastSequenceNumber = request.SequenceNumber;
        _requestSignatureBySequence[request.SequenceNumber] = signature;
        _deploymentBySequence[request.SequenceNumber] = request.UntypedDeploymentJson;

        return new CheckpointApplyResult(
            CheckpointApplyStatus.Applied,
            request.SequenceNumber,
            request.UntypedDeploymentJson);
    }

    public CheckpointApplyResult ApplyDelta(DeltaCheckpointRequest request)
    {
        var signature = $"delta:{request.Version}:{request.CheckpointHash}:{request.DeploymentDeltaJson}";
        if (TryHandleDuplicate(request.SequenceNumber, signature, out var duplicateResult))
        {
            return duplicateResult;
        }

        ValidateExpectedNextSequence(request.SequenceNumber);

        if (string.IsNullOrEmpty(_currentDeploymentJson))
        {
            throw new CheckpointDeltaBaselineMissingException();
        }

        if (!TextEditApplier.TryApplyTextEdits(_currentDeploymentJson, request.DeploymentDeltaJson, out var updatedDeployment))
        {
            throw new InvalidOperationException("invalid deployment delta");
        }

        var hash = TextEditApplier.ComputeSha256Hex(updatedDeployment);
        if (!string.Equals(hash, request.CheckpointHash, StringComparison.Ordinal))
        {
            throw new CheckpointHashMismatchException(request.CheckpointHash, hash);
        }

        _currentDeploymentJson = updatedDeployment;
        _lastSequenceNumber = request.SequenceNumber;
        _requestSignatureBySequence[request.SequenceNumber] = signature;
        _deploymentBySequence[request.SequenceNumber] = updatedDeployment;

        return new CheckpointApplyResult(
            CheckpointApplyStatus.Applied,
            request.SequenceNumber,
            updatedDeployment);
    }

    private bool TryHandleDuplicate(int sequenceNumber, string signature, out CheckpointApplyResult result)
    {
        result = default!;
        if (!_requestSignatureBySequence.TryGetValue(sequenceNumber, out var existingSignature))
        {
            return false;
        }

        if (!string.Equals(existingSignature, signature, StringComparison.Ordinal))
        {
            throw new CheckpointSequenceConflictException(sequenceNumber);
        }

        var deployment = _deploymentBySequence[sequenceNumber];
        result = new CheckpointApplyResult(CheckpointApplyStatus.Duplicate, sequenceNumber, deployment);
        return true;
    }

    private void ValidateExpectedNextSequence(int actualSequence)
    {
        var expected = _lastSequenceNumber + 1;
        if (actualSequence != expected)
        {
            throw new CheckpointSequenceOutOfOrderException(expected, actualSequence);
        }
    }
}
