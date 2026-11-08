using System.Collections.Generic;
using CarRacer.Core.Items;
using CarRacer.Core.Net;
using CarRacer.Game.Audio;
using UnityEngine;

namespace CarRacer.Game.Items
{
    /// <summary>
    /// Shows what items leave on the track (design doc §8): a dark disc for each oil slick and a
    /// spinning box for each Fake box, kept in step with the session's hazards; Missiles, Seekers and
    /// Bombs in flight, eased towards the positions the host sends; a quickly expanding ring where a
    /// Shockwave or a Bomb goes off; and each item's sound where it is used (Lightning's everywhere
    /// at once), plus a bang where a Missile or Seeker ends or a Bomb goes off.
    /// </summary>
    public sealed class ItemEffectViews : MonoBehaviour
    {
        private const float RingSeconds = 0.4f;

        /// <summary>How quickly a drawn projectile closes on its latest known position (per second, exponential).</summary>
        private const float ProjectileSharpness = 20f;

        private static readonly Color OilColour = new Color(0.08f, 0.07f, 0.1f);
        private static readonly Color ShockwaveColour = new Color(0.55f, 0.8f, 1f);
        private static readonly Color BlastColour = new Color(1f, 0.55f, 0.15f);
        private static readonly Color MissileColour = new Color(0.9f, 0.15f, 0.12f);
        private static readonly Color BombColour = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color SeekerColour = new Color(0.25f, 0.45f, 1f);

        /// <summary>A Fake box is a shade more orange than a real one (<see cref="ItemBoxes"/>): easy to miss at speed, fair once you know.</summary>
        private static readonly Color FakeBoxColour = new Color(0.98f, 0.62f, 0.15f);

        private readonly Dictionary<ushort, Transform> _hazards = new Dictionary<ushort, Transform>();
        private readonly HashSet<ushort> _fakes = new HashSet<ushort>();
        private readonly Dictionary<ushort, Transform> _projectiles = new Dictionary<ushort, Transform>();
        private readonly HashSet<ushort> _seen = new HashSet<ushort>();
        private readonly List<ushort> _gone = new List<ushort>();
        private readonly List<(Transform Ring, float Age, float Radius)> _rings = new List<(Transform, float, float)>();
        private IItemView _items;

        public void Initialise(IItemView items)
        {
            _items = items;
            _items.ItemUsed += OnItemUsed;
            _items.ProjectileEnded += OnProjectileEnded;
        }

        private void OnDestroy()
        {
            if (_items != null)
            {
                _items.ItemUsed -= OnItemUsed;
                _items.ProjectileEnded -= OnProjectileEnded;
            }
        }

        private void OnItemUsed(ItemUsedMessage used)
        {
            if (used.Item == ItemKind.Shockwave)
            {
                AddRing(used.Position.ToUnity(), ItemEffects.ShockwaveRadius, ShockwaveColour);
            }

            // Lightning strikes everyone ahead wherever they are, so every phone hears it at full volume.
            if (used.Item == ItemKind.Lightning)
            {
                GameAudio.Play("explosion");
                return;
            }

            string sound = UseSound(used.Item);
            if (sound != null)
            {
                GameAudio.PlayAt(sound, used.Position.ToUnity());
            }
        }

        private static string UseSound(ItemKind item)
        {
            switch (item)
            {
                case ItemKind.Boost:
                    return "boost";
                case ItemKind.Shield:
                    return "shield";
                case ItemKind.Shockwave:
                    return "shockwave";
                case ItemKind.Missile:
                case ItemKind.Seeker:
                    return "missile";
                case ItemKind.OilSlick:
                case ItemKind.FakeBox:
                    return "oil";
                case ItemKind.Bomb:
                    return "bomb_throw";
                default:
                    return null;
            }
        }

        /// <summary>A Bomb goes off with a ring; a Missile or Seeker has hit someone or run out of time, and either way bangs.</summary>
        private void OnProjectileEnded(ItemKind kind, CarRacer.Core.Numerics.Vec3 position)
        {
            if (kind != ItemKind.Bomb)
            {
                GameAudio.PlayAt("explosion", position.ToUnity(), 0.8f);
                return;
            }

            AddRing(position.ToUnity(), ProjectileRules.BombBlastRadius, BlastColour);
            GameAudio.PlayAt("explosion", position.ToUnity());
        }

