using UnityEditor;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;
using CustomAvatars;
using System.IO;
using System.Drawing;
using UnityEditor.Animations;

[CustomEditor(typeof(AvatarDescriptor))]
public class AvatarDescriptorEditor : Editor
{
    SerializedProperty avatarPrefabProp;
    SerializedProperty swapOriginalMeshProp;
    SerializedProperty playerShaderSlotsProp;
    SerializedProperty bodyShaderSlotProp;
    SerializedProperty parametersProp;
    SerializedProperty eyeSettingsProp;
    SerializedProperty jawOpenBlendshapeProp;
    SerializedProperty defaultBlendshapesProp;

    void OnEnable()
    {
        avatarPrefabProp = serializedObject.FindProperty("avatarPrefab");
        // animatorControllerProp = serializedObject.FindProperty("animatorController");
        // animatorControllerNameProp = serializedObject.FindProperty("animatorControllerName");
        eyeSettingsProp = serializedObject.FindProperty("eyeSettings");
        swapOriginalMeshProp = serializedObject.FindProperty("swapOriginalMesh");
        playerShaderSlotsProp = serializedObject.FindProperty("playerShaderSlots");
        bodyShaderSlotProp = serializedObject.FindProperty("bodyShaderSlot");
        parametersProp = serializedObject.FindProperty("parameters");
        jawOpenBlendshapeProp = serializedObject.FindProperty("jawOpenBlendshape");
        defaultBlendshapesProp = serializedObject.FindProperty("defaultBlendshapes");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var desc = (AvatarDescriptor)target;

        EditorGUILayout.PropertyField(avatarPrefabProp, new GUIContent("Avatar Prefab"));
        GameObject prefab = avatarPrefabProp.objectReferenceValue as GameObject;

        SkinnedMeshRenderer smr = null;
        if (prefab != null)
            smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>();

        if (smr == null)
        {
            serializedObject.ApplyModifiedProperties();
            return;
        }

        EditorGUILayout.Space();
        
        swapOriginalMeshProp.boolValue = EditorGUILayout.ToggleLeft("Swap Original Mesh", swapOriginalMeshProp.boolValue);

        EditorGUILayout.Space();

        if (smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField("Player Shader Slots", EditorStyles.boldLabel);
            var mats = smr.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string matName = mats[i] ? mats[i].name : "<None>";
                bool isSelected = desc.playerShaderSlots.Contains(i);
                var prevColor = GUI.backgroundColor;

                GUI.backgroundColor = isSelected ? new UnityEngine.Color(0.3f, 1f, 0.3f, 0.3f) : UnityEngine.Color.white;
                EditorGUILayout.BeginHorizontal("box");

                var matIcon = EditorGUIUtility.IconContent("Material Icon");
                GUILayout.Label(matIcon, GUILayout.Width(20), GUILayout.Height(20));

                bool toggle = EditorGUILayout.ToggleLeft($"[{i}] {matName}", isSelected);
                if (toggle && !isSelected) desc.playerShaderSlots.Add(i);
                if (!toggle && isSelected) desc.playerShaderSlots.Remove(i);

                EditorGUILayout.EndHorizontal();
                GUI.backgroundColor = prevColor;
            }

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Body Material (Shows in Rock Cam)", EditorStyles.boldLabel);
            Material[] allMats = smr.sharedMaterials;
            List<int> slotIndices = desc.playerShaderSlots.Count > 0
                ? desc.playerShaderSlots
                : Enumerable.Range(0, allMats.Length).ToList();

            for (int i = 0; i < slotIndices.Count; i++)
            {
                int matIndex = slotIndices[i];
                string matName = allMats[matIndex] ? allMats[matIndex].name : "<None>";
                bool isSelected = desc.bodyShaderSlot == matIndex;

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = isSelected ? new UnityEngine.Color(0.3f, 1f, 0.3f, 0.3f) : UnityEngine.Color.white;

                EditorGUILayout.BeginHorizontal("box");
                var matIcon = EditorGUIUtility.IconContent("Material Icon");
                GUILayout.Label(matIcon, GUILayout.Width(20), GUILayout.Height(20));
                bool toggle = EditorGUILayout.ToggleLeft($"[{matIndex}] {matName}", isSelected);
                if (toggle && !isSelected)
                    desc.bodyShaderSlot = matIndex;
                EditorGUILayout.EndHorizontal();

                GUI.backgroundColor = prevColor;
            }

            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space();

        // EditorGUILayout.Space();
        // EditorGUILayout.BeginVertical("box");
        // EditorGUILayout.LabelField("Animator Parameters", EditorStyles.boldLabel);

        // EditorGUILayout.PropertyField(animatorControllerProp, new GUIContent("AnimatorController"));
        // AnimatorController controller = animatorControllerProp.objectReferenceValue as AnimatorController;
        // animatorControllerNameProp.stringValue = controller.name;

        // string[] paramNames = null;
        // if (controller is AnimatorController ctrl)
        //     paramNames = ctrl.parameters.Select(p => p.name).ToArray();

        // for (int i = 0; i < parametersProp.arraySize; i++)
        // {
        //     var entry = parametersProp.GetArrayElementAtIndex(i);
        //     var nameProp = entry.FindPropertyRelative("name");
        //     var typeProp = entry.FindPropertyRelative("type");
        //     var netProp = entry.FindPropertyRelative("networked");
        //     var labelProp = entry.FindPropertyRelative("uiLabel");

        //     EditorGUILayout.BeginVertical("box");

        //     EditorGUI.indentLevel++;
        //     string foldoutLabel = string.IsNullOrEmpty(nameProp.stringValue) ? "(None)" : nameProp.stringValue;
        //     entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded, foldoutLabel, true);
        //     EditorGUI.indentLevel--;

        //     if (entry.isExpanded)
        //     {
        //         EditorGUILayout.Space();

        //         if (paramNames != null && paramNames.Length > 0)
        //         {
        //             int currentIndex = Mathf.Max(0, Array.IndexOf(paramNames, nameProp.stringValue));
        //             int newIndex = EditorGUILayout.Popup("Parameter", currentIndex, paramNames);
        //             nameProp.stringValue = paramNames[newIndex];
        //         }
        //         else
        //         {
        //             EditorGUILayout.HelpBox("Animator Controller not found or has no parameters.", MessageType.Warning);
        //             nameProp.stringValue = EditorGUILayout.TextField("Parameter", nameProp.stringValue);
        //         }

        //         if (paramNames != null && paramNames.Length > 0 && controller is AnimatorController ac)
        //         {
        //             var param = ac.parameters.FirstOrDefault(p => p.name == nameProp.stringValue);
        //             if (param != null)
        //             {
        //                 EditorGUILayout.LabelField("Type", param.type.ToString());
        //             }
        //             else
        //             {
        //                 EditorGUILayout.LabelField("Type", "Unknown");
        //             }
        //         }

        //         netProp.boolValue = EditorGUILayout.Toggle("Networked", netProp.boolValue);
        //         labelProp.stringValue = EditorGUILayout.TextField("ModUI Name", labelProp.stringValue);

        //         GUILayout.Space(2);
        //         if (GUILayout.Button("Remove Parameter"))
        //         {
        //             parametersProp.DeleteArrayElementAtIndex(i);
        //             EditorGUILayout.EndVertical();
        //             break;
        //         }
        //     }

        //     EditorGUILayout.EndVertical();
        // }

        // if (GUILayout.Button("Add Parameter"))
        // {
        //     parametersProp.InsertArrayElementAtIndex(parametersProp.arraySize);
        // }

        // EditorGUILayout.EndVertical();

        if (smr.sharedMesh != null && smr.sharedMesh.blendShapeCount > 0)
        {
            var mesh = smr.sharedMesh;
            string[] blendshapeNames = Enumerable.Range(0, mesh.blendShapeCount)
                .Select(i => mesh.GetBlendShapeName(i))
                .Prepend("None")
                .ToArray();

            int DrawBlendshapePopup(string label, int currentIndex)
            {
                string currentName = currentIndex >= 0 && currentIndex < mesh.blendShapeCount
                    ? mesh.GetBlendShapeName(currentIndex)
                    : "None";

                int displayIndex = Array.IndexOf(blendshapeNames, currentName);
                int newDisplayIndex = EditorGUILayout.Popup(label, displayIndex, blendshapeNames);
                return newDisplayIndex <= 0 ? -1 : mesh.GetBlendShapeIndex(blendshapeNames[newDisplayIndex]);
            }

            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Blendshape Settings", EditorStyles.boldLabel);

            jawOpenBlendshapeProp.intValue = DrawBlendshapePopup("Jaw Open", jawOpenBlendshapeProp.intValue);

            EditorGUILayout.Space();

            var blinkTypeProp = eyeSettingsProp.FindPropertyRelative("blinkType");
            var blinkProp = eyeSettingsProp.FindPropertyRelative("blinkBlendshape");
            var blinkLeftProp = eyeSettingsProp.FindPropertyRelative("blinkLeftBlendshape");
            var blinkRightProp = eyeSettingsProp.FindPropertyRelative("blinkRightBlendshape");
            var upProp = eyeSettingsProp.FindPropertyRelative("eyeUpBlendshape");
            var downProp = eyeSettingsProp.FindPropertyRelative("eyeDownBlendshape");
            var leftProp = eyeSettingsProp.FindPropertyRelative("eyeLeftBlendshape");
            var rightProp = eyeSettingsProp.FindPropertyRelative("eyeRightBlendshape");
            var gainProp = eyeSettingsProp.FindPropertyRelative("eyeGain");
            var intervalProp = eyeSettingsProp.FindPropertyRelative("blinkInterval");
            var blinkSpeedProp = eyeSettingsProp.FindPropertyRelative("blinkSpeed");

            EditorGUILayout.PropertyField(blinkTypeProp, new GUIContent("Blink Type"));
            var blinkType = (AvatarDescriptor.BlinkType)blinkTypeProp.enumValueIndex;

            switch (blinkType)
            {
                case AvatarDescriptor.BlinkType.Single:
                    blinkProp.intValue = DrawBlendshapePopup("Blink", blinkProp.intValue);
                    break;
                case AvatarDescriptor.BlinkType.LeftRight:
                    blinkLeftProp.intValue = DrawBlendshapePopup("Blink Left", blinkLeftProp.intValue);
                    blinkRightProp.intValue = DrawBlendshapePopup("Blink Right", blinkRightProp.intValue);
                    break;
            }

            bool hasBlink = (blinkType == AvatarDescriptor.BlinkType.Single && blinkProp.intValue >= 0)
                    || (blinkType == AvatarDescriptor.BlinkType.LeftRight &&
                        blinkLeftProp.intValue >= 0 && blinkRightProp.intValue >= 0);

            if (hasBlink)
            {
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(intervalProp);
                EditorGUILayout.PropertyField(blinkSpeedProp);
            }

            EditorGUILayout.Space();

            upProp.intValue = DrawBlendshapePopup("Eye Up", upProp.intValue);
            downProp.intValue = DrawBlendshapePopup("Eye Down", downProp.intValue);
            leftProp.intValue = DrawBlendshapePopup("Eye Left", leftProp.intValue);
            rightProp.intValue = DrawBlendshapePopup("Eye Right", rightProp.intValue);

            gainProp.floatValue = EditorGUILayout.FloatField("Eye Movement Intensity", gainProp.floatValue);

    

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Default Blendshapes", EditorStyles.boldLabel);

            SerializedProperty initFlag = serializedObject.FindProperty("blendshapesInitialized");

            if (!initFlag.boolValue && smr != null)
            {
                initFlag.boolValue = true;

                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string shapeName = smr.sharedMesh.GetBlendShapeName(i);
                    float weight = smr.GetBlendShapeWeight(i);

                    bool alreadyExists = false;
                    for (int j = 0; j < defaultBlendshapesProp.arraySize; j++)
                    {
                        var entry = defaultBlendshapesProp.GetArrayElementAtIndex(j);
                        if (entry.FindPropertyRelative("name").stringValue == shapeName)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (!alreadyExists && weight != 0f)
                    {
                        defaultBlendshapesProp.InsertArrayElementAtIndex(defaultBlendshapesProp.arraySize);
                        var newEntry = defaultBlendshapesProp.GetArrayElementAtIndex(defaultBlendshapesProp.arraySize - 1);
                        newEntry.FindPropertyRelative("name").stringValue = shapeName;
                        newEntry.FindPropertyRelative("index").intValue = i;
                        newEntry.FindPropertyRelative("weight").floatValue = weight;
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }

            for (int i = 0; i < defaultBlendshapesProp.arraySize; i++)
            {
                var entry = defaultBlendshapesProp.GetArrayElementAtIndex(i);
                var nameProp = entry.FindPropertyRelative("name");
                var indexProp = entry.FindPropertyRelative("index");
                var weightProp = entry.FindPropertyRelative("weight");

                EditorGUILayout.BeginHorizontal();

                int selectedIndex = Array.IndexOf(blendshapeNames, nameProp.stringValue);
                selectedIndex = EditorGUILayout.Popup(selectedIndex, blendshapeNames);
                nameProp.stringValue = (selectedIndex >= 0) ? blendshapeNames[selectedIndex] : string.Empty;

                weightProp.floatValue = EditorGUILayout.Slider(weightProp.floatValue, 0f, 100f);

                if (smr != null && nameProp.stringValue != "None")
                    indexProp.intValue = smr.sharedMesh.GetBlendShapeIndex(nameProp.stringValue);

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    defaultBlendshapesProp.DeleteArrayElementAtIndex(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("Add Blendshape"))
            {
                defaultBlendshapesProp.InsertArrayElementAtIndex(defaultBlendshapesProp.arraySize);
                var newEntry = defaultBlendshapesProp.GetArrayElementAtIndex(defaultBlendshapesProp.arraySize - 1);
                newEntry.FindPropertyRelative("name").stringValue = "None";
                newEntry.FindPropertyRelative("weight").floatValue = 0f;
            }

            EditorGUILayout.EndVertical();

            if (smr.sharedMaterials.Length > 1)
            {
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                IssueBox("Avatar has more than one material. Keep in mind only one material can be shown in RockCam.", MessageType.Warning);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    public static void BuildConfig(AvatarDescriptor descriptor)
    {
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        foreach (string path in allAssetPaths)
        {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer != null && !string.IsNullOrEmpty(importer.assetBundleName))
            {
                importer.assetBundleName = null;
            }
        }

        string dirPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(descriptor));
        if (!string.IsNullOrEmpty(dirPath))
        {
            string jsonPath = Path.Combine(dirPath, "Config.json");

            string json = JsonUtility.ToJson(descriptor, true);
            File.WriteAllText(jsonPath, json);

            AssetDatabase.ImportAsset(jsonPath);

            TextAsset jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (jsonAsset != null)
            {
                var importer = AssetImporter.GetAtPath(jsonPath);
                if (importer != null)
                {
                    importer.assetBundleName = "rig.rumbleavatar";
                    AssetDatabase.SaveAssets();
                }
            }
            else
            {
                Debug.LogError("Failed to load Config.json as TextAsset.");
            }

            var avatarImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(descriptor.avatarPrefab));
            if (avatarImporter != null)
            {
                avatarImporter.assetBundleName = "rig.rumbleavatar";
                AssetDatabase.SaveAssets();
            }

            // var animatorImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(descriptor.animatorController));
            // if (animatorImporter != null)
            // {
            //     animatorImporter.assetBundleName = "rig.rumbleavatar";
            //     AssetDatabase.SaveAssets();
            // }
        }
    }

    public static void IssueBox(string message, MessageType type)
    {
        Texture icon = EditorGUIUtility.IconContent(
            type == MessageType.Error   ? "console.erroricon" :
            type == MessageType.Warning ? "console.warnicon" :
                                        "console.infoicon"
        ).image;

        GUIStyle box = new GUIStyle("box")
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(6, 6, 6, 6),
            stretchWidth = false
        };

        GUIStyle label = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true
        };

        Vector2 textSize = EditorStyles.label.CalcSize(new GUIContent(message));
        float boxWidth = Mathf.Min(textSize.x + 48, 500);

        GUILayout.BeginHorizontal(box, GUILayout.Width(boxWidth), GUILayout.Height(40));
        {
            GUILayout.Label(icon, GUILayout.Width(32), GUILayout.Height(32));
            GUILayout.Label(message, label, GUILayout.ExpandWidth(false));
        }
        GUILayout.EndHorizontal();
    }
}
