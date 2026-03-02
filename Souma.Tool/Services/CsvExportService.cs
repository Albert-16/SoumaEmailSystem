using System.Text;
using Souma.EmailLogging.Models;

namespace Souma.Tool.Services;

/// <summary>
/// Servicio para exportar logs de email a formato CSV.
/// </summary>
public static class CsvExportService
{
    /// <summary>Genera contenido CSV a partir de una lista de logs.</summary>
    public static byte[] GenerateCsv(List<EmailLogDto> logs)
    {
        StringBuilder sb = new();

        // Encabezados
        sb.AppendLine("MessageId,CorrelationId,SourceMicroservice,SenderAddress,RecipientAddresses,CcAddresses,Subject,Status,StatusMessage,SentAtUtc,DurationMs,RetryCount,HasAttachments,Environment");

        foreach (EmailLogDto log in logs)
        {
            sb.Append(log.MessageId).Append(',');
            sb.Append(EscapeCsv(log.CorrelationId)).Append(',');
            sb.Append(EscapeCsv(log.SourceMicroservice)).Append(',');
            sb.Append(EscapeCsv(log.SenderAddress)).Append(',');
            sb.Append(EscapeCsv(string.Join("; ", log.RecipientAddresses))).Append(',');
            sb.Append(EscapeCsv(log.CcAddresses is not null ? string.Join("; ", log.CcAddresses) : "")).Append(',');
            sb.Append(EscapeCsv(log.Subject)).Append(',');
            sb.Append(log.Status).Append(',');
            sb.Append(EscapeCsv(log.StatusMessage)).Append(',');
            sb.Append(log.SentAtUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append(',');
            sb.Append(log.DurationMs).Append(',');
            sb.Append(log.RetryCount).Append(',');
            sb.Append(log.HasAttachments).Append(',');
            sb.AppendLine(log.Environment.ToString());
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
