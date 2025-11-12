using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Text;

namespace AgentWikiChat.Services.VersionControl;

/// <summary>
/// Implementación específica para Subversion (SVN).
/// SEGURIDAD: Solo permite operaciones de lectura.
/// </summary>
public class SvnVersionControlHandler : BaseVersionControlHandler
{
    private static bool? _svnInstalled = null;
    private static string? _svnVersion = null;
    private static int _svnMajorVersion = 0;

    // Comandos permitidos (solo lectura)
    private static readonly string[] AllowedCommands = new[]
    {
        "log", "info", "list", "cat", "diff", "blame", "status"
    };

    // Comandos prohibidos (escritura/modificación)
    private static readonly string[] ProhibitedCommands = new[]
    {
        "commit", "ci", "delete", "del", "remove", "rm",
        "add", "checkout", "co", "update", "up", "switch",
        "merge", "copy", "cp", "move", "mv", "mkdir",
        "import", "export", "propdel", "propset", "lock", "unlock"
    };

    public override string ProviderName => "SVN";

    public SvnVersionControlHandler(IConfiguration configuration)
        : base(configuration)
    {
        // Verificar si SVN está instalado (solo una vez)
        if (_svnInstalled == null)
        {
            _svnInstalled = IsClientInstalled();
            if (_svnInstalled == true)
            {
                _svnVersion = GetClientVersion();
                if (!string.IsNullOrEmpty(_svnVersion))
                {
                    ParseSvnVersion(_svnVersion);
                    LogDebug($"[SVN] Cliente SVN detectado - v{_svnVersion} (Major: {_svnMajorVersion})");
                }
            }
            else
            {
                LogError($"[SVN] ?? Cliente SVN no encontrado en el sistema");
            }
        }

        LogDebug($"[SVN] Inicializado - URL: {RepositoryUrl}, Timeout: {CommandTimeout}s");

        // Diagnóstico inicial
        if (_svnInstalled == true)
        {
            _ = TestConnectionAsync().Result; // Fire and forget para no bloquear
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
                FileName = "svn",
                Arguments = "--version --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null) return "";

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return output;
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
            //return true;
            LogDebug($"[SVN] Probando conexión con {RepositoryUrl}...");

            var args = new List<string> { "info", $"\"{RepositoryUrl}\"" };

            // Agregar credenciales
            if (!string.IsNullOrEmpty(Username))
            {
                args.Add($"--username \"{Username}\"");
                if (!string.IsNullOrEmpty(Password))
                    args.Add($"--password \"{Password}\"");
            }

            args.Add("--non-interactive");

            // Solo usar trust-server-cert en versiones 1.9+
            if (_svnMajorVersion >= 1)
            {
                args.Add("--trust-server-cert");
            }

            var command = string.Join(" ", args);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "svn",
                Arguments = command,
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
                LogDebug($"[SVN] ? Conexión exitosa con el repositorio");
                return true;
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                LogWarning($"[SVN] ?? Problema al conectar: {error.Substring(0, Math.Min(200, error.Length))}");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogWarning($"[SVN] ?? No se pudo verificar conexión: {ex.Message}");
            return false;
        }
    }

    public override async Task<string> ExecuteReadOnlyOperationAsync(string operation, Dictionary<string, string> parameters)
    {
        if (!IsOperationAllowed(operation))
        {
            throw new InvalidOperationException($"Operación '{operation}' no está permitida. Solo operaciones de lectura.");
        }

        var svnCommand = BuildSvnCommand(operation, parameters);

        LogDebug($"[SVN] Ejecutando: svn {svnCommand}");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "svn",
            Arguments = svnCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
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
            throw new TimeoutException($"La operación SVN excedió el timeout de {CommandTimeout} segundos.");
        }

        if (process.ExitCode != 0)
        {
            var error = errorBuilder.ToString().Trim();
            throw new InvalidOperationException($"SVN retornó código de error {process.ExitCode}: {error}");
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
        message.AppendLine("? **Cliente SVN No Encontrado**\n");
        message.AppendLine("El sistema no puede encontrar el ejecutable `svn` (Subversion client).\n");
        message.AppendLine("**?? Soluciones:**\n");
        message.AppendLine("**Windows:**");
        message.AppendLine("1. Instalar TortoiseSVN: https://tortoisesvn.net/");
        message.AppendLine("   - Durante la instalación, marcar: *\"command line client tools\"*");
        message.AppendLine("2. O instalar Apache Subversion: https://subversion.apache.org/packages.html");
        message.AppendLine("3. Agregar al PATH: `C:\\Program Files\\TortoiseSVN\\bin`");
        message.AppendLine("4. Reiniciar la aplicación\n");
        message.AppendLine("**Linux (Ubuntu/Debian):**");
        message.AppendLine("```bash");
        message.AppendLine("sudo apt-get update");
        message.AppendLine("sudo apt-get install subversion");
        message.AppendLine("```\n");
        message.AppendLine("**Linux (CentOS/RHEL):**");
        message.AppendLine("```bash");
        message.AppendLine("sudo yum install subversion");
        message.AppendLine("```\n");
        message.AppendLine("**macOS:**");
        message.AppendLine("```bash");
        message.AppendLine("brew install svn");
        message.AppendLine("```\n");
        message.AppendLine("**? Verificar instalación:**");
        message.AppendLine("```bash");
        message.AppendLine("svn --version");
        message.AppendLine("```\n");
        message.AppendLine("?? Después de instalar, reinicia esta aplicación.");

        return message.ToString();
    }

