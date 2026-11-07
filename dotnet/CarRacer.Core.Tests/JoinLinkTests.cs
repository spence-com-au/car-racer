using CarRacer.Core.Discovery;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class JoinLinkTests
    {
        [Fact]
        public void ALinkReadsBackAsTheHostAndPort()
        {
            string link = JoinLink.Make("192.168.1.20", 7777);
            Assert.Equal("carracer://join?host=192.168.1.20&port=7777", link);
            Assert.True(JoinLink.TryParse(link, out string address, out ushort port));
            Assert.Equal("192.168.1.20", address);
            Assert.Equal(7777, port);
        }

        [Fact]
        public void TheOrderOfTheFieldsAndTheSchemesCaseDoNotMatter()
        {
            Assert.True(JoinLink.TryParse("CarRacer://join?port=8000&host=10.0.0.5", out string address, out ushort port));
            Assert.Equal("10.0.0.5", address);
            Assert.Equal(8000, port);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://example.com/?host=1.2.3.4&port=7777")]
        [InlineData("carracer://join?host=1.2.3.4")]
        [InlineData("carracer://join?port=7777")]
        [InlineData("carracer://join?host=1.2.3.4&port=99999")]
        [InlineData("carracer://join?host=&port=7777")]
        [InlineData("carracer://join?host=evil.example.com&port=7777")]
        [InlineData("carracer://join?host=010.0.0.1&port=7777")]
        [InlineData("carracer://join?host=1.2.3&port=7777")]
        [InlineData("carracer://join?host=1.2.3.4:80&port=7777")]
        public void AnythingElseIsNotAJoinLink(string link)
        {
            Assert.False(JoinLink.TryParse(link, out _, out _));
        }

        [Fact]
        public void TheLinkFitsASmallQrCode()
        {
            // The longest IPv4 address and port: version 4 at medium error correction, about 33 modules across.
            string link = JoinLink.Make("255.255.255.255", 65535);
            Assert.True(Platform.QrCode.Encode(link).Version <= 4);
        }
    }
}
