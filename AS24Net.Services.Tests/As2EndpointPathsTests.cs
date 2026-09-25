using Microsoft.Extensions.Configuration;
using AS24Net.Services.As2;

namespace AS24Net.Services.Tests;

public class As2EndpointPathsTests
{
    private static IReadOnlyList<string> Get(params (string Key, string? Value)[] values) =>
        As2EndpointPaths.Get(new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build());

    [Fact]
    public void WithoutAliases_OnlyAs2()
    {
        Assert.Equal(["/as2"], Get());
    }

    [Fact]
    public void Aliases_AsAList()
    {
        Assert.Equal(["/as2", "/receiver.aspx", "/mdn.aspx"],
            Get(("As2:AdditionalPaths:0", "/receiver.aspx"), ("As2:AdditionalPaths:1", "mdn.aspx")));
    }

    [Fact]
    public void Aliases_SeparatedByCommas_WithoutDuplicates()
    {
        Assert.Equal(["/as2", "/Receiver.aspx", "/legacy/mdn.aspx"],
            Get(("As2:AdditionalPaths", " /Receiver.aspx, /legacy/mdn.aspx/ ,/receiver.aspx, /AS2")));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/{partner}")]
    [InlineData("/receiver aspx")]
    [InlineData("/receiver.aspx?x=1")]
    public void SomethingElseThanAPath_IsRefused(string value)
    {
        Assert.Throws<InvalidOperationException>(() => Get(("As2:AdditionalPaths:0", value)));
    }
}
