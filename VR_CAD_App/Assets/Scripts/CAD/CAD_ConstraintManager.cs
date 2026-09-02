using System;
using UnityEngine;

namespace VRCAD.Core
{
    public class CAD_ConstraintManager : MonoBehaviour
    {
        [Header("Constraint Rules")]
        [SerializeField] private bool enforceOrthogonal = true;
        [SerializeField] private bool preventSelfIntersection = true;
        [SerializeField] private float minDimensionSize = 0.005f; // 5mm minimum
        [SerializeField] private float maxDimensionSize = 5.0f;    // 5 meters maximum

        public bool EnforceOrthogonal { get => enforceOrthogonal; set => enforceOrthogonal = value; }
        public float MinDimensionSize => minDimensionSize;
        public float MaxDimensionSize => maxDimensionSize;

        public Vector3 ConstrainDimensions(Vector3 inputDimensions)
        {
            return new Vector3(
                Mathf.Clamp(inputDimensions.x, minDimensionSize, maxDimensionSize),
                Mathf.Clamp(inputDimensions.y, minDimensionSize, maxDimensionSize),
                Mathf.Clamp(inputDimensions.z, minDimensionSize, maxDimensionSize)
            );
        }

        public Vector3 AlignToClosestPrincipalAxis(Vector3 direction)
        {
            Vector3 abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (abs.x >= abs.y && abs.x >= abs.z)
            {
                return new Vector3(Mathf.Sign(direction.x), 0, 0);
            }
            if (abs.y >= abs.x && abs.y >= abs.z)
            {
                return new Vector3(0, Mathf.Sign(direction.y), 0);
            }
            return new Vector3(0, 0, Mathf.Sign(direction.z));
        }

        public Quaternion SnapRotationToCardinalAngles(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            float snapAngle = 90f;

            float x = Mathf.Round(euler.x / snapAngle) * snapAngle;
            float y = Mathf.Round(euler.y / snapAngle) * snapAngle;
            float z = Mathf.Round(euler.z / snapAngle) * snapAngle;

            return Quaternion.Euler(x, y, z);
        }

        public bool ValidateWatertightManifold(Mesh mesh)
        {
            if (mesh == null) return false;
            // Quick edge-manifold verification (each edge shared by exactly 2 triangles)
            int[] tris = mesh.triangles;
            var edgeDict = new System.Collections.Generic.Dictionary<(int, int), int>();

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t + 0];
                int b = tris[t + 1];
                int c = tris[t + 2];

                void Add(int v1, int v2)
                {
                    var edge = v1 < v2 ? (v1, v2) : (v2, v1);
                    if (!edgeDict.ContainsKey(edge)) edgeDict[edge] = 0;
                    edgeDict[edge]++;
                }

                Add(a, b);
                Add(b, c);
                Add(c, a);
            }

            foreach (var count in edgeDict.Values)
            {
                if (count != 2) return false; // Open boundary or non-manifold
            }

            return true;
        }
    }
}
