using AgentWikiChat.Configuration;
using AgentWikiChat.Services;
using AgentWikiChat.Services.AI;
using AgentWikiChat.Services.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

// Configurar la configuración
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

// Asegurar salida/entrada UTF-8 para emojis correctos
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Configurar Console Logger
var loggingConfig = configuration.GetSection("Logging");
var enableSessionLogging = loggingConfig.GetValue("EnableSessionLogging", true);
var logDirectory = loggingConfig.GetValue("LogDirectory", "Logs/Sessions") ?? "Logs/Sessions";
var logFilePrefix = loggingConfig.GetValue("LogFilePrefix", "session") ?? "session";

ConsoleLogger? consoleLogger = null;

if (enableSessionLogging)
{
    consoleLogger = new ConsoleLogger(logDirectory, logFilePrefix);
}

try
{
    // Configurar el contenedor de servicios
    var services = new ServiceCollection();

    // Agregar configuración
    services.AddSingleton<IConfiguration>(configuration);

    // Agregar HttpClient
    services.AddHttpClient();

    // Servicio de IA con Tool Calling - Factory multi-provider
    services.AddSingleton<IToolCallingService>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();
        return ToolCallingServiceFactory.CreateService(httpClient, configuration);
    });

    // Servicios de memoria
    services.AddSingleton<MemoryService>();

    // Configuración del agente ReAct
    services.AddSingleton(sp =>
    {
        var agentConfigSection = configuration.GetSection("Agent");
        var agentConfig = new AgentConfig
        {
            MaxIterations = agentConfigSection.GetValue("MaxIterations", 10),
            IterationTimeoutSeconds = agentConfigSection.GetValue("IterationTimeoutSeconds", 60),
            EnableReActPattern = agentConfigSection.GetValue("EnableReActPattern", true),
            EnableMultiToolLoop = agentConfigSection.GetValue("EnableMultiToolLoop", true),
            ShowIntermediateSteps = agentConfigSection.GetValue("ShowIntermediateSteps", true),
            EnableSelfCorrection = agentConfigSection.GetValue("EnableSelfCorrection", true),
            VerboseMode = agentConfigSection.GetValue("VerboseMode", false),
            PreventDuplicateToolCalls = agentConfigSection.GetValue("PreventDuplicateToolCalls", true),
            MaxConsecutiveDuplicates = agentConfigSection.GetValue("MaxConsecutiveDuplicates", 2)
        };
        return agentConfig;
    });

    // Handlers con soporte de Tools
    services.AddSingleton<IToolHandler, WikipediaHandler>();  // Expone 2 tools: search + article
    services.AddSingleton<IToolHandler>(sp => new RAGToolHandler(
        sp.GetRequiredService<IConfiguration>(),
        sp.GetRequiredService<IHttpClientFactory>()));
    services.AddSingleton<IToolHandler, RAGToolHandler>();     // Para RAG general (futuro)
    services.AddSingleton<IToolHandler>(sp => new DatabaseToolHandler(sp.GetRequiredService<IConfiguration>()));
    services.AddSingleton<IToolHandler>(sp => new RepositoryToolHandler(sp.GetRequiredService<IConfiguration>()));

    // Orchestrator con sistema de tools multi-provider + ReAct Engine
    services.AddSingleton(sp =>
    {
        var toolService = sp.GetRequiredService<IToolCallingService>();
        var handlers = sp.GetServices<IToolHandler>();
        var memory = sp.GetRequiredService<MemoryService>();
        var agentConfig = sp.GetRequiredService<AgentConfig>();
        var debugMode = bool.TryParse(configuration["Ui:Debug"], out var d) && d;

        return new AgentOrchestrator(toolService, handlers, memory, agentConfig, debugMode);
    });

    // Construir el service provider
    var serviceProvider = services.BuildServiceProvider();

    // Obtener servicios
    var toolService = serviceProvider.GetRequiredService<IToolCallingService>();
    var memory = serviceProvider.GetRequiredService<MemoryService>();
    var orchestrator = serviceProvider.GetRequiredService<AgentOrchestrator>();
    var agentConfig = serviceProvider.GetRequiredService<AgentConfig>();


    bool useEmoji = bool.TryParse(configuration["Ui:UseEmoji"], out var e) ? e : true;
    bool debugMode = bool.TryParse(configuration["Ui:Debug"], out var d2) ? d2 : false;

    string Bot() => useEmoji ? "🤖" : String.Empty;
    string Prompt() => useEmoji ? "👤" : String.Empty;

    // Helper para mostrar comandos con color
    void PrintCommandHelp()
    {
        Console.Write("💡 Comandos: ");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("/salir");
        Console.ResetColor();
        Console.Write(", ");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("/memoria");
        Console.ResetColor();
        Console.Write(", ");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("/limpiar");
        Console.ResetColor();
        Console.Write(", ");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("/tools");
        Console.ResetColor();
        Console.Write(", ");

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write("/config");
        Console.ResetColor();
        Console.WriteLine();
    }

    // Log de inicio
    var appName = configuration["AppSettings:AppName"] ?? "AgentWikiChat";
    var version = configuration["AppSettings:Version"] ?? "3.0.0";
    var systemPrompt = configuration["AppSettings:SystemPrompt"]
        ?? "Eres un asistente útil experto en tecnología y amigable. Recuerda el contexto de la conversación.";

    // Aplicación de consola interactiva
    Console.WriteLine($"=== {appName} v{version} ===");
    Console.WriteLine($"🤖 Proveedor IA: {toolService.GetProviderName()}");
    Console.WriteLine($"🎯 Sistema: Multi-Provider Tool Calling + ReAct Pattern");
    Console.WriteLine($"🌐 Proveedores: Ollama, OpenAI, LM Studio, Anthropic, Gemini");
    Console.WriteLine($"🧠 ReAct Engine: {(agentConfig.EnableReActPattern ? "✅ ACTIVADO" : "⚠️ DESACTIVADO")}");
    Console.WriteLine($"🔗 Multi-Tool Loop: {(agentConfig.EnableMultiToolLoop ? "✅ ACTIVADO" : "⚠️ DESACTIVADO")} (máx {agentConfig.MaxIterations} iteraciones)");

    if (enableSessionLogging && consoleLogger != null)
    {
        Console.WriteLine($"📝 Session Logging: ✅ ACTIVADO");
        Console.WriteLine($"📁 Log Directory: {logDirectory}");
    }

    PrintCommandHelp();
    Console.WriteLine();

    if (debugMode)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️  Modo DEBUG activado");
        Console.ResetColor();
        orchestrator.PrintAvailableHandlers();
    }

    // Inicializar memoria con system prompt
    memory.AddToGlobal("system", systemPrompt);

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.Write(" " + Prompt() + " Tú> ");

        var input = Console.ReadLine();
        Console.ResetColor();

        // Loguear el input del usuario (ReadLine no pasa por ConsoleLogWriter)
        if (!string.IsNullOrWhiteSpace(input) && consoleLogger != null)
        {
            consoleLogger.LogLine($" {Prompt()} Tú> {input}");
        }

        if (string.IsNullOrWhiteSpace(input))
            continue;

        // Comandos especiales
        if (input.ToLower() is "/salir" or "/exit" or "/quit")
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("👋 ¡Hasta luego!");
            Console.ResetColor();
            Console.WriteLine();
            break;
        }

        if (input.ToLower() == "/memoria")
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("📊 Estado de la memoria:");
            Console.ResetColor();
            Console.WriteLine();
            memory.PrintDebug();
            continue;
        }

        if (input.ToLower() == "/tools")
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("🔧 Herramientas disponibles:");
            Console.ResetColor();
            Console.WriteLine();
            orchestrator.PrintAvailableHandlers();
            continue;
        }

        if (input.ToLower() == "/debug")
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            debugMode = !debugMode;
            Console.WriteLine("⚠️ Nuevo estado Debug: " + debugMode.ToString());
            Console.ResetColor();
            Console.WriteLine();
            orchestrator.PrintAvailableHandlers();
            continue;
        }

        if (input.ToLower() == "/config")
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("⚙️  Configuración del agente:");
            Console.WriteLine($"   🔄 Max Iteraciones: {agentConfig.MaxIterations}");
            Console.WriteLine($"   ⏱️  Timeout por iteración: {agentConfig.IterationTimeoutSeconds}s");
            Console.WriteLine($"   🧠 ReAct Pattern: {(agentConfig.EnableReActPattern ? "✅" : "❌")}");
            Console.WriteLine($"   🔗 Multi-Tool Loop: {(agentConfig.EnableMultiToolLoop ? "✅" : "❌")}");
            Console.WriteLine($"   👁️  Pasos intermedios: {(agentConfig.ShowIntermediateSteps ? "✅" : "❌")}");
            Console.WriteLine($"   🔧 Auto-corrección: {(agentConfig.EnableSelfCorrection ? "✅" : "❌")}");
            Console.WriteLine($"   📢 Modo verbose: {(agentConfig.VerboseMode ? "✅" : "❌")}");
            Console.WriteLine();
            Console.ResetColor();
            continue;
        }

        if (input.ToLower() == "/limpiar")
        {
            memory.ClearAll();
            // Restaurar system prompt desde configuración
            memory.AddToGlobal("system", systemPrompt);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("🧹 Memoria limpiada correctamente");
            Console.ResetColor();
            Console.WriteLine();
            continue;
        }

        // Iniciar timer para medir tiempo de respuesta
        var startTime = DateTime.Now;

        try
        {
            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.Write(" " + Bot() + " Bot: ");
            Console.ResetColor();

            // Procesar consulta - Orchestrator maneja la memoria internamente
            var response = await orchestrator.ProcessQueryAsync(input);

            Console.ForegroundColor = ConsoleColor.DarkBlue;
            Console.WriteLine(response);
            Console.ResetColor();

            // Calcular y mostrar tiempo transcurrido
            var endTime = DateTime.Now;
            var elapsed = endTime - startTime;
            var timeMessage = elapsed.TotalSeconds < 60
          ? $"⏱ Tiempo: {elapsed.TotalSeconds:F2}s | 💬 Mensajes en memoria: {memory.GetTotalMessageCount()}"
                : $"⏱ Tiempo: {elapsed.Minutes:D2}:{elapsed.Seconds:D2}m | 💬 Mensajes en memoria: {memory.GetTotalMessageCount()}";

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(timeMessage);
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (debugMode)
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            Console.ResetColor();
        }

        Console.WriteLine();
    }
}
finally
{
    // Asegurar que el logger se cierre correctamente al finalizar
    consoleLogger?.Dispose();
}
