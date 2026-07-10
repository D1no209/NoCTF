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

if (!builder.Environment.IsDevelopment())
{
    var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
    if (SecretValueValidator.IsConnectionStringUnsafe(defaultConnection))
        throw new InvalidOperationException("ConnectionStrings:DefaultConnection contains a missing, weak, or placeholder value.");
}

builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddSingleton<IStorageProvider>(StorageProviderFactory.Create(builder.Configuration));
var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"];
if (!string.IsNullOrWhiteSpace(runnerBaseUrl))
{
    if (!builder.Environment.IsDevelopment())
        SecretValueValidator.RequireSafe("Runner:ApiKey", builder.Configuration["Runner:ApiKey"], 24);
    builder.Services.AddHttpClient<IRunnerClient, HttpRunnerClient>(client =>
    {
        client.BaseAddress = new Uri(runnerBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(
            builder.Configuration.GetValue("Runner:TimeoutSeconds", 900), 30, 3600));
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
