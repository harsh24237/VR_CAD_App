using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core.CSG
{
    public struct CSGPlane
    {
        public const float Epsilon = 1e-5f;

        public Vector3 normal;
        public float w;

        public const int Coplanar = 0;
        public const int Front = 1;
        public const int Back = 2;
        public const int Spanning = 3;

        public CSGPlane(Vector3 normal, float w)
        {
            this.normal = normal;
            this.w = w;
        }

        public static CSGPlane FromPoints(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float mag = n.sqrMagnitude;
            if (mag > 1e-12f)
            {
                n /= Mathf.Sqrt(mag);
            }
            else
            {
                n = Vector3.up;
            }
            return new CSGPlane(n, Vector3.Dot(n, a));
        }

        public static CSGPlane FromPolygon(List<CSGVertex> vertices)
        {
            if (vertices == null || vertices.Count < 3)
            {
                return new CSGPlane(Vector3.up, 0f);
            }

            // Newell's method for robust polygon normal calculation
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 current = vertices[i].position;
                Vector3 next = vertices[(i + 1) % vertices.Count].position;
                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            float mag = normal.sqrMagnitude;
            if (mag > 1e-12f)
            {
                normal /= Mathf.Sqrt(mag);
            }
            else
            {
                normal = Vector3.Cross(vertices[1].position - vertices[0].position,
                                       vertices[2].position - vertices[0].position).normalized;
                if (normal.sqrMagnitude < 1e-12f) normal = Vector3.up;
            }

            return new CSGPlane(normal, Vector3.Dot(normal, vertices[0].position));
        }

        public void Flip()
        {
            normal = -normal;
            w = -w;
        }

        public void SplitPolygon(
            CSGPolygon polygon,
            List<CSGPolygon> coplanarFront,
            List<CSGPolygon> coplanarBack,
            List<CSGPolygon> front,
            List<CSGPolygon> back)
        {
            int polygonType = 0;
            var types = new List<int>(polygon.vertices.Count);

            for (int i = 0; i < polygon.vertices.Count; i++)
            {
                float t = Vector3.Dot(normal, polygon.vertices[i].position) - w;
                int type = (t < -Epsilon) ? Back : (t > Epsilon) ? Front : Coplanar;
                polygonType |= type;
                types.Add(type);
            }

            switch (polygonType)
            {
                case Coplanar:
                    float dot = Vector3.Dot(normal, polygon.plane.normal);
                    if (dot > 0f)
                    {
                        coplanarFront.Add(polygon);
                    }
                    else
                    {
                        coplanarBack.Add(polygon);
                    }
                    break;

                case Front:
                    front.Add(polygon);
                    break;

                case Back:
                    back.Add(polygon);
                    break;

                case Spanning:
                    var f = new List<CSGVertex>();
                    var b = new List<CSGVertex>();

                    for (int i = 0; i < polygon.vertices.Count; i++)
                    {
                        int j = (i + 1) % polygon.vertices.Count;
                        int ti = types[i];
                        int tj = types[j];
                        CSGVertex vi = polygon.vertices[i];
                        CSGVertex vj = polygon.vertices[j];

                        if (ti != Back) f.Add(vi);
                        if (ti != Front) b.Add(vi);

                        if ((ti | tj) == Spanning)
                        {
                            float denom = Vector3.Dot(normal, vj.position - vi.position);
                            float t = Mathf.Abs(denom) > 1e-9f
                                ? Mathf.Clamp01((w - Vector3.Dot(normal, vi.position)) / denom)
                                : 0.5f;

                            CSGVertex v = CSGVertex.Lerp(vi, vj, t);
                            f.Add(v);
                            b.Add(v);
                        }
                    }

                    if (f.Count >= 3) front.Add(new CSGPolygon(f, polygon.sharedData));
                    if (b.Count >= 3) back.Add(new CSGPolygon(b, polygon.sharedData));
                    break;
            }
        }
    }
}
