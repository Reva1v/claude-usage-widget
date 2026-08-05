namespace ClaudeUsageWidget.Core.Tests;

public class OrganizationPickerTests
{
    [Fact]
    public void PrefersTeamOrganizationWithChat() =>
        Assert.Equal("team-uuid", OrganizationPicker.Pick("""
        [
          { "uuid": "personal-uuid", "capabilities": ["chat"], "raven_type": "personal" },
          { "uuid": "team-uuid", "capabilities": ["chat"], "raven_type": "team" },
          { "uuid": "no-chat", "capabilities": ["api"] }
        ]
        """));

    [Fact]
    public void FallsBackToFirstChatCapable() =>
        Assert.Equal("personal-uuid", OrganizationPicker.Pick("""
        [ { "uuid": "personal-uuid", "capabilities": ["chat"] } ]
        """));

    [Fact]
    public void AcceptsIdWhenUuidMissing() =>
        Assert.Equal("42", OrganizationPicker.Pick("""
        [ { "id": "42", "capabilities": ["chat"] } ]
        """));

    [Fact]
    public void ReturnsNullOnGarbageOrEmpty()
    {
        Assert.Null(OrganizationPicker.Pick("[]"));
        Assert.Null(OrganizationPicker.Pick("not json"));
    }
}
