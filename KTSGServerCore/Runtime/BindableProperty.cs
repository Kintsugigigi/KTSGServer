using System;
using System.Collections.Generic;

namespace KTSG.Server;

public sealed class BindableProperty<T>
{
    private T _value;
    private Action<T>? _onValueChanged;

    public BindableProperty(T defaultValue = default!)
    {
        _value = defaultValue;
    }

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            _onValueChanged?.Invoke(_value);
        }
    }

    public IUnRegister RegisterOnValueChanged(Action<T> action)
    {
        _onValueChanged += action;
        return PureClassPool.Get<BindablePropertyUnRegister<T>>(true).Init(this, action);
    }

    public void SetValueWithoutNotify(T value)
    {
        _value = value;
    }

    public void ClearOnValueChanged()
    {
        _onValueChanged = null;
    }

    public void UnRegisterOnValueChanged(Action<T> action)
    {
        _onValueChanged -= action;
    }
}

public sealed class BindablePropertyUnRegister<T> : IUnRegister
{
    private BindableProperty<T>? _bindableProperty;
    private Action<T>? _unregisterAction;
    private bool _recycled;

    public BindablePropertyUnRegister<T> Init(BindableProperty<T> bindableProperty, Action<T> action)
    {
        _recycled = false;
        _bindableProperty = bindableProperty;
        _unregisterAction = action;
        return this;
    }

    public void UnRegister()
    {
        if (_recycled)
        {
            return;
        }

        _recycled = true;
        if (_bindableProperty != null && _unregisterAction != null)
        {
            _bindableProperty.UnRegisterOnValueChanged(_unregisterAction);
        }

        _bindableProperty = null;
        _unregisterAction = null;
        PureClassPool.Return(this);
    }
}
