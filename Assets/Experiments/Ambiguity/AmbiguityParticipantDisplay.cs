using UnityEngine;
using TMPro;

/// <summary>
/// 被験者向けの表示（注視点と文言）。SRD に出すため、World Space の Canvas 上の TextMeshPro で表示する。
/// ハーフミラー経由で見る場合は、mirrorRoot の X スケールを反転して、文字が正しく読めるようにする。
/// </summary>
public class AmbiguityParticipantDisplay : MonoBehaviour
{
    [Header("表示物（World Space の Canvas 上に置く）")]
    [Tooltip("注視点「＋」")]
    [SerializeField] private TMP_Text fixationText;
    [Tooltip("教示・案内文")]
    [SerializeField] private TMP_Text messageText;

    [Header("ハーフミラー対応")]
    [Tooltip("ON なら mirrorRoot の X スケールを負にして、鏡像でも読める向きにする")]
    [SerializeField] private bool mirrorX = true;
    [Tooltip("反転する親（Canvas など）。文字と注視点の両方を含める")]
    [SerializeField] private Transform mirrorRoot;

    private void Awake() => ApplyMirror();
    private void OnValidate() => ApplyMirror();

    private void ApplyMirror()
    {
        if (mirrorRoot == null) return;
        var s = mirrorRoot.localScale;
        s.x = mirrorX ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
        mirrorRoot.localScale = s;
    }

    public void ShowFixation(bool on)
    {
        if (fixationText != null && fixationText.gameObject.activeSelf != on)
            fixationText.gameObject.SetActive(on);
    }

    /// <summary>空文字なら消す。</summary>
    public void ShowMessage(string text)
    {
        if (messageText == null) return;
        bool on = !string.IsNullOrEmpty(text);
        if (messageText.gameObject.activeSelf != on) messageText.gameObject.SetActive(on);
        if (on) messageText.text = text;
    }
}
