# Pipeline to GitHub Actions Converter

> [!WARNING]
> This project is experimental and under active development. Features may change without notice and the generated workflows should be reviewed carefully before use in production.

A .NET 10 application that converts CI/CD pipelines from GitLab, Azure DevOps, and Jenkins to GitHub Actions using the [GitHub Copilot SDK](https://github.com/github/copilot-sdk).

## Features

- **Multi-source support**: Convert pipelines from GitLab CI, Azure DevOps, and Jenkins
- **AI-powered conversion**: Uses GitHub Copilot to intelligently map pipeline constructs to GitHub Actions
- **Custom agents**: Define custom Copilot agents via markdown files with YAML front matter
- **Validation agent**: Custom Copilot agent validates generated workflows for:
  - YAML syntax correctness
  - GitHub Actions structure requirements
  - Security best practices
  - Action version pinning
- **Live web dashboard**: Optional Blazor Server dashboard (`--port`) for real-time conversion progress monitoring
- **Extensible architecture**: `IPipelineSource` interface allows easy addition of new pipeline sources
- **Detailed reports**: Generates validation reports with suggestions for improvements
- **Auto-improved workflows**: Validation improvements are applied directly to the output workflow

## How It Works

```mermaid
flowchart TB
    Input["📁 <b>Input Directory</b><br/>.gitlab-ci.yml · azure-pipelines.yml · Jenkinsfile"]

    Scan["🔍 <b>PipelineScanner</b><br/>Matches files against IPipelineSource patterns"]

    Input --> Scan

    subgraph Process["⚙️ ParallelPipelineProcessor"]
        direction TB
        Client["<b>CopilotClient</b> — single connection, N parallel sessions"]
        Client --> S1 & S2 & SN

        S1["<b>Session 1</b><br/>🔄 Converter Agent → ✅ Validator Agent"]
        S2["<b>Session 2</b><br/>🔄 Converter Agent → ✅ Validator Agent"]
        SN["<b>Session N</b><br/>🔄 Converter Agent → ✅ Validator Agent"]
    end

    Scan --> Process

    Write["💾 <b>WorkflowWriter</b><br/>Saves workflow.yml (with improvements) + validation.md"]

    Process --> Write

    Output["📂 <b>Output Directory</b><br/>✅ Converted workflows · 📝 Validation reports"]

    Write --> Output

    Dashboard["📊 <b>Blazor Dashboard</b> (optional)<br/>Real-time progress UI via --port"]

    Process -.-> Dashboard
```

1. **Scan** — `PipelineScanner` walks the input directory and matches files against registered `IPipelineSource` implementations (GitLab, Azure DevOps, Jenkins). Each matched file is read and wrapped in a `PipelineInfo` object.

2. **Connect** — A single `CopilotClient` is created and connects to GitHub Copilot via the CLI. A `SemaphoreSlim` throttles work to `MaxParallelSessions` concurrent sessions.

3. **Convert** — For each pipeline, a dedicated Copilot session is created with a sanitized session ID. The `CopilotConverterService` sends the pipeline content along with a conversion prompt to a custom **pipeline-converter** agent. The model returns a GitHub Actions workflow in a YAML code block, which is extracted and saved.

4. **Validate** — In the *same* session (preserving conversation context), the `CopilotValidationService` sends the original pipeline and converted workflow to a custom **workflow-validator** agent. The validator checks syntax, security, and action pinning, then returns issues, suggestions, and an improved workflow.

5. **Write** — `WorkflowWriter` saves the workflow file (overwriting with the improved version if one was produced) and generates a `.validation.md` report.

6. **Report** — Console output summarizes results per pipeline (errors, warnings, suggestions, duration). If `--port` was specified, the Blazor Server dashboard shows live progress throughout.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) installed and authenticated
- Active GitHub Copilot subscription

## Quick Start

```bash
git clone https://github.com/Frank802/ghcp-action-importer.git
cd ghcp-action-importer/src
dotnet build
```

Run the converter on the included sample pipelines:

```bash
dotnet run -- -i ../samples -o ../output -v
```

This will convert:
- GitLab CI (`.gitlab-ci.yml`) — Node.js build/test/deploy pipeline
- Azure DevOps (`azure-pipelines.yml`) — .NET multi-stage pipeline
- Jenkins (`Jenkinsfile`) — Java Maven with Docker and Kubernetes

Output will be saved to `output/` with:
- Converted workflow files (`.yml`)
- Validation reports (`.validation.md`)

## Usage

```bash
# Basic usage
dotnet run -- -i <input-folder> -o <output-folder>

# Convert only GitLab pipelines with verbose output
dotnet run -- -i ./pipelines -o ./converted -s GitLab --verbose

# Skip validation step
dotnet run -- -i ./ci -o ./output --skip-validation

# Launch with live web dashboard
dotnet run -- -i ./ci -o ./output -p 5050
```

### Command Line Options

