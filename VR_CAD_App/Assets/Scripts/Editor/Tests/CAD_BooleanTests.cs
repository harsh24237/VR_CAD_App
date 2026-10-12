using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRCAD.Core;
using VRCAD.Core.CSG;

namespace VRCAD.Tests
{
    [TestFixture]
    public class CAD_BooleanTests
    {
        private GameObject rootObj;
        private CAD_BooleanManager booleanManager;
        private CAD_UndoRedoManager undoRedoManager;

        [SetUp]
        public void SetUp()
        {
            rootObj = new GameObject("TestRoot_CSG");
            booleanManager = rootObj.AddComponent<CAD_BooleanManager>();
            undoRedoManager = rootObj.AddComponent<CAD_UndoRedoManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (rootObj != null)
            {
                UnityEngine.Object.DestroyImmediate(rootObj);
            }
        }

        private CADObject CreateCubeObject(string name, Vector3 position, Vector3 scale, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(rootObj.transform);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.transform.rotation = rotation;

            CADObject cadObj = go.AddComponent<CADObject>();
            Mesh mesh = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            cadObj.SetMesh(mesh);
            return cadObj;
        }

        private CADObject CreateCylinderObject(string name, Vector3 position, float radius, float height, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(rootObj.transform);
            go.transform.position = position;
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one;

            CADObject cadObj = go.AddComponent<CADObject>();
            Mesh mesh = CAD_ShapeManager.CreateCylinderMesh(radius, height, 24);
            cadObj.SetMesh(mesh);
            return cadObj;
        }

