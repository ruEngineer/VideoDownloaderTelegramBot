using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DownloadBot.Services;

public class VideoDownloader
{
    private readonly string _outputDir;
    private readonly ILogger<VideoDownloader> _logger;

    public VideoDownloader(ILogger<VideoDownloader> logger)
    {
        _outputDir = "/app/videos";
        _logger = logger;
        Directory.CreateDirectory(_outputDir);
    }

    public async Task<string?> DownloadVideoAsync(string url, CancellationToken ct = default)
    {
        url = url.Trim();
        if (url.EndsWith("/")) url = url.TrimEnd('/');

        if (!IsValidVideoUrl(url))
        {
            _logger.LogWarning("Invalid URL: {Url}", url);
            return null;
        }


        var fileName = Guid.NewGuid().ToString("N")[..12] + ".mp4";
        var outputPath = Path.Combine(_outputDir, fileName);

        string format;
        if (url.Contains("pinterest.com", StringComparison.OrdinalIgnoreCase) || 
        url.Contains("pin.it", StringComparison.OrdinalIgnoreCase))
        {
            format = "bestvideo+bestaudio";
        }
        else
        {
            format = "best[filesize<40M]";
        }

        var arguments = $"--format \"{format}\" " +
                        $"--no-warnings --quiet --no-playlist " +
                        $"--output \"{outputPath}\" " +
                        $"\"{url}\"";

        _logger.LogInformation("Downloading: {Url}", url);

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await WaitForExitAsync(process, ct);

            _logger.LogInformation("yt-dlp завершён с кодом {ExitCode}", process.ExitCode);

            string errorOutput = await process.StandardError.ReadToEndAsync();
            string standardOutput = await process.StandardOutput.ReadToEndAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("yt-dlp STDERR: {Error}", errorOutput);
                _logger.LogError("yt-dlp STDOUT: {Output}", standardOutput);
                return null;
            }                

            if (process.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                _logger.LogError("Download failed for {Url}", url);
                if (File.Exists(outputPath)) File.Delete(outputPath);
                return null;
            }

            _logger.LogInformation("Downloaded: {Path}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during download");
            if (File.Exists(outputPath)) File.Delete(outputPath);
            return null;
        }
    }

    private static bool IsValidVideoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) 
        {
            return false;
        }
        var uri = Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
        if (uri == null) 
        {
            return false;
        }

        var allowed = new[] { "tiktok.com", "pinterest.com", "pin.it"};
        return allowed.Any(domain => uri.Host.Contains(domain, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();
        process.EnableRaisingEvents = true;
        process.Exited += (s, e) => tcs.TrySetResult(true);
        using (cancellationToken.Register(() => tcs.TrySetCanceled()))
        {
            if (!process.HasExited) await tcs.Task;
        }
    }
}