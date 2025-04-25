using UnityEngine;
using UnityEditor;
using System.IO;
using UnityEditor.VersionControl;

public static class AutomationBPConverter
{
    private const string SAVE_FOLDER_PATH = "Assets/ConvertedMechanismSOs";
    
    [MenuItem("Tools/AddColToBuilding")]
    public static void AddBuildingCol()
    {
        if (!Directory.Exists(SAVE_FOLDER_PATH))
        {
            Directory.CreateDirectory(SAVE_FOLDER_PATH);
        }
        Object[] selectedObjects = Selection.objects;
        foreach (Object obj in selectedObjects)
        {
            if (obj is Building b)
            {
                if (b.physic != null)
                {
                    b.col = b.physic.GetComponent<BoxCollider2D>();
                }
            }
            EditorUtility.SetDirty(obj);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/AddPartPart")]
    public static void AddPartPart()
    {
        // Ensure that the target folder exists, or create it
        if (!Directory.Exists(SAVE_FOLDER_PATH))
        {
            Directory.CreateDirectory(SAVE_FOLDER_PATH);
        }

        // Get currently selected objects
        Object[] selectedObjects = Selection.objects;

        foreach (Object obj in selectedObjects)
        {
            // We only want to process if the selected object is an AutomationBP
            if (obj is MechanismSO b)
            {
                b.p = b.g.GetComponent<Part>();
            }
            EditorUtility.SetDirty(obj);
        }

        // Save and refresh so new assets are visible in the Project window
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/ConvertBPTOMechanisms")]
    public static void ConvertBPToMechanismSO()
    {
        // Ensure that the target folder exists, or create it
        if (!Directory.Exists(SAVE_FOLDER_PATH))
        {
            Directory.CreateDirectory(SAVE_FOLDER_PATH);
        }

        // Get currently selected objects
        Object[] selectedObjects = Selection.objects;

        foreach (Object obj in selectedObjects)
        {
            // We only want to process if the selected object is an AutomationBP
            if (obj is Blueprint automationBP)
            {
                // Create a new MechanismSO instance
                MechanismSO newMechanism = ScriptableObject.CreateInstance<MechanismSO>();

                // Copy fields from the AutomationBP (which also inherits from MechanismSO)
                // Copy everything you consider relevant for the new MechanismSO
                newMechanism.s          = automationBP.s;
                newMechanism.g          = automationBP.g;
                newMechanism.cost       = automationBP.cost;
                newMechanism.shopCost   = automationBP.shopCost;
                newMechanism.description = automationBP.description;
                newMechanism.unique     = automationBP.unique;
                newMechanism.relevents  = automationBP.relevents;
                newMechanism.classifier = automationBP.classifier;
                newMechanism.p = newMechanism.g.GetComponent<Part>();
                // Optionally copy over other relevant MechanismSO fields
                // e.g., newMechanism.continual = automationBP.continual;
                // e.g., newMechanism.p = automationBP.p;

                // Give the new MechanismSO a name that is distinct
                newMechanism.name = automationBP.name;
                
                // Generate a unique asset path to avoid conflicts
                string assetPath =
                    AssetDatabase.GenerateUniqueAssetPath($"{SAVE_FOLDER_PATH}/{newMechanism.name}.asset");

                // Create and save the new asset
                AssetDatabase.CreateAsset(newMechanism, assetPath);
                Debug.Log($"Converted AutomationBP '{automationBP.name}' to MechanismSO at: {assetPath}");
            }
        }

        // Save and refresh so new assets are visible in the Project window
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
