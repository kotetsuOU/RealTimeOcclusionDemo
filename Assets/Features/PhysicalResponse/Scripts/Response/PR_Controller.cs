#if false
using UnityEngine;

namespace Features.PhysicalResponse
{
    /// <summary>
    /// Midair Hapticsの物理応答（PhysicsProfile, MoveToStartPosApplier, ShapeMatchingSoftbodyApplier）
    /// の全パラメータを実行時に調整・管理するためのコントローラースクリプトです。
    /// インスペクター上で各設定を分かりやすく管理できます。
    /// </summary>
    public class PR_Controller : MonoBehaviour
    {
        [Header("--- Target Setup ---")]
        [Tooltip("設定を適用する対象の GameObject。未設定の場合は自身のアタッチされたオブジェクトを使用します。")]
        public GameObject targetObject;

        private PhysicsProfile _physicsProfile;
        private MoveToStartPosApplier _moveToStartPosApplier;
        private ShapeMatchingSoftbodyApplier _softbodyApplier;

        // ==========================================
        // Physics Profile Settings
        // ==========================================
        [Space(10)]
        [Header("--- Physics Profile Settings ---")]
        [Tooltip("GPU 側 contact force reduction 出力を使う")]
        public bool useGpu = true;

        [Space(5)]
        [Tooltip("コンタクトフォース全体に掛ける倍率")]
        [Range(0f, 10f)] public float forceScale = 1.0f;
        
        [Tooltip("正方向 X/Y/Z 成分に掛ける倍率")]
        public Vector3 positiveAxisScale = Vector3.one;
        [Tooltip("負方向 X/Y/Z 成分に掛ける倍率")]
        public Vector3 negativeAxisScale = Vector3.one;

        [Space(5)]
        [Tooltip("Rigidbody に contact force を加えるか")]
        public bool applyContactForcesToRigidbody = true;
        [Tooltip("contact force を bone Rigidbody へ加えるか")]
        public bool applyContactForcesToBones = false;
        [Tooltip("目標 bone pose へ追従させるか")]
        public bool followTargetBonePose = false;

        [Space(5)]
        [Tooltip("bone contact force の計算経路")]
        public BoneContactForcePathMode boneContactForcePathMode = BoneContactForcePathMode.Cpu;

        [Space(5)]
        [Tooltip("contact force を bone force に変換する倍率")]
        [Range(0f, 10f)] public float coeffFingerToBone = 1.0f;
        [Tooltip("bone 位置追従 force の spring 係数")]
        [Range(0f, 1f)] public float coeffKBonePos = 0.0f;
        [Tooltip("bone 回転追従 torque の spring 係数")]
        [Range(0f, 1f)] public float coeffKBoneRot = 0.1f;
        [Tooltip("kinematic pose follow の blend 増分")]
        [Range(0f, 1f)] public float kinematicPoseFollowBlendStep = 0.05f;
        [Tooltip("skin weight を force 分配 weight に変換する power")]
        [Range(0f, 10f)] public float weightPower = 1.0f;

        // ==========================================
        // Move To Start Pos Applier Settings
        // ==========================================
        [Space(10)]
        [Header("--- Move To Start Pos Settings ---")]
        [Tooltip("初期位置へ戻す force 係数")]
        [Range(0f, 100f)] public float posK = 10f;
        [Tooltip("初期回転へ戻す torque 係数")]
        [Range(0f, 100f)] public float rotK = 10f;
        [Tooltip("return force の magnitude 上限")]
        [Range(0f, 100f)] public float maxForce = 50f;
        
        [Space(5)]
        [Tooltip("接触 count に基づいて return force を残す比率")]
        [Range(0f, 1f)] public float ratioTouched = 0f;
        [Tooltip("return force X 成分倍率")]
        [Range(0f, 5f)] public float coeffForceX = 1f;
        [Tooltip("return force Y 成分倍率")]
        [Range(0f, 5f)] public float coeffForceY = 1f;
        [Tooltip("return force Z 成分倍率")]
        [Range(0f, 5f)] public float coeffForceZ = 1f;