        [Test]
        public void TestUnion_OverlappingCubes_ProducesValidClosedMesh()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0f, 0f), Vector3.one, Quaternion.identity);

            bool success = booleanManager.PerformBoolean(cubeA, cubeB, BooleanOperation.Union);

            Assert.IsTrue(success, "Union of overlapping cubes should succeed.");
            Mesh res = cubeA.MeshFilter.sharedMesh;
            Assert.IsNotNull(res, "Result mesh must not be null.");

            var val = CSGModel.ValidateMesh(res);
            Assert.IsTrue(val.isValid, $"Result mesh must be valid. Message: {val.message}");
            Assert.Greater(res.vertexCount, 0, "Vertex count must be > 0.");
            Assert.Greater(res.triangles.Length, 0, "Triangle count must be > 0.");

            // Combined bounds on X should span from -0.5 to 1.0 (size ~1.5)
            Bounds b = res.bounds;
            Assert.AreEqual(1.5f, b.size.x, 0.05f, "Bounds X size should reflect merged cubes.");
            Assert.IsFalse(cubeB.gameObject.activeSelf, "Merged tool object should be deactivated.");
        }

        [Test]
        public void TestSubtract_OverlappingCubes_CutsGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            // Cutter cube cutting off the corner (+X, +Y, +Z)
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0.5f, 0.5f), Vector3.one, Quaternion.identity);

            int initialVertCount = cubeA.MeshFilter.sharedMesh.vertexCount;
            bool success = booleanManager.PerformBoolean(cubeA, cubeB, BooleanOperation.Subtract);

            Assert.IsTrue(success, "Subtract of overlapping corner should succeed.");
            Mesh res = cubeA.MeshFilter.sharedMesh;
            Assert.IsNotNull(res, "Result mesh must not be null.");

            var val = CSGModel.ValidateMesh(res);
            Assert.IsTrue(val.isValid, $"Cut mesh must be valid: {val.message}");
            Assert.Greater(res.triangles.Length, 12 * 3, "Cut mesh must have new interior facets.");
            Assert.IsFalse(cubeB.gameObject.activeSelf, "Cutter must be deactivated on successful subtract.");
        }

        [Test]
        public void TestSubtract_CylinderFromCube_CutsHole()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            // Cylinder along Y axis penetrating the cube vertically
            CADObject cylB = CreateCylinderObject("CylinderB", Vector3.zero, 0.25f, 1.5f, Quaternion.identity);

            bool success = booleanManager.PerformBoolean(cubeA, cylB, BooleanOperation.Subtract);

            Assert.IsTrue(success, "Subtract of penetrating cylinder must succeed.");
            Mesh res = cubeA.MeshFilter.sharedMesh;
            Assert.IsNotNull(res, "Result mesh must not be null.");

            var val = CSGModel.ValidateMesh(res);
            Assert.IsTrue(val.isValid, $"Hole cut mesh must be valid: {val.message}");
            Assert.Greater(res.triangles.Length, 24, "Result must contain cylindrical hole wall triangles.");
        }

        [Test]
        public void TestSubtract_RotatedOperands_ProducesCorrectGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            // Rotate cutter 45 degrees around Y
            Quaternion rot45 = Quaternion.Euler(0f, 45f, 0f);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.4f, 0f, 0f), Vector3.one, rot45);

            bool success = booleanManager.PerformBoolean(cubeA, cubeB, BooleanOperation.Subtract);

            Assert.IsTrue(success, "Subtract with rotated cutter should succeed.");
            Mesh res = cubeA.MeshFilter.sharedMesh;
            Assert.IsNotNull(res, "Result mesh must not be null.");

            var val = CSGModel.ValidateMesh(res);
            Assert.IsTrue(val.isValid, $"Rotated cut mesh must be valid: {val.message}");
        }

        [Test]
        public void TestSubtract_DisjointOperands_DoesNotDeactivateCutter()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(10f, 0f, 0f), Vector3.one, Quaternion.identity);

            Mesh originalMesh = cubeA.MeshFilter.sharedMesh;
            int originalVertCount = originalMesh.vertexCount;

            bool success = booleanManager.PerformBoolean(cubeA, cubeB, BooleanOperation.Subtract);

            Assert.IsFalse(success, "Subtract of completely disjoint shapes must return false.");
            Assert.AreSame(originalMesh, cubeA.MeshFilter.sharedMesh, "Target mesh must remain untouched.");
            Assert.IsTrue(cubeB.gameObject.activeSelf, "Cutter must NOT be removed or deactivated when non-overlapping.");
        }

        [Test]
        public void TestUnion_DisjointOperands_MergesGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(5f, 0f, 0f), Vector3.one, Quaternion.identity);

            bool success = booleanManager.PerformBoolean(cubeA, cubeB, BooleanOperation.Union);

            Assert.IsTrue(success, "Disjoint union should succeed by combining bodies.");
            Mesh res = cubeA.MeshFilter.sharedMesh;
            Assert.IsNotNull(res, "Merged mesh must not be null.");
            Assert.AreEqual(24, res.triangles.Length / 3, "2 cubes of 12 tris each = 24 tris in disjoint union.");
            Assert.IsFalse(cubeB.gameObject.activeSelf, "Disjoint tool object should be deactivated after merge.");
        }

        [Test]
        public void TestUndoRedo_RestoresMeshAndCutterState()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0.5f, 0.5f), Vector3.one, Quaternion.identity);

            Mesh initialMesh = UnityEngine.Object.Instantiate(cubeA.MeshFilter.sharedMesh);
            int initialTriCount = initialMesh.triangles.Length;

            bool success = booleanManager.PerformBoolean(cubeA, cubeB, BooleanOperation.Subtract);
            Assert.IsTrue(success);

            Mesh cutMesh = cubeA.MeshFilter.sharedMesh;
            Assert.IsFalse(cubeB.gameObject.activeSelf, "Cutter is initially deactivated after subtract.");

            // Record command and test Undo
            var cmd = new BooleanCommand(cubeA, cubeB, initialMesh, cutMesh, "Test Subtract");
            undoRedoManager.RecordCommand(cmd);

            // 1. Test Undo
            bool undoSuccess = undoRedoManager.Undo();
            Assert.IsTrue(undoSuccess, "Undo should succeed.");
            Assert.AreEqual(initialTriCount, cubeA.MeshFilter.sharedMesh.triangles.Length, "Undo must restore original mesh.");
            Assert.IsTrue(cubeB.gameObject.activeSelf, "Undo must reactivate the cutter tool.");

            // 2. Test Redo
            bool redoSuccess = undoRedoManager.Redo();
            Assert.IsTrue(redoSuccess, "Redo should succeed.");
            Assert.AreEqual(cutMesh.triangles.Length, cubeA.MeshFilter.sharedMesh.triangles.Length, "Redo must restore cut mesh.");
            Assert.IsFalse(cubeB.gameObject.activeSelf, "Redo must deactivate the cutter tool again.");
        }

        [MenuItem("CAD/Tests/Run CSG Boolean Regression Tests")]
        public static void RunAllTestsFromMenu()
        {
            Debug.Log("==== [CSG REGRESSION TEST RUNNER] Starting Suite ====");
            var suite = new CAD_BooleanTests();
            int passed = 0;
            int failed = 0;

            void Run(string testName, Action testAction)
            {
                suite.SetUp();
                try
                {
                    testAction();
                    Debug.Log($"[PASS] {testName}");
                    passed++;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FAIL] {testName}: {ex.Message}\n{ex.StackTrace}");
                    failed++;
                }
                finally
                {
                    suite.TearDown();
                }
            }

            Run("Union_OverlappingCubes", suite.TestUnion_OverlappingCubes_ProducesValidClosedMesh);
            Run("Subtract_OverlappingCubes", suite.TestSubtract_OverlappingCubes_CutsGeometry);
            Run("Subtract_CylinderFromCube", suite.TestSubtract_CylinderFromCube_CutsHole);
            Run("Subtract_RotatedOperands", suite.TestSubtract_RotatedOperands_ProducesCorrectGeometry);
            Run("Subtract_DisjointOperands", suite.TestSubtract_DisjointOperands_DoesNotDeactivateCutter);
            Run("Union_DisjointOperands", suite.TestUnion_DisjointOperands_MergesGeometry);
            Run("UndoRedo_Restoration", suite.TestUndoRedo_RestoresMeshAndCutterState);

            Debug.Log($"==== [CSG REGRESSION TEST RUNNER] Finished: {passed} PASSED, {failed} FAILED ====");
        }
    }
}
