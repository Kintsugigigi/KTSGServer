using System;
using System.Collections.Generic;

namespace KTSG.Server;

public interface ITypeEventBus
{
    void Publish<T>() where T : new();
    void Publish<T>(T evt);
    IUnRegister Subscribe<T>(Action<T> action);
    void Unsubscribe<T>(Action<T> action);
}

public interface IUnRegister
{
    void UnRegister();
}

public sealed class TypeEventBusUnRegister<T> : IUnRegister
{
    private ITypeEventBus? _typeEventBus;
    private Action<T>? _unregisterAction;
    private bool _recycled;

    public TypeEventBusUnRegister<T> Init(ITypeEventBus typeEventBus, Action<T> action)
    {
        _recycled = false;
        _typeEventBus = typeEventBus;
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
        if (_typeEventBus != null && _unregisterAction != null)
        {
            _typeEventBus.Unsubscribe(_unregisterAction);
        }

        _typeEventBus = null;
        _unregisterAction = null;
        PureClassPool.Return(this);
    }
}

public sealed class TypeEventBus : ITypeEventBus
{
    private readonly Dictionary<Type, IRegistration> _registrations = new();

    public void Publish<T>() where T : new()
    {
        Publish(new T());
    }

    public void Publish<T>(T evt)
    {
        if (_registrations.TryGetValue(typeof(T), out IRegistration? registration))
        {
            ((Registration<T>)registration).Handlers?.Invoke(evt);
        }
    }

    public IUnRegister Subscribe<T>(Action<T> action)
    {
        Type eventType = typeof(T);
        if (!_registrations.TryGetValue(eventType, out IRegistration? registration))
        {
            registration = new Registration<T>();
            _registrations[eventType] = registration;
        }

        Registration<T> typedRegistration = (Registration<T>)registration;
        typedRegistration.Handlers += action;
        return PureClassPool.Get<TypeEventBusUnRegister<T>>(true).Init(this, action);
    }

    public void Unsubscribe<T>(Action<T> action)
    {
        if (_registrations.TryGetValue(typeof(T), out IRegistration? registration))
        {
            Registration<T> typedRegistration = (Registration<T>)registration;
            typedRegistration.Handlers -= action;
            if (typedRegistration.Handlers == null)
            {
                _registrations.Remove(typeof(T));
            }
        }
    }

    public void Clear()
    {
        _registrations.Clear();
    }

    private interface IRegistration
    {
    }

    private sealed class Registration<T> : IRegistration
    {
        public Action<T>? Handlers;
    }
}
