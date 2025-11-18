using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DownloadBot.Services;

public class TelegramBotHostedService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly VideoDownloader _downloader;
    private readonly ILogger<TelegramBotHostedService> _logger;
    private readonly IConfiguration _config;

    public TelegramBotHostedService(
        IConfiguration config,
        VideoDownloader downloader,
        ILogger<TelegramBotHostedService> logger)
    {
        var token = config["TELEGRAM_BOT_TOKEN"] ?? throw new InvalidOperationException("TELEGRAM_BOT_TOKEN is required");
        _botClient = new TelegramBotClient(token);
        _downloader = downloader;
        _logger = logger;
        _config = config;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await _botClient.GetMe(cancellationToken);
        _logger.LogInformation("Бот запущен: @{Username}", me.Username);

        _botClient.StartReceiving(
            HandleUpdateAsync,
            HandlePollingErrorAsync,
            receiverOptions: new()
            {
                AllowedUpdates = Array.Empty<UpdateType>()
            },
            cancellationToken
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message?.Text is not { } text) return;

        var chatId = update.Message.Chat.Id;
        _logger.LogInformation("Получено от {ChatId}: {Text}", chatId, text);

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await botClient.SendMessage(chatId, "Привет! Пришли ссылку на YouTube или TikTok, Pinterest.", cancellationToken: ct);
            return;
        }

        string? videoPath = await _downloader.DownloadVideoAsync(text, ct);

        if (videoPath is null)
        {
            await _botClient.SendMessage(
                chatId: chatId,
                text: "❌ Не удалось скачать видео. Проверь ссылку или попробуй короткое видео (<50 МБ).",
                cancellationToken: ct
            );
            return;
        }

        var fs = File.OpenRead(videoPath);

        if (videoPath != null)
        {
            try
            {
                var videoStream = new InputFileStream(fs, videoPath);

                await botClient.SendVideo(
                    chatId: chatId,
                    video: videoStream,
                    caption: "✅ Вот твоё видео!",
                    cancellationToken: ct
                );
                _logger.LogInformation("Видео отправлено в {ChatId}", chatId);
            }
            finally
            {
                File.Delete(videoPath);
                _logger.LogDebug("Файл удалён: {Path}", videoPath);
            }
        }
        else
        {
            await botClient.SendMessage(
                chatId: chatId,
                text: "❌ Не удалось скачать видео. Проверь ссылку или попробуй короткое видео (<3 мин, <50 МБ).",
                cancellationToken: ct
            );
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        var errorMessage = exception switch
        {
            ApiRequestException apiEx => $"Telegram API Error:\n[{apiEx.ErrorCode}]\n{apiEx.Message}",
            _ => exception.ToString()
        };

        _logger.LogError("Ошибка опроса: {Error}", errorMessage);
        return Task.CompletedTask;
    }
}