using System.Text;
using Workboard.Application.Attachments;
using Workboard.Application.Common;

namespace Workboard.Tests;

public class AttachmentFileTests
{
    internal static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==");

    [Fact]
    public void AllowedSignaturesUnicodeNamesAndTextKeepOriginalBytes()
    {
        // Arrange
        var cases = new[]
        {
            (Name: "Šaltinis.PDF", Bytes: Encoding.UTF8.GetBytes("%PDF-1.4\n%%EOF\n"), Type: "application/pdf"),
            (Name: "Tiny.png", Bytes: Png, Type: "image/png"),
            (Name: "Image.JPEG", Bytes: new byte[] { 0xff, 0xd8, 0xff, 0xe0, 0xff, 0xd9 }, Type: "image/jpeg"),
            (Name: "Notes with spaces.txt", Bytes: Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Ąžuolas\r\n\tNotes")).ToArray(), Type: "text/plain"),
            (Name: "Data.csv", Bytes: Encoding.UTF8.GetBytes("name,value\nOne,2\n"), Type: "text/csv"),
            (Name: new string('n', 251) + ".txt", Bytes: new byte[] { 65 }, Type: "text/plain")
        };

        // Act
        foreach (var item in cases)
        {
            var before = item.Bytes.ToArray();
            var result = AttachmentFile.Validate(item.Name, item.Bytes);

            // Assert
            Assert.Equal(OperationStatus.Success, result.Status);
            Assert.Equal(item.Type, result.Value);
            Assert.Equal(before, item.Bytes);
        }
    }

    [Fact]
    public void InvalidNameTypeSignatureEncodingAndSizeAreRejected()
    {
        // Arrange
        var text = Encoding.UTF8.GetBytes("Text");
        var names = new[] { "", " ", ".", "..", "../file.txt", "C:\\file.txt", "part/file.txt", "quote\".txt", "a|b.txt", "a?.txt", "nul\0.txt", "line\n.txt", new string('x', 252) + ".txt", "run.exe", "script.svg", "web.html" };
        var invalidPng = Png; Array.Clear(invalidPng, 16, 4);
        var contents = new[]
        {
            (Name: "empty.txt", Bytes: Array.Empty<byte>()),
            (Name: "huge.txt", Bytes: new byte[AttachmentFile.MaxBytes + 1]),
            (Name: "fake.pdf", Bytes: text),
            (Name: "no-eof.pdf", Bytes: Encoding.UTF8.GetBytes("%PDF-1.4")),
            (Name: "zero-width.png", Bytes: invalidPng),
            (Name: "truncated.png", Bytes: Png[..^1]),
            (Name: "truncated.jpg", Bytes: new byte[] { 0xff, 0xd8, 0xff, 0xe0 }),
            (Name: "invalid.txt", Bytes: new byte[] { 0xc3, 0x28 }),
            (Name: "nul.csv", Bytes: Encoding.UTF8.GetBytes("A\0B")),
            (Name: "control.txt", Bytes: new byte[] { 27, 65 })
        };

        // Act
        var nameResults = names.Select(name => AttachmentFile.Validate(name, text)).ToList();
        var contentResults = contents.Select(item => AttachmentFile.Validate(item.Name, item.Bytes)).ToList();

        // Assert
        Assert.All(nameResults.Concat(contentResults), result => Assert.Equal(OperationStatus.Invalid, result.Status));
        Assert.Equal(OperationStatus.Invalid, AttachmentFile.Validate(null, text).Status);
        Assert.Equal(OperationStatus.Invalid, AttachmentFile.Validate("file.txt", null).Status);
    }

    [Fact]
    public async Task BoundedReaderUsesActualBytesAndRejectsTruncationReadFailureAndCancellation()
    {
        // Arrange
        var bytes = Enumerable.Repeat((byte)'a', AttachmentFile.MaxBytes).ToArray();
        await using var exact = new AttachmentReadStream(bytes);
        await using var oversized = new AttachmentReadStream(bytes.Concat(new byte[100]).ToArray());
        await using var shortFile = new AttachmentReadStream(new byte[] { 65, 66, 67 });
        await using var failed = new AttachmentReadStream(new byte[10], 3, true);
        await using var cancelled = new AttachmentReadStream(bytes);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();

        // Act
        var accepted = await AttachmentFile.ReadAsync(exact, bytes.Length, default);
        var tooLarge = await AttachmentFile.ReadAsync(oversized, 1, default);
        var truncated = await AttachmentFile.ReadAsync(shortFile, 99, default);
        var readFailure = await AttachmentFile.ReadAsync(failed, 10, default);

        // Assert
        Assert.Equal(OperationStatus.Success, accepted.Status);
        Assert.Equal(bytes, accepted.Value);
        Assert.Equal(AttachmentFile.MaxBytes, exact.BytesRead);
        Assert.Equal(OperationStatus.Invalid, tooLarge.Status);
        Assert.Equal(AttachmentFile.MaxBytes + 1, oversized.BytesRead);
        Assert.Equal(OperationStatus.Invalid, truncated.Status);
        Assert.Equal(OperationStatus.Invalid, readFailure.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AttachmentFile.ReadAsync(cancelled, bytes.Length, cancellation.Token));
    }
}

internal class AttachmentReadStream(byte[] contents, int chunk = 65536, bool failAfterFirstRead = false) : MemoryStream(contents)
{
    public int BytesRead { get; private set; }
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (failAfterFirstRead && BytesRead > 0) throw new IOException("Interrupted read.");
        var read = await base.ReadAsync(buffer[..Math.Min(chunk, buffer.Length)], cancellationToken);
        BytesRead += read;
        return read;
    }
}
