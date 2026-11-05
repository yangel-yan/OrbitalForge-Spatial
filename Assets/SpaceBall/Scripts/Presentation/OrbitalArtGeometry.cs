using System.Collections.Generic;
using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// Decorative scene meshes only. No collider, Rigidbody, or motion behaviour
    /// is created here. The Editor scene builder persists generated meshes.
    /// </summary>
    public static class OrbitalArtGeometry
    {
        static Mesh unitBox;

        public static Transform BuildChassis(Transform parent, Material body, Material trim, Material light, Material deck)
        {
            Transform root = Group(parent, "Art_Chassis", Vector3.zero);
            Solid(root, "Bevelled_Hull", new Vector3(0, -.027f, 0),
                OctagonalPrism(.96f, .032f, .70f, .065f, .006f), body);
            Solid(root, "Lower_Reveal", new Vector3(0, -.039f, 0),
                OctagonalRing(.932f, .672f, .054f, .008f, .004f), trim);
            // Four broad deck panels expose the dark structural tray in their
            // seams. They stay below the former top surface and all ball paths.
            foreach (float x in new[] { -1f, 1f })
                foreach (float z in new[] { -1f, 1f })
                    Solid(root, "Deck_Quadrant", new Vector3(x * .224f, -.008f, z * .159f),
                        OctagonalPrism(.442f, .005f, .311f, .018f, .0012f), deck);
            Solid(root, "Machined_Rim", new Vector3(0, -.008f, 0),
                OctagonalRing(.954f, .694f, .062f, .0055f, .003f), trim);
            foreach (float sign in new[] { -1f, 1f })
            {
                // Two service cassettes, with recessed open ventilation slots.
                // Their broad shapes establish scale without adding visual noise.
                Transform hatch = Solid(root, "Service_Cassette", new Vector3(sign * .351f, -.006f, -.219f),
                    OctagonalPrism(.143f, .004f, .073f, .011f, .001f), trim);
                Solid(hatch, "Recessed_Vent_Well", new Vector3(0, .00205f, 0),
                    OctagonalPrism(.117f, .0007f, .048f, .006f, .00015f), body);
                for (int i = -3; i <= 3; i++)
                    Housing(hatch, "Vent_Louvre", new Vector3(i * .014f, .0027f, 0),
                        new Vector3(.006f, .001f, .036f), .00035f, trim);
                foreach (float end in new[] { -1f, 1f })
                    Housing(hatch, "Service_Cassette_Latch", new Vector3(end * .064f, .0021f, 0),
                        new Vector3(.005f, .0013f, .020f), .0004f, deck);
                // Side-wall vents and split reveals are part of the chassis,
                // rather than decorative objects hovering above the work area.
                Housing(root, "Side_Recess", new Vector3(sign * .478f, -.027f, -.075f),
                    new Vector3(.002f, .013f, .151f), .0006f, body);
                for (int i = -3; i <= 3; i++)
                    Box(root, "Side_Heat_Sink", new Vector3(sign * .479f, -.027f, -.075f + i * .018f),
                        new Vector3(.001f, .008f, .006f), trim);
            }

            foreach (float x in new[] { -.425f, .425f })
            {
                foreach (float z in new[] { -.29f, .29f })
                {
                    Cylinder(root, "Recessed_Fastener", new Vector3(x, -.004f, z), .004f, .001f, trim, 8);
                    Solid(root, "Corner_Inlay", new Vector3(x, -.0063f, z),
                        OctagonalPrism(.061f, .002f, .041f, .009f, .0006f), trim);
                    Cylinder(root, "Landing_Foot", new Vector3(x, -.043f, z), .021f, .009f, body, 12);
                }
            }

            foreach (float sign in new[] { -1f, 1f })
            {
                Box(root, "Front_Light_Recess", new Vector3(sign * .30f, -.004f, -.327f),
                    new Vector3(.076f, .003f, .006f), body);
                Box(root, "Front_Light_Strip", new Vector3(sign * .30f, -.002f, -.327f),
                    new Vector3(.056f, .0015f, .0025f), light);
                Box(root, "Side_Service_Port", new Vector3(sign * .473f, -.025f, -.16f),
                    new Vector3(.003f, .011f, .045f), trim);
            }
            CombineStaticArt(root);
            return root;
        }

        public static Transform BuildMoon(Transform parent, Vector3 point, float radius,
            Material rock, Material dark, Material accent)
        {
            Transform root = Group(parent, "Art_ReactorMoon", point);
            var surveyHeight = (Texture2D)rock.GetTexture("_ParallaxMap");
            Solid(root, "Crater_Hull", Vector3.zero, OrbitalLunarSurface.CreateMesh(surveyHeight, radius), rock)
                .localRotation = Quaternion.Euler(0, 90, 0);

            // The housing follows the sphere's outside without filling the
            // centre opening or projecting a decorative arm into a ball route.
            Quaternion bandRotation = Quaternion.Euler(76f, 0f, -16f);
            Ring(root, "Reactor_Band", Vector3.zero, radius * 1.018f, radius * .021f,
                dark, bandRotation, 28, 4);
            for (int i = 0; i < 4; i++)
            {
                float angle = (i * 90f + 25f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                Vector3 localPoint = bandRotation * (radial * (radius * 1.028f));
                Transform clip = Box(root, "Reactor_Clamp", localPoint,
                    new Vector3(radius * .12f, radius * .045f, radius * .09f), dark);
                clip.localRotation = bandRotation * Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                Transform stripe = Box(root, "Reactor_Clamp_Light", localPoint +
                    bandRotation * Vector3.forward * (radius * .05f),
                    new Vector3(radius * .075f, radius * .019f, radius * .012f), accent);
                stripe.localRotation = clip.localRotation;
            }
            Cylinder(root, "Moon_Support_Collar", new Vector3(0, -radius + .003f, 0),
                radius * .33f, .012f, dark, 12);
            return root;
        }

        public static Transform BuildStation(Transform parent, Vector3 point,
            Material body, Material trim, Material light)
        {
            Transform root = Group(parent, "Art_DockingStation", point);
            Solid(root, "Dock_Foundation", new Vector3(0, -.026f, 0),
                OctagonalPrism(.13f, .014f, .13f, .028f, .003f), body);
            Solid(root, "Dock_InnerDeck", new Vector3(0, -.017f, 0),
                OctagonalRing(.116f, .116f, .025f, .014f, .006f), trim);
            Ring(root, "Capture_Collar", new Vector3(0, -.011f, 0), .043f, .003f,
                trim, Quaternion.Euler(90, 0, 0), 32, 6);
            Transform illumination = Group(root, "Illumination", Vector3.zero);
            Ring(illumination, "Dock_Light", new Vector3(0, -.007f, 0), .036f, .0022f,
                light, Quaternion.Euler(90, 0, 0), 32, 4);
            for (int i = 0; i < 4; i++)
            {
                float angle = i * 90f * Mathf.Deg2Rad;
                Vector3 at = new Vector3(Mathf.Cos(angle) * .054f, -.010f, Mathf.Sin(angle) * .054f);
                Transform jaw = Housing(root, "Dock_Lock_Jaw", at, new Vector3(.017f, .009f, .013f), .002f, trim);
                jaw.localRotation = Quaternion.Euler(0, -i * 90f, 0);
            }
            return root;
        }

        /// <summary>Clean original terminal, entirely inside the former generated-model side bay.</summary>
        public static Transform BuildStationHabitat(Transform station, Material body, Material ceramic, Material light)
        {
            Transform existing = station.Find("Station_Habitat");
            if (existing != null) return existing;
            Transform habitat = Group(station, "Station_Habitat", new Vector3(.12f, -.028f, .025f));
            Housing(habitat, "Habitat_Plinth", new Vector3(0, .007f, 0), new Vector3(.136f, .014f, .122f), .004f, body);
            Housing(habitat, "Habitat_Collar", new Vector3(0, .020f, 0), new Vector3(.126f, .013f, .112f), .004f, ceramic);
            Housing(habitat, "Habitat_Service_Core", new Vector3(0, .052f, 0), new Vector3(.101f, .064f, .091f), .010f, body);
            Housing(habitat, "Habitat_UpperShell", new Vector3(0, .082f, 0), new Vector3(.117f, .043f, .106f), .009f, ceramic);
            Housing(habitat, "Habitat_Roof_Reveal", new Vector3(0, .103f, 0), new Vector3(.086f, .005f, .077f), .002f, body);
            Housing(habitat, "Habitat_Roof", new Vector3(0, .108f, 0), new Vector3(.083f, .008f, .074f), .003f, ceramic);
            // Broad inset glazing and two structural cheeks, not a field of noisy vents.
            Housing(habitat, "Habitat_Observation_Window", new Vector3(0, .081f, -.0535f), new Vector3(.071f, .018f, .003f), .001f, body);
            foreach (float side in new[] { -1f, 1f })
                Housing(habitat, "Habitat_Side_Cheek", new Vector3(side * .050f, .046f, 0),
                    new Vector3(.014f, .051f, .074f), .004f, ceramic);
            Cylinder(habitat, "Telemetry_Pedestal", new Vector3(0, .116f, .012f), .015f, .009f, body, 16);
            Cylinder(habitat, "Telemetry_Disc", new Vector3(0, .123f, .012f), .025f, .006f, ceramic, 20);
            Ring(habitat, "Telemetry_Inset", new Vector3(0, .127f, .012f), .017f, .0014f,
                body, Quaternion.Euler(90, 0, 0), 20, 4);
            Transform illumination = station.Find("Illumination");
            if (illumination == null) illumination = Group(station, "Illumination", Vector3.zero);
            Transform status = Group(illumination, "Habitat_Status", habitat.localPosition);
            Box(status, "Habitat_Window_Light", new Vector3(0, .072f, -.0555f), new Vector3(.051f, .002f, .0015f), light);
            Box(status, "Habitat_Dock_Link", new Vector3(-.055f, .035f, -.018f), new Vector3(.002f, .018f, .026f), light);
            return habitat;
        }

        /// <summary>Decorative frame around an existing calibrated plate. Its collider and pose stay intact.</summary>
        public static Transform BuildReflectorHousing(Transform plate, Material body, Material ceramic, Material gold)
        {
            Transform existing = plate.Find("Art_ReflectorHousing");
            if (existing != null) return existing;
            var renderer = plate.GetComponent<Renderer>();
            var filter = plate.GetComponent<MeshFilter>();
            if (renderer != null) Object.DestroyImmediate(renderer);
            if (filter != null) Object.DestroyImmediate(filter);
            Transform frame = Group(plate, "Art_ReflectorHousing", Vector3.zero);
            Vector3 scale = plate.localScale;
            frame.localScale = new Vector3(1f / Mathf.Max(Mathf.Abs(scale.x), .00001f),
                1f / Mathf.Max(Mathf.Abs(scale.y), .00001f), 1f / Mathf.Max(Mathf.Abs(scale.z), .00001f));
            Housing(frame, "Reflector_Chassis", new Vector3(0, 0, .004f), new Vector3(.096f, .072f, .013f), .003f, body);
            Housing(frame, "Reflector_Contact_Face", new Vector3(0, 0, -.0034f), new Vector3(.084f, .058f, .0012f), .0005f, gold);
            foreach (float side in new[] { -1f, 1f })
            {
                Housing(frame, "Reflector_Side_Cap", new Vector3(side * .046f, 0, .001f), new Vector3(.008f, .071f, .010f), .002f, ceramic);
                Housing(frame, "Reflector_End_Cap", new Vector3(0, side * .0335f, .001f), new Vector3(.091f, .007f, .010f), .002f, ceramic);
            }
            Housing(frame, "Reflector_Actuator", new Vector3(0, -.010f, .018f), new Vector3(.046f, .036f, .016f), .004f, body);
            return frame;
        }

        public static Transform BuildLauncher(Transform parent, Vector3 point,
            Material body, Material trim, Material light)
        {
            Transform root = Group(parent, "Art_Launcher", point);
            // Local +Z is the muzzle; both prototype launch lanes run along +X.
            root.localRotation = Quaternion.Euler(0, 90, 0);
            Solid(root, "Launcher_Cradle", new Vector3(0, -.030f, -.042f),
                OctagonalPrism(.078f, .018f, .136f, .018f, .004f), body);
            Transform barrel = Cylinder(root, "Machined_Barrel", new Vector3(0, -.001f, -.047f),
                .025f, .082f, body, 32);
            barrel.localRotation = Quaternion.Euler(90, 0, 0);
            Ring(root, "Muzzle_Rim", new Vector3(0, -.001f, -.005f), .024f, .005f,
                trim, Quaternion.identity, 28, 6);
            Ring(root, "Muzzle_Iris", new Vector3(0, -.001f, -.001f), .0185f, .0022f,
                light, Quaternion.identity, 20, 4);
            Ring(root, "Rear_Coil", new Vector3(0, -.001f, -.077f), .029f, .003f,
                trim, Quaternion.identity, 24, 6);
            Ring(root, "Barrel_Collar", new Vector3(0, -.001f, -.044f), .030f, .003f,
                trim, Quaternion.identity, 20, 4);
            foreach (float sign in new[] { -1f, 1f })
            {
                Housing(root, "Cradle_Side_Rail", new Vector3(sign * .029f, -.010f, -.047f),
                    new Vector3(.011f, .020f, .072f), .003f, trim);
                Transform piston = Cylinder(root, "Charging_Piston", new Vector3(sign * .028f, .008f, -.046f),
                    .0035f, .048f, trim, 12);
                piston.localRotation = Quaternion.Euler(90, 0, 0);
                Transform socket = Cylinder(root, "Piston_Rear_Socket", new Vector3(sign * .028f, .008f, -.071f),
                    .005f, .012f, body, 12);
                socket.localRotation = piston.localRotation;
                Box(root, "Charge_Window", new Vector3(sign * .0356f, .004f, -.043f),
                    new Vector3(.0012f, .0025f, .023f), light);
            }
            // Split dorsal shroud leaves the dark barrel visible between ceramic
            // ribs. It reads as assembled hardware rather than a solid cylinder.
            for (int i = 0; i < 3; i++)
            {
                float angle = (i * 32f - 32f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0);
                Transform rib = Housing(root, "Barrel_Heat_Shroud", new Vector3(0, -.001f, -.049f) + radial * .0265f,
                    new Vector3(.010f, .003f, .054f), .001f, trim);
                rib.localRotation = Quaternion.Euler(0, 0, -angle * Mathf.Rad2Deg);
            }
            CombineStaticArt(root);
            return root;
        }

        public static Transform BuildTarget(Transform parent, Vector3 point,
            Material body, Material trim, Material light)
        {
            Transform root = Group(parent, "Art_MechanicalTarget", point);
            Transform structure = Group(root, "Structure", Vector3.zero);
            Transform illumination = Group(root, "Illumination", Vector3.zero);
            Vector3 center = Vector3.up * .010f;
            // The existing broad hoop carries the same real power state as the
            // thin core ring. Geometry stays fixed; only its shared material changes.
            Ring(illumination, "Machined_Hoop", center, .0385f, .0045f,
                light, Quaternion.identity, 32, 6);
            Ring(structure, "Inner_Guide_Rim", center - Vector3.forward * .003f, .032f, .0025f,
                trim, Quaternion.identity, 24, 4);
            Ring(illumination, "CoreRing", center - Vector3.forward * .006f, .032f, .0017f,
                light, Quaternion.identity, 24, 4);
            for (int i = 0; i < 4; i++)
            {
                float angle = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 at = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * .039f;
                Transform clamp = Housing(structure, "Hoop_Clamp", at, new Vector3(.013f, .009f, .012f), .002f, trim);
                clamp.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                Transform tab = Box(illumination, "Power_Tab", at - Vector3.forward * .008f,
                    new Vector3(.007f, .004f, .002f), light);
                tab.localRotation = clamp.localRotation;
            }
            foreach (float sign in new[] { -1f, 1f })
            {
                Housing(structure, "Target_Foot", new Vector3(sign * .042f, -.025f, 0),
                    new Vector3(.018f, .010f, .035f), .002f, body);
                Transform strut = Box(structure, "Target_Strut", new Vector3(sign * .042f, -.013f, 0),
                    new Vector3(.008f, .026f, .012f), trim);
                strut.localRotation = Quaternion.Euler(0, 0, -sign * 14f);
            }
            return root;
        }

        /// <summary>Optional support below an anchor. top is a local track-centre position.</summary>
        public static Transform BuildPylon(Transform parent, Vector3 top, Material body, Material trim)
        {
            Transform root = Group(parent, "Art_Pylon", Vector3.zero);
            float capY = top.y - .032f;
            float baseY = -.007f;
            float height = Mathf.Max(.015f, capY - baseY);
            Cylinder(root, "Pylon_Base", new Vector3(top.x, baseY, top.z), .026f, .009f, body, 8);
            Cylinder(root, "Pylon_Column", new Vector3(top.x, baseY + height * .5f, top.z),
                .008f, height, trim, 8);
            Solid(root, "Pylon_Cap", new Vector3(top.x, capY, top.z),
                OctagonalPrism(.041f, .009f, .041f, .008f, .002f), body);
            return root;
        }

        /// <summary>A torus in local XY, with its axis along +Z.</summary>
        public static Transform Ring(Transform parent, string name, Vector3 point, float radius,
            float tubeRadius, Material material, Quaternion rotation, int segments = 24, int tubeSides = 4)
        {
            Transform ring = Solid(parent, name, point, Torus(radius, tubeRadius,
                Mathf.Clamp(segments, 8, 48), Mathf.Clamp(tubeSides, 3, 8)), material);
            ring.localRotation = rotation;
            return ring;
        }

        public static Transform Ring(Transform parent, Vector3 point, float radius,
            float tubeRadius, Material material)
        {
            return Ring(parent, "Decorative_Ring", point, radius, tubeRadius, material, Quaternion.identity);
        }

        /// <summary>A shared chamfer scale for standard ceramic and mechanical housings.</summary>
        public static Transform Housing(Transform parent, string name, Vector3 point, Vector3 size, float bevel, Material material)
        {
            return Solid(parent, name, point, OctagonalPrism(size.x, size.y, size.z,
                Mathf.Min(size.x, size.z) * .19f, bevel), material);
        }

        static Transform Group(Transform parent, string name, Vector3 point)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = point;
            return root;
        }

        static Transform Solid(Transform parent, string name, Vector3 point, Mesh mesh, params Material[] materials)
        {
            Transform root = Group(parent, name, point);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return root;
        }

        // Both the chassis and launcher are static art with no child references.
        // Keep their surface detail without exporting a renderer per vent or rib.
        static void CombineStaticArt(Transform root)
        {
            var groups = new Dictionary<Material, List<CombineInstance>>();
            var sourceMeshes = new HashSet<Mesh>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                sourceMeshes.Add(filter.sharedMesh);
                var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!groups.TryGetValue(materials[i], out var instances))
                        groups.Add(materials[i], instances = new List<CombineInstance>());
                    instances.Add(new CombineInstance { mesh = filter.sharedMesh, subMeshIndex = i,
                        transform = root.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                }
            }
            var partial = new List<CombineInstance>();
            var resultMaterials = new List<Material>();
            foreach (var group in groups)
            {
                var mesh = new Mesh();
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                partial.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
                resultMaterials.Add(group.Key);
            }
            var combined = new Mesh { name = "Orbital_" + root.name + "_Surfaces" };
            combined.CombineMeshes(partial.ToArray(), false, true);
            for (int i = root.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.GetChild(i).gameObject);
            root.gameObject.AddComponent<MeshFilter>().sharedMesh = combined;
            root.gameObject.AddComponent<MeshRenderer>().sharedMaterials = resultMaterials.ToArray();
            foreach (var part in partial) Object.DestroyImmediate(part.mesh);
            foreach (var mesh in sourceMeshes) if (mesh != unitBox) Object.DestroyImmediate(mesh);
        }

        static Transform Box(Transform parent, string name, Vector3 point, Vector3 size, Material material)
        {
            if (unitBox == null)
            {
                var b = new MeshBuilder();
                Vector3[] v = {
                    new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
                    new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                    new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
                    new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
                b.Quad(v[0],v[1],v[2],v[3],Vector3.back);
                b.Quad(v[4],v[5],v[6],v[7],Vector3.forward);
                b.Quad(v[0],v[4],v[7],v[3],Vector3.left);
                b.Quad(v[1],v[5],v[6],v[2],Vector3.right);
                b.Quad(v[3],v[2],v[6],v[7],Vector3.up);
                b.Quad(v[0],v[1],v[5],v[4],Vector3.down);
                unitBox = b.Mesh("Orbital_UnitBox");
            }
            Transform box = Solid(parent, name, point, unitBox, material);
            box.localScale = size;
            return box;
        }

        static Transform Cylinder(Transform parent, string name, Vector3 point,
            float radius, float height, Material material, int segments)
        {
            var b = new MeshBuilder();
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 loA = new Vector3(Mathf.Cos(a)*radius,-height*.5f,Mathf.Sin(a)*radius);
                Vector3 loB = new Vector3(Mathf.Cos(c)*radius,-height*.5f,Mathf.Sin(c)*radius);
                Vector3 hiA = loA + Vector3.up*height;
                Vector3 hiB = loB + Vector3.up*height;
                b.Quad(loA,loB,hiB,hiA,new Vector3(Mathf.Cos((a+c)*.5f),0,Mathf.Sin((a+c)*.5f)));
                b.Triangle(Vector3.up*height*.5f,hiA,hiB,Vector3.up);
                b.Triangle(Vector3.down*height*.5f,loA,loB,Vector3.down);
            }
            return Solid(parent,name,point,b.Mesh("Orbital_Cylinder"),material);
        }

        static Vector3[] Outline(float width, float depth, float corner, float y)
        {
            float x=width*.5f,z=depth*.5f;
            float c=Mathf.Clamp(corner,.0001f,Mathf.Min(x,z)*.8f);
            return new[] { new Vector3(-x+c,y,-z),new Vector3(x-c,y,-z),new Vector3(x,y,-z+c),
                new Vector3(x,y,z-c),new Vector3(x-c,y,z),new Vector3(-x+c,y,z),
                new Vector3(-x,y,z-c),new Vector3(-x,y,-z+c) };
        }

        static Mesh OctagonalPrism(float width, float height, float depth, float corner, float bevel)
        {
            var b=new MeshBuilder();
            float inset=Mathf.Clamp(bevel,0,Mathf.Min(height*.45f,Mathf.Min(width,depth)*.15f));
            Vector3[][] rings={Outline(width-inset*2,depth-inset*2,Mathf.Max(.001f,corner-inset),-height*.5f),
                Outline(width,depth,corner,-height*.5f+inset),Outline(width,depth,corner,height*.5f-inset),
                Outline(width-inset*2,depth-inset*2,Mathf.Max(.001f,corner-inset),height*.5f)};
            for(int level=0;level<rings.Length-1;level++)
                for(int i=0;i<8;i++)
                {
                    int j=(i+1)%8;
                    Vector3 normal=(rings[level][i]+rings[level][j])*.5f;
                    normal.y=level==0?-inset:level==2?inset:0;
                    b.Quad(rings[level][i],rings[level][j],rings[level+1][j],rings[level+1][i],normal);
                }
            for(int i=0;i<8;i++)
            {
                int j=(i+1)%8;
                b.Triangle(Vector3.down*height*.5f,rings[0][i],rings[0][j],Vector3.down);
                b.Triangle(Vector3.up*height*.5f,rings[3][i],rings[3][j],Vector3.up);
            }
            return b.Mesh("Orbital_BevelledOctagon");
        }

        static Mesh OctagonalRing(float width,float depth,float corner,float inset,float height)
        {
            var b=new MeshBuilder();
            var lo=Outline(width,depth,corner,-height*.5f);
            var hi=Outline(width,depth,corner,height*.5f);
            var innerLo=Outline(width-inset*2,depth-inset*2,Mathf.Max(.001f,corner-inset),-height*.5f);
            var innerHi=Outline(width-inset*2,depth-inset*2,Mathf.Max(.001f,corner-inset),height*.5f);
            for(int i=0;i<8;i++)
            {
                int j=(i+1)%8;
                Vector3 outward=(lo[i]+lo[j])*.5f;outward.y=0;
                b.Quad(hi[i],hi[j],innerHi[j],innerHi[i],Vector3.up);
                b.Quad(lo[i],lo[j],innerLo[j],innerLo[i],Vector3.down);
                b.Quad(lo[i],lo[j],hi[j],hi[i],outward);
                b.Quad(innerLo[i],innerLo[j],innerHi[j],innerHi[i],-outward);
            }
            return b.Mesh("Orbital_OctagonalRim");
        }

        static Mesh Torus(float radius,float tube,int segments,int sides)
        {
            var b=new MeshBuilder();
            for(int i=0;i<segments;i++)
                for(int j=0;j<sides;j++)
                {
                    float a=i*Mathf.PI*2/segments,c=(i+1)*Mathf.PI*2/segments;
                    float u=j*Mathf.PI*2/sides,v=(j+1)*Mathf.PI*2/sides;
                    Vector3 p0=TorusPoint(a,u,radius,tube),p1=TorusPoint(c,u,radius,tube);
                    Vector3 p2=TorusPoint(c,v,radius,tube),p3=TorusPoint(a,v,radius,tube);
                    float mid=(a+c)*.5f;
                    Vector3 normal=(p0+p1+p2+p3)*.25f-new Vector3(Mathf.Cos(mid)*radius,Mathf.Sin(mid)*radius,0);
                    float cross=(u+v)*.5f;
                    Vector3 na=new Vector3(Mathf.Cos(a)*Mathf.Cos(cross),Mathf.Sin(a)*Mathf.Cos(cross),Mathf.Sin(cross));
                    Vector3 nb=new Vector3(Mathf.Cos(c)*Mathf.Cos(cross),Mathf.Sin(c)*Mathf.Cos(cross),Mathf.Sin(cross));
                    b.SmoothQuad(p0,p1,p2,p3,normal,na,nb);
                }
            return b.Mesh("Orbital_MachinedRing");
        }

        static Vector3 TorusPoint(float angle,float side,float radius,float tube)
        {
            float r=radius+Mathf.Cos(side)*tube;
            return new Vector3(Mathf.Cos(angle)*r,Mathf.Sin(angle)*r,Mathf.Sin(side)*tube);
        }

        // Deliberately composed large/medium impact bowls, not random surface noise.
        // The front hemisphere carries the readable hero forms; smaller pits continue
        // the same sculptural language around the silhouette and the rear hemisphere.
        static readonly Vector3[] craterDirections={new Vector3(-.40f,.52f,-.75f).normalized,
            new Vector3(.42f,.62f,-.66f).normalized,new Vector3(.70f,.02f,-.70f).normalized,
            new Vector3(-.10f,-.34f,-.94f).normalized,new Vector3(-.74f,.10f,-.65f).normalized,
            new Vector3(.08f,.88f,-.45f).normalized,new Vector3(.77f,.48f,.42f).normalized,
            new Vector3(-.50f,.46f,.72f).normalized,new Vector3(.30f,-.40f,.84f).normalized,
            new Vector3(-.73f,-.47f,.42f).normalized,new Vector3(.06f,.12f,.99f).normalized,
            new Vector3(-.91f,.34f,.20f).normalized};
        static readonly float[] craterAngles={.33f,.25f,.22f,.20f,.13f,.15f,.24f,.28f,.21f,.16f,.14f,.12f};
        static readonly float[] craterDepths={.125f,.105f,.09f,.085f,.05f,.06f,.10f,.115f,.09f,.065f,.055f,.045f};

        static Vector3 MoonPoint(Vector3 direction,float radius)
        {
            float displacement=0;
            for(int i=0;i<craterDirections.Length;i++)
            {
                float angle=Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction,craterDirections[i]),-1,1));
                float t=angle/craterAngles[i];
                if(t>1.35f)continue;
                if(t<1f)
                    displacement-=radius*craterDepths[i]*Mathf.Pow(.5f+.5f*Mathf.Cos(t*Mathf.PI),.85f);
                // Smooth raised lip gives a genuine shaded edge and a slight scallop
                // at the silhouette. No separate floating torus is laid over the pit.
                float rim=(t-.96f)/.135f;
                displacement+=radius*Mathf.Lerp(.011f,.022f,Mathf.InverseLerp(.12f,.33f,craterAngles[i]))*Mathf.Exp(-rim*rim);
            }
            return direction*(radius+displacement);
        }

        static float CraterShadeDistance(Vector3 direction)
        {
            float distance=10f;
            for(int i=0;i<craterDirections.Length;i++)
                distance=Mathf.Min(distance,Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction,craterDirections[i]),-1,1))/craterAngles[i]-.55f);
            return distance;
        }

        static Mesh CraterMoon(float radius)
        {
            const int longitude=96,latitude=64;
            var b=new MeshBuilder(2);
            for(int lat=0;lat<latitude;lat++)
                for(int lon=0;lon<longitude;lon++)
                {
                    Vector3 d0=SphereDirection(lat,lon,latitude,longitude);
                    Vector3 d1=SphereDirection(lat,lon+1,latitude,longitude);
                    Vector3 d2=SphereDirection(lat+1,lon+1,latitude,longitude);
                    Vector3 d3=SphereDirection(lat+1,lon,latitude,longitude);
                    // Keep separate u=0 and u=1 vertices at the longitude seam.
                    // Each polar wedge has its own midpoint UV to avoid a degenerate
                    // texture triangle while retaining the existing sphere geometry.
                    float u0=(float)lon/longitude,u1=(float)(lon+1)/longitude;
                    float v0=1f-(float)lat/latitude,v1=1f-(float)(lat+1)/latitude;
                    var uv0=new Vector2(lat==0?(u0+u1)*.5f:u0,v0);
                    var uv1=new Vector2(u1,v0);
                    var uv2=new Vector2(lat==latitude-1?(u0+u1)*.5f:u1,v1);
                    var uv3=new Vector2(u0,v1);
                    if(lat>0)
                    {
                        MoonTriangle(b,d0,d1,d2,uv0,uv1,uv2,radius);
                    }
                    if(lat<latitude-1)
                    {
                        MoonTriangle(b,d0,d2,d3,uv0,uv2,uv3,radius);
                    }
                }
            var mesh=b.Mesh("Orbital_CraterMoon");
            SmoothMoonNormals(mesh,radius);
            return mesh;
        }

        struct MoonVertex
        {
            public Vector3 direction;
            public Vector2 uv;
            public float shade;
            public MoonVertex(Vector3 d, Vector2 texture)
            { direction=d;uv=texture;shade=CraterShadeDistance(d); }
        }

        static void MoonTriangle(MeshBuilder builder, Vector3 a, Vector3 b, Vector3 c,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, float radius)
        {
            var vertices=new[]{new MoonVertex(a,uvA),new MoonVertex(b,uvB),new MoonVertex(c,uvC)};
            bool first=vertices[0].shade<0;
            if((vertices[1].shade<0)==first && (vertices[2].shade<0)==first)
            {
                AddMoonTriangle(builder,vertices[0],vertices[1],vertices[2],radius,first?1:0);
                return;
            }
            // Clip both material regions to their actual circular boundary. Assigning
            // entire latitude triangles produced the old polygonal "stickers".
            for(int material=0;material<2;material++)
            {
                var polygon=new List<MoonVertex>(4);
                for(int i=0;i<3;i++)
                {
                    MoonVertex start=vertices[i],end=vertices[(i+1)%3];
                    bool insideStart=(start.shade<0)==(material==1);
                    bool insideEnd=(end.shade<0)==(material==1);
                    if(insideStart)polygon.Add(start);
                    if(insideStart!=insideEnd)polygon.Add(MoonShadeIntersection(start,end));
                }
                for(int i=1;i<polygon.Count-1;i++)
                    AddMoonTriangle(builder,polygon[0],polygon[i],polygon[i+1],radius,material);
            }
        }

        static MoonVertex MoonShadeIntersection(MoonVertex a,MoonVertex b)
        {
            float low=0,high=1;
            bool negative=a.shade<0;
            for(int step=0;step<16;step++)
            {
                float t=(low+high)*.5f;
                Vector3 direction=Vector3.Lerp(a.direction,b.direction,t).normalized;
                if((CraterShadeDistance(direction)<0)==negative)low=t;else high=t;
            }
            float fraction=(low+high)*.5f;
            return new MoonVertex(Vector3.Lerp(a.direction,b.direction,fraction).normalized,Vector2.Lerp(a.uv,b.uv,fraction));
        }

        static void AddMoonTriangle(MeshBuilder builder,MoonVertex a,MoonVertex b,MoonVertex c,float radius,int material)
        {
            builder.Triangle(MoonPoint(a.direction,radius),MoonPoint(b.direction,radius),MoonPoint(c.direction,radius),
                (a.direction+b.direction+c.direction).normalized,a.uv,b.uv,c.uv,material);
        }

        static void SmoothMoonNormals(Mesh mesh,float radius)
        {
            var vertices=mesh.vertices;
            var triangles=mesh.triangles;
            var keys=new Vector3Int[vertices.Length];
            var sums=new Dictionary<Vector3Int,Vector3>();
            // The longitude seam and poles have separate UV vertices. Quantizing
            // only the lookup key joins their floating-point roundoff differences
            // without moving or welding any vertex in the actual mesh.
            float tolerance=Mathf.Max(Mathf.Abs(radius)*.00001f,.0000001f);
            for(int i=0;i<vertices.Length;i++)
            {
                Vector3 p=vertices[i]/tolerance;
                keys[i]=new Vector3Int(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.y),Mathf.RoundToInt(p.z));
            }
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                // Unnormalized cross products give larger faces proportionate weight.
                Vector3 face=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                for(int corner=0;corner<3;corner++)
                {
                    var key=keys[triangles[i+corner]];
                    sums.TryGetValue(key,out var sum);
                    sums[key]=sum+face;
                }
            }
            var normals=new Vector3[vertices.Length];
            for(int i=0;i<normals.Length;i++)
            {
                Vector3 sum=sums[keys[i]];
                float squaredLength=sum.sqrMagnitude;
                // Area-weighted sums can be tiny at clipped crater boundaries.
                // Normalising explicitly preserves their direction at this scale.
                if(squaredLength>1e-30f)normals[i]=sum/Mathf.Sqrt(squaredLength);
                else
                {
                    Vector3 radial=vertices[i];
                    float radialSquared=radial.sqrMagnitude;
                    normals[i]=radialSquared>1e-30f?radial/Mathf.Sqrt(radialSquared):Vector3.up;
                }
            }
            mesh.normals=normals;
            mesh.RecalculateTangents();
        }

        static Vector3 SphereDirection(int lat,int lon,int latitude,int longitude)
        {
            float y=lat*Mathf.PI/latitude,a=lon*Mathf.PI*2/longitude;
            return new Vector3(Mathf.Sin(y)*Mathf.Cos(a),Mathf.Cos(y),Mathf.Sin(y)*Mathf.Sin(a));
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices=new List<Vector3>();
            readonly List<Vector3> normals=new List<Vector3>();
            readonly List<Vector2> uv=new List<Vector2>();
            readonly List<int>[] faces;

            public MeshBuilder(int materials=1)
            {
                faces=new List<int>[materials];
                for(int i=0;i<materials;i++)faces[i]=new List<int>();
            }

            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward,int material=0)
            {
                Triangle(a,b,c,outward,SurfaceUV(a,outward),SurfaceUV(b,outward),SurfaceUV(c,outward),material);
            }

            public void Triangle(Vector3 a,Vector3 b,Vector3 c,Vector3 outward,
                Vector2 uvA,Vector2 uvB,Vector2 uvC,int material=0,
                Vector3? shadingA=null,Vector3? shadingB=null,Vector3? shadingC=null)
            {
                Vector3 n=Vector3.Cross(b-a,c-a);
                if(n.sqrMagnitude<1e-16f)return;
                if(Vector3.Dot(n,outward)<0)
                {
                    Vector3 swap=b;b=c;c=swap;n=-n;
                    Vector2 swapUv=uvB;uvB=uvC;uvC=swapUv;
                    var swapNormal=shadingB;shadingB=shadingC;shadingC=swapNormal;
                }
                int first=vertices.Count;
                vertices.Add(a);vertices.Add(b);vertices.Add(c);
                n/=Mathf.Sqrt(n.sqrMagnitude);normals.Add(shadingA??n);normals.Add(shadingB??n);normals.Add(shadingC??n);
                uv.Add(uvA);uv.Add(uvB);uv.Add(uvC);
                faces[material].Add(first);faces[material].Add(first+1);faces[material].Add(first+2);
            }

            public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward,int material=0)
            {
                Triangle(a,b,c,outward,material);
                Triangle(a,c,d,outward,material);
            }

            public void SmoothQuad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 outward,Vector3 na,Vector3 nb)
            {
                Triangle(a,b,c,outward,SurfaceUV(a,outward),SurfaceUV(b,outward),SurfaceUV(c,outward),0,na,nb,nb);
                Triangle(a,c,d,outward,SurfaceUV(a,outward),SurfaceUV(c,outward),SurfaceUV(d,outward),0,na,nb,na);
            }

            static Vector2 SurfaceUV(Vector3 point,Vector3 normal)
            {
                Vector3 n=new Vector3(Mathf.Abs(normal.x),Mathf.Abs(normal.y),Mathf.Abs(normal.z));
                if(n.y>=n.x && n.y>=n.z)return new Vector2(point.x,point.z)*8f;
                if(n.x>=n.z)return new Vector2(point.z,point.y)*8f;
                return new Vector2(point.x,point.y)*8f;
            }

            public Mesh Mesh(string name)
            {
                var mesh=new Mesh{name=name};
                mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,uv);
                mesh.subMeshCount=faces.Length;
                for(int i=0;i<faces.Length;i++)mesh.SetTriangles(faces[i],i);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
