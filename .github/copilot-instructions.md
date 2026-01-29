# Copilot Instructions for ghcp-action-importer

## Project Overview
A .NET 10 console app using GitHub Copilot SDK to convert GitLab/Azure DevOps/Jenkins pipelines to GitHub Actions. Uses parallel Copilot sessions for concurrent processing.

## Architecture

### Core Flow
1. `Program.cs` → CLI parsing and orchestration
2. `PipelineScanner` → discovers pipelines using `IPipelineSource` implementations
3. `ParallelPipelineProcessor` → processes pipelines in parallel Copilot sessions
4. Each session: conversion → validation → write output

### Key Interfaces
- **`IPipelineSource`** ([Abstractions/IPipelineSource.cs](src/PipelineConverter/Abstractions/IPipelineSource.cs)): Implement to add new pipeline formats. Must define `Type`, `FilePatterns`, `CanHandle()`, `ExtractInfo()`.
- **`PipelineProcessingResult`**: Contains conversion result, validation, output paths, duration.

### Session Management (Important!)
- `ParallelPipelineProcessor` uses a single `CopilotClient` with multiple sessions
- `SemaphoreSlim` throttles to `MaxParallelSessions` 
- **Session IDs must be alphanumeric + hyphens only** - use `SanitizeSessionId()` helper
- Validation runs in same session as conversion (maintains context)

## Build & Run
```bash
cd src/PipelineConverter
dotnet build
dotnet run -- -i ../../samples -o ../../output -v
dotnet run -- --help  # Show all options
```

## Conventions

### Adding New Pipeline Sources
1. Add enum value to `PipelineType` in `IPipelineSource.cs`
2. Create `Sources/MyPipelineSource.cs` implementing `IPipelineSource`
3. Register in `Program.cs` sources array

### Configuration
- Settings in `appsettings.json`, bound to `AppSettings` class
- CLI args override config (see `ParseArguments()` in Program.cs)
- Create `appsettings.local.json` for local overrides (gitignored)

### Copilot SDK Patterns
```csharp
// Session creation with sanitized ID
var sessionConfig = new SessionConfig {
    SessionId = $"pipeline-{SanitizeSessionId(name)}-{Guid.NewGuid():N}",
    Model = _settings.Copilot.Model
};

// Handle dynamic response types - explicit cast required
string responseContent = (string)(response?.Data?.Content ?? "");

// Tuple returns use .Item1/.Item2 (not deconstruction with dynamic)
var result = ParseCopilotValidation(responseContent);
issues.AddRange(result.Item2);
```

### Custom Agents
Define in markdown with YAML front matter in `Agents/` folder:
```markdown
---
name: my-agent
displayName: Display Name
description: What it does
infer: true
---
Prompt content here...
```
Load with `CustomAgentConfigExtensions.FromMarkdownFile()`.

## Testing
Use sample pipelines in `samples/` folder:
- `.gitlab-ci.yml` - Node.js pipeline
- `azure-pipelines.yml` - .NET multi-stage
- `Jenkinsfile` - Java Maven with Docker

## Key Files
| File | Purpose |
|------|---------|
| `Services/ParallelPipelineProcessor.cs` | Main processing logic, session management |
| `Services/WorkflowWriter.cs` | Output generation (workflows, reports, improved versions) |
| `Abstractions/IPipelineSource.cs` | Interface + PipelineType enum for extensibility |
| `Configuration/AppSettings.cs` | All config models |
| `Extensions/CustomAgentConfigExtensions.cs` | Parse agent markdown files |
