using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace YeastSim.EditorTools
{
    /// <summary>
    /// One-time cleanup after the move to the new simulator: removes the old prototype scripts,
    /// prefabs, scenes and shader experiments, the URP template leftovers and unused packages,
    /// and moves the cell model into Assets/YeastSimulator. Everything removed stays in git history.
    /// Menu: Yeast Simulator → Clean Up Project…
    /// </summary>
    public static class ProjectCleanup
    {
        private static readonly string[] AssetsToDelete =
        {
            // Old prototype scripts (replaced by Assets/YeastSimulator/Scripts)
            "Assets/BoneSphere.cs", "Assets/BoneSphereNew.cs", "Assets/TestBoneSphere.cs", "Assets/SoftBody.cs",
            "Assets/Budding.cs", "Assets/Growth.cs", "Assets/Force.cs", "Assets/Jitter.cs", "Assets/HideChildren.cs",
            "Assets/Contour.cs", "Assets/RecordTransformHierarchy.cs",
            // Old prefabs, scenes and recordings
            "Assets/Resources", "Assets/Scenes", "Assets/Recordings",
            "Assets/Models/BuddingYeast.prefab", "Assets/Models/BuddingYeast2.prefab",
            "Assets/Models/TestFixedJoint.prefab", "Assets/Models/MicroscopeSlide.prefab",
            "Assets/Models/microscope_slide.fbx", "Assets/Models/Stiff.preset",
            // Shader experiments and third-party samples
            "Assets/Shaders", "Assets/QuickOutline",
            // URP template leftovers
            "Assets/TutorialInfo", "Assets/Readme.asset",
        };

        private static readonly (string from, string to)[] AssetsToMove =
        {
            ("Assets/Models/budding_yeast.fbx", "Assets/YeastSimulator/Models/budding_yeast.fbx"),
            ("Assets/Models/cell_wall.mat", "Assets/YeastSimulator/Models/cell_wall.mat"),
        };

        // Source art that Unity can't import without Blender; kept in the repo, outside Assets.
        private const string BlendFile = "Assets/Models/yeast.blend";
        private const string ArtFolder = "Art";

        private static readonly string[] PackagesToRemove =
        {
            "com.unity.visualscripting", "com.unity.multiplayer.center", "com.unity.collab-proxy",
            "com.unity.ide.vscode", "com.unity.animation.rigging", "com.unity.formats.fbx",
            "com.unity.postprocessing", "com.unity.recorder",
        };

        private static AddAndRemoveRequest s_request;

        [MenuItem("Yeast Simulator/Clean Up Project…", priority = 20)]
        public static void Run()
        {
            List<string> deletions = AssetsToDelete.Where(Exists).ToList();
            List<(string from, string to)> moves = AssetsToMove.Where(m => Exists(m.from)).ToList();
            bool moveBlend = File.Exists(BlendFile);

            var summary = new StringBuilder();
            if (deletions.Count > 0) summary.AppendLine("Delete:\n  " + string.Join("\n  ", deletions));
            if (moves.Count > 0) summary.AppendLine("\nMove:\n  " + string.Join("\n  ", moves.Select(m => $"{m.from} → {m.to}")));
            if (moveBlend) summary.AppendLine($"\nMove out of Assets:\n  {BlendFile} → {ArtFolder}/yeast.blend");
            summary.AppendLine("\nRemove packages:\n  " + string.Join("\n  ", PackagesToRemove));
            summary.AppendLine("\nEverything removed stays in git history.");

            if (!EditorUtility.DisplayDialog("Clean Up Project", summary.ToString(), "Clean up", "Cancel")) return;

            // Don't delete the scene that is open right now.
            string openScene = EditorSceneManager.GetActiveScene().path;
            if (deletions.Any(d => openScene == d || openScene.StartsWith(d + "/")))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            }

            foreach ((string _, string to) in moves) EnsureFolder(Path.GetDirectoryName(to).Replace('\\', '/'));

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in deletions)
                    if (!AssetDatabase.DeleteAsset(path)) Debug.LogWarning($"[Cleanup] Could not delete {path}");

                foreach ((string from, string to) in moves)
                {
                    string error = AssetDatabase.MoveAsset(from, to);
                    if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"[Cleanup] {from}: {error}");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (moveBlend)
            {
                Directory.CreateDirectory(ArtFolder);
                string target = Path.Combine(ArtFolder, "yeast.blend");
                if (!File.Exists(target)) File.Move(BlendFile, target);
                if (File.Exists(BlendFile + ".meta")) File.Delete(BlendFile + ".meta");
            }

            if (AssetDatabase.IsValidFolder("Assets/Models") &&
                AssetDatabase.FindAssets("", new[] { "Assets/Models" }).Length == 0)
                AssetDatabase.DeleteAsset("Assets/Models");

            AssetDatabase.Refresh();

            string[] packages = PackagesToRemove.Where(IsInstalled).ToArray();
            if (packages.Length == 0)
            {
                Debug.Log("[Cleanup] Done.");
                return;
            }

            // Unity reloads scripts after removing packages, so this may be the last message.
            Debug.Log($"[Cleanup] Assets cleaned up; removing {packages.Length} unused package(s): {string.Join(", ", packages)}");
            s_request = Client.AddAndRemove(null, packages);
            EditorApplication.update += WaitForPackages;
        }

        private static void WaitForPackages()
        {
            if (s_request == null || !s_request.IsCompleted) return;
            EditorApplication.update -= WaitForPackages;
            if (s_request.Status == StatusCode.Success) Debug.Log("[Cleanup] Done.");
            else Debug.LogWarning($"[Cleanup] Removing packages failed: {s_request.Error?.message}");
            s_request = null;
        }

        private static bool Exists(string path) => AssetDatabase.IsValidFolder(path) || File.Exists(path);

        private static bool IsInstalled(string package)
        {
            string manifest = File.ReadAllText("Packages/manifest.json");
            return manifest.Contains($"\"{package}\"");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
