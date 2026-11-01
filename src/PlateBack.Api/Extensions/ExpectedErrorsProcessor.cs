using OpenTelemetry;
using OpenTelemetry.Logs;

namespace PlateBack.Api.Extensions;

/// <summary>
/// Keeps Application Insights' Failures view for real faults (Week 6). The
/// request log records expected answers (a wrong password, a used pickup code)
/// at Information level but with their exception attached, and Application
/// Insights files any log carrying an exception as a failure. Below Warning
/// the exception is dropped before export; the log line itself is kept.
/// </summary>
internal sealed class ExpectedErrorsProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        if (data.LogLevel < LogLevel.Warning)
        {
            data.Exception = null;
        }
    }
}
