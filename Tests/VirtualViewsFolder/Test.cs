namespace IL.VirtualViews.Tests.VirtualViewsFolder;

public class TestAttribute : VirtualViewPathAttribute
{
    public TestAttribute(string componentName) : base($"/views/{componentName}.cshtml")
    {
    }
}

[Test("test")]
public partial class Test
{
}