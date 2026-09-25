namespace NoCTF.Application.Challenges.Bank;

public interface IExperimentalFeatureReader
{
    Task<bool> IsCtfPatchVerificationEnabledAsync(CancellationToken cancellationToken);
}
