using System.IO;
using CarRacer.Core.Session;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class AndroidSigningTests
    {
        private static AndroidSigning Choose(bool development, params string[] argv) =>
            AndroidSigning.FromArguments(CommandLineArguments.Parse(argv, CommandLineArguments.BuildValueArguments), development);

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void APassedKeystoreIsUsedWithItsCredentials(bool development)
        {
            var signing = Choose(
                development,
                "-androidKeystoreName", "release.keystore",
                "-androidKeystorePass", "-store",
                "-androidKeyaliasName", "alias",
                "-androidKeyaliasPass", "key");

            Assert.True(signing.UseCustomKeystore);
            Assert.Equal("release.keystore", signing.KeystoreName);
            Assert.Equal("-store", signing.KeystorePass);
            Assert.Equal("alias", signing.KeyaliasName);
            Assert.Equal("key", signing.KeyaliasPass);
            Assert.True(signing.IsAllowedFor(false));
        }

        [Fact]
        public void APassedKeystoreWithoutCredentialsHasEmptyOnes()
        {
            var signing = Choose(false, "-androidKeystoreName", "release.keystore");

            Assert.True(signing.UseCustomKeystore);
            Assert.Equal(string.Empty, signing.KeystorePass);
            Assert.Equal(string.Empty, signing.KeyaliasName);
            Assert.Equal(string.Empty, signing.KeyaliasPass);
        }

        [Fact]
        public void ADevelopmentBuildWithoutAKeystoreUsesTheSharedDebugKeystore()
        {
            var signing = Choose(true, "-carRacerDevelopment");

            Assert.True(signing.UseCustomKeystore);
            Assert.Equal("Keystores/debug.keystore", signing.KeystoreName);
            Assert.Equal("android", signing.KeystorePass);
            Assert.Equal("androiddebugkey", signing.KeyaliasName);
            Assert.Equal("android", signing.KeyaliasPass);
            Assert.True(signing.IsSharedDebug);
            Assert.True(signing.IsAllowedFor(true));
        }

        [Fact]
        public void ABlankKeystoreNameIsNoKeystoreForADevelopmentBuild()
        {
            Assert.True(Choose(true, "-androidKeystoreName", string.Empty).IsSharedDebug);
        }

        [Fact]
        public void AReleaseBuildWithoutAKeystoreFallsBackToUnitysOwnDebugKey()
        {
            var signing = Choose(false, "-batchmode");

            Assert.False(signing.UseCustomKeystore);
            Assert.False(signing.IsSharedDebug);
            Assert.True(signing.IsAllowedFor(false));
        }

        [Fact]
        public void ABlankKeystoreNameIsNoKeystoreForAReleaseBuild()
        {
            Assert.False(Choose(false, "-androidKeystoreName", string.Empty).UseCustomKeystore);
        }

        [Theory]
        [InlineData("Keystores/debug.keystore")]
        [InlineData("./Keystores/debug.keystore")]
        [InlineData("Keystores//debug.keystore")]
        [InlineData("Keystores/../Keystores/debug.keystore")]
        public void AReleaseBuildRefusesTheSharedDebugKeystoreHoweverItIsSpelled(string keystore)
        {
            var signing = Choose(false, "-androidKeystoreName", keystore);

            Assert.True(signing.IsSharedDebug);
            Assert.False(signing.IsAllowedFor(false));
        }

        [Fact]
        public void AReleaseBuildRefusesTheSharedDebugKeystoreByAbsolutePath()
        {
            string absolute = Path.GetFullPath(AndroidSigning.SharedDebugKeystorePath);

            Assert.False(Choose(false, "-androidKeystoreName", absolute).IsAllowedFor(false));
        }

        [Fact]
        public void ADevelopmentBuildMayPassTheSharedDebugKeystore()
        {
            var signing = Choose(true, "-androidKeystoreName", AndroidSigning.SharedDebugKeystorePath);

            Assert.True(signing.IsAllowedFor(true));
        }

        [Theory]
        [InlineData("other/debug.keystore")]
        [InlineData("debug.keystore")]
        [InlineData("Keystores/debug.keystore.bak")]
        public void AnotherKeystoreIsNotTheSharedOne(string keystore)
        {
            var signing = Choose(false, "-androidKeystoreName", keystore);

            Assert.False(signing.IsSharedDebug);
            Assert.True(signing.IsAllowedFor(false));
        }

        [Fact]
        public void TheSharedKeystoreIsOnlyInUseWhenACustomKeystoreIs()
        {
            var signing = new AndroidSigning(false, AndroidSigning.SharedDebugKeystorePath, "android", "androiddebugkey", "android");

            Assert.False(signing.IsSharedDebug);
            Assert.True(signing.IsAllowedFor(false));
        }
    }
}