        [Space(5)]
        [Tooltip("noise force の倍率")]
        [Range(0f, 50f)] public float randomForce = 0f;
        [Tooltip("noise torque の倍率")]
        [Range(0f, 50f)] public float randomTorque = 0f;
        [Tooltip("noise sampling の time scale")]
        [Range(0f, 10f)] public float randomSpeed = 1f;

        // ==========================================
        // Shape Matching Softbody Applier Settings
        // ==========================================
        [Space(10)]
        [Header("--- Softbody Settings ---")]
        [Tooltip("contact force を particle force に入れるか")]
        public bool applyExternalForceToSoftbody = true;
        [Tooltip("internal spring / damping force を使うか")]
        public bool applyInternalForceToSoftbody = true;
        
        [Space(5)]
        [Tooltip("contact force の倍率")]
        [Range(0f, 10f)] public float softbodyContactForceScale = 1.0f;
        [Tooltip("internal spring force 係数")]
        [Range(0f, 500f)] public float internalForceSpring = 100f;
        [Tooltip("internal damping force 係数")]
        [Range(0f, 50f)] public float internalForceDamping = 10f;
        [Tooltip("particle force 上限")]
        [Range(0f, 1000f)] public float maxParticleForce = 100f;
        
        [Space(5)]
        [Tooltip("目標 pose へ寄せる stiffness")]
        [Range(0f, 10f)] public float targetStiffness = 1.0f;
        [Tooltip("shape matching で rest shape を保つ stiffness")]
        [Range(0f, 10f)] public float shapeStiffness = 1.0f;
        [Tooltip("particle velocity damping")]
        [Range(0f, 1f)] public float particleDamping = 0.05f;

        [Space(5)]
        [Tooltip("1 Unity frame の simulation substep 数")]
        [Range(1, 10)] public int substepCount = 1;
        [Tooltip("shape matching iteration 数")]
        [Range(1, 10)] public int iterationCount = 1;
        [Tooltip("reset 判定に使う移動距離")]
        public float teleportResetDistance = 1.0f;
        [Tooltip("reset 判定に使う回転角度")]
        public float teleportResetAngle = 45f;
        [Tooltip("best-fit rotation の solve 方式")]
        public ShapeMatchingBestFitRotationSolveMode bestFitRotationSolveMode = ShapeMatchingBestFitRotationSolveMode.PolarDecomposition;

        private void Start()
        {
            if (targetObject == null)
            {
                targetObject = gameObject;
            }

            _physicsProfile = targetObject.GetComponentInChildren<PhysicsProfile>();
            _moveToStartPosApplier = targetObject.GetComponentInChildren<MoveToStartPosApplier>();
            _softbodyApplier = targetObject.GetComponentInChildren<ShapeMatchingSoftbodyApplier>();

            FetchCurrentSettings();
            ApplySettings();
        }

        private void Update()
        {
            // エディタ実行中など、インスペクターの値が変更された際に毎フレーム反映する
            ApplySettings();
        }

