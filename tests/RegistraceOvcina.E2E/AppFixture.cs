using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace RegistraceOvcina.E2E;

public sealed class AppFixture : IAsyncLifetime
{
    private readonly StringBuilder _capturedOutput = new();
    private IPlaywright? _playwright;
    private Process? _process;
    private PostgreSqlContainer? _postgresContainer;

    public string BaseUrl { get; private set; } = string.Empty;

    public string ConnectionString { get; private set; } = string.Empty;

    public IBrowser Browser { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        var repoRoot = FindRepoRoot();
        BaseUrl = $"http://127.0.0.1:{GetFreePort()}";
        var buildConfiguration = DetectBuildConfiguration();

        ConnectionString = Environment.GetEnvironmentVariable("OVCINA_E2E_CONNECTION_STRING") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            _postgresContainer = new PostgreSqlBuilder("postgres:16-alpine")
                .WithDatabase($"registrace_ovcina_e2e_{Guid.NewGuid():N}")
                .WithUsername("postgres")
                .WithPassword("postgres")
                .Build();

            await _postgresContainer.StartAsync();
            ConnectionString = _postgresContainer.GetConnectionString();
        }

        var startInfo = new ProcessStartInfo(
            "dotnet",
            $"run --no-build --configuration {buildConfiguration} --no-launch-profile --project src\\RegistraceOvcina.Web\\RegistraceOvcina.Web.csproj")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = ConnectionString;
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the application process for E2E tests.");

        _process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _capturedOutput.AppendLine(args.Data);
            }
        };

        _process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _capturedOutput.AppendLine(args.Data);
            }
        };

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitForAppAsync();

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        if (_postgresContainer is not null)
        {
            await _postgresContainer.DisposeAsync();
        }

        _playwright?.Dispose();
    }

    public string GetDiagnostics() => _capturedOutput.ToString();

    private async Task WaitForAppAsync()
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        var timeout = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < timeout)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException(
                    $"The test host exited before it became ready.{Environment.NewLine}{GetDiagnostics()}");
            }

            try
            {
                var response = await client.GetAsync($"{BaseUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Timed out waiting for the test host to start.{Environment.NewLine}{GetDiagnostics()}");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RegistraceOvcina.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root for E2E tests.");
    }

    private static string DetectBuildConfiguration()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current.Parent is not null)
        {
            if (string.Equals(current.Parent.Name, "bin", StringComparison.OrdinalIgnoreCase))
            {
                return current.Name;
            }

            current = current.Parent;
        }

        return "Debug";
    }
}
