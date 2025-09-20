using UnityEngine;

public class DisplaySwitcher : MonoBehaviour
{
    void Start()
    {
        // 最初のディスプレイ以外を有効化する
        if (Display.displays.Length > 1)
        {
            Display.displays[1].Activate();
        }
    }

    void Update()
    {
        // Spaceキーでカメラの表示先を切り替える
        if (Input.GetKeyDown(KeyCode.Space))
        {
            // 現在のカメラを取得
            Camera mainCamera = Camera.main;

            // 現在のターゲットディスプレイを取得
            int currentTargetDisplay = mainCamera.targetDisplay;

            // 次のターゲットディスプレイを計算
            int nextTargetDisplay = (currentTargetDisplay + 1) % Display.displays.Length;

            // カメラのターゲットディスプレイを切り替える
            mainCamera.targetDisplay = nextTargetDisplay;

            Debug.Log("カメラの表示先をディスプレイ " + nextTargetDisplay + " に切り替えました。");
        }
    }
}