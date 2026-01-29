# Pipeline to GitHub Actions Converter

A .NET 10 console application that converts CI/CD pipelines from GitLab, Azure DevOps, and Jenkins to GitHub Actions using the [GitHub Copilot SDK](https://github.com/github/copilot-sdk).

## Features

- **Multi-source support**: Convert pipelines from GitLab CI, Azure DevOps, and Jenkins
- **AI-powered conversion**: Uses GitHub Copilot to intelligently map pipeline constructs to GitHub Actions
- **Validation agent**: Custom Copilot agent validates generated workflows for:
  - YAML syntax correctness
  - GitHub Actions structure requirements
  - Security best practices
  - Action version pinning
- **Extensible architecture**: `IPipelineSource` interface allows easy addition of new pipeline sources
- **Detailed reports**: Generates validation reports with suggestions for improvements

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) installed and authenticated
- Active GitHub Copilot subscription

## Installation

```bash
git clone https://github.com/yourusername/ghcp-action-importer.git
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
src/PipelineConverter/
├── Abstractions/
│   └── IPipelineSource.cs          # Interface for pipeline sources
├── Agents/
│   └── CopilotValidationAgent.cs   # Custom validation agent
├── Configuration/
│   └── AppSettings.cs              # Configuration models
├── Models/
│   ├── ConversionResult.cs         # Conversion result model
│   ├── PipelineInfo.cs             # Pipeline metadata
│   └── ValidationResult.cs         # Validation result model
├── Services/
│   ├── CopilotConverterService.cs  # AI conversion service
│   ├── PipelineScanner.cs          # Pipeline file discovery
│   └── WorkflowWriter.cs           # Output writer
├── Sources/
│   ├── AzureDevOpsPipelineSource.cs
│   ├── GitLabPipelineSource.cs
│   └── JenkinsPipelineSource.cs
├── appsettings.json                # Configuration file
└── Program.cs                      # CLI entry point
```

## Configuration

The application uses `appsettings.json` for configuration. Settings can be customized:

```json
{
  "Copilot": {
    "Model": "gpt-4.1",        // Model to use (gpt-4.1, claude-sonnet-4.5, etc.)
    "Timeout": 120              // Timeout in seconds
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

Create `appsettings.local.json` for local overrides (ignored by git).

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

## License

MIT

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
