using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SpaceBall.Editor
{
    // Actual scene render, with temporary visibility/camera changes restored afterwards.
    // This does not represent the PICO compositor or a native device acceptance test.
    public static class OrbitalArtPreview
    {
        public static string Capture(string label, bool close = false)
        {
            var prototype = UnityEngine.Object.FindFirstObjectByType<OrbitalPrototype>();
            bool playing = Application.isPlaying;
            if (playing && prototype.State.Level != OrbitalLevel.Moon)
                throw new InvalidOperationException("Select the Moon in a protected preview before capturing Play mode.");
            var levels = prototype.GetComponentsInChildren<OrbitalPresentation>(true);
            bool[] enabled = levels.Select(item => item.gameObject.activeSelf).ToArray();
            var original = prototype.previewCamera;
            var cameraObject = new GameObject("Temporary art review camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(original);
            var target = new RenderTexture(1800, 1200, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                if (!playing)
                    for (int i = 0; i < levels.Length; i++) levels[i].gameObject.SetActive(i == 1);
                camera.transform.position = close ? new Vector3(.28f, .43f, -.63f) : new Vector3(.65f, .82f, -1.31f);
                camera.transform.LookAt(close ? new Vector3(.035f, .035f, -.10f) : new Vector3(0, .18f, .015f));
                camera.fieldOfView = close ? 42 : 46;
                camera.aspect = 1.5f;
                camera.targetTexture = target;
                camera.enabled = false;
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                string folder = Path.GetFullPath("Builds/SpaceBall/Art-Hero-Slice");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, label + (close ? "-detail" : "-full") + ".png");
                File.WriteAllBytes(path, pixels.EncodeToPNG());
                return path;
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target);
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (!playing)
                    for (int i = 0; i < levels.Length; i++) levels[i].gameObject.SetActive(enabled[i]);
            }
        }
    }
}
