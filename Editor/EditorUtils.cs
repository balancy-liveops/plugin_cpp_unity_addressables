#if UNITY_EDITOR && !BALANCY_SERVER
using System.Collections.Generic;
using UnityEngine;

namespace Balancy.Editor
{
    public class AddressablesEditorUtils
    {
        public class ServerRequest : AddressablesUnityUtils.IUnityRequestInfo
        {
            public string Url { get; }
            public Dictionary<string, string> Headers { get; }
            public Dictionary<string, object> Body { get; }
            public bool IsMultipart { get; set; }
            public List<AddressablesUnityUtils.ImageInfo> Images { get; private set;}
            public string ImageKey { get; private set;}
            public List<AddressablesUnityUtils.FileInfo> Files { get; private set;}

            public string Method;

            public string GetMethod()
            {
                return Method;
            }

            public ServerRequest(string fullUrl, bool isPost)
            {
                Url = fullUrl;
                Headers = new Dictionary<string, string>();
                Body = isPost ? new Dictionary<string, object>() : null;
                Method = isPost ? "POST" : "GET";
            }

            public ServerRequest SetHeader(string key, string value)
            {
                if (Headers.ContainsKey(key))
                    Headers[key] = value;
                else
                    Headers.Add(key, value);
                return this;
            }

            public ServerRequest AddBody(string key, object value)
            {
                Body.Add(key, value);
                return this;
            }

            public ServerRequest AddTexture(Texture2D img, string name, string prefabName, string imageKey = "image")
            {
                if (Images == null)
                    Images = new List<AddressablesUnityUtils.ImageInfo>();
                Images.Add(new AddressablesUnityUtils.ImageInfo(img, name, prefabName));

                ImageKey = imageKey;

                return this;
            }

            public ServerRequest AddFile(string name, string path)
            {
                if (Files == null)
                    Files = new List<AddressablesUnityUtils.FileInfo>();
                Files.Add(new AddressablesUnityUtils.FileInfo(name, path));

                return this;
            }

            public ServerRequest SetMultipart()
            {
                IsMultipart = true;

                return this;
            }
        }
    }
}
#endif
