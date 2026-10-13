using UnityEditor;
using UnityEngine;

namespace SICESI.Editor
{
    /// <summary>
    /// SICE SI 追加実験における「現在のシーン配置の JSON プリセット保存」セクションを描画するクラス。
    /// オブジェクトの直接指定 (ObjectField)、登録リストからのクイック交換、複数アクティブ警告・排他化、
    /// および JSON 保存を担当します。
    /// </summary>
    public static class SICESI_PresetSaveSectionDrawer
    {
        private static string _newCaseName = "Case_1";
        private static string _subFolderName = "";
        private static SICESI_PresetPoseManager.PresetCategory _selectedCategory = SICESI_PresetPoseManager.PresetCategory.Generalization;
        private static GameObject _lastInspectedVo = null;
        private static GameObject _lastInspectedHand = null;
        private static bool _initialized = false;

        /// <summary>
        /// プリセット保存セクションを描画します。
        /// </summary>
        public static void Draw(SICESI_StereoEvaluationController controller)
        {
            if (controller == null) return;
            var config = controller.datasetConfig;

            // 1. 必要に応じて非アクティブオブジェクトを検出し、Controller に自動補完
            if (config != null)
            {
                config.SyncActiveObjectsWithController(controller);
            }

            // 現在のオブジェクト認識状態を自動判定
            var detectedCategory = SICESI_PresetPoseManager.DetectCurrentCategory(controller, out string detectReason);

            // 対象オブジェクトが変更された場合、または初回のみ自動判定カテゴリに同期
            if (!_initialized || controller.virtualObject != _lastInspectedVo || controller.groundTruthObject != _lastInspectedHand)
            {
                _lastInspectedVo = controller.virtualObject;
                _lastInspectedHand = controller.groundTruthObject;
                _selectedCategory = detectedCategory;
                _initialized = true;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                // ヘッダーバー (タイトル & 再取得ボタン)
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.LabelField("💾 現在のシーン配置を JSON プリセットとして保存", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("🔄 シーンのアクティブから再取得", EditorStyles.miniButton, GUILayout.Width(180)))
                    {
                        if (config != null)
                        {
                            config.ForceSyncActiveObjectsFromScene(controller);
                            EditorUtility.SetDirty(controller);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);

                // 対象オブジェクトの直接指定 & クイック交換 UI
                DrawTargetObjectSelector(controller, config, detectReason);

                EditorGUILayout.Space(4);

                // 保存先カテゴリの選択 (自動判定結果を反映)
                DrawCategorySelector(detectedCategory);

                EditorGUILayout.Space(2);

                // 保存先フォルダーの選択・入力
                DrawFolderSelector(controller);

                EditorGUILayout.Space(4);

                // Case 名 & 保存ボタン
                DrawSaveButton(controller);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawTargetObjectSelector(
            SICESI_StereoEvaluationController controller,
            SICESI_EvaluationDatasetConfig config,
            string detectReason)
        {
            EditorGUILayout.BeginVertical(EditorStyles.textArea);
            {
                // 仮想オブジェクト (VO)
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginChangeCheck();
                    var newVo = (GameObject)EditorGUILayout.ObjectField(
                        new GUIContent("仮想物体 (VO):", "プリセットに保存される仮想オブジェクト。直接ドラッグ＆ドロップまたは右のプルダウンで交換できます。"),
                        controller.virtualObject,
                        typeof(GameObject),
                        true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(controller, "Change Virtual Object");
                        controller.virtualObject = newVo;
                        EditorUtility.SetDirty(controller);
                    }

                    if (config != null)
                    {
                        var allVos = config.GetAllVirtualObjects();
                        if (allVos.Count > 0 && GUILayout.Button("▼ 交換", EditorStyles.miniButton, GUILayout.Width(50)))
                        {
                            var menu = new GenericMenu();
                            foreach (var vo in allVos)
                            {
                                if (vo == null) continue;
                                bool isSelected = controller.virtualObject == vo;
                                string prefix = config.IsEvaluationObject(vo) ? "🔬 検証用/" : "🪐 最適化用/";
                                string label = $"{prefix}{vo.name}" + (vo.activeInHierarchy ? " (Active)" : "");
                                var targetVo = vo;
                                menu.AddItem(new GUIContent(label), isSelected, () =>
                                {
                                    Undo.RecordObject(controller, "Switch Virtual Object");
                                    config.SwitchVirtualObject(controller, targetVo);
                                    EditorUtility.SetDirty(controller);
                                });
                            }
                            menu.ShowAsContext();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                // 手メッシュ (GT)
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginChangeCheck();
                    var newHand = (GameObject)EditorGUILayout.ObjectField(
                        new GUIContent("手メッシュ (GT):", "プリセットに保存される手メッシュ。変更するとダミー点群ターゲットも自動追従します。"),
                        controller.groundTruthObject,
                        typeof(GameObject),
                        true);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(controller, "Change Ground Truth Hand");
                        controller.groundTruthObject = newHand;
                        controller.SyncDummyPointCloudWithGroundTruth(true);
                        EditorUtility.SetDirty(controller);
                    }

                    if (config != null)
                    {
                        var allHands = config.GetAllHandMeshes();
                        if (allHands.Count > 0 && GUILayout.Button("▼ 交換", EditorStyles.miniButton, GUILayout.Width(50)))
                        {
                            var menu = new GenericMenu();
                            foreach (var hand in allHands)
                            {
                                if (hand == null) continue;
                                bool isSelected = controller.groundTruthObject == hand;
                                string prefix = config.IsEvaluationObject(hand) ? "🔬 検証用/" : "🪐 最適化用/";
                                string label = $"{prefix}{hand.name}" + (hand.activeInHierarchy ? " (Active)" : "");
                                var targetHand = hand;
                                menu.AddItem(new GUIContent(label), isSelected, () =>
                                {
                                    Undo.RecordObject(controller, "Switch Hand Mesh");
                                    config.SwitchHandMesh(controller, targetHand);
                                    EditorUtility.SetDirty(controller);
                                });
                            }
                            menu.ShowAsContext();
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                // 状態バッジ表示
                string voStatusBadge = GetObjectStatusBadge(config, controller.virtualObject);
                string handStatusBadge = GetObjectStatusBadge(config, controller.groundTruthObject);
                EditorGUILayout.LabelField($"状態: VO {voStatusBadge} | Hand {handStatusBadge}", new GUIStyle(EditorStyles.miniLabel) { richText = true });

                // 複数アクティブ警告 & 排他化ボタン
                if (config != null)
                {
                    var activeVos = config.GetActiveVirtualObjects();
                    if (activeVos.Count > 1)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.HelpBox($"⚠️ 警告: 管理対象の仮想オブジェクトが {activeVos.Count} 個同時にアクティブです。1つに排他化してください。", MessageType.Warning);
                            if (controller.virtualObject != null && GUILayout.Button("現在のVOに排他化", GUILayout.Width(120), GUILayout.Height(38)))
                            {
                                config.SwitchVirtualObject(controller, controller.virtualObject);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    var activeHands = config.GetActiveHandMeshes();
                    if (activeHands.Count > 1)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.HelpBox($"⚠️ 警告: 管理対象の手メッシュが {activeHands.Count} 個同時にアクティブです。1つに排他化してください。", MessageType.Warning);
                            if (controller.groundTruthObject != null && GUILayout.Button("現在の手メッシュに排他化", GUILayout.Width(130), GUILayout.Height(38)))
                            {
                                config.SwitchHandMesh(controller, controller.groundTruthObject);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.LabelField($"💡 自動検出用途: {detectReason}", EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private static void DrawCategorySelector(SICESI_PresetPoseManager.PresetCategory detectedCategory)
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("保存用途:", GUILayout.Width(70));
                int catIndex = (int)_selectedCategory;
                string[] catNames = new string[] { "🔬 検証用 (Generalization)", "🪐 最適化用 (Optimization)" };
                int newCatIndex = GUILayout.Toolbar(catIndex, catNames, GUILayout.Height(22));
                if (newCatIndex != catIndex)
                {
                    _selectedCategory = (SICESI_PresetPoseManager.PresetCategory)newCatIndex;
                }

                if (_selectedCategory != detectedCategory)
                {
                    if (GUILayout.Button("自動判定に戻す", EditorStyles.miniButton, GUILayout.Width(95)))
                    {
                        _selectedCategory = detectedCategory;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawFolderSelector(SICESI_StereoEvaluationController controller)
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("フォルダー:", GUILayout.Width(70));
                string displayFolder = string.IsNullOrEmpty(_subFolderName) ? "" : _subFolderName;
                string newFolder = EditorGUILayout.TextField(displayFolder);
                if (newFolder != displayFolder)
                {
                    _subFolderName = newFolder.Trim();
                }

                var existingFolders = SICESI_PresetPoseManager.GetExistingFolders(controller.outputDirectory, _selectedCategory);
                if (existingFolders != null && existingFolders.Count > 0)
                {
                    if (GUILayout.Button("📁 既存選択", EditorStyles.miniButton, GUILayout.Width(75)))
                    {
                        var menu = new GenericMenu();
                        foreach (var folder in existingFolders)
                        {
                            string f = folder;
                            bool isSelected = (f == "(ルート)" && string.IsNullOrEmpty(_subFolderName)) || (_subFolderName == f);
                            menu.AddItem(new GUIContent(f), isSelected, () =>
                            {
                                _subFolderName = (f == "(ルート)") ? "" : f;
                            });
                        }
                        menu.ShowAsContext();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            string folderHint = string.IsNullOrEmpty(_subFolderName)
                ? "💡 未入力時はルート (直下) に保存されます"
                : $"💡 保存先サブフォルダー: [{_subFolderName}]";
            EditorGUILayout.LabelField(folderHint, EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawSaveButton(SICESI_StereoEvaluationController controller)
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Case 名:", GUILayout.Width(70));
                _newCaseName = EditorGUILayout.TextField(_newCaseName);

                GUI.backgroundColor = _selectedCategory == SICESI_PresetPoseManager.PresetCategory.Generalization
                    ? new Color(1.0f, 0.7f, 0.3f)
                    : new Color(0.4f, 0.85f, 0.6f);

                string saveBtnText = _selectedCategory == SICESI_PresetPoseManager.PresetCategory.Generalization
                    ? "💾 検証用配置として保存"
                    : "💾 最適化用配置として保存";

                string curVoName = controller.virtualObject != null ? controller.virtualObject.name : "未設定";
                string curHandName = controller.groundTruthObject != null ? controller.groundTruthObject.name : "未設定";

                if (GUILayout.Button(saveBtnText, GUILayout.Width(170), GUILayout.Height(26)))
                {
                    if (SICESI_PresetPoseManager.SaveCurrentSceneAsPreset(controller, _newCaseName, _selectedCategory, _subFolderName))
                    {
                        string folderInfo = string.IsNullOrEmpty(_subFolderName) ? "" : $"フォルダー: {_subFolderName}\n";
                        EditorUtility.DisplayDialog("保存完了", $"[{_selectedCategory}] 配置プリセット [{_newCaseName}] を保存しました。\n{folderInfo}VO: {curVoName}\nHand: {curHandName}", "OK");

                        if (_newCaseName.StartsWith("Case_") && int.TryParse(_newCaseName.Substring(5), out int num))
                        {
                            _newCaseName = $"Case_{num + 1}";
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string GetObjectStatusBadge(SICESI_EvaluationDatasetConfig config, GameObject obj)
        {
            if (obj == null) return "<color=red>[未設定]</color>";
            if (config == null) return "";

            string activeState = obj.activeInHierarchy ? "<b><color=#00CC66>(Active)</color></b>" : "<color=gray>(Inactive)</color>";

            if (config.evaluationVirtualObjects != null && config.evaluationVirtualObjects.Contains(obj))
                return $"<color=#FF9933><b>[🔬検証用]</b></color> {activeState}";
            if (config.evaluationHandMeshes != null && config.evaluationHandMeshes.Contains(obj))
                return $"<color=#FF9933><b>[🔬検証用]</b></color> {activeState}";

            if (config.optimizationVirtualObjects != null && config.optimizationVirtualObjects.Contains(obj))
                return $"<color=#33CC66><b>[🪐最適化用]</b></color> {activeState}";
            if (config.optimizationHandMeshes != null && config.optimizationHandMeshes.Contains(obj))
                return $"<color=#33CC66><b>[🪐最適化用]</b></color> {activeState}";

            return $"<color=gray>[リスト未登録]</color> {activeState}";
        }
    }
}
