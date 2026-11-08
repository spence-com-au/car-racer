using System;
using System.Linq;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Art
{
    /// <summary>
    /// Turns any garage body into a monster truck for Monster Jam (design doc §7.0): its four road
    /// wheels (the parts named <c>wheel-front-left</c> and so on; a spare on the back stays with the
    /// body) are made <see cref="WheelScale"/> times bigger and pushed out to clear the body (and,
    /// where a short wheelbase would have them cut through each other, forward or back to meet
    /// halfway between the axles), the whole model is lifted so the wheels still sit on the ground,
    /// and the body rides higher still, above its new wheels. UVs are kept, so the paint and its
    /// recolouring work as before.
    /// </summary>
    public static class MonsterTruck
    {
        public const float WheelScale = 2.2f;

        /// <summary>How much of the wheels' growth in radius the body rises on top of the lift, so it sits up between them.</summary>
        public const float BodyRise = 0.6f;

        public static bool IsRoadWheel(ObjPart part) =>
            part.Name.StartsWith("wheel-", StringComparison.Ordinal) && part.Name.Split('-').Length == 3;

        public static ObjModel Build(ObjModel car)
        {
            float radius = 0f;
            float middle = 0f;
            int wheels = 0;
            foreach (var part in car.Parts)
            {
                if (IsRoadWheel(part) && part.Positions.Count > 0)
                {
                    var (min, max) = ObjModel.BoundsOf(part.Positions);
                    radius += (max.Y - min.Y) / 2f;
                    middle += (min.Z + max.Z) / 2f;
                    wheels++;
                }
            }

            radius = wheels > 0 ? radius / wheels : 0f;
            middle = wheels > 0 ? middle / wheels : 0f;
            float lift = (WheelScale - 1f) * radius;
            var truck = new ObjModel();
            foreach (var part in car.Parts)
            {
                var copy = new ObjPart { Name = part.Name, Material = part.Material };
                copy.Normals.AddRange(part.Normals);
                copy.Uvs.AddRange(part.Uvs);
                copy.Triangles.AddRange(part.Triangles);
                if (IsRoadWheel(part) && part.Positions.Count > 0)
                {
                    var (min, max) = ObjModel.BoundsOf(part.Positions);
                    var centre = (min + max) * 0.5f;
                    float outward = Math.Sign(centre.X) * (max.X - min.X) / 2f * (WheelScale - 1f);
                    float pastMiddle = (max.Z - min.Z) / 2f * WheelScale - Math.Abs(centre.Z - middle);
                    float apart = Math.Sign(centre.Z - middle) * Math.Max(0f, pastMiddle);
                    var moved = centre + new Vec3(outward, lift, apart);
                    foreach (var p in part.Positions)
                    {
                        copy.Positions.Add(moved + (p - centre) * WheelScale);
                    }
                }
                else
                {
                    var up = new Vec3(0f, lift + BodyRise * lift, 0f);
                    foreach (var p in part.Positions)
                    {
                        copy.Positions.Add(p + up);
                    }
                }

                truck.Parts.Add(copy);
            }

            (truck.Min, truck.Max) = ObjModel.BoundsOf(truck.Parts.SelectMany(part => part.Positions));
            return truck;
        }
    }
}
