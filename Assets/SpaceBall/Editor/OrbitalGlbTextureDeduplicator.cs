using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpaceBall.Editor
{
    /// <summary>
    /// Removes byte-identical embedded PNG images from this project's generated
    /// GLB, then merges identical core texture records when their reference
    /// shape is known. Material indices and authored scene data stay stable.
    /// Runs after SDK export and before the official Spatial bundle packer.
    /// </summary>
    public static class OrbitalGlbTextureDeduplicator
    {
        const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        sealed class Glb
        {
            public JObject json;
            public byte[] bin;
        }

        sealed class ImageData
        {
            public int oldIndex;
            public string mime, hash;
            public byte[] bytes;
        }

        [Serializable]
        sealed class Report
        {
            public string status, path, beforeSha256, afterSha256, sceneInvariantSha256, textureMergeStatus;
            public long beforeBytes, afterBytes, bytesSaved;
            public int imagesBefore, imagesAfter, texturesBefore, texturesAfter;
            public int bufferViewsBefore, bufferViewsAfter, removedImageBufferViews;
            public int remappedCoreTextureInfoReferences;
            public int nodes, meshes, primitives;
            public bool sceneDataVerified, textureSourcesVerified;
        }

        /// <summary>Returns a JSON evidence report; optional reportPath is outside the GLB.</summary>
        public static string Optimize(string glbPath, string reportPath = null)
        {
            string path = Path.GetFullPath(glbPath);
            if (!string.Equals(Path.GetExtension(path), ".glb", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Texture deduplication accepts one explicit .glb file.");
            if (!string.IsNullOrEmpty(reportPath) && string.Equals(Path.GetFullPath(reportPath), path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The report path must differ from the input GLB.");
            byte[] original = File.ReadAllBytes(path);
            Glb before = Read(original);
            ValidateBufferLayout(before);
            var images = before.json["images"] as JArray ?? new JArray();
            var textures = before.json["textures"] as JArray ?? new JArray();
            var views = (JArray)before.json["bufferViews"];
            var report = new Report
            {
                status = "unchanged", path = path,
                beforeBytes = original.LongLength, afterBytes = original.LongLength,
                beforeSha256 = Hash(original), afterSha256 = Hash(original),
                imagesBefore = images.Count, imagesAfter = images.Count,
                texturesBefore = textures.Count, texturesAfter = textures.Count,
                bufferViewsBefore = views.Count, bufferViewsAfter = views.Count,
                nodes = (before.json["nodes"] as JArray)?.Count ?? 0,
                meshes = (before.json["meshes"] as JArray)?.Count ?? 0,
                primitives = (before.json["meshes"] as JArray)?.Sum(mesh => ((JArray)mesh["primitives"]).Count) ?? 0,
                sceneInvariantSha256 = SceneFingerprint(before)
            };

            // Only standard embedded PNG records are eligible. Extensions/extras
            // may carry other semantics, so those images remain untouched.
            var canonical = new List<ImageData>();
            var map = new int[images.Count];
            var keptImages = new JArray();
            var redundantViews = new HashSet<int>();
            var retainedImageViews = new HashSet<int>();
            for (int index = 0; index < images.Count; index++)
            {
                var image = (JObject)images[index];
                int view = image.Value<int?>("bufferView") ?? -1;
                string mime = image.Value<string>("mimeType");
                bool eligible = mime == "image/png" && view >= 0 &&
                    image.Properties().All(property => property.Name == "name" || property.Name == "mimeType" || property.Name == "bufferView");
                byte[] bytes = eligible ? ViewBytes(before, view) : null;
                string hash = eligible ? Hash(bytes) : null;
                ImageData existing = eligible ? canonical.FirstOrDefault(item => item.mime == mime && item.hash == hash && item.bytes.SequenceEqual(bytes)) : null;
                if (existing != null)
                {
                    map[index] = map[existing.oldIndex];
                    redundantViews.Add(view);
                }
                else
                {
                    map[index] = keptImages.Count;
                    keptImages.Add(image.DeepClone());
                    if (view >= 0) retainedImageViews.Add(view);
                    if (eligible) canonical.Add(new ImageData { oldIndex = index, mime = mime, hash = hash, bytes = bytes });
                }
            }

            Glb after = new Glb { json = (JObject)before.json.DeepClone() };
            after.json["images"] = keptImages;
            // Image merging can run independently of texture-record merging.
            foreach (var texture in (after.json["textures"] as JArray ?? new JArray()).OfType<JObject>())
                RemapImageSources(texture, map);
            int[] textureMap = MergeCoreTextures(after.json, out string textureMergeStatus, out int remappedTextureInfos);
            report.textureMergeStatus = textureMergeStatus;
            report.remappedCoreTextureInfoReferences = remappedTextureInfos;
            report.texturesAfter = (after.json["textures"] as JArray)?.Count ?? 0;
            if (keptImages.Count == images.Count && report.texturesAfter == textures.Count)
            {
                report.sceneDataVerified = report.textureSourcesVerified = true;
                return SaveReport(report, reportPath);
            }

            var nonImageReferences = new HashSet<int>();
            foreach (var property in after.json.Properties())
                if (property.Name != "images" && property.Name != "bufferViews")
                    VisitViewReferences(property.Value, id => { nonImageReferences.Add(id); return id; });
            redundantViews.ExceptWith(retainedImageViews);
            redundantViews.ExceptWith(nonImageReferences);

            var viewMap = new Dictionary<int, int>();
            var rebuiltViews = new JArray();
            using (var stream = new MemoryStream())
            {
                for (int index = 0; index < views.Count; index++)
                {
                    if (redundantViews.Contains(index)) continue;
                    while ((stream.Position & 3) != 0) stream.WriteByte(0);
                    var view = (JObject)views[index].DeepClone();
                    byte[] bytes = ViewBytes(before, index);
                    view["byteOffset"] = stream.Position;
                    stream.Write(bytes, 0, bytes.Length);
                    viewMap.Add(index, rebuiltViews.Count);
                    rebuiltViews.Add(view);
                }
                after.bin = stream.ToArray();
            }
            after.json["bufferViews"] = rebuiltViews;
            VisitViewReferences(after.json, old => viewMap.TryGetValue(old, out int value) ? value :
                throw new InvalidDataException("A removed image bufferView is still referenced: " + old));
            after.json["buffers"][0]["byteLength"] = after.bin.LongLength;
            ValidateBufferLayout(after);
            string fingerprint = SceneFingerprint(after);
            if (fingerprint != report.sceneInvariantSha256)
                throw new InvalidDataException("Texture deduplication changed scene, material, accessor or geometry data.");
            VerifyTextureSources(before, after, map, textureMap);

            byte[] result = Write(after);
            // Parse the actual emitted GLB again, rather than validating only the
            // in-memory model which produced it.
            Glb reread = Read(result);
            ValidateBufferLayout(reread);
            if (SceneFingerprint(reread) != report.sceneInvariantSha256)
                throw new InvalidDataException("Written GLB failed the scene-data round-trip check.");
            VerifyTextureSources(before, reread, map, textureMap);
            if (result.LongLength >= original.LongLength)
                throw new InvalidDataException("Deduplicated GLB did not become smaller; original file retained.");

            string temporary = path + ".spaceball-tmp";
            try
            {
                File.WriteAllBytes(temporary, result);
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            report.status = "optimized";
            report.afterBytes = result.LongLength;
            report.bytesSaved = report.beforeBytes - report.afterBytes;
            report.afterSha256 = Hash(result);
            report.imagesAfter = keptImages.Count;
            report.bufferViewsAfter = rebuiltViews.Count;
            report.removedImageBufferViews = redundantViews.Count;
            report.sceneDataVerified = report.textureSourcesVerified = true;
            return SaveReport(report, reportPath);
        }

        static int[] MergeCoreTextures(JObject json, out string status, out int remappedInfos)
        {
            var textures = json["textures"] as JArray ?? new JArray();
            var map = Enumerable.Range(0, textures.Count).ToArray();
            remappedInfos = 0;
            // Unknown extensions could contain texture indices with another
            // meaning. Keep the images-only optimization in that case.
            foreach (var extensionProperty in json.Descendants().OfType<JProperty>().Where(property => property.Name == "extensions"))
                if (extensionProperty.Value is JObject extensions && extensions.Properties().Any(property =>
                    property.Name != "KHR_materials_unlit" || !(property.Value is JObject) || property.Value.HasValues))
                { status = "skipped_unsupported_extensions"; return map; }
            if (textures.OfType<JObject>().Any(texture => texture.Properties().Any(property => property.Name != "name" && property.Name != "source" && property.Name != "sampler")))
            { status = "skipped_noncore_texture_record"; return map; }

            var materialFields = new HashSet<string> { "name", "pbrMetallicRoughness", "normalTexture", "occlusionTexture", "emissiveTexture", "emissiveFactor", "alphaMode", "alphaCutoff", "doubleSided", "extensions" };
            var pbrFields = new HashSet<string> { "baseColorFactor", "baseColorTexture", "metallicFactor", "roughnessFactor", "metallicRoughnessTexture" };
            var materials = json["materials"] as JArray ?? new JArray();
            foreach (var material in materials.OfType<JObject>())
            {
                if (material.Properties().Any(property => !materialFields.Contains(property.Name)) ||
                    material["pbrMetallicRoughness"] is JObject pbr && pbr.Properties().Any(property => !pbrFields.Contains(property.Name)))
                { status = "skipped_noncore_material_shape"; return map; }
                foreach (var info in CoreTextureInfos(material))
                {
                    if (info["index"]?.Type != JTokenType.Integer || info.Value<int>("index") < 0 || info.Value<int>("index") >= textures.Count)
                        throw new InvalidDataException("Core material textureInfo index is out of range.");
                    var allowed = new HashSet<string> { "index", "texCoord", "scale", "strength" };
                    if (info.Properties().Any(property => !allowed.Contains(property.Name)))
                    { status = "skipped_extended_texture_info"; return map; }
                }
            }

            var kept = new JArray();
            for (int index = 0; index < textures.Count; index++)
            {
                int existing = -1;
                for (int candidate = 0; candidate < kept.Count; candidate++)
                    if (JToken.DeepEquals(textures[index], kept[candidate])) { existing = candidate; break; }
                map[index] = existing >= 0 ? existing : kept.Count;
                if (existing < 0) kept.Add(textures[index].DeepClone());
            }
            if (kept.Count == textures.Count) { status = "no_duplicate_core_textures"; return map; }
            foreach (var material in materials.OfType<JObject>())
                foreach (var info in CoreTextureInfos(material))
                {
                    int old = info.Value<int>("index");
                    info["index"] = map[old];
                    if (old != map[old]) remappedInfos++;
                }
            json["textures"] = kept;
            status = "merged_core_textures";
            return map;
        }

        static IEnumerable<JObject> CoreTextureInfos(JObject material)
        {
            var pbr = material["pbrMetallicRoughness"] as JObject;
            foreach (string name in new[] { "baseColorTexture", "metallicRoughnessTexture" })
            {
                var info = pbr?[name] as JObject;
                if (info != null) yield return info;
            }
            foreach (string name in new[] { "normalTexture", "occlusionTexture", "emissiveTexture" })
            {
                var info = material[name] as JObject;
                if (info != null) yield return info;
            }
        }

        static void RemapImageSources(JToken token, int[] map)
        {
            if (token is JObject obj)
                foreach (var property in obj.Properties().ToArray())
                {
                    if (property.Name == "source" && property.Value.Type == JTokenType.Integer)
                    {
                        int old = property.Value.Value<int>();
                        if (old < 0 || old >= map.Length) throw new InvalidDataException("Texture image source is out of range.");
                        property.Value = map[old];
                    }
                    else RemapImageSources(property.Value, map);
                }
            else if (token is JArray array) foreach (var item in array) RemapImageSources(item, map);
        }

        static void VisitViewReferences(JToken token, Func<int, int> rewrite)
        {
            if (token is JObject obj)
                foreach (var property in obj.Properties().ToArray())
                {
                    if (property.Name == "bufferView" && property.Value.Type == JTokenType.Integer)
                        property.Value = rewrite(property.Value.Value<int>());
                    else VisitViewReferences(property.Value, rewrite);
                }
            else if (token is JArray array) foreach (var item in array) VisitViewReferences(item, rewrite);
        }

        static string SceneFingerprint(Glb glb)
        {
            var copy = (JObject)glb.json.DeepClone();
            // Every other JSON field is preserved, including node identity,
            // materials, mesh topology, animations, skins and all accessors.
            foreach (string name in new[] { "images", "textures", "buffers", "bufferViews" }) copy.Remove(name);
            var textureFingerprints = new Dictionary<int, string>();
            foreach (var material in (copy["materials"] as JArray ?? new JArray()).OfType<JObject>())
                foreach (var info in CoreTextureInfos(material))
                {
                    int index = info.Value<int>("index");
                    if (!textureFingerprints.TryGetValue(index, out string fingerprint))
                        textureFingerprints[index] = fingerprint = TextureFingerprint(glb, index);
                    // Normalize the reference by its rendered content, not its
                    // old array position. Other textureInfo fields stay exact.
                    info["index"] = fingerprint;
                }
            ReplaceViewReferencesWithContent(copy, glb);
            return Hash(Encoding.UTF8.GetBytes(copy.ToString(Formatting.None)));
        }

        static string TextureFingerprint(Glb glb, int index)
        {
            var texture = (JObject)glb.json["textures"][index].DeepClone();
            int? source = texture.Value<int?>("source");
            if (source.HasValue)
            {
                var image = (JObject)glb.json["images"][source.Value].DeepClone();
                int? view = image.Value<int?>("bufferView");
                image.Remove("name");
                if (view.HasValue)
                {
                    image.Remove("bufferView");
                    image["payloadSha256"] = Hash(ViewBytes(glb, view.Value));
                }
                texture["source"] = image;
            }
            int? sampler = texture.Value<int?>("sampler");
            if (sampler.HasValue) texture["sampler"] = glb.json["samplers"][sampler.Value].DeepClone();
            return Hash(Encoding.UTF8.GetBytes(CanonicalJson(texture).ToString(Formatting.None)));
        }

        static JToken CanonicalJson(JToken token)
        {
            if (token is JObject obj)
                return new JObject(obj.Properties().OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => new JProperty(property.Name, CanonicalJson(property.Value))));
            if (token is JArray array) return new JArray(array.Select(CanonicalJson));
            return token.DeepClone();
        }

        static void ReplaceViewReferencesWithContent(JToken token, Glb glb)
        {
            if (token is JObject obj)
                foreach (var property in obj.Properties().ToArray())
                {
                    if (property.Name == "bufferView" && property.Value.Type == JTokenType.Integer)
                    {
                        int id = property.Value.Value<int>();
                        var description = (JObject)glb.json["bufferViews"][id].DeepClone();
                        description.Remove("byteOffset");
                        description["payloadSha256"] = Hash(ViewBytes(glb, id));
                        property.Value = description;
                    }
                    else ReplaceViewReferencesWithContent(property.Value, glb);
                }
            else if (token is JArray array) foreach (var item in array) ReplaceViewReferencesWithContent(item, glb);
        }

        static void VerifyTextureSources(Glb before, Glb after, int[] map, int[] textureMap)
        {
            var oldTextures = before.json["textures"] as JArray ?? new JArray();
            var newTextures = after.json["textures"] as JArray ?? new JArray();
            if (textureMap.Length != oldTextures.Count) throw new InvalidDataException("Texture remap is incomplete.");
            for (int index = 0; index < oldTextures.Count; index++)
            {
                var expected = oldTextures[index].DeepClone();
                RemapImageSources(expected, map);
                int remapped = textureMap[index];
                if (remapped < 0 || remapped >= newTextures.Count || !JToken.DeepEquals(expected, newTextures[remapped]))
                    throw new InvalidDataException("Texture sampler, extension or source changed unexpectedly.");
                if (TextureFingerprint(before, index) != TextureFingerprint(after, remapped))
                    throw new InvalidDataException("Texture sampler or image content changed semantically.");
            }
            var oldImages = before.json["images"] as JArray ?? new JArray();
            var newImages = after.json["images"] as JArray ?? new JArray();
            for (int index = 0; index < oldImages.Count; index++)
            {
                var oldImage = oldImages[index]; var newImage = newImages[map[index]];
                if (oldImage.Value<string>("mimeType") != newImage.Value<string>("mimeType") ||
                    oldImage.Value<string>("uri") != newImage.Value<string>("uri"))
                    throw new InvalidDataException("Image media type or URI changed.");
                int? oldView = oldImage.Value<int?>("bufferView"), newView = newImage.Value<int?>("bufferView");
                if (oldView.HasValue != newView.HasValue || oldView.HasValue &&
                    !ViewBytes(before, oldView.Value).SequenceEqual(ViewBytes(after, newView.Value)))
                    throw new InvalidDataException("A texture's image bytes changed.");
            }
        }

        static void ValidateBufferLayout(Glb glb)
        {
            var buffers = glb.json["buffers"] as JArray;
            if (buffers == null || buffers.Count != 1 || buffers[0]["uri"] != null)
                throw new InvalidDataException("Expected one embedded GLB buffer.");
            long declared = buffers[0].Value<long>("byteLength");
            if (declared < 0 || declared > glb.bin.LongLength) throw new InvalidDataException("Invalid BIN buffer length.");
            foreach (var view in ((JArray)glb.json["bufferViews"]).OfType<JObject>())
            {
                if (view.Value<int>("buffer") != 0 || view["extensions"] != null)
                    throw new InvalidDataException("External or compressed bufferView is outside this optimizer's supported export shape.");
                long offset = view.Value<long?>("byteOffset") ?? 0, size = view.Value<long>("byteLength");
                if (offset < 0 || size < 0 || offset + size > declared)
                    throw new InvalidDataException("bufferView exceeds the embedded BIN buffer.");
            }
            VisitViewReferences(glb.json, id => id >= 0 && id < ((JArray)glb.json["bufferViews"]).Count ? id :
                throw new InvalidDataException("bufferView reference is out of range."));
        }

        static byte[] ViewBytes(Glb glb, int id)
        {
            var view = glb.json["bufferViews"][id];
            int start = view.Value<int?>("byteOffset") ?? 0, length = view.Value<int>("byteLength");
            var result = new byte[length];
            Buffer.BlockCopy(glb.bin, start, result, 0, length);
            return result;
        }

        static Glb Read(byte[] raw)
        {
            using (var stream = new MemoryStream(raw, false))
            using (var reader = new BinaryReader(stream))
            {
                if (raw.Length < 20 || reader.ReadUInt32() != Magic || reader.ReadUInt32() != 2 || reader.ReadUInt32() != raw.Length)
                    throw new InvalidDataException("Invalid GLB 2 header.");
                var glb = new Glb();
                while (stream.Position < stream.Length)
                {
                    if (stream.Length - stream.Position < 8) throw new InvalidDataException("Truncated GLB chunk header.");
                    uint length = reader.ReadUInt32(), kind = reader.ReadUInt32();
                    if ((length & 3) != 0 || length > stream.Length - stream.Position) throw new InvalidDataException("Invalid GLB chunk length.");
                    byte[] chunk = reader.ReadBytes(checked((int)length));
                    if (kind == JsonChunk && glb.json == null)
                        glb.json = JObject.Parse(Encoding.UTF8.GetString(chunk).TrimEnd(' ', '\0', '\r', '\n', '\t'));
                    else if (kind == BinChunk && glb.bin == null) glb.bin = chunk;
                    else throw new InvalidDataException("Unsupported or duplicate GLB chunk.");
                }
                if (glb.json == null || glb.bin == null) throw new InvalidDataException("GLB requires JSON and BIN chunks.");
                return glb;
            }
        }

        static byte[] Write(Glb glb)
        {
            byte[] json = Encoding.UTF8.GetBytes(glb.json.ToString(Formatting.None));
            int jsonLength = checked((json.Length + 3) & ~3), binLength = checked((glb.bin.Length + 3) & ~3);
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write((uint)2); writer.Write(checked((uint)(12L + 8 + jsonLength + 8 + binLength)));
                writer.Write((uint)jsonLength); writer.Write(JsonChunk); writer.Write(json);
                for (int index = json.Length; index < jsonLength; index++) writer.Write((byte)0x20);
                writer.Write((uint)binLength); writer.Write(BinChunk); writer.Write(glb.bin);
                for (int index = glb.bin.Length; index < binLength; index++) writer.Write((byte)0);
                return stream.ToArray();
            }
        }

        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static string SaveReport(Report report, string reportPath)
        {
            string json = JsonConvert.SerializeObject(report, Formatting.Indented);
            if (!string.IsNullOrEmpty(reportPath))
            {
                string path = Path.GetFullPath(reportPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            return json;
        }
    }
}
