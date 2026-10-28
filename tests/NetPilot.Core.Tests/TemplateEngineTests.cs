using System.Text.RegularExpressions;
using NetPilot.Core.Codec;
using Xunit;

namespace NetPilot.Core.Tests;

public sealed class TemplateEngineTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void Expand_Time_ReplacesWithFormattedTime()
    {
        var result = _engine.Expand("${time:yyyy}");
        Assert.Equal(DateTime.Now.Year.ToString(), result);
    }

    [Fact]
    public void Expand_Random_RespectsRange()
    {
        for (var i = 0; i < 20; i++)
        {
            var value = int.Parse(_engine.Expand("${random:5-7}"));
            Assert.InRange(value, 5, 7);
        }
    }

    [Fact]
    public void Expand_Sequence_IncrementsFromStart()
    {
        Assert.Equal("101", _engine.Expand("${seq:100}"));
        Assert.Equal("102", _engine.Expand("${seq}"));
        _engine.Reset();
        Assert.Equal("1", _engine.Expand("${seq}"));
    }

    [Fact]
    public void Expand_UnknownVariable_Unchanged() =>
        Assert.Equal("${unknown}", _engine.Expand("${unknown}"));

    [Fact]
    public void Expand_MixedTemplate_ReplacesAllVariables()
    {
        var result = _engine.Expand("id=${seq} at=${time:HHmmss}");
        Assert.Matches(new Regex(@"^id=\d+ at=\d{6}$"), result);
    }
}
