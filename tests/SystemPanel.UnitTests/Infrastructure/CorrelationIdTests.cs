using Microsoft.AspNetCore.Http;
using SystemPanel.Infrastructure;

namespace SystemPanel.UnitTests.Infrastructure;

public class CorrelationIdTests
{
    [Theory]
    [InlineData("abc123")]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    [InlineData("req_42")]
    public void Valid_ids_are_accepted(string id) => Assert.True(CorrelationIdMiddleware.IsValid(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("line\nbreak")]
    [InlineData("<script>")]
    public void Unsafe_ids_are_rejected(string? id) => Assert.False(CorrelationIdMiddleware.IsValid(id));

    [Fact]
    public void Too_long_ids_are_rejected() => Assert.False(CorrelationIdMiddleware.IsValid(new string('a', 65)));

    [Fact]
    public async Task Middleware_reuses_valid_incoming_id_and_replaces_invalid_one()
    {
        string? seen = null;
        var mw = new CorrelationIdMiddleware(ctx => { seen = CorrelationIdMiddleware.Get(ctx); return Task.CompletedTask; });

        var good = new DefaultHttpContext();
        good.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc-123";
        await mw.InvokeAsync(good);
        Assert.Equal("abc-123", seen);
        Assert.Equal("abc-123", good.TraceIdentifier);

        var bad = new DefaultHttpContext();
        bad.Request.Headers[CorrelationIdMiddleware.HeaderName] = "evil\r\nheader";
        await mw.InvokeAsync(bad);
        Assert.NotEqual("evil\r\nheader", seen);
        Assert.Equal(32, seen!.Length);
    }
}
