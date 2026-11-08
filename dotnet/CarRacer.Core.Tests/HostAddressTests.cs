using System.Net;
using CarRacer.Core.Discovery;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class HostAddressTests
    {
        [Theory]
        [InlineData("192.168.1.23", "192.168.1.23", DiscoveryConstants.DefaultGamePort)]
        [InlineData("  10.0.0.7  ", "10.0.0.7", DiscoveryConstants.DefaultGamePort)]
        [InlineData("192.168.1.23:47800", "192.168.1.23", 47800)]
        [InlineData("172.20.10.1:1", "172.20.10.1", 1)]
        [InlineData("10.0.0.0", "10.0.0.0", DiscoveryConstants.DefaultGamePort)]
        public void ValidAddressesParse(string text, string address, int port)
        {
            Assert.True(HostAddress.TryParse(text, out string parsed, out ushort parsedPort));
            Assert.Equal(address, parsed);
            Assert.Equal(port, parsedPort);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("192.168.1")]
        [InlineData("192.168.1.256")]
        [InlineData("192.168.1.23.4")]
        [InlineData("192.168..23")]
        [InlineData("a.b.c.d")]
        [InlineData("192.168.1.23:")]
        [InlineData("192.168.1.23:0")]
        [InlineData("192.168.1.23:70000")]
        [InlineData("192.168.1.23:-5")]
        [InlineData("+1.2.3.4")]
        [InlineData("1.2.3.0004")]
        [InlineData("1.2.3.007")]
        [InlineData("01.2.3.4")]
        [InlineData("192.168.001.010")]
        [InlineData("myphone.local")]
        public void InvalidAddressesDoNot(string text)
        {
            Assert.False(HostAddress.TryParse(text, out _, out _));
        }

        [Fact]
        public void UsableKeepsOnlyDistinctNonLoopbackIPv4InOrder()
        {
            var usable = HostAddress.Usable(new[]
            {
                IPAddress.Parse("127.0.0.1"),
                IPAddress.Parse("fe80::1"),
                IPAddress.Parse("::1"),
                IPAddress.Any,
                IPAddress.Parse("192.168.1.23"),
                IPAddress.Parse("10.0.0.7"),
                IPAddress.Parse("192.168.1.23"),
            });

            Assert.Equal(new[] { "192.168.1.23", "10.0.0.7" }, usable);
        }

        [Fact]
        public void UsableIsEmptyWithNothingUsable()
        {
            Assert.Empty(HostAddress.Usable(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback }));
        }
    }
}
