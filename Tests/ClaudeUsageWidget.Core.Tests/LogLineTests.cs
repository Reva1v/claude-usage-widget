namespace ClaudeUsageWidget.Core.Tests;

public class LogLineTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 11, 39, 45, 123, TimeSpan.FromHours(3));

    [Fact]
    public void PutsTheStampAccountEventAndDetailsOnOneLine()
    {
        Assert.Equal(
            "2026-09-04T11:39:45.123+03:00 low nav-failed path=/x status=Unknown http=0 attempt=1",
            LogLine.Format(Now, "low", "nav-failed", "path=/x status=Unknown http=0 attempt=1"));
    }

    [Fact]
    public void FoldsANewlineInTheDetailsIntoASpace()
    {
        // Details carry server text and exception messages. One event that
        // smuggled in a line break would make every line after it unreadable
        // to anything parsing this file line by line.
        var line = LogLine.Format(Now, "low", "nav-failed", "first\nsecond");

        Assert.EndsWith(" nav-failed first second", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void FoldsEachOfCrAndLfSeparatelyInEveryArgument()
    {
        // Per character, not per line break: "\r\n" becomes two spaces. The
        // rule is about never emitting a control character, not about
        // prettifying the text. The account name is user-typed, so it needs
        // the same treatment as the details.
        var line = LogLine.Format(Now, "wo\r\nrk", "nav\nfailed", "a\rb");

        Assert.Equal("2026-09-04T11:39:45.123+03:00 wo  rk nav failed a b", line);
    }
}
