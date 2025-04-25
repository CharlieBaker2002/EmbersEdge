using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System;

[CustomEditor(typeof(Room))]
[CanEditMultipleObjects]
public class RoomEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("EnterRoom"))
        {
            ((Room)target).OnEnter();
        }
        if (GUILayout.Button("DefeatRoom"))
        {
            ((Room)target).OnDefeat();
        }
        EditorGUILayout.LabelField("Adds light, rigidbody, box collider then sets up existing safe, sp and doors");
        if (GUILayout.Button("SetUpShortcut"))
        {
            SetUpShortcut();
        }
    }

    public void SetUpShortcut()
    {
        Room r = (Room)target;
        if(r.GetComponent<Light2D>() == null)
        {
            r.gameObject.AddComponent<Light2D>();
        }
        var l = r.GetComponent<Light2D>();
        l.lightType = Light2D.LightType.Sprite;
        l.lightCookieSprite = r.GetComponent<SpriteRenderer>().sprite;
        l.intensity = 0.25f;

        if (r.GetComponent<Rigidbody2D>() == null)
        {
            r.gameObject.AddComponent<Rigidbody2D>();
        }
        var rb = r.GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.isKinematic = true;
        rb.simulated = true;
        rb.useFullKinematicContacts = true;
        bool jAddedBox = false;
        if (r.GetComponent<BoxCollider2D>()==null)
        {
            r.gameObject.AddComponent<BoxCollider2D>().usedByComposite = true;
            jAddedBox = true;
        }
        if (r.GetComponent<CompositeCollider2D>() == null)
        {
            r.gameObject.AddComponent<CompositeCollider2D>().isTrigger = true;
        }
        r.sp = new Transform[] { };
        var box = r.GetComponent<BoxCollider2D>();
        box.usedByComposite = true;
        Rect rect = r.GetComponent<SpriteRenderer>().sprite.rect;
        rect.size *= 0.10695f;
        if (jAddedBox)
        {
            box.size = rect.size;
            Debug.Log(box.size);
        }
        r.safeSpawn = null;
        r.doors = new List<Door>();
        Transform T = r.transform.parent;
        GameObject wall = null;
        foreach(Transform t in T) //sibling gameobjects
        {
            EditorUtility.SetDirty(t.gameObject);
            if (CheckName(t, "sp")){
                Array.Resize(ref r.sp, r.sp.Length + 1);
                r.sp[r.sp.Length - 1] = t;
                t.tag = "Misc";
                continue;
            }
            else if (CheckName(t, "safe"))
            {
                r.safeSpawn = t;
                t.tag = "Misc";
                continue;
            }
            else if (CheckName(t, "door"))
            {
                t.GetComponent<Door>().room1 = r;
                r.doors.Add(t.GetComponent<Door>());
            }
            else if (CheckName(t, "wall"))
            {
                wall = t.gameObject;
            }
            else
            {
                t.tag = "Misc";
                t.gameObject.layer = LayerMask.NameToLayer("Dungeon");
                continue;
            }
            t.tag = "Walls";
        }
        if (wall != null) {
            if(wall.GetComponent<BoxCollider2D>() == null && wall.GetComponent<PolygonCollider2D>() == null)
            {
                for(int i = 0; i < 4; i++)
                {
                    wall.AddComponent<BoxCollider2D>();
                }
            }
            if (wall.GetComponent<Rigidbody2D>() == null)
            {
                wall.AddComponent<Rigidbody2D>();
            }
        }
        else
        {
            wall = new("Walls", typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(BoxCollider2D), typeof(BoxCollider2D), typeof(BoxCollider2D), typeof(PolygonCollider2D));
            wall.transform.SetParent(T);
        }
        wall.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        wall.GetComponent<Rigidbody2D>().isKinematic = true;
        wall.GetComponent<Rigidbody2D>().useFullKinematicContacts = true;
        wall.GetComponent<Rigidbody2D>().simulated = true;
        var walls = wall.GetComponents<BoxCollider2D>();
        if(walls.Length == 4 && walls[0].size == Vector2.one)
        {
            walls[0].size = new Vector2(rect.size.x, 1);
            walls[0].offset = new Vector2(0, -rect.size.y * 0.5f);
            walls[1].size = new Vector2(rect.size.x, 1);
            walls[1].offset = new Vector2(0, rect.size.y * 0.5f);
            walls[2].size = new Vector2(1, rect.size.y);
            walls[2].offset = new Vector2(-rect.size.x * 0.5f, 0);
            walls[3].size = new Vector2(1, rect.size.y);
            walls[3].offset = new Vector2(rect.size.x * 0.5f, 0);
            wall.transform.position = r.transform.position;

        }
        wall.tag = "Walls";
        wall.layer = LayerMask.NameToLayer("Walls");
        PrefabUtility.RecordPrefabInstancePropertyModifications(r);
    }

    private bool CheckName(Transform t, string s)
    {
        if (t.name.ToLower().Contains(s)){
            t.gameObject.layer = LayerMask.NameToLayer("Dungeon");
            return true;
        }
        return false;
    }
}


[CustomEditor(typeof(D2_0Room))]
[CanEditMultipleObjects]
public class RoomEdit2 : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
        if (GUILayout.Button("EnterRoom"))
        {
            ((Room)target).OnEnter();
        }
    }
}


