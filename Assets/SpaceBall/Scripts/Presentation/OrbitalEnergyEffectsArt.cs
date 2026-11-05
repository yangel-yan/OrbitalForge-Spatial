using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceBall
{
    /// <summary>Authored mesh pools for native Shared Space. No runtime particles or mesh mutation.</summary>
    public static partial class OrbitalEnergyEffectsArt
    {
        public static OrbitalEnergyEffects Build(OrbitalPrototype owner, OrbitalPresentation presentation,
            Rigidbody ball, Transform board, Transform[] targets, Material[] palette)
        {
            if (owner == null || presentation == null || ball == null || board == null || palette == null || palette.Length < 5)
                throw new System.ArgumentException("Energy feedback requires an owner, board, ball, targets and five effect materials.");
            Material white = palette.Length >= 13 ? palette[4] : palette[0];
            Material cyan = palette.Length >= 13 ? palette[10] : palette[1];
            Material deep = palette.Length >= 13 ? palette[1] : palette[2];
            Material gold = palette.Length >= 13 ? palette[11] : palette[3];
            Material dimGold = palette.Length >= 13 ? palette[2] : palette[4];
            var root = new GameObject("Energy feedback"); root.layer = 2;
            root.transform.SetParent(board, false);
            var fx = root.AddComponent<OrbitalEnergyEffects>();
            fx.board = board; fx.energyBall = ball; fx.ballVisual = presentation.ballVisual;
            fx.white = white; fx.cyan = cyan; fx.deepCyan = deep; fx.gold = gold; fx.dimGold = dimGold;
            fx.coreRenderer = fx.ballVisual != null ? fx.ballVisual.GetComponent<Renderer>() : null;
            if (fx.coreRenderer != null) fx.coreRenderer.sharedMaterial = white;
            Mesh wave = Torus("Orbital_VfxWave", .03f, 32, 5);
            Mesh shell = Torus("Orbital_VfxShell", .065f, 32, 5);
            Mesh shard = Diamond("Orbital_VfxShard");
            Mesh bar = Box("Orbital_VfxRelayBar");
            fx.shellRoot = Group(root.transform, "Energy corona", board.InverseTransformPoint(ball.position));
            fx.shellRings = new Transform[3];
            fx.shellRenderers = new Renderer[3];
            for (int i = 0; i < 3; i++)
            {
                Transform ring = MeshNode(fx.shellRoot, "Corona orbit " + i, shell, i == 0 ? white : cyan);
                ring.localScale = Vector3.one * (.0132f + i * .0007f);
                ring.localRotation = Quaternion.Euler(i * 56 + 24, i * 39, i * 47);
                fx.shellRings[i] = ring; fx.shellRenderers[i] = ring.GetComponent<Renderer>();
            }
            fx.tail = new Transform[10];
            for (int i = 0; i < fx.tail.Length; i++)
            {
                fx.tail[i] = MeshNode(root.transform, "Comet streak " + i, shard, i == 0 ? white : i < 6 ? cyan : deep);
                fx.tail[i].localScale = new Vector3(.003f, .003f, .01f);
            }
            fx.bursts = new OrbitalEnergyEffects.Burst[5];
            for (int i = 0; i < fx.bursts.Length; i++)
            {
                if (i == 3 || targets == null || i >= targets.Length || targets[i] == null) continue;
                var burst = new OrbitalEnergyEffects.Burst();
                burst.origin = board.InverseTransformPoint(targets[i].position);
                burst.root = Group(root.transform, "Target shockwave " + i, burst.origin);
                burst.rings = new Transform[2]; burst.sparks = new Transform[8];
                for (int ring = 0; ring < 2; ring++)
                {
                    burst.rings[ring] = MeshNode(burst.root, "Expanding ring " + ring, wave,
                        ring == 0 ? i == 1 ? gold : cyan : white);
                    burst.rings[ring].localScale = Vector3.one * .025f;
                }
                for (int spark = 0; spark < burst.sparks.Length; spark++)
                {
                    burst.sparks[spark] = MeshNode(burst.root, "Impact spark " + spark, shard,
                        spark % 3 == 0 ? white : i == 1 ? dimGold : deep);
                    burst.sparks[spark].localScale = new Vector3(.002f, .002f, .009f);
                }
                fx.bursts[i] = burst;
            }
            Vector3 dock = targets != null && targets.Length > 3 && targets[3] != null
                ? board.InverseTransformPoint(targets[3].position) : board.InverseTransformPoint(ball.position);
            fx.dockLocal = dock;
            fx.dockRoot = Group(root.transform, "Station energy receiver", dock);
            fx.dockFlow = new Transform[6];
            for (int i = 0; i < fx.dockFlow.Length; i++)
            {
                fx.dockFlow[i] = MeshNode(fx.dockRoot, "Received energy " + i, shard, i % 2 == 0 ? white : gold);
                fx.dockFlow[i].localScale = new Vector3(.008f, .008f, .024f);
            }
            fx.relayBars = new Renderer[6];
            for (int i = 0; i < fx.relayBars.Length; i++)
            {
                Transform lamp = MeshNode(fx.dockRoot, "Charging relay " + i, bar, dimGold);
                lamp.localPosition = new Vector3(.064f, .012f + i * .015f, .055f);
                lamp.localScale = new Vector3(.024f, .006f, .006f);
                fx.relayBars[i] = lamp.GetComponent<Renderer>();
            }
            fx.dockWaves = new Transform[2];
            for (int i = 0; i < fx.dockWaves.Length; i++)
            {
                fx.dockWaves[i] = MeshNode(fx.dockRoot, "Delivery scan " + i, wave, i == 0 ? gold : cyan);
                fx.dockWaves[i].localScale = Vector3.one * .045f;
            }
            BuildLaunchCeremony(fx, board.Find("Art_Launcher"), wave, shard);
            BuildGateCeremony(fx, targets[2], wave, bar);
            BuildDeliveryCeremony(fx);
            fx.Bind(owner);
            return fx;
        }

        static Transform Group(Transform parent, string name, Vector3 point)
        {
            var go = new GameObject(name); go.layer = 2;
            go.transform.SetParent(parent, false); go.transform.localPosition = point;
            return go.transform;
        }

        static Transform MeshNode(Transform parent, string name, Mesh mesh, Material material)
        {
            Transform node = Group(parent, name, Vector3.zero);
            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return node;
        }

        static Mesh Torus(string name, float tube, int segments, int sides)
        {
            var vertices = new Vector3[segments * sides]; var triangles = new int[segments * sides * 6];
            int index = 0;
            for (int i = 0; i < segments; i++)
            {
                float a = Mathf.PI * 2 * i / segments;
                Vector3 radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j < sides; j++)
                {
                    float b = Mathf.PI * 2 * j / sides;
                    vertices[i * sides + j] = radial * (1 + Mathf.Cos(b) * tube) + Vector3.up * Mathf.Sin(b) * tube;
                    int first = i * sides + j, next = ((i + 1) % segments) * sides + j;
                    int across = i * sides + (j + 1) % sides, diagonal = ((i + 1) % segments) * sides + (j + 1) % sides;
                    triangles[index++] = first; triangles[index++] = next; triangles[index++] = across;
                    triangles[index++] = across; triangles[index++] = next; triangles[index++] = diagonal;
                }
            }
            return Finish(name, vertices, triangles);
        }

        static Mesh Diamond(string name) => Finish(name, new[]
        {
            new Vector3(0,0,.5f), new Vector3(.5f,0,-.10f), new Vector3(0,.5f,-.10f),
            new Vector3(-.5f,0,-.10f), new Vector3(0,-.5f,-.10f), new Vector3(0,0,-.5f)
        }, new[] { 0,1,2, 0,2,3, 0,3,4, 0,4,1, 5,2,1, 5,3,2, 5,4,3, 5,1,4 });

        static Mesh Box(string name) => Finish(name, new[]
        {
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
            new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f)
        }, new[] { 0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4 });

        static Mesh Finish(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = name, vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
