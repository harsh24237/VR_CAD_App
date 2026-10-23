using System;
using UnityEngine;

namespace VRCAD.Core.CSG
{
    [Serializable]
    public struct CSGVertex
    {
        public Vector3 position;
        public Vector3 normal;
        public Vector2 uv;

        public CSGVertex(Vector3 position, Vector3 normal, Vector2 uv)
        {
            this.position = position;
            this.normal = normal;
            this.uv = uv;
        }

        public static CSGVertex Lerp(CSGVertex a, CSGVertex b, float t)
        {
            Vector3 pos = Vector3.Lerp(a.position, b.position, t);
            Vector3 norm = Vector3.Lerp(a.normal, b.normal, t);
            float normMag = norm.sqrMagnitude;
            if (normMag > 1e-8f)
            {
                norm /= Mathf.Sqrt(normMag);
            }
            else
            {
                norm = a.normal;
            }
            Vector2 uv = Vector2.Lerp(a.uv, b.uv, t);
            return new CSGVertex(pos, norm, uv);
        }

        public void Flip()
        {
            normal = -normal;
        }
    }
}
