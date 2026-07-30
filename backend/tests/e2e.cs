#:property TargetFramework=net10.0
#:property PublishAot=false
#:property PackAsTool=false

using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

return await E2EProgram.RunAsync(args);

internal static class E2EProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        E2EOptions options;
        try
        {
            options = E2EOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            E2EOptions.PrintUsage();
            return 2;
        }

        if (options.ShowHelp)
        {
            E2EOptions.PrintUsage();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var repositoryRoot = RepositoryLocator.Resolve();
        var runner = new ProcessRunner(repositoryRoot);
        try
        {
            await runner.RequireSuccessAsync("docker", ["version"], cancellation.Token);
            await runner.RequireSuccessAsync("docker", ["compose", "version"], cancellation.Token);
            await runner.RequireSuccessAsync("dotnet", ["--version"], cancellation.Token);

            foreach (var mode in options.Modes)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                await new ModeEnvironment(repositoryRoot, runner, mode, options)
                    .RunAsync(cancellation.Token);
            }

            Console.WriteLine("All requested NoCTF E2E suites passed.");
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            Console.Error.WriteLine("NoCTF E2E run was cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}

internal sealed class ModeEnvironment(
    string repositoryRoot,
    ProcessRunner runner,
    E2EMode mode,
    E2EOptions options)
{
    private readonly string modeName = mode.ToString().ToLowerInvariant();
    private readonly string suffix =
        $"{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{RandomToken(3)}"
            .ToLowerInvariant();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var projectName = $"noctf-{modeName}-e2e-{suffix}";
        var networkName = $"{projectName}-network";
        var runnerPool = $"{projectName}-runner";
        var composeFile = Path.Combine(
            repositoryRoot,
            "backend",
            "tests",
            "NoCTF.E2E",
            $"docker-compose.{modeName}.yml");
        var dockerSocketGroupId = await ResolveDockerSocketGroupIdAsync(cancellationToken);
        var environment = CreateEnvironment(
            projectName,
            networkName,
            runnerPool,
            dockerSocketGroupId);
        var compose = new ComposeRunner(runner, projectName, composeFile, environment);
        var succeeded = false;

        Console.WriteLine($"=== {mode} {options.Suite} E2E: {projectName} ===");
        try
        {
            await compose.RequireSuccessAsync(
                ["build", "--quiet", .. FixtureServices(mode), "migration", "backend", "worker", "runner"],
                cancellationToken);
            await compose.RequireSuccessAsync(
                [
                    "up",
                    "-d",
                    "postgres",
                    "redis",
                    "minio",
                    "minio-init",
                    "migration",
                    "backend",
                    "worker",
                    "runner"
                ],
                cancellationToken);

            var baseUrl = await ResolveBaseUrlAsync(compose, cancellationToken);
            environment["NOCTF_E2E_BASE_URL"] = baseUrl;
            await WaitUntilReadyAsync(compose, baseUrl, runnerPool, cancellationToken);

            var testArguments = new[]
            {
                "run",
                "--project",
                Path.Combine(
                    repositoryRoot,
                    "backend",
                    "tests",
                    "NoCTF.E2E",
                    "NoCTF.E2E.csproj"),
                "--",
                "--treenode-filter",
                $"/*/*/*/*[Category={mode}E2E]",
                "--minimum-expected-tests",
                "1"
            };
            await runner.RequireSuccessAsync(
                "dotnet",
                testArguments,
                cancellationToken,
                environment);
            if (options.Suite == E2ESuite.Full)
            {
                await RunFullResilienceAsync(
                    compose,
                    baseUrl,
                    runnerPool,
                    environment["NOCTF_E2E_ADMIN_PASSWORD"],
                    cancellationToken);
            }
            succeeded = true;
        }
        finally
        {
            if (!succeeded)
                await WriteDiagnosticsAsync(compose);

            if (options.KeepEnvironment)
            {
                Console.WriteLine($"Environment retained: project={projectName}");
                Console.WriteLine(
                    $"Cleanup: docker compose -p {projectName} -f \"{composeFile}\" down -v --remove-orphans --rmi local");
            }
            else
            {
                await CleanupAsync(compose, projectName);
            }
        }
    }

    private Dictionary<string, string> CreateEnvironment(
        string projectName,
        string networkName,
        string runnerPool,
        string dockerSocketGroupId)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NOCTF_E2E_NETWORK"] = networkName,
            ["NOCTF_E2E_DOCKER_GID"] = dockerSocketGroupId,
            ["NOCTF_E2E_RUNTIME_PROVIDER"] = "Docker",
            ["NOCTF_E2E_RUNNER_POOL"] = runnerPool,
            ["NOCTF_E2E_CALLBACK_CONTAINER"] =
                mode == E2EMode.Koh ? $"{projectName}-worker" : $"{projectName}-backend",
            ["NOCTF_E2E_POSTGRES_PASSWORD"] = $"postgres-{RandomToken(18)}",
            ["NOCTF_E2E_MINIO_USER"] = $"e2e{RandomToken(6)}",
            ["NOCTF_E2E_MINIO_PASSWORD"] = $"minio-{RandomToken(20)}",
            ["NOCTF_E2E_JWT_SECRET"] = $"jwt-{RandomToken(40)}",
            ["NOCTF_E2E_ADMIN_PASSWORD"] = $"admin-{RandomToken(20)}",
            ["NOCTF_E2E_SUITE"] = options.Suite.ToString().ToLowerInvariant(),
            ["NOCTF_E2E_BUILD_HTTP_PROXY"] = string.Empty,
            ["NOCTF_E2E_BUILD_HTTPS_PROXY"] = string.Empty,
            ["ALL_PROXY"] = string.Empty
        };
        var imagePrefix = projectName.Replace('_', '-');
        switch (mode)
        {
            case E2EMode.Ctf:
                environment["NOCTF_E2E_RUNTIME_IMAGE"] = $"{imagePrefix}-runtime:latest";
                break;
            case E2EMode.Awd:
                environment["NOCTF_E2E_RUNTIME_IMAGE"] = $"{imagePrefix}-runtime:latest";
                environment["NOCTF_E2E_CHECKER_IMAGE"] = $"{imagePrefix}-checker:latest";
                break;
            case E2EMode.Awdp:
                environment["NOCTF_E2E_TARGET_IMAGE"] = $"{imagePrefix}-target:latest";
                environment["NOCTF_E2E_CHECKER_IMAGE"] = $"{imagePrefix}-checker:latest";
                break;
            case E2EMode.Koh:
                environment["NOCTF_E2E_RUNTIME_IMAGE"] = $"{imagePrefix}-runtime:latest";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
        return environment;
    }

    private async Task<string> ResolveDockerSocketGroupIdAsync(
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return "0";

        var result = await runner.RunAsync(
            "stat",
            ["-c", "%g", "/var/run/docker.sock"],
            cancellationToken,
            displayOutput: false);
        result.EnsureSuccess("Resolving the Docker socket group id");
        var groupId = result.StandardOutput.Trim();
        return uint.TryParse(groupId, out _)
            ? groupId
            : throw new InvalidOperationException(
                $"Docker socket returned an invalid group id: '{groupId}'.");
    }

    private static string[] FixtureServices(E2EMode mode) => mode switch
    {
        E2EMode.Ctf => ["runtime-fixture"],
        E2EMode.Awd => ["runtime-fixture", "checker-fixture"],
        E2EMode.Awdp => ["target-fixture", "checker-fixture"],
        E2EMode.Koh => ["runtime-fixture"],
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    private static async Task<string> ResolveBaseUrlAsync(
        ComposeRunner compose,
        CancellationToken cancellationToken)
    {
        var result = await compose.RunAsync(
            ["port", "backend", "8080"],
            cancellationToken,
            displayOutput: false);
        result.EnsureSuccess("Resolving the Docker-assigned API port");
        var endpoint = result.StandardOutput.Trim();
        var separator = endpoint.LastIndexOf(':');
        if (separator < 0
            || !int.TryParse(endpoint[(separator + 1)..], out var port)
            || port is < IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        {
            throw new InvalidOperationException(
                $"Docker returned an invalid backend port mapping: '{endpoint}'.");
        }
        return $"http://127.0.0.1:{port}";
    }

    private static async Task WaitUntilReadyAsync(
        ComposeRunner compose,
        string baseUrl,
        string runnerPool,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        string lastHealth = "not requested";
        string lastHeartbeat = "not requested";
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(
                    $"{baseUrl}/health",
                    cancellationToken);
                lastHealth = ((int)response.StatusCode).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                lastHealth = exception.Message;
            }

            var heartbeat = await compose.RunAsync(
                [
                    "exec",
                    "-T",
                    "redis",
                    "redis-cli",
                    "EXISTS",
                    $"runner-pool:{runnerPool}:members"
                ],
                cancellationToken,
                displayOutput: false);
            lastHeartbeat = heartbeat.StandardOutput.Trim();
            if (lastHealth == "200"
                && heartbeat.ExitCode == 0
                && lastHeartbeat == "1")
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new TimeoutException(
            $"API/Runner readiness timed out. health={lastHealth}, heartbeat={lastHeartbeat}");
    }

    private async Task RunFullResilienceAsync(
        ComposeRunner compose,
        string baseUrl,
        string runnerPool,
        string adminPassword,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"=== {mode} full resilience checks ===");
        using var initialHttp = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        var token = await LoginAsync(
            initialHttp,
            $"{modeName}-e2e-admin",
            adminPassword,
            cancellationToken);

        await compose.RequireSuccessAsync(["restart", "backend"], cancellationToken);
        baseUrl = await ResolveBaseUrlAsync(compose, cancellationToken);
        using var http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        await WaitForAuthorizedMeAsync(http, token, cancellationToken);

        await compose.RequireSuccessAsync(["stop", "redis"], cancellationToken);
        _ = await LoginAsync(http, $"{modeName}-e2e-admin", adminPassword, cancellationToken);
        await compose.RequireSuccessAsync(["start", "redis"], cancellationToken);
        await WaitUntilReadyAsync(compose, baseUrl, runnerPool, cancellationToken);

        await compose.RequireSuccessAsync(["restart", "postgres"], cancellationToken);
        await WaitUntilReadyAsync(compose, baseUrl, runnerPool, cancellationToken);
        _ = await LoginAsync(http, $"{modeName}-e2e-admin", adminPassword, cancellationToken);
    }

    private static async Task<string> LoginAsync(
        HttpClient http,
        string login,
        string password,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        string last = "not requested";
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await http.PostAsJsonAsync(
                    "/api/v1/auth/login",
                    new { login, password },
                    cancellationToken);
                last = $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}";
                if (response.IsSuccessStatusCode)
                {
                    using var document = System.Text.Json.JsonDocument.Parse(last[(last.IndexOf(' ') + 1)..]);
                    return document.RootElement.GetProperty("accessToken").GetString()
                        ?? throw new InvalidOperationException("Login response omitted accessToken.");
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                last = exception.Message;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new TimeoutException($"Admin login did not recover: {last}");
    }

    private static async Task WaitForAuthorizedMeAsync(
        HttpClient http,
        string token,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(1);
        string last = "not requested";
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using var response = await http.SendAsync(request, cancellationToken);
                last = ((int)response.StatusCode).ToString(
                    System.Globalization.CultureInfo.InvariantCulture);
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException)
            {
                last = exception.Message;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        throw new TimeoutException($"JWT did not remain valid after API restart: {last}");
    }

    private static async Task WriteDiagnosticsAsync(ComposeRunner compose)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await compose.RunAsync(["ps", "-a"], timeout.Token);
            await compose.RunAsync(
                ["logs", "--no-color", "--tail", "300", "backend", "worker", "runner"],
                timeout.Token);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Writing E2E diagnostics failed: {exception.Message}");
        }
    }

    private async Task CleanupAsync(ComposeRunner compose, string projectName)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            var resources = await runner.RunAsync(
                "docker",
                [
                    "ps",
                    "-aq",
                    "--filter",
                    $"label=com.docker.compose.project={projectName}"
                ],
                timeout.Token,
                displayOutput: false);
            var ids = resources.StandardOutput
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length > 0)
                await runner.RunAsync("docker", ["rm", "-f", .. ids], timeout.Token);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Removing attached E2E containers failed: {exception.Message}");
        }

        try
        {
            await compose.RunAsync(
                ["down", "-v", "--remove-orphans", "--rmi", "local"],
                timeout.Token);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Docker Compose cleanup failed: {exception.Message}");
        }
    }

    private static string RandomToken(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}

