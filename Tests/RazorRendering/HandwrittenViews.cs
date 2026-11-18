namespace IL.VirtualViews.Tests.RazorRendering;

public sealed record HandwrittenModel(string Message);

[VirtualViewPath("/views/record.cshtml")]
public sealed record HandwrittenRecordView : IVirtualView
{
    public static string ViewContent => "<p>Record template</p>";
}

[VirtualViewPath("/_VirtualViews/custom.cshtml")]
public sealed class CustomPrefixView : IVirtualView
{
    public static string ViewContent => "<code>@Path</code>";
}

[VirtualViewPath("/views/escaped.cshtml")]
public sealed class @event : IVirtualView
{
    public static string ViewContent => "<code>@Path</code>";
}

[VirtualViewPath("/views/handwritten.cshtml")]
public sealed class HandwrittenView : IVirtualView
{
    public static string ViewContent => """
        @model IL.VirtualViews.Tests.RazorRendering.HandwrittenModel
        <strong>@Model.Message</strong>
        """;
}

[VirtualViewPath("/views/layout.cshtml")]
public sealed class HandwrittenLayout : IVirtualView
{
    public static string ViewContent => "<main>@RenderBody()</main>";
}

[VirtualViewPath("/views/partial.cshtml")]
public sealed class HandwrittenPartial : IVirtualView
{
    public static string ViewContent => """
        @model string
        <aside>@Model</aside>
        """;
}

[VirtualViewPath("/views/composed.cshtml")]
public sealed class HandwrittenComposedView : IVirtualView
{
    // An initialized constant property is supported as well as an expression body.
    public static string ViewContent { get; } = """
        @model string
        @{ Layout = "/views/layout.cshtml"; }
        <article>@Model</article>
        @await Html.PartialAsync("/views/partial.cshtml", Model)
        """;
}
