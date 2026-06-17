using Microsoft.EntityFrameworkCore;
using NoCTF.Application.BackgroundTasks;
using NoCTF.Infrastructure;
using NoCTF.PluginBase;
using NoCTF.Plugins.AWDP;
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
builder.Services.AddScoped<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddScoped<IAwdpPatchService, AwdpPatchService>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
await app.RunAsync();
