using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Api;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using Jellyfin.Plugin.PhishNet.Configuration;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Chapters.Api;

/// <summary>
/// Every chapter API action needs a Jellyfin admin. The controller runs in a test host whose
/// "RequiresElevation" policy is built the way Jellyfin 12.3 builds it
/// (ApiServiceCollectionExtensions: the CustomAuthentication scheme plus the Administrator role claim).
/// The endpoints are discovered from routing, so an action added later is covered without editing this.
/// </summary>
public sealed class ChapterMarkersControllerAuthTests : IAsyncLifetime
{
    private const string Scheme = "CustomAuthentication";
    private const string RoleHeader = "X-Test-Role";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chapter-api-" + Guid.NewGuid().ToString("N"));
    private WebApplication? _app;
    private ChapterMarkerService? _service;
    private HttpClient? _client;

    public static IEnumerable<object[]> Callers() =>
    [
        [null!, HttpStatusCode.Unauthorized],
        ["User", HttpStatusCode.Forbidden],
        ["Guest", HttpStatusCode.Forbidden],
    ];

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, HeaderRoleHandler>(Scheme, _ => { });
        builder.Services.AddAuthorization(options =>
        {
            options.DefaultPolicy = new AuthorizationPolicyBuilder().AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser().Build();
            options.AddPolicy(Policies.RequiresElevation, policy => policy.AddAuthenticationSchemes(Scheme).RequireClaim(ClaimTypes.Role, "Administrator"));
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(ChapterMarkersController).Assembly).AddControllersAsServices();
        AddChapterServices(builder.Services);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Routing_ShouldExposeTheStatusAndTheThreeActions()
    {
        Endpoints().Select(e => $"{e.Method} {e.Route}").Should().BeEquivalentTo(
            "GET PhishData/Chapters/Status",
            "POST PhishData/Chapters/{itemId}/Retry",
            "POST PhishData/Chapters/{itemId}/ApplyAnyway",
            "POST PhishData/Chapters/{itemId}/Ignore");
    }

    [Theory]
    [MemberData(nameof(Callers))]
    public async Task EveryAction_ShouldRefuseAnyoneButAnAdmin(string? role, HttpStatusCode expected)
    {
        foreach (var endpoint in Endpoints())
        {
            var response = await Send(endpoint, role);

            response.StatusCode.Should().Be(expected, $"{endpoint.Method} {endpoint.Route} as {role ?? "anonymous"}");
        }
    }

    [Fact]
    public async Task EveryAction_ShouldLetAnAdminThrough()
    {
        foreach (var endpoint in Endpoints())
        {
            var response = await Send(endpoint, "Administrator");

            ((int)response.StatusCode).Should().BeOneOf(new[] { 200, 204, 404, 409 }, $"{endpoint.Method} {endpoint.Route} as admin");
        }
    }

    [Fact]
    public void EveryAction_ShouldCarryTheElevationPolicy_AndNoneMayOptOut()
    {
        var controller = typeof(ChapterMarkersController);
        controller.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).Should().Equal(Policies.RequiresElevation);
        controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Should().OnlyContain(m => !m.GetCustomAttributes<AllowAnonymousAttribute>().Any() && !m.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy != Policies.RequiresElevation));
    }

    [Fact]
    public async Task Admin_ShouldGetTheStatus_AndTheReasonAnActionWasRefused()
    {
        var id = Guid.NewGuid();
        await _service!.Store.WriteAsync(id, new ChapterState { Status = ChapterStatus.Applied, ShowDate = new DateTime(2026, 7, 7) }, CancellationToken.None);

        var status = await Send(("GET", "PhishData/Chapters/Status"), "Administrator");
        var ignore = await Send(("POST", $"PhishData/Chapters/{id:N}/Ignore"), "Administrator");
        var retry = await Send(("POST", $"PhishData/Chapters/{id:N}/Retry"), "Administrator");

        status.StatusCode.Should().Be(HttpStatusCode.OK);
        (await status.Content.ReadAsStringAsync()).Should().Contain("\"Applied\":1", "dictionary keys keep their case under any naming policy");
        ignore.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ignore.Content.ReadAsStringAsync()).Should().Contain("only shows without our chapters can be ignored");
        retry.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _service.Store.ReadAsync(id, CancellationToken.None))!.RetryRequested.Should().BeTrue();
    }

    private void AddChapterServices(IServiceCollection services)
    {
        var library = new Mock<ILibraryManager>();
        var serverConfig = new Mock<IServerConfigurationManager>();
        serverConfig.Setup(c => c.Configuration).Returns(new ServerConfiguration());
        var config = new PluginConfiguration { EnableChapterMarkers = true };
        Func<PluginConfiguration> getConfig = () => config;
        var service = new ChapterMarkerService(
            Mock.Of<IChapterManager>(),
            serverConfig.Object,
            Mock.Of<ILocalizationManager>(),
            Mock.Of<IShowAudioSource>(),
            Mock.Of<IEnvelopeDecoder>(),
            new ChapterStateStore(_dir),
            getConfig,
            TimeProvider.System,
            NullLogger<ChapterMarkerService>.Instance);
        services.AddSingleton(service);
        _service = service;
        services.AddSingleton(new ChapterQueue(library.Object, service, getConfig, TimeProvider.System, NullLogger<ChapterQueue>.Instance));
        services.AddSingleton(library.Object);
        services.AddSingleton(getConfig);
    }

    private IReadOnlyList<(string Method, string Route)> Endpoints() =>
        _app!.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo == typeof(ChapterMarkersController))
            .SelectMany(e => e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Select(m => (m, e.RoutePattern.RawText!)))
            .ToList();

    private Task<HttpResponseMessage> Send((string Method, string Route) endpoint, string? role)
    {
        var path = endpoint.Route.Replace("{itemId}", Guid.NewGuid().ToString("N"), StringComparison.Ordinal);
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), "/" + path);
        if (role is not null)
        {
            request.Headers.Add(RoleHeader, role);
        }

        return _client!.SendAsync(request);
    }

    /// <summary>Stands in for Jellyfin's token check: the header names the caller's role; no header is anonymous.</summary>
    private sealed class HeaderRoleHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone"), new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
