using System.Collections.Generic;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class CommandLineArgumentsTests
    {
        private static readonly HashSet<string> Values = new HashSet<string> { "buildVersion", "androidKeystorePass" };

        [Fact]
        public void AFlagTakesTheNextEntryAsItsValue()
        {
            var args = CommandLineArguments.Parse(new[] { "-batchmode", "-buildVersion", "1.2.3", "-quit" }, Values);
            Assert.Equal("1.2.3", args["buildVersion"]);
            Assert.Equal(string.Empty, args["batchmode"]);
            Assert.Equal(string.Empty, args["quit"]);
        }

        [Fact]
        public void AValueArgumentTakesAValueThatStartsWithADash()
        {
            var args = CommandLineArguments.Parse(new[] { "-androidKeystorePass", "-secret", "-quit" }, Values);
            Assert.Equal("-secret", args["androidKeystorePass"]);
            Assert.True(args.ContainsKey("quit"));
            Assert.False(args.ContainsKey("secret"));
        }

        [Fact]
        public void AnyOtherFlagLeavesADashedEntryForTheNextFlag()
        {
            var args = CommandLineArguments.Parse(new[] { "-carRacerDevelopment", "-buildVersion", "2" }, Values);
            Assert.Equal(string.Empty, args["carRacerDevelopment"]);
            Assert.Equal("2", args["buildVersion"]);
        }

        [Fact]
        public void AValueArgumentAtTheEndHasNoValue()
        {
            var args = CommandLineArguments.Parse(new[] { "-buildVersion" }, Values);
            Assert.Equal(string.Empty, args["buildVersion"]);
        }

        [Fact]
        public void EntriesWithoutADashAreIgnored_AndTheLastValueWins()
        {
            var args = CommandLineArguments.Parse(new[] { "stray", "--buildVersion", "1", "-buildVersion", "2" }, Values);
            Assert.Equal("2", args["buildVersion"]);
            Assert.Single(args);
        }
    }
}
