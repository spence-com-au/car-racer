using System;
using System.Collections.Generic;
using System.IO;
using CarRacer.Core.Cast;
using CarRacer.Core.Platform;
using Xunit;

namespace CarRacer.Core.Tests
{
    public class AppUpdateTests
    {
        private const string ApkUrl = "https://github.com/spence-com-au/car-racer/releases/download/v0.14.0/CarRacer.apk";

        /// <summary>GitHub's latest-release response, cut down to the fields the game reads.</summary>
        private static string Release(string tag, params string[] assets)
        {
            var list = new List<object>();
            foreach (string name in assets)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["browser_download_url"] = $"https://github.com/spence-com-au/car-racer/releases/download/{tag}/{name}",
                });
            }

            return Json.Write(new Dictionary<string, object> { ["tag_name"] = tag, ["assets"] = list });
        }

        [Fact]
        public void ANewerReleaseIsOfferedWithItsApk()
        {
            var release = AppUpdate.Newer(Release("v0.14.0", "CarRacer-server-v0.14.0-linux-x86_64.tar.gz", "CarRacer.apk"), "0.13.0");
            Assert.Equal("0.14.0", release.Version);
            Assert.Equal(ApkUrl, release.ApkUrl);
        }

        [Theory]
        [InlineData("v0.13.0")]
        [InlineData("v0.12.1")]
        public void TheSameOrAnOlderReleaseIsNot(string tag)
        {
            Assert.Null(AppUpdate.Newer(Release(tag, "CarRacer.apk"), "0.13.0"));
        }

        [Fact]
        public void VersionsCompareByNumberNotText()
        {
            Assert.NotNull(AppUpdate.Newer(Release("v0.10.0", "CarRacer.apk"), "0.9.0"));
            Assert.NotNull(AppUpdate.Newer(Release("v1.0.0", "CarRacer.apk"), "0.13.2"));
        }

        [Fact]
        public void AReleaseWithoutTheApkIsNot()
        {
            Assert.Null(AppUpdate.Newer(Release("v0.14.0", "CarRacer-server-v0.14.0-linux-x86_64.tar.gz"), "0.13.0"));
        }

        [Theory]
        [InlineData("not json", "0.13.0")]
        [InlineData("{}", "0.13.0")]
        [InlineData("{\"tag_name\":\"latest\"}", "0.13.0")]
        public void AnythingUnreadableOffersNothing(string json, string running)
        {
            Assert.Null(AppUpdate.Newer(json, running));
        }

        [Fact]
        public void AnUnreadableRunningVersionOffersNothing()
        {
            Assert.Null(AppUpdate.Newer(Release("v0.14.0", "CarRacer.apk"), string.Empty));
        }

        [Fact]
        public void TheMenuSaysNothingUntilThereIsAnUpdate()
        {
            var status = new UpdateStatus();
            Assert.Null(AppUpdate.ButtonLabel(status));
            Assert.Null(AppUpdate.Line(status, "0.13.0"));
        }

        [Fact]
        public void TheMenuFollowsTheUpdate()
        {
            var status = new UpdateStatus { State = UpdateState.Available, Release = new AppRelease { Version = "0.14.0", ApkUrl = ApkUrl } };
            Assert.Equal("Update", AppUpdate.ButtonLabel(status));
            Assert.Equal("Version 0.14.0 is out; this phone has 0.13.0.", AppUpdate.Line(status, "0.13.0"));

            status.State = UpdateState.Downloading;
            status.Progress = 0.426f;
            Assert.Equal("Downloading", AppUpdate.ButtonLabel(status));
            Assert.Equal("Downloading version 0.14.0: 42%", AppUpdate.Line(status, "0.13.0"));

            status.State = UpdateState.Downloaded;
            Assert.Equal("Install", AppUpdate.ButtonLabel(status));
            Assert.Equal("Version 0.14.0 is ready. Tap Install, and if Android asks, let Car Racer install apps.", AppUpdate.Line(status, "0.13.0"));

            status.State = UpdateState.Failed;
            status.Error = "Cannot resolve destination host";
            Assert.Equal("Try again", AppUpdate.ButtonLabel(status));
            Assert.Equal("Updating to 0.14.0 failed: Cannot resolve destination host", AppUpdate.Line(status, "0.13.0"));
        }

        [Fact]
        public void TheReleaseWorkflowUploadsTheApkUnderTheNameTheGameLooksFor()
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!Directory.Exists(Path.Combine(root.FullName, ".github")))
            {
                root = root.Parent;
            }

            string workflow = File.ReadAllText(Path.Combine(root.FullName, ".github", "workflows", "release.yaml"));
            Assert.Contains($"files: 'build/Android/{AppUpdate.ApkName}'", workflow);
        }
    }
}
