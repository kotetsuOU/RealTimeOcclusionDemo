using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Features.Weather
{
    /// <summary>
    /// 落雷のマルチストローク（再雷撃 2〜4 回）および残光フェードアウトの閃光タイムラインを管理する純粋シーケンサークラス。
    /// 自然界の稲妻特有のパパパッという多重放電と、その後の滑らかな残光減衰 F(t) の時間発展を制御します。
    /// </summary>
    public class WeatherLightningSequencer
    {
        public float FlashDuration { get; set; } = 0.35f;
        public float FadeDuration { get; set; } = 0.45f;

        /// <summary>
        /// 再雷撃マルチストロークおよび減衰フェードのコルーチンシーケンスを実行します。
        /// </summary>
        /// <param name="onApplyFlash">閃光強度 F(t) [0, 1] を反映するコールバック</param>
        /// <param name="onEndStrike">落雷終了時に呼び出されるコールバック</param>
        public IEnumerator ExecuteStrikeRoutine(Action<float> onApplyFlash, Action onEndStrike)
        {
            if (onApplyFlash == null) yield break;

            // 2〜4回の再雷撃 (Re-strike)
            int strokes = Random.Range(2, 4);
            float baseDuration = Mathf.Max(0.05f, FlashDuration);
            float firstStrokeHold = baseDuration * 0.45f; // 第1撃のメイン稲妻をしっかり保持して形を視認させる
            float reStrokeHold = baseDuration * 0.25f;
            float blinkGap = baseDuration * 0.12f;

            for (int i = 0; i < strokes; i++)
            {
                float peak = (i == 0) ? 1.0f : Random.Range(0.7f, 0.95f);
                float holdTime = (i == 0) ? firstStrokeHold : reStrokeHold;

                float holdEndTime = Time.time + holdTime;
                do
                {
                    onApplyFlash(peak);
                    yield return null;
                } while (Time.time < holdEndTime);

                if (i < strokes - 1)
                {
                    float gapEndTime = Time.time + blinkGap;
                    do
                    {
                        onApplyFlash(0.1f);
                        yield return null;
                    } while (Time.time < gapEndTime);
                }
            }

            // 残光減衰フェード
            float totalFade = Mathf.Max(0.05f, FadeDuration);
            for (float t = 0f; t < totalFade; t += Time.deltaTime)
            {
                float lvl = Mathf.Lerp(0.6f, 0.0f, t / totalFade);
                onApplyFlash(lvl);
                yield return null;
            }

            onEndStrike?.Invoke();
        }
    }
}
