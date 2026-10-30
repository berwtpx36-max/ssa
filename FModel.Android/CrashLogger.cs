using System.Text;

namespace FModel.Android;

internal static class CrashLogger
{
    private static readonly object Sync = new();
    private static string? _logPath;
    private static int _initialized;

    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        var context = global::Android.App.Application.Context;
        var directory = context.GetExternalFilesDir(null)?.AbsolutePath ?? context.FilesDir?.AbsolutePath;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _logPath = Path.Combine(directory, "crash.log");
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
            }
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public static string LogException(string operation, Exception exception)
    {
        var details = FormatException(exception);
        AppendLog($"[{DateTimeOffset.Now:O}] {operation}{Environment.NewLine}{details}");
        return details;
    }

    private static string FormatException(Exception exception)
    {
        var output = new StringBuilder();
        AppendException(output, exception, 0);
        return output.ToString();
    }

    private static void AppendException(StringBuilder output, Exception exception, int depth)
    {
        var indent = new string(' ', depth * 2);
        output.AppendLine($"{indent}Tür: {exception.GetType().FullName}");
        output.AppendLine($"{indent}Mesaj: {exception.Message}");
        output.AppendLine($"{indent}StackTrace: {exception.StackTrace ?? "<yok>"}");

        if (exception is AggregateException aggregate)
        {
            if (aggregate.InnerExceptions.Count == 0)
                output.AppendLine($"{indent}InnerException: <yok>");

            foreach (var innerException in aggregate.InnerExceptions)
            {
                output.AppendLine($"{indent}InnerException:");
                AppendException(output, innerException, depth + 1);
            }
        }
        else if (exception.InnerException is not null)
        {
            output.AppendLine($"{indent}InnerException:");
            AppendException(output, exception.InnerException, depth + 1);
        }
        else
        {
            output.AppendLine($"{indent}InnerException: <yok>");
        }
    }

    private static void AppendLog(string entry)
    {
        if (_logPath is null)
            return;

        try
        {
            lock (Sync)
                File.AppendAllText(_logPath, entry + Environment.NewLine + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            LogException($"UnhandledException (IsTerminating: {args.IsTerminating})", exception);
        else
            AppendLog($"[{DateTimeOffset.Now:O}] UnhandledException: {args.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        LogException("UnobservedTaskException", args.Exception);
        args.SetObserved();
    }
}