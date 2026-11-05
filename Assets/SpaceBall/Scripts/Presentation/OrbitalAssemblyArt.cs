using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// Visual-only assembly kit, built on local XZ with +Y up. Piece bottoms sit
    /// at y=0; the caller owns placement, selection, collisions and puzzle rules.
    /// All meshes can be persisted by OrbitalSceneBuilder's existing mesh bake.
    /// </summary>
    public static class OrbitalAssemblyArt
    {
        const int Body = 3, Ceramic = 8, Metal = 9, Cyan = 10, Amber = 2;

        /// <summary>
        /// About 11 cm square, with a 55 mm clear lane and floor top at y=.0215.
        /// Input is -Z. Route exits are +Z / -X; reflector exits are -X / +X.
        /// </summary>
        public static Transform BuildPiece(Transform parent, bool reflector, Material[] palette)
        {
            ValidatePalette(palette);
            Transform root = Group(parent, reflector ? "Art_ReflectorBrick" : "Art_RouteBrick");
            var shell = new Geometry();
            // A thin machined frame, recessed dark gasket and inset lid read as an
            // assembled instrument rather than one thick injection-moulded block.
            shell.Prism(new Vector3(0, .0025f, 0), .108f, .005f, .108f, .012f, Metal);
            shell.Prism(new Vector3(0, .009f, 0), .104f, .008f, .104f, .011f, Body);
            shell.Prism(new Vector3(0, .0145f, 0), .101f, .003f, .101f, .010f, Metal);
            shell.Prism(new Vector3(0, .0175f, 0), .098f, .003f, .098f, .010f, Ceramic);
            // Short grips sit below the rail crowns and leave the three lane mouths open.
            foreach (float side in new[] { -1f, 1f })
            {
                shell.Prism(new Vector3(side * .0465f, .023f, -.039f), .011f, .010f, .025f, .003f, Body);
                shell.Prism(new Vector3(side * .047f, .028f, -.039f), .008f, .004f, .021f, .002f, Ceramic);
                shell.Box(new Vector3(side * .0514f, .024f, -.039f), new Vector3(.001f, .002f, .010f), Metal);
                shell.Box(new Vector3(side * .047f, .0303f, -.043f), new Vector3(.003f, .0006f, .006f), reflector ? Amber : Cyan);
            }
            Port(shell, new Vector3(0, .017f, -.052f), 0f, reflector ? Amber : Cyan);

            if (reflector)
            {
                Port(shell, new Vector3(-.052f, .017f, 0), 90f, Amber);
                Port(shell, new Vector3(.052f, .017f, 0), 90f, Amber);
                Junction(shell, true);
                // A clipped, bevelled strike plate is held by two small axle bushes.
                // The broad flat box is replaced without moving its reflective face
                // out of the calibrated lane or raising the previous 52 mm envelope.
                var vane = new Geometry();
                Quaternion vaneYaw = Quaternion.Euler(0, 42, 0);
                Quaternion upright = vaneYaw * Quaternion.Euler(90, 0, 0);
                Vector3 plate = new Vector3(0, .037f, .011f);
                vane.Prism(plate, .052f, .004f, .026f, .005f, Metal, upright);
                vane.Prism(plate - vaneYaw * Vector3.forward * .0025f,
                    .046f, .0015f, .020f, .004f, Body, upright);
                vane.Prism(plate - vaneYaw * Vector3.forward * .0035f,
                    .040f, .001f, .013f, .0025f, Amber, upright);
                vane.Prism(plate + vaneYaw * new Vector3(0, .010f, -.0028f),
                    .036f, .001f, .0025f, .0007f, Ceramic, upright);
                vane.Cylinder(new Vector3(0, .0225f, .010f), .015f, .002f, Body);
                vane.Cylinder(new Vector3(0, .024f, .010f), .011f, .0015f, Metal);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 bearing = new Vector3(0, .0255f, .011f) + vaneYaw * new Vector3(side * .0225f, 0, .001f);
                    vane.Prism(bearing + Vector3.down * .0005f, .009f, .009f, .012f, .002f, Ceramic, vaneYaw);
                    Quaternion axle = vaneYaw * Quaternion.Euler(0, 0, 90);
                    vane.Cylinder(bearing, .0044f, .011f, Metal, axle);
                    vane.Cylinder(bearing + vaneYaw * Vector3.right * (side * .0057f),
                        .0023f, .001f, Body, axle);
                }
                Arrow(vane, new Vector3(-.028f, .0215f, .007f), -90f, .70f, Amber);
                Transform rotor = Group(root, "DirectionRotor");
                vane.Apply(rotor, palette, "Orbital_AssemblyReflectorVane");
            }
            else
            {
                Port(shell, new Vector3(0, .017f, .052f), 0f, Cyan);
                Port(shell, new Vector3(-.052f, .017f, 0), 90f, Cyan);
                Junction(shell, false);
                // The actuator occupies the unused corner, outside the T's clear lane.
                shell.Prism(new Vector3(.0395f, .0235f, .0395f), .023f, .007f, .023f, .005f, Body);
                shell.Prism(new Vector3(.0395f, .0275f, .0395f), .020f, .002f, .020f, .004f, Ceramic);
                shell.Cylinder(new Vector3(.0395f, .029f, .0395f), .0068f, .002f, Metal);
                shell.Cylinder(new Vector3(.0395f, .0302f, .0395f), .0035f, .0008f, Body);
                var pointer = new Geometry();
                Arrow(pointer, new Vector3(0, .0223f, -.006f), 0f, .90f, Cyan);
                Transform rotor = Group(root, "DirectionRotor");
                pointer.Apply(rotor, palette, "Orbital_AssemblyRoutePointer");
            }
            shell.Apply(Group(root, "BrickShell"), palette, "Orbital_AssemblyBrickShell");

            // Thin elevated edges stay readable above the deck without filling the part
            // with emissive colour. All feedback remains ordinary exported mesh geometry.
            Transform halo = OrbitalArtGeometry.Ring(root, "SelectionHalo", new Vector3(0, .039f, 0),
                .071f, .0015f, palette[reflector ? Amber : Cyan], Quaternion.Euler(90, 0, 0), 24, 4);
            halo.gameObject.SetActive(false);
            CornerCue(root, "SelectionBrackets", .058f, .043f, 4, palette);
            Transform feedback = OrbitalArtGeometry.Ring(root, "FeedbackHalo", new Vector3(0, .040f, 0),
                .074f, .0016f, palette[4], Quaternion.Euler(90, 0, 0), 28, 4);
            feedback.gameObject.SetActive(false);
            return root;
        }

        /// <summary>12.6 cm socket. A piece at this root's origin is seated on its deck.</summary>
        public static Transform BuildSocket(Transform parent, Material[] palette)
        {
            ValidatePalette(palette);
            Transform root = Group(parent, "Art_AssemblySocket");
            var geometry = new Geometry();
            geometry.Prism(new Vector3(0, -.013f, 0), .126f, .006f, .126f, .018f, Metal);
            geometry.Prism(new Vector3(0, -.006f, 0), .121f, .008f, .121f, .016f, Body);
            geometry.Prism(new Vector3(0, -.001f, 0), .110f, .002f, .110f, .012f, Metal);
            geometry.Prism(new Vector3(0, .0005f, 0), .105f, .001f, .105f, .011f, Body);
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                // Diagonal keys follow the piece's cut corners. The clear space
                // between them stays open; four tall square towers are unnecessary.
                Quaternion yaw = Quaternion.Euler(0, x * z * 45f, 0);
                geometry.Prism(new Vector3(x * .0505f, .001f, z * .0505f), .026f, .006f, .007f, .002f, Metal, yaw);
                geometry.Prism(new Vector3(x * .051f, .0135f, z * .051f), .027f, .018f, .006f, .002f, Body, yaw);
                geometry.Prism(new Vector3(x * .0507f, .0235f, z * .0507f), .021f, .003f, .004f, .001f, Ceramic, yaw);
                geometry.Box(new Vector3(x * .0507f, .0253f, z * .0507f), new Vector3(.008f, .0006f, .0015f), Cyan, yaw);
            }
            // Exposed connector bars make the empty location readable from the front.
            Port(geometry, new Vector3(0, .005f, -.060f), 0f, Cyan);
            Port(geometry, new Vector3(0, .005f, .060f), 0f, Cyan);
            geometry.Box(new Vector3(0, .0025f, 0), new Vector3(.026f, .001f, .004f), Metal);
            geometry.Box(new Vector3(0, .0025f, 0), new Vector3(.004f, .001f, .026f), Metal);
            geometry.Apply(Group(root, "SocketShell"), palette, "Orbital_AssemblySocket");
            Transform halo = OrbitalArtGeometry.Ring(root, "SelectionHalo", new Vector3(0, .054f, 0),
                .078f, .002f, palette[Cyan], Quaternion.Euler(90, 0, 0), 24, 4);
            halo.gameObject.SetActive(false);
            CornerCue(root, "DockGuide", .055f, .058f, Cyan, palette);
            var rejection = new Geometry();
            rejection.Box(new Vector3(0, .071f, 0), new Vector3(.041f, .003f, .005f), Amber, Quaternion.Euler(0, 45, 0));
            rejection.Box(new Vector3(0, .071f, 0), new Vector3(.041f, .003f, .005f), Amber, Quaternion.Euler(0, -45, 0));
            Transform reject = Group(root, "RejectCue");
            rejection.Apply(reject, palette, "Orbital_AssemblyRejectCue");
            reject.gameObject.SetActive(false);
            return root;
        }

        static void CornerCue(Transform parent, string name, float radius, float height, int material, Material[] palette)
        {
            var geometry = new Geometry();
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                geometry.Box(new Vector3(x * (radius - .009f), height, z * radius), new Vector3(.020f, .0025f, .0025f), material);
                geometry.Box(new Vector3(x * radius, height, z * (radius - .009f)), new Vector3(.0025f, .0025f, .020f), material);
            }
            Transform cue = Group(parent, name);
            geometry.Apply(cue, palette, "Orbital_" + name);
            cue.gameObject.SetActive(false);
        }

        /// <summary>
        /// A 30 x 16 cm two-part tray. Empty Seat_Route / Seat_Reflector transforms
        /// are at x=-/+0.075, y=0.004; callers can place pieces relative to them.
        /// </summary>
        public static Transform BuildTray(Transform parent, Material[] palette)
        {
            ValidatePalette(palette);
            Transform root = Group(parent, "Art_AssemblyTray");
            var geometry = new Geometry();
            geometry.Prism(new Vector3(0, -.012f, 0), .30f, .024f, .16f, .018f, Body);
            geometry.Prism(new Vector3(0, .002f, .071f), .255f, .008f, .009f, .002f, Ceramic);
            geometry.Prism(new Vector3(0, .002f, -.071f), .255f, .008f, .009f, .002f, Ceramic);
            foreach (float side in new[] { -1f, 1f })
            {
                geometry.Prism(new Vector3(side * .075f, .001f, 0), .126f, .004f, .126f, .012f, Metal);
                geometry.Prism(new Vector3(side * .075f, .0035f, 0), .114f, .001f, .114f, .011f, Body);
                geometry.Box(new Vector3(side * .075f, .003f, -.073f), new Vector3(.062f, .003f, .002f), side < 0 ? Cyan : Amber);
                Transform seat = Group(root, side < 0 ? "Seat_Route" : "Seat_Reflector");
                seat.localPosition = new Vector3(side * .075f, .004f, 0);
            }
            geometry.Apply(Group(root, "TrayShell"), palette, "Orbital_AssemblyTray");
            return root;
        }

        static void Junction(Geometry geometry, bool reflector)
        {
            // The 55 mm clear channel matches OrbitalTrackGeometry. Only the outside
            // boundary gets a guard: no internal rail may cut across the T junction.
            // Floor top is y=.0215; callers align that with the calibrated deck.
            const float width = .055f, reach = .048f, edge = .0295f, inner = .0275f;
            if (reflector)
            {
                geometry.Box(new Vector3(0, .020f, 0), new Vector3(reach * 2, .003f, width), Body);
                geometry.Box(new Vector3(0, .020f, -reach * .5f), new Vector3(width, .003f, reach), Body);
                Rail(geometry, new Vector2(-reach, edge), new Vector2(reach, edge));
                foreach (float sign in new[] { -1f, 1f })
                {
                    Rail(geometry, new Vector2(sign * edge, -reach), new Vector2(sign * edge, -inner));
                    Rail(geometry, new Vector2(sign * inner, -edge), new Vector2(sign * reach, -edge));
                }
            }
            else
            {
                geometry.Box(new Vector3(0, .020f, 0), new Vector3(width, .003f, reach * 2), Body);
                geometry.Box(new Vector3(-reach * .5f, .020f, 0), new Vector3(reach, .003f, width), Body);
                Rail(geometry, new Vector2(edge, -reach), new Vector2(edge, reach));
                foreach (float sign in new[] { -1f, 1f })
                {
                    Rail(geometry, new Vector2(-edge, sign * inner), new Vector2(-edge, sign * reach));
                    Rail(geometry, new Vector2(-reach, sign * edge), new Vector2(-inner, sign * edge));
                }
            }
        }

        static void Rail(Geometry geometry, Vector2 from, Vector2 to)
        {
            Vector3 start = new Vector3(from.x, 0, from.y), end = new Vector3(to.x, 0, to.y);
            Vector3 center = (start + end) * .5f;
            float length = Vector3.Distance(start, end);
            Quaternion rotation = Quaternion.LookRotation(end - start, Vector3.up);
            geometry.Prism(center + Vector3.up * .023f, .006f, .003f, length, .001f, Body, rotation);
            geometry.Prism(center + Vector3.up * .028f, .004f, .008f, length, .0008f, Ceramic, rotation);
            geometry.Box(center + Vector3.up * .0325f, new Vector3(.003f, .001f, length * .98f), Metal, rotation);
            geometry.Box(center + Vector3.up * .0333f, new Vector3(.0011f, .0006f, length * .40f), Cyan, rotation);
        }

        static void Port(Geometry geometry, Vector3 center, float yaw, int light, float width = .065f)
        {
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            geometry.Prism(center - Vector3.up * .0005f, width, .004f, .009f, .002f, Body, rotation);
            geometry.Prism(center + Vector3.up * .002f, width - .016f, .0015f, .007f, .001f, Metal, rotation);
            foreach (float side in new[] { -1f, 1f })
                geometry.Prism(center + rotation * new Vector3(side * (width * .5f - .004f), .001f, 0),
                    .008f, .005f, .008f, .0015f, Ceramic, rotation);
            geometry.Box(center + Vector3.up * .0031f, new Vector3(width - .028f, .0008f, .0015f), light, rotation);
        }

        static void Arrow(Geometry geometry, Vector3 center, float yaw, float scale, int material)
        {
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            geometry.Box(center - rotation * Vector3.forward * (.003f * scale), new Vector3(.004f, .0015f, .019f) * scale, material, rotation);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 position = center + rotation * new Vector3(side * .004f, 0, .004f) * scale;
                geometry.Box(position, new Vector3(.0037f, .0015f, .013f) * scale, material, rotation * Quaternion.Euler(0, -side * 43f, 0));
            }
        }

        static Transform Group(Transform parent, string name)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            return root;
        }

        static void ValidatePalette(Material[] palette)
        {
            if (palette == null || palette.Length <= Cyan)
                throw new ArgumentException("Assembly art requires the Orbital scene palette.", nameof(palette));
        }

        // Geometry is grouped by material in one mesh per shell; no primitive colliders,
        // transient materials, per-frame mesh edits or unsupported rendering components.
        sealed class Geometry
        {
            readonly List<Vector3> vertices = new();
            readonly List<Vector2> uv = new();
            readonly Dictionary<int, List<int>> indices = new();

            public void Box(Vector3 center, Vector3 size, int material, Quaternion? rotation = null)
            {
                Quaternion q = rotation ?? Quaternion.identity;
                Vector3[] p = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    p[i] = center + q * Vector3.Scale(size * .5f,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Quad(p[0], p[1], p[3], p[2], q * Vector3.back, material);
                Quad(p[4], p[6], p[7], p[5], q * Vector3.forward, material);
                Quad(p[0], p[2], p[6], p[4], q * Vector3.left, material);
                Quad(p[1], p[5], p[7], p[3], q * Vector3.right, material);
                Quad(p[2], p[3], p[7], p[6], q * Vector3.up, material);
                Quad(p[0], p[4], p[5], p[1], q * Vector3.down, material);
            }

            public void Prism(Vector3 center, float width, float height, float depth, float corner, int material, Quaternion? rotation = null)
            {
                int firstVertex = vertices.Count;
                float bevel = Mathf.Min(.0025f, height * .3f, Mathf.Min(width, depth) * .12f);
                Vector3[] Outline(float inset, float y)
                {
                    float x = width * .5f - inset, z = depth * .5f - inset;
                    float c = Mathf.Clamp(corner - inset, .0001f, Mathf.Min(x, z) * .8f);
                    return new[] { center + new Vector3(-x + c, y, -z), center + new Vector3(x - c, y, -z),
                        center + new Vector3(x, y, -z + c), center + new Vector3(x, y, z - c),
                        center + new Vector3(x - c, y, z), center + new Vector3(-x + c, y, z),
                        center + new Vector3(-x, y, z - c), center + new Vector3(-x, y, -z + c) };
                }
                var rings = new[] { Outline(bevel, -height * .5f), Outline(0, -height * .5f + bevel),
                    Outline(0, height * .5f - bevel), Outline(bevel, height * .5f) };
                for (int level = 0; level < 3; level++)
                    for (int i = 0; i < 8; i++)
                    {
                        int next = (i + 1) % 8;
                        Vector3 normal = (rings[level][i] + rings[level][next]) * .5f - center;
                        normal.y = level == 0 ? -bevel : level == 2 ? bevel : 0;
                        Quad(rings[level][i], rings[level][next], rings[level + 1][next], rings[level + 1][i], normal, material);
                    }
                for (int i = 0; i < 8; i++)
                {
                    int next = (i + 1) % 8;
                    Triangle(center + Vector3.up * height * .5f, rings[3][i], rings[3][next], Vector3.up, material);
                    Triangle(center - Vector3.up * height * .5f, rings[0][i], rings[0][next], Vector3.down, material);
                }
                if (rotation.HasValue)
                    for (int i = firstVertex; i < vertices.Count; i++) vertices[i] = center + rotation.Value * (vertices[i] - center);
            }

            public void Cylinder(Vector3 center, float radius, float height, int material, Quaternion? rotation = null)
            {
                const int segments = 16;
                int firstVertex = vertices.Count;
                float bevel = Mathf.Min(height * .25f, radius * .18f);
                float[] radii = { radius - bevel, radius, radius, radius - bevel };
                float[] levels = { -height * .5f, -height * .5f + bevel, height * .5f - bevel, height * .5f };
                Vector3 Point(int ring, int side)
                {
                    float angle = side * Mathf.PI * 2f / segments;
                    return center + new Vector3(Mathf.Cos(angle) * radii[ring], levels[ring], Mathf.Sin(angle) * radii[ring]);
                }
                for (int side = 0; side < segments; side++)
                {
                    int next = (side + 1) % segments;
                    for (int ring = 0; ring < 3; ring++)
                    {
                        Vector3 normal = (Point(ring, side) + Point(ring, next)) * .5f - center;
                        normal.y = ring == 0 ? -bevel : ring == 2 ? bevel : 0;
                        Quad(Point(ring, side), Point(ring, next), Point(ring + 1, next), Point(ring + 1, side), normal, material);
                    }
                    Triangle(center - Vector3.up * height * .5f, Point(0, side), Point(0, next), Vector3.down, material);
                    Triangle(center + Vector3.up * height * .5f, Point(3, side), Point(3, next), Vector3.up, material);
                }
                if (rotation.HasValue)
                    for (int i = firstVertex; i < vertices.Count; i++) vertices[i] = center + rotation.Value * (vertices[i] - center);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, int material)
            {
                Triangle(a, b, c, normal, material);
                Triangle(a, c, d, normal, material);
            }

            void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, int material)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0) (b, c) = (c, b);
                if (!indices.TryGetValue(material, out var triangles)) indices.Add(material, triangles = new List<int>());
                int first = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                // Textured ceramic/metal use one physical 12.5 cm tile, never a
                // complete texture stretched over each fan triangle or bevel.
                // Project coplanar surfaces in the same frame so their fine grain
                // remains continuous even when their topology is split.
                Vector3 face = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                foreach (var v in new[] { a, b, c })
                {
                    Vector2 surface = face.y >= face.x && face.y >= face.z ? new Vector2(v.x, v.z) :
                                      face.x >= face.z ? new Vector2(v.z, v.y) : new Vector2(v.x, v.y);
                    uv.Add(material == Ceramic || material == Metal ? surface * 8f : Vector2.zero);
                }
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            }

            public void Apply(Transform target, Material[] palette, string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uv);
                mesh.subMeshCount = indices.Count;
                var materials = new List<Material>();
                int submesh = 0;
                foreach (var pair in indices)
                {
                    mesh.SetTriangles(pair.Value, submesh++);
                    materials.Add(palette[pair.Key]);
                }
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                target.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            }
        }
    }
}
