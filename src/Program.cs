using Microsoft.Extensions.Configuration;
using PipelineConverter.Abstractions;
using PipelineConverter.Configuration;
using PipelineConverter.Models;
using PipelineConverter.Services;
using PipelineConverter.Sources;
using PipelineConverter.Components;
using System.Diagnostics;

// Load configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .Build();

var appSettings = new AppSettings();
configuration.Bind(appSettings);

// Parse command line arguments
var arguments = ParseArguments(args);

// Apply config defaults if command line args not provided
arguments.InputPath ??= string.IsNullOrEmpty(appSettings.Paths.InputDirectory) 
    ? null 
    : appSettings.Paths.InputDirectory;
arguments.OutputPath ??= string.IsNullOrEmpty(appSettings.Paths.OutputDirectory) 
    ? null 
    : appSettings.Paths.OutputDirectory;
arguments.SourceFilter ??= string.IsNullOrEmpty(appSettings.Paths.SourceFilter) 
    ? null 
    : Enum.TryParse<PipelineType>(appSettings.Paths.SourceFilter, ignoreCase: true, out var configSource) 
        ? configSource 
        : null;

// Apply max sessions override from command line
if (arguments.MaxSessions.HasValue)
{
    appSettings.Copilot.MaxParallelSessions = arguments.MaxSessions.Value;
}

if (arguments.ShowHelp || arguments.InputPath is null || arguments.OutputPath is null)
{
    ShowHelp();
    return arguments.ShowHelp ? 0 : 1;
}

// Command line verbose flag overrides config
var verbose = arguments.Verbose || appSettings.Logging.Verbose;

// Optionally start a Blazor Server dashboard
WebApplication? webApp = null;
PipelineProgressService? progressService = null;

if (arguments.Port.HasValue)
{
    var builder = WebApplication.CreateBuilder();
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddSingleton<PipelineProgressService>();
    builder.WebHost.UseUrls($"http://localhost:{arguments.Port.Value}");
    builder.WebHost.UseStaticWebAssets();
    // Suppress default ASP.NET Core console logging noise
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    webApp = builder.Build();
    webApp.UseStaticFiles();
    webApp.UseAntiforgery();
    webApp.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    await webApp.StartAsync();
    progressService = webApp.Services.GetRequiredService<PipelineProgressService>();

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Dashboard available at http://localhost:{arguments.Port.Value}");
    Console.ResetColor();
    Console.WriteLine();
}

await RunConversionAsync(
    new DirectoryInfo(arguments.InputPath),
    new DirectoryInfo(arguments.OutputPath),
    arguments.SourceFilter,
    arguments.SkipValidation,
    arguments.SkipAnalysis,
    verbose,
    appSettings,
    progressService,
    CancellationToken.None);

if (webApp is not null)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"Dashboard is still running at http://localhost:{arguments.Port!.Value}");
    Console.WriteLine("Press Ctrl+C to stop.");
    Console.ResetColor();
    await webApp.WaitForShutdownAsync();
}

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

            case "--skip-analysis":
                result.SkipAnalysis = true;
                break;
                
            case "-v":
            case "--verbose":
                result.Verbose = true;
                break;
                
            case "-h":
            case "--help":
                result.ShowHelp = true;
                break;
                
            case "-m":
            case "--max-sessions":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var maxSessions))
                    result.MaxSessions = maxSessions;
                break;

            case "-p":
            case "--port":
                if (i + 1 < args.Length && int.TryParse(args[++i], out var port))
                    result.Port = port;
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

Required (or set in appsettings.json):
  -i, --input <path>      Directory containing pipeline files to convert
  -o, --output <path>     Output directory for converted workflows

Options:
  -s, --source <type>     Filter to specific source (GitLab, AzureDevOps, Jenkins)
  -m, --max-sessions <n>  Maximum parallel Copilot sessions (default: 3)
  -p, --port <port>       Start a web dashboard on the given port
  --skip-validation       Skip validation step after conversion
  --skip-analysis         Skip pre-conversion analysis step
  -v, --verbose           Enable verbose output
  -h, --help              Show this help message

Configuration:
  Settings can be defined in appsettings.json including default paths.
  Command line arguments override configuration file settings.
  MaxParallelSessions controls concurrent pipeline processing (default: 3).

Examples:
  PipelineConverter -i ./pipelines -o ./converted
  PipelineConverter -i ./ci -o ./output -s GitLab --verbose
  PipelineConverter -i ./ci -o ./output -p 5050   # With live dashboard
  PipelineConverter                     # Uses paths from appsettings.json
");
}

