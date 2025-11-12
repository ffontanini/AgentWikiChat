# 🤖 AgentWikiChat

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-3.6.0-green.svg)](https://github.com/fernandobequir/AgentWikiChat)

AgentWikiChat es un agente conversacional inteligente multi-provider basado en .NET 9 que implementa el patrón ReAct (Reasoning + Acting) con soporte completo para Tool Calling. Permite interactuar con múltiples proveedores de IA y ejecutar herramientas especializadas de forma autónoma (Wikipedia, Bases de Datos, Repositorios SVN/Git/GitHub), manteniendo memoria de la sesión y logs persistentes.

---

## ✨ Características Principales

- 🧠 ReAct Pattern: razonamiento paso a paso con múltiples herramientas
- 🔄 Multi-Provider IA: Ollama, OpenAI, LM Studio, Anthropic y Gemini
- 🛠️ Tool Calling Unificado: formato estándar compatible con todos los proveedores
- 🗄️ Multi-Database: SQL Server y PostgreSQL mediante la misma interfaz
- 📚 Wikipedia Integration: búsqueda y obtención de artículos
- 📦 Repositorios: SVN, Git y GitHub (REST API) modo solo lectura
- 🔒 Seguridad: solo SELECT en BD y solo lectura en repositorios
- 💾 Session Logging: guarda conversaciones automáticamente en `Logs/Sessions`
- 🎯 Memoria Modular: contexto global + contexto por módulo
- 🔍 Debug Mode y métricas: visualización del proceso y prevención de loops

---

## 🎯 Casos de Uso

- 💬 Chatbot con acceso a datos estructurados
- 📊 Análisis y reportes con consultas SQL seguras
- 🔍 Búsqueda enciclopédica (Wikipedia)
- 📦 Auditoría de repositorios (historial, blame, diffs)
- 🧪 Investigación multi-paso combinando herramientas (ReAct)

---

## 🚀 Inicio Rápido

### Prerrequisitos

- .NET 9 SDK
- (Opcional) SQL Server o PostgreSQL
- (Opcional) Cliente SVN o Git instalado (para handlers locales)
- Uno de los siguientes proveedores IA: Ollama/LM Studio (local), OpenAI, Anthropic o Gemini

### Instalación

1) Clonar el repositorio

```bash
git clone https://github.com/fernandobequir/AgentWikiChat.git
cd AgentWikiChat
```

2) Restaurar dependencias

```bash
cd AgentWikiChat
dotnet restore
```

3) Configurar proveedores en `appsettings.json`

