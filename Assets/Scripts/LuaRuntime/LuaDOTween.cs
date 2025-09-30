// Assets/Scripts/LuaRuntime/LuaDOTween.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;
using UnityEngine;
using MoonSharp.Interpreter;
using LuaProxies; // your proxies

[MoonSharpUserData]
public class LuaDOTween
{
    // Store tweens/sequences by handle for control from Lua
    readonly Dictionary<int, Tween> _tweens = new();
    readonly Dictionary<int, Sequence> _seqs = new();
    int _nextId = 1;

    // --- Helpers to extract underlying Unity objects from proxies ---
    static Transform From(TransformProxy tp)
    {
        if (tp == null) return null;
        var f = typeof(TransformProxy).GetField("_transform", BindingFlags.Instance | BindingFlags.NonPublic);
        return (Transform)f?.GetValue(tp);
    }
    static GameObject From(GameObjectProxy gp)
    {
        if (gp == null) return null;
        var f = typeof(GameObjectProxy).GetField("_gameObject", BindingFlags.Instance | BindingFlags.NonPublic);
        return (GameObject)f?.GetValue(gp);
    }

    static Ease ParseEase(string easeOrEmpty)
    {
        if (string.IsNullOrEmpty(easeOrEmpty)) return Ease.Linear;
        return Enum.TryParse<Ease>(easeOrEmpty, true, out var e) ? e : Ease.Linear;
    }

    // -------------- Basic Tweens (Transform) --------------

    // Move a TransformProxy to (x,y,z) over duration, optional ease (e.g., "OutQuad")
    public int MoveTo(TransformProxy tProxy, double x, double y, double z, double duration, string ease = "")
    {
        var t = From(tProxy);
        if (!t) return -1;
        var id = _nextId++;
        var tw = t.DOMove(new Vector3((float)x, (float)y, (float)z), (float)duration).SetEase(ParseEase(ease));
        _tweens[id] = tw;
        return id;
    }

    // Rotate to absolute Euler over duration
    public int RotateTo(TransformProxy tProxy, double x, double y, double z, double duration, string ease = "")
    {
        var t = From(tProxy);
        if (!t) return -1;
        var id = _nextId++;
        var tw = t.DORotate(new Vector3((float)x, (float)y, (float)z), (float)duration, RotateMode.Fast).SetEase(ParseEase(ease));
        _tweens[id] = tw;
        return id;
    }

    // Scale to over duration
    public int ScaleTo(TransformProxy tProxy, double x, double y, double z, double duration, string ease = "")
    {
        var t = From(tProxy);
        if (!t) return -1;
        var id = _nextId++;
        var tw = t.DOScale(new Vector3((float)x, (float)y, (float)z), (float)duration).SetEase(ParseEase(ease));
        _tweens[id] = tw;
        return id;
    }

    // -------------- CanvasGroup / Renderer helpers (optional) --------------

    public int CanvasFade(GameObjectProxy goProxy, double alpha, double duration, string ease = "")
    {
        var go = From(goProxy);
        if (!go) return -1;
        if (!go.TryGetComponent<CanvasGroup>(out var cg)) return -1;
        var id = _nextId++;
        var tw = cg.DOFade((float)alpha, (float)duration).SetEase(ParseEase(ease));
        _tweens[id] = tw;
        return id;
    }

    // -------------- Controls (Tween) --------------

    public bool Kill(int handle, bool complete = false)
    {
        if (_tweens.TryGetValue(handle, out var tw) && tw.IsActive())
        {
            if (complete) tw.Complete(true);
            tw.Kill();
            _tweens.Remove(handle);
            return true;
        }
        return false;
    }

    public bool Pause(int handle)
    {
        if (_tweens.TryGetValue(handle, out var tw) && tw.IsActive()) { tw.Pause(); return true; }
        return false;
    }

    public bool Play(int handle)
    {
        if (_tweens.TryGetValue(handle, out var tw) && tw.IsActive()) { tw.Play(); return true; }
        return false;
    }

    public bool SetLoops(int handle, int loops = -1, string loopType = "restart")
    {
        if (_tweens.TryGetValue(handle, out var tw) && tw.IsActive())
        {
            var lt = loopType.Equals("yoyo", StringComparison.OrdinalIgnoreCase) ? LoopType.Yoyo
                   : loopType.Equals("incremental", StringComparison.OrdinalIgnoreCase) ? LoopType.Incremental
                   : LoopType.Restart;
            tw.SetLoops(loops, lt);
            return true;
        }
        return false;
    }

    public bool SetEase(int handle, string ease)
    {
        if (_tweens.TryGetValue(handle, out var tw) && tw.IsActive())
        {
            tw.SetEase(ParseEase(ease));
            return true;
        }
        return false;
    }

    // -------------- Sequences --------------

    public int SeqCreate()
    {
        var id = _nextId++;
        _seqs[id] = DOTween.Sequence();
        return id;
    }

    public bool SeqAppendMove(int seqHandle, TransformProxy tProxy, double x, double y, double z, double duration, string ease = "")
    {
        if (!_seqs.TryGetValue(seqHandle, out var seq) || !seq.IsActive()) return false;
        var t = From(tProxy); if (!t) return false;
        seq.Append(t.DOMove(new Vector3((float)x,(float)y,(float)z),(float)duration).SetEase(ParseEase(ease)));
        return true;
    }

    public bool SeqJoinScale(int seqHandle, TransformProxy tProxy, double x, double y, double z, double duration, string ease = "")
    {
        if (!_seqs.TryGetValue(seqHandle, out var seq) || !seq.IsActive()) return false;
        var t = From(tProxy); if (!t) return false;
        seq.Join(t.DOScale(new Vector3((float)x,(float)y,(float)z),(float)duration).SetEase(ParseEase(ease)));
        return true;
    }

    public bool SeqAppendInterval(int seqHandle, double seconds)
    {
        if (!_seqs.TryGetValue(seqHandle, out var seq) || !seq.IsActive()) return false;
        seq.AppendInterval((float)seconds);
        return true;
    }

    public bool SeqSetLoops(int seqHandle, int loops = -1, string loopType = "restart")
    {
        if (!_seqs.TryGetValue(seqHandle, out var seq) || !seq.IsActive()) return false;
        var lt = loopType.Equals("yoyo", StringComparison.OrdinalIgnoreCase) ? LoopType.Yoyo
               : loopType.Equals("incremental", StringComparison.OrdinalIgnoreCase) ? LoopType.Incremental
               : LoopType.Restart;
        seq.SetLoops(loops, lt); return true;
    }

    public bool SeqPlay(int seqHandle)
    {
        if (_seqs.TryGetValue(seqHandle, out var seq) && seq.IsActive()) { seq.Play(); return true; }
        return false;
    }

    public bool SeqKill(int seqHandle, bool complete = false)
    {
        if (_seqs.TryGetValue(seqHandle, out var seq) && seq.IsActive())
        {
            if (complete) seq.Complete(true);
            seq.Kill();
            _seqs.Remove(seqHandle);
            return true;
        }
        return false;
    }
}
