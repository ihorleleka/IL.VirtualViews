namespace IL.VirtualViews.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class VirtualViewSourcePathAttribute : Attribute
{
    public VirtualViewSourcePathAttribute(string sourcePath)
    {
        SourcePath = sourcePath;
    }

    public string SourcePath { get; }
}
