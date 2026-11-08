using System.Collections.Generic;
using CarRacer.Core.Art;
using UnityEngine;

namespace CarRacer.Game.Driving
{
    /// <summary>
    /// Kenney Car Kit bodies (design doc §13), built from the OBJ and palette in
    /// <c>Resources/Models</c> by <see cref="ObjModel"/> rather than a model importer, and painted
    /// in each player's colour (<see cref="PaintRecolour"/>). Each model's mesh, and each paint
    /// strip's material in each colour, are made once and shared. In Monster Jam each body is drawn
    /// as a monster truck (<see cref="MonsterTruck"/>): the same model raised on big wheels, with
    /// its own shared mesh.
    /// </summary>
    public static class CarBodies
    {
        private static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();
        private static Color32[] _palette;
        private static int _paletteWidth;
        private static int _paletteHeight;

        // Keyed by the paint strip rather than the model, since the bodies share a handful of strips
        // and each material keeps its own copy of the palette texture for good.
        private static readonly Dictionary<(UvRect, Color), Material> Materials = new Dictionary<(UvRect, Color), Material>();

        /// <summary>Puts <paramref name="model"/> under <paramref name="visual"/> in <paramref name="colour"/>, as a monster truck if <paramref name="monster"/>; false if its files can't be loaded.</summary>
        public static bool TryBuild(Transform visual, Color colour, CarModel model, bool monster = false)
        {
            var mesh = MeshFor(model, monster);
            var material = mesh != null ? MaterialFor(model, colour) : null;
            if (material == null)
            {
                return false;
            }

            var body = new GameObject(model.Name);
            body.transform.SetParent(visual, false);
            body.transform.localScale = Vector3.one * model.Scale;
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = material;
            return true;
        }

        private static Mesh MeshFor(CarModel model, bool monster)
        {
            string key = monster ? model.Name + " truck" : model.Name;
            if (Meshes.TryGetValue(key, out var mesh))
            {
                return mesh;
            }

            var asset = Resources.Load<TextAsset>("Models/" + model.Name + ".obj");
            if (asset != null)
            {
                var obj = ObjModel.Parse(asset.text);
                mesh = Build(monster ? MonsterTruck.Build(obj) : obj, key);
            }
            else
            {
                Debug.LogError($"Car model '{model.Name}' is missing; using the placeholder body.");
            }

            Meshes[key] = mesh;
            return mesh;
        }

        /// <summary>All the model's parts in one mesh, so a car is one draw call.</summary>
        private static Mesh Build(ObjModel obj, string name)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var part in obj.Parts)
            {
                int start = positions.Count;
                foreach (var p in part.Positions)
                {
                    positions.Add(p.ToUnity());
                }

                foreach (var n in part.Normals)
                {
                    normals.Add(n.ToUnity());
                }

                foreach (var uv in part.Uvs)
                {
                    uvs.Add(uv.ToUnity());
                }

                foreach (int i in part.Triangles)
                {
                    triangles.Add(start + i);
                }
            }

            var mesh = new Mesh { name = name };
            if (positions.Count > ushort.MaxValue)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static Material MaterialFor(CarModel model, Color colour)
        {
            var key = (model.Paint, colour);
            if (Materials.TryGetValue(key, out var material))
            {
                return material;
            }

            var bytes = PaintedPalette(model, colour, out int width, out int height);
            if (bytes == null)
            {
                return null;
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = $"{CarModels.Palette} {ColorUtility.ToHtmlStringRGB(colour)}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.LoadRawTextureData(bytes);
            texture.Apply(false, true);

            material = SimpleMaterials.Textured(texture);
            Materials[key] = material;
            return material;
        }

        /// <summary>
        /// The shared palette as RGBA bytes, rows from the bottom up, with <paramref name="model"/>'s paint
        /// recoloured to <paramref name="colour"/>; null if the palette can't be loaded. The texture can't be
        /// read back once uploaded, so this is also what tests check.
        /// </summary>
        public static byte[] PaintedPalette(CarModel model, Color colour, out int width, out int height)
        {
            if (_palette == null)
            {
                var asset = Resources.Load<TextAsset>("Models/" + CarModels.Palette + ".png");
                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (asset == null || !source.LoadImage(asset.bytes))
                {
                    Debug.LogError("The car palette is missing; using the placeholder body.");
                    Object.DestroyImmediate(source);
                    width = 0;
                    height = 0;
                    return null;
                }

                _palette = source.GetPixels32();
                _paletteWidth = source.width;
                _paletteHeight = source.height;
                Object.DestroyImmediate(source);
            }

            width = _paletteWidth;
            height = _paletteHeight;

            // Color32 is laid out as RGBA bytes, rows from the bottom up, as PaintRecolour expects.
            var bytes = new byte[_palette.Length * 4];
            for (int i = 0; i < _palette.Length; i++)
            {
                var c = _palette[i];
                bytes[i * 4] = c.r;
                bytes[i * 4 + 1] = c.g;
                bytes[i * 4 + 2] = c.b;
                bytes[i * 4 + 3] = c.a;
            }

            Color32 paint = colour;
            PaintRecolour.Recolour(bytes, width, height, model.Paint, paint.r, paint.g, paint.b);
            return bytes;
        }
    }
}