| Option | Alias | Description |
|--------|-------|-------------|
| `--input` | `-i` | **Required.** Directory containing pipeline files to convert |
| `--output` | `-o` | **Required.** Output directory for converted workflows |
| `--source` | `-s` | Filter to specific source type: `GitLab`, `AzureDevOps`, `Jenkins` |
| `--max-sessions` | `-m` | Maximum parallel Copilot sessions (default: 3) |
| `--port` | `-p` | Start a Blazor Server dashboard on the given port |
| `--skip-validation` | | Skip the validation step after conversion |
| `--verbose` | `-v` | Enable verbose output |
| `--help` | `-h` | Show help message |

## Live Dashboard

Pass `--port` to launch an interactive Blazor Server dashboard that displays real-time conversion progress:

```bash
dotnet run -- -i ../samples -o ../output -p 5050
```

The dashboard shows:
- Overall progress bar with completion percentage
- Per-pipeline status cards with phase indicators (converting, validating, writing, complete/failed)
- Source type badges (GitLab, Azure DevOps, Jenkins)
- Elapsed time per pipeline and total processing time
- Error details for failed conversions

The dashboard stays running after processing completes — press Ctrl+C to stop.

## Supported Pipeline Formats

| Source | File Patterns |
|--------|---------------|
| GitLab CI/CD | `.gitlab-ci.yml`, `.gitlab-ci.yaml` |
| Azure DevOps | `azure-pipelines.yml`, `azure-pipelines.yaml` |
| Jenkins | `Jenkinsfile`, `Jenkinsfile.*` |

## Output Structure

Converted workflows are saved to the output directory. When `CreateWorkflowsSubdirectory` is enabled (default), the structure is:

```
<output-folder>/
└── .github/
    └── workflows/
        ├── gitlab-ci.yml           # Converted workflow (with improvements applied)
        └── gitlab-ci.validation.md # Validation report
```

When disabled, files are written directly to the output folder. If the validator produces an improved workflow, it overwrites the converted file automatically.

## Project Structure

```
ghcp-action-importer/
├── ghcp-action-importer.sln            # Solution file
├── samples/                            # Sample pipeline files for testing
│   ├── .gitlab-ci.yml
│   ├── azure-pipelines.yml
│   └── Jenkinsfile
├── src/
│   ├── PipelineConverter.csproj        # Project file (Microsoft.NET.Sdk.Web)
│   ├── Program.cs                      # CLI entry point & Blazor host
│   ├── appsettings.json                # Configuration file
│   ├── Abstractions/
│   │   └── IPipelineSource.cs          # Interface + PipelineType enum
│   ├── Agents/
│   │   ├── pipeline-converter.md       # Converter agent definition
│   │   └── workflow-validator.md       # Validator agent definition
│   ├── Components/                     # Blazor Server UI
│   │   ├── App.razor                   # Root component / HTML host
│   │   ├── Routes.razor
│   │   ├── _Imports.razor
│   │   ├── Layout/
│   │   │   └── MainLayout.razor
│   │   └── Pages/
│   │       └── Dashboard.razor         # Real-time progress dashboard
│   ├── Configuration/
│   │   └── AppSettings.cs              # Configuration models
│   ├── Extensions/
│   │   └── CustomAgentConfigExtensions.cs  # Agent markdown file parser
│   ├── Models/
│   │   ├── ConversionResult.cs         # Conversion result model
│   │   ├── PipelineInfo.cs             # Pipeline metadata
│   │   └── ValidationResult.cs         # Validation result model
│   ├── Services/
│   │   ├── CopilotServiceBase.cs       # Base class for Copilot services
│   │   ├── CopilotConverterService.cs  # AI conversion (standalone or session-based)
│   │   ├── CopilotValidationService.cs # AI validation (standalone or session-based)
│   │   ├── ParallelPipelineProcessor.cs # Parallel processing orchestrator
│   │   ├── PipelineProgressService.cs  # Bridge between processor and Blazor UI
│   │   ├── PipelineScanner.cs          # Pipeline file discovery
│   │   └── WorkflowWriter.cs           # Output writer
│   ├── Sources/
│   │   ├── AzureDevOpsPipelineSource.cs
│   │   ├── GitLabPipelineSource.cs
│   │   └── JenkinsPipelineSource.cs
│   ├── Utilities/
│   │   ├── FileNameGenerator.cs        # Workflow filename generation
│   │   └── SessionIdSanitizer.cs       # Session ID sanitization
│   └── wwwroot/
│       └── css/
│           └── app.css                 # Dashboard styles
└── README.md
```

## Configuration

The application uses `appsettings.json` for configuration. Settings can be customized:

