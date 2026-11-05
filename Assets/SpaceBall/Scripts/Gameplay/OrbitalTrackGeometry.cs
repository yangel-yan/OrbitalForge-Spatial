using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceBall
{
    /// <summary>
    /// Builds visible, collidable rails from local-space ball-centre anchors.
    /// This helper never moves a ball, modifies a velocity, or guides a trajectory.
    /// </summary>
    public static class OrbitalTrackGeometry
    {
        public const float BallRadius = 0.011f;
        public const float FloorThickness = 0.006f;
        public const float GuardThickness = 0.006f;
        public const float GuardHeight = 0.025f;

        const float MinimumSegmentLength = 0.00001f;
        const float SeamOverlap = 0.002f;

        /// <summary>
        /// Rounds interior corners with circular XZ fillets and interpolated Y.
        /// cornerRadius is the desired actual radius. The tangent trim distance
        /// is limited to 45% of either adjacent XZ edge, reducing the effective
        /// radius when space is limited. Endpoints are preserved exactly.
        /// </summary>
        public static Vector3[] SmoothPolyline(Vector3[] points, float cornerRadius = 0.035f,
            int cornerSteps = 8)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (float.IsNaN(cornerRadius) || float.IsInfinity(cornerRadius))
                throw new ArgumentOutOfRangeException(nameof(cornerRadius));

            var clean = new List<Vector3>(points.Length);
            foreach (Vector3 point in points)
            {
                RequireFinite(point, nameof(points));
                AddDistinct(clean, point);
            }

            if (clean.Count < 3 || cornerRadius <= 0f)
                return clean.ToArray();

            var result = new List<Vector3>(clean.Count * Mathf.Clamp(cornerSteps, 2, 32));
            result.Add(clean[0]);

            for (int i = 1; i < clean.Count - 1; i++)
            {
                Vector3 corner = clean[i];
                Vector3 previous = clean[i - 1];
                Vector3 next = clean[i + 1];
                Vector2 incoming = new Vector2(corner.x - previous.x, corner.z - previous.z);
                Vector2 outgoing = new Vector2(next.x - corner.x, next.z - corner.z);
                float incomingLength = incoming.magnitude;
                float outgoingLength = outgoing.magnitude;
                if (incomingLength < MinimumSegmentLength || outgoingLength < MinimumSegmentLength)
                {
                    AddDistinct(result, corner);
                    continue;
                }
                incoming /= incomingLength;
                outgoing /= outgoingLength;

                float directionDot = Mathf.Clamp(Vector2.Dot(incoming, outgoing), -1f, 1f);
                if (directionDot > 0.9995f || directionDot < -0.9995f)
                {
                    // A reversal needs an explicitly laid-out arc, not a
                    // zero-radius fillet. Keep the authored corner visible.
                    AddDistinct(result, corner);
                    continue;
                }

                float angle = Mathf.Acos(directionDot);
                float halfAngleTangent = Mathf.Tan(angle * 0.5f);
                float trim = Mathf.Min(cornerRadius * halfAngleTangent,
                    Mathf.Min(incomingLength, outgoingLength) * 0.45f);
                float effectiveRadius = trim / halfAngleTangent;
                float turnSign = Mathf.Sign(incoming.x * outgoing.y - incoming.y * outgoing.x);
                Vector2 entryXZ = new Vector2(corner.x, corner.z) - incoming * trim;
                Vector2 exitXZ = new Vector2(corner.x, corner.z) + outgoing * trim;
                float entryY = Mathf.Lerp(corner.y, previous.y, trim / incomingLength);
                float exitY = Mathf.Lerp(corner.y, next.y, trim / outgoingLength);
                Vector3 entry = new Vector3(entryXZ.x, entryY, entryXZ.y);
                Vector3 exit = new Vector3(exitXZ.x, exitY, exitXZ.y);
                Vector2 inwardNormal = new Vector2(-incoming.y, incoming.x) * turnSign;
                Vector2 center = entryXZ + inwardNormal * effectiveRadius;
                Vector2 startRadius = entryXZ - center;
                int steps = Mathf.Clamp(Mathf.Max(cornerSteps,
                    Mathf.CeilToInt(angle * Mathf.Rad2Deg / 10f)), 2, 32);
                AddDistinct(result, entry);

                for (int step = 1; step <= steps; step++)
                {
                    float t = step / (float)steps;
                    if (step == steps)
                    {
                        AddDistinct(result, exit);
                        continue;
                    }
                    float rotation = turnSign * angle * t;
                    float cos = Mathf.Cos(rotation);
                    float sin = Mathf.Sin(rotation);
                    Vector2 radial = new Vector2(startRadius.x * cos - startRadius.y * sin,
                        startRadius.x * sin + startRadius.y * cos);
                    Vector2 arcPoint = center + radial;
                    AddDistinct(result, new Vector3(arcPoint.x,
                        Mathf.Lerp(entryY, exitY, t), arcPoint.y));
                }
            }

            AddDistinct(result, clean[clean.Count - 1]);
            return result.ToArray();
        }

        /// <summary>
        /// Uses the supplied polyline as-is. Call SmoothPolyline once beforehand
        /// when rounded corners are desired. Coordinates are local to parent;
        /// local +Y is up. unguardedStart/End remove guards over those path
        /// distances while retaining the entire collidable floor.
        /// </summary>
        public static GameObject BuildTrack(Transform parent, string name, Vector3[] centerline,
            Material floor, Material rails, PhysicsMaterial surface, float width = 0.055f,
            float unguardedStart = 0f, float unguardedEnd = 0f, bool guards = true)
        {
            if (centerline == null)
                throw new ArgumentNullException(nameof(centerline));
            if (centerline.Length < 2)
                throw new ArgumentException("A track needs at least two centreline anchors.", nameof(centerline));
            if (width <= BallRadius * 2f || float.IsNaN(width) || float.IsInfinity(width))
                throw new ArgumentOutOfRangeException(nameof(width), "The clear track width must exceed the ball diameter.");
            if (unguardedStart < 0f || float.IsNaN(unguardedStart) || float.IsInfinity(unguardedStart))
                throw new ArgumentOutOfRangeException(nameof(unguardedStart));
            if (unguardedEnd < 0f || float.IsNaN(unguardedEnd) || float.IsInfinity(unguardedEnd))
                throw new ArgumentOutOfRangeException(nameof(unguardedEnd));

            // Validate before constructing so invalid input cannot leave half a track.
            float totalLength = 0f;
            for (int i = 0; i < centerline.Length; i++)
            {
                RequireFinite(centerline[i], nameof(centerline));
                if (i == 0)
                    continue;
                Vector3 delta = centerline[i] - centerline[i - 1];
                totalLength += delta.magnitude;
                if (delta.sqrMagnitude > MinimumSegmentLength * MinimumSegmentLength &&
                    new Vector2(delta.x, delta.z).sqrMagnitude < MinimumSegmentLength * MinimumSegmentLength)
                    throw new ArgumentException("Use a sloped track for a height change; vertical drops have no supporting floor.", nameof(centerline));
            }

            var track = new GameObject(string.IsNullOrEmpty(name) ? "Track" : name);
            track.transform.SetParent(parent, false);

            float travelled = 0f;
            float guardedStart = Mathf.Min(unguardedStart, totalLength);
            float guardedEnd = Mathf.Max(0f, totalLength - unguardedEnd);
            for (int i = 0; i < centerline.Length - 1; i++)
            {
                Vector3 start = centerline[i];
                Vector3 end = centerline[i + 1];
                Vector3 delta = end - start;
                float length = delta.magnitude;
                if (length < MinimumSegmentLength)
                    continue;

                Vector3 forward = delta / length;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 up = Vector3.Cross(forward, right).normalized;
                Quaternion rotation = Quaternion.LookRotation(forward, up);
                Vector3 middle = (start + end) * 0.5f;
                Vector3 floorTop = middle - Vector3.up * BallRadius;

                MakeBox(track.transform, "Floor_" + i,
                    floorTop - up * (FloorThickness * 0.5f), rotation,
                    new Vector3(width + GuardThickness * 2f, FloorThickness, length + SeamOverlap),
                    floor, surface);

                float guardFrom = Mathf.Max(travelled - SeamOverlap * 0.5f, guardedStart);
                float guardTo = Mathf.Min(travelled + length + SeamOverlap * 0.5f, guardedEnd);
                if (guards && guardTo - guardFrom > MinimumSegmentLength)
                {
                    float guardOffset = width * 0.5f + GuardThickness * 0.5f;
                    Vector3 guardPathMiddle = start + forward * ((guardFrom + guardTo) * 0.5f - travelled);
                    Vector3 guardMiddle = guardPathMiddle - Vector3.up * BallRadius +
                        up * (GuardHeight * 0.5f);
                    Vector3 guardSize = new Vector3(GuardThickness, GuardHeight, guardTo - guardFrom);
                    MakeBox(track.transform, "GuardL_" + i, guardMiddle - right * guardOffset,
                        rotation, guardSize, rails, surface);
                    MakeBox(track.transform, "GuardR_" + i, guardMiddle + right * guardOffset,
                        rotation, guardSize, rails, surface);
                }
                travelled += length;
            }

            return track;
        }

        /// <summary>Builds the same physical floor without side guards.</summary>
        public static GameObject BuildTrackFloorOnly(Transform parent, string name, Vector3[] centerline,
            Material floor, Material rails, PhysicsMaterial surface, float width = 0.055f)
        {
            return BuildTrack(parent, name, centerline, floor, rails, surface, width, guards: false);
        }

        /// <summary>
        /// The contactCenter is the BALL CENTRE at impact, not a point on the
        /// plate's surface. Directions are incoming/outgoing velocity directions
        /// in parent's local space. Restitution/friction are supplied by bounce.
        /// </summary>
        public static GameObject BuildReflector(Transform parent, Vector3 contactCenter,
            Vector3 incomingDirection, Vector3 outgoingDirection, Material material,
            PhysicsMaterial bounce, float ballRadius = BallRadius)
        {
            RequireFinite(contactCenter, nameof(contactCenter));
            RequireFinite(incomingDirection, nameof(incomingDirection));
            RequireFinite(outgoingDirection, nameof(outgoingDirection));
            if (ballRadius <= 0f || float.IsNaN(ballRadius) || float.IsInfinity(ballRadius))
                throw new ArgumentOutOfRangeException(nameof(ballRadius));
            if (incomingDirection.sqrMagnitude < MinimumSegmentLength * MinimumSegmentLength ||
                outgoingDirection.sqrMagnitude < MinimumSegmentLength * MinimumSegmentLength)
                throw new ArgumentException("A reflector requires nonzero incoming and outgoing directions.");

            Vector3 normal = incomingDirection.normalized - outgoingDirection.normalized;
            if (normal.sqrMagnitude < 0.000001f)
                throw new ArgumentException("Identical incoming and outgoing directions do not describe a reflection.");
            normal.Normalize();

            const float thickness = 0.008f;
            Vector3 upReference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.95f
                ? Vector3.forward
                : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(normal, upReference);
            Vector3 plateCenter = contactCenter + normal * (ballRadius + thickness * 0.5f);
            return MakeBox(parent, "Reflector", plateCenter, rotation,
                new Vector3(0.09f, 0.065f, thickness), material, bounce);
        }

        static GameObject MakeBox(Transform parent, string name, Vector3 position,
            Quaternion rotation, Vector3 size, Material material, PhysicsMaterial physicsMaterial)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.transform.localRotation = rotation;
            box.transform.localScale = size;
            if (material != null)
                box.GetComponent<MeshRenderer>().sharedMaterial = material;
            box.GetComponent<BoxCollider>().sharedMaterial = physicsMaterial;
            box.GetComponent<BoxCollider>().contactOffset = .001f;
            return box;
        }

        static void AddDistinct(List<Vector3> points, Vector3 point)
        {
            if (points.Count == 0 || (points[points.Count - 1] - point).sqrMagnitude >
                MinimumSegmentLength * MinimumSegmentLength)
                points.Add(point);
        }

        static void RequireFinite(Vector3 value, string parameterName)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                float.IsNaN(value.z) || float.IsInfinity(value.z))
                throw new ArgumentException("Track positions and directions must be finite.", parameterName);
        }
    }
}
