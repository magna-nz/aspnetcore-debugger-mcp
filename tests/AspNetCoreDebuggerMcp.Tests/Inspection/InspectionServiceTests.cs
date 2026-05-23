using AspNetCoreDebuggerMcp.Inspection;

namespace AspNetCoreDebuggerMcp.Tests.Inspection;

public class InspectionServiceTests
{
    [Fact]
    public void TryReadSnippet_ReturnsLinesAroundHighlightWithMarker()
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, new[] { "a", "b", "c", "d", "e", "f", "g" });
        try
        {
            var snippet = InspectionService.TryReadSnippet(path, highlightLine: 4, radius: 2);

            Assert.NotNull(snippet);
            Assert.Equal(2, snippet!.StartLine);
            Assert.Equal(6, snippet.EndLine);
            Assert.Equal(4, snippet.HighlightLine);
            Assert.Contains("→    4: d", snippet.Text);
            Assert.Contains("     2: b", snippet.Text);
            Assert.Contains("     6: f", snippet.Text);
            Assert.DoesNotContain(": a", snippet.Text);
            Assert.DoesNotContain(": g", snippet.Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryReadSnippet_ClampsToFileBoundaries()
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, new[] { "one", "two", "three" });
        try
        {
            var snippet = InspectionService.TryReadSnippet(path, highlightLine: 1, radius: 5);
            Assert.NotNull(snippet);
            Assert.Equal(1, snippet!.StartLine);
            Assert.Equal(3, snippet.EndLine);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryReadSnippet_ReturnsNullForMissingFile()
    {
        var snippet = InspectionService.TryReadSnippet("/nonexistent/path/xyz.cs", 1);
        Assert.Null(snippet);
    }
}
