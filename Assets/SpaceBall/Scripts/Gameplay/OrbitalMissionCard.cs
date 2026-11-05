using System;
using TMPro;
using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        [Serializable]
        sealed class MissionNode
        {
            public Transform root;
            public Renderer rim, core;
            public TextMeshPro symbol, caption;
        }

        [Serializable]
        sealed class MissionMedal
        {
            public Renderer face;
            public Renderer[] glyph;
            public TextMeshPro caption;
        }

        [Serializable]
        sealed class MissionDiagram
        {
            public Transform root;
            public MissionNode[] nodes;
            public Transform[] links;
        }

        [SerializeField] Transform missionRoute, missionMedals;
        [SerializeField] Transform[] missionLinks;
        [SerializeField] MissionNode[] missionNodes;
        [SerializeField] MissionDiagram[] missionDiagrams;
        [SerializeField] MissionMedal[] missionAwards;
        [SerializeField] TextMeshPro missionStep, missionTelemetry;
        string[] missionSequence;
        int missionSequenceLevel = -1;

        void BuildMissionCard()
        {
            var panel = new GameObject("Mission console").transform;
            panel.SetParent(transform, false);
            // All actions remain above the Moon tracks. The tallest edge is y=.724.
            panel.localPosition = Vector3.up * .055f;
            MissionPlate(panel, "Mission ceramic rim", new Vector3(-.08f, .49f, .397f), .836f, .228f, .010f, .010f, palette[9]);
            MissionPlate(panel, "Mission inset", new Vector3(-.08f, .49f, .392f), .831f, .223f, .0085f, .006f, palette[3]);
            MissionBox(panel, "Signal seam", new Vector3(-.433f, .579f, .381f), new Vector3(.08f, .003f, .003f), palette[1]);
            MissionBox(panel, "Header seam", new Vector3(-.08f, .526f, .382f), new Vector3(.747f, .0015f, .002f), palette[5]);
            MissionPlate(panel, "Flight selector shell", new Vector3(.49f, .49f, .397f), .29f, .228f, .010f, .010f, palette[9]);
            MissionPlate(panel, "Flight selector inset", new Vector3(.49f, .49f, .392f), .285f, .223f, .0085f, .006f, palette[3]);

            journeyText = MissionCopy(panel, "ORBITAL FORGE", new Vector3(-.24f, .585f, .378f), .37f, .024f, 2f, TextAlignmentOptions.Left);
            // Ellipsis on this short decorative line produced an all-zero TMP
            // mesh in the native export. All three fixed headers fit at this size.
            journeyText.enableAutoSizing = false;
            journeyText.fontSize = .1f;
            journeyText.overflowMode = TextOverflowModes.Overflow;
            journeyText.color = new Color(.48f, .73f, .80f);
            titleText = MissionCopy(panel, "点亮中继", new Vector3(-.20f, .550f, .378f), .51f, .044f, 5.4f, TextAlignmentOptions.Left);
            statusText = MissionCopy(panel, "拼装", new Vector3(.225f, .548f, .377f), .13f, .022f, 2.5f);
            statusText.color = new Color(.48f, .87f, .86f);
            goalText = MissionCopy(panel, "本航程成就", new Vector3(-.27f, .511f, .377f), .37f, .021f, 2.5f, TextAlignmentOptions.Left);
            goalText.color = new Color(.56f, .72f, .78f);
            missionStep = MissionCopy(panel, "01", new Vector3(-.437f, .408f, .375f), .045f, .032f, 3.6f);
            missionStep.color = new Color(.99f, .71f, .43f);
            feedbackText = MissionCopy(panel, "点托盘中的 D 道岔，再点 D 接口", new Vector3(-.055f, .408f, .375f), .665f, .040f, 3.4f, TextAlignmentOptions.Left);
            MissionBox(panel, "Step divider", new Vector3(-.395f, .408f, .378f), new Vector3(.002f, .032f, .003f), palette[2]);

            MissionCopy(panel, "归航计划 · 六段航程", new Vector3(.49f, .578f, .378f), .25f, .024f, 3f);
            for (int i = 0; i < OrbitalCampaign.Count; i++)
                MissionControl(panel, (i + 1).ToString("00") + " " + (i == 0 ? "中继" : "待解锁"),
                    new Vector3(.419f + (i % 2) * .142f, .542f - (i / 2) * .048f, .375f), .126f, OrbitalControlKind.Level, i);
            missionTelemetry = MissionCopy(panel, "试跑 00", new Vector3(.414f, .399f, .378f), .12f, .022f, 2.9f);
            missionTelemetry.color = new Color(.55f, .75f, .79f);

            // Native previews have shown incomplete node/link layouts after dynamic
            // repositioning. Prebuild fixed alternatives as a compatibility measure;
            // this does not establish the SDK's underlying synchronisation cause.
            missionDiagrams = new MissionDiagram[4];
            for (int i = 0; i < missionDiagrams.Length; i++)
                missionDiagrams[i] = BuildMissionDiagram(panel, i + 3);
            SelectMissionDiagram(3, true);
            missionMedals = new GameObject("Mission achievement medals").transform;
            missionMedals.SetParent(panel, false);
            missionAwards = new MissionMedal[3];
            for (int i = 0; i < 3; i++) missionAwards[i] = BuildMissionMedal(missionMedals, i);
            if (missionMedals.gameObject.activeSelf) missionMedals.gameObject.SetActive(false);

            // One action row. Flight-specific actions disappear when they have no meaning.
            MissionControl(panel, "路线提示", new Vector3(-.42f, .335f, .375f), .14f, OrbitalControlKind.Hint);
            MissionControl(panel, "再试一次", new Vector3(-.257f, .335f, .375f), .14f, OrbitalControlKind.Reset);
            MissionControl(panel, "暂停", new Vector3(-.094f, .335f, .375f), .14f, OrbitalControlKind.Pause);
            MissionControl(panel, "回看航迹", new Vector3(.069f, .335f, .375f), .14f, OrbitalControlKind.Replay);
            MissionControl(panel, "下一航程 →", new Vector3(.26f, .335f, .375f), .19f, OrbitalControlKind.Continue);
            MissionControl(panel, "偏好", new Vector3(.565f, .399f, .375f), .126f, OrbitalControlKind.Settings);
            // Preferences occupy the route inset, rather than adding another hanging row.
            MissionControl(panel, "声音 开", new Vector3(-.325f, .475f, .365f), .19f, OrbitalControlKind.Audio);
            MissionControl(panel, "舒适 动态", new Vector3(-.085f, .475f, .365f), .19f, OrbitalControlKind.Comfort);
            MissionControl(panel, "航迹 开", new Vector3(.155f, .475f, .365f), .19f, OrbitalControlKind.Trace);
        }

        void RefreshMissionCard(OrbitalProgress.Record record, bool success, bool flight)
        {
            bool overview = chapterOverview && success;
            SetCopy(journeyText, IsReviewSession ? "DEVELOPMENT REVIEW" : overview ? "EXPEDITION COMPLETE" : success ? "MISSION COMPLETE" : "ORBITAL FORGE");
            SetCopy(titleText, overview ? "归航计划 · 首章完成" : OrbitalCampaign.Title(State.Level));
            SetCopy(goalText, "本机偏好");
            if (goalText.gameObject.activeSelf != preferencesOpen) goalText.gameObject.SetActive(preferencesOpen);
            SetCopy(statusText, preferencesOpen ? "偏好" : overview ? Profile.TotalBadges + "/18 奖章" : replaying ? "半速回看" : success ? "交付完成" :
                State.Phase == OrbitalPhase.Running ? "航行中" : State.Phase == OrbitalPhase.Paused ? "已暂停" :
                State.Phase == OrbitalPhase.Failure ? "调整航路" : Construction != null ? "接轨 " + Construction.PlacedCount : Assembly != null && Assembly.Ready ? "待发射" : "拼装");
            if (missionStep != null)
                SetCopy(missionStep, preferencesOpen ? "•" : success ? "✓" : replaying ? "↶" : flight ? "→" :
                    State.Phase == OrbitalPhase.Failure ? "↻" : Construction != null ? "+" : Assembly != null && !Assembly.pieces[0].Installed ? "01" :
                    Assembly != null && !Assembly.pieces[1].Installed ? "02" : "03");
            if (missionTelemetry != null)
                SetCopy(missionTelemetry, success || record.completed ? "最佳 " + record.bestAttempts + " 发" : "试跑 " + attempts.ToString("00"));
            if (missionMedals != null)
            {
                bool medalsVisible = success && !overview && !preferencesOpen;
                if (missionMedals.gameObject.activeSelf != medalsVisible) missionMedals.gameObject.SetActive(medalsVisible);
            }
            if (missionSequenceLevel != (int)State.Level || missionSequence == null)
            { missionSequence = OrbitalCampaign.Nodes(State.Level); missionSequenceLevel = (int)State.Level; }
            int nodeCount = overview ? OrbitalCampaign.Count : missionSequence.Length;
            SelectMissionDiagram(nodeCount, (!success || overview) && !preferencesOpen);
            if (missionNodes != null)
            {
                for (int i = 0; i < missionNodes.Length; i++)
                {
                    string symbol = overview ? (i + 1).ToString("00") : missionSequence[i];
                    SetCopy(missionNodes[i].symbol, symbol);
                    SetCopy(missionNodes[i].caption, overview ? OrbitalCampaign.ShortTitle((OrbitalLevel)i) :
                        symbol == "S" ? "终点" : symbol == "G" ? "供能门" : symbol == "C" ? "校验点" : "中继");
                    bool reached = overview ? Profile.For((OrbitalLevel)i).completed : symbol == "A" ? DisplayA :
                        symbol == "B" ? DisplayB : symbol == "G" ? DisplayGateCrossed : symbol == "C" ? DisplayC : DisplayDelivered;
                    SetMissionMaterial(missionNodes[i].rim, reached ? palette[1] : palette[5]);
                    SetMissionMaterial(missionNodes[i].core, reached ? palette[1] : palette[3]);
                    missionNodes[i].symbol.color = reached ? new Color(.025f, .09f, .12f) : new Color(.72f, .83f, .87f);
                }
            }
            if (missionAwards != null)
                for (int i = 0; i < missionAwards.Length; i++)
                {
                    bool earned = i == 0 ? record.completed : i == 1 ? record.independent : record.efficient;
                    SetMissionMaterial(missionAwards[i].face, earned ? palette[2] : palette[5]);
                    foreach (var renderer in missionAwards[i].glyph) SetMissionMaterial(renderer, earned ? palette[3] : palette[6]);
                    missionAwards[i].caption.color = earned ? new Color(1, .83f, .59f) : new Color(.43f, .56f, .62f);
                }
        }

        MissionDiagram BuildMissionDiagram(Transform panel, int count)
        {
            var diagram = new MissionDiagram
            {
                // Keep a normal-sized solid behind the task panel. Its offset is
                // baked into vertices: the container transform remains zero/one,
                // preserving every child layout without sub-millimetre geometry.
                root = MissionPlate(panel, "Mission route diagram " + count, Vector3.zero,
                    .020f, .012f, .002f, .008f, palette[3], new Vector3(-.43f, .57f, .405f)).transform,
                nodes = new MissionNode[count],
                links = new Transform[count - 1]
            };
            diagram.root.SetParent(panel, false);
            float spacing = .60f / (count - 1);
            string[] initialSymbols = count == 3 ? new[] { "A", "B", "S" } :
                count == 4 ? new[] { "A", "G", "B", "S" } :
                count == 5 ? new[] { "A", "B", "G", "C", "S" } :
                new[] { "01", "02", "03", "04", "05", "06" };
            for (int i = 0; i < count; i++)
            {
                // Node roots keep their real octagonal mesh. Their poses and the
                // static diagram container are never moved during a mission refresh.
                var rim = MissionDisc(diagram.root, "Route node " + i,
                    new Vector3(-.35f + i * spacing, .476f, .375f), .023f, .005f, 8, palette[5]);
                var core = MissionDisc(rim.transform, "Status core", new Vector3(0, 0, -.004f), .019f, .003f, 8, palette[3]);
                string symbolText = initialSymbols[i];
                var symbol = MissionCopy(rim.transform, symbolText, new Vector3(0, 0, -.008f), .035f, .028f, 3.4f);
                var caption = MissionCopy(rim.transform, count == 6 ? OrbitalCampaign.ShortTitle((OrbitalLevel)i) :
                    symbolText == "S" ? "终点" : symbolText == "G" ? "供能门" : symbolText == "C" ? "校验点" : "中继",
                    new Vector3(0, -.033f, -.002f), .10f, .021f, 2.6f);
                diagram.nodes[i] = new MissionNode { root = rim.transform, rim = rim, core = core, symbol = symbol, caption = caption };
            }
            for (int i = 0; i < diagram.links.Length; i++)
            {
                // The link root owns the actual connector, rather than a tiny
                // placeholder plus a second child mesh. The chevron remains a child.
                var link = MissionPolygon(diagram.root, "Flight link " + i, Vector3.zero, new[]
                {
                    new Vector2(-.0535f, -.001f), new Vector2(.0535f, -.001f),
                    new Vector2(.0535f, .001f), new Vector2(-.0535f, .001f)
                }, .002f, palette[5]).transform;
                link.localPosition = new Vector3(-.35f + (i + .5f) * spacing, .476f, .378f);
                link.localScale = new Vector3(Mathf.Max(.45f, (spacing - .055f) / .107f), 1, 1);
                diagram.links[i] = link;
                var arrow = MissionPlate(link, "Flight chevron", new Vector3(.022f, 0, -.003f), .008f, .008f, .001f, .003f, palette[1]);
                arrow.transform.localRotation = Quaternion.Euler(0, 0, 45);
            }
            if (diagram.root.gameObject.activeSelf) diagram.root.gameObject.SetActive(false);
            return diagram;
        }

        void SelectMissionDiagram(int count, bool visible)
        {
            if (missionDiagrams == null) return;
            foreach (var diagram in missionDiagrams)
            {
                if (diagram == null || diagram.root == null) continue;
                bool selected = diagram.nodes.Length == count;
                bool active = selected && visible;
                if (diagram.root.gameObject.activeSelf != active) diagram.root.gameObject.SetActive(active);
                if (!selected) continue;
                // Preserve the existing inspection contract for the current group.
                missionRoute = diagram.root;
                missionNodes = diagram.nodes;
                missionLinks = diagram.links;
            }
        }

        MissionMedal BuildMissionMedal(Transform parent, int index)
        {
            var root = new GameObject("Achievement " + index).transform;
            root.SetParent(parent, false);
            root.localPosition = new Vector3(-.325f + index * .24f, .475f, .372f);
            MissionDisc(root, "Ceramic medal rim", Vector3.zero, .034f, .006f, 8, palette[8]);
            var face = MissionDisc(root, "Medal face", new Vector3(0, 0, -.005f), .029f, .004f, 8, palette[2]);
            // Each achievement has a distinct physical emblem, independent of its text.
            if (index == 0)
            {
                MissionBox(root, "Dock station", new Vector3(0, 0, -.009f), new Vector3(.014f, .023f, .003f), palette[3]);
                MissionBox(root, "Left solar wing", new Vector3(-.017f, 0, -.009f), new Vector3(.013f, .012f, .003f), palette[3]);
                MissionBox(root, "Right solar wing", new Vector3(.017f, 0, -.009f), new Vector3(.013f, .012f, .003f), palette[3]);
            }
            else if (index == 1)
            {
                var compass = MissionPlate(root, "Compass rose", new Vector3(0, 0, -.009f), .027f, .027f, .004f, .003f, palette[3]);
                compass.transform.localRotation = Quaternion.Euler(0, 0, 45);
                MissionDisc(root, "Compass centre", new Vector3(0, 0, -.012f), .006f, .002f, 8, palette[2]);
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    var stroke = MissionBox(root, "Efficient chevron", new Vector3(-.008f + i * .016f, .005f, -.009f), new Vector3(.006f, .024f, .003f), palette[3]);
                    stroke.transform.localRotation = Quaternion.Euler(0, 0, -28);
                    var lower = MissionBox(root, "Efficient chevron return", new Vector3(-.008f + i * .016f, -.009f, -.009f), new Vector3(.006f, .014f, .003f), palette[3]);
                    lower.transform.localRotation = Quaternion.Euler(0, 0, 28);
                }
            }
            var caption = MissionCopy(root, index == 0 ? "能源交付" : index == 1 ? "独立解题" : "两发内交付", new Vector3(0, -.043f, -.005f), .18f, .020f, 2.3f);
            var all = root.GetComponentsInChildren<Renderer>();
            var glyph = new System.Collections.Generic.List<Renderer>();
            foreach (var renderer in all)
                if (renderer != face && renderer.name != "Ceramic medal rim" && renderer.GetComponent<TMP_Text>() == null && renderer.name != "Compass centre") glyph.Add(renderer);
            return new MissionMedal { face = face, glyph = glyph.ToArray(), caption = caption };
        }

        void MissionControl(Transform parent, string caption, Vector3 point, float width, OrbitalControlKind kind, int value = 0)
        {
            var go = MissionPlate(parent, "Action " + kind, point, width, .038f, .003f, .006f, palette[3]);
            var collider = go.AddComponent<BoxCollider>(); collider.size = new Vector3(width, .038f, .014f); collider.isTrigger = true;
            go.AddComponent<OrbitalClickTarget>();
            var control = go.AddComponent<OrbitalControl>(); control.owner = this; control.kind = kind; control.value = value;
            buildControls.Add(control);
            var label = MissionCopy(parent, caption, point - Vector3.forward * .009f, width - .014f, .026f, 2.8f);
            go.AddComponent<OrbitalControlLabel>().text = label;
            MissionBox(go.transform, "Action signal", new Vector3(-width * .5f + .005f, 0, -.004f), new Vector3(.002f, .020f, .001f),
                kind == OrbitalControlKind.Hint ? palette[2] : palette[1]);
        }

        TextMeshPro MissionCopy(Transform parent, string text, Vector3 point, float width, float height, float maxSize, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var result = Label(parent, text, point, width, height);
            result.fontSizeMax = maxSize;
            result.alignment = alignment;
            result.overflowMode = TextOverflowModes.Ellipsis;
            return result;
        }

        GameObject MissionBox(Transform parent, string name, Vector3 point, Vector3 size, Material material)
            => Primitive(name, PrimitiveType.Cube, parent, point, size, material, false);

        Renderer MissionDisc(Transform parent, string name, Vector3 point, float radius, float depth, int sides, Material material)
        {
            var outline = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = (i + .5f) * Mathf.PI * 2 / sides;
                outline[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return MissionPolygon(parent, name, point, outline, depth, material).GetComponent<Renderer>();
        }

        GameObject MissionPlate(Transform parent, string name, Vector3 point, float width, float height, float cut, float depth, Material material,
            Vector3 meshOffset = default(Vector3))
        {
            float x = width * .5f, y = height * .5f;
            return MissionPolygon(parent, name, point, new[] {
                new Vector2(-x+cut,-y),new Vector2(x-cut,-y),new Vector2(x,-y+cut),new Vector2(x,y-cut),
                new Vector2(x-cut,y),new Vector2(-x+cut,y),new Vector2(-x,y-cut),new Vector2(-x,-y+cut)
            }, depth, material, meshOffset);
        }

        GameObject MissionPolygon(Transform parent, string name, Vector3 point, Vector2[] outline, float depth, Material material,
            Vector3 meshOffset = default(Vector3))
        {
            int count = outline.Length;
            var vertices = new Vector3[count * 2];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(outline[i].x, outline[i].y, -depth * .5f);
                vertices[i + count] = new Vector3(outline[i].x, outline[i].y, depth * .5f);
            }
            var meshVertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            void Face(int a, int b, int c)
            {
                Vector3 cross = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                double length = Math.Sqrt((double)cross.x * cross.x + (double)cross.y * cross.y + (double)cross.z * cross.z);
                // Collinear fan vertices do not define a face. Avoid zero normals
                // without relying on RecalculateNormals' small-vector threshold.
                if (length <= 1e-20) return;
                Vector3 normal = new Vector3((float)(cross.x / length), (float)(cross.y / length), (float)(cross.z / length));
                int first = meshVertices.Count;
                meshVertices.Add(vertices[a] + meshOffset); meshVertices.Add(vertices[b] + meshOffset); meshVertices.Add(vertices[c] + meshOffset);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            }
            for (int i = 1; i < count - 1; i++)
            {
                Face(0, i + 1, i);
                Face(count, count + i, count + i + 1);
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                Face(i, next, i + count);
                Face(next, next + count, i + count);
            }
            if (meshVertices.Count == 0) throw new InvalidOperationException("Mission polygon has no valid faces: " + name);
            var mesh = new Mesh { name = "Mission " + name };
            mesh.SetVertices(meshVertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = point;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        static void SetMissionMaterial(Renderer renderer, Material material)
        {
            if (renderer != null && renderer.sharedMaterial != material) renderer.sharedMaterial = material;
        }
    }
}
