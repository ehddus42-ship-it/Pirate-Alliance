using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AcRoguelike
{
    /// <summary>Spends a movement's remaining distance along a glancing obstacle without changing the body's collision shape.</summary>
    public sealed class CharacterObstacleSlide
    {
        readonly CharacterController body;
        readonly Predicate<Vector3> allowedPosition;
        RaycastHit[] hits = new RaycastHit[16];

        public CharacterObstacleSlide(CharacterController body, Predicate<Vector3> allowedPosition = null)
        {
            this.body = body;
            this.allowedPosition = allowedPosition;
        }

        public CollisionFlags Move(Vector3 displacement)
        {
            if (!body || !body.enabled || !body.gameObject.activeInHierarchy) return CollisionFlags.None;
            Vector3 start = body.transform.position;
            Vector3 planar = Vector3.ProjectOnPlane(displacement, Vector3.up);
            float distance = planar.magnitude;

            if (distance < .0001f) return body.Move(displacement);
            Vector3 direction = planar / distance;
            if (!TryObstacle(start, direction, distance, out Vector3 normal, out float approach))
                return body.Move(displacement);

            Vector3 tangent = Vector3.ProjectOnPlane(direction, normal);
            // A mostly head-on impact must stop. Only glancing contacts get help along the surface.
            if (tangent.sqrMagnitude < .25f) return body.Move(displacement);
            float remaining = distance - approach;
            Vector3 correction = tangent.normalized * remaining;
            if (allowedPosition != null && !allowedPosition(start + direction * approach + correction))
                return body.Move(displacement);

            // Budget the two requested segments before moving. Measuring only the first Move's
            // endpoint would miss its own bent slide path and could extend the total dash distance.
            CollisionFlags flags = body.Move(direction * approach + (displacement - planar));
            if (allowedPosition != null && !allowedPosition(body.transform.position + correction))
                correction = direction * remaining;
            // Both segments still sweep the full controller, including against a second wall.
            return flags | body.Move(correction);
        }

        bool TryObstacle(Vector3 start, Vector3 direction, float distance, out Vector3 normal, out float approach)
        {
            normal = Vector3.zero;
            approach = distance;
            Vector3 scale = body.transform.lossyScale;
            float widthScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float skin = body.skinWidth * widthScale;
            float radius = Mathf.Max(.01f, body.radius * widthScale - skin);
            float halfHeight = Mathf.Max(radius, body.height * Mathf.Abs(scale.y) * .5f - skin);
            Vector3 center = start + body.transform.TransformVector(body.center);
            Vector3 offset = Vector3.up * (halfHeight - radius);
            var physics = body.gameObject.scene.GetPhysicsScene();
            int count;
            // A full query buffer may omit the nearest collider. Grow rarely, and fail closed if still full.
            while (true)
            {
                count = physics.CapsuleCast(center - offset, center + offset, radius, direction, hits,
                    distance + skin * 4f, ~0, QueryTriggerInteraction.Ignore);
                if (count < hits.Length) break;
                if (hits.Length >= 128) return false;
                hits = new RaycastHit[hits.Length * 2];
            }

            float nearest = float.PositiveInfinity;
            Collider obstacle = null;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                var other = hit.collider;
                if (!other || other == body || other.transform.IsChildOf(body.transform)) continue;
                if (Physics.GetIgnoreLayerCollision(body.gameObject.layer, other.gameObject.layer)
                    || Physics.GetIgnoreCollision(body, other)) continue;
                // Floors and ramps retain the controller's normal stepping and slope handling.
                if (Mathf.Abs(hit.normal.y) > .4f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                obstacle = other;
                normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            }

            // Do not steer around combatants or pushable physics objects as if they were scenery.
            if (!(obstacle && !(obstacle is CharacterController)
                && (!obstacle.attachedRigidbody || obstacle.attachedRigidbody.isKinematic)
                && normal.sqrMagnitude > .5f)) return false;

            float inward = -Vector3.Dot(direction, normal);
            // Very shallow contact already retains over 96% of normal controller speed.
            // Limit inset compensation to the query extension, instead of extrapolating distant hits.
            if (inward < .25f) return false;
            approach = Mathf.Max(0, nearest - skin / inward);
            return approach < distance;
        }
    }
}
