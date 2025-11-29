using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading;

public class RsUnityMainThreadDispatcher : MonoBehaviour
{
    private static RsUnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private bool _isQuitting = false;

    public static RsUnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<RsUnityMainThreadDispatcher>();
            }
            return _instance;
        }
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
        _isQuitting = false;
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                try
                {
                    _executionQueue.Dequeue().Invoke();
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[RsUnityMainThreadDispatcher] Error in Action: {e.Message}");
                }
            }
        }
    }

    void OnDestroy()
    {
        _isQuitting = true;
        lock (_executionQueue)
        {
            _executionQueue.Clear();
        }
        _instance = null;
    }

    public void EnqueueAndWait(Action action)
    {
        if (_isQuitting || _instance == null) return;

        if (UnityEngine.Application.platform == RuntimePlatform.WebGLPlayer ||
            Thread.CurrentThread.ManagedThreadId == 1)
        {
            action();
            return;
        }

        using (var waitHandle = new ManualResetEvent(false))
        {
            Exception ex = null;
            lock (_executionQueue)
            {
                _executionQueue.Enqueue(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        ex = e;
                    }
                    finally
                    {
                        if (waitHandle != null && !_isQuitting)
                        {
                            try { waitHandle.Set(); } catch { }
                        }
                    }
                });
            }

            if (waitHandle.WaitOne(1000))
            {
                if (ex != null) throw ex;
            }
            else
            {
                if (!_isQuitting)
                {
                    UnityEngine.Debug.LogWarning("[RsUnityMainThreadDispatcher] Dispatch timed out. (Play mode stopping?)");
                }
            }
        }
    }

    public void Enqueue(Action action)
    {
        if (_isQuitting) return;
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}