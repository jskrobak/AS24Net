using AS24Net.Domain;

namespace AS24Net.Services.Tests;

public class ReceivedMessageResolutionTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 15, 0, 0);

    [Fact]
    public void ARefusedMessageIsResolvedWithWhoAndNote()
    {
        var message = new ReceivedMessage { Status = ReceivedStatus.Failed };

        message.Resolve(Now, " admin@artipa.com ", " Sent again from the new connection ");

        Assert.Equal(Now, message.ResolvedDate);
        Assert.Equal("admin@artipa.com", message.ResolvedBy);
        Assert.Equal("Sent again from the new connection", message.ResolutionNote);
    }

    [Fact]
    public void AnEmptyNoteAndAnUnknownUserAreKeptEmpty()
    {
        var message = new ReceivedMessage { Status = ReceivedStatus.Failed };

        message.Resolve(Now, null, "  ");

        Assert.Equal(Now, message.ResolvedDate);
        Assert.Null(message.ResolvedBy);
        Assert.Null(message.ResolutionNote);
    }

    [Theory]
    [InlineData(ReceivedStatus.Received)]
    [InlineData(ReceivedStatus.Duplicate)]
    public void OnlyARefusedMessageCanBeResolved(ReceivedStatus status)
    {
        var message = new ReceivedMessage { Status = status };

        Assert.Throws<InvalidOperationException>(() => message.Resolve(Now, "admin", null));
        Assert.Null(message.ResolvedDate);
    }

    [Fact]
    public void AResolvedMessageCannotBeResolvedAgain()
    {
        var message = new ReceivedMessage { Status = ReceivedStatus.Failed };
        message.Resolve(Now, "first", "first note");

        Assert.Throws<InvalidOperationException>(() => message.Resolve(Now.AddHours(1), "second", null));
        Assert.Equal("first", message.ResolvedBy);
        Assert.Equal(Now, message.ResolvedDate);
    }
}