internal sealed class ComposeRunner(
    ProcessRunner runner,
    string projectName,
    string composeFile,
    IReadOnlyDictionary<string, string> environment)
{
    public Task RequireSuccessAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        runner.RequireSuccessAsync(
            "docker",
            ["compose", "-p", projectName, "-f", composeFile, .. arguments],
            cancellationToken,
            environment);

    public Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool displayOutput = true) =>
        runner.RunAsync(
            "docker",
            ["compose", "-p", projectName, "-f", composeFile, .. arguments],
            cancellationToken,
            displayOutput,
            environment);
}

internal sealed class ProcessRunner(string workingDirectory)
{
    public async Task RequireSuccessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await RunAsync(
            fileName,
            arguments,
            cancellationToken,
            environment: environment);
        result.EnsureSuccess($"{fileName} {string.Join(' ', arguments)}");
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool displayOutput = true,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (var variable in environment ?? new Dictionary<string, string>())
            startInfo.Environment[variable.Key] = variable.Value;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException($"Could not start '{fileName}'.");

        var standardOutput = new List<string>();
        var standardError = new List<string>();
        var outputTask = PumpAsync(
            process.StandardOutput,
            standardOutput,
            displayOutput ? Console.Out : null);
        var errorTask = PumpAsync(
            process.StandardError,
            standardError,
            displayOutput ? Console.Error : null);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        await Task.WhenAll(outputTask, errorTask);
        return new(
            process.ExitCode,
            string.Join(Environment.NewLine, standardOutput),
            string.Join(Environment.NewLine, standardError));
    }

    private static async Task PumpAsync(
        StreamReader reader,
        ICollection<string> lines,
        TextWriter? output)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lines.Add(line);
            if (output is not null)
                await output.WriteLineAsync(line);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public void EnsureSuccess(string operation)
    {
        if (ExitCode != 0)
            throw new InvalidOperationException(
                $"{operation} failed with exit code {ExitCode}.{Environment.NewLine}{StandardError}");
    }
}

