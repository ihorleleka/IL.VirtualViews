using IL.VirtualViews.Tests.VirtualViewsFolder;

namespace IL.VirtualViews.Tests.GeneratedFileContent;

public class GeneratedFileContentTests
{
    [Fact]
    public void Generated_Cs_Class_Outputs_Correct_Content()
    {
        const string expected = """
                                @model string

                                <p>Test 123</p>
                                """;
        var result = Test.ViewContent;
        
        Assert.Equal(expected, result);
    }
}