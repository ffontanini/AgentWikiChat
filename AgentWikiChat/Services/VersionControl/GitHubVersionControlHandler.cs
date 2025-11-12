using Microsoft.Extensions.Configuration;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentWikiChat.Services.VersionControl;

/// <summary>
/// Implementación específica para GitHub usando REST API v3.
/// SEGURIDAD: Solo permite operaciones de lectura.
/// Requiere Personal Access Token con permisos de solo lectura (repo:read).
/// </summary>
public class GitHubVersionControlHandler : BaseVersionControlHandler
{
    private readonly HttpClient _httpClient;
    private readonly string _owner;
    private readonly string _repo;
    private readonly string _token;
    private readonly string _branch;

    // Comandos permitidos (solo lectura usando API)
    private static readonly string[] AllowedCommands = new[]
    {
        "log", "show", "list", "cat", "diff", "blame", "branches", "tags", "info"
    };

    // Comandos prohibidos (escritura/modificación)
    private static readonly string[] ProhibitedCommands = new[]
    {
        "commit", "push", "pull", "merge", "create", "delete",
        "update", "add", "remove", "fork"
    };

    public override string ProviderName => "GitHub";

    public GitHubVersionControlHandler(IConfiguration configuration)
        : base(configuration)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AgentWikiChat", "3.5.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        // Parsear RepositoryUrl para extraer owner y repo
        // Formato esperado: https://github.com/owner/repo o https://github.com/owner/repo.git
        var url = RepositoryUrl.Replace(".git", "").TrimEnd('/');
        var parts = url.Split('/');
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"URL de repositorio GitHub inválida: {RepositoryUrl}. Formato esperado: https://github.com/owner/repo");
        }

        _owner = parts[^2];
        _repo = parts[^1];
        _token = Password; // En GitHub, el password es el Personal Access Token
        _branch = configuration.GetSection("Repository").GetValue<string>("Branch") ?? "main";

        // Configurar autenticación si hay token
        if (!string.IsNullOrEmpty(_token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        LogDebug($"[GitHub] Inicializado - Owner: {_owner}, Repo: {_repo}, Branch: {_branch}");

        // Diagnóstico inicial
        _ = TestConnectionAsync().Result; // Fire and forget
    }

    public override bool IsClientInstalled()
    {
        // GitHub API no requiere cliente local instalado
        return true;
    }

    public override string GetClientVersion()
    {
        // GitHub API siempre está disponible
        return "GitHub API v3";
    }

    public override async Task<bool> TestConnectionAsync()
    {
        try
        {
            LogDebug($"[GitHub] Probando conexión con {_owner}/{_repo}...");

            var response = await _httpClient.GetAsync($"https://api.github.com/repos/{_owner}/{_repo}");

            if (response.IsSuccessStatusCode)
            {
                LogDebug($"[GitHub] ? Conexión exitosa con el repositorio");
                return true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                LogWarning($"[GitHub] ?? Problema al conectar: {response.StatusCode} - {error.Substring(0, Math.Min(200, error.Length))}");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogWarning($"[GitHub] ?? No se pudo verificar conexión: {ex.Message}");
            return false;
        }
    }

    public override async Task<string> ExecuteReadOnlyOperationAsync(string operation, Dictionary<string, string> parameters)
    {
        if (!IsOperationAllowed(operation))
        {
            throw new InvalidOperationException($"Operación '{operation}' no está permitida. Solo operaciones de lectura.");
        }

        parameters.TryGetValue("path", out var path);
        parameters.TryGetValue("revision", out var revision);
        parameters.TryGetValue("limit", out var limit);

        path ??= "";
        revision ??= _branch;
        limit ??= "10";

        LogDebug($"[GitHub] Ejecutando: {operation}, Path: {path}, Revision: {revision}");

        try
        {
            return operation.ToLowerInvariant() switch
            {
                "log" => await GetCommitsAsync(path, revision, int.Parse(limit)),
                "show" => await GetCommitDetailsAsync(revision),
                "list" => await GetTreeAsync(path, revision),
                "cat" => await GetFileContentAsync(path, revision),
                "diff" => await GetDiffAsync(revision, path),
                "blame" => await GetBlameAsync(path, revision),
                "branches" => await GetBranchesAsync(),
                "tags" => await GetTagsAsync(),
                "info" => await GetRepositoryInfoAsync(),
                _ => throw new InvalidOperationException($"Operación '{operation}' no implementada")
            };
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Error al comunicarse con GitHub API: {ex.Message}", ex);
        }
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
        message.AppendLine("?? **GitHub API**\n");
        message.AppendLine("GitHub no requiere instalación de cliente local. Usa la API REST.\n");
        message.AppendLine("**?? Configuración:**\n");
        message.AppendLine("1. Obtén un Personal Access Token:");
        message.AppendLine("   - Ve a: https://github.com/settings/tokens");
        message.AppendLine("   - Click en 'Generate new token (classic)'");
        message.AppendLine("   - Selecciona scope: `repo` (o `public_repo` para repos públicos)");
        message.AppendLine("   - Copia el token generado\n");
        message.AppendLine("2. Configura en `appsettings.json`:");
        message.AppendLine("```json");
        message.AppendLine("{");
        message.AppendLine("  \"GitHub\": {");
        message.AppendLine("    \"Provider\": \"GitHub\",");
        message.AppendLine("    \"RepositoryUrl\": \"https://github.com/owner/repo\",");
        message.AppendLine("    \"Username\": \"your-github-username\",");
        message.AppendLine("    \"Password\": \"ghp_YourPersonalAccessToken\",");
        message.AppendLine("    \"Branch\": \"main\"");
        message.AppendLine("  }");
        message.AppendLine("}");
        message.AppendLine("```\n");
        message.AppendLine("**?? Importante:**");
        message.AppendLine("- El campo `Password` debe contener tu Personal Access Token");
        message.AppendLine("- Para repositorios públicos, el token es opcional");
        message.AppendLine("- Nunca compartas tu token en repositorios públicos");

        return message.ToString();
    }

    public override string GetErrorSuggestions(string errorMessage)
    {
        var suggestions = new StringBuilder();
        suggestions.AppendLine("?? **Posibles soluciones:**\n");

        if (errorMessage.Contains("401") || errorMessage.Contains("Unauthorized") || errorMessage.Contains("Bad credentials"))
        {
            suggestions.AppendLine("**Problema de autenticación (401 Unauthorized):**");
            suggestions.AppendLine("1. ?? Verifica que tu Personal Access Token sea válido");
            suggestions.AppendLine("2. ?? Genera un nuevo token en: https://github.com/settings/tokens");
            suggestions.AppendLine("3. ?? Asegúrate de tener el scope `repo` o `public_repo`");
            suggestions.AppendLine("4. ? El token puede haber expirado");
            suggestions.AppendLine("5. ?? Verifica que el token esté correctamente configurado en appsettings.json");
        }
        else if (errorMessage.Contains("404") || errorMessage.Contains("Not Found"))
        {
            suggestions.AppendLine("**Recurso no encontrado (404):**");
            suggestions.AppendLine("1. ?? Verifica que el repositorio exista: https://github.com/" + _owner + "/" + _repo);
            suggestions.AppendLine("2. ?? Verifica que el path del archivo sea correcto");
            suggestions.AppendLine("3. ?? Verifica que la rama/commit exista");
            suggestions.AppendLine("4. ??? Para repos privados, verifica que tengas permisos de lectura");
        }
        else if (errorMessage.Contains("403") || errorMessage.Contains("Forbidden") || errorMessage.Contains("rate limit"))
        {
            suggestions.AppendLine("**Límite de API excedido (403 Forbidden):**");
            suggestions.AppendLine("1. ?? GitHub limita a 60 requests/hora sin autenticación");
            suggestions.AppendLine("2. ?? Con token: 5,000 requests/hora");
            suggestions.AppendLine("3. ? Espera a que se resetee el límite");
            suggestions.AppendLine("4. ?? Usa un Personal Access Token para aumentar el límite");
        }
        else if (errorMessage.Contains("timeout") || errorMessage.Contains("Timeout"))
        {
            suggestions.AppendLine("**Timeout de conexión:**");
            suggestions.AppendLine("1. ?? Verifica tu conexión a internet");
            suggestions.AppendLine("2. ?? Verifica configuración de firewall/proxy");
            suggestions.AppendLine("3. ?? Aumenta CommandTimeout en appsettings.json");
        }

        suggestions.AppendLine("\n?? **Verificación rápida:**");
        suggestions.AppendLine("```bash");
        suggestions.AppendLine($"curl -H \"Authorization: Bearer YOUR_TOKEN\" https://api.github.com/repos/{_owner}/{_repo}");
        suggestions.AppendLine("```");

        return suggestions.ToString();
    }

    #region API Methods

    private async Task<string> GetCommitsAsync(string path, string branch, int limit)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/commits?sha={branch}&per_page={limit}";
        if (!string.IsNullOrEmpty(path))
            url += $"&path={path}";

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var commits = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine($"?? Últimos {limit} commits:");
        result.AppendLine();

        foreach (var commit in commits.EnumerateArray())
        {
            var sha = commit.GetProperty("sha").GetString()?[..7];
            var message = commit.GetProperty("commit").GetProperty("message").GetString();
            var author = commit.GetProperty("commit").GetProperty("author").GetProperty("name").GetString();
            var date = commit.GetProperty("commit").GetProperty("author").GetProperty("date").GetString();

            result.AppendLine($"?? {sha} - {author}");
            result.AppendLine($"   ?? {date}");
            result.AppendLine($"   ?? {message}");
            result.AppendLine();
        }

        return result.ToString();
    }

    private async Task<string> GetCommitDetailsAsync(string sha)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/commits/{sha}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var commit = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine($"?? Detalles del commit {sha[..7]}:");
        result.AppendLine();
        result.AppendLine($"**Autor**: {commit.GetProperty("commit").GetProperty("author").GetProperty("name").GetString()}");
        result.AppendLine($"**Email**: {commit.GetProperty("commit").GetProperty("author").GetProperty("email").GetString()}");
        result.AppendLine($"**Fecha**: {commit.GetProperty("commit").GetProperty("author").GetProperty("date").GetString()}");
        result.AppendLine($"**Mensaje**: {commit.GetProperty("commit").GetProperty("message").GetString()}");
        result.AppendLine();
        result.AppendLine("**Archivos modificados**:");

        var files = commit.GetProperty("files");
        foreach (var file in files.EnumerateArray())
        {
            var filename = file.GetProperty("filename").GetString();
            var status = file.GetProperty("status").GetString();
            var additions = file.GetProperty("additions").GetInt32();
            var deletions = file.GetProperty("deletions").GetInt32();

            result.AppendLine($"  {status}: {filename} (+{additions}/-{deletions})");
        }

        return result.ToString();
    }

    private async Task<string> GetTreeAsync(string path, string branch)
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/contents/{path}?ref={branch}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var items = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine($"?? Contenido de {(string.IsNullOrEmpty(path) ? "raíz" : path)}:");
        result.AppendLine();

        foreach (var item in items.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString();
            var type = item.GetProperty("type").GetString();
            var size = item.TryGetProperty("size", out var s) ? s.GetInt32() : 0;

            var icon = type == "dir" ? "??" : "??";
            var sizeStr = type == "file" ? $" ({size} bytes)" : "";
            result.AppendLine($"{icon} {name}{sizeStr}");
        }

        return result.ToString();
    }

    private async Task<string> GetFileContentAsync(string path, string branch)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("El comando 'cat' requiere especificar un path de archivo");

        var url = $"https://api.github.com/repos/{_owner}/{_repo}/contents/{path}?ref={branch}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var file = JsonSerializer.Deserialize<JsonElement>(json);

        var encoding = file.GetProperty("encoding").GetString();
        var content = file.GetProperty("content").GetString();

        if (encoding == "base64")
        {
            var bytes = Convert.FromBase64String(content?.Replace("\n", "") ?? "");
            return Encoding.UTF8.GetString(bytes);
        }

        return content ?? "";
    }

    private async Task<string> GetDiffAsync(string sha, string path)
    {
        // GitHub API no tiene endpoint directo para diff, usamos comparación con parent
        var commitUrl = $"https://api.github.com/repos/{_owner}/{_repo}/commits/{sha}";
        var commitResponse = await _httpClient.GetAsync(commitUrl);
        commitResponse.EnsureSuccessStatusCode();

        var commitJson = await commitResponse.Content.ReadAsStringAsync();
        var commit = JsonSerializer.Deserialize<JsonElement>(commitJson);

        var result = new StringBuilder();
        result.AppendLine($"?? Cambios en commit {sha[..7]}:");
        result.AppendLine();

        var files = commit.GetProperty("files");
        foreach (var file in files.EnumerateArray())
        {
            var filename = file.GetProperty("filename").GetString();
            if (!string.IsNullOrEmpty(path) && !filename!.Contains(path))
                continue;

            var patch = file.TryGetProperty("patch", out var p) ? p.GetString() : "Sin cambios de texto";
            result.AppendLine($"**Archivo**: {filename}");
            result.AppendLine("```diff");
            result.AppendLine(patch);
            result.AppendLine("```");
            result.AppendLine();
        }

        return result.ToString();
    }

    private async Task<string> GetBlameAsync(string path, string branch)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("El comando 'blame' requiere especificar un path de archivo");

        // GitHub API no tiene endpoint blame directo, usamos commits del archivo
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/commits?path={path}&sha={branch}&per_page=1";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var commits = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine($"?? Información de autoría de {path}:");
        result.AppendLine();

        if (commits.GetArrayLength() > 0)
        {
            var lastCommit = commits[0];
            var author = lastCommit.GetProperty("commit").GetProperty("author").GetProperty("name").GetString();
            var date = lastCommit.GetProperty("commit").GetProperty("author").GetProperty("date").GetString();
            var message = lastCommit.GetProperty("commit").GetProperty("message").GetString();

            result.AppendLine($"**Última modificación por**: {author}");
            result.AppendLine($"**Fecha**: {date}");
            result.AppendLine($"**Mensaje**: {message}");
        }
        else
        {
            result.AppendLine("No se encontró información de commits para este archivo");
        }

        result.AppendLine();
        result.AppendLine("?? Nota: Para blame línea por línea completo, usa el comando Git local con working copy");

        return result.ToString();
    }

    private async Task<string> GetBranchesAsync()
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/branches";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var branches = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine("?? Ramas disponibles:");
        result.AppendLine();

        foreach (var branch in branches.EnumerateArray())
        {
            var name = branch.GetProperty("name").GetString();
            var isProtected = branch.GetProperty("protected").GetBoolean();
            var icon = isProtected ? "??" : "??";

            result.AppendLine($"{icon} {name}");
        }

        return result.ToString();
    }

    private async Task<string> GetTagsAsync()
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}/tags";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var tags = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine("??? Tags disponibles:");
        result.AppendLine();

        foreach (var tag in tags.EnumerateArray())
        {
            var name = tag.GetProperty("name").GetString();
            var sha = tag.GetProperty("commit").GetProperty("sha").GetString()?[..7];

            result.AppendLine($"??? {name} ({sha})");
        }

        if (tags.GetArrayLength() == 0)
        {
            result.AppendLine("No hay tags en este repositorio");
        }

        return result.ToString();
    }

    private async Task<string> GetRepositoryInfoAsync()
    {
        var url = $"https://api.github.com/repos/{_owner}/{_repo}";
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var repo = JsonSerializer.Deserialize<JsonElement>(json);

        var result = new StringBuilder();
        result.AppendLine($"?? Información del repositorio {_owner}/{_repo}:");
        result.AppendLine();
        result.AppendLine($"**Nombre**: {repo.GetProperty("full_name").GetString()}");
        result.AppendLine($"**Descripción**: {(repo.TryGetProperty("description", out var desc) ? desc.GetString() : "Sin descripción")}");
        result.AppendLine($"**Visibilidad**: {(repo.GetProperty("private").GetBoolean() ? "?? Privado" : "?? Público")}");
        result.AppendLine($"**Rama predeterminada**: {repo.GetProperty("default_branch").GetString()}");
        result.AppendLine($"**Lenguaje principal**: {(repo.TryGetProperty("language", out var lang) ? lang.GetString() : "N/A")}");
        result.AppendLine($"**Tamaño**: {repo.GetProperty("size").GetInt32()} KB");
        result.AppendLine($"**Stars**: ? {repo.GetProperty("stargazers_count").GetInt32()}");
        result.AppendLine($"**Forks**: ?? {repo.GetProperty("forks_count").GetInt32()}");
        result.AppendLine($"**Issues abiertos**: ?? {repo.GetProperty("open_issues_count").GetInt32()}");
        result.AppendLine($"**Creado**: {repo.GetProperty("created_at").GetString()}");
        result.AppendLine($"**Última actualización**: {repo.GetProperty("updated_at").GetString()}");
        result.AppendLine($"**URL**: {repo.GetProperty("html_url").GetString()}");

        return result.ToString();
    }

    #endregion
}
