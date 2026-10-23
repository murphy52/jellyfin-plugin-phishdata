using FluentAssertions;
using Jellyfin.Plugin.PhishNet.Chapters;
using Jellyfin.Plugin.PhishNet.Chapters.Audio;
using Jellyfin.Plugin.PhishNet.Chapters.PhishIn;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Chapters;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.PhishNet.Tests.Plugin;

/// <summary>
/// The chapter pieces resolve from the services Jellyfin provides, with one queue that is also the hosted
/// service, and the task (which Jellyfin finds by assembly scan) can be built from the container.
/// </summary>
public class PluginServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_ShouldWireTheChapterFeature()
    {
        var data = Path.Combine(Path.GetTempPath(), "registrator-" + Guid.NewGuid().ToString("N"));
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(data);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(paths.Object);
        services.AddSingleton(Mock.Of<ILibraryManager>());
        services.AddSingleton(Mock.Of<IChapterManager>());
        services.AddSingleton(Mock.Of<IServerConfigurationManager>());
        services.AddSingleton(Mock.Of<ILocalizationManager>());
        services.AddSingleton(Mock.Of<IMediaEncoder>());

        new PluginServiceRegistrator().RegisterServices(services, Mock.Of<IServerApplicationHost>());
        using var provider = services.BuildServiceProvider();

        var queue = provider.GetRequiredService<ChapterQueue>();
        provider.GetServices<IHostedService>().Should().ContainSingle(h => ReferenceEquals(h, queue));
        provider.GetRequiredService<IChapterMarkerService>().Should().BeSameAs(provider.GetRequiredService<ChapterMarkerService>());
        provider.GetRequiredService<IShowAudioSource>().Should().BeOfType<PhishInTemplateSource>();
        provider.GetRequiredService<IEnvelopeDecoder>().Should().BeOfType<EnvelopeDecoder>();
        provider.GetRequiredService<PhishInPacer>().Should().BeSameAs(provider.GetRequiredService<PhishInPacer>());
        ActivatorUtilities.CreateInstance<ChapterMarkerTask>(provider).Name.Should().Be("Phish: add chapter markers");

        provider.GetRequiredService<ChapterMarkerService>().Store.KnownItemIds().Should().BeEmpty();
        Directory.Exists(data).Should().BeFalse("nothing is written until there is work");
    }
}
