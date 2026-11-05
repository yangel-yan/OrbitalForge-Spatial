using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        static bool UsesConstruction(OrbitalLevel level) => OrbitalConstructionLayout.Supports(level);

        LevelRig BuildConstructionLevel(OrbitalLevel level)
        {
            var layout=OrbitalConstructionLayout.For(level);
            var rig=new LevelRig();
            rig.root=new GameObject(((int)level+1).ToString("00")+" Construction "+level);
            rig.root.transform.SetParent(transform,false);
            Transform board=rig.root.transform;
            board.localRotation=Quaternion.Euler(-22,0,0);
            board.localPosition=new Vector3(0,-.055f,-.025f);
            OrbitalArtGeometry.BuildChassis(board,palette[3],palette[9],palette[10],palette[13]);
            rig.presentation=rig.root.AddComponent<OrbitalPresentation>();
            rig.presentation.offMaterial=palette[6];rig.presentation.onMaterial=palette[7];rig.presentation.amberMaterial=palette[11];

            foreach(var path in layout.FixedPaths) Track(board,"Fixed receiving rail",path,0,0,.068f);
            var kit=rig.root.AddComponent<OrbitalConstructionKit>();
            kit.owner=this;
            kit.tiles=new OrbitalConstructionTile[layout.TileShapes.Length];
            kit.sockets=new OrbitalConstructionSocket[layout.SocketPositions.Length];
            var fixedPorts=new List<Vector3>();
            foreach(var path in layout.FixedPaths){fixedPorts.Add(path[0]);fixedPorts.Add(path[path.Length-1]);}
            kit.fixedPorts=fixedPorts.ToArray();
            for(int i=0;i<kit.sockets.Length;i++)
                kit.sockets[i]=BuildConstructionSocket(board,kit,i,layout.SocketPositions[i]);
            BuildConstructionTray(board,kit);
            for(int i=0;i<kit.tiles.Length;i++)
                kit.tiles[i]=BuildConstructionTile(board,kit,i,layout.TileShapes[i],kit.tiles.Length);
            rig.construction=kit;
            kit.Clear();

            rig.aLight=ConstructionTarget(board,"A",layout.TargetA,out rig.aLamps);
            rig.bLight=ConstructionTarget(board,"B",layout.TargetB,out rig.bLamps);
            Sensor(board,"Target A",layout.TargetA,OrbitalEvent.TargetA,.034f);
            Sensor(board,"Target B",layout.TargetB,OrbitalEvent.TargetB,.034f);
            var rings=new List<Transform>{rig.aLight.transform.parent,rig.bLight.transform.parent,null,null};
            if(OrbitalCampaign.RequiresGate(level))
            {
                Vector3 direction=level==OrbitalLevel.Finale?Vector3.forward:Vector3.back;
                rig.gateBlock=Primitive("Gate G blocking bar",PrimitiveType.Cube,board,layout.Gate+Vector3.up*.015f,
                    new Vector3(.086f,.05f,.009f),palette[2],true);
                rig.gateBlock.transform.localRotation=Quaternion.LookRotation(direction,Vector3.up);
                rig.gateVisual=Primitive("Gate G power indicator",PrimitiveType.Cube,board,layout.Gate+Vector3.up*.065f,
                    new Vector3(.085f,.012f,.012f),palette[2],false);
                Sensor(board,"Gate closed sensor",layout.Gate-direction*.035f,OrbitalEvent.ClosedGate,.019f);
                Sensor(board,"Gate crossed",layout.Gate+direction*.035f,OrbitalEvent.ReturnGate,.019f);
                BuildGateHousing(board,layout.Gate,direction);
                SceneMarker(board,"G",layout.Gate+new Vector3(-.085f,.065f,0),palette[2]);
                Cable(board,layout.TargetB+Vector3.up*.025f,layout.Gate+Vector3.up*.06f,true);
                if(level==OrbitalLevel.Finale) Cable(board,layout.TargetA+Vector3.up*.025f,layout.Gate+Vector3.up*.06f,true);
                rings[2]=rig.gateVisual.transform;
            }
            if(OrbitalCampaign.HasCheckpoint(level))
            {
                rig.cLight=ConstructionTarget(board,"C",layout.Checkpoint,out rig.cLamps);
                Sensor(board,"Checkpoint C",layout.Checkpoint,OrbitalEvent.CheckpointC,.034f);
                rings.Add(rig.cLight.transform.parent);
                SceneMarker(board,"C 外环校验",layout.Checkpoint+new Vector3(-.07f,.078f,0),palette[2],.14f);
                SceneMarker(board,"短接线",new Vector3(-.30f,.145f,.03f),palette[2],.095f);
            }

            var launcher=OrbitalArtGeometry.BuildLauncher(board,layout.Launch,palette[3],palette[8],palette[11]);
            launcher.localRotation=Quaternion.LookRotation(layout.Direction,Vector3.up);
            var launchCollider=launcher.gameObject.AddComponent<BoxCollider>();
            launchCollider.center=new Vector3(-.012f,.025f,0);launchCollider.size=new Vector3(.12f,.10f,.12f);launchCollider.isTrigger=true;
            launcher.gameObject.AddComponent<OrbitalClickTarget>();
            var launchControl=launcher.gameObject.AddComponent<OrbitalControl>();
            launchControl.owner=this;launchControl.kind=OrbitalControlKind.Launch;buildControls.Add(launchControl);
            SceneMarker(board,"发射",layout.Launch+new Vector3(0,.065f,-.065f),palette[2],.074f);

            var station=OrbitalArtGeometry.BuildStation(board,layout.Dock,palette[3],palette[8],palette[6]);
            if(level==OrbitalLevel.Finale)station.localRotation=Quaternion.Euler(0,180,0);
            if(stationVisualPrefab!=null)OrbitalStationVisual.Attach(station,stationVisualPrefab);
            rig.stationLight=Primitive("Station core",PrimitiveType.Sphere,board,layout.Dock+Vector3.up*.03f,
                Vector3.one*.018f,palette[6],false).GetComponent<Renderer>();
            var arrival=Ring(board,"Dock pulse",layout.Dock+Vector3.up*.012f,palette[10]);
            arrival.transform.parent.localRotation=Quaternion.Euler(90,0,0);rings[3]=arrival.transform.parent;
            var stationLamps=new List<Renderer>(arrival.transform.parent.GetComponentsInChildren<Renderer>());
            stationLamps.AddRange(station.Find("Illumination").GetComponentsInChildren<Renderer>());
            rig.presentation.stationLamps=stationLamps.ToArray();
            rig.presentation.pulseRings=rings.ToArray();
            rig.presentation.targetA=rig.aLamps;rig.presentation.targetB=rig.bLamps;
            Primitive("Station dock",PrimitiveType.Cylinder,board,layout.Dock-Vector3.up*.018f,new Vector3(.09f,.01f,.09f),palette[9],false);
            Sensor(board,"Dock S",layout.Dock,OrbitalEvent.Dock,.038f);
            SceneMarker(board,"S · 空间站",layout.Dock+new Vector3(.07f,.115f,-.025f),palette[1],.13f);
            SceneMarker(board,layout.Caption,new Vector3(0,.018f,-.31f),palette[2],.56f);

            var ball=Primitive("Energy ball",PrimitiveType.Sphere,board,layout.Launch,Vector3.one*.022f,palette[11],true);
            rig.ball=ball.AddComponent<Rigidbody>();rig.ball.mass=.045f;rig.ball.useGravity=false;rig.ball.isKinematic=true;
            rig.ball.linearDamping=.025f;rig.ball.angularDamping=.015f;rig.ball.interpolation=RigidbodyInterpolation.Interpolate;
            rig.ball.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            ball.GetComponent<Collider>().sharedMaterial=rollingSurface;
            var shell=new GameObject("Energy shell");shell.transform.SetParent(ball.transform,false);
            shell.AddComponent<MeshFilter>().sharedMesh=ball.GetComponent<MeshFilter>().sharedMesh;
            shell.AddComponent<MeshRenderer>().sharedMaterial=ball.GetComponent<Renderer>().sharedMaterial;
            DestroyImmediate(ball.GetComponent<MeshFilter>());DestroyImmediate(ball.GetComponent<MeshRenderer>());
            rig.presentation.ballVisual=shell.transform;
            OrbitalConstructionVisuals.Build(board,level,palette);
            rig.launchPosition=layout.Launch;rig.launchDirection=layout.Direction;
            return rig;
        }

        Renderer ConstructionTarget(Transform board,string caption,Vector3 point,out Renderer[] lamps)
        {
            Renderer ring=Ring(board,caption,point,palette[6]);
            var list=new List<Renderer>(ring.transform.parent.GetComponentsInChildren<Renderer>());
            var target=OrbitalArtGeometry.BuildTarget(board,point,palette[9],palette[8],palette[6]);
            list.AddRange(target.Find("Illumination").GetComponentsInChildren<Renderer>());lamps=list.ToArray();
            SceneMarker(board,caption,point+Vector3.up*.072f,palette[1]);
            return ring;
        }

        OrbitalConstructionSocket BuildConstructionSocket(Transform board,OrbitalConstructionKit kit,int index,Vector3 point)
        {
            var go=new GameObject("Assembly interface "+(index+1));go.transform.SetParent(board,false);go.transform.localPosition=point;
            var collider=go.AddComponent<BoxCollider>();collider.center=new Vector3(0,-.035f,0);collider.size=new Vector3(.17f,.13f,.17f);collider.isTrigger=true;
            go.AddComponent<OrbitalClickTarget>();
            var socket=go.AddComponent<OrbitalConstructionSocket>();socket.kit=kit;socket.index=index;
            float platform=point.y-.098f;
            foreach(float side in new[]{-1f,1f})
            {
                ConstructionDecoration("Socket frame",PrimitiveType.Cube,go.transform,new Vector3(side*.082f,-.098f,0),new Vector3(.008f,.008f,.17f),palette[9]);
                ConstructionDecoration("Socket cross rail",PrimitiveType.Cube,go.transform,new Vector3(0,-.098f,side*.082f),new Vector3(.17f,.008f,.008f),palette[9]);
                float height=Mathf.Max(.012f,platform-.004f);
                ConstructionDecoration("Socket support",PrimitiveType.Cube,board,new Vector3(point.x+side*.075f,.004f+height*.5f,point.z+.075f),new Vector3(.018f,height,.022f),palette[9]);
                ConstructionDecoration("Socket foot",PrimitiveType.Cube,board,new Vector3(point.x+side*.075f,.002f,point.z+.075f),new Vector3(.040f,.006f,.045f),palette[3]);
            }
            foreach(float x in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
            {
                ConstructionDecoration("Interface height guide",PrimitiveType.Cube,go.transform,
                    new Vector3(x*.075f,-.061f,z*.075f),new Vector3(.008f,.066f,.008f),palette[9]);
                ConstructionDecoration("Interface ceramic seat",PrimitiveType.Cube,go.transform,
                    new Vector3(x*.075f,-.024f,z*.075f),new Vector3(.018f,.008f,.018f),palette[8]);
            }
            var ring=OrbitalArtGeometry.Ring(go.transform,"SelectionHalo",new Vector3(0,-.089f,0),.077f,.003f,palette[1],Quaternion.Euler(90,0,0),32,6);
            socket.selectionHalo=ring;ring.gameObject.SetActive(false);
            var rejected=OrbitalArtGeometry.Ring(go.transform,"RejectCue",new Vector3(0,-.083f,0),.081f,.004f,palette[2],Quaternion.Euler(90,0,0),32,6);
            rejected.gameObject.SetActive(false);
            SceneMarker(go.transform,(index+1).ToString("00"),new Vector3(-.105f,-.065f,-.105f),palette[9],.045f);
            return socket;
        }

        void BuildConstructionTray(Transform board,OrbitalConstructionKit kit)
        {
            float width=kit.tiles.Length*.18f+.055f;
            var tray=ConstructionDecoration("Track module tray",PrimitiveType.Cube,board,new Vector3(0,.015f,-.43f),new Vector3(width,.025f,.225f),palette[3]);
            var collider=tray.AddComponent<BoxCollider>();collider.isTrigger=true;
            kit.trayTarget=tray.AddComponent<OrbitalClickTarget>();
            ConstructionDecoration("Tray front ceramic",PrimitiveType.Cube,board,new Vector3(0,.025f,-.545f),new Vector3(width,.018f,.012f),palette[8]);
            SceneMarker(board,"轨段托盘 · 点空白归还",new Vector3(0,.053f,-.55f),palette[9],.39f);
        }

        OrbitalConstructionTile BuildConstructionTile(Transform board,OrbitalConstructionKit kit,int index,OrbitalTileShape shape,int count)
        {
            var go=new GameObject("Track module "+(index+1)+" "+shape);go.transform.SetParent(board,false);
            var tile=go.AddComponent<OrbitalConstructionTile>();tile.kit=kit;tile.index=index;tile.shape=shape;
            tile.homePosition=new Vector3((index-(count-1)*.5f)*.18f,shape==OrbitalTileShape.Drop?.115f:.047f,-.43f);
            Vector3 end=shape==OrbitalTileShape.CornerLeft?new Vector3(0,-.01f,.09f):
                shape==OrbitalTileShape.CornerRight?new Vector3(0,-.01f,-.09f):
                shape==OrbitalTileShape.Drop?new Vector3(.09f,-.07f,0):new Vector3(.09f,-.01f,0);
            tile.inputPort=new Vector3(-.09f,.01f,0);tile.outputPort=end;
            Vector3[] path=shape==OrbitalTileShape.CornerLeft||shape==OrbitalTileShape.CornerRight?
                new[]{tile.inputPort,Vector3.zero,end}:shape==OrbitalTileShape.Drop?
                new[]{tile.inputPort,new Vector3(-.04f,.004f,0),new Vector3(.04f,-.064f,0),end}:
                new[]{tile.inputPort,end};
            var track=Track(go.transform,"Movable physical rail",path,0,0,.068f);
            tile.trackColliders=track.GetComponentsInChildren<Collider>(true);
            foreach(var physical in tile.trackColliders)physical.gameObject.layer=2;
            var selection=go.AddComponent<BoxCollider>();selection.isTrigger=true;
            selection.center=new Vector3(0,-.01f,0);selection.size=new Vector3(.18f,.11f,.18f);
            var halo=OrbitalArtGeometry.Ring(go.transform,"SelectionHalo",new Vector3(0,-.035f,0),.112f,.003f,palette[1],Quaternion.Euler(90,0,0),32,6);
            tile.selectionHalo=halo;halo.gameObject.SetActive(false);
            var input=OrbitalArtGeometry.Ring(go.transform,"High entry port",tile.inputPort+Vector3.up*.002f,.020f,.0025f,palette[2],Quaternion.Euler(90,0,0),24,6);
            var output=OrbitalArtGeometry.Ring(go.transform,"Low exit port",tile.outputPort+Vector3.up*.002f,.020f,.0025f,palette[2],Quaternion.Euler(90,0,0),24,6);
            tile.inputIndicator=input.GetComponent<Renderer>();tile.outputIndicator=output.GetComponent<Renderer>();
            tile.connectedMaterial=palette[7];tile.disconnectedMaterial=palette[2];
            tile.inputIdleMaterial=palette[1];tile.outputIdleMaterial=palette[2];
            string caption=shape==OrbitalTileShape.Straight?"直轨":shape==OrbitalTileShape.Drop?"降轨":shape==OrbitalTileShape.CornerLeft?"左弯":"右弯";
            tile.labelRoot=SceneMarker(go.transform,caption,new Vector3(0,.048f,-.062f),palette[9],.070f);
            tile.ResetToTray();
            return tile;
        }

        GameObject ConstructionDecoration(string name,PrimitiveType type,Transform parent,Vector3 point,Vector3 size,Material material)
        {
            var go=Primitive(name,type,parent,point,size,material,false);
            go.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            return go;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>QA convenience: place the authored solution into the already selected mission. No flight/state shortcut.</summary>
        public bool ApplyConstructionReferenceSolution(OrbitalLevel level)
        {
            if(State.Level!=level||Construction==null||!OrbitalConstructionLayout.Supports(level)||!AssemblyEditable)return false;
            Construction.Clear();
            foreach(var placement in OrbitalConstructionLayout.For(level).ReferenceSolution)
                if(!Construction.Place(placement.Tile,placement.Socket,placement.QuarterTurns))return false;
            Physics.SyncTransforms();
            return true;
        }
#endif
    }
}