    public override string GetErrorSuggestions(string errorMessage)
    {
        var suggestions = new StringBuilder();
        suggestions.AppendLine("?? **Posibles soluciones:**\n");

        if (errorMessage.Contains("E170013") || errorMessage.Contains("Unable to connect"))
        {
            suggestions.AppendLine("**Problema de conexión detectado:**");
            suggestions.AppendLine("1. ?? Verifica que la URL sea correcta: `" + RepositoryUrl + "`");
            suggestions.AppendLine("2. ?? Verifica conectividad de red al servidor (ping, firewall)");
            suggestions.AppendLine("3. ?? Verifica credenciales (usuario/password)");
            suggestions.AppendLine("4. ?? El servidor puede requerir certificado SSL válido");
            suggestions.AppendLine("5. ?? Intenta usar una working copy local si tienes una (configura `WorkingCopyPath`)");
            suggestions.AppendLine();
            suggestions.AppendLine("**Prueba manual:**");
            suggestions.AppendLine("```bash");
            suggestions.AppendLine($"svn info {RepositoryUrl} --username {Username} --password [tu-password]");
            suggestions.AppendLine("```");
        }
        else if (errorMessage.Contains("E120112") || errorMessage.Contains("APR does not understand"))
        {
            suggestions.AppendLine("**Error de APR (Apache Portable Runtime):**");
            suggestions.AppendLine("1. ?? Puede ser un problema temporal del servidor");
            suggestions.AppendLine("2. ?? Reinstala el cliente SVN (TortoiseSVN con command line tools)");
            suggestions.AppendLine("3. ??? Problema con autenticación guardada - limpia cache:");
            suggestions.AppendLine("   - Windows: Elimina `%APPDATA%\\Subversion\\auth`");
            suggestions.AppendLine("   - Linux/Mac: Elimina `~/.subversion/auth`");
        }
        else if (errorMessage.Contains("E170001") || errorMessage.Contains("authorization"))
        {
            suggestions.AppendLine("**Problema de autenticación:**");
            suggestions.AppendLine("1. ?? Verifica usuario/contraseña en appsettings.json");
            suggestions.AppendLine("2. ?? Verifica que el usuario tenga permisos de lectura");
            suggestions.AppendLine("3. ?? Intenta autenticarte manualmente primero con `svn info`");
        }

        suggestions.AppendLine("\n?? Si el problema persiste, considera usar una working copy local.");

        return suggestions.ToString();
    }

    #region Private Helpers

    private void ParseSvnVersion(string versionString)
    {
        try
        {
            // Version format: "1.14.1" or similar
            var parts = versionString.Split('.');
            if (parts.Length > 0 && int.TryParse(parts[0], out var major))
            {
                _svnMajorVersion = major;
            }
        }
        catch
        {
            _svnMajorVersion = 1; // Default to 1.x
        }
    }

    private string BuildSvnCommand(string operation, Dictionary<string, string> parameters)
    {
        var args = new List<string> { operation };

        parameters.TryGetValue("path", out var path);
        parameters.TryGetValue("revision", out var revision);
        parameters.TryGetValue("limit", out var limit);

        path ??= "";
        revision ??= "HEAD";
        limit ??= "10";

        // Si hay working copy configurada, úsala para algunas operaciones
        var useWorkingCopy = !string.IsNullOrEmpty(WorkingCopyPath) &&
                           Directory.Exists(WorkingCopyPath) &&
                           (operation.ToLowerInvariant() == "status" || operation.ToLowerInvariant() == "info");

        string targetUrl;

        if (useWorkingCopy)
        {
            targetUrl = string.IsNullOrEmpty(path)
                ? WorkingCopyPath
                : Path.Combine(WorkingCopyPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            targetUrl = string.IsNullOrEmpty(path)
                ? RepositoryUrl
                : $"{RepositoryUrl.TrimEnd('/')}/{path.TrimStart('/')}";
        }

        switch (operation.ToLowerInvariant())
        {
            case "log":
                args.Add($"\"{targetUrl}\"");
                args.Add($"-r {revision}");
                if (int.TryParse(limit, out var logLimit))
                    args.Add($"-l {logLimit}");
                args.Add("-v");
                break;

            case "info":
                args.Add($"\"{targetUrl}\"");
                if (revision != "HEAD")
                    args.Add($"-r {revision}");
                break;

            case "list":
            case "ls":
                args.Add($"\"{targetUrl}\"");
                if (revision != "HEAD")
                    args.Add($"-r {revision}");
                args.Add("-v");
                break;

            case "cat":
                args.Add($"\"{targetUrl}\"");
                if (revision != "HEAD")
                    args.Add($"-r {revision}");
                break;

            case "diff":
                args.Add($"\"{targetUrl}\"");
                if (revision.Contains(":"))
                    args.Add($"-r {revision}");
                else if (revision != "HEAD")
                    args.Add($"-r {revision}:HEAD");
                break;

            case "blame":
            case "praise":
            case "annotate":
                args.Add($"\"{targetUrl}\"");
                if (revision != "HEAD")
                    args.Add($"-r {revision}");
                break;

            case "status":
                // Ya manejado arriba con useWorkingCopy
                break;
        }

        // Agregar credenciales si no estamos usando working copy
        if (!useWorkingCopy && !string.IsNullOrEmpty(Username))
        {
            args.Add($"--username \"{Username}\"");
            if (!string.IsNullOrEmpty(Password))
                args.Add($"--password \"{Password}\"");
        }

        // Opciones de autenticación
        args.Add("--non-interactive");

        // Usar flag apropiado según versión de SVN
        if (_svnMajorVersion >= 1)
        {
            args.Add("--trust-server-cert");
        }

        return string.Join(" ", args);
    }

    #endregion
}
