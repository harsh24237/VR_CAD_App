using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCAD.Core.CSG
{
    public class CSGPolygon
    {
        public List<CSGVertex> vertices;
        public CSGPlane plane;
        public int sharedData;

        public CSGPolygon(List<CSGVertex> vertices, int sharedData = 0)
        {
            this.vertices = vertices;
            this.sharedData = sharedData;
            this.plane = CSGPlane.FromPolygon(vertices);
        }

        public CSGPolygon(List<CSGVertex> vertices, CSGPlane plane, int sharedData = 0)
        {
            this.vertices = vertices;
            this.plane = plane;
            this.sharedData = sharedData;
        }

        public void Flip()
        {
            vertices.Reverse();
            for (int i = 0; i < vertices.Count; i++)
            {
                CSGVertex v = vertices[i];
                v.Flip();
                vertices[i] = v;
            }
            plane.Flip();
        }

        public CSGPolygon Clone()
        {
            var vertClones = new List<CSGVertex>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++)
            {
                vertClones.Add(vertices[i]);
            }
            return new CSGPolygon(vertClones, plane, sharedData);
        }

        public float CalculateArea()
        {
            if (vertices == null || vertices.Count < 3) return 0f;
            Vector3 totalCross = Vector3.zero;
            Vector3 v0 = vertices[0].position;
            for (int i = 1; i < vertices.Count - 1; i++)
            {
                totalCross += Vector3.Cross(vertices[i].position - v0, vertices[i + 1].position - v0);
            }
            return totalCross.magnitude * 0.5f;
        }
    }
}
