using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgentWikiChat.Models;
using Microsoft.Extensions.Configuration;

namespace AgentWikiChat.Services.AI;

/// <summary>
/// Servicio para interactuar con OpenAI usando Function Calling.
/// Implementa la interfaz unificada IToolCallingService.
/// </summary>
public class OpenAIToolService : IToolCallingService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string _apiKey;
    private readonly double _temperature;
    private readonly int _maxTokens;
    private readonly string _providerName;
    private readonly List<ToolDefinition> _tools = new();

    public OpenAIToolService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        // Buscar el proveedor activo
        var activeProviderName = configuration["AI:ActiveProvider"] ?? "OpenAI-GPT4";
        var providers = configuration.GetSection("AI:Providers").Get<List<AIProviderConfig>>();

        if (providers == null || !providers.Any())
            throw new InvalidOperationException("No hay proveedores configurados en AI:Providers");

        var provider = providers.FirstOrDefault(p => p.Name == activeProviderName);

        if (provider == null)
            throw new InvalidOperationException($"Proveedor '{activeProviderName}' no encontrado en configuración");

        if (string.IsNullOrWhiteSpace(provider.ApiKey))
            throw new InvalidOperationException("API Key de OpenAI no configurada");

        _providerName = provider.Name;
        _baseUrl = provider.BaseUrl;
        _model = provider.Model;
        _apiKey = provider.ApiKey;
        _temperature = provider.Temperature;
        _maxTokens = provider.MaxTokens;

        _httpClient.BaseAddress = new Uri(_baseUrl);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds);
    }

    public void RegisterTool(ToolDefinition tool)
    {
        if (!_tools.Any(t => t.Function.Name == tool.Function.Name))
        {
            _tools.Add(tool);
        }
    }

    public void RegisterTools(IEnumerable<ToolDefinition> tools)
    {
        foreach (var tool in tools)
        {
            RegisterTool(tool);
        }
    }

    public IReadOnlyList<ToolDefinition> GetRegisteredTools() => _tools.AsReadOnly();

    /// <summary>
    /// Envía un mensaje con contexto y herramientas disponibles.
    /// OpenAI decidirá si necesita invocar alguna herramienta.
    /// </summary>
    public async Task<ToolCallingResponse> SendMessageWithToolsAsync(
        string message,
        IEnumerable<Message> context,
        CancellationToken cancellationToken = default)
    {
        var messages = context.Select(m => new OpenAIMessage
        {
            Role = m.Role,
            Content = m.Content
        }).ToList();

        messages.Add(new OpenAIMessage { Role = "user", Content = message });

        var request = new OpenAIChatRequest
        {
            Model = _model,
            Messages = messages,
            Tools = _tools.Select(ConvertToOpenAITool).ToList(),
            Temperature = _temperature,
            MaxTokens = _maxTokens
        };

        var response = await _httpClient.PostAsJsonAsync("v1/chat/completions", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        try
        {
            var result = JsonSerializer.Deserialize<OpenAIChatResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null || result.Choices == null || !result.Choices.Any())
                throw new InvalidOperationException("Respuesta vacía de OpenAI");

            var choice = result.Choices.First();
            var responseMessage = choice.Message;

            // Convertir tool_calls de OpenAI a formato unificado
            List<ToolCall>? toolCalls = null;
            if (responseMessage.ToolCalls != null && responseMessage.ToolCalls.Any())
            {
                toolCalls = responseMessage.ToolCalls.Select(tc => new ToolCall
                {
                    Id = tc.Id,
                    Type = tc.Type,
                    Function = new ToolCallFunction
                    {
                        Name = tc.Function.Name,
                        Arguments = JsonSerializer.SerializeToElement(tc.Function.Arguments)
                    }
                }).ToList();
            }

            return new ToolCallingResponse
            {
                Content = responseMessage.Content,
                ToolCalls = toolCalls,
                Role = responseMessage.Role,
                Done = true,
                Metadata = new Dictionary<string, object>
                {
                    ["finish_reason"] = choice.FinishReason ?? "stop",
                    ["usage"] = result.Usage ?? new OpenAIUsage()
                }
            };
        }
        catch (JsonException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"? Error deserializando respuesta de OpenAI:");
            Console.WriteLine($"Respuesta cruda: {responseContent}");
            Console.ResetColor();
            throw new InvalidOperationException($"Error deserializando respuesta de OpenAI: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Convierte ToolDefinition (formato unificado) a formato específico de OpenAI.
    /// </summary>
    private OpenAITool ConvertToOpenAITool(ToolDefinition tool)
    {
        return new OpenAITool
        {
            Type = "function",
            Function = new OpenAIFunction
            {
                Name = tool.Function.Name,
                Description = tool.Function.Description,
                Parameters = new OpenAIFunctionParameters
                {
                    Type = "object",
                    Properties = tool.Function.Parameters.Properties.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new OpenAIProperty
                        {
                            Type = kvp.Value.Type,
                            Description = kvp.Value.Description,
                            Enum = kvp.Value.Enum
                        }
                    ),
                    Required = tool.Function.Parameters.Required
                }
            }
        };
    }

    public string GetProviderName() => $"{_providerName} ({_model}) [Tools: {_tools.Count}]";
}

#region DTOs for OpenAI API

internal class OpenAIChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OpenAIMessage> Messages { get; set; } = new();

    [JsonPropertyName("tools")]
    public List<OpenAITool>? Tools { get; set; }

    [JsonPropertyName("temperature")]
    [JsonIgnore]
    public double Temperature { get; set; }
    [JsonIgnore]

    [JsonPropertyName("max_tokens")]

    public int MaxTokens { get; set; }
}

internal class OpenAIMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}

internal class OpenAITool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIFunction Function { get; set; } = new();
}

internal class OpenAIFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public OpenAIFunctionParameters Parameters { get; set; } = new();
}

internal class OpenAIFunctionParameters
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, OpenAIProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = new();
}

internal class OpenAIProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }
}

internal class OpenAIToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAIToolCallFunction Function { get; set; } = new();
}

internal class OpenAIToolCallFunction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = string.Empty;
}

internal class OpenAIChatResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<OpenAIChoice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public OpenAIUsage? Usage { get; set; }
}

internal class OpenAIChoice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public OpenAIMessage Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal class OpenAIUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

#endregion
