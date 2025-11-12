using System.Diagnostics;
using AgentWikiChat.Configuration;
using AgentWikiChat.Models;
using AgentWikiChat.Services.AI;
using AgentWikiChat.Services.Handlers;

namespace AgentWikiChat.Services;

/// <summary>
/// Motor ReAct (Reasoning + Acting) que ejecuta loops de herramientas múltiples.
/// Implementa el patrón: Thought ? Action ? Observation ? Repeat hasta terminar.
/// </summary>
public class ReActEngine
{
    private readonly IToolCallingService _toolService;
    private readonly Dictionary<string, IToolHandler> _handlers;
    private readonly MemoryService _memory;
    private readonly AgentConfig _config;
    private readonly bool _debugMode;

    public ReActEngine(
        IToolCallingService toolService,
        Dictionary<string, IToolHandler> handlers,
        MemoryService memory,
        AgentConfig config,
        bool debugMode = false)
    {
        _toolService = toolService;
        _handlers = handlers;
        _memory = memory;
        _config = config;
        _debugMode = debugMode;
    }

    /// <summary>
    /// Ejecuta el loop ReAct completo para una consulta del usuario.
    /// </summary>
    /// <param name="userQuery">Consulta del usuario</param>
    /// <param name="historicalContext">Contexto histórico de la conversación</param>
    /// <returns>Resultado completo de la ejecución con métricas</returns>
    public async Task<AgentExecutionResult> ExecuteAsync(string userQuery, List<Message> historicalContext)
    {
        var result = new AgentExecutionResult
        {
            StartTime = DateTime.Now,
            Success = false
        };

        var stopwatch = Stopwatch.StartNew();
        var currentContext = new List<Message>(historicalContext);

        // Para detectar loops: tracking de herramientas invocadas
        string? lastToolName = null;
        string? lastToolArgs = null;
        int consecutiveDuplicates = 0;

        try
        {
            LogInfo($"?? Iniciando ReAct Loop (máx {_config.MaxIterations} iteraciones)");

            for (int iteration = 1; iteration <= _config.MaxIterations; iteration++)
            {
                var step = new ReActStep { Iteration = iteration };
                var stepStopwatch = Stopwatch.StartNew();

                LogInfo($"\n{'=',-60}");
                LogInfo($"?? Iteración {iteration}/{_config.MaxIterations}");
                LogInfo($"{'=',-60}");

                // Enviar mensaje al LLM con contexto actual
                var response = await _toolService.SendMessageWithToolsAsync(
            userQuery,
                currentContext
             );

                // Caso 1: El LLM respondió directamente sin tool calls (terminó)
                if (!response.HasToolCalls)
                {
                    step.IsComplete = true;
                    step.FinalAnswer = response.Content ?? "Sin respuesta";
                    step.DurationMs = stepStopwatch.ElapsedMilliseconds;
                    result.Steps.Add(step);

                    LogSuccess($"? Respuesta final obtenida (sin herramientas)");

                    result.FinalAnswer = step.FinalAnswer;
                    result.Success = true;
                    result.TerminationReason = "Respuesta directa del LLM";
                    break;
                }

                // Caso 2: El LLM invocó herramientas
                // Primero, agregar el mensaje del assistant con tool_calls al contexto
                if (response.ToolCalls != null && response.ToolCalls.Any())
                {
                    currentContext.Add(new Message("assistant", response.Content ?? string.Empty, response.ToolCalls));
                }

                foreach (var toolCall in response.ToolCalls!)
                {
                    step.ActionTool = toolCall.Function.Name;
                    step.ActionArguments = toolCall.Function.GetArgumentsAsString();

                    // Detectar loop: misma herramienta con mismos argumentos
                    if (_config.PreventDuplicateToolCalls &&
                         step.ActionTool == lastToolName &&
                    step.ActionArguments == lastToolArgs)
                    {
                        consecutiveDuplicates++;
                        LogWarning($"??  Detectado: misma herramienta invocada {consecutiveDuplicates} veces consecutivas");

                        if (consecutiveDuplicates >= _config.MaxConsecutiveDuplicates)
                        {
                            LogWarning($"?? Loop detectado! Forzando terminación...");

                            // Usar la última observación como respuesta
                            var lastObservation = result.Steps.LastOrDefault()?.Observation;
                            if (!string.IsNullOrEmpty(lastObservation))
                            {
                                result.FinalAnswer = lastObservation;
                                result.Success = true;
                                result.TerminationReason = $"Loop detectado - {consecutiveDuplicates} invocaciones duplicadas";
                                step.IsComplete = true;
                                step.FinalAnswer = lastObservation;
                                step.DurationMs = stepStopwatch.ElapsedMilliseconds;
                                result.Steps.Add(step);
                                return result;
                            }
                        }
                    }
                    else
                    {
                        // Resetear contador si es diferente
                        consecutiveDuplicates = 0;
                    }

                    // Actualizar tracking
                    lastToolName = step.ActionTool;
                    lastToolArgs = step.ActionArguments;

                    LogTool($"???  Herramienta invocada: {step.ActionTool}");
                    LogDebug($"?? Argumentos: {step.ActionArguments}");

                    // Ejecutar handler
                    var observation = await ExecuteToolAsync(toolCall);
                    step.Observation = observation;

                    LogObservation($"???  Observación: {TruncateForDisplay(observation, 500)}");

                    // Agregar la observación al contexto con instrucciones claras para el LLM
                    currentContext.Add(new Message("tool", observation, toolCall.Id));

                    // Agregar instrucción explícita para que el LLM responda
                    if (iteration == 1)
                    {
                        currentContext.Add(new Message("system",
                       "Ahora que tenés la información de la herramienta, " +
                     "respondé al usuario con los datos obtenidos. " +
                              "NO invoques más herramientas a menos que realmente necesites información adicional diferente."));
                    }

                    // Guardar en memoria modular
                    _memory.AddToModule("react", "tool", $"{step.ActionTool}: {observation}");
                }

                step.DurationMs = stepStopwatch.ElapsedMilliseconds;
                result.Steps.Add(step);

                // Si no estamos en modo multi-tool loop, salir después de la primera tool
                if (!_config.EnableMultiToolLoop)
                {
                    result.FinalAnswer = step.Observation ?? "Ejecución completada";
                    result.Success = true;
                    result.TerminationReason = "Modo single-tool (multi-tool desactivado)";
                    break;
                }

                // Continuar el loop para permitir que el LLM procese la observación
            }

            // Si llegamos al límite de iteraciones sin respuesta final
            if (!result.Success)
            {
                // Usar la última observación como respuesta si existe
                var lastObservation = result.Steps.LastOrDefault()?.Observation;
                if (!string.IsNullOrEmpty(lastObservation))
                {
                    result.FinalAnswer = lastObservation;
                    result.Success = true;
                    result.TerminationReason = $"Límite de {_config.MaxIterations} iteraciones - usando última observación";
                }
                else
                {
                    result.FinalAnswer = "Se alcanzó el límite de iteraciones sin completar la tarea.";
                    result.TerminationReason = $"Límite de {_config.MaxIterations} iteraciones alcanzado";
                }

                LogWarning($"??  {result.TerminationReason}");
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.FinalAnswer = $"Error durante la ejecución: {ex.Message}";
            result.TerminationReason = $"Excepción: {ex.GetType().Name}";
            LogError($"? Error: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            result.EndTime = DateTime.Now;
            result.TotalDurationMs = stopwatch.ElapsedMilliseconds;

            LogInfo($"\n?? Resumen de ejecución:");
            LogInfo($"   ??  Duración total: {result.TotalDurationMs}ms");
            LogInfo($"   ?? Iteraciones: {result.TotalIterations}");
            LogInfo($"   ???  Herramientas usadas: {result.ToolCallsCount}");
            var estadoTexto = result.Success ? "Éxito" : "Fallo";
            LogInfo($"   ? Estado: {estadoTexto}");
            LogInfo($"   ?? Razón: {result.TerminationReason}");
        }

        return result;
    }

    /// <summary>
    /// Ejecuta una herramienta específica.
    /// </summary>
    private async Task<string> ExecuteToolAsync(ToolCall toolCall)
    {
        if (!_handlers.TryGetValue(toolCall.Function.Name, out var handler))
        {
            return $"?? Error: No existe handler para la herramienta '{toolCall.Function.Name}'";
        }

        try
        {
            var parameters = new ToolParameters(toolCall.Function.GetArgumentsAsString());
            var result = await handler.HandleAsync(parameters, _memory);
            return result;
        }
        catch (Exception ex)
        {
            var errorMsg = $"? Error ejecutando {toolCall.Function.Name}: {ex.Message}";
            LogError(errorMsg);
            return errorMsg;
        }
    }

    #region Logging Helpers

    private void LogInfo(string message)
    {
        if (_config.ShowIntermediateSteps)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    private void LogSuccess(string message)
    {
        if (_config.ShowIntermediateSteps)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    private void LogTool(string message)
    {
        if (_config.ShowIntermediateSteps)
        {
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    private void LogObservation(string message)
    {
        if (_config.ShowIntermediateSteps)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    private void LogWarning(string message)
    {
        if (_config.ShowIntermediateSteps)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    private void LogError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private void LogDebug(string message)
    {
        if (_debugMode && _config.VerboseMode)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    private string TruncateForDisplay(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength) + "...";
    }

    #endregion
}
