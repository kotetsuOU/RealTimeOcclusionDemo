using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading;
using Core.Logging;

[AppLoggable("RealSense (Pipeline)")]
public class RsUnityMainThreadDispatcher : MonoBehaviour
{
    private static RsUnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private bool _isQuitting = false;

    /// <summary>
    /// ゲーム起動時（シーンロード前）にメインスレッド上で自動的にGameObjectを生成・常駐させる。
    /// これにより、各シーンに手動でDispatcherオブジェクトを配置する必要がなくなります。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoInitialize()
    {
        EnsureInstance();
    }

    /// <summary>
    /// エディタ再生停止・再開時（Domain Reload無効時を含む）の静的状態のリセット
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic()
    {
        _instance = null;
        lock (_executionQueue)
        {
            _executionQueue.Clear();
        }
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        // メインスレッド上でのみGameObjectの検索・生成が可能
        if (Thread.CurrentThread.ManagedThreadId == 1 || AppLogger.IsMainThread)
        {
            _instance = FindFirstObjectByType<RsUnityMainThreadDispatcher>();

            if (_instance == null)
            {
                var go = new GameObject("RsUnityMainThreadDispatcher");
                _instance = go.AddComponent<RsUnityMainThreadDispatcher>();
                DontDestroyOnLoad(go);
            }
        }
    }

    public static RsUnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                EnsureInstance();
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
        else if (_instance != this)
        {
            // シーン上に手動配置された残骸や重複オブジェクトがある場合は自動破棄
            Destroy(this.gameObject);
            return;
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
                    AppLogger.LogError("RsUnityMainThreadDispatcher", $"Error in Action: {e.Message}");
                }
            }
        }
    }

    void OnDestroy()
    {
        if (_instance == this)
        {
            _isQuitting = true;
            lock (_executionQueue)
            {
                _executionQueue.Clear();
            }
            _instance = null;
        }
    }

    public void EnqueueAndWait(Action action)
    {
        if (_isQuitting || _instance == null) return;

        if (UnityEngine.Application.platform == RuntimePlatform.WebGLPlayer ||
            Thread.CurrentThread.ManagedThreadId == 1 ||
            AppLogger.IsMainThread)
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

            if (waitHandle.WaitOne(3000))
            {
                if (ex != null) throw ex;
            }
            else
            {
                if (!_isQuitting)
                {
                    AppLogger.LogWarning("RsUnityMainThreadDispatcher", "Dispatch timed out. (Play mode stopping?)");
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
