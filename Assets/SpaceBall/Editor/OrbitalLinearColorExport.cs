using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>
    /// Restores linear glTF base-color factors from this project's authored URP
    /// colours. The source materials retain the gamma colours expected by Unity;
    /// emissive factors, texture data and SDK package code remain untouched.
    /// </summary>
    public static class OrbitalLinearColorExport
    {
        const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;
        const string MaterialFolder = "Assets/SpaceBall/Materials";
        const string SceneFile = "Assets/SpaceBall/Scenes/OrbitalCourier_Prototype.unity";
        const double ColorTolerance = .000001;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        sealed class Glb
        {
            public JObject json;
            // null represents the actual material-only GLB shape with no BIN chunk.
            public byte[] bin;
        }

        sealed class SourceColor
        {
            public string path;
            public double[] gamma, linear;
        }

        sealed class Change
        {
            public int materialIndex;
            public string materialName, sourceAsset;
            public double[] before, after;
        }

        sealed class Report
        {
            public string status, path, beforeSha256, afterSha256, binSha256;
            public long beforeBytes, afterBytes, binBytes;
            public int matchedMaterials, changedMaterials, alreadyLinearMaterials, unknownMaterials;
            public bool onlyBaseColorFactorsChanged, binBytesUnchanged, emissiveAndTexturesUnchanged;
            public readonly List<Change> changes = new List<Change>();
            public readonly List<string> untouchedMaterialNames = new List<string>();
        }

        /// <summary>
        /// Accepts only this project's normal/dirty exported scene or a Temp
        /// SpaceBall material GLB. The returned JSON identifies the exact artifact
        /// and proves that only known baseColorFactor values were replaced.
        /// </summary>
        public static string Restore(string glbPath)
        {
            string path = AcceptedPath(glbPath);
            byte[] original = File.ReadAllBytes(path);
            Glb before = Read(original);
            var sources = SourceColors();
            var document = (JObject)before.json.DeepClone();
            var report = new Report
            {
                status = "unchanged", path = path,
                beforeSha256 = Hash(original), afterSha256 = Hash(original),
                beforeBytes = original.LongLength, afterBytes = original.LongLength,
                binBytes = before.bin == null ? 0 : before.bin.LongLength,
                binSha256 = before.bin == null ? "" : Hash(before.bin)
            };

            if (document["materials"] != null && !(document["materials"] is JArray))
                throw new InvalidDataException("GLB materials must be an array.");
            var materials = document["materials"] as JArray;
            if (materials != null)
                for (int index = 0; index < materials.Count; index++)
                {
                    if (!(materials[index] is JObject material))
                        throw new InvalidDataException("GLB material must be an object.");
                    if (material["name"] != null && material["name"].Type != JTokenType.String)
                        throw new InvalidDataException("GLB material name must be a string.");
                    string name = material.Value<string>("name");
                    if (name == null || !sources.TryGetValue(name, out SourceColor source))
                    {
                        report.unknownMaterials++;
                        if (!report.untouchedMaterialNames.Contains(name)) report.untouchedMaterialNames.Add(name);
                        continue;
                    }
                    report.matchedMaterials++;
                    if (!(material["pbrMetallicRoughness"] is JObject pbr))
                        throw new InvalidDataException("Expected exported URP/Lit PBR material: " + name);
                    double[] current = ReadFactor(pbr["baseColorFactor"]);
                    if (SameColor(current, source.linear))
                    {
                        report.alreadyLinearMaterials++;
                        continue;
                    }
                    if (!SameColor(current, source.gamma))
                        throw new InvalidDataException("Exported baseColorFactor matches neither its source URP colour nor its restored linear value: " + name);
                    pbr["baseColorFactor"] = new JArray(source.linear);
                    report.changes.Add(new Change
                    {
                        materialIndex = index, materialName = name, sourceAsset = source.path,
                        before = current, after = source.linear
                    });
                }

            byte[] result = original;
            if (report.changes.Count > 0)
            {
                result = Write(new Glb { json = document, bin = before.bin });
                Glb after = Read(result);
                VerifyOnlyColorChanges(before, after, report.changes);
                report.status = "restored";
                report.changedMaterials = report.changes.Count;
                report.afterBytes = result.LongLength;
                report.afterSha256 = Hash(result);
                File.WriteAllBytes(path, result);
            }
            // No-change calls retain the original bytes, including JSON layout.
            report.onlyBaseColorFactorsChanged = true;
            report.binBytesUnchanged = true;
            report.emissiveAndTexturesUnchanged = true;
            return JsonConvert.SerializeObject(report, Formatting.Indented);
        }

        static string AcceptedPath(string input)
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(project, input));
            string temp = Path.GetFullPath(Path.Combine(project, "Temp", "SpatialAdapterExport"));
            string streaming = Path.GetFullPath(Path.Combine(project, "Assets", "StreamingAssets", "SpatialAdapterExport"));
            foreach (string root in new[] { temp, streaming })
                foreach (string suffix in new[] { ".glb", ".dirty.glb" })
                    if (string.Equals(path, Path.GetFullPath(Path.Combine(root, SceneFile + suffix)), StringComparison.OrdinalIgnoreCase))
                        return path;
            string directory = Path.GetDirectoryName(path);
            if (string.Equals(directory, Path.GetFullPath(Path.Combine(temp, MaterialFolder)), StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(path).EndsWith(".mat.glb", StringComparison.OrdinalIgnoreCase))
                return path;
            throw new ArgumentException("Colour restoration accepts only this project's normal/dirty scene exports or Temp SpaceBall material exports.", nameof(input));
        }

        static Dictionary<string, SourceColor> SourceColors()
        {
            var result = new Dictionary<string, SourceColor>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material.shader.name != "Universal Render Pipeline/Lit") continue;
                Color gamma = material.GetColor("_BaseColor");
                Color linear = gamma.linear;
                double[] raw = { gamma.r, gamma.g, gamma.b, gamma.a };
                double[] converted = { linear.r, linear.g, linear.b, gamma.a };
                if (raw.Any(value => double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1) ||
                    converted.Any(value => double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > 1))
                    throw new InvalidDataException("Source base colour is outside the glTF factor range: " + path);
                if (result.ContainsKey(material.name))
                    throw new InvalidDataException("Source material name is ambiguous in SpaceBall Materials: " + material.name);
                result.Add(material.name, new SourceColor { path = path, gamma = raw, linear = converted });
            }
            return result;
        }

        static double[] ReadFactor(JToken token)
        {
            if (token == null) return new[] { 1d, 1d, 1d, 1d }; // glTF's defined default.
            if (!(token is JArray array) || array.Count != 4)
                throw new InvalidDataException("baseColorFactor must contain four numeric components.");
            var values = new double[4];
            for (int i = 0; i < values.Length; i++)
            {
                if (array[i].Type != JTokenType.Integer && array[i].Type != JTokenType.Float)
                    throw new InvalidDataException("baseColorFactor contains a non-numeric component.");
                values[i] = array[i].Value<double>();
                if (double.IsNaN(values[i]) || double.IsInfinity(values[i]) || values[i] < 0 || values[i] > 1)
                    throw new InvalidDataException("baseColorFactor component is outside [0,1].");
            }
            return values;
        }

        static bool SameColor(double[] left, double[] right)
        {
            for (int i = 0; i < 4; i++) if (Math.Abs(left[i] - right[i]) > ColorTolerance) return false;
            return true;
        }

        static void VerifyOnlyColorChanges(Glb before, Glb after, List<Change> changes)
        {
            if ((before.bin == null) != (after.bin == null) ||
                before.bin != null && !before.bin.SequenceEqual(after.bin))
                throw new InvalidDataException("Colour restoration changed the BIN payload.");
            var restored = (JObject)after.json.DeepClone();
            foreach (var change in changes)
            {
                var pbr = (JObject)restored["materials"][change.materialIndex]["pbrMetallicRoughness"];
                if (!SameColor(ReadFactor(pbr["baseColorFactor"]), change.after))
                    throw new InvalidDataException("Restored base colour differs from its source linear colour.");
                var original = before.json["materials"][change.materialIndex]["pbrMetallicRoughness"]["baseColorFactor"];
                if (original == null) pbr.Remove("baseColorFactor");
                else pbr["baseColorFactor"] = original.DeepClone();
            }
            if (!JToken.DeepEquals(before.json, restored))
                throw new InvalidDataException("Colour restoration changed JSON outside the recorded baseColorFactor values.");
        }

        static Glb Read(byte[] raw)
        {
            using (var stream = new MemoryStream(raw, false))
            using (var reader = new BinaryReader(stream))
            {
                if (raw.Length < 20 || reader.ReadUInt32() != Magic || reader.ReadUInt32() != 2 || reader.ReadUInt32() != raw.Length)
                    throw new InvalidDataException("Invalid GLB 2 header or file length.");
                uint jsonLength = reader.ReadUInt32(), kind = reader.ReadUInt32();
                if (kind != JsonChunk || (jsonLength & 3) != 0 || jsonLength > stream.Length - stream.Position)
                    throw new InvalidDataException("GLB must begin with an aligned, bounded JSON chunk.");
                string text = Utf8.GetString(reader.ReadBytes(checked((int)jsonLength)));
                JObject document;
                using (var jsonReader = new JsonTextReader(new StringReader(text)) { DateParseHandling = DateParseHandling.None })
                {
                    document = JObject.Load(jsonReader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (jsonReader.Read()) throw new InvalidDataException("Unexpected content after the GLB JSON object.");
                }
                var glb = new Glb { json = document };
                if (stream.Position < stream.Length)
                {
                    if (stream.Length - stream.Position < 8) throw new InvalidDataException("Truncated GLB BIN header.");
                    uint binLength = reader.ReadUInt32();
                    if (reader.ReadUInt32() != BinChunk || (binLength & 3) != 0 || binLength != stream.Length - stream.Position)
                        throw new InvalidDataException("Expected one final, aligned, bounded BIN chunk.");
                    glb.bin = reader.ReadBytes(checked((int)binLength));
                }
                ValidateLayout(glb);
                return glb;
            }
        }

        static void ValidateLayout(Glb glb)
        {
            if (!(glb.json["asset"] is JObject asset) || asset.Value<string>("version") != "2.0")
                throw new InvalidDataException("Expected glTF asset version 2.0.");
            if (glb.json["buffers"] != null && !(glb.json["buffers"] is JArray) ||
                glb.json["bufferViews"] != null && !(glb.json["bufferViews"] is JArray))
                throw new InvalidDataException("GLB buffers and bufferViews must be arrays.");
            var buffers = glb.json["buffers"] as JArray;
            var views = glb.json["bufferViews"] as JArray;
            long declared = 0;
            if (glb.bin != null)
            {
                if (buffers == null || buffers.Count != 1 || !(buffers[0] is JObject buffer) || buffer["uri"] != null ||
                    buffer["byteLength"]?.Type != JTokenType.Integer)
                    throw new InvalidDataException("Expected one embedded GLB buffer declaration.");
                declared = buffer.Value<long>("byteLength");
                if (declared < 0 || declared > glb.bin.LongLength || glb.bin.LongLength - declared > 3)
                    throw new InvalidDataException("Declared embedded buffer length does not match BIN bytes.");
                for (long i = declared; i < glb.bin.LongLength; i++)
                    if (glb.bin[i] != 0) throw new InvalidDataException("Nonzero GLB BIN padding.");
            }
            else if (buffers != null && buffers.Count != 0 || views != null && views.Count != 0)
                throw new InvalidDataException("A GLB without BIN cannot contain embedded buffer declarations or views.");
            if (views != null)
                foreach (var token in views)
                {
                    if (!(token is JObject view) || view["buffer"]?.Type != JTokenType.Integer || view.Value<int>("buffer") != 0 ||
                        view["byteLength"]?.Type != JTokenType.Integer || view["byteOffset"] != null && view["byteOffset"].Type != JTokenType.Integer)
                        throw new InvalidDataException("Invalid embedded bufferView declaration.");
                    long offset = view.Value<long?>("byteOffset") ?? 0, length = view.Value<long>("byteLength");
                    if (offset < 0 || length < 0 || offset > declared || length > declared - offset)
                        throw new InvalidDataException("bufferView lies outside the embedded buffer.");
                }
            foreach (var property in glb.json.Descendants().OfType<JProperty>().Where(property => property.Name == "bufferView"))
                if (property.Value.Type != JTokenType.Integer || property.Value.Value<long>() < 0 || views == null || property.Value.Value<long>() >= views.Count)
                    throw new InvalidDataException("bufferView reference is outside the declared array.");
        }

        static byte[] Write(Glb glb)
        {
            byte[] json = Utf8.GetBytes(glb.json.ToString(Formatting.None));
            int jsonLength = checked((json.Length + 3) & ~3);
            long length = 20L + jsonLength + (glb.bin == null ? 0 : 8L + glb.bin.LongLength);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((uint)2); writer.Write(checked((uint)length));
                writer.Write((uint)jsonLength); writer.Write(JsonChunk); writer.Write(json);
                for (int i = json.Length; i < jsonLength; i++) writer.Write((byte)0x20);
                if (glb.bin != null)
                {
                    writer.Write(checked((uint)glb.bin.Length)); writer.Write(BinChunk); writer.Write(glb.bin);
                }
                return stream.ToArray();
            }
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
