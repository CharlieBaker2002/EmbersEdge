using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using TMPro;

public static class SM
{
    const string SAVES_PATH = "/saves";

    public static void Save<T>(T obj, string key)
    {
        BinaryFormatter form = new BinaryFormatter();
        string p = Application.persistentDataPath + SAVES_PATH;
        Directory.CreateDirectory(p);
        FileStream stream = new FileStream(p + "/" + key, FileMode.Create);
        form.Serialize(stream, obj);
        stream.Close();
    }

    public static T Load<T>(string key)
    {
        BinaryFormatter form = new BinaryFormatter();
        string p = Application.persistentDataPath + SAVES_PATH + "/" + key;
        T data = default;
        if (File.Exists(p))
        {
            FileStream stream = new FileStream(p, FileMode.Open);
            data = (T)form.Deserialize(stream);
            stream.Close();
        }
        else
        {
            Debug.LogError("No object at path: " + p);
        }
        return data;
    }

    public static bool HasFile(string key)
    {
        string p = Application.persistentDataPath + SAVES_PATH + "/" + key;
        if (File.Exists(p))
        {
            return true;
        }
        return false;
    }

}
