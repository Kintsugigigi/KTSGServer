using System;
using System.Collections.Generic;
using KTSG.Server.Model;

namespace KTSG.Server;

public abstract class Query
{
    private readonly List<IUnRegister> _unregisters = new();
    private bool _recycled;

    protected PlayerData Player { get; private set; } = null!;
    protected RoleData Role { get; private set; } = null!;
    protected TypeEventBus Events => Role.RuntimeEvents ?? throw new InvalidOperationException("Role.RuntimeEvents is null.");

    public BindableProperty<bool> IsCompleted { get; } = new(false);

    public void Init(
        PlayerData player,
        int arg1 = 0,
        int arg2 = 0,
        int arg3 = 0,
        int arg4 = 0,
        int arg5 = 0,
        int arg6 = 0,
        int arg7 = 0,
        int arg8 = 0)
    {
        Reset();
        _recycled = false;
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Role = player.CurrentRole ?? throw new InvalidOperationException("Player.CurrentRole is null.");
        if (Role.RuntimeEvents == null)
        {
            throw new InvalidOperationException("Role runtime has not been initialized.");
        }

        OnInit(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
    }

    protected void Listen(IUnRegister unregister)
    {
        if (unregister != null)
        {
            _unregisters.Add(unregister);
        }
    }

    protected bool Compare(int left, int right, cfg.ECompType compType)
    {
        return compType switch
        {
            cfg.ECompType.Equals => left == right,
            cfg.ECompType.Greater => left >= right,
            cfg.ECompType.Lower => left <= right,
            _ => false
        };
    }

    protected abstract void OnInit(int arg1, int arg2, int arg3, int arg4, int arg5, int arg6, int arg7, int arg8);
    protected abstract int GetNow();
    protected abstract bool CheckCompleted(int count);

    public virtual void Reset()
    {
        for (int i = 0; i < _unregisters.Count; i++)
        {
            _unregisters[i].UnRegister();
        }

        _unregisters.Clear();
        IsCompleted.ClearOnValueChanged();
        IsCompleted.Value = false;
        Player = null!;
        Role = null!;
        OnReset();
    }

    protected virtual void OnReset()
    {
    }

    public void Recycle()
    {
        if (_recycled)
        {
            return;
        }

        _recycled = true;
        Reset();
        PureClassPool.Return(this);
    }
}
