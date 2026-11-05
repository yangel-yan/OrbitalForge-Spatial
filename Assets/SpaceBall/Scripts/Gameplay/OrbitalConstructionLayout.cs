using UnityEngine;

namespace SpaceBall
{
    /// <summary>Authored anchors for the four construction missions. Placement references are QA data only.</summary>
    public sealed class OrbitalConstructionLayout
    {
        public struct Placement
        {
            public int Tile, Socket, QuarterTurns;
            public Placement(int tile, int socket, int quarterTurns)
            { Tile = tile; Socket = socket; QuarterTurns = quarterTurns; }
        }

        public OrbitalLevel Level;
        public string Caption;
        public Vector3 Launch, Direction, TargetA, TargetB, Dock, Recovery, Gate, Checkpoint;
        public Vector3[] SocketPositions;
        public OrbitalTileShape[] TileShapes;
        public Placement[] ReferenceSolution;
        public Vector3[][] FixedPaths;
        public int GateAfterSocket = -1;
        public int DecisionSocket = -1;

        static Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
        static Placement At(int tile, int socket, int turns) => new Placement(tile, socket, turns);

        public static bool Supports(OrbitalLevel level) => (int)level >= (int)OrbitalLevel.Crossing && (int)level <= (int)OrbitalLevel.Finale;