- IA (estructura actual con ActiveProvider + Providers):

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
        "MaxTokens": 2048,
        "TimeoutSeconds": 300
      }
    ]
  }
}
```

- Base de Datos (multi-provider):

```json
{
  "Database": {
    "ActiveProvider": "SqlServer-Development",
    "Providers": [
      {
        "Name": "SqlServer-Development",
        "Type": "SqlServer",
        "ConnectionString": "Server=localhost;Database=DevDB;Integrated Security=True;TrustServerCertificate=True;",
        "CommandTimeout": 60,
        "MaxRowsToReturn": 1000,
        "EnableQueryLogging": true
      },
      {
        "Name": "PostgreSQL-Local",
        "Type": "PostgreSQL",
        "ConnectionString": "Host=localhost;Port=5432;Database=testdb;Username=dev;Password=dev123;",
        "CommandTimeout": 60,
        "MaxRowsToReturn": 500,
        "EnableQueryLogging": true
      }
    ]
  }
}
```

- Repositorios (SVN/Git/GitHub) vía arquitectura unificada:

```json
{
  "Repository": {
    "ActiveProvider": "GitHub-AgentWikiChat",
    "Providers": [
      {
        "Name": "GitHub-AgentWikiChat",
        "Type": "GitHub",
        "RepositoryUrl": "https://github.com/owner/repo",
        "Username": "your-github-username",
        "Password": "ghp_your_token",
        "WorkingCopyPath": "",
        "CommandTimeout": 60,
        "EnableLogging": true
      },
      {
        "Name": "SVN-TestRepo",
        "Type": "SVN",
        "RepositoryUrl": "https://svn.server.com/repos/project",
        "Username": "user",
        "Password": "pass",
        "WorkingCopyPath": "C:\\Projects\\MySvnWorkingCopy",
        "CommandTimeout": 60,
        "EnableLogging": true
      },
      {
        "Name": "Git-LocalProject",
        "Type": "Git",
        "RepositoryUrl": "",
        "Username": "",
        "Password": "",
        "WorkingCopyPath": "C:\\Projects\\MyLocalGitRepo",
        "CommandTimeout": 60,
        "EnableLogging": true
      }
    ]
  }
}
```

4) Ejecutar

```bash
dotnet run
```

---

## 📖 Uso

### Comandos de la consola

- `/salir` – Finaliza la aplicación
- `/memoria` – Muestra el estado de la memoria conversacional
- `/limpiar` – Limpia la memoria y restablece el system prompt
- `/tools` – Lista herramientas disponibles registradas
- `/config` – Muestra configuración activa del agente
- `/debug` – Alterna modo debug

### Ejemplos de consultas

Wikipedia:
- "busca información sobre inteligencia artificial"

Base de Datos:
- "¿cuántos usuarios hay en la base de datos?"
- "muéstrame las últimas 10 ventas"

SVN/Git/GitHub:
- "muestra los últimos 5 commits del repositorio"
- "¿quién modificó el archivo Main.cs?"
- "lista los archivos en src/"

ReAct (multi-paso):
- "busca información sobre C# en Wikipedia y luego dime cuántos proyectos en C# hay en la BD"

---

## 🛠️ Herramientas Disponibles

1) 📚 Wikipedia
- `search_wikipedia_titles`: busca títulos de artículos (usar primero)
- `get_wikipedia_article`: obtiene el contenido resumido de un artículo

2) 🗄️ Base de Datos (solo lectura)
- `query_database`: ejecuta consultas SELECT (bloquea INSERT/UPDATE/DELETE, etc.)
- Soporta: SQL Server y PostgreSQL

3) 📦 Repositorios (solo lectura)
- `svn_operation` (SVN): `log`, `info`, `list`, `cat`, `diff`, `blame`, `status`
- `git_operation` (Git local): `log`, `show`, `ls-tree`, `blame`, `diff`, `status`, `branch`, `tag`
- `github_operation` (GitHub API): `log`, `show`, `list`, `cat`, `diff`, `blame`, `branches`, `tags`, `info`

---

## 🔧 Configuración del Agente y Logging

- System Prompt, ReAct y loop de herramientas se configuran en `Agent` dentro de `appsettings.json` (máx. iteraciones, timeouts, duplicados consecutivos, etc.).
- `Logging` permite activar el logging de sesión; los archivos se guardan en `Logs/Sessions` con timestamp.
- `Ui` expone `UseEmoji` y `Debug` para la experiencia de consola.

---

## 📚 Documentación

- Arquitectura general: `AgentWikiChat/Docs/ARCHITECTURE.md`
- Wikipedia Tool: dentro del código `Services/Handlers/WikipediaHandler.cs`
- Database Tool: `AgentWikiChat/Docs/SqlServerTool-README.md`
- SVN Tool: `AgentWikiChat/Docs/SVNTool-README.md`
- GitHub Tool: `AgentWikiChat/Docs/GitHubTool-README.md`
- Session Logging: `AgentWikiChat/Docs/SessionLogging-README.md`
- Multi-Database: `AgentWikiChat/Docs/MultiDatabase-Support.md`
- Version Control (arquitectura): `AgentWikiChat/Docs/VersionControl-Architecture.md`

---

## 🏗️ Estructura del Proyecto (resumen)

```
AgentWikiChat/
├── Configuration/
├── Models/
├── Services/
│   ├── AI/                   # Proveedores IA (Ollama/OpenAI/LMStudio/Anthropic/Gemini)
│   ├── Database/             # Multi-DB (SqlServer/PostgreSQL)
│   ├── VersionControl/       # SVN/Git/GitHub + Factory
│   ├── Handlers/             # Handlers de Tools (Wikipedia/DB/Repos/RAG)
│   ├── AgentOrchestrator.cs
│   ├── ReActEngine.cs
│   ├── MemoryService.cs
│   └── ConsoleLogger.cs
├── Docs/
├── Program.cs
└── appsettings.json
```

---

## ✅ Notas y Recomendaciones

- Reemplaza las API keys y credenciales de ejemplo por valores reales (OpenAI/Anthropic/Gemini, GitHub, BD).
- BD y repositorios solo admiten operaciones de lectura por seguridad.
- Si usas `svn_operation` o `git_operation` locales, asegúrate de tener los clientes instalados y en PATH.
- Los modelos y endpoints pueden variar; ajusta `AI.ActiveProvider` y `Providers` según tu entorno.

---

## 👥 Autores
 
- **Fernando Bequir** - *fernandobequir@gmail.com*
- **Francisco Fontanini** - *fjfontanini@gmail.com*
- **Danilo Del Collado** - *daniloddc.92@gmail.com*
- **Francisco Troncoso** - *tonio2007@gmail.com*

---
## 📄 Licencia

MIT
