using System.IO;
using System.Text;
using System.Windows;

namespace MandelbrotGpu;

internal static class AppDiagnostics
{
    public static string? TryWriteFatalError(Exception exception, string diagnostics)
    {
        string[] candidatePaths =
        [
            Path.Combine(AppContext.BaseDirectory, "error.log"),
            Path.Combine(Environment.CurrentDirectory, "error.log"),
            Path.Combine(Path.GetTempPath(), "MandelbrotGpu-error.log")
        ];

        string payload = BuildPayload(exception, diagnostics);
        foreach (string path in candidatePaths)
        {
            try
            {
                File.WriteAllText(path, payload);
                return path;
            }
            catch
            {
                // Try the next writable location.
            }
        }

        return null;
    }

    public static void ShowFatalError(string title, Exception exception, string diagnostics, string? logPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine(exception.Message);
        builder.AppendLine();
        builder.AppendLine("The application wrote a fatal-error report with runtime diagnostics.");
        if (!string.IsNullOrWhiteSpace(logPath))
            builder.AppendLine($"Log path: {logPath}");
        else
            builder.AppendLine("Log path: unavailable");

        string message = builder.ToString().TrimEnd();
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        Console.Error.WriteLine(diagnostics);

        try
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // If WPF is unavailable during shutdown, the console output and log file remain.
        }
    }

    private static string BuildPayload(Exception exception, string diagnostics)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}");
        builder.AppendLine();
        builder.AppendLine("Diagnostics:");
        builder.AppendLine(diagnostics);
        builder.AppendLine();
        builder.AppendLine("Exception:");
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }
}
