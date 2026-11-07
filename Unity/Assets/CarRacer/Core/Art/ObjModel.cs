using System;
using System.Collections.Generic;
using System.Globalization;
using CarRacer.Core.Numerics;

namespace CarRacer.Core.Art
{
    /// <summary>One named part of an <see cref="ObjModel"/>, ready to become a mesh: one vertex per triangle corner.</summary>
    public sealed class ObjPart
    {
        public string Name = string.Empty;

        /// <summary>The OBJ material (<c>usemtl</c>) the part is drawn in; a group using several becomes several parts.</summary>
        public string Material = string.Empty;
        public readonly List<Vec3> Positions = new List<Vec3>();
        public readonly List<Vec3> Normals = new List<Vec3>();
        public readonly List<Vec2> Uvs = new List<Vec2>();

        /// <summary>Three indices per triangle, wound clockwise seen from outside (Unity's front face).</summary>
        public readonly List<int> Triangles = new List<int>();
    }

    /// <summary>
    /// A Wavefront OBJ model (the format Kenney ships alongside FBX), read by the game itself so
    /// the art needs no model importer (design doc §13). OBJ is right-handed and Unity left-handed,
    /// so x is mirrored and each triangle's winding reversed; the model keeps facing +z.
    /// </summary>
    public sealed class ObjModel
    {
        public readonly List<ObjPart> Parts = new List<ObjPart>();

        public Vec3 Min { get; internal set; }
        public Vec3 Max { get; internal set; }

        public int TriangleCount
        {
            get
            {
                int count = 0;
                foreach (var part in Parts)
                {
                    count += part.Triangles.Count / 3;
                }

                return count;
            }
        }

        public static ObjModel Parse(string text)
        {
            var model = new ObjModel();
            var positions = new List<Vec3>();
            var normals = new List<Vec3>();
            var uvs = new List<Vec2>();
            ObjPart part = null;
            var corners = new List<(int V, int T, int N)>();
            var min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vec3(float.MinValue, float.MinValue, float.MinValue);

            // Kenney's Racing Kit models repeat a group with an identical copy of its faces; drawing both doubles the cost.
            var seenFaces = new HashSet<string>();

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] f = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (f[0])
                {
                    case "v":
                        var p = new Vec3(-Float(f[1]), Float(f[2]), Float(f[3]));
                        positions.Add(p);
                        min = new Vec3(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
                        max = new Vec3(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
                        break;
                    case "vn":
                        normals.Add(new Vec3(-Float(f[1]), Float(f[2]), Float(f[3])));
                        break;
                    case "vt":
                        uvs.Add(new Vec2(Float(f[1]), Float(f[2])));
                        break;
                    case "g":
                    case "o":
                        part = new ObjPart { Name = f.Length > 1 ? f[1] : string.Empty, Material = part != null ? part.Material : string.Empty };
                        model.Parts.Add(part);
                        break;
                    case "usemtl":
                        string material = f.Length > 1 ? f[1] : string.Empty;
                        if (part == null || part.Triangles.Count > 0)
                        {
                            part = new ObjPart { Name = part != null ? part.Name : string.Empty, Material = material };
                            model.Parts.Add(part);
                        }
                        else
                        {
                            part.Material = material;
                        }

                        break;
                    case "f":
                        if (part == null)
                        {
                            part = new ObjPart();
                            model.Parts.Add(part);
                        }

                        corners.Clear();
                        for (int i = 1; i < f.Length; i++)
                        {
                            corners.Add(Corner(f[i], positions.Count, uvs.Count, normals.Count));
                        }

                        if (!seenFaces.Add(part.Name + "\n" + part.Material + "\n" + string.Join(" ", corners)))
                        {
                            break;
                        }

                        // A fan of triangles, each wound the other way for the mirrored x.
                        for (int i = 1; i + 1 < corners.Count; i++)
                        {
                            AddCorner(part, corners[0], positions, uvs, normals);
                            AddCorner(part, corners[i + 1], positions, uvs, normals);
                            AddCorner(part, corners[i], positions, uvs, normals);
                        }

                        break;
                }
            }

            model.Parts.RemoveAll(p => p.Triangles.Count == 0);
            model.Min = positions.Count > 0 ? min : Vec3.Zero;
            model.Max = positions.Count > 0 ? max : Vec3.Zero;
            return model;
        }

        private static void AddCorner(ObjPart part, (int V, int T, int N) c, List<Vec3> positions, List<Vec2> uvs, List<Vec3> normals)
        {
            part.Triangles.Add(part.Positions.Count);
            part.Positions.Add(positions[c.V]);
            part.Uvs.Add(c.T >= 0 ? uvs[c.T] : new Vec2(0f, 0f));
            part.Normals.Add(c.N >= 0 ? normals[c.N] : Vec3.Up);
        }

        /// <summary>"v", "v/t", "v//n" or "v/t/n", 1-based or negative (counting back from the end).</summary>
        private static (int V, int T, int N) Corner(string token, int positionCount, int uvCount, int normalCount)
        {
            string[] parts = token.Split('/');
            int v = Index(parts[0], positionCount);
            int t = parts.Length > 1 && parts[1].Length > 0 ? Index(parts[1], uvCount) : -1;
            int n = parts.Length > 2 && parts[2].Length > 0 ? Index(parts[2], normalCount) : -1;
            return (v, t, n);
        }

        private static int Index(string token, int count)
        {
            int i = int.Parse(token, CultureInfo.InvariantCulture);
            return i < 0 ? count + i : i - 1;
        }

        private static float Float(string token) => float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
