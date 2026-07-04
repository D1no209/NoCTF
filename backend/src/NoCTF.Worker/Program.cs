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
builder.Services.AddHttpClient<IRunnerClient, HttpRunnerClient>(client =>
{
    var baseUrl = builder.Configuration["Runner:BaseUrl"] ?? "http://runner:8080";
    client.BaseAddress = new Uri(baseUrl);
});
builder.Services.AddScoped<IContainerManager, RunnerBackedContainerManager>();
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