internal sealed record E2EOptions(
    IReadOnlyList<E2EMode> Modes,
    E2ESuite Suite,
    bool KeepEnvironment,
    bool ShowHelp)
{
    public static E2EOptions Parse(string[] args)
    {
        var modes = new List<E2EMode>();
        var suite = E2ESuite.Full;
        var keepEnvironment = false;
        var showHelp = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--mode":
                    var mode = NextValue(args, ref index, "--mode");
                    if (mode.Equals("all", StringComparison.OrdinalIgnoreCase))
                    {
                        modes.Clear();
                        modes.AddRange(Enum.GetValues<E2EMode>());
                    }
                    else if (Enum.TryParse<E2EMode>(mode, true, out var parsedMode))
                    {
                        modes.Add(parsedMode);
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Unsupported mode '{mode}'. Expected all, ctf, awd, awdp, or koh.");
                    }
                    break;
                case "--suite":
                    var suiteText = NextValue(args, ref index, "--suite");
                    if (!Enum.TryParse<E2ESuite>(suiteText, true, out suite))
                        throw new ArgumentException(
                            $"Unsupported suite '{suiteText}'. Expected smoke or full.");
                    break;
                case "--keep-environment":
                    keepEnvironment = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[index]}'.");
            }
        }
        if (modes.Count == 0)
            modes.AddRange(Enum.GetValues<E2EMode>());
        return new(modes.Distinct().ToArray(), suite, keepEnvironment, showHelp);
    }

    public static void PrintUsage()
    {
        Console.WriteLine(
            """
            NoCTF local Docker E2E runner

              dotnet run --file backend/tests/e2e.cs
              dotnet run --file backend/tests/e2e.cs -- --mode ctf --suite smoke

            Options:
              --mode all|ctf|awd|awdp|koh   Mode to run. Repeatable. Default: all.
              --suite smoke|full            Scenario suite. Default: full.
              --keep-environment            Do not remove the Compose environment.
              --help                        Show this help.
            """);
    }

    private static string NextValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }
}

internal static class RepositoryLocator
{
    public static string Resolve([CallerFilePath] string sourcePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("Could not locate the E2E source file.");
        var backendDirectory = Directory.GetParent(testsDirectory)?.FullName
            ?? throw new InvalidOperationException("Could not locate the backend directory.");
        var repositoryRoot = Directory.GetParent(backendDirectory)?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
        if (!File.Exists(Path.Combine(backendDirectory, "NoCTF.slnx")))
            throw new InvalidOperationException(
                $"Resolved repository root '{repositoryRoot}' does not contain backend/NoCTF.slnx.");
        return repositoryRoot;
    }
}

internal enum E2EMode
{
    Ctf,
    Awd,
    Awdp,
    Koh
}

internal enum E2ESuite
{
    Smoke,
    Full
}
