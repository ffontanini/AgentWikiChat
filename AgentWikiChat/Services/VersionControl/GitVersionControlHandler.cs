using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text;

namespace AgentWikiChat.Services.VersionControl;

/// <summary>
/// Implementación específica para Git.
/// SEGURIDAD: Solo permite operaciones de lectura.
/// NOTA: Esta es una implementación de referencia/plantilla para el futuro.
/// </summary>
public class GitVersionControlHandler : BaseVersionControlHandler
{
    private static bool? _gitInstalled = null;
    private static string? _gitVersion = null;

    // Comandos permitidos (solo lectura)
    private static readonly string[] AllowedCommands = new[]
    {
        "log", "show", "ls-tree", "blame", "diff", "status", "branch", "tag"
    };

    // Comandos prohibidos (escritura/modificación)
    private static readonly string[] ProhibitedCommands = new[]
    {
        "commit", "push", "pull", "fetch", "add", "rm", "remove",
        "checkout", "switch", "merge", "rebase", "cherry-pick",
        "reset", "revert", "stash", "tag -d", "branch -d",
        "init", "clone", "remote"
    };

    public override string ProviderName => "Git";

    public GitVersionControlHandler(IConfiguration configuration)
        : base(configuration)
    {
        // Verificar si Git está instalado (solo una vez)
        if (_gitInstalled == null)
        {
            _gitInstalled = IsClientInstalled();
            if (_gitInstalled == true)
            {
                _gitVersion = GetClientVersion();
                if (!string.IsNullOrEmpty(_gitVersion))
                {
                    LogDebug($"[Git] Cliente Git detectado - v{_gitVersion}");
                }
            }
            else
            {
                LogError($"[Git] ?? Cliente Git no encontrado en el sistema");
            }
        }

        LogDebug($"[Git] Inicializado - URL: {RepositoryUrl}, Timeout: {CommandTimeout}s");

        // Diagnóstico inicial
        if (_gitInstalled == true)
        {
            _ = TestConnectionAsync().Result; // Fire and forget
        }
    }

    public override bool IsClientInstalled()
    {
        try
        {
            var version = GetClientVersion();
            return !string.IsNullOrEmpty(version);
        }
        catch
        {
            return false;
        }
    }

