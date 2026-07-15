using NoCTF.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.AddNoCtfWorkerServices();

var app = builder.Build();
await app.RunAsync();
