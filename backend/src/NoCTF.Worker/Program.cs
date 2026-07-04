using Microsoft.EntityFrameworkCore;
using NoCTF.Application;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Application.Security;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;
using NoCTF.Plugins.Penetration;
using NoCTF.Runner.Client;
using NoCTF.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddSingleton<IStorageProvider>(StorageProviderFactory.Create(builder.Configuration));
var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"];
if (!string.IsNullOrWhiteSpace(runnerBaseUrl))
{
    builder.Services.AddHttpClient<IRunnerClient, HttpRunnerClient>(client =>
    {
        client.BaseAddress = new Uri(runnerBaseUrl);
        var runnerApiKey = builder.Configuration["Runner:ApiKey"];
        if (!string.IsNullOrWhiteSpace(runnerApiKey))
            client.DefaultRequestHeaders.Add("X-Runner-Token", runnerApiKey);
    });
    builder.Services.AddScoped<IContainerManager, RunnerBackedContainerManager>();
}
else
{
    builder.Services.AddSingleton(
        _ => new NoCTF.Container.Docker.DockerProvider(builder.Configuration["Docker:Host"]));
    builder.Services.AddScoped<IContainerManager, NoCTF.Container.Docker.DockerManager>();
}
builder.Services.AddNoCtfApplicationCore();
builder.Services.AddScoped<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddScoped<IPatchArchiveValidator, PatchArchiveValidator>();
builder.Services.AddScoped<AwdpConfigResolver>();
builder.Services.AddScoped<AwdpStateService>();
builder.Services.AddScoped<IAwdpPatchService, AwdpPatchService>();
builder.Services.AddScoped<ICompetitionJobHandler, AwdpPatchValidationJobHandler>();
new PenetrationModule().ConfigureServices(builder.Services);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<ExpiredInstanceCleanupService>();

var app = builder.Build();
await app.RunAsync();
