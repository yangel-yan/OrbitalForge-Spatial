using UnityEngine;

namespace SpaceBall
{
    public static partial class OrbitalEnergyEffectsArt
    {
        static void BuildDeliveryCeremony(OrbitalEnergyEffects fx)
        {
            Mesh petal = DeliveryLockMesh();
            fx.deliveryLocks = new Transform[3];
            for (int i = 0; i < fx.deliveryLocks.Length; i++)
            {
                Transform jaw = MeshNode(fx.dockRoot, "Delivery lock petal " + i, petal, fx.dimGold);
                jaw.localScale = new Vector3(.018f, .010f, .032f);
                float angle = (i * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                jaw.localPosition = radial * .055f + Vector3.up * .018f;
                jaw.localRotation = Quaternion.LookRotation(-radial, Vector3.up);
                fx.deliveryLocks[i] = jaw;
            }
            // Reuse the exported energy shard instead of creating another mesh
            // resource for the station's small, stable receiving crystal.
            Mesh crystal = fx.dockFlow[0].GetComponent<MeshFilter>().sharedMesh;
            fx.deliveryCore = MeshNode(fx.dockRoot, "Delivery receiving core", crystal, fx.gold);
            // Cap the six-bar energy bank; the existing functional station lamp
            // already occupies the capture collar's centre.
            fx.deliveryCore.localPosition = new Vector3(.064f, .102f, .055f);
            fx.deliveryCore.localRotation = Quaternion.Euler(90, 0, 0);
            fx.deliveryCore.localScale = Vector3.one * .018f;
            fx.deliveryCoreRenderer = fx.deliveryCore.GetComponent<Renderer>();
        }

        static Mesh DeliveryLockMesh()
        {
            // A broad outer seat tapers to a short inward nose. All three jaws
            // share this closed bevelled prism and move only by transform.
            Vector2[] section = {
                new Vector2(-.5f,-.35f), new Vector2(-.5f,.20f),
                new Vector2(-.25f,.5f), new Vector2(.15f,.5f),
                new Vector2(.5f,.10f), new Vector2(.5f,-.10f),
                new Vector2(.25f,-.35f), new Vector2(-.25f,-.5f) };
            var vertices = new Vector3[16];
            var triangles = new int[84];
            for (int i = 0; i < 8; i++)
            {
                vertices[i] = new Vector3(-.5f, section[i].y, section[i].x);
                vertices[i + 8] = new Vector3(.5f, section[i].y, section[i].x);
            }
            int index = 0;
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                triangles[index++] = i; triangles[index++] = next; triangles[index++] = i + 8;
                triangles[index++] = next; triangles[index++] = next + 8; triangles[index++] = i + 8;
            }
            for (int i = 1; i < 7; i++)
            {
                triangles[index++] = 0; triangles[index++] = i + 1; triangles[index++] = i;
                triangles[index++] = 8; triangles[index++] = i + 8; triangles[index++] = i + 9;
            }
            return Finish("Orbital_DeliveryLockPetal", vertices, triangles);
        }
    }
}
