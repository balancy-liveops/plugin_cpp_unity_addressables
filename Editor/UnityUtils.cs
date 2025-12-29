#if UNITY_EDITOR && !BALANCY_SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Balancy.Editor
{
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

                string stringBody = JsonConvert.SerializeObject(requestData.Body);
                var body = Encoding.UTF8.GetBytes(stringBody);

                request.uploadHandler = new UploadHandlerRaw(body);
                return request;
            }

            return new UnityWebRequest(requestData.Url, requestData.GetMethod());
        }
    }
}
#endif
