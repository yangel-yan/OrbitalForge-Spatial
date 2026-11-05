using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// Decorative NASA LOLA lunar globe. The height texture must be readable,
    /// linear, uncompressed RGB: R is the high byte and G the low byte of a
    /// 16-bit value mapping -12..+12 km relative to the 1737.4 km lunar radius.
    /// This mesh never supplies gameplay collision.
    /// </summary>
    public static class OrbitalLunarSurface
    {
        public const float ReferenceRadiusKm = 1737.4f;
        public const float ReliefMultiplier = 2f;
        public const float HeightMinimumKm = -12f;
        public const float HeightMaximumKm = 12f;
        const int LongitudeSegments = 128;
        const int LatitudeSegments = 80;
        const float CollisionClearance = .002f;

        public static Mesh CreateMesh(Texture2D height, float radius)
        {
            int stride = LongitudeSegments + 1;
            var vertices = new Vector3[stride * (LatitudeSegments + 1)];
            var normals = new Vector3[vertices.Length];
            var tangents = new Vector4[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[LongitudeSegments * (LatitudeSegments - 1) * 6];
            int triangle = 0;
            for (int lat = 0; lat <= LatitudeSegments; lat++)
            {
                float v = 1f - (float)lat / LatitudeSegments;
                float phi = (v - .5f) * Mathf.PI;
                float sinPhi = Mathf.Sin(phi), cosPhi = Mathf.Cos(phi);
                for (int lon = 0; lon <= LongitudeSegments; lon++)
                {
                    float u = (float)lon / LongitudeSegments;
                    float lambda = (u - .5f) * Mathf.PI * 2f;
                    float sinLambda = Mathf.Sin(lambda), cosLambda = Mathf.Cos(lambda);
                    Vector3 radial = new Vector3(cosPhi * cosLambda, sinPhi, cosPhi * sinLambda);
                    Vector3 east = new Vector3(-sinLambda, 0, cosLambda);
                    float elevationKm = HeightKm(height, u, v);
                    // Conservatively fit every allowed elevation below the existing
                    // collision sphere. No collider radius or route is altered.
                    float referenceScale = 1f - CollisionClearance -
                                           HeightMaximumKm * ReliefMultiplier / ReferenceRadiusKm;
                    float scale = referenceScale + elevationKm * ReliefMultiplier / ReferenceRadiusKm;
                    int index = lat * stride + lon;
                    vertices[index] = radial * (radius * scale);
                    // The full LOLA normal map supplies terrain slopes against
                    // this smooth spherical frame. Baking the same slopes into
                    // vertex normals too would count the relief twice.
                    normals[index] = radial;
                    tangents[index] = new Vector4(east.x, east.y, east.z, -1f);
                    // Each polar wedge uses a midpoint u, avoiding a degenerate UV
                    // triangle; the two longitude seam columns stay independent.
                    float textureU = lat == 0 ? u + .5f / LongitudeSegments :
                                     lat == LatitudeSegments ? u - .5f / LongitudeSegments : u;
                    uv[index] = new Vector2(textureU, v);
                }
            }

            for (int lat = 0; lat < LatitudeSegments; lat++)
                for (int lon = 0; lon < LongitudeSegments; lon++)
                {
                    int a = lat * stride + lon, b = a + 1;
                    int d = a + stride, c = d + 1;
                    if (lat > 0)
                    {
                        triangles[triangle++] = a;
                        triangles[triangle++] = b;
                        triangles[triangle++] = c;
                    }
                    if (lat < LatitudeSegments - 1)
                    {
                        triangles[triangle++] = a;
                        triangles[triangle++] = c;
                        triangles[triangle++] = d;
                    }
                }

            var mesh = new Mesh { name = "Orbital_LRO_LunarSurface" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static float HeightKm(Texture2D height, float u, float v)
        {
            Color sample = height.GetPixelBilinear(Mathf.Repeat(u, 1f), Mathf.Clamp01(v));
            float encoded = sample.r * 65280f + sample.g * 255f;
            return Mathf.Lerp(HeightMinimumKm, HeightMaximumKm, encoded / 65535f);
        }
    }
}
