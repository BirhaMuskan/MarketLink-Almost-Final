using System.Diagnostics;
using System.Net.Http;

namespace MarketLink.Services
{
    public class LocalAiHostedService : BackgroundService
    {
        private readonly ILogger<LocalAiHostedService> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IConfiguration _configuration;
        private Process? _aiProcess;

        public LocalAiHostedService(
            ILogger<LocalAiHostedService> logger,
            IWebHostEnvironment environment,
            IConfiguration configuration)
        {
            _logger = logger;
            _environment = environment;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            var autoStart =
                _configuration.GetValue<bool?>(
                    "AiService:AutoStart")
                ?? true;

            if (!autoStart)
            {
                _logger.LogInformation(
                    "Local AI auto-start is disabled.");
                return;
            }

            if (!OperatingSystem.IsWindows())
            {
                _logger.LogInformation(
                    "Local AI auto-start is configured for Windows only. Start the AI service manually on this platform.");
                return;
            }

            var healthUrl =
                _configuration["AiService:HealthUrl"]
                ?? "http://127.0.0.1:8001/health";

            if (await IsHealthyAsync(
                    healthUrl,
                    stoppingToken))
            {
                _logger.LogInformation(
                    "MarketLink AI service is already running.");
                return;
            }

            var aiDirectory =
                Path.Combine(
                    _environment.ContentRootPath,
                    "ai-service");

            if (!Directory.Exists(aiDirectory))
            {
                _logger.LogWarning(
                    "AI service folder was not found: {AiDirectory}",
                    aiDirectory);
                return;
            }

            var virtualPython =
                Path.Combine(
                    aiDirectory,
                    ".venv",
                    "Scripts",
                    "python.exe");

            var startBat =
                Path.Combine(
                    aiDirectory,
                    "start-ai.bat");

            try
            {
                if (File.Exists(virtualPython))
                {
                    _logger.LogInformation(
                        "Starting MarketLink local AI service from existing virtual environment.");

                    _aiProcess = StartPythonDirectly(
                        virtualPython,
                        aiDirectory);
                }
                else if (File.Exists(startBat))
                {
                    _logger.LogInformation(
                        "AI virtual environment not found. Running first-time AI setup.");

                    _aiProcess = StartBatchFile(
                        startBat,
                        aiDirectory);
                }
                else
                {
                    _logger.LogWarning(
                        "Neither Python virtual environment nor start-ai.bat was found.");
                    return;
                }

                var started = await WaitUntilHealthyAsync(
                    healthUrl,
                    TimeSpan.FromSeconds(90),
                    stoppingToken);

                if (started)
                {
                    _logger.LogInformation(
                        "MarketLink local AI service is ready at {HealthUrl}",
                        healthUrl);
                }
                else
                {
                    _logger.LogWarning(
                        "The AI process was started, but the health endpoint did not become ready in time.");
                }

                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to automatically start the MarketLink local AI service.");
            }
        }

        public override Task StopAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                if (_aiProcess != null &&
                    !_aiProcess.HasExited)
                {
                    _logger.LogInformation(
                        "Stopping MarketLink local AI service.");

                    _aiProcess.Kill(
                        entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not stop the local AI process cleanly.");
            }
            finally
            {
                _aiProcess?.Dispose();
                _aiProcess = null;
            }

            return base.StopAsync(
                cancellationToken);
        }

        private static Process StartPythonDirectly(
            string pythonExe,
            string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments =
                        "-m uvicorn app:app --host 127.0.0.1 --port 8001",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                }
            };

            process.Start();
            return process;
        }

        private static Process StartBatchFile(
            string batchFile,
            string workingDirectory)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments =
                        $"/c \"{batchFile}\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                }
            };

            process.Start();
            return process;
        }

        private static async Task<bool> IsHealthyAsync(
            string healthUrl,
            CancellationToken cancellationToken)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(2)
                };

                using var response =
                    await client.GetAsync(
                        healthUrl,
                        cancellationToken);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> WaitUntilHealthyAsync(
            string healthUrl,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var deadline =
                DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < deadline &&
                   !cancellationToken.IsCancellationRequested)
            {
                if (await IsHealthyAsync(
                        healthUrl,
                        cancellationToken))
                {
                    return true;
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(2),
                    cancellationToken);
            }

            return false;
        }
    }
}
