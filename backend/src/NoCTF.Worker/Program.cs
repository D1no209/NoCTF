using NoCTF.Worker.Composition;

var builder = Host.CreateApplicationBuilder(args);
builder.AddNoCtfWorker();
await builder.Build().RunAsync();
