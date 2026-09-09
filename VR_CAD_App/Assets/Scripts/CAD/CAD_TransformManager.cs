using System;
using UnityEngine;

namespace VRCAD.Core
{
    public enum AxisConstraint
    {
        None,
        LockX,
        LockY,
        LockZ,
        PlanarXY,
        PlanarXZ,
        PlanarYZ
    }

    public class CAD_TransformManager : MonoBehaviour
    {
        [Header("Snapping Configuration")]
        [SerializeField] private bool snapEnabled = true;
        [SerializeField] private float gridSnapIncrement = 0.05f; // 50mm (in meters)
        [SerializeField] private float angleSnapIncrement = 15f;  // 15 degrees
        [SerializeField] private AxisConstraint activeConstraint = AxisConstraint.None;

        [Header("Runtime State")]
        private CADObject activeManipulatedObject;
        private Vector3 initialGrabPosition;
        private Quaternion initialGrabRotation;

        public bool SnapEnabled { get => snapEnabled; set => snapEnabled = value; }
        public float GridSnapIncrement { get => gridSnapIncrement; set => gridSnapIncrement = Mathf.Max(0.001f, value); }
        public float AngleSnapIncrement { get => angleSnapIncrement; set => angleSnapIncrement = Mathf.Max(1f, value); }
        public AxisConstraint ActiveConstraint { get => activeConstraint; set => activeConstraint = value; }

        public Vector3 ApplyPositionSnap(Vector3 rawPosition)
        {
            if (!snapEnabled || gridSnapIncrement <= 0.0001f)
            {
                return rawPosition;
            }

            float x = Mathf.Round(rawPosition.x / gridSnapIncrement) * gridSnapIncrement;
            float y = Mathf.Round(rawPosition.y / gridSnapIncrement) * gridSnapIncrement;
            float z = Mathf.Round(rawPosition.z / gridSnapIncrement) * gridSnapIncrement;

            return new Vector3(x, y, z);
        }

        public Quaternion ApplyRotationSnap(Quaternion rawRotation)
        {
            if (!snapEnabled || angleSnapIncrement <= 0.1f)
            {
                return rawRotation;
            }

            Vector3 euler = rawRotation.eulerAngles;
            float x = Mathf.Round(euler.x / angleSnapIncrement) * angleSnapIncrement;
            float y = Mathf.Round(euler.y / angleSnapIncrement) * angleSnapIncrement;
            float z = Mathf.Round(euler.z / angleSnapIncrement) * angleSnapIncrement;

            return Quaternion.Euler(x, y, z);
        }

        public Vector3 ApplyAxisConstraint(Vector3 currentPosition, Vector3 initialPosition)
        {
            return activeConstraint switch
            {
                AxisConstraint.LockX => new Vector3(currentPosition.x, initialPosition.y, initialPosition.z),
                AxisConstraint.LockY => new Vector3(initialPosition.x, currentPosition.y, initialPosition.z),
                AxisConstraint.LockZ => new Vector3(initialPosition.x, initialPosition.y, currentPosition.z),
                AxisConstraint.PlanarXY => new Vector3(currentPosition.x, currentPosition.y, initialPosition.z),
                AxisConstraint.PlanarXZ => new Vector3(currentPosition.x, initialPosition.y, currentPosition.z),
                AxisConstraint.PlanarYZ => new Vector3(initialPosition.x, currentPosition.y, currentPosition.z),
                _ => currentPosition
            };
        }

        public void StartObjectManipulation(CADObject cadObject)
        {
            if (cadObject == null) return;
            activeManipulatedObject = cadObject;
            initialGrabPosition = cadObject.transform.position;
            initialGrabRotation = cadObject.transform.rotation;
        }

        public void UpdateObjectManipulation(CADObject cadObject, Vector3 targetPosition, Quaternion targetRotation)
        {
            if (cadObject == null) return;

            Vector3 constrainedPos = ApplyAxisConstraint(targetPosition, initialGrabPosition);
            Vector3 finalPos = ApplyPositionSnap(constrainedPos);
            Quaternion finalRot = ApplyRotationSnap(targetRotation);

            cadObject.transform.position = finalPos;
            cadObject.transform.rotation = finalRot;
        }

        public void EndObjectManipulation(CADObject cadObject)
        {
            if (activeManipulatedObject == cadObject)
            {
                // Apply final alignment snap
                cadObject.transform.position = ApplyPositionSnap(cadObject.transform.position);
                cadObject.transform.rotation = ApplyRotationSnap(cadObject.transform.rotation);
                activeManipulatedObject = null;
            }
        }

        public void NudgeObject(CADObject cadObject, Vector3 direction)
        {
            if (cadObject == null) return;
            float step = snapEnabled ? gridSnapIncrement : 0.01f;
            cadObject.transform.position = ApplyPositionSnap(cadObject.transform.position + direction.normalized * step);
        }

        public void RotateObject(CADObject cadObject, Vector3 axis, float angleDelta)
        {
            if (cadObject == null) return;
            cadObject.transform.Rotate(axis, angleDelta, Space.Self);
            cadObject.transform.rotation = ApplyRotationSnap(cadObject.transform.rotation);
        }

        public void ScaleObject(CADObject cadObject, Vector3 scaleMultiplier)
        {
            if (cadObject == null) return;
            Vector3 newScale = Vector3.Scale(cadObject.transform.localScale, scaleMultiplier);
            cadObject.transform.localScale = new Vector3(
                Mathf.Max(0.01f, newScale.x),
                Mathf.Max(0.01f, newScale.y),
                Mathf.Max(0.01f, newScale.z)
            );
            cadObject.Dimensions = cadObject.transform.localScale;
        }
    }
}
