using System;
using System.Collections.Generic;
using UnityEngine;

namespace SICESI
{
    /// <summary>
    /// SICE SI 評価実験において、正解画像 (Ground Truth) やテスト画像の撮影時に
    /// 手メッシュや仮想オブジェクトのマテリアル・レイヤーを一時的に差し替え、撮影後に完全復元する調停クラス。
    /// </summary>
    public class SICESI_MaterialSwapper : IDisposable
    {
        public class GroundTruthStateBackup
        {
            public Dictionary<Renderer, Material[]> Materials = new Dictionary<Renderer, Material[]>();
            public Dictionary<GameObject, int> Layers = new Dictionary<GameObject, int>();
        }

        public class GroundTruthSkinColorBackup
        {
            public bool WasActive;
            public Dictionary<GameObject, int> Layers = new Dictionary<GameObject, int>();
            public List<MaterialColorRecord> MaterialRecords = new List<MaterialColorRecord>();
            public Dictionary<Renderer, MaterialPropertyBlock> OriginalPropertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
        }

        public class MaterialColorRecord
        {
            public Material Mat;
            public bool HasColor;
            public Color OriginalColor;
            public bool HasBaseColor;
            public Color OriginalBaseColor;
        }

        public class VirtualObjectStateBackup
        {
            public Dictionary<Renderer, Material[]> Materials = new Dictionary<Renderer, Material[]>();
        }

        private Material _blackMaterialCache;
        private Material _whiteUnlitMaterialCache;

        public void Dispose()
        {
            if (_blackMaterialCache != null)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(_blackMaterialCache);
#else
                UnityEngine.Object.Destroy(_blackMaterialCache);
#endif
                _blackMaterialCache = null;
            }

