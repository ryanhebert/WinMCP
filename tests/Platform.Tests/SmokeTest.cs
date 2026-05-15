using Xunit;

namespace WinMcp.Platform.Tests;

public class SmokeTest
{
    [Fact]
    public void RuntimeWorks() => Assert.Equal(4, 2 + 2);
}
