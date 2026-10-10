using System.Globalization;
using System.Text;
using BookingApp.Core.Notifications;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Notifications;

/// <summary>The "Email" settings for the file sender.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Where .eml files go. Relative paths are resolved against the API's folder.</summary>
    public string Folder { get; set; } = ".data/emails";

    public string From { get; set; } = "Riverside Sports Club <bookings@riverside.example>";

    /// <summary>Development: how long each send takes, so the API can be stopped in the middle of one.</summary>
    public double DelaySeconds { get; set; }
}

/// <summary>
/// A fake mail server: each email becomes an .eml file (open it in any mail program). It is an idempotent receiver:
/// the file is named after the message id and published with a move that never overwrites, so the same message sent
/// twice (the relay crashed after sending, before recording it) is delivered once (library 14.2, 12.2 "Idempotent
/// receiver").
/// </summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options, TimeProvider time) : IEmailSender
{
    public string Folder => options.Value.Folder;

    /// <summary>Tests: runs after the file is written and before the send returns, such as a crash before it is recorded.</summary>
    public Action<EmailMessage>? AfterWrite { get; set; }

    /// <summary>Tests: throws instead of sending, as an unreachable mail server would.</summary>
    public Func<EmailMessage, Exception?>? Fail { get; set; }

    public static string FileName(EmailMessage m) => $"{m.Date.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{m.Id:N}.eml";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail?.Invoke(message) is { } failure) throw failure;
        if (options.Value.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(options.Value.DelaySeconds), time, cancellationToken);

        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, FileName(message));
        if (File.Exists(path)) return;   // delivered before: a repeat

        var temp = Path.Combine(Folder, $".{message.Id:N}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temp, Render(message, options.Value.From), new UTF8Encoding(false), cancellationToken);
        try
        {
            File.Move(temp, path, overwrite: false);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(temp);   // another attempt delivered it at the same moment
        }
        AfterWrite?.Invoke(message);
    }

    /// <summary>RFC 5322 text. Header values never carry a line break (no header injection); non-ASCII subjects are encoded.</summary>
    public static string Render(EmailMessage m, string from)
    {
        var headers = new StringBuilder()
            .Append("Message-ID: <").Append(m.Id.ToString("N")).Append("@booking-app>\r\n")
            .Append("Date: ").Append(m.Date.ToString("ddd, dd MMM yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("From: ").Append(OneLine(from)).Append("\r\n")
            .Append("To: ").Append(OneLine(m.To)).Append("\r\n")
            .Append("Subject: ").Append(EncodeWord(OneLine(m.Subject))).Append("\r\n")
            .Append("MIME-Version: 1.0\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n")
            .Append("Content-Transfer-Encoding: 8bit\r\n\r\n");
        return headers.Append(m.Body.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n").ToString();
    }

    private static string OneLine(string value) => value.Replace("\r", " ").Replace("\n", " ");

    private static string EncodeWord(string value) =>
        value.All(c => c < 128) ? value : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
}
