using System.Collections.Generic;

namespace BetterFG.Utilities
{
    internal static class JsonUtil
    {
        public static string GetValue(string json, string key)
        {
            string searchKey = $"\"{key}\":";
            int start = json.IndexOf(searchKey);
            if (start == -1) return "";
            start += searchKey.Length;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t')) start++;
            if (start >= json.Length || json[start] != '"') return "";
            start++;
            int end = json.IndexOf('"', start);
            if (end == -1) return "";
            return json.Substring(start, end - start);
        }

        public static float GetFloat(string json, string key, float def = 0f)
        {
            string sk = $"\"{key}\":";
            int i = json.IndexOf(sk);
            if (i == -1) return def;
            i += sk.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int e = i;
            while (e < json.Length && "0123456789+-.eE".IndexOf(json[e]) != -1) e++;
            if (e == i) return def;
            return float.TryParse(json.Substring(i, e - i),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        public static int GetInt(string json, string key, int def = 0)
        {
            return (int)GetFloat(json, key, def);
        }

        public static bool GetBool(string json, string key, bool def = false)
        {
            string sk = $"\"{key}\":";
            int i = json.IndexOf(sk);
            if (i == -1) return def;
            i += sk.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i + 3 < json.Length && json.Substring(i, 4) == "true") return true;
            if (i + 4 < json.Length && json.Substring(i, 5) == "false") return false;
            return def;
        }

        // returns the raw json object string (with braces) for a given key, null if not found
        public static string GetObject(string json, string key)
        {
            string sk = $"\"{key}\":";
            int ki = json.IndexOf(sk);
            if (ki == -1) return null;
            int objStart = json.IndexOf('{', ki + sk.Length);
            if (objStart == -1) return null;
            int objEnd = FindMatchingBrace(json, objStart, '{', '}');
            if (objEnd == -1) return null;
            return json.Substring(objStart, objEnd - objStart + 1);
        }

        // returns list of raw object strings from a json array under a key
        public static List<string> GetArray(string json, string key)
        {
            var result = new List<string>();
            string sk = $"\"{key}\":";
            int ki = json.IndexOf(sk);
            if (ki == -1) return result;
            int arrStart = json.IndexOf('[', ki + sk.Length);
            if (arrStart == -1) return result;
            int arrEnd = FindMatchingBrace(json, arrStart, '[', ']');
            if (arrEnd == -1) return result;
            ParseObjectsFromArray(json.Substring(arrStart + 1, arrEnd - arrStart - 1), result);
            return result;
        }

        // parses root-level array of objects
        public static List<string> GetRootArray(string json)
        {
            var result = new List<string>();
            int arrStart = json.IndexOf('[');
            if (arrStart == -1) return result;
            int arrEnd = FindMatchingBrace(json, arrStart, '[', ']');
            if (arrEnd == -1) return result;
            ParseObjectsFromArray(json.Substring(arrStart + 1, arrEnd - arrStart - 1), result);
            return result;
        }

        private static void ParseObjectsFromArray(string inner, List<string> result)
        {
            int idx = 0;
            while (idx < inner.Length)
            {
                int os = inner.IndexOf('{', idx);
                if (os == -1) break;
                int oe = FindMatchingBrace(inner, os, '{', '}');
                if (oe == -1) break;
                result.Add(inner.Substring(os, oe - os + 1));
                idx = oe + 1;
            }
        }

        private static int FindMatchingBrace(string s, int start, char open, char close)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }
    }
}