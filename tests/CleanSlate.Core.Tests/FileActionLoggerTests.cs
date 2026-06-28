using CleanSlate.Core.Diagnostics;
using Xunit;

namespace CleanSlate.Core.Tests;

/// <summary>
/// Tests du formatage des tailles. On se limite à des valeurs sans partie
/// fractionnaire pour rester indépendant de la culture (séparateur décimal).
/// </summary>
public class FileActionLoggerTests
{
    [Theory]
    [InlineData(0L, "0 o")]
    [InlineData(512L, "512 o")]
    [InlineData(1023L, "1023 o")]
    [InlineData(1024L, "1 Ko")]
    [InlineData(1048576L, "1 Mo")]
    [InlineData(1073741824L, "1 Go")]
    [InlineData(1099511627776L, "1 To")]
    public void FormatBytes_FormateLesBornes(long bytes, string expected)
    {
        Assert.Equal(expected, FileActionLogger.FormatBytes(bytes));
    }
}
