using PipelineConverter.Abstractions;
using PipelineConverter.Agents;
using PipelineConverter.Models;
using PipelineConverter.Services;
using PipelineConverter.Sources;

// Parse command line arguments
var arguments = ParseArguments(args);

if (arguments.ShowHelp || arguments.InputPath is null || arguments.OutputPath is null)
{
    ShowHelp();
    return arguments.ShowHelp ? 0 : 1;
}

await RunConversionAsync(
    new DirectoryInfo(arguments.InputPath),
    new DirectoryInfo(arguments.OutputPath),
    arguments.SourceFilter,
    arguments.SkipValidation,
    arguments.Verbose,
    CancellationToken.None);

return 0;

// Argument parsing
static CommandLineArgs ParseArguments(string[] args)
{
    var result = new CommandLineArgs();
    
    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        
        switch (arg.ToLowerInvariant())
        {
            case "-i":
            case "--input":
                if (i + 1 < args.Length)
                    result.InputPath = args[++i];
                break;
                
            case "-o":
            case "--output":
                if (i + 1 < args.Length)
                    result.OutputPath = args[++i];
                break;
                
            case "-s":
            case "--source":
                if (i + 1 < args.Length)
                {
                    var sourceArg = args[++i];
                    if (Enum.TryParse<PipelineType>(sourceArg, ignoreCase: true, out var sourceType))
                        result.SourceFilter = sourceType;
                }
                break;
                
            case "--skip-validation":
                result.SkipValidation = true;
                break;
                
            case "-v":
            case "--verbose":
                result.Verbose = true;
                break;
                
            case "-h":
            case "--help":
                result.ShowHelp = true;
                break;
        }
    }
    
    return result;
}

