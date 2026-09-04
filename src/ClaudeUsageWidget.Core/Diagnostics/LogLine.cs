using System.Globalization;

namespace ClaudeUsageWidget.Core;

/// One line of the widget's event log. Pure: the file itself is the App
/// layer's business.
public static class LogLine
{
    /// "2026-09-04T11:39:45.123+03:00 low nav-failed path=/x status=Unknown http=0 attempt=1"
    ///
    /// Local time WITH the offset, not UTC: the reader is a human comparing
    /// this against when he saw the widget go red, and a bare UTC stamp makes
    /// him do the arithmetic. The offset keeps it unambiguous anyway.
    ///
    /// Every argument is folded, not just the details: the account name is
    /// user-typed. One embedded line break would turn a line-oriented file
    /// into an unparseable one, and the log exists precisely for the days
    /// when something is already going wrong.
    public static string Format(DateTimeOffset now, string account, string evt, string details) =>
        // InvariantCulture: the separators of the pattern below are literal in
        // an invariant format, but culture-dependent under a culture that
        // reads ':' and '.' as its own time/decimal separators.
        $"{now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture)} " +
        $"{Fold(account)} {Fold(evt)} {Fold(details)}";

    /// Per character, so "\r\n" becomes two spaces. Replacing the pair with a
    /// single space would be prettier and would also mean carrying a rule
    /// about WHICH control characters pair up; the point here is only that
    /// none of them reach the file.
    private static string Fold(string text) => text.Replace('\r', ' ').Replace('\n', ' ');
}
