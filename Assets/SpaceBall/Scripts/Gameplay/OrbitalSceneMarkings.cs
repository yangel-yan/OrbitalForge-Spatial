using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        // Small physical labels replace free-floating paragraphs. They have no
        // colliders and never participate in native target selection.
        Transform SceneMarker(Transform parent,string caption,Vector3 at,Material accent,float width=.046f)
        {
            var root=new GameObject("Mark "+caption).transform;
            root.SetParent(parent,false);
            root.localPosition=at;
            root.localRotation=Quaternion.Euler(22,0,0);
            var plate=Primitive("Marker enamel",PrimitiveType.Cube,root,Vector3.zero,
                new Vector3(width,.029f,.006f),palette[3],false);
            Primitive("Marker index",PrimitiveType.Cube,root,new Vector3(-width*.5f+.002f,0,-.004f),
                new Vector3(.003f,.024f,.002f),accent,false);
            var label=Label(root,caption,new Vector3(.002f,0,-.0045f),width-.009f,.023f);
            label.color=new Color(.91f,.96f,.96f);
            foreach(var renderer in root.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        void BuildGateHousing(Transform parent,Vector3 point,Vector3 tangent)
        {
            var root=new GameObject("Return gate housing").transform;
            root.SetParent(parent,false);root.localPosition=point;
            root.localRotation=Quaternion.LookRotation(tangent,Vector3.up);
            foreach(float side in new[]{-1f,1f})
            {
                Primitive("Gate foot",PrimitiveType.Cube,root,new Vector3(side*.055f,-.021f,0),
                    new Vector3(.025f,.009f,.034f),palette[3],false);
                Primitive("Gate ceramic cheek",PrimitiveType.Cube,root,new Vector3(side*.052f,.015f,0),
                    new Vector3(.009f,.064f,.016f),palette[8],false);
                Primitive("Gate inset",PrimitiveType.Cube,root,new Vector3(side*.058f,.019f,-.008f),
                    new Vector3(.004f,.034f,.003f),palette[9],false);
            }
            Primitive("Gate bridge",PrimitiveType.Cube,root,new Vector3(0,.052f,0),
                new Vector3(.113f,.011f,.020f),palette[8],false);
        }
    }
}
