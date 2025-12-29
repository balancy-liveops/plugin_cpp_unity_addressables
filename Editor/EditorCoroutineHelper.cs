#if UNITY_EDITOR && !BALANCY_SERVER
using System.Collections;
using UnityEditor;
using UnityEngine.Networking;

namespace Balancy.Editor
{
    /// <summary>
    /// Helper class to run coroutines in the Unity Editor without requiring a GameObject.
    /// Uses EditorApplication.update to process coroutine steps.
    /// Properly handles UnityWebRequestAsyncOperation and nested coroutines.
    /// </summary>
    public class EditorCoroutineHelper
    {
        private IEnumerator _enumerator;
        private bool _isRunning;
        private object _currentYield;

        public static EditorCoroutineHelper Create()
        {
            return new EditorCoroutineHelper();
        }

        public static EditorCoroutineHelper Execute(IEnumerator enumerator)
        {
            var helper = Create();
            helper.LaunchCoroutine(enumerator);
            return helper;
        }

        public void LaunchCoroutine(IEnumerator enumerator)
        {
            if (_isRunning)
            {
                UnityEngine.Debug.LogWarning("EditorCoroutineHelper: Coroutine already running!");
                return;
            }

            _enumerator = enumerator;
            _isRunning = true;
            _currentYield = null;
            EditorApplication.update += Update;
        }

        private void Update()
        {
            if (_enumerator == null || !_isRunning)
            {
                Stop();
                return;
            }

            try
            {
                // Check if we're waiting for an async operation to complete
                if (_currentYield != null)
                {
                    if (_currentYield is UnityWebRequestAsyncOperation asyncOp)
                    {
                        // Wait until the web request is done
                        if (!asyncOp.isDone)
                            return;
                    }
                    else if (_currentYield is IEnumerator nestedEnumerator)
                    {
                        // Process nested coroutine
                        if (nestedEnumerator.MoveNext())
                        {
                            _currentYield = nestedEnumerator.Current;
                            return;
                        }
                    }

                    // Clear the yield and continue
                    _currentYield = null;
                }

                // Move to next step
                if (_enumerator.MoveNext())
                {
                    _currentYield = _enumerator.Current;
                }
                else
                {
                    Stop();
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"EditorCoroutineHelper exception: {e.Message}\n{e.StackTrace}");
                Stop();
            }
        }

        private void Stop()
        {
            _isRunning = false;
            EditorApplication.update -= Update;
            _enumerator = null;
            _currentYield = null;
        }
    }
}
#endif
