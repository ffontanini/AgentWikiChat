using AgentWikiChat.Models;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AgentWikiChat.Services.Handlers;

/// <summary>
/// Handler para RAG (Retrieval-Augmented Generation).
/// Búsqueda en documentos indexados en Chroma usando embeddings de LM Studio.
/// </summary>
public class RAGToolHandler : IToolHandler
{
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _lmStudioEmbeddingsUrl;
    private readonly string _chromaApiUrl;
    private readonly string _collectionName;
    private readonly int _maxResults;
    private readonly int _timeoutSeconds;
    private readonly bool _debugMode = true;

    public RAGToolHandler(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;

        var ragConfig = configuration.GetSection("RAG");
        _lmStudioEmbeddingsUrl = ragConfig.GetValue<string>("LmStudioEmbeddings")
            ?? "http://localhost:1234/v1/embeddings";
        _chromaApiUrl = ragConfig.GetValue<string>("ChromaApi")
            ?? "http://localhost:8000/api/v1";
        _collectionName = ragConfig.GetValue<string>("CollectionName")
            ?? "Documentos";
        _maxResults = ragConfig.GetValue<int>("MaxResults", 5);
        _timeoutSeconds = ragConfig.GetValue<int>("TimeoutSeconds", 300);

        LogDebug($"[RAG] Inicializado - Embeddings: {_lmStudioEmbeddingsUrl}, Chroma: {_chromaApiUrl}, Collection: {_collectionName}");
    }

    public string ToolName => "search_documents";