static void ShowHelp()
{
    Console.WriteLine(@"
Pipeline to GitHub Actions Converter

Usage: PipelineConverter -i <input> -o <output> [options]

Required:
  -i, --input <path>      Directory containing pipeline files to convert
  -o, --output <path>     Output directory for converted workflows

Options:
  -s, --source <type>     Filter to specific source (GitLab, AzureDevOps, Jenkins)
  --skip-validation       Skip validation step after conversion
  -v, --verbose           Enable verbose output
  -h, --help              Show this help message

Examples:
  PipelineConverter -i ./pipelines -o ./converted
  PipelineConverter -i ./ci -o ./output -s GitLab --verbose
");
}

// Main conversion logic
async Task RunConversionAsync(
    DirectoryInfo input,
    DirectoryInfo output,
    PipelineType? sourceFilter,
    bool skipValidation,
    bool verbose,
    CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║       Pipeline to GitHub Actions Converter (Copilot)         ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
    Console.WriteLine();

    // Validate input directory
    if (!input.Exists)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: Input directory does not exist: {input.FullName}");
        Console.ResetColor();
        return;
    }

    // Create output directory if needed
    if (!output.Exists)
    {
        output.Create();
        if (verbose)
        {
            Console.WriteLine($"Created output directory: {output.FullName}");
        }
    }

    // Initialize pipeline sources
    var sources = new IPipelineSource[]
    {
        new GitLabPipelineSource(),
        new AzureDevOpsPipelineSource(),
        new JenkinsPipelineSource()
    };

    var scanner = new PipelineScanner(sources);
    var writer = new WorkflowWriter(output.FullName);

    // Scan for pipelines
    Console.WriteLine($"Scanning: {input.FullName}");
    if (sourceFilter.HasValue)
    {
        Console.WriteLine($"Filter: {sourceFilter.Value} pipelines only");
    }
    Console.WriteLine();

    var pipelines = await scanner.ScanAsync(input.FullName, sourceFilter, recursive: true, cancellationToken);

    if (pipelines.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("No pipeline files found in the specified directory.");
        Console.ResetColor();
        
        Console.WriteLine();
        Console.WriteLine("Supported file patterns:");
        foreach (var (type, patterns) in scanner.GetSupportedPatterns())
        {
            Console.WriteLine($"  {type}: {string.Join(", ", patterns)}");
        }
        return;
    }

    Console.WriteLine($"Found {pipelines.Count} pipeline(s):");
    foreach (var pipeline in pipelines)
    {
        Console.WriteLine($"  [{pipeline.SourceType}] {Path.GetFileName(pipeline.FilePath)}");
    }
    Console.WriteLine();

    // Initialize Copilot services
    Console.WriteLine("Initializing GitHub Copilot...");
    
    await using var converter = new CopilotConverterService();
    CopilotValidationAgent? validator = null;
    
    if (!skipValidation)
    {
        validator = new CopilotValidationAgent();
    }

    try
    {
        await converter.StartAsync(cancellationToken);
        if (validator is not null)
        {
            await validator.StartAsync(cancellationToken);
        }
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Copilot connected successfully.");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to connect to Copilot: {ex.Message}");
        Console.WriteLine();
        Console.WriteLine("Make sure you have:");
        Console.WriteLine("  1. GitHub Copilot CLI installed and in PATH");
        Console.WriteLine("  2. A valid GitHub Copilot subscription");
        Console.WriteLine("  3. Authenticated with GitHub");
        Console.ResetColor();
        return;
    }

    Console.WriteLine();

    // Process each pipeline
    var successCount = 0;
    var failCount = 0;

    foreach (var pipeline in pipelines)
    {
        Console.WriteLine(new string('─', 60));
        Console.WriteLine($"Converting: {Path.GetFileName(pipeline.FilePath)}");
        Console.WriteLine($"  Source: {pipeline.SourceType}");

        if (verbose)
        {
            Console.WriteLine($"  Path: {pipeline.FilePath}");
        }

        try
        {
            // Convert
            Console.Write("  Converting... ");
            var result = await converter.ConvertAsync(pipeline, cancellationToken);

            if (!result.IsSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("FAILED");
                Console.WriteLine($"  Error: {result.ErrorMessage}");
                Console.ResetColor();
                failCount++;
                continue;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("OK");
            Console.ResetColor();

            // Show notes if any and verbose
            if (verbose && result.Notes?.Count > 0)
            {
                Console.WriteLine("  Notes:");
                foreach (var note in result.Notes)
                {
                    Console.WriteLine($"    - {note}");
                }
            }

            // Write the workflow
            var workflowPath = await writer.WriteAsync(result, pipeline, cancellationToken);
            Console.WriteLine($"  Output: {Path.GetRelativePath(output.FullName, workflowPath)}");

            // Validate if not skipped
            if (validator is not null)
            {
                Console.Write("  Validating... ");
                var validation = await validator.ValidateAsync(
                    pipeline.OriginalContent, 
                    result.WorkflowYaml!, 
                    cancellationToken);

                if (validation.IsValid)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("PASSED");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("ISSUES FOUND");
                    Console.ResetColor();
                }

                var errorCount = validation.Issues.Count(i => i.Severity == ValidationSeverity.Error);
                var warnCount = validation.Issues.Count(i => i.Severity == ValidationSeverity.Warning);

                if (errorCount > 0 || warnCount > 0)
                {
                    Console.WriteLine($"  Issues: {errorCount} error(s), {warnCount} warning(s)");
                }

                if (verbose && validation.Issues.Count > 0)
                {
                    foreach (var issue in validation.Issues.Take(5))
                    {
                        var icon = issue.Severity switch
                        {
                            ValidationSeverity.Error => "❌",
                            ValidationSeverity.Warning => "⚠️",
                            _ => "ℹ️"
                        };
                        Console.WriteLine($"    {icon} {issue.Message}");
                    }
                    if (validation.Issues.Count > 5)
                    {
                        Console.WriteLine($"    ... and {validation.Issues.Count - 5} more");
                    }
                }

                // Write validation report
                var reportPath = await writer.WriteValidationReportAsync(workflowPath, validation, cancellationToken);
                if (verbose)
                {
                    Console.WriteLine($"  Report: {Path.GetRelativePath(output.FullName, reportPath)}");
                }

                // Write improved workflow if available
                var improvedPath = await writer.WriteImprovedWorkflowAsync(workflowPath, validation, cancellationToken);
                if (improvedPath is not null)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  Improved: {Path.GetRelativePath(output.FullName, improvedPath)}");
                    Console.ResetColor();
                }

                if (validation.Suggestions?.Count > 0 && verbose)
                {
                    Console.WriteLine("  Suggestions:");
                    foreach (var suggestion in validation.Suggestions.Take(3))
                    {
                        Console.WriteLine($"    💡 {suggestion}");
                    }
                }
            }

            successCount++;
        }
        catch (OperationCanceledException)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Cancelled.");
            Console.ResetColor();
            break;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            
            if (verbose)
            {
                Console.WriteLine($"  {ex.StackTrace}");
            }
            
            failCount++;
        }
    }

    // Dispose validator if created
    if (validator is not null)
    {
        await validator.DisposeAsync();
    }

    // Summary
    Console.WriteLine();
    Console.WriteLine(new string('═', 60));
    Console.WriteLine();
    Console.WriteLine("Summary:");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Converted: {successCount}");
    Console.ResetColor();
    
    if (failCount > 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  Failed: {failCount}");
        Console.ResetColor();
    }

    Console.WriteLine();
    Console.WriteLine($"Output directory: {writer.WorkflowsDirectory}");
}

// Command line arguments class
class CommandLineArgs
{
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public PipelineType? SourceFilter { get; set; }
    public bool SkipValidation { get; set; }
    public bool Verbose { get; set; }
    public bool ShowHelp { get; set; }
}
