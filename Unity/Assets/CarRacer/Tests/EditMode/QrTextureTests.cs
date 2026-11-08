using CarRacer.Core.Discovery;
using CarRacer.Core.Platform;
using CarRacer.Game.Menu;
using NUnit.Framework;
using UnityEngine;

namespace CarRacer.Tests
{
    /// <summary>
    /// The join QR code's texture is the encoder's modules, one pixel each and the right way up, inside a white border.
    /// </summary>
    public class QrTextureTests
    {
        private const int QuietZone = 4;
        private const int FinderSize = 7;

        private static readonly string Link = JoinLink.Make("192.168.1.20", 7777);

        [Test]
        public void TheTextureHasOnePixelPerModuleInAWhiteBorder()
        {
            var code = QrCode.Encode(Link);
            var image = Render(Link);

            int size = code.Size + 2 * QuietZone;
            Assert.AreEqual(size, image.GetLength(0));
            Assert.AreEqual(size, image.GetLength(1));
            for (int row = 0; row < size; row++)
            {
                for (int column = 0; column < size; column++)
                {
                    bool inBorder = row < QuietZone || row >= size - QuietZone
                        || column < QuietZone || column >= size - QuietZone;
                    Assert.IsFalse(inBorder && image[row, column], $"row {row}, column {column}");
                }
            }
        }

        [Test]
        public void TheFindersAreInTheTopLeftTopRightAndBottomLeftCorners()
        {
            var image = Render(Link);

            int far = image.GetLength(0) - QuietZone - FinderSize;
            AssertFinder(image, QuietZone, QuietZone);
            AssertFinder(image, QuietZone, far);
            AssertFinder(image, far, QuietZone);
        }

        [Test]
        public void EveryModuleIsDrawnWhereTheCodeHasIt()
        {
            var code = QrCode.Encode(Link);
            var image = Render(Link);

            for (int y = 0; y < code.Size; y++)
            {
                for (int x = 0; x < code.Size; x++)
                {
                    Assert.AreEqual(code.IsDark(x, y), image[y + QuietZone, x + QuietZone], $"module ({x}, {y})");
                }
            }
        }

        /// <summary>
        /// The texture as a viewer sees it: <c>[row, column]</c> from the top left, true where a pixel is dark.
        /// </summary>
        private static bool[,] Render(string text)
        {
            var texture = QrTexture.Build(text);
            try
            {
                int size = texture.width;
                Assert.AreEqual(size, texture.height);
                var pixels = texture.GetPixels32();
                var image = new bool[size, size];
                for (int row = 0; row < size; row++)
                {
                    for (int column = 0; column < size; column++)
                    {
                        // Texture rows run bottom-up.
                        image[row, column] = pixels[(size - 1 - row) * size + column].r == 0;
                    }
                }

                return image;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        /// <summary>A finder pattern: a dark ring, a light one, then a dark 3x3 centre.</summary>
        private static void AssertFinder(bool[,] image, int top, int left)
        {
            for (int dy = 0; dy < FinderSize; dy++)
            {
                for (int dx = 0; dx < FinderSize; dx++)
                {
                    bool isDark = Mathf.Max(Mathf.Abs(dx - 3), Mathf.Abs(dy - 3)) != 2;
                    Assert.AreEqual(
                        isDark,
                        image[top + dy, left + dx],
                        $"finder at row {top}, column {left}: pixel ({dx}, {dy})");
                }
            }
        }
    }
}
