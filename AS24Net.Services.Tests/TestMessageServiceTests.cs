namespace AS24Net.Services.Tests;

public class TestMessageServiceTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        var request = new TestMessageRequest();

        Assert.Null(TestMessageService.Validate(request));
        Assert.Equal("test.txt", request.FileName);
        Assert.Equal("This is test from artipa. support@artipa.com", request.Content);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("dir/test.txt")]
    [InlineData("..\\test.txt")]
    [InlineData("te\"st.txt")]
    public void InvalidFileNamesAreRefused(string name)
    {
        Assert.NotNull(TestMessageService.Validate(new TestMessageRequest { FileName = name }));
    }

    [Fact]
    public void EmptyContentIsRefused()
    {
        Assert.NotNull(TestMessageService.Validate(new TestMessageRequest { Content = "\r\n " }));
    }
}
