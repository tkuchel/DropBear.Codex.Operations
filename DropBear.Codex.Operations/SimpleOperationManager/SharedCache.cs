﻿namespace DropBear.Codex.Operations.SimpleOperationManager;

public class SharedCache
{
    private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Set<T>(string key, T value)
    {
        if (value is not null)
        {
            _cache[key] = value;
        }
    }

    public T Get<T>(string key)
    {
        if (_cache.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        throw new KeyNotFoundException($"Key '{key}' not found in cache or is not of type {typeof(T)}");
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_cache.TryGetValue(key, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }
}
