using ProjectManagement.Domain.Enums;
using ProjectManagement.Web.Services;

namespace ProjectManagement.Tests;

public class DocumentRenderingTests
{
    [Fact]
    public void MarkdownRendersStructureWithoutActiveHtmlOrImageRequests()
    {
        // Arrange
        const string source = "# Delivery notes\n\n- Check **barcode**\n- Keep counts\n\n<script>alert(1)</script>\n\n![private image](https://tracking.test/pixel)\n\n[unsafe](javascript:alert%281%29) [data](data:text/html,bad) [file](file:///etc/passwd) [relative](/Account/Logout) <javascript:alert(1)>\n\n[guide](https://example.test/guide?q=1&b=2) <reader@example.test>";

        // Act
        var rendered = DocumentRenderer.Render(source, DocumentFormat.Markdown);
        var plain = DocumentRenderer.Render(source, DocumentFormat.PlainText);
        var unknown = DocumentRenderer.Render(source, (DocumentFormat)99);

        // Assert
        Assert.Contains("<h1>Delivery notes</h1>", rendered);
        Assert.Contains("<ul>", rendered);
        Assert.Contains("<strong>barcode</strong>", rendered);
        Assert.Contains("&lt;script&gt;", rendered);
        Assert.DoesNotContain("<script", rendered);
        Assert.DoesNotContain("<img", rendered);
        Assert.DoesNotContain("href=\"javascript:", rendered);
        Assert.DoesNotContain("href=\"data:", rendered);
        Assert.DoesNotContain("href=\"file:", rendered);
        Assert.DoesNotContain("href=\"/", rendered);
        Assert.Contains("href=\"https://example.test/guide", rendered);
        Assert.Contains("href=\"mailto:reader@example.test", rendered);
        Assert.DoesNotContain("<h1>", plain);
        Assert.Equal(plain, unknown);
    }
}
