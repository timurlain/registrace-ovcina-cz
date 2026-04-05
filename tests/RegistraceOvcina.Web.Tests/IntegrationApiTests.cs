using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using RegistraceOvcina.Web.Features.Integration;

namespace RegistraceOvcina.Web.Tests;

/// <summary>
/// Unit tests for the API key filter and DTO shapes.
/// These tests do not require a running database — they exercise the filter logic directly.
/// </summary>
public sealed class IntegrationApiOptionsTests
{
    [Fact]
    public void SectionName_IsExpectedValue()
    {
        Assert.Equal("IntegrationApi", IntegrationApiOptions.SectionName);
    }

    [Fact]
    public void DefaultApiKey_IsEmpty()
    {
        var opts = new IntegrationApiOptions();
        Assert.Equal("", opts.ApiKey);
    }
}

public sealed class ApiKeyEndpointFilterTests
{
    private static ApiKeyEndpointFilter CreateFilter(string configuredKey)
    {
        var options = Options.Create(new IntegrationApiOptions { ApiKey = configuredKey });
        return new ApiKeyEndpointFilter(options);
    }

    private static DefaultHttpContext CreateHttpContext(string? headerValue)
    {
        var ctx = new DefaultHttpContext();
        if (headerValue is not null)
            ctx.Request.Headers["X-Api-Key"] = headerValue;
        return ctx;
    }

    [Fact]
    public async Task MissingKey_Returns401()
    {
        var filter = CreateFilter("secret-key");
        var httpCtx = CreateHttpContext(null);
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsType<UnauthorizedHttpResult>(httpResult);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        var filter = CreateFilter("secret-key");
        var httpCtx = CreateHttpContext("wrong-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsType<UnauthorizedHttpResult>(httpResult);
    }

    [Fact]
    public async Task CorrectKey_PassesThrough()
    {
        var filter = CreateFilter("secret-key");
        var httpCtx = CreateHttpContext("secret-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var sentinel = Results.Ok("passed");
        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(sentinel));

        Assert.Same(sentinel, result);
    }

    [Fact]
    public async Task EmptyConfiguredKey_Returns503()
    {
        var filter = CreateFilter("");
        var httpCtx = CreateHttpContext("any-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        // Should be a ProblemHttpResult (503)
        Assert.IsType<ProblemHttpResult>(httpResult);
    }

    [Fact]
    public async Task KeyIsCaseSensitive()
    {
        var filter = CreateFilter("Secret-Key");
        var httpCtx = CreateHttpContext("secret-key");
        var efCtx = new FakeEndpointFilterContext(httpCtx);

        var result = await filter.InvokeAsync(efCtx, _ => ValueTask.FromResult<object?>(Results.Ok()));

        var httpResult = Assert.IsAssignableFrom<IResult>(result);
        Assert.IsType<UnauthorizedHttpResult>(httpResult);
    }
}

public sealed class IntegrationApiDtoTests
{
    [Fact]
    public void GameDto_RecordEquality()
    {
        var now = DateTime.UtcNow;
        var a = new GameDto(1, "Ovčina 2026", null, now, now, now, 100, true);
        var b = new GameDto(1, "Ovčina 2026", null, now, now, now, 100, true);
        Assert.Equal(a, b);
    }

    [Fact]
    public void RegistrationDto_RecordEquality()
    {
        var a = new RegistrationDto(10, 5, "Jan", "Novák", 2000, "Player", "Frodo", "Active");
        var b = new RegistrationDto(10, 5, "Jan", "Novák", 2000, "Player", "Frodo", "Active");
        Assert.Equal(a, b);
    }

    [Fact]
    public void PresenceCheckDto_IsRegistered_True()
    {
        var dto = new PresenceCheckDto(true);
        Assert.True(dto.IsRegistered);
    }

    [Fact]
    public void PresenceCheckDto_IsRegistered_False()
    {
        var dto = new PresenceCheckDto(false);
        Assert.False(dto.IsRegistered);
    }
}

// Minimal test double for EndpointFilterInvocationContext
internal sealed class FakeEndpointFilterContext(HttpContext httpContext) : EndpointFilterInvocationContext
{
    public override HttpContext HttpContext => httpContext;
    public override IList<object?> Arguments => [];
    public override T GetArgument<T>(int index) => throw new NotSupportedException();
}
