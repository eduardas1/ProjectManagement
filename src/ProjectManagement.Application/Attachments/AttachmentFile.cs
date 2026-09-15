using System.Buffers.Binary;
using System.Text;
using ProjectManagement.Application.Common;

namespace ProjectManagement.Application.Attachments;

public static class AttachmentFile
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int TransportLimit = 6 * 1024 * 1024;

    public static async Task<OperationResult<byte[]>> ReadAsync(Stream stream, long expectedLength, CancellationToken cancellationToken)
    {
        if (expectedLength < 0 || expectedLength > MaxBytes)
            return OperationResult<byte[]>.Invalid("File", "Select a file no larger than 5 MiB.");
        try
        {
            using var contents = new MemoryStream((int)Math.Min(expectedLength, 81920));
            var buffer = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, MaxBytes + 1 - (int)contents.Length)), cancellationToken);
                if (read == 0) break;
                await contents.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (contents.Length > MaxBytes) return OperationResult<byte[]>.Invalid("File", "Select a file no larger than 5 MiB.");
            }
            if (contents.Length == 0) return OperationResult<byte[]>.Invalid("File", "Select a nonempty file.");
            if (contents.Length != expectedLength) return OperationResult<byte[]>.Invalid("File", "The file could not be read completely. Select it again.");
            return OperationResult<byte[]>.Success(contents.ToArray());
        }
        catch (IOException)
        {
            return OperationResult<byte[]>.Invalid("File", "The file could not be read. Select it again.");
        }
    }

    public static OperationResult<string> Validate(string? displayName, byte[]? contents)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 255 || displayName is "." or ".."
            || displayName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            return OperationResult<string>.Invalid("File", "Use a filename of 1 to 255 characters without paths or reserved filename characters.");
        if (contents == null || contents.Length is < 1 or > MaxBytes)
            return OperationResult<string>.Invalid("File", "Select a nonempty file no larger than 5 MiB.");
        var extension = Path.GetExtension(displayName).ToLowerInvariant();
        var contentType = extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            _ => null
        };
        if (contentType == null) return OperationResult<string>.Invalid("File", "Allowed files: PDF, PNG, JPEG, UTF-8 text and CSV.");
        var valid = extension switch
        {
            ".pdf" => contents.AsSpan().StartsWith("%PDF-"u8)
                && contents.AsSpan(Math.Max(0, contents.Length - 1024)).IndexOf("%%EOF"u8) >= 0,
            ".png" => IsPng(contents),
            ".jpg" or ".jpeg" => contents.Length >= 5 && contents[0] == 0xff && contents[1] == 0xd8
                && contents[2] == 0xff && contents[^2] == 0xff && contents[^1] == 0xd9,
            _ => IsText(contents)
        };
        return valid ? OperationResult<string>.Success(contentType)
            : OperationResult<string>.Invalid("File", "The file content does not match its type or encoding.");
    }

    private static bool IsPng(byte[] contents)
        => contents.Length >= 45 && contents.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && BinaryPrimitives.ReadUInt32BigEndian(contents.AsSpan(8, 4)) == 13
            && contents.AsSpan(12, 4).SequenceEqual("IHDR"u8)
            && BinaryPrimitives.ReadUInt32BigEndian(contents.AsSpan(16, 4)) > 0
            && BinaryPrimitives.ReadUInt32BigEndian(contents.AsSpan(20, 4)) > 0
            && contents.AsSpan(contents.Length - 12).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 });

    private static bool IsText(byte[] contents)
    {
        try
        {
            var text = new UTF8Encoding(false, true).GetString(contents);
            return !text.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t');
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
