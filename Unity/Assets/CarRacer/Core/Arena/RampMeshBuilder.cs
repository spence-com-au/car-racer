using CarRacer.Core.Numerics;
using CarRacer.Core.Track;

namespace CarRacer.Core.Arena
{
    /// <summary>Builds the wedge mesh for a <see cref="Ramp"/>; the Unity layer copies it with `ToUnityMesh`.</summary>
    public static class RampMeshBuilder
    {
        /// <summary>The slope up to the lip, a vertical back and two triangular sides; flat-shaded.</summary>
        public static MeshData Build(Ramp ramp)
        {
            Vec3 side = Vec3.Cross(Vec3.Up, ramp.Forward) * (ramp.Width / 2f);
            Vec3 foot = ramp.Foot;
            Vec3 lip = ramp.Lip;
            Vec3 back = lip + new Vec3(0f, -ramp.Height, 0f);

            var mesh = new MeshData();
            AddFace(mesh, foot - side, lip - side, lip + side, foot + side);
            AddFace(mesh, back - side, back + side, lip + side, lip - side);
            AddFace(mesh, foot - side, back - side, lip - side);
            AddFace(mesh, foot + side, lip + side, back + side);
            return mesh;
        }

        /// <summary>A fan of triangles over a convex face, with each vertex given the face's normal.</summary>
        private static void AddFace(MeshData mesh, params Vec3[] corners)
        {
            int start = mesh.Vertices.Count;
            Vec3 normal = Vec3.Cross(corners[1] - corners[0], corners[2] - corners[0]).Normalized;
            foreach (Vec3 corner in corners)
            {
                mesh.Vertices.Add(corner);
                mesh.Normals.Add(normal);
                mesh.Uvs.Add(default);
            }

            for (int i = 1; i + 1 < corners.Length; i++)
            {
                mesh.Triangles.Add(start);
                mesh.Triangles.Add(start + i);
                mesh.Triangles.Add(start + i + 1);
            }
        }
    }
}
