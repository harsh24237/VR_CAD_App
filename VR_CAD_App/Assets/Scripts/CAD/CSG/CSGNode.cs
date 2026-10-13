using System;
using System.Collections.Generic;

namespace VRCAD.Core.CSG
{
    public class CSGNode
    {
        public CSGPlane? plane;
        public CSGNode front;
        public CSGNode back;
        public List<CSGPolygon> polygons = new List<CSGPolygon>();

        public CSGNode(List<CSGPolygon> list = null)
        {
            if (list != null && list.Count > 0)
            {
                Build(list);
            }
        }

        public CSGNode Clone()
        {
            var node = new CSGNode();
            node.plane = this.plane;
            if (this.front != null) node.front = this.front.Clone();
            if (this.back != null) node.back = this.back.Clone();

            node.polygons = new List<CSGPolygon>(this.polygons.Count);
            for (int i = 0; i < this.polygons.Count; i++)
            {
                node.polygons.Add(this.polygons[i].Clone());
            }

            return node;
        }

        public void Invert()
        {
            for (int i = 0; i < polygons.Count; i++)
            {
                polygons[i].Flip();
            }

            if (plane.HasValue)
            {
                var p = plane.Value;
                p.Flip();
                plane = p;
            }

            if (front != null) front.Invert();
            if (back != null) back.Invert();

            var temp = front;
            front = back;
            back = temp;
        }

        public List<CSGPolygon> ClipPolygons(List<CSGPolygon> list)
        {
            if (!plane.HasValue)
            {
                var copy = new List<CSGPolygon>(list.Count);
                for (int i = 0; i < list.Count; i++) copy.Add(list[i]);
                return copy;
            }

            var f = new List<CSGPolygon>();
            var b = new List<CSGPolygon>();

            var p = plane.Value;
            for (int i = 0; i < list.Count; i++)
            {
                p.SplitPolygon(list[i], f, b, f, b);
            }

            if (front != null)
            {
                f = front.ClipPolygons(f);
            }

            if (back != null)
            {
                b = back.ClipPolygons(b);
            }
            else
            {
                b.Clear();
            }

            f.AddRange(b);
            return f;
        }

        public void ClipTo(CSGNode bsp)
        {
            polygons = bsp.ClipPolygons(polygons);
            if (front != null) front.ClipTo(bsp);
            if (back != null) back.ClipTo(bsp);
        }

        public List<CSGPolygon> AllPolygons()
        {
            var result = new List<CSGPolygon>(polygons);
            if (front != null) result.AddRange(front.AllPolygons());
            if (back != null) result.AddRange(back.AllPolygons());
            return result;
        }

        public void Build(List<CSGPolygon> list, int depth = 0)
        {
            if (list == null || list.Count == 0) return;

            if (depth > 500)
            {
                polygons.AddRange(list);
                return;
            }

            if (!plane.HasValue)
            {
                plane = list[0].plane;
            }

            var f = new List<CSGPolygon>();
            var b = new List<CSGPolygon>();
            var p = plane.Value;

            for (int i = 0; i < list.Count; i++)
            {
                p.SplitPolygon(list[i], polygons, polygons, f, b);
            }

            if (f.Count > 0)
            {
                if (front == null) front = new CSGNode();
                front.Build(f, depth + 1);
            }

            if (b.Count > 0)
            {
                if (back == null) back = new CSGNode();
                back.Build(b, depth + 1);
            }
        }
    }
}