    public ToolDefinition GetToolDefinition()
    {
        return new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = ToolName,
                Description = "Busca información en documentos indexados usando RAG (Retrieval-Augmented Generation). " +
                             "Esta herramienta busca en una base de datos vectorial (Chroma) documentos relevantes " +
                             "basados en similitud semántica. Útil para encontrar información específica en documentos " +
                             "previamente indexados (.docx, .pdf, .json, etc.).",
                Parameters = new FunctionParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, PropertyDefinition>
                    {
                        ["query"] = new PropertyDefinition
                        {
                            Type = "string",
                            Description = "Consulta de búsqueda. Describe lo que estás buscando en los documentos indexados."
                        },
                        ["max_results"] = new PropertyDefinition
                        {
                            Type = "string",
                            Description = $"Número máximo de fragmentos relevantes a retornar (por defecto: {_maxResults})."
                        }
                    },
                    Required = new List<string> { "query" }
                }
            }
        };
    }

    public async Task<string> HandleAsync(ToolParameters parameters, MemoryService memory)
    {
        var query = parameters.GetString("query");
        var maxResultsStr = parameters.GetString("max_results", _maxResults.ToString());

        if (string.IsNullOrWhiteSpace(query))
        {
            return "⚠️ Error: La consulta de búsqueda no puede estar vacía.";
        }

        // Validar que el máximo de resultados sea un número válido
        if (!int.TryParse(maxResultsStr, out var maxResults))
        {
            maxResults = _maxResults;
        }

        // Limitar el máximo de resultados
        if (maxResults > 10)
        {
            maxResults = 10;
        }

        LogDebug($"[RAG] Búsqueda: '{TruncateForDisplay(query, 200)}' - MaxResults: {maxResults}");

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

            // 1. Obtener o crear la colección en Chroma
            string collectionId = await GetOrCreateCollectionAsync(httpClient, _collectionName);

            // 2. Crear embedding de la pregunta
            LogDebug("[RAG] Creando embedding de la consulta...");
            var queryEmbedding = await CreateEmbeddingAsync(httpClient, query);

            // 3. Buscar documentos relevantes en Chroma
            LogDebug("[RAG] Buscando documentos relevantes en Chroma...");
            var relevantDocuments = await QueryChromaAsync(httpClient, collectionId, queryEmbedding, maxResults);

            if (relevantDocuments.Count == 0)
            {
                return $"🔍 **Búsqueda RAG completada**\n\n" +
                       $"**Consulta**: `{query}`\n\n" +
                       $"ℹ️ No se encontraron documentos relevantes en la colección '{_collectionName}'.\n\n" +
                       $"💡 **Sugerencia**: Asegúrate de que los documentos hayan sido indexados previamente.";
            }

            // 4. Formatear respuesta
            var response = FormatRAGResponse(query, relevantDocuments);

            // Guardar en memoria
            memory.AddToModule("rag", "system", $"Búsqueda RAG: '{TruncateForDisplay(query, 100)}' - {relevantDocuments.Count} resultados");

            return response;
        }
        catch (HttpRequestException ex)
        {
            LogError($"[RAG] Error HTTP: {ex.Message}");
            return $"❌ **Error de conexión RAG**\n\n" +
                   $"**Mensaje**: {ex.Message}\n\n" +
                   $"💡 Verifica que:\n" +
                   $"   - LM Studio esté ejecutándose en {_lmStudioEmbeddingsUrl}\n" +
                   $"   - Chroma esté ejecutándose en {_chromaApiUrl}";
        }
        catch (Exception ex)
        {
            LogError($"[RAG] Error: {ex.Message}");
            return $"❌ **Error en búsqueda RAG**\n\n" +
                   $"**Mensaje**: {ex.Message}\n\n" +
                   $"💡 Verifica la configuración y que los servicios estén disponibles.";
        }
    }

    #region Chroma Operations

    /// <summary>
    /// Obtiene o crea una colección en Chroma y retorna su ID.
    /// </summary>
    private async Task<string> GetOrCreateCollectionAsync(HttpClient httpClient, string collectionName)
    {
        // Intentar obtener colecciones existentes
        var getResponse = await httpClient.GetAsync($"{_chromaApiUrl}/collections");
        if (getResponse.IsSuccessStatusCode)
        {
            var jsonGet = await getResponse.Content.ReadAsStringAsync();
            using var docGet = JsonDocument.Parse(jsonGet);

            if (docGet.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var collection in docGet.RootElement.EnumerateArray())
                {
                    if (collection.TryGetProperty("name", out var nameProp) &&
                        nameProp.GetString() == collectionName)
                    {
                        if (collection.TryGetProperty("id", out var idProp))
                        {
                            string existingId = idProp.GetString() ?? string.Empty;
                            LogDebug($"[RAG] Colección '{collectionName}' encontrada. ID: {existingId}");
                            return existingId;
                        }
                    }
                }
            }
        }

        // Crear nueva colección si no existe
        var payload = new { name = collectionName };
        var createResponse = await httpClient.PostAsync(
            $"{_chromaApiUrl}/collections",
            JsonBody(payload));

        if (!createResponse.IsSuccessStatusCode)
        {
            var errorContent = await createResponse.Content.ReadAsStringAsync();
            throw new Exception($"Error al crear colección: {createResponse.StatusCode} - {errorContent}");
        }

        var jsonCreate = await createResponse.Content.ReadAsStringAsync();
        using var docCreate = JsonDocument.Parse(jsonCreate);

        if (!docCreate.RootElement.TryGetProperty("id", out var newIdProp))
        {
            throw new Exception("No se pudo obtener el ID de la colección creada.");
        }

        string newId = newIdProp.GetString() ?? string.Empty;
        LogDebug($"[RAG] Colección '{collectionName}' creada. ID: {newId}");
        return newId;
    }

    /// <summary>
    /// Crea un embedding para el texto dado usando LM Studio.
    /// </summary>
    private async Task<float[]> CreateEmbeddingAsync(HttpClient httpClient, string texto)
    {
        var payload = new { input = texto };
        var response = await httpClient.PostAsync(_lmStudioEmbeddingsUrl, JsonBody(payload));

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error al crear embedding: {response.StatusCode} - {errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var dataProp) ||
            dataProp.GetArrayLength() == 0)
        {
            throw new Exception("Respuesta de embedding inválida: no hay datos.");
        }

        var embeddingArray = dataProp[0].GetProperty("embedding").EnumerateArray();
        var embedding = new List<float>();

        foreach (var value in embeddingArray)
        {
            embedding.Add(value.GetSingle());
        }

        return embedding.ToArray();
    }

    /// <summary>
    /// Consulta Chroma para obtener documentos similares.
    /// </summary>
    private async Task<List<string>> QueryChromaAsync(
        HttpClient httpClient,
        string collectionId,
        float[] queryEmbedding,
        int nResults)
    {
        var payload = new
        {
            query_embeddings = new[] { queryEmbedding },
            n_results = nResults
        };

        var response = await httpClient.PostAsync(
            $"{_chromaApiUrl}/collections/{collectionId}/query",
            JsonBody(payload));

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error al consultar Chroma: {response.StatusCode} - {errorContent}");
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var documents = new List<string>();

        if (doc.RootElement.TryGetProperty("documents", out var docsProp) &&
            docsProp.GetArrayLength() > 0)
        {
            var docsArray = docsProp[0];
            foreach (var docElement in docsArray.EnumerateArray())
            {
                var docText = docElement.GetString();
                if (!string.IsNullOrWhiteSpace(docText))
                {
                    documents.Add(docText);
                }
            }
        }

        return documents;
    }

    #endregion

    #region Formatting

    /// <summary>
    /// Formatea la respuesta de RAG para presentación al usuario.
    /// </summary>
    private string FormatRAGResponse(string query, List<string> documents)
    {
        var output = new StringBuilder();

        output.AppendLine($"🔍 **Resultado de Búsqueda RAG**\n");
        output.AppendLine($"**Consulta**: `{query}`");
        output.AppendLine($"**Documentos encontrados**: {documents.Count}\n");
        output.AppendLine("---\n");

        for (int i = 0; i < documents.Count; i++)
        {
            output.AppendLine($"**Fragmento {i + 1}:**\n");
            output.AppendLine(documents[i]);
            output.AppendLine("\n---\n");
        }

        output.AppendLine($"💡 **Nota**: Estos fragmentos fueron recuperados de la base de datos vectorial " +
                         $"basándose en similitud semántica con tu consulta.");

        return output.ToString();
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Crea un StringContent con JSON serializado.
    /// </summary>
    private static StringContent JsonBody(object obj)
    {
        return new StringContent(
            JsonSerializer.Serialize(obj),
            Encoding.UTF8,
            "application/json");
    }

    private void LogDebug(string message)
    {
        if (_debugMode)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[DEBUG] {message}");
            Console.ResetColor();
        }
    }

    private void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }

    private string TruncateForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength) + "...";
    }

    #endregion
}
