// =============================================================================
// PCDOptionalTexture.cs
// -----------------------------------------------------------------------------
// デバッグ・記録用途など「特定フレームでのみ必要なフル解像度テクスチャ」を扱う RTHandle ラッパー。
//
// 不要なフレームでは 1×1 のダミーテクスチャを返す。シェーダー側の UAV 書き込みは
// 範囲外アクセスとしてハードウェア側で破棄されるため、カーネルを変更せずにメモリ帯域を消費しなくなる。
//
// フル解像度側は一度確保すると解像度変更または Release まで保持する。
// ワンショット記録（AsyncGPUReadback）の完了前に解放してしまう事故を防ぐため。
// =============================================================================
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// 必要時のみフル解像度テクスチャを有効化し、それ以外は 1×1 ダミーを返す RTHandle ラッパー。
/// </summary>
internal sealed class PCDOptionalTexture : IDisposable
{
    private readonly GraphicsFormat _format;
    private readonly string _name;

    private RTHandle _full;
    private RTHandle _dummy;
    private int _width;
    private int _height;

    /// <summary> 現在フレームでフル解像度テクスチャがバインド対象かどうか </summary>
    public bool IsActive { get; private set; }

    /// <summary> 現在フレームでバインドすべきハンドル（有効時はフル解像度、無効時は 1×1 ダミー） </summary>
    public RTHandle Handle => IsActive ? _full : _dummy;

    public PCDOptionalTexture(GraphicsFormat format, string name)
    {
        _format = format;
        _name = name;
    }

    /// <summary>
    /// 現在フレームの必要性と解像度に応じてハンドルを更新する。
    /// 確保が発生した場合のみ true を返す（毎フレーム呼ばれても安全）。
    /// </summary>
    public bool Update(bool needed, int width, int height)
    {
        bool allocated = false;

        if (_dummy == null)
        {
            _dummy = RTHandles.Alloc(1, 1, colorFormat: _format, enableRandomWrite: true, name: _name + "_Dummy");
        }

        if (_full != null && (_width != width || _height != height))
        {
            _full.Release();
            _full = null;
        }

        if (needed && _full == null)
        {
            _full = RTHandles.Alloc(width, height, colorFormat: _format, enableRandomWrite: true, name: _name);
            _width = width;
            _height = height;
            allocated = true;
        }

        IsActive = needed;
        return allocated;
    }

    /// <summary> フル解像度・ダミー双方を解放する。 </summary>
    public void Release()
    {
        _full?.Release();
        _full = null;
        _dummy?.Release();
        _dummy = null;
        _width = 0;
        _height = 0;
        IsActive = false;
    }

    public void Dispose() => Release();
}
