using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        // Reflection in board space preserves the tested contact geometry and
        // gravity while rebuilding meshes normally, so labels never mirror.
        static Vector3 CampaignPoint(OrbitalLevel level, Vector3 point)
        {
            if (level == OrbitalLevel.Crossing || level == OrbitalLevel.Bridge || level == OrbitalLevel.Finale)
                point.x = -point.x;
            if (level == OrbitalLevel.Reverse || level == OrbitalLevel.Finale)
                point.z = -point.z;
            return point;
        }

        static float CampaignHandedness(OrbitalLevel level) =>
            level == OrbitalLevel.Crossing || level == OrbitalLevel.Reverse || level == OrbitalLevel.Bridge ? -1f : 1f;

        void LowerRouteMarker(Transform parent,string caption,Vector3 anchor,Vector3 plaquePosition,Material accent)
        {
            // A low-level target can sit behind the high deck. Move only its
            // letter plaque outward; a short neutral leader ends on the real part.
            // Gold power cables remain visually distinct from these grey-white labels.
            SceneMarker(parent,caption,plaquePosition,accent,.054f);
            Vector3 elbow=new Vector3(plaquePosition.x,anchor.y,plaquePosition.z+.022f);
            LowerRouteLeader(parent,caption+" target leader",anchor,elbow);
            LowerRouteLeader(parent,caption+" plaque leader",elbow,plaquePosition);
            var endpoint=Primitive(caption+" label anchor",PrimitiveType.Sphere,parent,anchor,
                Vector3.one*.008f,accent,false);
            endpoint.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void LowerRouteLeader(Transform parent,string name,Vector3 from,Vector3 to)
        {
            Vector3 delta=to-from;
            if(delta.sqrMagnitude<.000001f)return;
            var segment=Primitive(name,PrimitiveType.Cylinder,parent,(from+to)*.5f,
                new Vector3(.003f,delta.magnitude*.5f,.003f),palette[8],false);
            segment.transform.localRotation=Quaternion.FromToRotation(Vector3.up,delta);
            segment.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildCampaignLandmarks(Transform parent, OrbitalLevel level)
        {
            if (level == OrbitalLevel.Tutorial || level == OrbitalLevel.Moon) return;
            var root=new GameObject("Mission landmark " + level).transform;
            root.SetParent(parent,false);
            Material body=palette[3], ceramic=palette[8], metal=palette[9], light=palette[10];
            if (level == OrbitalLevel.Crossing)
            {
                // Two separated decks at the crossover explain depth without
                // suggesting a junction or adding a clickable target.
                Primitive("Crossing ground plinth",PrimitiveType.Cylinder,root,new Vector3(0,.014f,0),new Vector3(.125f,.008f,.125f),metal,false);
                Primitive("Traffic beacon",PrimitiveType.Cylinder,root,new Vector3(0,.056f,0),new Vector3(.027f,.043f,.027f),body,false);
                OrbitalArtGeometry.Ring(root,"Upper deck signal",new Vector3(0,.107f,0),.029f,.003f,light,Quaternion.Euler(90,0,0),32,6);
                OrbitalArtGeometry.Ring(root,"Lower deck signal",new Vector3(0,.042f,0),.041f,.003f,palette[2],Quaternion.Euler(90,0,0),32,6);
                SceneMarker(parent,"上层",new Vector3(.18f,.36f,.20f),light,.074f);
                SceneMarker(parent,"下层",new Vector3(-.18f,.235f,.08f),palette[2],.074f);
            }
            else if (level == OrbitalLevel.Reverse)
            {
                foreach(float side in new[]{-1f,1f})
                {
                    var mast=Primitive("Relay antenna",PrimitiveType.Cylinder,root,new Vector3(-.32f,.065f,side*.05f),new Vector3(.014f,.055f,.014f),metal,false);
                    Primitive("Relay receiver",PrimitiveType.Cube,root,mast.transform.localPosition+Vector3.up*.06f,new Vector3(.052f,.035f,.008f),ceramic,false);
                }
                SceneMarker(parent,"B → A",new Vector3(-.32f,.17f,0),palette[2],.09f);
            }
            else if (level == OrbitalLevel.Bridge)
            {
                Primitive("Power bridge footing",PrimitiveType.Cube,root,new Vector3(0,.022f,0),new Vector3(.14f,.014f,.12f),metal,false);
                Primitive("Power bridge core",PrimitiveType.Cube,root,new Vector3(0,.062f,0),new Vector3(.07f,.066f,.08f),ceramic,false);
                foreach(float side in new[]{-1f,1f})
                {
                    Primitive("Power bus",PrimitiveType.Cube,root,new Vector3(side*.045f,.071f,0),new Vector3(.009f,.071f,.095f),body,false);
                    Primitive("Power cell window",PrimitiveType.Cube,root,new Vector3(side*.038f,.064f,-.042f),new Vector3(.023f,.035f,.004f),light,false);
                }
                SceneMarker(parent,"B 供电",new Vector3(0,.14f,-.04f),palette[2],.09f);
            }
            else
            {
                Primitive("Navigation core base",PrimitiveType.Cylinder,root,new Vector3(0,.025f,0),new Vector3(.14f,.015f,.14f),body,false);
                OrbitalArtGeometry.Ring(root,"Navigation crown",new Vector3(0,.049f,0),.057f,.006f,ceramic,Quaternion.Euler(90,0,0),40,6);
                Primitive("Navigation core",PrimitiveType.Sphere,root,new Vector3(0,.065f,0),new Vector3(.06f,.06f,.06f),light,false);
                SceneMarker(parent,"双中继",new Vector3(0,.145f,-.045f),palette[2],.09f);
            }
        }
    }
}
