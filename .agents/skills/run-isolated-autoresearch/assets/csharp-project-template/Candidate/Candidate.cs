using Autoresearch.Contract;

namespace Autoresearch.Candidate;

public static class CandidateImplementation
{
    // This is the only file a research worker may edit after the contract is frozen.
    public static ValueTask<CandidateResult> ExecuteAsync(
        CandidateInput input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CandidateResult(input.Value));
    }
}
