using QuickMCP.Helpers;

namespace QuickMCP.Tests;

public class PathHelperTests
{
    [Fact]
    public void ShouldFindPath()
    {
        var fileName = PathHelper.GetFullPath("config.json");
    }
    
}