        /// <summary>
        /// 制御対象のGameObjectを動的に変更し、そのコンポーネントから設定を読み直します。
        /// </summary>
        public void SetTarget(GameObject newTarget)
        {
            if (newTarget == null || newTarget == targetObject) return;

            targetObject = newTarget;
            
            // 1. 直下や親を検索 (自身がすでにSoftBodyやBonePhysicsルートの場合など)
            _physicsProfile = targetObject.GetComponentInChildren<PhysicsProfile>();
            if (_physicsProfile == null) _physicsProfile = targetObject.GetComponentInParent<PhysicsProfile>();
            
            _moveToStartPosApplier = targetObject.GetComponentInChildren<MoveToStartPosApplier>();
            if (_moveToStartPosApplier == null) _moveToStartPosApplier = targetObject.GetComponentInParent<MoveToStartPosApplier>();
            
            _softbodyApplier = targetObject.GetComponentInChildren<ShapeMatchingSoftbodyApplier>();
            if (_softbodyApplier == null) _softbodyApplier = targetObject.GetComponentInParent<ShapeMatchingSoftbodyApplier>();

            // 2. もし見つからなかった場合、Midair Hapticsの自動生成命名規則に基づいて検索する
            // (例: "Fox" を渡した場合、"FoxBonePhysics" や "FoxSoftBody" を探す)
            if (_physicsProfile == null)
            {
                string bonePhysicsName = targetObject.name + "BonePhysics";
                string softBodyName = targetObject.name + "SoftBody";
                Transform generatedRoot = null;

                // 兄弟オブジェクトの検索
                if (targetObject.transform.parent != null)
                {
                    generatedRoot = targetObject.transform.parent.Find(bonePhysicsName);
                    if (generatedRoot == null) generatedRoot = targetObject.transform.parent.Find(softBodyName);
                }

                // 見つからなければシーン全体からアクティブなものを検索
                if (generatedRoot == null)
                {
                    GameObject foundObj = GameObject.Find(bonePhysicsName);
                    if (foundObj == null) foundObj = GameObject.Find(softBodyName);
                    if (foundObj != null) generatedRoot = foundObj.transform;
                }

                if (generatedRoot != null)
                {
                    _physicsProfile = generatedRoot.GetComponentInChildren<PhysicsProfile>();
                    _moveToStartPosApplier = generatedRoot.GetComponentInChildren<MoveToStartPosApplier>();
                    _softbodyApplier = generatedRoot.GetComponentInChildren<ShapeMatchingSoftbodyApplier>();
                    UnityEngine.Debug.Log($"[PR_Controller] '{targetObject.name}' に関連する物理セットアップ '{generatedRoot.name}' を自動検出しました。");
                }
            }

            if (_physicsProfile == null && _softbodyApplier == null)
            {
                UnityEngine.Debug.LogWarning($"[PR_Controller] '{targetObject.name}' に関連する PhysicsProfile や SoftbodyApplier が見つかりませんでした。Contact Physics Setup ツールで生成されているか確認してください。");
            }

            FetchCurrentSettings();
            ApplySettings();
        }

        /// <summary>
        /// アタッチされているコンポーネントから現在の値を読み取ります。
        /// </summary>
        public void FetchCurrentSettings()
        {
            if (_physicsProfile != null)
            {
                useGpu = _physicsProfile.useGpu;
                forceScale = _physicsProfile.forceScale;
                positiveAxisScale = _physicsProfile.positiveAxisScale;
                negativeAxisScale = _physicsProfile.negativeAxisScale;
                applyContactForcesToRigidbody = _physicsProfile.applyContactForcesToRigidbody;
                applyContactForcesToBones = _physicsProfile.applyContactForcesToBones;
                followTargetBonePose = _physicsProfile.followTargetBonePose;
                boneContactForcePathMode = _physicsProfile.boneContactForcePathMode;
                coeffFingerToBone = _physicsProfile.coeffFingerToBone;
                coeffKBonePos = _physicsProfile.coeffKBonePos;
                coeffKBoneRot = _physicsProfile.coeffKBoneRot;
                kinematicPoseFollowBlendStep = _physicsProfile.kinematicPoseFollowBlendStep;
                weightPower = _physicsProfile.weightPower;
            }

            if (_moveToStartPosApplier != null)
            {
                posK = _moveToStartPosApplier.posK;
                rotK = _moveToStartPosApplier.rotK;
                maxForce = _moveToStartPosApplier.maxForce;
                ratioTouched = _moveToStartPosApplier.ratioTouched;
                coeffForceX = _moveToStartPosApplier.coeffForceX;
                coeffForceY = _moveToStartPosApplier.coeffForceY;
                coeffForceZ = _moveToStartPosApplier.coeffForceZ;
                randomForce = _moveToStartPosApplier.randomForce;
                randomTorque = _moveToStartPosApplier.randomTorque;
                randomSpeed = _moveToStartPosApplier.randomSpeed;
            }

            if (_softbodyApplier != null)
            {
                applyExternalForceToSoftbody = _softbodyApplier.applyExternalForceToSoftbody;
                applyInternalForceToSoftbody = _softbodyApplier.applyInternalForceToSoftbody;
                softbodyContactForceScale = _softbodyApplier.contactForceScale;
                internalForceSpring = _softbodyApplier.internalForceSpring;
                internalForceDamping = _softbodyApplier.internalForceDamping;
                maxParticleForce = _softbodyApplier.maxParticleForce;
                targetStiffness = _softbodyApplier.targetStiffness;
                shapeStiffness = _softbodyApplier.shapeStiffness;
                particleDamping = _softbodyApplier.particleDamping;
                substepCount = _softbodyApplier.substepCount;
                iterationCount = _softbodyApplier.iterationCount;
                teleportResetDistance = _softbodyApplier.teleportResetDistance;
                teleportResetAngle = _softbodyApplier.teleportResetAngle;
                bestFitRotationSolveMode = _softbodyApplier.bestFitRotationSolveMode;
            }
        }

