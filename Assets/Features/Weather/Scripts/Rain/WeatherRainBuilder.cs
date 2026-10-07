using UnityEngine;
using UnityEngine.Rendering;

namespace Features.Weather
{
    /// <summary>
    /// 雨シミュレーション用 ParticleSystem および衝突・飛沫サブエミッターの初期化・パラメータ構築を担当するビルダークラス。
    /// WeatherRainProcessor から生成・構築責務を分離し、保守性とテスタビリティを向上させます。
    /// </summary>
    public static class WeatherRainBuilder
    {
        private static readonly Color DropColor = new Color(0.75f, 0.8f, 0.95f, 0.45f);
        private static readonly Color SplashColor = new Color(0.8f, 0.85f, 1.0f, 0.35f);

        /// <summary>
        /// 雨粒メインパーティクルシステムのパラメータを構成します。
        /// </summary>
        public static void BuildRainParticles(
            ParticleSystem rainPs,
            Material material,
            float cloudY,
            float groundY,
            float fallSpeedMin,
            float fallSpeedMax,
            Vector2 rainAreaSize,
            float maxRate,
            WeatherRainCollisionMode collisionMode,
            float dropSize = 0.002f,
            float lengthScale = 1.8f)
        {
            if (rainPs == null) return;

            rainPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float height = Mathf.Max(0.1f, cloudY - groundY);
            float vMin = Mathf.Max(0.1f, fallSpeedMin);
            float vMax = Mathf.Max(vMin, fallSpeedMax);
            float avgSpeed = (vMin + vMax) * 0.5f;
            float lifetime = collisionMode == WeatherRainCollisionMode.None ? (height / avgSpeed) : (height / avgSpeed + 0.1f);

            var main = rainPs.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(vMin, vMax);
            main.startSize = Mathf.Max(0.0005f, dropSize);
            main.startColor = DropColor;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(maxRate * lifetime * 1.3f);

            var emission = rainPs.emission;
            emission.rateOverTime = 0f;

            // Boxエミッター: +Z方向に放出されるため、X軸90度回転で真下(-Y)へ向ける
            var shape = rainPs.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.scale = new Vector3(rainAreaSize.x, rainAreaSize.y, 0.01f);

            // レンダラー設定 (落下方向に引き伸ばす Stretch Billboard)
            var renderer = rainPs.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.02f;
            renderer.lengthScale = Mathf.Max(0.1f, lengthScale);
            renderer.maxParticleSize = 0.015f; // 視点近接時の巨大化をクリップ
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;
        }

        /// <summary>
        /// パーティクル衝突モジュールを設定します。
        /// </summary>
        public static Transform BuildCollisionModule(
            ParticleSystem rainPs,
            WeatherRainCollisionMode collisionMode,
            float groundY,
            LayerMask environmentMask,
            Transform currentPlaneTransform,
            Transform parentTransform)
        {
            if (rainPs == null) return currentPlaneTransform;

            var col = rainPs.collision;
            col.enabled = collisionMode != WeatherRainCollisionMode.None;
            if (!col.enabled) return currentPlaneTransform;

            col.bounce = 0f;
            col.dampen = 1f;
            col.lifetimeLoss = 1f; // 衝突時に雨粒を即座に消滅させる

            if (collisionMode == WeatherRainCollisionMode.Plane)
            {
                col.type = ParticleSystemCollisionType.Planes;
                Transform planeTrans = currentPlaneTransform;
                if (planeTrans == null)
                {
                    var planeGo = new GameObject("RainFloorPlane");
                    planeGo.transform.SetParent(parentTransform, false);
                    planeTrans = planeGo.transform;
                }
                planeTrans.SetPositionAndRotation(new Vector3(0f, groundY, 0f), Quaternion.identity);
                col.SetPlane(0, planeTrans);
                return planeTrans;
            }
            else if (collisionMode == WeatherRainCollisionMode.World)
            {
                col.type = ParticleSystemCollisionType.World;
                col.mode = ParticleSystemCollisionMode.Collision3D;
                col.quality = ParticleSystemCollisionQuality.Low;
                col.collidesWith = environmentMask;
                col.enableDynamicColliders = false;
            }

            return currentPlaneTransform;
        }

        /// <summary>
        /// 飛沫 (Splash) サブエミッターを構築し、メインパーティクルシステムに紐づけます。
        /// </summary>
        public static void BuildSplashSubEmitter(
            ParticleSystem splashPs,
            ParticleSystem rainPs,
            Material material,
            float maxRate)
        {
            if (splashPs == null || rainPs == null) return;

            splashPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = splashPs.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.007f);
            main.startColor = SplashColor;
            main.gravityModifier = 1.0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(maxRate * 3f * 0.22f * 1.2f);

            var emission = splashPs.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 4) });

            // コーン状に上方向へ跳ねる
            var shape = splashPs.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 35f;
            shape.radius = 0.005f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var renderer = splashPs.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = material;

            var subEmitters = rainPs.subEmitters;
            subEmitters.enabled = true;
            while (subEmitters.subEmittersCount > 0)
            {
                subEmitters.RemoveSubEmitter(0);
            }
            subEmitters.AddSubEmitter(splashPs, ParticleSystemSubEmitterType.Collision,
                ParticleSystemSubEmitterProperties.InheritNothing);
        }

        /// <summary>
        /// マテリアル未設定時のフォールバック用マテリアルを生成します。
        /// </summary>
        public static Material CreateFallbackMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");

            if (shader == null) return null;

            var mat = new Material(shader)
            {
                name = "Weather_Rain_Material",
                renderQueue = (int)RenderQueue.Geometry
            };
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            mat.SetOverrideTag("RenderType", "Opaque");
            return mat;
        }
    }
}
