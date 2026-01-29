# Pipeline to GitHub Actions Converter

A .NET 10 console application that converts CI/CD pipelines from GitLab, Azure DevOps, and Jenkins to GitHub Actions using the [GitHub Copilot SDK](https://github.com/github/copilot-sdk).

## Features

- **Multi-source support**: Convert pipelines from GitLab CI, Azure DevOps, and Jenkins
- **AI-powered conversion**: Uses GitHub Copilot to intelligently map pipeline constructs to GitHub Actions
- **Custom agents**: Define custom Copilot agents via markdown files with YAML front matter
- **Validation agent**: Custom Copilot agent validates generated workflows for:
  - YAML syntax correctness
  - GitHub Actions structure requirements
  - Security best practices
  - Action version pinning
- **Extensible architecture**: `IPipelineSource` interface allows easy addition of new pipeline sources
- **Detailed reports**: Generates validation reports with suggestions for improvements
- **Improved workflows**: Automatically generates improved versions with best practices applied

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) installed and authenticated
- Active GitHub Copilot subscription

## Installation

```bash
git clone https://github.com/Frank802/ghcp-action-importer.git
cd ghcp-action-importer/src/PipelineConverter
dotnet build
```

## Usage

```bash
# Basic usage
dotnet run -- -i <input-folder> -o <output-folder>

# Convert only GitLab pipelines with verbose output
dotnet run -- -i ./pipelines -o ./converted -s GitLab --verbose

# Skip validation step
dotnet run -- -i ./ci -o ./output --skip-validation
```

### Command Line Options

| Option | Alias | Description |
|--------|-------|-------------|
| `--input` | `-i` | **Required.** Directory containing pipeline files to convert |
| `--output` | `-o` | **Required.** Output directory for converted workflows |
| `--source` | `-s` | Filter to specific source type: `GitLab`, `AzureDevOps`, `Jenkins` |
| `--max-sessions` | `-m` | Maximum parallel Copilot sessions (default: 3) |
| `--skip-validation` | | Skip the validation step after conversion |
| `--verbose` | `-v` | Enable verbose output |
| `--help` | `-h` | Show help message |

## Supported Pipeline Formats

| Source | File Patterns |
|--------|---------------|
| GitLab CI/CD | `.gitlab-ci.yml`, `.gitlab-ci.yaml` |
| Azure DevOps | `azure-pipelines.yml`, `azure-pipelines.yaml` |
| Jenkins | `Jenkinsfile`, `Jenkinsfile.*` |

## Output Structure

Converted workflows are saved to:

```
<output-folder>/
└── .github/
    └── workflows/
        ├── gitlab-ci.yml           # Converted workflow
        ├── gitlab-ci.validation.md # Validation report
        └── gitlab-ci.improved.yml  # Improved version (if suggestions available)
```

## Project Structure

```
ghcp-action-importer/
├── samples/                            # Sample pipeline files for testing
│   ├── .gitlab-ci.yml
│   ├── azure-pipelines.yml
│   └── Jenkinsfile
├── src/PipelineConverter/
│   ├── Abstractions/
│   │   └── IPipelineSource.cs          # Interface for pipeline sources
│   ├── Agents/
│   │   ├── CopilotValidationAgent.cs   # Custom validation agent
│   │   ├── pipeline-converter.md       # Converter agent definition
│   │   └── workflow-validator.md       # Validator agent definition
│   ├── Configuration/
│   │   └── AppSettings.cs              # Configuration models
│   ├── Extensions/
│   │   └── CustomAgentConfigExtensions.cs  # Agent markdown file parser
│   ├── Models/
│   │   ├── ConversionResult.cs         # Conversion result model
│   │   ├── PipelineInfo.cs             # Pipeline metadata
│   │   └── ValidationResult.cs         # Validation result model
│   ├── Services/
│   │   ├── CopilotConverterService.cs  # AI conversion service (single session)
│   │   ├── ParallelPipelineProcessor.cs # Parallel processing with multiple sessions
│   │   ├── PipelineScanner.cs          # Pipeline file discovery
│   │   └── WorkflowWriter.cs           # Output writer
│   ├── Sources/
│   │   ├── AzureDevOpsPipelineSource.cs
│   │   ├── GitLabPipelineSource.cs
│   │   └── JenkinsPipelineSource.cs
│   ├── appsettings.json                # Configuration file
│   └── Program.cs                      # CLI entry point
└── README.md
```

## Configuration

The application uses `appsettings.json` for configuration. Settings can be customized:

```json
{
  "Paths": {
    "InputDirectory": "",          // Default input directory (can be overridden by -i)
    "OutputDirectory": "",         // Default output directory (can be overridden by -o)
    "SourceFilter": ""             // Filter: GitLab, AzureDevOps, Jenkins (optional)
  },
  "Copilot": {
    "Model": "gpt-4.1",            // Model to use (gpt-4.1, claude-sonnet-4.5, etc.)
    "Timeout": 120,                // Timeout in seconds per operation
    "MaxParallelSessions": 3       // Number of concurrent Copilot sessions
  },
  "Conversion": {
    "CreateWorkflowsSubdirectory": true,   // Create .github/workflows structure
    "GenerateValidationReports": true,      // Generate .validation.md files
    "GenerateImprovedWorkflows": true       // Generate .improved.yml files
  },
  "Validation": {
    "CheckSyntax": true,           // Validate YAML syntax
    "CheckSecurity": true,         // Check for security issues
    "CheckActionVersions": true,   // Verify action versions are pinned
    "MaxIssuesInConsole": 5        // Max issues shown in console
  },
  "Logging": {
    "Verbose": false               // Default verbose setting
  }
}
```

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

Custom Copilot agents can be defined using markdown files with YAML front matter. This allows you to customize the conversion and validation behavior without modifying code.

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

```csharp
using PipelineConverter.Extensions;
using GitHub.Copilot;

// Load agent from markdown file
var agent = CustomAgentConfigExtensions.FromMarkdownFile("Agents/my-agent.md");

// Use with CopilotConverterService
var service = CopilotConverterService.WithAgentFromFile(settings, "Agents/my-agent.md");
```

See the included agent files in `src/PipelineConverter/Agents/` for examples.

## Quick Start

Run the converter on the included sample pipelines:

```bash
cd src/PipelineConverter
dotnet run -- -i ../../samples -o ../../output -v
```

This will convert:
- GitLab CI (`.gitlab-ci.yml`) - Node.js build/test/deploy pipeline
- Azure DevOps (`azure-pipelines.yml`) - .NET multi-stage pipeline
- Jenkins (`Jenkinsfile`) - Java Maven with Docker and Kubernetes

Output will be saved to `output/.github/workflows/` with:
- Converted workflow files (`.yml`)
- Validation reports (`.validation.md`)
- Improved versions (`.improved.yml`)


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
- [Microsoft.Extensions.Configuration](https://www.nuget.org/packages/Microsoft.Extensions.Configuration) - Configuration management

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