            if (_whiteUnlitMaterialCache != null)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(_whiteUnlitMaterialCache);
#else
                UnityEngine.Object.Destroy(_whiteUnlitMaterialCache);
#endif
                _whiteUnlitMaterialCache = null;
            }
        }

        /// <summary>
        /// GT撮影用に手メッシュを黒色オクルージョンマスクおよび指定レイヤーに一時変更します。
        /// </summary>
        public GroundTruthStateBackup SetGroundTruthState(GameObject groundTruthObject, string groundTruthCaptureLayer, bool renderGroundTruthAsBlack)
        {
            var backup = new GroundTruthStateBackup();
            if (groundTruthObject == null) return backup;

            // 1. レイヤーの一時変更
            if (!string.IsNullOrEmpty(groundTruthCaptureLayer))
            {
                int targetLayer = LayerMask.NameToLayer(groundTruthCaptureLayer);
                if (targetLayer >= 0)
                {
                    var transforms = groundTruthObject.GetComponentsInChildren<Transform>(true);
                    foreach (var t in transforms)
                    {
                        backup.Layers[t.gameObject] = t.gameObject.layer;
                        t.gameObject.layer = targetLayer;
                    }
                    Debug.Log($"[SICESI] GT撮影のため一時的に Layer を '{groundTruthCaptureLayer}' (ID: {targetLayer}) に変更しました。");
                }
                else
                {
                    Debug.LogWarning($"[SICESI] 指定レイヤー '{groundTruthCaptureLayer}' が見つかりません。ProjectSettings > Tags and Layers を確認してください。");
                }
            }

            // 2. マテリアルを黒に一時変更
            if (renderGroundTruthAsBlack)
            {
                if (_blackMaterialCache == null)
                {
                    Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                    _blackMaterialCache = new Material(unlitShader)
                    {
                        color = Color.black,
                        name = "SICESI_Black_GT_Mat"
                    };
                    if (_blackMaterialCache.HasProperty("_BaseColor"))
                    {
                        _blackMaterialCache.SetColor("_BaseColor", Color.black);
                    }
                }

                var renderers = groundTruthObject.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    backup.Materials[r] = r.sharedMaterials;
                    Material[] blackMats = new Material[r.sharedMaterials.Length];
                    for (int m = 0; m < blackMats.Length; m++)
                    {
                        blackMats[m] = _blackMaterialCache;
                    }
                    r.sharedMaterials = blackMats;
                }
            }

            return backup;
        }

        /// <summary>
        /// 一時変更した手メッシュのレイヤーとマテリアルを元に戻します。
        /// </summary>
        public void RestoreGroundTruthState(GroundTruthStateBackup backup)
        {
            if (backup == null) return;

            foreach (var kvp in backup.Layers)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.layer = kvp.Value;
                }
            }

            foreach (var kvp in backup.Materials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.sharedMaterials = kvp.Value;
                }
            }
        }

        /// <summary>
        /// 撮影用に仮想オブジェクトのマテリアルを Universal Render Pipeline/Unlit の完全な白 (RGB: 1, 1, 1) に設定します。
        /// </summary>
        public VirtualObjectStateBackup SetVirtualObjectUnlitWhiteState(GameObject virtualObject, bool renderVirtualObjectAsUnlitWhite)
        {
            var backup = new VirtualObjectStateBackup();
            if (virtualObject == null || !renderVirtualObjectAsUnlitWhite) return backup;

            if (_whiteUnlitMaterialCache == null)
            {
                Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                _whiteUnlitMaterialCache = new Material(unlitShader)
                {
                    color = Color.white,
                    name = "SICESI_White_Unlit_VO_Mat"
                };
                if (_whiteUnlitMaterialCache.HasProperty("_BaseColor"))
                {
                    _whiteUnlitMaterialCache.SetColor("_BaseColor", Color.white);
                }
                if (_whiteUnlitMaterialCache.HasProperty("_Color"))
                {
                    _whiteUnlitMaterialCache.SetColor("_Color", Color.white);
                }
            }

            var renderers = virtualObject.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null) continue;
                backup.Materials[r] = r.sharedMaterials;
                Material[] whiteMats = new Material[r.sharedMaterials.Length];
                for (int m = 0; m < whiteMats.Length; m++)
                {
                    whiteMats[m] = _whiteUnlitMaterialCache;
                }
                r.sharedMaterials = whiteMats;
            }

            return backup;
        }

        /// <summary>
        /// 一時的に Unlit 白に差し替えた仮想オブジェクトのマテリアルを元に戻します。
        /// </summary>
        public void RestoreVirtualObjectState(VirtualObjectStateBackup backup)
        {
            if (backup == null || backup.Materials == null) return;

            foreach (var kvp in backup.Materials)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.sharedMaterials = kvp.Value;
                }
            }
        }

        /// <summary>
        /// シーン俯瞰撮影用に手メッシュのマテリアル色を肌色に変更し、撮影用レイヤーへ一時変更します。
        /// </summary>
        public GroundTruthSkinColorBackup SetGroundTruthSkinState(GameObject groundTruthObject, string groundTruthCaptureLayer, Color skinColor)
        {
            var backup = new GroundTruthSkinColorBackup();
            if (groundTruthObject == null) return backup;

            backup.WasActive = groundTruthObject.activeSelf;
            if (!groundTruthObject.activeSelf)
            {
                groundTruthObject.SetActive(true);
            }

            if (!string.IsNullOrEmpty(groundTruthCaptureLayer))
            {
                int targetLayer = LayerMask.NameToLayer(groundTruthCaptureLayer);
                if (targetLayer != -1)
                {
                    var transforms = groundTruthObject.GetComponentsInChildren<Transform>(true);
                    foreach (var t in transforms)
                    {
                        backup.Layers[t.gameObject] = t.gameObject.layer;
                        t.gameObject.layer = targetLayer;
                    }
                }
            }

            var skinMPB = new MaterialPropertyBlock();
            skinMPB.SetColor("_Color", skinColor);
            skinMPB.SetColor("_BaseColor", skinColor);

            var renderers = groundTruthObject.GetComponentsInChildren<Renderer>(true);
            var processedMats = new HashSet<Material>();

            foreach (var r in renderers)
            {
                if (r == null) continue;

                var oldMPB = new MaterialPropertyBlock();
                r.GetPropertyBlock(oldMPB);
                backup.OriginalPropertyBlocks[r] = oldMPB;
                r.SetPropertyBlock(skinMPB);

                var mats = r.sharedMaterials;
                if (mats != null)
                {
                    foreach (var mat in mats)
                    {
                        if (mat == null || processedMats.Contains(mat)) continue;
                        processedMats.Add(mat);

                        var record = new MaterialColorRecord
                        {
                            Mat = mat,
                            HasBaseColor = mat.HasProperty("_BaseColor"),
                            HasColor = mat.HasProperty("_Color")
                        };

                        if (record.HasBaseColor) record.OriginalBaseColor = mat.GetColor("_BaseColor");
                        if (record.HasColor) record.OriginalColor = mat.GetColor("_Color");

                        backup.MaterialRecords.Add(record);

                        if (record.HasBaseColor) mat.SetColor("_BaseColor", skinColor);
                        if (record.HasColor) mat.SetColor("_Color", skinColor);
                        try { mat.color = skinColor; } catch { }
                    }
                }
            }

            return backup;
        }

        /// <summary>
        /// 肌色に変更した手メッシュのマテリアル色、レイヤー、およびアクティブ状態を元に戻します。
        /// </summary>
        public void RestoreGroundTruthSkinState(GameObject groundTruthObject, GroundTruthSkinColorBackup backup)
        {
            if (backup == null) return;

            foreach (var kvp in backup.Layers)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.layer = kvp.Value;
                }
            }

            foreach (var record in backup.MaterialRecords)
            {
                if (record.Mat != null)
                {
                    if (record.HasBaseColor) record.Mat.SetColor("_BaseColor", record.OriginalBaseColor);
                    if (record.HasColor) record.Mat.SetColor("_Color", record.OriginalColor);
                    try { record.Mat.color = record.OriginalColor; } catch { }
                }
            }

            foreach (var kvp in backup.OriginalPropertyBlocks)
            {
                if (kvp.Key != null)
                {
                    kvp.Key.SetPropertyBlock(kvp.Value);
                }
            }

            if (groundTruthObject != null && groundTruthObject.activeSelf != backup.WasActive)
            {
                groundTruthObject.SetActive(backup.WasActive);
            }
        }
    }
}
