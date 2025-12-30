using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;

namespace Balancy
{
    public class AssetsRuntime
    {
#if (UNITY_IPHONE || UNITY_WEBGL) && !UNITY_EDITOR
        private const string DllName = "__Internal";
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        private const string DllName = "libBalancyCore";
        //private const string DllName = "Assets/Balancy/Plugins/Windows/x86_64/libBalancyCore";
#else
        private const string DllName = "libBalancyCore";
#endif
        
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        
        public static extern IntPtr balancyGetAddressablesUrl(int devicePlatform);
        
        private class LoadedObject
        {
            private readonly Object _originalObject;

            public LoadedObject(Object originalObject)
            {
                _originalObject = originalObject;
            }

            public Object GetObject()
            {
                return _originalObject;
            }
        }

        private static readonly Transform m_PoolHolder;

        private class TypedRequests
        {
            public readonly Dictionary<string, LoadedObject> LoadedObjects = new Dictionary<string, LoadedObject>();
            public readonly Dictionary<string, List<Action<Object>>> LoadingQueue = new Dictionary<string, List<Action<Object>>>();    
        }
        
        private static readonly Dictionary<string, TypedRequests> _typedRequests = new Dictionary<string, TypedRequests>();
        
        [RuntimeInitializeOnLoadMethod]
        public static void Init()
        {
            _typedRequests.Clear();
            Balancy.Controller.OnDataUpdated -= PrepareAddresses;
            Balancy.Controller.OnDataUpdated += PrepareAddresses;
            Balancy.Models.UnnyObject.OnLoadAssetAsSprite = GetSprite;
            Balancy.Models.UnnyObject.OnLoadAssetAsObject = GetObject;
        }

        private static void PrepareAddresses(bool dataUpdated, bool profileChanged)
        {
            var url = GetAddressablesUrl();
            if (string.IsNullOrEmpty(url))
                return;

            UnityEngine.AddressableAssets.Addressables.InternalIdTransformFunc = (location) => {
                string id = location.InternalId;
                if (id.StartsWith("BALANCY_URL"))
                {
                    var newId = id.Replace("//", "/").Replace("BALANCY_URL", url);
                    return newId;
                }

                return id;
            };
        }

        private static string GetAddressablesUrl()
        {
            var devicePlatform = Balancy.Controller.GetDevicePlatform();
            var urlPtr = balancyGetAddressablesUrl((int)devicePlatform);
            return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(urlPtr);
        }

        private static Balancy.Constants.DevicePlatform ConvertRuntimePlatformToDevicePlatform(UnityEngine.RuntimePlatform target)
        {
            switch (target)
            {
                case UnityEngine.RuntimePlatform.WindowsEditor:
                    return Balancy.Constants.DevicePlatform.WindowsPlayer;
                case UnityEngine.RuntimePlatform.OSXEditor:
                    return Balancy.Constants.DevicePlatform.OSXPlayer;
                case UnityEngine.RuntimePlatform.LinuxEditor:
                    return Balancy.Constants.DevicePlatform.LinuxPlayer;
                default:
                    return (Balancy.Constants.DevicePlatform)(int)target;
            }
        }

        public static AsyncLoadHandler GetSprite(string name, Action<Sprite> callback)
        {
            // Try to load as Sprite first
            return GetAsset<Sprite>(name, sprite =>
            {
                if (sprite != null)
                {
                    callback?.Invoke(sprite);
                }
                else
                {
                    // Fallback: Try loading as Texture2D and convert to Sprite
                    Debug.Log($"Failed to load as Sprite, trying Texture2D for: {name}");
                    GetAsset<Texture2D>(name, texture =>
                    {
                        if (texture != null)
                        {
                            // Convert Texture2D to Sprite
                            var convertedSprite = Sprite.Create(
                                texture,
                                new Rect(0, 0, texture.width, texture.height),
                                new Vector2(0.5f, 0.5f)
                            );
                            callback?.Invoke(convertedSprite);
                        }
                        else
                        {
                            callback?.Invoke(null);
                        }
                    });
                }
            });
        }
        
        public static AsyncLoadHandler GetObject(string name, Action<Object> callback)
        {
            return GetAsset<Object>(name, callback);
        }
        
        public static AsyncLoadHandler GetAsset<T>(string name, Action<T> callback) where T : Object
        {
            var handler = AsyncLoadHandler.CreateHandler();
            CheckAndPrepareObject<T>(name, o =>
            {
                if (handler.GetStatus() == AsyncLoadHandler.Status.Loading)
                {
                    handler.Finish();
                    callback(o as T);
                }
            });
            return handler;
        }
        
        private static void CheckAndPrepareObject<T>(string name, Action<Object> callback) where T : Object
        {
            var typeString = typeof(T).ToString();
            if (!_typedRequests.TryGetValue(typeString, out var typedRequests))
            {
                typedRequests = new TypedRequests();
                _typedRequests.Add(typeString, typedRequests);
            }
            
            if (typedRequests.LoadedObjects.TryGetValue(name, out var value))
            {
                callback?.Invoke(value?.GetObject());
            }
            else
            {
                if (typedRequests.LoadingQueue.TryGetValue(name, out var queue))
                {
                    queue?.Add(callback);
                }
                else
                {
                    var newQueue = new List<Action<Object>> {callback};
                    typedRequests.LoadingQueue.Add(name, newQueue);

                    bool completed = false;
                    void invokeCallbacksAndCleanUp(Object obj)
                    {
                        completed = true;
                        foreach (var action in newQueue)
                            action(obj);

                        typedRequests.LoadingQueue.Remove(name);
                    }
                    
                    UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<T>(name).Completed += result =>
                    {
                        if (completed)
                            return;
                        
                        Object obj = null;
                        if (result.Status == AsyncOperationStatus.Succeeded)
                        {
                            var loadedObject = new LoadedObject(result.Result);
                            typedRequests.LoadedObjects.Add(name, loadedObject);
                            obj = loadedObject.GetObject();
                        }
                        else
                        {
                            Debug.LogError("Couldn't load asset by name " + name);
                        }

                        invokeCallbacksAndCleanUp(obj);
                    };

                    Tasks.Wait(20, () =>
                    {
                        if (!completed)
                        {
                            Debug.LogError("[Timeout] Couldn't load asset by name " + name);
                            invokeCallbacksAndCleanUp(null);
                        }
                    });
                }
            }
        }
    }
}
