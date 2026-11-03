using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.WebUtilities;
using ProjectManagement.Application.Common;

namespace ProjectManagement.Web.TagHelpers;

[HtmlTargetElement("table", Attributes = "table-options")]
public class TableTagHelper : TagHelper
{
    public TableOptions TableOptions { get; set; } = new();
    public int TablePage { get; set; }
    public int TableTotal { get; set; }
    public bool TableSearch { get; set; }
    public bool TableContext { get; set; }
    [ViewContext, HtmlAttributeNotBound] public ViewContext ViewContext { get; set; } = null!;

    public override void Init(TagHelperContext context) => context.Items[typeof(TableTagHelper)] = this;

    public string Url(params (string Key, string Value)[] changes)
    {
        var query = ViewContext.HttpContext.Request.Query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (var change in changes) query[change.Key] = change.Value;
        return QueryHelpers.AddQueryString(ViewContext.HttpContext.Request.PathBase + ViewContext.HttpContext.Request.Path, query);
    }

    public string PageKey => TableSearch ? "Input.Page" : "page";
    public string SizeKey => TableSearch ? "Input.PageSize" : "pageSize";
    public string LinkAttribute => TableContext ? "data-context-link" : "data-workspace-link";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        output.Attributes.SetAttribute("data-server-table", "");
        output.Content.SetHtmlContent(await output.GetChildContentAsync());
        var pages = TableOptions.Pages(TableTotal);
        var footer = new TagBuilder("div");
        footer.AddCssClass("table-controls");
        var status = new TagBuilder("span");
        status.InnerHtml.Append($"Page {TablePage} of {pages} ({TableTotal} items)");
        footer.InnerHtml.AppendHtml(status);
        foreach (var (label, page) in new[] { ("Previous", TablePage - 1), ("Next", TablePage + 1) })
        {
            if (page < 1 || page > pages) continue;
            var link = new TagBuilder("a");
            link.AddCssClass("btn btn-default btn-sm");
            link.Attributes[LinkAttribute] = "";
            link.Attributes["href"] = Url((PageKey, page.ToString()));
            link.InnerHtml.Append(label);
            footer.InnerHtml.AppendHtml(link);
        }
        AddSelect(footer, "Page", "data-table-page", Enumerable.Range(1, pages).Select(page => (page, page.ToString())), TablePage, PageKey);
        AddSelect(footer, "Rows", "data-table-size", new[] { 10, 20, 50, 100 }.Select(size => (size, size.ToString())), TableOptions.PageSize, SizeKey);
        output.PostElement.AppendHtml(footer);
    }

    private void AddSelect(TagBuilder footer, string name, string attribute, IEnumerable<(int Value, string Label)> choices, int selected, string key)
    {
        var label = new TagBuilder("label");
        label.InnerHtml.Append(name + " ");
        var select = new TagBuilder("select");
        select.AddCssClass("form-control input-sm");
        select.Attributes[attribute] = "";
        select.Attributes["data-table-context"] = TableContext ? "true" : "false";
        foreach (var choice in choices)
        {
            var option = new TagBuilder("option");
            option.Attributes["value"] = key == PageKey ? Url((PageKey, choice.Value.ToString()))
                : Url((PageKey, "1"), (SizeKey, choice.Value.ToString()));
            if (choice.Value == selected) option.Attributes["selected"] = "selected";
            option.InnerHtml.Append(choice.Label);
            select.InnerHtml.AppendHtml(option);
        }
        label.InnerHtml.AppendHtml(select);
        footer.InnerHtml.AppendHtml(label);
    }
}

[HtmlTargetElement("th", Attributes = "table-sort")]
public class TableSortTagHelper : TagHelper
{
    public string TableSort { get; set; } = "";

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (!context.Items.TryGetValue(typeof(TableTagHelper), out var value) || value is not TableTagHelper table) return;
        var active = table.TableOptions.Sort == TableSort;
        output.Attributes.SetAttribute("aria-sort", active ? table.TableOptions.Descending ? "descending" : "ascending" : "none");
        var descending = active && !table.TableOptions.Descending;
        var link = new TagBuilder("a");
        link.Attributes[table.LinkAttribute] = "";
        link.Attributes["href"] = table.TableSearch
            ? table.Url((table.PageKey, "1"), ("Input.Ordering", TableSort + (descending ? " desc" : "")))
            : table.Url((table.PageKey, "1"), ("sort", TableSort), ("descending", descending.ToString().ToLowerInvariant()));
        link.InnerHtml.AppendHtml(await output.GetChildContentAsync());
        if (active)
        {
            var arrow = new TagBuilder("span");
            arrow.Attributes["aria-hidden"] = "true";
            arrow.InnerHtml.Append(table.TableOptions.Descending ? " ▼" : " ▲");
            link.InnerHtml.AppendHtml(arrow);
        }
        output.Content.SetHtmlContent(link);
    }
}
