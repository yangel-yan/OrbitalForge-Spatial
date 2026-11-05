using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceBall
{
    /// <summary>
    /// Low instrument silhouettes and deck engravings identify each construction
    /// mission. They do not carry input, physics, progress, or route information.
    /// </summary>
    public static class OrbitalConstructionVisuals
    {
        public static void Build(Transform board, OrbitalLevel level, Material[] palette)
        {
            if (board == null || palette == null || palette.Length < 13 ||
                !OrbitalConstructionLayout.Supports(level)) return;

            var root = new GameObject("Construction mission instruments").transform;
            root.SetParent(board, false);
            switch (level)
            {
                case OrbitalLevel.Crossing:
                    NavigationBeacon(root, new Vector3(-.32f, 0, .16f), palette);
                    break;
                case OrbitalLevel.Reverse:
                    HeightGauge(root, new Vector3(-.32f, 0, .13f), palette);
                    break;
                case OrbitalLevel.Bridge:
                    PowerInstrument(root, new Vector3(.31f, 0, .14f), palette);
                    break;
                case OrbitalLevel.Finale:
                    StarMap(root, new Vector3(-.36f, 0, .13f), palette);
                    break;
            }
            BuildDockIdentity(board, OrbitalConstructionLayout.For(level), palette);
        }

        /// <summary>
        /// A visible spaceport receiver around the real dock. The two track
        /// approaches run along X, so all solid silhouettes sit beside that
        /// corridor or well above the ball. These meshes never affect a flight.
        /// </summary>
        public static void BuildDockIdentity(Transform board, OrbitalConstructionLayout layout, Material[] p)
        {
            if (board == null || layout == null || p == null || p.Length < 13) return;
            Transform root = Group(board, "Spaceport receiving beacon", layout.Dock);
            Ring(root, "Receiver ceramic perimeter", new Vector3(0, -.010f, 0), .054f, .0032f, p[8]);

            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * .074f;
                Solid(root, "Receiver wing footing", PrimitiveType.Cube, new Vector3(0, -.022f, z),
                    new Vector3(.054f, .013f, .044f), p[9]);
                Solid(root, "Receiver ceramic wing", PrimitiveType.Cube, new Vector3(0, .035f, z),
                    new Vector3(.023f, .104f, .027f), p[8]);
                Solid(root, "Receiver wing inset", PrimitiveType.Cube, new Vector3(.0125f, .042f, z),
                    new Vector3(.002f, .067f, .016f), p[3]);
                Solid(root, "Receiver wing signal", PrimitiveType.Cube, new Vector3(.014f, .043f, z),
                    new Vector3(.002f, .049f, .006f), p[10]);
                Solid(root, "Receiver forward signal", PrimitiveType.Cube, new Vector3(0, .043f, z - .0145f),
                    new Vector3(.006f, .049f, .002f), p[10]);
                Solid(root, "Receiver ceramic crown", PrimitiveType.Cube, new Vector3(0, .092f, z),
                    new Vector3(.033f, .010f, .035f), p[8]);
                Solid(root, "Receiver crown reveal", PrimitiveType.Cube, new Vector3(0, .099f, z),
                    new Vector3(.020f, .004f, .025f), p[9]);

                // The four guide lamps sit beyond the rail's 34 mm half-width;
                // they frame the receiver rather than drawing over the ball.
                foreach (float approach in new[] { -1f, 1f })
                    Solid(root, "Receiver approach lamp", PrimitiveType.Cube,
                        new Vector3(approach * .055f, -.008f, side * .045f),
                        new Vector3(.019f, .003f, .005f), p[10]);
            }

            // The open lintel gives S a recognisable destination silhouette even
            // where a lower receiver is partly hidden by the return track.
            Solid(root, "Receiver open lintel", PrimitiveType.Cube, new Vector3(0, .105f, 0),
                new Vector3(.028f, .012f, .174f), p[8]);
            Solid(root, "Receiver lintel navigation stripe", PrimitiveType.Cube, new Vector3(.015f, .106f, 0),
                new Vector3(.002f, .003f, .110f), p[10]);

            // The telemetry dish belongs to the rear wing. It stays off the
            // approach centreline and does not add another goal-like light orb.
            Solid(root, "Receiver telemetry mast", PrimitiveType.Cylinder, new Vector3(0, .124f, .074f),
                new Vector3(.007f, .023f, .007f), p[9]);
            Transform dish = Solid(root, "Receiver telemetry dish", PrimitiveType.Cylinder,
                new Vector3(0, .150f, .074f), new Vector3(.035f, .003f, .035f), p[8]);
            dish.localRotation = Quaternion.Euler(0, 0, 24);
            Transform dishInset = Solid(root, "Receiver telemetry inset", PrimitiveType.Cylinder,
                new Vector3(-.0012f, .153f, .074f), new Vector3(.021f, .001f, .021f), p[9]);
            dishInset.localRotation = dish.localRotation;
        }

        static void NavigationBeacon(Transform parent, Vector3 position, Material[] p)
        {
            Transform root = Group(parent, "Navigation instrument", position);
            Plinth(root, .074f, p);
            Ring(root, "Navigation scale", new Vector3(0, .016f, 0), .058f, .0018f, p[9]);
            // A broad transmitter cap has a different silhouette from a ball or a
            // socket. Its light is a small instrument stripe, not a goal lamp.
            Solid(root, "Beacon pedestal", PrimitiveType.Cylinder, new Vector3(0, .030f, 0),
                new Vector3(.037f, .014f, .037f), p[3]);
            Solid(root, "Beacon ceramic cap", PrimitiveType.Cylinder, new Vector3(0, .047f, 0),
                new Vector3(.049f, .004f, .049f), p[8]);
            Solid(root, "Beacon reveal", PrimitiveType.Cylinder, new Vector3(0, .054f, 0),
                new Vector3(.031f, .003f, .031f), p[9]);
            Ring(root, "Instrument signal inset", new Vector3(0, .058f, 0), .014f, .0014f, p[10]);
            BearingTicks(root, .062f, .0165f, p[8]);
            DeckConstellation(parent, position + new Vector3(.018f, .001f, .092f), p);
        }

        static void HeightGauge(Transform parent, Vector3 position, Material[] p)
        {
            Transform root = Group(parent, "Height calibration instrument", position);
            Solid(root, "Gauge footing", PrimitiveType.Cube, new Vector3(0, .008f, 0),
                new Vector3(.143f, .016f, .096f), p[9]);
            Solid(root, "Gauge ceramic sill", PrimitiveType.Cube, new Vector3(0, .018f, 0),
                new Vector3(.126f, .005f, .080f), p[8]);
            // Three physical levels express the chapter's new height vocabulary
            // without drawing a suggested solution between gameplay interfaces.
            for (int i = 0; i < 3; i++)
            {
                float x = -.043f + .043f * i;
                float top = .032f + .016f * i;
                Solid(root, "Gauge column " + i, PrimitiveType.Cube, new Vector3(x, (.020f + top) * .5f, .008f),
                    new Vector3(.024f, top - .020f, .039f), p[3]);
                Solid(root, "Gauge level cap " + i, PrimitiveType.Cube, new Vector3(x, top, .008f),
                    new Vector3(.033f, .004f, .048f), p[8]);
                Solid(root, "Gauge amber division " + i, PrimitiveType.Cube, new Vector3(x, top - .007f, -.013f),
                    new Vector3(.016f, .002f, .002f), p[2]);
            }
            Solid(root, "Gauge vertical scale", PrimitiveType.Cube, new Vector3(-.068f, .044f, .010f),
                new Vector3(.004f, .049f, .050f), p[9]);
            for (int i = 0; i < 4; i++)
                Solid(root, "Gauge scale division " + i, PrimitiveType.Cube, new Vector3(-.066f, .025f + i * .012f, -.017f),
                    new Vector3(i % 2 == 0 ? .010f : .006f, .0018f, .002f), p[8]);
            DeckConstellation(parent, position + new Vector3(.016f, .001f, .075f), p);
        }

        static void PowerInstrument(Transform parent, Vector3 position, Material[] p)
        {
            Transform root = Group(parent, "Power instrument", position);
            Plinth(root, .067f, p);
            Solid(root, "Reactor lower vessel", PrimitiveType.Cylinder, new Vector3(0, .030f, 0),
                new Vector3(.060f, .016f, .060f), p[3]);
            Solid(root, "Reactor ceramic crown", PrimitiveType.Cylinder, new Vector3(0, .049f, 0),
                new Vector3(.074f, .004f, .074f), p[8]);
            Solid(root, "Reactor cap", PrimitiveType.Cylinder, new Vector3(0, .058f, 0),
                new Vector3(.045f, .004f, .045f), p[9]);
            Ring(root, "Power instrument collar", new Vector3(0, .064f, 0), .019f, .002f, p[2]);
            foreach (float side in new[] { -1f, 1f })
            {
                Solid(root, "Reactor outer cheek", PrimitiveType.Cube, new Vector3(side * .041f, .038f, 0),
                    new Vector3(.013f, .044f, .048f), p[8]);
                Solid(root, "Reactor service window", PrimitiveType.Cube, new Vector3(side * .025f, .032f, -.031f),
                    new Vector3(.013f, .018f, .002f), p[9]);
                // The gold bus ends at this instrument's own service lugs. It
                // never impersonates the actual B-to-G supply cable overhead.
                Beam(root, "Local power bus", new Vector3(side * .043f, .013f, -.047f),
                    new Vector3(side * .043f, .013f, -.077f), .0027f, p[2]);
                Solid(root, "Power bus lug", PrimitiveType.Cube, new Vector3(side * .043f, .013f, -.079f),
                    new Vector3(.013f, .007f, .009f), p[9]);
            }
        }

        static void StarMap(Transform parent, Vector3 position, Material[] p)
        {
            Transform root = Group(parent, "Celestial navigation instrument", position);
            Plinth(root, .067f, p);
            Ring(root, "Star map horizon", new Vector3(0, .018f, 0), .053f, .0017f, p[9]);
            Solid(root, "Map spindle", PrimitiveType.Cylinder, new Vector3(0, .029f, 0),
                new Vector3(.015f, .012f, .015f), p[9]);
            Solid(root, "Map ceramic globe", PrimitiveType.Sphere, new Vector3(0, .037f, 0),
                Vector3.one * .032f, p[8]);
            Transform meridian = OrbitalArtGeometry.Ring(root, "Map meridian", new Vector3(0, .037f, 0),
                .026f, .0017f, p[2], Quaternion.Euler(53, 0, 21), 32, 6);
            Quiet(meridian);
            Transform orbit = OrbitalArtGeometry.Ring(root, "Map orbital plane", new Vector3(0, .037f, 0),
                .034f, .0015f, p[9], Quaternion.Euler(60, 0, -16), 36, 6);
            Quiet(orbit);
            BearingTicks(root, .058f, .0175f, p[8]);
            DeckConstellation(parent, position + new Vector3(.008f, .001f, .087f), p);
        }

        static void Plinth(Transform parent, float radius, Material[] p)
        {
            Solid(parent, "Instrument footing", PrimitiveType.Cylinder, new Vector3(0, .005f, 0),
                new Vector3(radius * 2, .005f, radius * 2), p[3]);
            Ring(parent, "Instrument footing reveal", new Vector3(0, .011f, 0), radius * .91f, .0022f, p[9]);
            Solid(parent, "Instrument inset", PrimitiveType.Cylinder, new Vector3(0, .013f, 0),
                new Vector3(radius * 1.67f, .002f, radius * 1.67f), p[3]);
        }

        static void BearingTicks(Transform parent, float radius, float y, Material material)
        {
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector3 at = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                var tick = Solid(parent, "Bearing division " + i, PrimitiveType.Cube, at,
                    new Vector3(i % 2 == 0 ? .010f : .006f, .0015f, .002f), material);
                tick.localRotation = Quaternion.Euler(0, -i * 45, 0);
            }
        }

        static void DeckConstellation(Transform parent, Vector3 origin, Material[] p)
        {
            // A bounded navigation engraving, deliberately unrelated to socket
            // positions. These thin titanium marks remain below every rail.
            Vector3[] points = {
                origin + new Vector3(-.047f, .002f, 0),
                origin + new Vector3(-.010f, .002f, .028f),
                origin + new Vector3(.037f, .002f, .017f),
                origin + new Vector3(.062f, .002f, -.009f)
            };
            for (int i = 0; i + 1 < points.Length; i++)
                Beam(parent, "Deck navigation engraving", points[i], points[i + 1], .0013f, p[9]);
            for (int i = 0; i < points.Length; i++)
                Solid(parent, "Navigation registration " + i, PrimitiveType.Cylinder, points[i],
                    new Vector3(i == 1 ? .009f : .006f, .0008f, i == 1 ? .009f : .006f), i == 1 ? p[2] : p[9]);
        }

        static Transform Group(Transform parent, string name, Vector3 point)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = point;
            return root;
        }

        static Transform Solid(Transform parent, string name, PrimitiveType type,
            Vector3 point, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = point;
            go.transform.localScale = size;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
            Quiet(go.transform);
            return go.transform;
        }

        static void Ring(Transform parent, string name, Vector3 point, float radius,
            float tube, Material material)
        {
            Quiet(OrbitalArtGeometry.Ring(parent, name, point, radius, tube,
                material, Quaternion.Euler(90, 0, 0), 32, 6));
        }

        static void Beam(Transform parent, string name, Vector3 from, Vector3 to,
            float width, Material material)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return;
            Transform beam = Solid(parent, name, PrimitiveType.Cylinder, (from + to) * .5f,
                new Vector3(width, delta.magnitude * .5f, width), material);
            beam.localRotation = Quaternion.FromToRotation(Vector3.up, delta);
        }

        static void Quiet(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}
