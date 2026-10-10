using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Application.Runtime.Access;
using NoCTF.Application.Runtime.Instances;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Application.LiveSolo.Resources;

public static class LiveSoloPreparationPolicy
{
    public static LiveSoloFailure? Failure(ScopedRuntimeResult result) => result.Failure switch
    {
        RuntimeMutationFailure.NotFound => LiveSoloFailure.NotFound,
        RuntimeMutationFailure.CapacityExceeded => LiveSoloFailure.ResourceCapacityExceeded,
        RuntimeMutationFailure.Conflict => LiveSoloFailure.Conflict,
        RuntimeMutationFailure.ConfigurationInvalid => LiveSoloFailure.InvalidConfiguration,
        RuntimeMutationFailure.Unsupported when result.IsolationStatus is null => LiveSoloFailure.UnsupportedEnvironment,
        _ when result.IsolationStatus == ExecutionIsolationStatus.NotConfigured => LiveSoloFailure.IsolationNotConfigured,
        _ when result.IsolationStatus == ExecutionIsolationStatus.Unavailable => LiveSoloFailure.DependencyUnavailable,
        _ when result.IsolationStatus == ExecutionIsolationStatus.Unsupported => LiveSoloFailure.UnsupportedEnvironment,
        _ when !result.IsolationAvailable => LiveSoloFailure.IsolationVerificationFailed,
        not null => LiveSoloFailure.DependencyUnavailable,
        _ => null
    };
    public static LiveSoloResourceFailure Persisted(LiveSoloFailure failure) => failure switch
    {
        LiveSoloFailure.NotFound => LiveSoloResourceFailure.NotFound,
        LiveSoloFailure.ResourceCapacityExceeded => LiveSoloResourceFailure.ResourceCapacityExceeded,
        LiveSoloFailure.UnsupportedEnvironment => LiveSoloResourceFailure.UnsupportedEnvironment,
        LiveSoloFailure.IsolationNotConfigured => LiveSoloResourceFailure.IsolationNotConfigured,
        LiveSoloFailure.IsolationVerificationFailed => LiveSoloResourceFailure.IsolationVerificationFailed,
        _ => LiveSoloResourceFailure.DependencyUnavailable
    };
}
