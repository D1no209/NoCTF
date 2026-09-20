using NoCTF.Bot.Composition;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNoCtfBot(builder.Configuration);
await builder.Build().RunAsync();
