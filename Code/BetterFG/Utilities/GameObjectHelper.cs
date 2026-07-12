using UnityEngine;
namespace BetterFG.Utilities
{
    public static class GameObjectHelper
    {
        public static bool IsLobbyCharacter(GameObject bean)
        {
            if (bean == null) return false;
            return bean.name == "LobbyCharacter";
        }
        public static bool IsUICharacter(GameObject bean)
        {
            if (bean == null) return false;
            return bean.name == "PB_UI_Character";
        }
        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            string path = obj.name;
            Transform cur = obj.transform.parent;
            while (cur != null) { path = cur.name + "/" + path; cur = cur.parent; }
            return path;
        }
        public static Transform FindChildStartingWith(Transform parent, string prefix)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null && child.name.StartsWith(prefix)) return child;
            }
            return null;
        }
        public static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            for (int i = 0; i < obj.transform.childCount; i++)
                SetLayerRecursively(obj.transform.GetChild(i).gameObject, layer);
        }
    }
}