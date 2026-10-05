using System.Collections.Generic;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>Small work props carried by the animated hand. Existing clipboard users keep their hand free of a second prop.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class LobbyHandProp : MonoBehaviour
    {
        public enum Kind { Phone, TakeawayCup }

        LobbyNpc npc;
        Transform hand, forearm, content;
        string clipLabel;
        Kind kind;
        float side;

        public Kind PropKind => kind;
        public bool Visible => content && content.gameObject.activeSelf;

        public static LobbyHandProp Attach(LobbyNpc npc, Kind kind, bool leftHand, List<Object> owned)
        {
            if (!npc || npc.transform.Find(leftHand ? "Clipboard_L" : "Clipboard_R")) return null;
            string label = kind == Kind.Phone ? "phone" : "drink";
            if (!npc.Has(label)) return null;
            var hand = npc.Bone(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, leftHand ? "LeftHand" : "RightHand");
            var forearm = npc.Bone(leftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, leftHand ? "LeftForeArm" : "RightForeArm");
            if (!hand || !forearm) return null;
            var holder = new GameObject(kind + (leftHand ? "_L" : "_R"));
            holder.transform.SetParent(npc.transform, false);
            var prop = holder.AddComponent<LobbyHandProp>();
            prop.npc = npc; prop.hand = hand; prop.forearm = forearm; prop.kind = kind;
            prop.clipLabel = label; prop.side = leftHand ? -1 : 1;
            prop.content = new GameObject("Prop").transform;
            prop.content.SetParent(holder.transform, false);
            prop.content.gameObject.SetActive(false);

            Material Material(Color color, float smoothness = .3f)
            {
                var result = LiminalMonsterKit.Lit(color, smoothness);
                owned?.Add(result);
                return result;
            }

            if (kind == Kind.Phone)
            {
                var shell = Material(new Color(.075f, .10f, .14f), .55f);
                var screen = Material(new Color(.10f, .64f, .67f), .7f);
                var frame = Material(new Color(.56f, .62f, .66f), .65f);
                Part(prop.content, PrimitiveType.Cube, Vector3.zero, new Vector3(.075f, .15f, .014f), shell);
                Part(prop.content, PrimitiveType.Cube, new Vector3(0, .004f, .008f), new Vector3(.063f, .121f, .002f), screen);
                Part(prop.content, PrimitiveType.Cube, new Vector3(0, .064f, .01f), new Vector3(.018f, .003f, .002f), frame);
            }
            else
            {
                var cup = Material(new Color(.90f, .80f, .62f));
                var lid = Material(new Color(.16f, .22f, .24f));
                var band = Material(new Color(.14f, .47f, .44f));
                Part(prop.content, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.082f, .063f, .082f), cup);
                Part(prop.content, PrimitiveType.Cylinder, Vector3.zero, new Vector3(.084f, .022f, .084f), band);
                Part(prop.content, PrimitiveType.Cylinder, new Vector3(0, .066f, 0), new Vector3(.091f, .007f, .091f), lid);
                Part(prop.content, PrimitiveType.Cube, new Vector3(0, .074f, .024f), new Vector3(.024f, .007f, .015f), lid);
            }
            return prop;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var collider = go.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
        }

        void LateUpdate()
        {
            if (!npc || !hand || !forearm || !content) return;
            bool visible = npc.isActiveAndEnabled && npc.ClipWeight(clipLabel) > .2f;
            content.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 arm = hand.position - forearm.position;
            if (arm.sqrMagnitude < .00001f) arm = npc.transform.forward;
            arm.Normalize();
            if (kind == Kind.Phone)
            {
                Vector3 up = Vector3.Slerp(Vector3.up, arm, .35f).normalized;
                Vector3 face = Vector3.ProjectOnPlane(-npc.transform.right * side, up).normalized;
                if (face.sqrMagnitude < .01f) face = npc.transform.forward;
                transform.SetPositionAndRotation(hand.position + arm * .025f, Quaternion.LookRotation(face, up));
            }
            else
            {
                // Tilt only as the raised hand reaches the face; the cup stays upright during the lowered hold.
                float raised = Mathf.InverseLerp(npc.ModelHeight * .62f, npc.ModelHeight * .88f, hand.position.y - npc.transform.position.y);
                Vector3 up = Vector3.Slerp(Vector3.up, -npc.transform.forward, raised * .45f).normalized;
                Vector3 forward = Vector3.ProjectOnPlane(npc.transform.forward, up).normalized;
                transform.SetPositionAndRotation(hand.position + npc.transform.forward * .035f + Vector3.up * .025f,
                    Quaternion.LookRotation(forward, up));
            }
        }

        void OnDisable() { if (content) content.gameObject.SetActive(false); }
    }
}
