using UnityEngine;
using System.Collections.Generic;
using System;
using System.Threading;

public class RsUnityMainThreadDispatcher : MonoBehaviour
{
    private static RsUnityMainThreadDispatcher _instance;
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();

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
    }

    void Update()
    {
        lock (_executionQueue)
        {
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    public void EnqueueAndWait(Action action)
    {
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
                        waitHandle.Set();
                    }
                });
            }
            waitHandle.WaitOne();

            if (ex != null) throw ex;
        }
    }

    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }
}