        /// <summary>
        /// インスペクターのパラメータを各コンポーネントに適用します。
        /// </summary>
        public void ApplySettings()
        {
            if (_physicsProfile != null)
            {
                _physicsProfile.useGpu = useGpu;
                _physicsProfile.forceScale = forceScale;
                _physicsProfile.positiveAxisScale = positiveAxisScale;
                _physicsProfile.negativeAxisScale = negativeAxisScale;
                _physicsProfile.applyContactForcesToRigidbody = applyContactForcesToRigidbody;
                _physicsProfile.applyContactForcesToBones = applyContactForcesToBones;
                _physicsProfile.followTargetBonePose = followTargetBonePose;
                _physicsProfile.boneContactForcePathMode = boneContactForcePathMode;
                _physicsProfile.coeffFingerToBone = coeffFingerToBone;
                _physicsProfile.coeffKBonePos = coeffKBonePos;
                _physicsProfile.coeffKBoneRot = coeffKBoneRot;
                _physicsProfile.kinematicPoseFollowBlendStep = kinematicPoseFollowBlendStep;
                _physicsProfile.weightPower = weightPower;
            }

            if (_moveToStartPosApplier != null)
            {
                _moveToStartPosApplier.posK = posK;
                _moveToStartPosApplier.rotK = rotK;
                _moveToStartPosApplier.maxForce = maxForce;
                _moveToStartPosApplier.ratioTouched = ratioTouched;
                _moveToStartPosApplier.coeffForceX = coeffForceX;
                _moveToStartPosApplier.coeffForceY = coeffForceY;
                _moveToStartPosApplier.coeffForceZ = coeffForceZ;
                _moveToStartPosApplier.randomForce = randomForce;
                _moveToStartPosApplier.randomTorque = randomTorque;
                _moveToStartPosApplier.randomSpeed = randomSpeed;
            }

            if (_softbodyApplier != null)
            {
                _softbodyApplier.applyExternalForceToSoftbody = applyExternalForceToSoftbody;
                _softbodyApplier.applyInternalForceToSoftbody = applyInternalForceToSoftbody;
                _softbodyApplier.contactForceScale = softbodyContactForceScale;
                _softbodyApplier.internalForceSpring = internalForceSpring;
                _softbodyApplier.internalForceDamping = internalForceDamping;
                _softbodyApplier.maxParticleForce = maxParticleForce;
                _softbodyApplier.targetStiffness = targetStiffness;
                _softbodyApplier.shapeStiffness = shapeStiffness;
                _softbodyApplier.particleDamping = particleDamping;
                _softbodyApplier.substepCount = substepCount;
                _softbodyApplier.iterationCount = iterationCount;
                _softbodyApplier.teleportResetDistance = teleportResetDistance;
                _softbodyApplier.teleportResetAngle = teleportResetAngle;
                _softbodyApplier.bestFitRotationSolveMode = bestFitRotationSolveMode;
            }
        }
    }
}
#endif
