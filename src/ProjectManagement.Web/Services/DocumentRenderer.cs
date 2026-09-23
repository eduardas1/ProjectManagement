using System.Text.Encodings.Web;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax.Inlines;
using ProjectManagement.Domain.Enums;

namespace ProjectManagement.Web.Services;

public static class DocumentRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    public static string Render(string body, DocumentFormat format)
    {
        if (format != DocumentFormat.Markdown) return HtmlEncoder.Default.Encode(body);
        var document = Markdown.Parse(body, Pipeline);
        using var output = new StringWriter();
        var renderer = new HtmlRenderer(output);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Remove(renderer.ObjectRenderers.First(item => item is LinkInlineRenderer));
        renderer.ObjectRenderers.Remove(renderer.ObjectRenderers.First(item => item is AutolinkInlineRenderer));
        renderer.ObjectRenderers.Add(new SafeLinkRenderer());
        renderer.ObjectRenderers.Add(new SafeAutolinkRenderer());
        renderer.Render(document);
        return output.ToString();
    }

    private static string? SafeUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        return uri.Scheme is "http" or "https" or "mailto" ? uri.AbsoluteUri : null;
    }

    private sealed class SafeLinkRenderer : HtmlObjectRenderer<LinkInline>
    {
        protected override void Write(HtmlRenderer renderer, LinkInline link)
        {
            var url = link.IsImage ? null : SafeUrl(link.Url);
            if (url != null) renderer.Write("<a href=\"").WriteEscapeUrl(url).Write("\" rel=\"noreferrer noopener\">");
            renderer.WriteChildren(link);
            if (url != null) renderer.Write("</a>");
        }
    }

    private sealed class SafeAutolinkRenderer : HtmlObjectRenderer<AutolinkInline>
    {
        protected override void Write(HtmlRenderer renderer, AutolinkInline link)
        {
            var url = SafeUrl(link.IsEmail ? "mailto:" + link.Url : link.Url);
            if (url != null) renderer.Write("<a href=\"").WriteEscapeUrl(url).Write("\" rel=\"noreferrer noopener\">");
            renderer.WriteEscape(link.Url);
            if (url != null) renderer.Write("</a>");
        }
    }
}
