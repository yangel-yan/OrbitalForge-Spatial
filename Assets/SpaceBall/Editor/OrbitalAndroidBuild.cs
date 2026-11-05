using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ByteDance.PICO.SpatialAdapter.Exporter.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessWindowStyle = System.Diagnostics.ProcessWindowStyle;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace SpaceBall.Editor
{
    public static class OrbitalAndroidBuild
    {
        const string PrototypeScenePath = "Assets/SpaceBall/Scenes/OrbitalCourier_Prototype.unity";
        const string SpatialBundleEntry = "assets/SpatialAdapterExport/SpatialAssetBundle.bundle";
        internal static bool SpatialBundleCompatibilityActive { get; private set; }

        [MenuItem("SpaceBall/Build Android First Chapter")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building the Android prototype.");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new InvalidOperationException("The active build target must already be Android. " +
                                                    "The prototype build does not switch platforms.");

            if (BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Another Unity Player build is already running.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PrototypeScenePath) == null)
                throw new FileNotFoundException("Create and save the Orbital Courier prototype scene before building.",
                    PrototypeScenePath);

            OrbitalBranding.Apply();

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot))
                throw new InvalidOperationException("Unity could not determine the project directory.");

            string buildFolder = Path.Combine(projectRoot, "Builds", "SpaceBall");
            Directory.CreateDirectory(buildFolder);
            string outputPath = Path.Combine(buildFolder, "OrbitalForge-Chapter1.apk");
            string reportPath = Path.Combine(buildFolder, "chapter-build-report.json");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { PrototypeScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development
            };

            var timer = Stopwatch.StartNew();
            string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            BuildReport report;
            try
            {
                PrepareSpatialBuild(projectRoot);
                InvalidateGeneratedGradleArchive(projectRoot);
                // Preserve the previous candidate. A failed build must not leave
                // yesterday's APK at the current deliverable path.
                if (File.Exists(outputPath))
                {
                    string previousFolder = Path.Combine(buildFolder, "Previous");
                    Directory.CreateDirectory(previousFolder);
                    string previousPath = Path.Combine(previousFolder, "OrbitalForge-Chapter1-" +
                        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".apk");
                    File.Move(outputPath, previousPath);
                }
                // SDK 6.1.0's bundle helper can receive a relative CallerFilePath and fail
                // in its static initializer. Use the same official packer after SDK export.
                using (new SpatialBundleCompatibilityScope())
                    report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception exception)
            {
                timer.Stop();
                WriteReport(reportPath, new LocalBuildReport
                {
                    output = outputPath,
                    applicationIdentifier = applicationIdentifier,
                    status = "Exception",
                    summary = new LocalBuildSummary
                    {
                        elapsedSeconds = timer.Elapsed.TotalSeconds,
                        totalErrors = 1
                    },
                    errors = new[] { exception.Message }
                });
                Debug.LogError("Orbital Android build Exception after " + timer.Elapsed.TotalSeconds.ToString("F1") +
                               "s; errors=1, warnings=0. " + exception.Message);
                throw;
            }

            timer.Stop();
            if (report == null)
            {
                const string error = "Unity returned no BuildReport for the Android prototype build.";
                WriteReport(reportPath, new LocalBuildReport
                {
                    output = outputPath,
                    applicationIdentifier = applicationIdentifier,
                    status = "NoReport",
                    summary = new LocalBuildSummary
                    {
                        elapsedSeconds = timer.Elapsed.TotalSeconds,
                        totalErrors = 1
                    },
                    errors = new[] { error }
                });
                Debug.LogError(error + " Elapsed=" + timer.Elapsed.TotalSeconds.ToString("F1") + "s.");
                throw new InvalidOperationException(error);
            }

            var summary = report.summary;
            var errors = CollectBuildErrors(report);
            string result = summary.result.ToString();
            if (summary.result == BuildResult.Succeeded && summary.totalErrors > 0)
            {
                result = "FailedBuildErrors";
                if (errors.Count == 0)
                    errors.Add("Unity reported " + summary.totalErrors + " build errors despite a Succeeded result. Inspect the Editor build log.");
            }
            long apkSizeBytes = 0, spatialBundleSizeBytes = 0;
            int validationErrors = 0;
            if (summary.result == BuildResult.Succeeded)
            {
                try
                {
                    apkSizeBytes = new FileInfo(outputPath).Length;
                    spatialBundleSizeBytes = ValidateSpatialBundle(outputPath);
                }
                catch (Exception exception)
                {
                    result = "FailedValidation";
                    validationErrors = 1;
                    errors.Add("Packaged Spatial asset validation: " + exception.Message);
                }
            }
            WriteReport(reportPath, new LocalBuildReport
            {
                output = outputPath,
                applicationIdentifier = applicationIdentifier,
                status = result,
                apkSizeBytes = apkSizeBytes,
                spatialBundleSizeBytes = spatialBundleSizeBytes,
                summary = new LocalBuildSummary
                {
                    elapsedSeconds = timer.Elapsed.TotalSeconds,
                    buildSeconds = summary.totalTime.TotalSeconds,
                    totalErrors = summary.totalErrors + validationErrors,
                    totalWarnings = summary.totalWarnings,
                    sizeBytes = summary.totalSize
                },
                errors = errors.ToArray()
            });

            string status = "Orbital Android build " + result + " after " +
                            timer.Elapsed.TotalSeconds.ToString("F1") + "s; errors=" + (summary.totalErrors + validationErrors) +
                            ", warnings=" + summary.totalWarnings + ". Report: " + reportPath;
            if (result != BuildResult.Succeeded.ToString())
            {
                Debug.LogError(status);
                string detail = errors.Count > 0 ? string.Join("\n", errors) :
                    "The BuildReport contains no error messages. Result: " + summary.result + ".";
                throw new InvalidOperationException("Android prototype build failed.\n" + detail);
            }

            Debug.Log(status + "\nAPK: " + outputPath);
            return outputPath;
        }

        static void PrepareSpatialBuild(string projectRoot)
        {
            // Only save this build's scene. Unrelated open scenes are left untouched.
            var scene = SceneManager.GetSceneByPath(PrototypeScenePath);
            if (scene.IsValid() && scene.isLoaded && scene.isDirty && !EditorSceneManager.SaveScene(scene))
                throw new IOException("Could not save the Orbital Courier scene before Spatial export.");
            if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                throw new InvalidOperationException("The Orbital Courier scene is still dirty; Spatial export would skip it.");

            // P2P can leave a newer .dirty.glb while the APK references the older normal GLB.
            // Invalidate only these two known scene exports; the SDK rebuilds the normal file.
            string tempRoot = Path.GetFullPath(Path.Combine(projectRoot, "Temp")) + Path.DirectorySeparatorChar;
            string exportPath = Path.GetFullPath(Path.Combine(tempRoot, "SpatialAdapterExport", PrototypeScenePath + ".glb"));
            InvalidateSceneExport(exportPath, tempRoot);
            InvalidateSceneExport(Path.ChangeExtension(exportPath, ".dirty.glb"), tempRoot);
        }

        static void InvalidateSceneExport(string path, string tempRoot)
        {
            string absolutePath = Path.GetFullPath(path);
            if (!absolutePath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to invalidate an export outside this project's Temp directory.");
            if (File.Exists(absolutePath)) File.Delete(absolutePath);
        }

        static void InvalidateGeneratedGradleArchive(string projectRoot)
        {
            // AGP can preserve free ZIP ranges after large Spatial payloads shrink.
            // Recreate only its generated debug archive; keep all compilation caches
            // and preserve the previous reviewable APK below Builds/SpaceBall.
            string libraryRoot = Path.GetFullPath(Path.Combine(projectRoot, "Library")) + Path.DirectorySeparatorChar;
            string archive = Path.GetFullPath(Path.Combine(libraryRoot, "Bee", "Android", "Prj", "IL2CPP", "Gradle",
                "launcher", "build", "outputs", "apk", "debug", "launcher-debug.apk"));
            if (!archive.StartsWith(libraryRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Generated Android archive escaped the project Library directory.");
            if (File.Exists(archive)) File.Delete(archive);
        }

        sealed class SpatialBundleCompatibilityScope : IDisposable
        {
            readonly SpatialAdapterExporterSettings settings;
            readonly bool originalBundleSetting;

            public SpatialBundleCompatibilityScope()
            {
                if (SpatialBundleCompatibilityActive)
                    throw new InvalidOperationException("A SpaceBall Spatial bundle build is already active.");
                if (Application.platform != RuntimePlatform.WindowsEditor)
                    throw new PlatformNotSupportedException("This Spatial bundle compatibility hook is for Windows Editor.");
                settings = AssetDatabase.LoadAssetAtPath<SpatialAdapterExporterSettings>(SpatialAdapterExporterSettings.k_SettingsPath);
                if (settings == null)
                    throw new InvalidOperationException("PICO Spatial exporter settings are missing.");
                var serialized = new SerializedObject(settings);
                var property = serialized.FindProperty("m_exportAssetBundle");
                if (property == null)
                    throw new InvalidOperationException("The installed Spatial Adapter has no asset bundle export setting.");
                originalBundleSetting = property.boolValue;
                try
                {
                    SetBundleSetting(false);
                    SpatialBundleCompatibilityActive = true;
                }
                catch
                {
                    SetBundleSetting(originalBundleSetting);
                    throw;
                }
            }

            public void Dispose()
            {
                SpatialBundleCompatibilityActive = false;
                SetBundleSetting(originalBundleSetting);
            }

            void SetBundleSetting(bool enabled)
            {
                var serialized = new SerializedObject(settings);
                serialized.Update();
                var property = serialized.FindProperty("m_exportAssetBundle");
                if (property == null) throw new InvalidOperationException("Spatial bundle setting disappeared during the build.");
                property.boolValue = enabled;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssetIfDirty(settings);
            }
        }

        internal static void PackSpatialBundle()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string exportRoot = Path.GetFullPath(Path.Combine(projectRoot, "Temp", "SpatialAdapterExport"));
            string sceneExport = Path.Combine(exportRoot, PrototypeScenePath + ".glb");
            if (!File.Exists(sceneExport) || new FileInfo(sceneExport).Length == 0)
                throw new BuildFailedException("SDK preprocessing did not export the current Orbital Courier scene.");

            OrbitalLinearColorExport.Restore(sceneExport);
            string[] materialExports = Directory.GetFiles(Path.Combine(exportRoot, "Assets", "SpaceBall", "Materials"), "*.mat.glb");
            foreach (string material in materialExports)
                OrbitalLinearColorExport.Restore(material);
            OrbitalLinearOcclusionExport.Restore(sceneExport);
            foreach (string material in materialExports)
                if (OrbitalLinearOcclusionExport.IsLunarMaterialFile(material))
                    OrbitalLinearOcclusionExport.Restore(material);
            OrbitalGlbTextureDeduplicator.Optimize(sceneExport,
                Path.Combine(projectRoot, "Builds", "SpaceBall", "glb-texture-dedup-report.json"));

            string executable = PrepareOfficialBundleTool(projectRoot);
            string bundlePath = Path.Combine(exportRoot, "SpatialAssetBundle.bundle");
            string arguments = "pack -s " + QuotePath(exportRoot) + " -o " + QuotePath(bundlePath) +
                               " --formats glb --override-output --recursive --texture-dir " + QuotePath(exportRoot);
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var process = new Process { StartInfo = startInfo })
            {
                if (!process.Start()) throw new BuildFailedException("Could not start the official Spatial bundle tool.");
                // Drain both pipes concurrently; the SDK's sequential ReadToEnd can deadlock.
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(300000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    process.WaitForExit(5000);
                    throw new BuildFailedException("Official Spatial bundle tool timed out after five minutes.");
                }
                if (!Task.WaitAll(new Task[] { stdout, stderr }, 10000))
                    throw new BuildFailedException("Spatial bundle tool exited but its output streams did not close.");
                string output = stdout.Result;
                string error = stderr.Result;
                string logPath = Path.Combine(projectRoot, "Library", "SpaceBall", "SpatialBundle", "last-pack.log");
                File.WriteAllText(logPath, "Exit code: " + process.ExitCode + "\n" + output + "\n" + error, new UTF8Encoding(false));
                if (process.ExitCode != 0)
                    throw new BuildFailedException("Official Spatial bundle tool failed with exit code " + process.ExitCode +
                                                   ". Details: " + logPath + "\n" + Tail(error, 2000));
                Debug.Log("Official Spatial bundle tool completed. Log: " + logPath);
            }

            if (!File.Exists(bundlePath) || new FileInfo(bundlePath).Length == 0)
                throw new BuildFailedException("Official Spatial bundle tool produced no nonempty bundle.");
            string streamingFolder = Path.Combine(Application.streamingAssetsPath, "SpatialAdapterExport");
            Directory.CreateDirectory(streamingFolder);
            File.Copy(bundlePath, Path.Combine(streamingFolder, "SpatialAssetBundle.bundle"), true);
            // ExportAll already copied the loose GLB before this order-1 hook.
            // Synchronize it too: the APK must never retain the pre-dedup copy.
            string streamingScene = Path.Combine(streamingFolder, PrototypeScenePath + ".glb");
            Directory.CreateDirectory(Path.GetDirectoryName(streamingScene));
            File.Copy(sceneExport, streamingScene, true);
            foreach (string material in materialExports)
            {
                string destination = Path.Combine(streamingFolder, "Assets", "SpaceBall", "Materials", Path.GetFileName(material));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(material, destination, true);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        static string PrepareOfficialBundleTool(string projectRoot)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SpatialAdapterExporterSettings).Assembly);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath))
                throw new BuildFailedException("Cannot locate the installed PICO Spatial Adapter package.");
            string zipPath = Path.GetFullPath(Path.Combine(package.resolvedPath, "Tools~", "SpatialBundle.zip"));
            if (!File.Exists(zipPath)) throw new BuildFailedException("The installed SDK has no official SpatialBundle.zip.");
            var archiveInfo = new FileInfo(zipPath);
            string identity = zipPath + "|" + archiveInfo.Length + "|" + archiveInfo.LastWriteTimeUtc.Ticks;
            string versionKey;
            using (var hash = SHA256.Create())
                versionKey = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 16);
            string cacheRoot = Path.GetFullPath(Path.Combine(projectRoot, "Library", "SpaceBall", "SpatialBundle", versionKey));
            string cachePrefix = cacheRoot + Path.DirectorySeparatorChar;
            string executable = Path.Combine(cacheRoot, "win", "spatialbundle.exe");
            string marker = Path.Combine(cacheRoot, "extraction-complete.txt");
            if (File.Exists(marker) && File.Exists(executable)) return executable;

            Directory.CreateDirectory(cacheRoot);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("win/", StringComparison.Ordinal)) continue;
                    string destination = Path.GetFullPath(Path.Combine(cacheRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("SDK archive entry would escape the bundle tool cache.");
                    if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(destination);
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        using var source = entry.Open();
                        using var target = File.Create(destination);
                        source.CopyTo(target);
                    }
                }
            }
            if (!File.Exists(executable)) throw new BuildFailedException("SDK archive contains no Windows Spatial bundle executable.");
            File.WriteAllText(marker, identity, new UTF8Encoding(false));
            return executable;
        }

        static string QuotePath(string path)
        {
            if (path.IndexOf('"') >= 0) throw new ArgumentException("Invalid quote in tool path.");
            return "\"" + path + "\"";
        }

        static string Tail(string value, int length) => value.Length <= length ? value : value.Substring(value.Length - length);

        static long ValidateSpatialBundle(string apkPath)
        {
            using var stream = File.OpenRead(apkPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var bundle = archive.GetEntry(SpatialBundleEntry);
            if (bundle == null || bundle.Length == 0)
                throw new InvalidDataException("APK is missing a nonempty " + SpatialBundleEntry + ".");
            string sceneEntry = "assets/SpatialAdapterExport/" + PrototypeScenePath + ".glb";
            var packagedScene = archive.GetEntry(sceneEntry);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string exportedScene = Path.Combine(projectRoot, "Temp", "SpatialAdapterExport", PrototypeScenePath + ".glb");
            if (packagedScene == null || packagedScene.Length != new FileInfo(exportedScene).Length)
                throw new InvalidDataException("APK loose scene is missing or differs from the optimized scene export.");
            using var expected = File.OpenRead(exportedScene);
            using var actual = packagedScene.Open();
            using var sha = SHA256.Create();
            string expectedHash = BitConverter.ToString(sha.ComputeHash(expected));
            string actualHash = BitConverter.ToString(sha.ComputeHash(actual));
            if (!string.Equals(expectedHash, actualHash, StringComparison.Ordinal))
                throw new InvalidDataException("APK loose scene SHA256 differs from the optimized scene export.");
            string dedupReport = Path.Combine(projectRoot, "Builds", "SpaceBall", "glb-texture-dedup-report.json");
            string optimizedHash = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(dedupReport))
                .Value<string>("afterSha256");
            if (!string.Equals(optimizedHash, expectedHash.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The SDK overwrote the optimized GLB after packing.");
            string expectedBundle = Path.Combine(projectRoot, "Temp", "SpatialAdapterExport", "SpatialAssetBundle.bundle");
            using var expectedBundleStream = File.OpenRead(expectedBundle);
            using var actualBundleStream = bundle.Open();
            string expectedBundleHash = BitConverter.ToString(sha.ComputeHash(expectedBundleStream));
            string actualBundleHash = BitConverter.ToString(sha.ComputeHash(actualBundleStream));
            if (!string.Equals(expectedBundleHash, actualBundleHash, StringComparison.Ordinal))
                throw new InvalidDataException("APK Spatial bundle SHA256 differs from the freshly packed bundle.");
            return bundle.Length;
        }

        static List<string> CollectBuildErrors(BuildReport report)
        {
            var errors = new List<string>();
            foreach (var step in report.steps)
            {
                foreach (var message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception ||
                        message.type == LogType.Assert)
                        errors.Add(step.name + ": " + message.content);
                }
            }

            return errors;
        }

        static void WriteReport(string path, LocalBuildReport report)
        {
            File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
        }

        [Serializable]
        sealed class LocalBuildReport
        {
            public string output;
            public string applicationIdentifier;
            public string status;
            public long apkSizeBytes;
            public long spatialBundleSizeBytes;
            public LocalBuildSummary summary;
            public string[] errors;
        }

        [Serializable]
        sealed class LocalBuildSummary
        {
            public double elapsedSeconds;
            public double buildSeconds;
            public int totalErrors;
            public int totalWarnings;
            public ulong sizeBytes;
        }
    }

    // The SDK exporter has order 0. Pack only this wrapper's already-exported scene data.
    sealed class OrbitalSpatialBundlePreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 1;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!OrbitalAndroidBuild.SpatialBundleCompatibilityActive || report.summary.platform != BuildTarget.Android) return;
            OrbitalAndroidBuild.PackSpatialBundle();
        }
    }
}
