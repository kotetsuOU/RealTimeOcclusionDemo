using System.Collections;
using UnityEngine;

#nullable enable

/// <summary>
/// 参加者が「開始・終了」を自身で入力するインタラクティブなタスク（自己ペース課題）のための基底クラス。
/// Pointingタスクなど、ユーザがアクションを完了したタイミングで次の試行に進む実験に使用します。
/// </summary>
public abstract class EXP_BaseInteractiveTaskCondition : EXP_BaseCondition
{
    [Header("Interactive Task Info")]
    [Tooltip("このタスクの制限時間（秒）。0以下の場合は無制限。")]
    public float timeLimitSeconds = 0f;

    /// <summary>
    /// この条件の実験パラダイム識別名
    /// </summary>
    public override string ParadigmType => "InteractiveTask";

    /// <summary>
    /// 刺激提示コルーチン。タスクの実行フローを管理します。
    /// 派生クラスは必要に応じて SetupTask / ExecuteTask / CleanupTask をオーバーライドしてください。
    /// </summary>
    public override IEnumerator? StimulusCoroutine(EXP_TrialData trial, MonoBehaviour runner)
    {
        // 1. タスクの準備
        SetupTask(trial);
        
        // 2. タスク実行時間の記録開始
        trial.metadata["TaskStartTime"] = Time.realtimeSinceStartup.ToString("F6");
        
        // 3. 実際のタスク処理（派生クラスで実装）
        // この中で参加者の入力待ちなどを行います
        yield return runner.StartCoroutine(ExecuteTaskCoroutine(trial, runner));

        // 4. タスク完了時間の記録
        float startTime = float.Parse(trial.metadata["TaskStartTime"]);
        float completionTime = Time.realtimeSinceStartup - startTime;
        trial.metadata["CompletionTime"] = completionTime.ToString("F6");

        // 5. 後片付け
        CleanupTask(trial);
    }

    /// <summary>
    /// Applyはコルーチン側で処理するため空実装とします。
    /// </summary>
    public override void Apply(EXP_TrialData trial)
    {
    }

    /// <summary>
    /// タスク開始前の初期化処理。仮想ターゲットの配置などを行います。
    /// </summary>
    protected virtual void SetupTask(EXP_TrialData trial) { }

    /// <summary>
    /// タスクのメイン実行フロー。
    /// 例えば、特定のボタンが押されるまで yield return null; で待機するなどの処理を記述します。
    /// </summary>
    protected abstract IEnumerator ExecuteTaskCoroutine(EXP_TrialData trial, MonoBehaviour runner);

    /// <summary>
    /// タスク終了後の後片付け処理。配置したターゲットの非表示などを行います。
    /// </summary>
    protected virtual void CleanupTask(EXP_TrialData trial) { }
}
