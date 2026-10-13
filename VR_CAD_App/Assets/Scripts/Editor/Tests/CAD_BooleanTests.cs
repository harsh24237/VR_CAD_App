using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRCAD.Core;
using VRCAD.Core.CSG;
using Debug = UnityEngine.Debug;

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

        #region Helpers

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

        public static float CalculateSignedVolume(Mesh mesh)
        {
            if (mesh == null) return 0f;
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            float totalVol = 0f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 p0 = verts[tris[i]];
                Vector3 p1 = verts[tris[i + 1]];
                Vector3 p2 = verts[tris[i + 2]];
                totalVol += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
            }
            return Mathf.Abs(totalVol);
        }

        #endregion

        #region Group A: Strict Mesh Validation Tests (Problem 1)

        [Test]
        public void TestValidation_NormalCube_Passes()
        {
            Mesh cube = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            var val = CSGModel.ValidateMesh(cube);
            Assert.IsTrue(val.isValid, $"Normal cube must be valid: {val.message}");
            Assert.AreEqual(0, val.openEdgeCount, "Cube must have 0 open edges");
            Assert.AreEqual(0, val.nonManifoldEdgeCount, "Cube must have 0 non-manifold edges");
            Assert.AreEqual(0, val.degenerateTriangleCount, "Cube must have 0 degenerate triangles");
            Assert.AreEqual(0, val.inconsistentWindingCount, "Cube must have 0 inconsistent winding edges");
        }

        [Test]
        public void TestValidation_NormalCylinder_Passes()
        {
            Mesh cyl = CAD_ShapeManager.CreateCylinderMesh(0.5f, 1.0f, 24);
            var val = CSGModel.ValidateMesh(cyl);
            Debug.Log($"[CYLINDER_CHECK] isValid={val.isValid}, {val.message}");
            Assert.IsTrue(val.isValid, $"Normal cylinder must be valid: {val.message}");
        }

        [Test]
        public void TestValidation_OpenCube_Fails()
        {
            // Create a cube missing one face (2 triangles removed = open boundary)
            Mesh cube = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            int[] tris = cube.triangles;
            int[] openTris = new int[tris.Length - 6];
            Array.Copy(tris, openTris, openTris.Length);

            Mesh openMesh = new Mesh();
            openMesh.vertices = cube.vertices;
            openMesh.triangles = openTris;

            var val = CSGModel.ValidateMesh(openMesh);
            Assert.IsFalse(val.isValid, "Open cube must fail validation");
            Assert.Greater(val.openEdgeCount, 0, "Open cube must report open boundary edges");
        }

        [Test]
        public void TestValidation_NonManifoldEdge_Fails()
        {
            // Create a cube with an extra third triangle attached to an edge (non-manifold fin)
            Mesh cube = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            int[] tris = cube.triangles;
            var newTris = new List<int>(tris);
            var newVerts = new List<Vector3>(cube.vertices);

            // Add an extra vertex and attach triangle to existing edge (vertex 0 and 1)
            int extraIdx = newVerts.Count;
            newVerts.Add(new Vector3(2f, 2f, 2f));
            newTris.Add(0);
            newTris.Add(1);
            newTris.Add(extraIdx);

            Mesh nonManifoldMesh = new Mesh();
            nonManifoldMesh.SetVertices(newVerts);
            nonManifoldMesh.SetTriangles(newTris, 0);

            var val = CSGModel.ValidateMesh(nonManifoldMesh);
            Assert.IsFalse(val.isValid, "Non-manifold edge must fail validation");
            Assert.Greater(val.nonManifoldEdgeCount, 0, "Must report non-manifold edges");
        }

        [Test]
        public void TestValidation_InconsistentWinding_Fails()
        {
            // Reverse the vertex winding of a single triangle
            Mesh cube = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            int[] tris = cube.triangles;
            // Swap first triangle indices
            int temp = tris[0];
            tris[0] = tris[1];
            tris[1] = temp;

            Mesh flippedMesh = new Mesh();
            flippedMesh.vertices = cube.vertices;
            flippedMesh.triangles = tris;

            var val = CSGModel.ValidateMesh(flippedMesh);
            Assert.IsFalse(val.isValid, "Inconsistent winding must fail validation");
            Assert.Greater(val.inconsistentWindingCount, 0, "Must report inconsistent winding");
        }

        [Test]
        public void TestValidation_DegenerateTriangle_Fails()
        {
            // Create a mesh with a collapsed zero-area triangle
            Mesh cube = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            int[] tris = cube.triangles;
            tris[0] = tris[1]; // Collapse triangle 0

            Mesh degenMesh = new Mesh();
            degenMesh.vertices = cube.vertices;
            degenMesh.triangles = tris;

            var val = CSGModel.ValidateMesh(degenMesh);
            Assert.IsFalse(val.isValid, "Degenerate triangle must fail validation");
            Assert.Greater(val.degenerateTriangleCount, 0, "Must report degenerate triangles");
        }

        [Test]
        public void TestValidation_NaNVertex_Fails()
        {
            Mesh cube = CAD_ShapeManager.CreateBoxMesh(1f, 1f, 1f);
            Vector3[] verts = cube.vertices;
            verts[0] = new Vector3(float.NaN, 0f, 0f);

            Mesh nanMesh = new Mesh();
            nanMesh.vertices = verts;
            nanMesh.triangles = cube.triangles;

            var val = CSGModel.ValidateMesh(nanMesh);
            Assert.IsFalse(val.isValid, "NaN coordinates must fail validation");
            Assert.IsTrue(val.hasNonFiniteCoordinates, "Must flag non-finite coordinates");
        }

        #endregion

        #region Group B: Volume & CSG Correctness Tests (Problems 1, 3, 4, 5)

        [Test]
        public void TestUnion_OverlappingCubes_VolumeCorrectness()
        {
            // Two unit cubes overlapping by 0.5 along X: Expected Volume = 1.0 + 1.0 - 0.5 = 1.5
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0f, 0f), Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Union);
            Assert.AreEqual(BooleanResultStatus.Success, res.status, res.message);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount);
            Assert.AreEqual(0, res.validation.nonManifoldEdgeCount);

            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.AreEqual(1.5f, vol, 0.05f, "Union volume must equal 1.5");
        }

        [Test]
        public void TestSubtract_OverlappingCubes_VolumeCorrectness()
        {
            // Unit cube minus unit cube overlapping by 0.5 along X: Expected Volume = 1.0 - 0.5 = 0.5
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0f, 0f), Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Success, res.status, res.message);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount);

            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.AreEqual(0.5f, vol, 0.05f, "Subtract volume must equal 0.5");
        }

        [Test]
        public void TestIntersect_OverlappingCubes_VolumeCorrectness()
        {
            // Unit cube intersected with unit cube overlapping by 0.5 along X: Expected Volume = 0.5
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0f, 0f), Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Intersect);
            Assert.AreEqual(BooleanResultStatus.Success, res.status, res.message);
            Assert.IsTrue(res.validation.isValid, res.validation.message);

            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.AreEqual(0.5f, vol, 0.05f, "Intersect volume must equal 0.5");
        }

        [Test]
        public void TestUnion_DisjointCubes_ProducesMultiBodySolid()
        {
            // Two cubes 5m apart: Expected Volume = 2.0, connectedComponentCount = 2, openEdgeCount = 0
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(5f, 0f, 0f), Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Union);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount);
            Assert.AreEqual(2, res.validation.connectedComponentCount);

            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.AreEqual(2.0f, vol, 0.05f, "Disjoint union volume must equal 2.0");
        }

        [Test]
        public void TestSubtract_DisjointCubes_ReturnsUnchanged()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(10f, 0f, 0f), Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Unchanged, res.status, "Disjoint subtract must return Unchanged");
            Assert.AreEqual(cubeA.MeshFilter.sharedMesh, res.resultMesh, "Target mesh must remain untouched");
        }

        [Test]
        public void TestSubtract_EngulfingCutter_ProducesEmptyResult()
        {
            // Target is a 1m cube, Cutter is a huge 10m cube centered at same location
            CADObject target = CreateCubeObject("SmallTarget", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cutter = CreateCubeObject("HugeCutter", Vector3.zero, Vector3.one * 10f, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(target, cutter, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Empty, res.status, "Engulfed target subtraction must produce Empty status");
        }

        [Test]
        public void TestIntersect_DisjointCubes_ProducesEmptyResult()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(10f, 0f, 0f), Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Intersect);
            Assert.AreEqual(BooleanResultStatus.Empty, res.status, "Disjoint intersection must produce Empty status");
        }

        [Test]
        public void TestUnion_IdenticalCubes_PreservesGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", Vector3.zero, Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Union);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);
            Assert.IsTrue(res.validation.isValid);

            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.AreEqual(1.0f, vol, 0.05f, "Union of identical cubes must equal 1.0 volume");
        }

        [Test]
        public void TestSubtract_IdenticalCubes_ProducesEmptyResult()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", Vector3.zero, Vector3.one, Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Empty, res.status, "Subtracting identical cube must produce Empty");
        }

        #endregion

        #region Group C: Transforms & Mirrored Scaling (Problem 5)

        [Test]
        public void TestSubtract_RotatedOperands_ProducesCorrectGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            Quaternion rot45 = Quaternion.Euler(0f, 45f, 0f);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.4f, 0f, 0f), Vector3.one, rot45);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount);
        }

        [Test]
        public void TestUnion_MirroredScaleOperands_ProducesValidGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            // Mirrored scale on X axis: negative determinant transform
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0f, 0f), new Vector3(-1f, 1f, 1f), Quaternion.identity);

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Union);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);
            Assert.IsTrue(res.validation.isValid, $"Mirrored union must produce valid closed mesh: {res.validation.message}");
            Assert.AreEqual(0, res.validation.inconsistentWindingCount, "Mirrored transform must preserve outward winding");
            Assert.AreEqual(0, res.validation.openEdgeCount);
        }

        [Test]
        public void TestSubtract_NonUniformScaleOperands_ProducesValidGeometry()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cutter = CreateCubeObject("Cutter", new Vector3(0.3f, 0f, 0f), new Vector3(0.5f, 2.0f, 0.8f), Quaternion.Euler(15f, 30f, 0f));

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cutter, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
        }

        #endregion

        #region Group D: CAD Hole Cutting Tests (Problem 2)

        [Test]
        public void TestHole_ThroughHoleOnTopFace_CutsThroughGeometry()
        {
            CADObject cube = CreateCubeObject("Cube", Vector3.zero, Vector3.one, Quaternion.identity);
            Vector3 topFacePoint = new Vector3(0f, 0.5f, 0f);
            Vector3 topFaceNormal = Vector3.up;

            var options = new HoleOptions
            {
                holeType = HoleType.Through,
                radius = 0.2f,
                depth = 1.0f,
                radialSegments = 24,
                overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
            };

            BooleanResult res = booleanManager.CutHoleAdvanced(cube, topFacePoint, topFaceNormal, options);
            Assert.AreEqual(BooleanResultStatus.Success, res.status, res.message);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount, "Through-hole result must be closed solid");

            // Cylinder hole volume ≈ π * r^2 * h = π * 0.04 * 1 ≈ 0.1256
            // Expected remaining volume ≈ 1.0 - 0.1256 ≈ 0.874
            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.Less(vol, 0.95f, "Volume must decrease after through-hole cut");
            Assert.Greater(vol, 0.80f, "Remaining volume must match cylindrical cut");
        }

        [Test]
        public void TestHole_BlindHoleOnTopFace_TerminatesAtDepth()
        {
            CADObject cube = CreateCubeObject("Cube", Vector3.zero, Vector3.one, Quaternion.identity);
            Vector3 topFacePoint = new Vector3(0f, 0.5f, 0f);
            Vector3 topFaceNormal = Vector3.up;

            var options = new HoleOptions
            {
                holeType = HoleType.Blind,
                radius = 0.2f,
                depth = 0.4f, // 400mm blind depth into 1m cube
                radialSegments = 24,
                overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
            };

            BooleanResult res = booleanManager.CutHoleAdvanced(cube, topFacePoint, topFaceNormal, options);
            Assert.AreEqual(BooleanResultStatus.Success, res.status, res.message);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount, "Blind hole must have valid bottom cap");

            // Remaining volume should be greater than through hole (blind hole removes less material)
            float vol = CalculateSignedVolume(res.resultMesh);
            Assert.Less(vol, 0.98f);
            Assert.Greater(vol, 0.90f);
        }

        [Test]
        public void TestHole_SideFaceHole_CutsHorizontally()
        {
            CADObject cube = CreateCubeObject("Cube", Vector3.zero, Vector3.one, Quaternion.identity);
            // Right face (+X)
            Vector3 sideFacePoint = new Vector3(0.5f, 0f, 0f);
            Vector3 sideFaceNormal = Vector3.right;

            var options = new HoleOptions
            {
                holeType = HoleType.Through,
                radius = 0.15f,
                depth = 1.0f,
                radialSegments = 24,
                overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
            };

            BooleanResult res = booleanManager.CutHoleAdvanced(cube, sideFacePoint, sideFaceNormal, options);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);
            Assert.IsTrue(res.validation.isValid, res.validation.message);
            Assert.AreEqual(0, res.validation.openEdgeCount);
        }

        [Test]
        public void TestHole_InvalidParameters_PreservesOriginalMesh()
        {
            CADObject cube = CreateCubeObject("Cube", Vector3.zero, Vector3.one, Quaternion.identity);
            Mesh origMesh = cube.MeshFilter.sharedMesh;

            var invalidOptions = new HoleOptions
            {
                holeType = HoleType.Through,
                radius = -0.5f, // Invalid negative radius
                depth = 1.0f,
                radialSegments = 24,
                overcutMargin = 0.005f
            };

            BooleanResult res = booleanManager.CutHoleAdvanced(cube, Vector3.zero, Vector3.up, invalidOptions);
            Assert.AreEqual(BooleanResultStatus.Failure, res.status);
            Assert.AreSame(origMesh, cube.MeshFilter.sharedMesh, "Mesh must remain untouched on invalid input");
        }

        #endregion

        #region Group E: State Management & Undo/Redo (Problem 4)

        [Test]
        public void TestUndoRedo_StandardSubtract_RestoresMeshAndCutter()
        {
            CADObject cubeA = CreateCubeObject("CubeA", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cubeB = CreateCubeObject("CubeB", new Vector3(0.5f, 0.5f, 0.5f), Vector3.one, Quaternion.identity);

            Mesh initialMesh = UnityEngine.Object.Instantiate(cubeA.MeshFilter.sharedMesh);
            int initialTriCount = initialMesh.triangles.Length;

            BooleanResult res = booleanManager.ExecuteBoolean(cubeA, cubeB, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Success, res.status);

            cubeA.SetMesh(res.resultMesh);
            cubeB.gameObject.SetActive(false);

            var cmd = new BooleanCommand(cubeA, cubeB, initialMesh, res.resultMesh, "Test Subtract");
            undoRedoManager.RecordCommand(cmd);

            // Undo
            bool undoSuccess = undoRedoManager.Undo();
            Assert.IsTrue(undoSuccess);
            Assert.AreEqual(initialTriCount, cubeA.MeshFilter.sharedMesh.triangles.Length);
            Assert.IsTrue(cubeB.gameObject.activeSelf, "Cutter must reactivate on undo");

            // Redo
            bool redoSuccess = undoRedoManager.Redo();
            Assert.IsTrue(redoSuccess);
            Assert.AreEqual(res.resultMesh.triangles.Length, cubeA.MeshFilter.sharedMesh.triangles.Length);
            Assert.IsFalse(cubeB.gameObject.activeSelf, "Cutter must deactivate on redo");
        }

        [Test]
        public void TestUndoRedo_EmptySubtract_RestoresTargetAndCutter()
        {
            CADObject target = CreateCubeObject("SmallTarget", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cutter = CreateCubeObject("HugeCutter", Vector3.zero, Vector3.one * 10f, Quaternion.identity);

            Mesh initialMesh = UnityEngine.Object.Instantiate(target.MeshFilter.sharedMesh);
            BooleanResult res = booleanManager.ExecuteBoolean(target, cutter, BooleanOperation.Subtract);
            Assert.AreEqual(BooleanResultStatus.Empty, res.status);

            target.gameObject.SetActive(false);
            cutter.gameObject.SetActive(false);

            var cmd = new BooleanCommand(target, cutter, initialMesh, null, "Test Empty Subtract", targetBecameEmpty: true);
            undoRedoManager.RecordCommand(cmd);

            // Undo should restore both target and cutter active state
            undoRedoManager.Undo();
            Assert.IsTrue(target.gameObject.activeSelf, "Target must reactivate on undo from empty");
            Assert.IsTrue(cutter.gameObject.activeSelf, "Cutter must reactivate on undo from empty");

            // Redo should deactivate both again
            undoRedoManager.Redo();
            Assert.IsFalse(target.gameObject.activeSelf, "Target must deactivate on redo to empty");
            Assert.IsFalse(cutter.gameObject.activeSelf, "Cutter must deactivate on redo to empty");
        }

        #endregion

        #region Group F: Sequential Operations & Benchmarks

        [Test]
        public void TestRepeatedCSG_SequentialOperations_PreservesManifoldness()
        {
            // 1. Create Cube
            CADObject baseCube = CreateCubeObject("BaseCube", Vector3.zero, Vector3.one, Quaternion.identity);

            // 2. Cut Through-Hole on Top
            var hole1Options = new HoleOptions
            {
                holeType = HoleType.Through,
                radius = 0.15f,
                depth = 1.0f,
                radialSegments = 24,
                overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
            };
            BooleanResult h1 = booleanManager.CutHoleAdvanced(baseCube, new Vector3(0f, 0.5f, 0f), Vector3.up, hole1Options);
            Assert.AreEqual(BooleanResultStatus.Success, h1.status);
            Assert.IsTrue(h1.validation.isValid, $"Hole 1 invalid: {h1.validation.message}");
            baseCube.SetMesh(h1.resultMesh);

            // 3. Union another cube on the side (+X)
            CADObject sideCube = CreateCubeObject("SideCube", new Vector3(0.8f, 0f, 0f), new Vector3(0.6f, 0.6f, 0.6f), Quaternion.identity);
            BooleanResult u1 = booleanManager.ExecuteBoolean(baseCube, sideCube, BooleanOperation.Union);
            Assert.AreEqual(BooleanResultStatus.Success, u1.status);
            Assert.IsTrue(u1.validation.isValid, $"Union invalid: {u1.validation.message}");
            baseCube.SetMesh(u1.resultMesh);
            sideCube.gameObject.SetActive(false);

            // 4. Cut Side Hole through the merged geometry
            var hole2Options = new HoleOptions
            {
                holeType = HoleType.Through,
                radius = 0.1f,
                depth = 2.0f,
                radialSegments = 24,
                overcutMargin = CSGTolerance.ThroughHoleOvercutMargin
            };
            BooleanResult h2 = booleanManager.CutHoleAdvanced(baseCube, new Vector3(1.1f, 0f, 0f), Vector3.right, hole2Options);
            Assert.AreEqual(BooleanResultStatus.Success, h2.status, h2.message);
            Assert.IsTrue(h2.validation.isValid, $"Hole 2 invalid: {h2.validation.message}");
            Assert.AreEqual(0, h2.validation.openEdgeCount, "Sequential CSG result must have 0 open edges");
            Assert.AreEqual(0, h2.validation.nonManifoldEdgeCount, "Sequential CSG result must have 0 non-manifold edges");
        }

        [Test]
        public void TestBenchmark_CSGOperations_MeasuresExecutionTimes()
        {
            CADObject cube = CreateCubeObject("BenchCube", Vector3.zero, Vector3.one, Quaternion.identity);
            CADObject cyl24 = CreateCylinderObject("Cyl24", Vector3.zero, 0.25f, 1.5f, Quaternion.identity);

            var sw = Stopwatch.StartNew();
            BooleanResult res24 = booleanManager.ExecuteBoolean(cube, cyl24, BooleanOperation.Subtract);
            sw.Stop();
            long ms24 = sw.ElapsedMilliseconds;

            Assert.AreEqual(BooleanResultStatus.Success, res24.status);
            Assert.IsTrue(res24.validation.isValid);

            Debug.Log($"[CSG BENCHMARK] Cube - Cylinder(24-seg): {ms24} ms, Triangles: {res24.validation.triangleCount}");
            Assert.Less(ms24, 500, "Operation should complete within practical interactive frame budget (<500ms)");
        }

        #endregion

        #region Regression Menu Item Runner

        [MenuItem("CAD/Tests/Run CSG Boolean Regression Tests")]
        public static void RunAllTestsFromMenu()
        {
            Debug.Log("==== [CSG REGRESSION TEST RUNNER] Starting Comprehensive Suite ====");
            var suite = new CAD_BooleanTests();
            int passed = 0;
            int failed = 0;
            var failedNames = new List<string>();

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
                    failedNames.Add(testName);
                }
                finally
                {
                    suite.TearDown();
                }
            }

            // Group A: Validation
            Run("Validation_NormalCube", suite.TestValidation_NormalCube_Passes);
            Run("Validation_NormalCylinder", suite.TestValidation_NormalCylinder_Passes);
            Run("Validation_OpenCube", suite.TestValidation_OpenCube_Fails);
            Run("Validation_NonManifoldEdge", suite.TestValidation_NonManifoldEdge_Fails);
            Run("Validation_InconsistentWinding", suite.TestValidation_InconsistentWinding_Fails);
            Run("Validation_DegenerateTriangle", suite.TestValidation_DegenerateTriangle_Fails);
            Run("Validation_NaNVertex", suite.TestValidation_NaNVertex_Fails);

            // Group B: Volume & CSG Operations
            Run("Union_OverlappingCubes_Volume", suite.TestUnion_OverlappingCubes_VolumeCorrectness);
            Run("Subtract_OverlappingCubes_Volume", suite.TestSubtract_OverlappingCubes_VolumeCorrectness);
            Run("Intersect_OverlappingCubes_Volume", suite.TestIntersect_OverlappingCubes_VolumeCorrectness);
            Run("Union_DisjointCubes_MultiBody", suite.TestUnion_DisjointCubes_ProducesMultiBodySolid);
            Run("Subtract_DisjointCubes_Unchanged", suite.TestSubtract_DisjointCubes_ReturnsUnchanged);
            Run("Subtract_EngulfingCutter_Empty", suite.TestSubtract_EngulfingCutter_ProducesEmptyResult);
            Run("Intersect_DisjointCubes_Empty", suite.TestIntersect_DisjointCubes_ProducesEmptyResult);
            Run("Union_IdenticalCubes", suite.TestUnion_IdenticalCubes_PreservesGeometry);
            Run("Subtract_IdenticalCubes", suite.TestSubtract_IdenticalCubes_ProducesEmptyResult);

            // Group C: Transforms & Mirroring
            Run("Subtract_RotatedOperands", suite.TestSubtract_RotatedOperands_ProducesCorrectGeometry);
            Run("Union_MirroredScaleOperands", suite.TestUnion_MirroredScaleOperands_ProducesValidGeometry);
            Run("Subtract_NonUniformScaleOperands", suite.TestSubtract_NonUniformScaleOperands_ProducesValidGeometry);

            // Group D: CAD Holes
            Run("Hole_ThroughHoleOnTopFace", suite.TestHole_ThroughHoleOnTopFace_CutsThroughGeometry);
            Run("Hole_BlindHoleOnTopFace", suite.TestHole_BlindHoleOnTopFace_TerminatesAtDepth);
            Run("Hole_SideFaceHole", suite.TestHole_SideFaceHole_CutsHorizontally);
            Run("Hole_InvalidParameters", suite.TestHole_InvalidParameters_PreservesOriginalMesh);

            // Group E: State Management & Undo/Redo
            Run("UndoRedo_StandardSubtract", suite.TestUndoRedo_StandardSubtract_RestoresMeshAndCutter);
            Run("UndoRedo_EmptySubtract", suite.TestUndoRedo_EmptySubtract_RestoresTargetAndCutter);

            // Group F: Sequential Operations & Benchmark
            Run("RepeatedCSG_SequentialOperations", suite.TestRepeatedCSG_SequentialOperations_PreservesManifoldness);
            Run("Benchmark_CSGOperations", suite.TestBenchmark_CSGOperations_MeasuresExecutionTimes);

            Debug.Log($"==== [CSG REGRESSION TEST RUNNER] Finished: {passed} PASSED, {failed} FAILED. Failed: [{string.Join(", ", failedNames)}] ====");
        }

        #endregion
    }
}
