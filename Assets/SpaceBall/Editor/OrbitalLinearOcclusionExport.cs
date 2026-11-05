using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Preserves the authored linear lunar AO in the SDK's exported GLB.</summary>
    public static class OrbitalLinearOcclusionExport
    {
        const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;
        const string MapPath = "Assets/SpaceBall/Art/LunarSurvey/Moon_AO_LOLA.png";
        const string ScenePath = "Assets/SpaceBall/Scenes/OrbitalCourier_Prototype.unity";

        public static bool IsLunarMaterialFile(string path)
        {
            string filename = Path.GetFileName(path);
            return string.Equals(filename, "MoonSurface.mat.glb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filename, "MoonCrater.mat.glb", StringComparison.OrdinalIgnoreCase);
        }

        static string AcceptedPath(string input)
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(project, input));
            foreach (string exportRoot in new[] {
                Path.Combine(project, "Temp", "SpatialAdapterExport"),
                Path.Combine(project, "Assets", "StreamingAssets", "SpatialAdapterExport") })
            {
                foreach (string suffix in new[] { ".glb", ".dirty.glb" })
                    if (string.Equals(path, Path.GetFullPath(Path.Combine(exportRoot, ScenePath + suffix)), StringComparison.OrdinalIgnoreCase))
                        return path;
                string materials = Path.GetFullPath(Path.Combine(exportRoot, "Assets", "SpaceBall", "Materials"));
                if (string.Equals(Path.GetDirectoryName(path), materials, StringComparison.OrdinalIgnoreCase) && IsLunarMaterialFile(path))
                    return path;
            }
            throw new InvalidDataException("Linear AO correction accepts only this project's scene or two lunar material exports in Temp/StreamingAssets.");
        }

        public static string Restore(string glbPath)
        {
            glbPath = AcceptedPath(glbPath);
            byte[] original = File.ReadAllBytes(glbPath);
            byte[] sourceMap = File.ReadAllBytes(MapPath);
            JObject document;
            byte[] binary;
            using (var reader = new BinaryReader(new MemoryStream(original)))
            {
                if (reader.ReadUInt32() != Magic || reader.ReadUInt32() != 2 || reader.ReadUInt32() != original.Length)
                    throw new InvalidDataException("Invalid GLB header.");
                int jsonLength = checked((int)reader.ReadUInt32());
                if (reader.ReadUInt32() != JsonChunk) throw new InvalidDataException("Missing GLB JSON chunk.");
                document = JObject.Parse(Encoding.UTF8.GetString(reader.ReadBytes(jsonLength)));
                int binLength = checked((int)reader.ReadUInt32());
                if (reader.ReadUInt32() != BinChunk) throw new InvalidDataException("Missing GLB binary chunk.");
                binary = reader.ReadBytes(binLength);
                if (binary.Length != binLength || reader.BaseStream.Position != original.Length)
                    throw new InvalidDataException("Unexpected GLB binary extent.");
            }

            var materials = (JArray)document["materials"];
            var textures = (JArray)document["textures"];
            var images = (JArray)document["images"];
            var views = (JArray)document["bufferViews"];
            var imageIndices = new HashSet<int>();
            foreach (var material in materials)
                if ((string)material["name"] == "MoonSurface" || (string)material["name"] == "MoonCrater")
                {
                    int textureIndex = (int)material["occlusionTexture"]["index"];
                    imageIndices.Add((int)textures[textureIndex]["source"]);
                }
            if (imageIndices.Count == 0) throw new InvalidDataException("Exported lunar occlusion binding is missing.");

            // Only replace the linear data image. Geometry buffer views, UVs,
            // material factors and all texture/image indices stay unchanged.
            int corrected = 0;
            var updated = new MemoryStream();
            updated.Write(binary, 0, binary.Length);
            foreach (int index in imageIndices)
            {
                var image = images[index];
                if ((string)image["mimeType"] != "image/png") throw new InvalidDataException("Lunar AO must be embedded PNG.");
                var oldView = views[(int)image["bufferView"]];
                int offset = (int?)oldView["byteOffset"] ?? 0, length = (int)oldView["byteLength"];
                if ((int)oldView["buffer"] != 0 || offset < 0 || length < 1 || (long)offset + length > binary.Length)
                    throw new InvalidDataException("Invalid lunar image buffer view.");
                if (length == sourceMap.Length && binary.Skip(offset).Take(length).SequenceEqual(sourceMap)) continue;
                while (updated.Length % 4 != 0) updated.WriteByte(0);
                int newOffset = checked((int)updated.Length);
                updated.Write(sourceMap, 0, sourceMap.Length);
                image["bufferView"] = views.Count;
                views.Add(new JObject { ["buffer"] = 0, ["byteOffset"] = newOffset, ["byteLength"] = sourceMap.Length });
                corrected++;
            }
            if (corrected != 0)
            {
                byte[] resultBinary = updated.ToArray();
                document["buffers"][0]["byteLength"] = resultBinary.Length;
                byte[] json = Encoding.UTF8.GetBytes(document.ToString(Formatting.None));
                int jsonSize = (json.Length + 3) & ~3, binSize = (resultBinary.Length + 3) & ~3;
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic); writer.Write((uint)2);
                    writer.Write(checked((uint)(12L + 8 + jsonSize + 8 + binSize)));
                    writer.Write((uint)jsonSize); writer.Write(JsonChunk); writer.Write(json);
                    for (int i = json.Length; i < jsonSize; i++) writer.Write((byte)0x20);
                    writer.Write((uint)binSize); writer.Write(BinChunk); writer.Write(resultBinary);
                    for (int i = resultBinary.Length; i < binSize; i++) writer.Write((byte)0);
                    File.WriteAllBytes(glbPath, stream.ToArray());
                }
            }
            updated.Dispose();
            return JsonConvert.SerializeObject(new { status = "passed", path = Path.GetFullPath(glbPath),
                correctedImages = corrected, sourceMap = MapPath,
                reason = "The default colour export gamma-encodes a linear AO map; use the original data PNG." });
        }
    }
}
