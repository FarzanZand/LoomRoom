using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Explicit migration only: never changes designer assets on ordinary imports.
[InitializeOnLoad]
public static class StaminaSetup
{
    static StaminaSetup() => EditorApplication.update += Tick;
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/StaminaSetup.request")) return;
        File.Delete("Temp/StaminaSetup.request");
        var mode = EditorSettings.serializationMode;
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            var ui = UnityEngine.Object.FindAnyObjectByType<PlayerVitalsUI>(FindObjectsInactive.Include);
            var so = new SerializedObject(ui);
            var mana = (GameObject)so.FindProperty("manaRoot").objectReferenceValue;
            if (mana == null) throw new Exception("Mana bar reference is missing");
            if (so.FindProperty("staminaBarRoot").objectReferenceValue == null)
            {
                var manaFill = (Image)so.FindProperty("manaFill").objectReferenceValue;
                var manaValue = (TMP_Text)so.FindProperty("manaValue").objectReferenceValue;
                string fillPath = AnimationUtility.CalculateTransformPath(manaFill.transform, mana.transform);
                string valuePath = AnimationUtility.CalculateTransformPath(manaValue.transform, mana.transform);
                var health = ((Image)so.FindProperty("healthFill").objectReferenceValue).transform.parent as RectTransform;
                var mr = (RectTransform)mana.transform;
                float row = Mathf.Abs(health.anchoredPosition.y - mr.anchoredPosition.y);
                if (row < 20) row = 40;
                var stamina = UnityEngine.Object.Instantiate(mana, mana.transform.parent);
                stamina.name = "Stamina bar";
                ((RectTransform)stamina.transform).anchoredPosition = mr.anchoredPosition + Vector2.up * row;
                health.anchoredPosition += Vector2.up * row;
                ((RectTransform)ui.transform).sizeDelta += Vector2.up * row;
                var fill = stamina.transform.Find(fillPath).GetComponent<Image>();
                fill.color = new Color(.35f, .66f, .57f, 1);
                foreach (var text in stamina.GetComponentsInChildren<TMP_Text>(true))
                    if (text.text.ToUpperInvariant().Contains("MANA")) text.text = "STAMINA";
                so.FindProperty("staminaBarRoot").objectReferenceValue = stamina;
                so.FindProperty("staminaBarFill").objectReferenceValue = fill;
                so.FindProperty("staminaBarValue").objectReferenceValue = stamina.transform.Find(valuePath).GetComponent<TMP_Text>();
                so.ApplyModifiedPropertiesWithoutUndo();
                const string folder = "Assets/Game/UI/Prefabs/Vitals";
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Game/UI/Prefabs", "Vitals");
                foreach (var pair in new[] { (health.gameObject, "Health bar"), (mana, "Mana bar"), (stamina, "Stamina bar") })
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + pair.Item2 + ".prefab") == null)
                        PrefabUtility.SaveAsPrefabAssetAndConnect(pair.Item1, folder + "/" + pair.Item2 + ".prefab", InteractionMode.AutomatedAction);
            }
            foreach (var panel in UnityEngine.Object.FindObjectsByType<EquipmentPanelUI>(FindObjectsInactive.Include))
            {
                if (panel.stamina != null || panel.mana == null) continue;
                var health = panel.health.rectTransform;
                float row = Mathf.Abs(health.anchoredPosition.y - panel.mana.rectTransform.anchoredPosition.y);
                if (row < 20) row = 32;
                foreach (RectTransform child in health.parent)
                    if (child != health && child.anchoredPosition.y < health.anchoredPosition.y) child.anchoredPosition -= Vector2.up * row;
                panel.stamina = UnityEngine.Object.Instantiate(panel.mana, health.parent);
                panel.stamina.name = "Stamina"; panel.stamina.text = "STAMINA  30 / 30";
                panel.stamina.rectTransform.anchoredPosition = health.anchoredPosition - Vector2.up * row;
                if (health.parent is RectTransform parent) parent.sizeDelta += Vector2.up * row;
                EditorUtility.SetDirty(panel);
            }
            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            EditorSceneManager.SaveScene(ui.gameObject.scene);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/StaminaSetup.report", "PASS: authored health/stamina/mana prefab bars and character-panel stamina saved.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/StaminaSetup.report", ex.ToString()); }
        finally { EditorSettings.serializationMode = mode; }
    }
}
