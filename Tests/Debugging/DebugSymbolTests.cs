using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace IL.VirtualViews.Tests.Debugging;

public class DebugSymbolTests
{
    private static readonly Guid Sha256 = new("8829d00f-11b8-4213-878b-770e8597ac16");

    [Theory]
    [InlineData("DebugVirtualView.virtual.cshtml")]
    [InlineData("External.virtual.cshtml")]
    public void Compiled_Razor_maps_executable_code_to_original_template(string sourceName)
    {
        // Inputs are staged beside the test assembly by MSBuild. Neither the test
        // nor the debugger needs to discover the checkout to verify these symbols.
        var source = Path.Combine(AppContext.BaseDirectory, "TestAssets", "DebugSources", sourceName);
        var sourceBytes = File.ReadAllBytes(source);
        var lines = File.ReadAllLines(source);
        var modelLine = Array.FindIndex(lines, line => line.Contains("@Model", StringComparison.Ordinal)) + 1;
        Assert.True(modelLine > 0, "The fixture must contain an executable @Model expression.");

        var pdb = Path.ChangeExtension(typeof(DebugSymbolTests).Assembly.Location, ".pdb");
        using var stream = File.OpenRead(pdb);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        var documentHandle = Assert.Single(reader.Documents, handle =>
        {
            var name = reader.GetString(reader.GetDocument(handle).Name).Replace('\\', '/');
            return name.EndsWith("/" + sourceName, StringComparison.OrdinalIgnoreCase)
                || name.Equals(sourceName, StringComparison.OrdinalIgnoreCase);
        });
        var document = reader.GetDocument(documentHandle);
        Assert.Equal(Sha256, reader.GetGuid(document.HashAlgorithm));
        Assert.Equal(SHA256.HashData(sourceBytes), reader.GetBlobBytes(document.Hash));

        // A document/checksum entry alone does not make a breakpoint bind. At least
        // one nonhidden executable sequence point must map to the expression line.
        // Sequence points may inherit the method's default document.
        var hasExecutablePoint = false;
        foreach (var methodHandle in reader.MethodDebugInformation)
        {
            var method = reader.GetMethodDebugInformation(methodHandle);
            var currentDocument = method.Document;
            foreach (var point in method.GetSequencePoints())
            {
                if (!point.Document.IsNil)
                    currentDocument = point.Document;
                if (!point.IsHidden && currentDocument == documentHandle
                    && point.StartLine <= modelLine && point.EndLine >= modelLine)
                {
                    hasExecutablePoint = true;
                    break;
                }
            }
            if (hasExecutablePoint)
                break;
        }
        Assert.True(hasExecutablePoint,
            $"No executable sequence point maps to {sourceName}:{modelLine} in {Path.GetFileName(pdb)}.");
    }
}
