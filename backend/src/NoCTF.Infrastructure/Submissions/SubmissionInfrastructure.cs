using Microsoft.Extensions.DependencyInjection;
using NoCTF.Application.Submissions.Intake;
using NoCTF.Application.Submissions.Management;
using NoCTF.Application.Submissions.PatchUploads;
using NoCTF.Application.Submissions.Processing;
using NoCTF.Application.Submissions.Status;
using NoCTF.Application.Submissions.CheatIncidents;
using NoCTF.GameModes.Registration;
using NoCTF.GameModes.Submission;
using NoCTF.Infrastructure.Submissions.Administration;
using NoCTF.Infrastructure.Submissions.Intake;
using NoCTF.Infrastructure.Submissions.Management;
using NoCTF.Infrastructure.Submissions.PatchUploads;
using NoCTF.Infrastructure.Submissions.Processing;
using NoCTF.Infrastructure.Submissions.Status;
using NoCTF.Infrastructure.Submissions.CheatIncidents;

namespace NoCTF.Infrastructure.Submissions;

internal static class SubmissionInfrastructure
{
    internal static IServiceCollection AddNoCtfSubmissions(this IServiceCollection services)
    {
        services.AddScoped<SubmissionAttemptCriticalSection>();
        services.AddScoped<ISubmissionIntakeStore, SubmissionIntakeStore>();
        services.AddScoped<CreateManualAdjustment>();
        services.AddScoped<IPatchUploadStore, PatchUploadStore>();
        services.AddScoped<CreatePatchUpload>();
        services.AddScoped<IFixArchiveReader, FixArchiveReader>();
        services.AddScoped<ISubmissionStatusReader, SubmissionStatusReader>();
        services.AddScoped<IAdminSubmissionStatusReader, AdminSubmissionStatusReader>();
        services.AddScoped<ISubmissionManagementStore, SubmissionManagementStore>();
        services.AddScoped<ListSubmissions>();
        services.AddScoped<QueueSubmissionWork>();
        services.AddScoped<ICheatIncidentStore, CheatIncidentStore>();
        services.AddScoped<ListCheatIncidents>();
        services.AddScoped<AccessCheatIncident>();
        services.AddScoped<ResolveCheatIncident>();
        services.AddScoped<ISubmissionProcessor, SubmissionProcessor>();
        services.AddScoped<BloodRankCriticalSection>();
        services.AddScoped<IInternalResultStore, InternalResultStore>();
        services.AddScoped<RecordInternalResult>();
        services.AddSingleton<ISubmissionEvaluatorCatalog, GameModeSubmissionEvaluatorCatalog>();
        services.AddSingleton<ISubmissionAdmissionModePolicy, GameModeSubmissionAdmissionPolicy>();
        return services;
    }
}