```json
{
  "Paths": {
    "InputDirectory": "",
    "OutputDirectory": "",
    "SourceFilter": ""
  },
  "Copilot": {
    "Model": "gpt-4.1",
    "Timeout": 120,
    "MaxParallelSessions": 3,
    "ConverterAgentFile": "Agents/pipeline-converter.md",
    "ValidatorAgentFile": "Agents/workflow-validator.md"
  },
  "Conversion": {
    "CreateWorkflowsSubdirectory": true,
    "GenerateValidationReports": true
  },
  "Validation": {
    "CheckSyntax": true,
    "CheckSecurity": true,
    "CheckActionVersions": true,
    "MaxIssuesInConsole": 5
  },
  "Logging": {
    "Verbose": false
  }
}
```

| Section | Key | Description |
|---------|-----|-------------|
| **Paths** | `InputDirectory` | Default input directory (overridden by `-i`) |
| | `OutputDirectory` | Default output directory (overridden by `-o`) |
| | `SourceFilter` | Filter: `GitLab`, `AzureDevOps`, `Jenkins` (optional) |
| **Copilot** | `Model` | Model to use (`gpt-4.1`, `claude-sonnet-4.5`, etc.) |
| | `Timeout` | Timeout in seconds per Copilot operation |
| | `MaxParallelSessions` | Number of concurrent Copilot sessions |
| | `ConverterAgentFile` | Path to converter agent markdown file |
| | `ValidatorAgentFile` | Path to validator agent markdown file |
| **Conversion** | `CreateWorkflowsSubdirectory` | Create `.github/workflows` structure in output |
| | `GenerateValidationReports` | Generate `.validation.md` report files |
| **Validation** | `CheckSyntax` | Validate YAML syntax |
| | `CheckSecurity` | Check for security issues |
| | `CheckActionVersions` | Verify action versions are pinned |
| | `MaxIssuesInConsole` | Max issues shown in console output |
| **Logging** | `Verbose` | Enable verbose logging |

### Parallel Processing

The converter processes multiple pipelines concurrently using independent Copilot sessions:
- Each pipeline gets its own session for both conversion and validation
- `MaxParallelSessions` controls concurrency (default: 3)
- Validation runs in the same session as conversion, maintaining context for better results

When `Paths.InputDirectory` and `Paths.OutputDirectory` are set, you can run the tool without arguments:
```bash
dotnet run
```

Create `appsettings.local.json` for local overrides (ignored by git).

## Custom Agents

Custom Copilot agents are defined using markdown files with YAML front matter. This allows you to customize the conversion and validation behavior without modifying code.

### Agent File Format

```markdown
---
name: my-custom-agent
displayName: My Custom Agent
description: A custom agent for specific conversions
infer: true
---

You are an expert at converting pipelines...

## Your Role
- Analyze source pipelines
- Generate GitHub Actions workflows
- Follow best practices
```

### YAML Front Matter Properties

| Property | Type | Description |
|----------|------|-------------|
| `name` | string | Unique identifier for the agent |
| `displayName` | string | Human-readable name |
| `description` | string | Brief description of the agent's purpose |
| `infer` | bool | Enable AI inference capabilities |

### Loading Custom Agents

Agents are loaded automatically from the paths specified in `appsettings.json` (`ConverterAgentFile` and `ValidatorAgentFile`). You can also load them programmatically:

```csharp
using PipelineConverter.Extensions;
using GitHub.Copilot.SDK;

// Load agent from markdown file
var agent = CustomAgentConfigExtensions.FromMarkdownFile("Agents/my-agent.md");

// Use with CopilotConverterService
var service = CopilotConverterService.WithAgentFromFile("gpt-4.1", 120, "Agents/my-agent.md");
```

See the included agent files in `src/Agents/` for examples.

## Extending with New Sources

To add support for a new pipeline source:

1. Create a new class implementing `IPipelineSource`:

```csharp
public class MyPipelineSource : IPipelineSource
{
    public PipelineType Type => PipelineType.MySource;
    public IReadOnlyList<string> FilePatterns => ["my-pipeline.yml"];
    
    public bool CanHandle(string filePath, string? content = null) { ... }
    public PipelineInfo ExtractInfo(string filePath, string content) { ... }
}
```

2. Add the new type to the `PipelineType` enum in `IPipelineSource.cs`
3. Register the source in `Program.cs`

## Example Output

Converting a GitLab CI pipeline produces:

**Input** (`.gitlab-ci.yml`):
```yaml
stages:
  - build
  - test
  - deploy

build:
  stage: build
  script:
    - npm ci
    - npm run build
```

**Output** (`gitlab-ci.yml`):
```yaml
name: CI/CD Pipeline

on:
  push:
    branches: [main, develop]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: 'npm'
      - run: npm ci
      - run: npm run build
```

## Dependencies

- [GitHub.Copilot.SDK](https://www.nuget.org/packages/GitHub.Copilot.SDK) - GitHub Copilot integration
- [YamlDotNet](https://www.nuget.org/packages/YamlDotNet) - YAML parsing and validation
- [ASP.NET Core (Blazor Server)](https://learn.microsoft.com/aspnet/core/blazor) - Live dashboard UI

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
