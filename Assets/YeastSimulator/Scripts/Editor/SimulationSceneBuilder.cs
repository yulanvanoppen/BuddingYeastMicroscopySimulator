using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace YeastSim.EditorTools
{
    /// <summary>
    /// Builds the ready-to-run simulation scene: simulation + recorder, a viewing camera, a light
    /// and a visible slide. Menu: Yeast Simulator → Create Simulation Scene.
    /// </summary>
    public static class SimulationSceneBuilder
    {
        private const string Root = "Assets/YeastSimulator";
        private const string ScenePath = Root + "/Scenes/Simulation.unity";
        private const string MaterialFolder = Root + "/Materials";
        private const string ShaderPath = Root + "/Shaders/CellCapture.shader";
        private const int CellLayer = 31;

        [MenuItem("Yeast Simulator/Create Simulation Scene", priority = 0)]
        public static void CreateScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Create Simulation Scene", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            GameObject model = FindCellModel();
            if (model == null)
            {
                EditorUtility.DisplayDialog("Create Simulation Scene",
                    "Could not find the cell model 'budding_yeast' (FBX) in the project.", "OK");
                return;
            }

            Shader captureShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Material cellMaterial = GetOrCreateLitMaterial("CellDisplay", new Color(0.85f, 0.85f, 0.8f), 0.7f);
            Material slideMaterial = GetOrCreateLitMaterial("Slide", new Color(0.12f, 0.13f, 0.15f), 0.2f);
            NameLayer(CellLayer, "Cells");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Light
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);

            // Viewing camera (the exported images come from a separate capture camera).
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = 50f;
            cameraObject.transform.position = new Vector3(0f, 75f, -60f);
            cameraObject.transform.LookAt(Vector3.zero);
            cameraObject.AddComponent<AudioListener>();

            // Simulation
            var simulationObject = new GameObject("Simulation");
            var simulation = simulationObject.AddComponent<SimulationManager>();
            simulationObject.AddComponent<SimulationRecorder>();

            var serializedSimulation = new SerializedObject(simulation);
            serializedSimulation.FindProperty("_cellModel").objectReferenceValue = model;
            serializedSimulation.FindProperty("_displayMaterial").objectReferenceValue = cellMaterial;
            serializedSimulation.FindProperty("_cellLayer").intValue = CellLayer;
            serializedSimulation.ApplyModifiedPropertiesWithoutUndo();

            var serializedRecorder = new SerializedObject(simulationObject.GetComponent<SimulationRecorder>());
            serializedRecorder.FindProperty("_captureShader").objectReferenceValue = captureShader;
            serializedRecorder.ApplyModifiedPropertiesWithoutUndo();

            // Visible slide (rendering only; the simulation adds its own colliders).
            float fieldSize = simulation.Microscope.FieldSize;
            GameObject slide = GameObject.CreatePrimitive(PrimitiveType.Plane);
            slide.name = "Slide (visual)";
            Object.DestroyImmediate(slide.GetComponent<Collider>());
            slide.transform.position = new Vector3(0f, -0.01f, 0f);
            slide.transform.localScale = Vector3.one * (fieldSize * 2f / 10f);   // Unity's plane is 10 units wide
            slide.GetComponent<MeshRenderer>().sharedMaterial = slideMaterial;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            if (captureShader == null)
                Debug.LogWarning($"[YeastSim] Capture shader not found at {ShaderPath}; assign it on the recorder.");
            Selection.activeObject = simulationObject;
            Debug.Log($"[YeastSim] Created {ScenePath}. Press Play to simulate and record.");
        }

        private static GameObject FindCellModel()
        {
            foreach (string guid in AssetDatabase.FindAssets("budding_yeast t:Model"))
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (model != null && model.GetComponentInChildren<SkinnedMeshRenderer>() != null) return model;
            }
            return null;
        }

        private static Material GetOrCreateLitMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = GraphicsSettings.defaultRenderPipeline != null
                ? GraphicsSettings.defaultRenderPipeline.defaultMaterial.shader
                : Shader.Find("Standard");

            material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);

            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void NameLayer(int layer, string name)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || layer >= layers.arraySize) return;
            SerializedProperty entry = layers.GetArrayElementAtIndex(layer);
            if (!string.IsNullOrEmpty(entry.stringValue)) return;
            entry.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
