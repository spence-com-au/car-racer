using CarRacer.Core.Items;
using CarRacer.Game.Net;
using UnityEngine;

namespace CarRacer.Game.Items
{
    /// <summary>
    /// Shows the track's item boxes (design doc §8): spinning yellow cubes that disappear while
    /// taken and come back when the host says they respawn, and are gone for an event the host
    /// has turned items off for. Which boxes are up comes from the
    /// session's <see cref="IItemView"/>.
    /// </summary>
    public sealed class ItemBoxes : MonoBehaviour
    {
        private const float SpinDegreesPerSecond = 90f;
        private const float Size = 1.1f;
        private static readonly Color Colour = new Color(0.98f, 0.78f, 0.15f);

        private IItemView _items;
        private Transform[] _boxes = new Transform[0];

        public void Initialise(IItemView items)
        {
            _items = items;
            _boxes = new Transform[items.Boxes.Count];
            for (int i = 0; i < _boxes.Length; i++)
            {
                _boxes[i] = Box($"Item box {i}", transform, items.Boxes.Position(i).ToUnity(), Colour);
            }
        }

        /// <summary>
        /// A box at <paramref name="position"/>: a tilted cube that <see cref="Spin"/> turns in place.
        /// Fake boxes (<see cref="ItemEffectViews"/>) are drawn with it too, so only their colour
        /// tells them apart.
        /// </summary>
        public static Transform Box(string name, Transform parent, Vector3 position, Color colour)
        {
            var box = new GameObject(name).transform;
            box.SetParent(parent, false);
            box.position = position;
            var cube = SimpleMaterials.Part(PrimitiveType.Cube, box, Vector3.zero, Vector3.one * Size, SimpleMaterials.Get(colour));
            cube.localRotation = Quaternion.Euler(35f, 0f, 35f);
            return box;
        }

        /// <summary>How far a box turns this frame, applied as <c>spin * box.rotation</c>.</summary>
        public static Quaternion Spin()
        {
            return Quaternion.Euler(0f, SpinDegreesPerSecond * Time.deltaTime, 0f);
        }

        private void Update()
        {
            if (_items == null)
            {
                return;
            }

            long now = NetworkSession.NowMs;
            var spin = Spin();
            for (int i = 0; i < _boxes.Length; i++)
            {
                bool up = _items.BoxesShown && _items.Boxes.IsUp(i, now);
                if (_boxes[i].gameObject.activeSelf != up)
                {
                    _boxes[i].gameObject.SetActive(up);
                }

                _boxes[i].rotation = spin * _boxes[i].rotation;
            }
        }
    }
}
