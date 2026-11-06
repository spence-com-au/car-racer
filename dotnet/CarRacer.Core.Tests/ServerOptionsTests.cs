using CarRacer.Core.Discovery;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class ServerOptionsTests
    {
        [Fact]
        public void APlainLaunchIsNotAServer()
        {
            var options = ServerOptions.Parse(new[] { "-batchmode", "-nographics" });
            Assert.False(options.IsServer);
            Assert.Equal(DiscoveryConstants.DefaultGamePort, options.Port);
            Assert.Equal(ServerOptions.DefaultName, options.Name);
            Assert.Null(options.Error);
        }

        [Fact]
        public void ServerPortAndNameAreRead_AmongUnitysOwnArguments()
        {
            var options = ServerOptions.Parse(new[] { "-batchmode", "-SERVER", "-logFile", "-", "-port", "47900", "-name", "  Lounge PC  " });
            Assert.True(options.IsServer);
            Assert.Equal(47900, options.Port);
            Assert.Equal("Lounge PC", options.Name);
            Assert.Null(options.Error);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("70000")]
        [InlineData("abc")]
        [InlineData("47777")]
        public void ABadPortIsAnError_AndTheDefaultStays(string port)
        {
            var options = ServerOptions.Parse(new[] { "-server", "-port", port });
            Assert.NotNull(options.Error);
            Assert.Equal(DiscoveryConstants.DefaultGamePort, options.Port);
        }

        [Fact]
        public void AMissingValueIsHandled()
        {
            Assert.NotNull(ServerOptions.Parse(new[] { "-port" }).Error);
            Assert.Equal("Player", ServerOptions.Parse(new[] { "-name" }).Name);
        }

        [Fact]
        public void TvViewerOptionsAreRead()
        {
            var plain = ServerOptions.Parse(new[] { "-server" });
            Assert.Null(plain.CastTo);
            Assert.Equal("ffmpeg", plain.FfmpegPath);
            Assert.Equal(ServerOptions.DefaultViewerPort, plain.ViewerPort);

            var named = ServerOptions.Parse(new[] { "-server", "-cast", " Living Room TV ", "-ffmpeg", "/opt/ffmpeg/bin/ffmpeg", "-viewer-port", "8080" });
            Assert.Equal("Living Room TV", named.CastTo);
            Assert.Equal("/opt/ffmpeg/bin/ffmpeg", named.FfmpegPath);
            Assert.Equal(8080, named.ViewerPort);
            Assert.Null(named.Error);

            // A bare -cast takes the first TV found, whether a flag or nothing follows.
            Assert.Equal(string.Empty, ServerOptions.Parse(new[] { "-cast", "-name", "Lounge" }).CastTo);
            Assert.Equal("Lounge", ServerOptions.Parse(new[] { "-cast", "-name", "Lounge" }).Name);
            Assert.Equal(string.Empty, ServerOptions.Parse(new[] { "-cast" }).CastTo);
        }

        [Theory]
        [InlineData("-viewer-port", "0")]
        [InlineData("-viewer-port", "x")]
        [InlineData("-ffmpeg", " ")]
        public void BadTvViewerOptionsAreErrors(string flag, string value) => Assert.NotNull(ServerOptions.Parse(new[] { "-server", flag, value }).Error);
    }
}
