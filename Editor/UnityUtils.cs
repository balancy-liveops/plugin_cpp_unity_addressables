#if UNITY_EDITOR && !BALANCY_SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Balancy.Editor
{
    /// <summary>
    /// Simple JSON serializer/deserializer to avoid Newtonsoft.Json dependency
    /// </summary>
    public static class SimpleJsonSerializer
    {
        public static string SerializeDictionary(Dictionary<string, object> dict)
        {
            if (dict == null || dict.Count == 0)
                return "{}";

            var sb = new StringBuilder();
            sb.Append("{");

            bool first = true;
            foreach (var kvp in dict)
            {
                if (!first)
                    sb.Append(",");
                first = false;

                sb.Append("\"").Append(kvp.Key).Append("\":");
                sb.Append(SerializeValue(kvp.Value));
            }

            sb.Append("}");

            var result = sb.ToString();
            UnityEngine.Debug.Log($"[SimpleJsonSerializer] Serialized JSON: {result}");
            return result;
        }

        private static string SerializeValue(object value)
        {
            if (value == null)
                return "null";

            if (value is string str)
                return "\"" + EscapeString(str) + "\"";

            if (value is bool b)
                return b ? "true" : "false";

            if (value is int || value is long || value is float || value is double)
                return value.ToString();

            if (value is Dictionary<string, object> dict)
                return SerializeDictionary(dict);

            // Handle arrays (including arrays of custom objects)
            if (value is System.Array array)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;

                foreach (var item in array)
                {
                    if (!first)
                        sb.Append(",");
                    first = false;

                    // Check if this is a custom object that needs JsonUtility
                    if (item != null && !IsPrimitiveType(item))
                    {
                        // Use Unity's JsonUtility for complex objects
                        sb.Append(UnityEngine.JsonUtility.ToJson(item));
                    }
                    else
                    {
                        sb.Append(SerializeValue(item));
                    }
                }
                sb.Append("]");
                return sb.ToString();
            }

            // Handle Lists
            if (value is System.Collections.IList list)
            {
                var sb = new StringBuilder();
                sb.Append("[");
                bool first = true;
                foreach (var item in list)
                {
                    if (!first)
                        sb.Append(",");
                    first = false;

                    // Check if this is a custom object that needs JsonUtility
                    if (item != null && !IsPrimitiveType(item))
                    {
                        sb.Append(UnityEngine.JsonUtility.ToJson(item));
                    }
                    else
                    {
                        sb.Append(SerializeValue(item));
                    }
                }
                sb.Append("]");
                return sb.ToString();
            }

            // For custom objects, use Unity's JsonUtility
            if (!IsPrimitiveType(value))
            {
                return UnityEngine.JsonUtility.ToJson(value);
            }

            // Fallback for other types
            return "\"" + EscapeString(value.ToString()) + "\"";
        }

        private static bool IsPrimitiveType(object value)
        {
            return value is string || value is bool || value is int || value is long ||
                   value is float || value is double || value is decimal ||
                   value is byte || value is sbyte || value is short || value is ushort ||
                   value is uint || value is ulong || value is char;
        }

        private static string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        /// <summary>
        /// Simple JSON value extractor - finds "key":"value" pattern in JSON string
        /// </summary>
        public static string ExtractStringValue(string json, string key)
        {
            string pattern = $"\"{key}\":\"";
            int startIndex = json.IndexOf(pattern);
            if (startIndex == -1)
                return null;

            startIndex += pattern.Length;
            int endIndex = json.IndexOf("\"", startIndex);
            if (endIndex == -1)
                return null;

            return json.Substring(startIndex, endIndex - startIndex);
        }
    }

    public static class AddressablesUnityUtils
    {
        public class ImageInfo
        {
            public Texture2D Texture { get; }
            public string Name { get; }
            public string PrefabName { get; }

            public ImageInfo(Texture2D texture, string name, string prefabName)
            {
                Texture = texture;
                Name = name;
                PrefabName = prefabName;
            }
        }

        public class FileInfo
        {
            public string Name { get; }
            public string FullPath { get; }

            public FileInfo(string name, string path)
            {
                Name = name;
                FullPath = path;
            }
        }

        public interface IUnityRequestInfo
        {
            string Url { get; }
            Dictionary<string, string> Headers { get; }
            Dictionary<string, object> Body { get; }
            bool IsMultipart { get; }
            List<ImageInfo> Images { get; }
            string ImageKey { get; }
            List<FileInfo> Files { get; }
            string GetMethod();
        }

        public static IEnumerator SendRequest(IUnityRequestInfo requestData, Action<UnityWebRequest> doneCallback)
        {
            using (UnityWebRequest request = GetRequest(requestData))
            {
                request.downloadHandler = new DownloadHandlerBuffer();

                if (requestData.Headers != null)
                {
                    foreach (var header in requestData.Headers)
                        request.SetRequestHeader(header.Key, header.Value);
                }

                yield return request.SendWebRequest();
                doneCallback?.Invoke(request);
            }
        }

        private static UnityWebRequest GetRequest(IUnityRequestInfo requestData)
        {
            if (requestData.Body != null)
            {
                if (requestData.IsMultipart)
                {
                    WWWForm form = new WWWForm();

                    if (requestData.Images != null)
                    {
                        foreach (var image in requestData.Images)
                        {
                            byte[] bytes = image.Texture.EncodeToPNG();
                            form.AddBinaryData(requestData.ImageKey, bytes, image.Name);
                        }
                    }

                    if (requestData.Files != null)
                    {
                        foreach (var file in requestData.Files)
                        {
                            byte[] bytes = File.ReadAllBytes(file.FullPath);
                            form.AddBinaryData("file", bytes, file.Name);
                        }
                    }

                    foreach (var pm in requestData.Body)
                    {
                        form.AddField(pm.Key, pm.Value.ToString());
                    }

                    return UnityWebRequest.Post(requestData.Url, form);
                }

                var request = new UnityWebRequest(requestData.Url, requestData.GetMethod());

                string stringBody = SimpleJsonSerializer.SerializeDictionary(requestData.Body);
                var body = Encoding.UTF8.GetBytes(stringBody);

                request.uploadHandler = new UploadHandlerRaw(body);
                return request;
            }

            return new UnityWebRequest(requestData.Url, requestData.GetMethod());
        }
    }
}
#endif