// Main conversion logic using parallel sessions
async Task RunConversionAsync(
    DirectoryInfo input,
    DirectoryInfo output,
    PipelineType? sourceFilter,
    bool skipValidation,
    bool skipAnalysis,
    bool verbose,
    AppSettings settings,
    PipelineProgressService? progressService,
    CancellationToken cancellationToken)
{
    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║        Pipeline to GitHub Actions Converter (Copilot)        ║");
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

    // Create scanner with optional verbose logging for skipped files
    Action<string, Exception>? onFileSkipped = verbose
        ? (filePath, ex) =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Skipped: {Path.GetFileName(filePath)} - {ex.Message}");
            Console.ResetColor();
        }
        : null;

    var scanner = new PipelineScanner(sources.ToList(), onFileSkipped);
    var writer = new WorkflowWriter(output.FullName, settings.Conversion.CreateWorkflowsSubdirectory);

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

    // Initialize parallel processor
    Console.WriteLine($"Initializing GitHub Copilot (max {settings.Copilot.MaxParallelSessions} parallel sessions)...");
    
    await using var processor = await ParallelPipelineProcessor.CreateAsync(settings);

    try
    {
        await processor.StartAsync(cancellationToken);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Copilot connected successfully.");
        Console.ResetColor();
        Console.WriteLine($"Using model: {settings.Copilot.Model}");
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
    // Feed discovered pipelines to the dashboard (if running)
    progressService?.SetPipelines(pipelines);

    Console.WriteLine($"Processing {pipelines.Count} pipeline(s) in parallel...");
    Console.WriteLine();

    // Sequential progress log — avoids cursor manipulation issues with scrolling/resizing
    var progressLock = new object();

    var progress = new SynchronousProgress<ProcessingProgress>(p =>
    {
        // Push update to web dashboard (if running)
        progressService?.Update(p);

        // Only log meaningful phase transitions to keep output concise
        var (icon, label) = p.Phase switch
        {
            ProcessingPhase.Analyzing => ("🔎", "Analyzing"),
            ProcessingPhase.Converting => ("🔄", "Converting"),
            ProcessingPhase.Validating => ("🛡️", "Validating"),
            ProcessingPhase.Complete => ("✅", "Complete"),
            ProcessingPhase.Failed => ("❌", $"Failed: {p.Message}"),
            _ => (null, null)
        };

        if (icon is null) return;

        lock (progressLock)
        {
            var name = Path.GetFileName(p.Pipeline.FilePath);
            Console.WriteLine($"  {icon} [{name}] {label}");
        }
    });

    var stopwatch = Stopwatch.StartNew();
    
    // Process all pipelines in parallel
    var results = await processor.ProcessAsync(
        pipelines,
        writer,
        skipValidation,
        skipAnalysis,
        progress,
        cancellationToken);

    var totalDuration = stopwatch.Elapsed;

    // Display results
    Console.WriteLine();
    Console.WriteLine(new string('═', 60));
    Console.WriteLine("RESULTS");
    Console.WriteLine(new string('═', 60));

    var successCount = 0;
    var failCount = 0;
    var blockedCount = 0;

    foreach (var result in results)
    {
        var name = Path.GetFileName(result.Pipeline.FilePath);
        Console.WriteLine();
        Console.WriteLine(new string('─', 60));
        Console.WriteLine($"Pipeline: {name}");
        Console.WriteLine($"  Source: {result.Pipeline.SourceType}");
        Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F1}s");

        if (result.Conversion.IsSuccess)
        {
            // Show analysis results if available
            if (result.Analysis is not null)
            {
                Console.ForegroundColor = result.Analysis.ComplexityScore switch
                {
                    ComplexityLevel.Low => ConsoleColor.Green,
                    ComplexityLevel.Medium => ConsoleColor.Yellow,
                    ComplexityLevel.High => ConsoleColor.Red,
                    ComplexityLevel.Critical => ConsoleColor.DarkRed,
                    _ => ConsoleColor.Gray
                };
                Console.WriteLine($"  Analysis: {result.Analysis.ComplexityScore} complexity");
                Console.ResetColor();

                if (verbose && !string.IsNullOrWhiteSpace(result.Analysis.ComplexityJustification))
                {
                    Console.WriteLine($"    {result.Analysis.ComplexityJustification}");
                }

                if (result.Analysis.RiskItems?.Count > 0)
                {
                    var riskErrors = result.Analysis.RiskItems.Count(r => r.Severity == ValidationSeverity.Error);
                    var riskWarnings = result.Analysis.RiskItems.Count(r => r.Severity == ValidationSeverity.Warning);
                    Console.WriteLine($"  Risks: {riskErrors} error(s), {riskWarnings} warning(s)");
                }

                if (result.Analysis.UnsupportedFeatures?.Count > 0)
                {
                    Console.WriteLine($"  Unsupported features: {result.Analysis.UnsupportedFeatures.Count}");
                }

                if (verbose && result.AnalysisReportPath is not null)
                {
                    Console.WriteLine($"  Analysis report: {Path.GetRelativePath(output.FullName, result.AnalysisReportPath)}");
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  Conversion: OK");
            Console.ResetColor();

            if (result.WorkflowPath is not null)
            {
                Console.WriteLine($"  Output: {Path.GetRelativePath(output.FullName, result.WorkflowPath)}");
            }

            if (verbose && result.Conversion.Notes?.Count > 0)
            {
                Console.WriteLine("  Notes:");
                foreach (var note in result.Conversion.Notes.Take(5))
                {
                    Console.WriteLine($"    - {note}");
                }
            }

            if (result.Validation is not null)
            {
                var errorCount = result.Validation.Issues.Count(i => i.Severity == ValidationSeverity.Error);
                var warnCount = result.Validation.Issues.Count(i => i.Severity == ValidationSeverity.Warning);

                if (result.Validation.IsValid)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  Validation: PASSED");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  Validation: ISSUES FOUND");
                    Console.ResetColor();
                }

                if (errorCount > 0 || warnCount > 0)
                {
                    Console.WriteLine($"  Issues: {errorCount} error(s), {warnCount} warning(s)");
                }

                if (verbose && result.Validation.Issues.Count > 0)
                {
                    foreach (var issue in result.Validation.Issues.Take(settings.Validation.MaxIssuesInConsole))
                    {
                        var icon = issue.Severity switch
                        {
                            ValidationSeverity.Error => "❌",
                            ValidationSeverity.Warning => "⚠️",
                            _ => "ℹ️"
                        };
                        Console.WriteLine($"    {icon} {issue.Message}");
                    }
                }

                if (result.ValidationReportPath is not null && verbose)
                {
                    Console.WriteLine($"  Report: {Path.GetRelativePath(output.FullName, result.ValidationReportPath)}");
                }

                if (result.ValidatedWorkflowPath is not null)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"  Validated output: {Path.GetRelativePath(output.FullName, result.ValidatedWorkflowPath)}");
                    Console.ResetColor();
                }

                if (verbose && result.Validation.Suggestions?.Count > 0)
                {
                    Console.WriteLine("  Suggestions:");
                    foreach (var suggestion in result.Validation.Suggestions.Take(3))
                    {
                        Console.WriteLine($"    💡 {suggestion}");
                    }
                }
            }

            successCount++;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Conversion: FAILED");
            Console.WriteLine($"  Error: {result.Conversion.ErrorMessage}");
            Console.ResetColor();

            // Show analysis info even for failed/blocked pipelines
            if (result.Analysis is not null)
            {
                if (!result.Analysis.CanProceed)
                {
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    Console.WriteLine($"  Blocked by analysis: {result.Analysis.ComplexityJustification}");
                    Console.ResetColor();
                    blockedCount++;
                }

                if (verbose && result.AnalysisReportPath is not null)
                {
                    Console.WriteLine($"  Analysis report: {Path.GetRelativePath(output.FullName, result.AnalysisReportPath)}");
                }
            }

            if (verbose && result.Error is not null)
            {
                Console.WriteLine($"  Stack: {result.Error.StackTrace}");
            }

            failCount++;
        }
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

    if (blockedCount > 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkRed;
        Console.WriteLine($"  Blocked by analysis: {blockedCount}");
        Console.ResetColor();
    }

    Console.WriteLine($"  Total time: {totalDuration.TotalSeconds:F1}s");
    Console.WriteLine($"  Parallel sessions: {settings.Copilot.MaxParallelSessions}");
    Console.WriteLine();
    Console.WriteLine($"Output directory: {writer.WorkflowsDirectory}");
}

// Command line arguments
class CommandLineArgs
{
    public string? InputPath { get; set; }
    public string? OutputPath { get; set; }
    public PipelineType? SourceFilter { get; set; }
    public int? MaxSessions { get; set; }
    public int? Port { get; set; }
    public bool SkipValidation { get; set; }
    public bool SkipAnalysis { get; set; }
    public bool Verbose { get; set; }
    public bool ShowHelp { get; set; }
}

/// <summary>
/// IProgress implementation that invokes the handler synchronously on the calling thread,
/// avoiding the race condition where Progress&lt;T&gt; posts callbacks to the ThreadPool
/// that can fire after processing completes.
/// </summary>
class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
