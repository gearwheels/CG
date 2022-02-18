using System.Collections.Generic;
using CGLabPlatform;

namespace Lab7{
    public class Bezier2Curve{
        public Vertex P0, P1, P2;
        public Vertex[] Points;

        public Bezier2Curve(DVector2 p0, DVector2 p1, DVector2 p2, double dt){
            P0 = new Vertex(p0);
            P1 = new Vertex(p1);
            P2 = new Vertex(p2);

            Points = new Vertex[(int) (1 / dt) + 2];
            var i = 0;
            for (var t = 0.0; t <= 1; t += dt, i++) Points[i] = new Vertex(Bezier2(p0, p1, p2, t));
            Points[i] = new Vertex(Bezier2(p0, p1, p2, 1.0));
        }

        public void ApplyTransform(DMatrix3 t){
            // Вершины
            foreach (var p in Points) p.pointInWorld = t * p.pointInLocalSpace;

            P0.pointInWorld = t * P0.pointInLocalSpace;
            P1.pointInWorld = t * P1.pointInLocalSpace;
            P2.pointInWorld = t * P2.pointInLocalSpace;
        }

        private DVector2 Bezier2(DVector2 p0, DVector2 p1, DVector2 p2, double t){
            return
                (1.0 - t) * (1.0 - t) * p0
                + 2 * t * (1.0 - t) * p1
                + t * t * p2;
        }

        public IEnumerable<Vertex> Dots{
            get{ return new Vertex[]{P0, P1, P2}; }
        }
    }

    public class Vertex{
        public readonly DVector3 pointInLocalSpace;
        public DVector3 pointInWorld;

        public Vertex(DVector2 v){
            pointInLocalSpace = new DVector3(v, 1.0);
        }
    }
}