        private void AddRing(Vector3 centre, float radius, Color colour)
        {
            var ring = SimpleMaterials.Part(PrimitiveType.Cylinder, transform, Vector3.zero, Vector3.zero, SimpleMaterials.Get(colour));
            ring.position = centre + Vector3.down * 0.5f;
            _rings.Add((ring, 0f, radius));
        }

        private void Update()
        {
            if (_items == null)
            {
                return;
            }

            SyncHazards();
            SyncProjectiles();

            for (int i = _rings.Count - 1; i >= 0; i--)
            {
                var (ring, age, radius) = _rings[i];
                age += Time.deltaTime;
                if (age >= RingSeconds)
                {
                    Destroy(ring.gameObject);
                    _rings.RemoveAt(i);
                    continue;
                }

                float diameter = 2f * radius * (age / RingSeconds);
                ring.localScale = new Vector3(diameter, 0.03f, diameter);
                _rings[i] = (ring, age, radius);
            }
        }

        private void SyncProjectiles()
        {
            _seen.Clear();
            float blend = 1f - Mathf.Exp(-ProjectileSharpness * Time.deltaTime);
            foreach (var snapshot in _items.Projectiles)
            {
                _seen.Add(snapshot.EffectId);
                var target = snapshot.Position.ToUnity();
                bool homing = ProjectileRules.IsHoming(snapshot.Kind);
                if (!_projectiles.TryGetValue(snapshot.EffectId, out var view))
                {
                    view = homing
                        ? SimpleMaterials.Part(PrimitiveType.Cube, transform, Vector3.zero, new Vector3(0.35f, 0.35f, 1.1f),
                            SimpleMaterials.Get(snapshot.Kind == ItemKind.Seeker ? SeekerColour : MissileColour))
                        : SimpleMaterials.Part(PrimitiveType.Sphere, transform, Vector3.zero, Vector3.one * 0.8f, SimpleMaterials.Get(BombColour));
                    view.position = target;
                    _projectiles[snapshot.EffectId] = view;
                    continue;
                }

                var next = Vector3.Lerp(view.position, target, blend);
                var travel = next - view.position;
                travel.y = 0f;
                if (homing && travel.sqrMagnitude > 1e-6f)
                {
                    view.rotation = Quaternion.LookRotation(travel);
                }

                view.position = next;
            }

            RemoveUnseen(_projectiles);
        }

        private void SyncHazards()
        {
            _seen.Clear();
            var spin = ItemBoxes.Spin();
            foreach (var hazard in _items.Hazards.Active)
            {
                _seen.Add(hazard.EffectId);
                if (!_hazards.TryGetValue(hazard.EffectId, out var view))
                {
                    view = hazard.Item == ItemKind.FakeBox ? FakeBox(hazard) : OilDisc(hazard);
                    _hazards[hazard.EffectId] = view;
                }

                if (_fakes.Contains(hazard.EffectId))
                {
                    view.rotation = spin * view.rotation;
                }
            }

            RemoveUnseen(_hazards);
            _fakes.IntersectWith(_hazards.Keys);
        }

        private Transform OilDisc(Hazard hazard)
        {
            float diameter = 2f * ItemEffects.OilRadius;
            var disc = SimpleMaterials.Part(PrimitiveType.Cylinder, transform, Vector3.zero, new Vector3(diameter, 0.02f, diameter), SimpleMaterials.Get(OilColour));
            disc.position = hazard.Position.ToUnity() + Vector3.down * 0.55f;
            return disc;
        }

        /// <summary>A Fake box, drawn like the real ones (<see cref="ItemBoxes.Box"/>) but for its colour.</summary>
        private Transform FakeBox(Hazard hazard)
        {
            var box = ItemBoxes.Box($"Fake box {hazard.EffectId}", transform, hazard.Position.ToUnity() + Vector3.up * 0.15f, FakeBoxColour);
            _fakes.Add(hazard.EffectId);
            return box;
        }

        /// <summary>Destroys the views in <paramref name="views"/> whose ids weren't seen this frame.</summary>
        private void RemoveUnseen(Dictionary<ushort, Transform> views)
        {
            _gone.Clear();
            foreach (var id in views.Keys)
            {
                if (!_seen.Contains(id))
                {
                    _gone.Add(id);
                }
            }

            foreach (var id in _gone)
            {
                Destroy(views[id].gameObject);
                views.Remove(id);
            }
        }
    }
}
