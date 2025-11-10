# 🤖 AgentWikiChat

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-3.4.0-green.svg)](https://github.com/yourusername/AgentWikiChat)

**AgentWikiChat** es un agente conversacional inteligente multi-provider basado en .NET 9 que implementa el patrón **ReAct (Reasoning + Acting)** con soporte completo para **Tool Calling**. Permite interactuar con múltiples proveedores de IA y ejecutar herramientas especializadas de forma autónoma.

----

## ✨ Características Principales

- 🧠 **Patrón ReAct**: Razonamiento paso a paso con múltiples herramientas
- 🔄 **Multi-Provider AI**: Soporte para Ollama, OpenAI, LM Studio y Anthropic Claude
- 🛠️ **Tool Calling Unificado**: Formato estándar compatible con todos los proveedores
- 🗄️ **Multi-Database**: SQL Server y PostgreSQL con misma interfaz
- 📚 **Wikipedia Integration**: Búsqueda y obtención de artículos
- 🔒 **Seguridad**: Solo consultas SELECT de lectura en bases de datos
- 💾 **Session Logging**: Guarda conversaciones automáticamente
- 🎯 **Memoria Modular**: Contexto global + contexto por módulo
- 🔍 **Debug Mode**: Visualización detallada del proceso de razonamiento
- ⚡ **Detección de Loops**: Previene invocaciones repetidas inútiles

---

## 🎯 Casos de Uso

- 💬 **Chatbot Inteligente** con acceso a datos estructurados
- 📊 **Análisis de Datos** mediante consultas SQL naturales
- 🔍 **Búsqueda de Información** enciclopédica (Wikipedia)
- 🧪 **Investigación Multi-Paso** usando varias herramientas en secuencia
- 📈 **Reportes Automáticos** desde bases de datos
- 🎓 **Asistente de Aprendizaje** con contexto conversacional

---

## 🚀 Inicio Rápido

### Prerequisitos

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- SQL Server o PostgreSQL (opcional, para la herramienta de base de datos)
- Uno de los siguientes proveedores de IA:
  - [Ollama](https://ollama.ai/) (local, gratis)
  - [LM Studio](https://lmstudio.ai/) (local, gratis)
  - OpenAI API Key
  - Anthropic API Key

### Instalación

1. **Clonar el repositorio**
```bash
git clone https://github.com/yourusername/AgentWikiChat.git
cd AgentWikiChat
```

2. **Restaurar dependencias**
```bash
cd AgentWikiChat
dotnet restore
```

3. **Configurar el proveedor de IA**

Editar `appsettings.json`:
```json
{
  "AI": {
    "ActiveProvider": "LMStudio-Local",
    "Providers": [
      {
        "Name": "LMStudio-Local",
        "Type": "LMStudio",
        "BaseUrl": "http://localhost:1234",
        "Model": "meta-llama-3-8b-instruct",
 "Temperature": 0.7,
        "MaxTokens": 2048
      }
    ]
  }
}
```

4. **Configurar base de datos** (opcional)

```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=localhost;Database=MyDB;Integrated Security=True;",
    "MaxRowsToReturn": 100
  }
}
```

5. **Ejecutar**
```bash
dotnet run
```

---

## 📖 Uso

### Comandos Disponibles

| Comando | Descripción |
|---------|-------------|
| `/salir` | Finalizar la aplicación |
| `/memoria` | Ver estado de la memoria conversacional |
| `/limpiar` | Limpiar memoria y reiniciar contexto |
| `/tools` | Listar herramientas disponibles |
| `/config` | Ver configuración del agente |
| `/debug` | Alternar modo debug |

### Ejemplos de Consultas

**Wikipedia:**
```
👤 Tú> busca información sobre inteligencia artificial
```

**Base de Datos (SQL Server):**
```
👤 Tú> ¿cuántos usuarios hay en la base de datos?
👤 Tú> muéstrame las últimas 10 ventas
```

**Base de Datos (PostgreSQL):**
```
👤 Tú> dame los 5 productos más caros
👤 Tú> lista todas las tablas disponibles
```

**Multi-Step (ReAct):**
```
👤 Tú> busca información sobre C# en Wikipedia y luego cuéntame cuántos proyectos en C# tenemos en la BD
```

---

## 🛠️ Herramientas Disponibles

### 1. 📚 Wikipedia
- **`search_wikipedia_titles`**: Busca títulos de artículos
- **`get_wikipedia_article`**: Obtiene contenido completo de un artículo

### 2. 🗄️ Base de Datos
- **`query_database`**: Ejecuta consultas SELECT (solo lectura)
- Soporta: SQL Server, PostgreSQL
- Seguridad: Bloquea INSERT, UPDATE, DELETE, DROP, etc.

### 3. 🔮 RAG (Futuro)
- Búsqueda vectorial y recuperación de documentos

### 4. 📦 SVN Repository (Futuro)
- Consultas a repositorios de código

---

## ⚙️ Configuración Avanzada

### Configuración del Agente ReAct

```json
{
  "Agent": {
    "MaxIterations": 10,
    "IterationTimeoutSeconds": 300,
    "EnableReActPattern": true,
    "EnableMultiToolLoop": true,
    "ShowIntermediateSteps": true,
    "EnableSelfCorrection": true,
    "PreventDuplicateToolCalls": true,
    "MaxConsecutiveDuplicates": 3
  }
}
```

### Proveedores de IA

#### Ollama (Local)
```json
{
  "Name": "Ollama-Local",
  "Type": "Ollama",
  "BaseUrl": "http://localhost:11434",
  "Model": "qwen2.5:7b-instruct",
  "Temperature": 0.9
}
```

#### OpenAI
```json
{
  "Name": "OpenAI-GPT4",
  "Type": "OpenAI",
  "BaseUrl": "https://api.openai.com/v1",
  "ApiKey": "tu-api-key-aqui",
  "Model": "gpt-4-turbo-preview",
  "Temperature": 0.7
}
```

#### Anthropic Claude
```json
{
  "Name": "Anthropic-Claude-Sonnet",
  "Type": "Anthropic",
  "BaseUrl": "https://api.anthropic.com",
  "ApiKey": "tu-api-key-aqui",
  "Model": "claude-3-5-sonnet-20241022",
  "Temperature": 0.7
}
```

---

## 📁 Estructura del Proyecto

```
AgentWikiChat/
├── Configuration/     # Configuración del agente
├── Models/        # Modelos de datos
├── Services/
│   ├── AI/            # Servicios de proveedores de IA
│   ├── Database/      # Handlers de bases de datos
│   ├── Handlers/      # Handlers de herramientas
│   ├── AgentOrchestrator.cs
│   ├── ReActEngine.cs
│   ├── MemoryService.cs
│   └── ConsoleLogger.cs
├── Docs/    # Documentación
├── Logs/ # Logs de sesiones (no versionado)
├── Program.cs    # Punto de entrada
└── appsettings.json   # Configuración
```

---

## 📚 Documentación

- 📐 **[Arquitectura](AgentWikiChat/Docs/ARCHITECTURE.md)** - Diseño y patrones del sistema
- 🗄️ **[Database Tool](AgentWikiChat/Docs/SqlServerTool-README.md)** - Uso de consultas SQL
- 📝 **[Session Logging](AgentWikiChat/Docs/SessionLogging-README.md)** - Sistema de logging

---

## 🔒 Seguridad

### Base de Datos
- ✅ Solo consultas `SELECT` permitidas
- ❌ Bloqueadas: INSERT, UPDATE, DELETE, DROP, TRUNCATE, EXEC
- 🛡️ Validación antes de ejecutar consultas
- ⏱️ Timeout configurable para prevenir consultas lentas
- 📊 Límite de filas retornadas

### Logging
- 📁 Logs locales excluidos del repositorio (`.gitignore`)
- 🔐 No se registran contraseñas ni datos sensibles de configuración
- 🗂️ Permisos restringidos al usuario que ejecuta

---

## 🧪 Testing

```bash
# Ejecutar en modo debug
dotnet run

# Ver configuración
/config

# Listar herramientas
/tools

# Probar Wikipedia
busca información sobre .NET

# Probar Base de Datos
SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
```

---

## 🗺️ Roadmap

- [ ] Soporte para MySQL y SQLite
- [ ] RAG con embeddings y búsqueda vectorial
- [ ] Web API REST
- [ ] Dashboard web para monitoreo
- [ ] Herramienta de búsqueda en archivos locales
- [ ] Integración con GitHub API
- [ ] Soporte para Azure OpenAI
- [ ] Docker containerization
- [ ] Unit tests y integration tests

---

## 📄 Licencia

Este proyecto está bajo la licencia MIT. Ver el archivo [LICENSE](LICENSE) para más detalles.

---

## 👥 Autores

- **Fernando Bequir** - *Trabajo Inicial*
- **Francisco Fontanini** - *SQL Tools*

---

## 🙏 Agradecimientos

- [Ollama](https://ollama.ai/) por proporcionar LLMs locales
- [LM Studio](https://lmstudio.ai/) por la interfaz local de modelos
- [OpenAI](https://openai.com/) por la API de GPT
- [Anthropic](https://anthropic.com/) por Claude
- [Wikipedia](https://wikipedia.org/) por la API pública

---

<p align="center">
  Hecho con ❤️ usando .NET 9
</p>