        public static OrbitalConstructionLayout For(OrbitalLevel level)
        {
            switch (level)
            {
                case OrbitalLevel.Crossing:
                    return new OrbitalConstructionLayout
                    {
                        Level = level, Caption = "三件轨段 · 接通入口与接收口",
                        Launch=P(-.48f,.32f,-.18f), Direction=Vector3.right,
                        TargetA=P(-.27f,.28f,-.18f), TargetB=P(.14f,.2094f,0),
                        Dock=P(.34f,.18f,0), Recovery=P(.12f,.025f,-.28f),
                        TileShapes=new[]{OrbitalTileShape.Straight,OrbitalTileShape.CornerLeft,OrbitalTileShape.CornerRight},
                        SocketPositions=new[]{P(-.27f,.28f,-.18f),P(-.09f,.26f,-.18f),P(-.09f,.24f,0),P(.09f,.22f,-.18f)},
                        ReferenceSolution=new[]{At(0,0,0),At(1,1,0),At(2,2,3)},
                        FixedPaths=new[]{
                            new[]{P(-.48f,.32f,-.18f),P(-.36f,.29f,-.18f)},
                            new[]{P(0,.23f,0),P(.34f,.18f,0)}
                        }
                    };
                case OrbitalLevel.Reverse:
                    return new OrbitalConstructionLayout
                    {
                        Level=level, Caption="四件轨段 · 高口接入，降轨送往低层",
                        Launch=P(.49f,.35f,.18f), Direction=Vector3.left,
                        TargetB=P(.27f,.31f,.18f), TargetA=P(.28f,.1618f,-.18f),
                        Dock=P(.43f,.14f,-.18f), Recovery=P(-.20f,.025f,-.20f),
                        TileShapes=new[]{OrbitalTileShape.Straight,OrbitalTileShape.CornerLeft,OrbitalTileShape.Drop,OrbitalTileShape.CornerLeft},
                        SocketPositions=new[]{P(.27f,.31f,.18f),P(.09f,.29f,.18f),P(.09f,.27f,0),P(.09f,.19f,-.18f),P(-.09f,.27f,0)},
                        ReferenceSolution=new[]{At(0,0,2),At(1,1,2),At(2,2,1),At(3,3,1)},
                        FixedPaths=new[]{
                            new[]{P(.49f,.35f,.18f),P(.36f,.32f,.18f)},
                            new[]{P(.18f,.18f,-.18f),P(.43f,.14f,-.18f)}
                        }
                    };
                case OrbitalLevel.Bridge:
                    return new OrbitalConstructionLayout
                    {
                        Level=level, Caption="五件轨段 · 供电后连接低层交付线",
                        Launch=P(-.49f,.34f,.18f), Direction=Vector3.right,
                        TargetB=P(-.27f,.30f,.18f), TargetA=P(.30f,.1164f,-.24f),
                        Dock=P(.40f,.105f,-.24f), Recovery=P(.29f,.025f,.04f),
                        Gate=P(-.09f,.18f,-.12f), GateAfterSocket=2,
                        TileShapes=new[]{OrbitalTileShape.Straight,OrbitalTileShape.CornerRight,OrbitalTileShape.Drop,OrbitalTileShape.CornerLeft,OrbitalTileShape.Straight},
                        SocketPositions=new[]{P(-.27f,.30f,.18f),P(-.09f,.28f,.18f),P(-.09f,.26f,0),P(-.09f,.16f,-.24f),P(.09f,.14f,-.24f),P(.09f,.25f,0)},
                        ReferenceSolution=new[]{At(0,0,0),At(1,1,0),At(2,2,1),At(3,3,1),At(4,4,0)},
                        FixedPaths=new[]{
                            new[]{P(-.49f,.34f,.18f),P(-.36f,.31f,.18f)},
                            new[]{P(-.09f,.19f,-.09f),P(-.09f,.17f,-.15f)},
                            new[]{P(.18f,.13f,-.24f),P(.40f,.105f,-.24f)}
                        }
                    };
                case OrbitalLevel.Finale:
                    return new OrbitalConstructionLayout
                    {
                        Level=level, Caption="六件轨段 · 门后两条接法，交付前必须经过 C",
                        Launch=P(-.50f,.36f,-.24f), Direction=Vector3.right,
                        TargetA=P(-.36f,.33f,-.24f), TargetB=P(-.18f,.31f,-.24f),
                        Dock=P(0,.05f,-.12f), Recovery=P(-.43f,.025f,.04f),
                        Gate=P(0,.18f,.12f), Checkpoint=P(.49f,.0883333f,.12f), GateAfterSocket=2,DecisionSocket=3,
                        TileShapes=new[]{OrbitalTileShape.Straight,OrbitalTileShape.Straight,OrbitalTileShape.Drop,OrbitalTileShape.CornerRight,OrbitalTileShape.Straight,OrbitalTileShape.CornerLeft},
                        SocketPositions=new[]{P(-.36f,.33f,-.24f),P(-.18f,.31f,-.24f),P(0,.26f,0),P(0,.16f,.24f),P(.18f,.14f,.24f),P(.36f,.12f,.24f),P(.18f,.23f,-.18f),P(-.18f,.21f,.06f)},
                        ReferenceSolution=new[]{At(0,0,0),At(1,1,0),At(2,2,3),At(3,3,3),At(4,4,0),At(5,5,0)},
                        FixedPaths=new[]{
                            new[]{P(-.50f,.36f,-.24f),P(-.45f,.34f,-.24f)},
                            new[]{P(-.09f,.30f,-.24f),P(0,.29f,-.24f),P(0,.27f,-.09f)},
                            new[]{P(0,.19f,.09f),P(0,.17f,.15f)},
                            // C is on the outer route, not on the common station receiver.
                            FinaleOuterReturn(),
                            // A left-bending module after G forms a real bypass to S.
                            // It can physically deliver the ball but does not perform C.
                            new[]{P(-.09f,.15f,.24f),P(-.27f,.135f,.24f),P(-.27f,.10f,-.12f),P(0,.05f,-.12f)}
                        }
                    };
                default: throw new System.ArgumentOutOfRangeException(nameof(level));
            }
        }

        static Vector3[] FinaleOuterReturn()
        {
            // A measured half-circle preserves the incoming north tangent and
            // leaves on a long southbound receiver. No tiny orthogonal U bend.
            const int steps=16;
            var points=new Vector3[steps+3];
            for(int i=0;i<=steps;i++)
            {
                float t=Mathf.PI*i/steps;
                points[i]=P(.425f-.065f*Mathf.Cos(t),Mathf.Lerp(.11f,.10f,i/(float)steps),.33f+.065f*Mathf.Sin(t));
            }
            points[steps+1]=P(.49f,.075f,-.12f);
            points[steps+2]=P(0,.05f,-.12f);
            return points;
        }
    }
}
