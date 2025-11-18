using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DownloadBot.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<VideoDownloader>();
builder.Services.AddHostedService<TelegramBotHostedService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var host = builder.Build();
await host.RunAsync();