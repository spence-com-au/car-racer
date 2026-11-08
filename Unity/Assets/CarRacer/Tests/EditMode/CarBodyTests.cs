using System.Collections.Generic;
using System.Linq;
using CarRacer.Core.Art;
using CarRacer.Core.Track;
using CarRacer.Game;
using CarRacer.Game.Arenas;
using CarRacer.Game.Driving;
using NUnit.Framework;
using UnityEngine;

namespace CarRacer.Tests
{
    /// <summary>The Kenney car loads from its OBJ and palette, painted in the player's colour, and tracks get their Racing Kit scenery.</summary>
    public class CarBodyTests
    {
        [Test]
        public void TheRaceCarBuildsAsOnePaintedMesh()
        {
            var visual = new GameObject("Body").transform;
            try
            {
                Assert.IsTrue(CarBodies.TryBuild(visual, Color.blue, CarModels.Race));
                var body = visual.GetChild(0);
                var mesh = body.GetComponent<MeshFilter>().sharedMesh;
                Assert.Greater(mesh.GetIndexCount(0) / 3, 1000u);
                Assert.AreEqual(0f, mesh.bounds.min.y, 0.01f);
                Assert.Greater(mesh.bounds.center.z, -0.5f);

                var material = body.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreEqual(SimpleMaterials.ShaderName, material.shader.name);
                Assert.IsNotNull(material.mainTexture);
                Assert.AreEqual(512, material.mainTexture.width);
            }
            finally
            {
                Object.DestroyImmediate(visual.gameObject);
            }
        }

        [Test]
        public void TracksGetSceneryBakedIntoAFewMeshes()
        {
            var parent = new GameObject("Track").transform;
            var meshes = new List<Mesh>();
            try
            {
                TrackDressing.Build(parent, TrackCatalog.Layouts[0], "Oval", meshes);
                Assert.AreEqual(parent.childCount, meshes.Count);
                Assert.Greater(parent.childCount, 2);
                Assert.Less(parent.childCount, 12);
                foreach (Transform piece in parent)
                {
                    Assert.Greater(piece.GetComponent<MeshFilter>().sharedMesh.vertexCount, 0, piece.name);
                    Assert.AreEqual(SimpleMaterials.ShaderName, piece.GetComponent<MeshRenderer>().sharedMaterial.shader.name);
                    Assert.IsNull(piece.GetComponent<Collider>());
                }
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
                foreach (var mesh in meshes)
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void EveryGarageBodyBuilds()
        {
            foreach (var model in CarModels.Bodies)
            {
                var visual = new GameObject(model.Name).transform;
                try
                {
                    Assert.IsTrue(CarBodies.TryBuild(visual, Color.magenta, model), model.Name);
                    var mesh = visual.GetChild(0).GetComponent<MeshFilter>().sharedMesh;
                    Assert.AreEqual(CarModels.Length, mesh.bounds.size.z * model.Scale, 0.05f, model.Name);
                }
                finally
                {
                    Object.DestroyImmediate(visual.gameObject);
                }
            }
        }

        [Test]
        public void EachColourGetsItsOwnPaintAndSharesTheMesh()
        {
            var a = new GameObject("A").transform;
            var b = new GameObject("B").transform;
            try
            {
                CarBodies.TryBuild(a, Color.red, CarModels.Race);
                CarBodies.TryBuild(b, Color.green, CarModels.Race);
                Assert.AreSame(a.GetChild(0).GetComponent<MeshFilter>().sharedMesh, b.GetChild(0).GetComponent<MeshFilter>().sharedMesh);
                Assert.AreNotSame(a.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial, b.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
            }
        }

        [Test]
        public void BodiesWithTheSamePaintShareItsMaterial()
        {
            var race = CarModels.Race;
            var twin = CarModels.Bodies.First(m => m != race && m.Paint.Equals(race.Paint));
            var other = CarModels.Bodies.First(m => !m.Paint.Equals(race.Paint));
            var a = new GameObject(race.Name).transform;
            var b = new GameObject(twin.Name).transform;
            var c = new GameObject(other.Name).transform;
            try
            {
                Assert.IsTrue(CarBodies.TryBuild(a, Color.red, race));
                Assert.IsTrue(CarBodies.TryBuild(b, Color.red, twin));
                Assert.IsTrue(CarBodies.TryBuild(c, Color.red, other));
                var material = a.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
                Assert.AreSame(material, b.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial, twin.Name);
                Assert.AreNotSame(material, c.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial, other.Name);
            }
            finally
            {
                Object.DestroyImmediate(a.gameObject);
                Object.DestroyImmediate(b.gameObject);
                Object.DestroyImmediate(c.gameObject);
            }
        }

        [Test]
        public void ThePlayersColourLandsInTheModelsPaintRowsOnly()
        {
            var model = CarModels.Race;
            var red = CarBodies.PaintedPalette(model, Color.red, out int width, out int height);
            var green = CarBodies.PaintedPalette(model, Color.green, out _, out _);
            Assert.IsNotNull(red);
            Assert.AreEqual(width * height * 4, red.Length);

            // Rows count from the bottom up, so the paint's v range maps straight onto them.
            int x = (int)((model.Paint.UMin + model.Paint.UMax) * width / 2f);
            int y = (int)((model.Paint.VMin + model.Paint.VMax) * height / 2f);
            int i = (y * width + x) * 4;
            Assert.Greater(red[i], 0, "red paint");
            Assert.AreEqual(0, red[i + 1], "red paint");
            Assert.AreEqual(0, red[i + 2], "red paint");
            Assert.Greater(green[i + 1], 0, "green paint");
            Assert.AreEqual(0, green[i], "green paint");
            Assert.AreEqual(0, green[i + 2], "green paint");

            // The bottom and top rows are outside the paint strip, so they're the palette untouched.
            for (int j = 0; j < width * 4; j++)
            {
                Assert.AreEqual(red[j], green[j], "bottom row");
                Assert.AreEqual(red[red.Length - 1 - j], green[green.Length - 1 - j], "top row");
            }
        }

        [Test]
        public void AMonsterTruckIsTheSameModelRaisedWithItsOwnMesh()
        {
            var car = new GameObject("Car").transform;
            var truck = new GameObject("Truck").transform;
            try
            {
                Assert.IsTrue(CarBodies.TryBuild(car, Color.red, CarModels.Race));
                Assert.IsTrue(CarBodies.TryBuild(truck, Color.red, CarModels.Race, monster: true));
                var carBody = car.GetChild(0);
                var truckBody = truck.GetChild(0);
                var carMesh = carBody.GetComponent<MeshFilter>().sharedMesh;
                var truckMesh = truckBody.GetComponent<MeshFilter>().sharedMesh;
                Assert.AreNotSame(carMesh, truckMesh);
                Assert.AreEqual(CarModels.Race.Name + " truck", truckMesh.name);
                Assert.Greater(truckMesh.bounds.max.y, carMesh.bounds.max.y);
                Assert.AreSame(carBody.GetComponent<MeshRenderer>().sharedMaterial, truckBody.GetComponent<MeshRenderer>().sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(car.gameObject);
                Object.DestroyImmediate(truck.gameObject);
            }
        }
    }
}
