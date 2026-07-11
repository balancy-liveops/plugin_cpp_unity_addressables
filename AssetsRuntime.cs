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

        private static bool _catalogReady;
        private static string _catalogUpdateUrl;
        private static List<Action> _pendingLoads;

        [RuntimeInitializeOnLoadMethod]
        public static void Init()
        {
            _typedRequests.Clear();
            _catalogReady = false;
            _catalogUpdateUrl = null;
            _pendingLoads = new List<Action>();
            _localBundleNames = null;
            Balancy.Controller.OnDataUpdated -= PrepareAddresses;
            Balancy.Controller.OnDataUpdated += PrepareAddresses;
            Balancy.Models.UnnyObject.OnLoadAssetAsSprite = GetSprite;
            Balancy.Models.UnnyObject.OnLoadAssetAsObject = GetObject;
        }

        private static HashSet<string> _localBundleNames;

        private static void CacheLocalBundles()
        {
            _localBundleNames = new HashSet<string>();
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var assets = activity.Call<AndroidJavaObject>("getAssets"))
                {
                    string[] files = assets.Call<string[]>("list", "aa/Android");
                    if (files != null)
                        foreach (var f in files)
                            if (f.EndsWith(".bundle"))
                                _localBundleNames.Add(f);
                }
                Debug.Log($"Balancy: CacheLocalBundles found {_localBundleNames.Count} local bundle(s)");
                foreach (var b in _localBundleNames)
                    Debug.Log($"Balancy:   local bundle: {b}");
            }
            catch (Exception e)
            {
                Debug.LogWarning("Balancy: Failed to enumerate local bundles: " + e.Message);
            }
#else
            Debug.Log($"Balancy: CacheLocalBundles found {_localBundleNames.Count} local bundle(s)");
#endif
        }

        private static void PrepareAddresses(bool dataUpdated, bool profileChanged)
        {
            var url = GetAddressablesUrl();
            Debug.Log($"Balancy: PrepareAddresses URL={url}");
            if (string.IsNullOrEmpty(url))
                return;

            if (_localBundleNames == null)
                CacheLocalBundles();

            UnityEngine.AddressableAssets.Addressables.InternalIdTransformFunc = (location) => {
                string id = location.InternalId;
                if (id.StartsWith("BALANCY_URL"))
                {
                    var newId = id.Replace("//", "/").Replace("BALANCY_URL", url);
                    return newId;
                }

                // For non-HTTP .bundle paths: load locally if bundle exists, else redirect to CDN.
                if (id.EndsWith(".bundle") && !id.StartsWith("http"))
                {
                    string bundleName = id.Substring(id.LastIndexOf('/') + 1);
#if UNITY_ANDROID && !UNITY_EDITOR
                    // On Android, File.Exists doesn't work for jar: paths — use cached APK listing
                    if (_localBundleNames != null && _localBundleNames.Contains(bundleName))
                    {
                        Debug.Log($"Balancy: Bundle '{bundleName}' found locally, using local path");
                        return id;
                    }
#else
                    // On other platforms, check filesystem directly
                    if (System.IO.File.Exists(id))
                    {
                        Debug.Log($"Balancy: Bundle '{bundleName}' found locally at '{id}'");
                        return id;
                    }
#endif
                    // Bundle not found locally — redirect to CDN
                    var cdnUrl = url.TrimEnd('/') + "/" + bundleName;
                    var cached = false;
                    if (location.Data is UnityEngine.ResourceManagement.ResourceProviders.AssetBundleRequestOptions options && !string.IsNullOrEmpty(options.Hash))
                    {
                        cached = Caching.IsVersionCached(cdnUrl, Hash128.Parse(options.Hash));
                    }
                    Debug.Log($"Balancy: Bundle '{bundleName}' not found locally, redirecting to CDN (cached: {cached})");
                    return cdnUrl;
                }

                return id;
            };

            TriggerCatalogUpdate(url);
        }

        private static void TriggerCatalogUpdate(string url)
        {
            Debug.Log($"Balancy: TriggerCatalogUpdate url={url}");

            // If same URL already triggered, skip
            if (_catalogUpdateUrl == url)
                return;

            // Reset if URL changed (previous attempt used stale URL)
            if (_catalogUpdateUrl != null)
            {
                Debug.Log($"Balancy: URL changed from previous trigger, retrying catalog update");
                _catalogReady = false;
            }

            _catalogUpdateUrl = url;

            // Safety timeout: proceed with local catalog if update takes too long
            Tasks.Wait(15, () =>
            {
                if (!_catalogReady)
                {
                    Debug.LogWarning("Balancy: Catalog update timed out, proceeding with local catalog");
                    OnCatalogReady();
                }
            });

            UnityEngine.AddressableAssets.Addressables.CheckForCatalogUpdates().Completed += checkOp =>
            {
                // If URL changed while we were waiting, ignore this stale result
                if (_catalogUpdateUrl != url)
                {
                    Debug.Log("Balancy: Ignoring stale CheckForCatalogUpdates result (URL changed)");
                    return;
                }

                Debug.Log($"Balancy: CheckForCatalogUpdates Status={checkOp.Status}, Count={checkOp.Result?.Count ?? -1}");

                if (checkOp.Status == AsyncOperationStatus.Succeeded
                    && checkOp.Result != null
                    && checkOp.Result.Count > 0)
                {
                    UnityEngine.AddressableAssets.Addressables.UpdateCatalogs(checkOp.Result).Completed += updateOp =>
                    {
                        if (updateOp.Status == AsyncOperationStatus.Succeeded)
                            Debug.Log("Balancy: Addressables catalogs updated from CDN");
                        else
                            Debug.LogWarning("Balancy: Failed to update Addressables catalogs");
                        OnCatalogReady();
                    };
                }
                else
                {
                    Debug.Log("Balancy: No catalog updates found, using current catalog");
                    OnCatalogReady();
                }
            };
        }

        private static void OnCatalogReady()
        {
            _catalogReady = true;
            if (_pendingLoads != null && _pendingLoads.Count > 0)
            {
                Debug.Log($"Balancy: Catalog ready, flushing {_pendingLoads.Count} pending load(s)");
                var loads = new List<Action>(_pendingLoads);
                _pendingLoads.Clear();
                foreach (var load in loads)
                    load?.Invoke();
            }
        }

        private static string TryResolveKey(string name)
        {
            string fileName = System.IO.Path.GetFileName(name);
            foreach (var locator in UnityEngine.AddressableAssets.Addressables.ResourceLocators)
            {
                foreach (var key in locator.Keys)
                {
                    if (key is string s && s.EndsWith("/" + fileName))
                    {
                        Debug.Log($"Balancy: Resolved key '{name}' -> '{s}'");
                        return s;
                    }
                }
            }
            return null;
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
            if (!_catalogReady)
            {
                _pendingLoads.Add(() => CheckAndPrepareObject<T>(name, callback));
                return;
            }

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
                            var resolvedKey = TryResolveKey(name);
                            if (resolvedKey != null)
                            {
                                UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<T>(resolvedKey).Completed += retryResult =>
                                {
                                    Object retryObj = null;
                                    if (retryResult.Status == AsyncOperationStatus.Succeeded)
                                    {
                                        var loadedObject = new LoadedObject(retryResult.Result);
                                        typedRequests.LoadedObjects.Add(name, loadedObject);
                                        retryObj = loadedObject.GetObject();
                                    }
                                    else
                                        Debug.LogError("Couldn't load asset by resolved name " + resolvedKey);
                                    invokeCallbacksAndCleanUp(retryObj);
                                };
                                return;
                            }
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