    public override string GetClientVersion()
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null) return "";

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            // Output format: "git version 2.40.0"
            return output.Replace("git version ", "");
        }
        catch
        {
            return "";
        }
    }

    public override async Task<bool> TestConnectionAsync()
    {
        try
        {
            LogDebug($"[Git] Probando conexión con {RepositoryUrl}...");

            // Para Git, usamos ls-remote para probar conexión
            var args = new List<string> { "ls-remote", "--heads", RepositoryUrl };

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null) return false;

            await Task.Run(() => process.WaitForExit(10000));

            if (process.ExitCode == 0)
            {
                LogDebug($"[Git] ? Conexión exitosa con el repositorio");
                return true;
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                LogWarning($"[Git] ?? Problema al conectar: {error.Substring(0, Math.Min(200, error.Length))}");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogWarning($"[Git] ?? No se pudo verificar conexión: {ex.Message}");
            return false;
        }
    }

    public override async Task<string> ExecuteReadOnlyOperationAsync(string operation, Dictionary<string, string> parameters)
    {
        if (!IsOperationAllowed(operation))
        {
            throw new InvalidOperationException($"Operación '{operation}' no está permitida. Solo operaciones de lectura.");
        }

        var gitCommand = BuildGitCommand(operation, parameters);

        LogDebug($"[Git] Ejecutando: git {gitCommand}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = gitCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = !string.IsNullOrEmpty(WorkingCopyPath) && Directory.Exists(WorkingCopyPath)
                ? WorkingCopyPath
                : null
        };

        using var process = new Process { StartInfo = processStartInfo };
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = await Task.Run(() => process.WaitForExit(CommandTimeout * 1000));

        if (!completed)
        {
            process.Kill();
            throw new TimeoutException($"La operación Git excedió el timeout de {CommandTimeout} segundos.");
        }

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString().Trim();
            throw new InvalidOperationException($"Git retornó código de error {process.ExitCode}: {error}");
        }

        var output = outputBuilder.ToString().Trim();

        if (string.IsNullOrEmpty(output))
        {
            return "? La operación se completó exitosamente pero no retornó datos.";
        }

        return output;
    }

    public override bool IsOperationAllowed(string operation)
    {
        if (string.IsNullOrWhiteSpace(operation))
            return false;

        var normalizedOp = operation.Trim().ToLowerInvariant();

        if (ProhibitedCommands.Contains(normalizedOp))
            return false;

        return AllowedCommands.Contains(normalizedOp);
    }

    public override IEnumerable<string> GetAllowedOperations()
    {
        return AllowedCommands;
    }

    public override string GetInstallationInstructions()
    {
        var message = new StringBuilder();
        message.AppendLine("? **Cliente Git No Encontrado**\n");
        message.AppendLine("El sistema no puede encontrar el ejecutable `git`.\n");
        message.AppendLine("**?? Soluciones:**\n");
        message.AppendLine("**Windows:**");
        message.AppendLine("1. Instalar Git for Windows: https://git-scm.com/download/win");
        message.AppendLine("2. Durante la instalación, asegúrate de agregar Git al PATH");
        message.AppendLine("3. Reiniciar la aplicación\n");
        message.AppendLine("**Linux (Ubuntu/Debian):**");
        message.AppendLine("```bash");
        message.AppendLine("sudo apt-get update");
        message.AppendLine("sudo apt-get install git");
        message.AppendLine("```\n");
        message.AppendLine("**Linux (CentOS/RHEL):**");
        message.AppendLine("```bash");
        message.AppendLine("sudo yum install git");
        message.AppendLine("```\n");
        message.AppendLine("**macOS:**");
        message.AppendLine("```bash");
        message.AppendLine("brew install git");
        message.AppendLine("```");
        message.AppendLine("O instalar Xcode Command Line Tools:");
        message.AppendLine("```bash");
        message.AppendLine("xcode-select --install");
        message.AppendLine("```\n");
        message.AppendLine("**? Verificar instalación:**");
        message.AppendLine("```bash");
        message.AppendLine("git --version");
        message.AppendLine("```\n");
        message.AppendLine("?? Después de instalar, reinicia esta aplicación.");

        return message.ToString();
    }

    public override string GetErrorSuggestions(string errorMessage)
    {
        var suggestions = new StringBuilder();
        suggestions.AppendLine("?? **Posibles soluciones:**\n");

        if (errorMessage.Contains("fatal: could not read") || errorMessage.Contains("authentication failed"))
        {
            suggestions.AppendLine("**Problema de autenticación detectado:**");
            suggestions.AppendLine("1. ?? Verifica que la URL sea correcta: `" + RepositoryUrl + "`");
            suggestions.AppendLine("2. ?? Para repositorios privados, configura credenciales:");
            suggestions.AppendLine("   ```bash");
            suggestions.AppendLine("   git config --global credential.helper store");
            suggestions.AppendLine("   ```");
            suggestions.AppendLine("3. ?? Para GitHub/GitLab, usa Personal Access Token en lugar de password");
            suggestions.AppendLine("4. ?? Para SSH: verifica que tu clave SSH esté configurada");
            suggestions.AppendLine();
            suggestions.AppendLine("**Prueba manual:**");
            suggestions.AppendLine("```bash");
            suggestions.AppendLine($"git ls-remote {RepositoryUrl}");
            suggestions.AppendLine("```");
        }
        else if (errorMessage.Contains("fatal: not a git repository"))
        {
            suggestions.AppendLine("**No es un repositorio Git válido:**");
            suggestions.AppendLine("1. ?? Verifica que WorkingCopyPath apunte a un repositorio Git clonado");
            suggestions.AppendLine("2. ?? Si no tienes copia local, clona el repositorio:");
            suggestions.AppendLine("   ```bash");
            suggestions.AppendLine($"   git clone {RepositoryUrl} [ruta-local]");
            suggestions.AppendLine("   ```");
            suggestions.AppendLine("3. ?? Configura WorkingCopyPath en appsettings.json");
        }
        else if (errorMessage.Contains("fatal: unable to access"))
        {
            suggestions.AppendLine("**Problema de acceso/red:**");
            suggestions.AppendLine("1. ?? Verifica conectividad de red");
            suggestions.AppendLine("2. ?? Verifica firewall/proxy");
            suggestions.AppendLine("3. ?? Verifica que la URL del repositorio sea accesible");
            suggestions.AppendLine("4. ?? El servidor puede estar caído temporalmente");
        }

        suggestions.AppendLine("\n?? Si el problema persiste, verifica la configuración del repositorio.");

        return suggestions.ToString();
    }

    #region Private Helpers

    private string BuildGitCommand(string operation, Dictionary<string, string> parameters)
    {
        var args = new List<string>();

        parameters.TryGetValue("path", out var path);
        parameters.TryGetValue("revision", out var revision);
        parameters.TryGetValue("limit", out var limit);

        path ??= "";
        revision ??= "HEAD";
        limit ??= "10";

        switch (operation.ToLowerInvariant())
        {
            case "log":
                args.Add("log");
                if (int.TryParse(limit, out var logLimit))
                    args.Add($"-{logLimit}");
                args.Add("--oneline");
                args.Add("--graph");
                if (!string.IsNullOrEmpty(path))
                    args.Add($"-- {path}");
                break;

            case "show":
                args.Add("show");
                args.Add(revision);
                if (!string.IsNullOrEmpty(path))
                    args.Add(path);
                break;

            case "ls-tree":
                args.Add("ls-tree");
                args.Add("-r");
                args.Add("--name-only");
                args.Add(revision);
                if (!string.IsNullOrEmpty(path))
                    args.Add(path);
                break;

            case "blame":
                args.Add("blame");
                if (!string.IsNullOrEmpty(path))
                    args.Add(path);
                else
                    throw new ArgumentException("El comando 'blame' requiere un path");
                break;

            case "diff":
                args.Add("diff");
                if (revision.Contains(".."))
                    args.Add(revision);
                else
                    args.Add($"{revision}..HEAD");
                if (!string.IsNullOrEmpty(path))
                    args.Add($"-- {path}");
                break;

            case "status":
                args.Add("status");
                args.Add("--short");
                break;

            case "branch":
                args.Add("branch");
                args.Add("-a"); // all branches
                break;

            case "tag":
                args.Add("tag");
                args.Add("-l"); // list tags
                break;
        }

        return string.Join(" ", args);
    }

    #endregion
}
