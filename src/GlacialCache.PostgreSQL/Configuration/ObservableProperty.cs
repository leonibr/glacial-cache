using System.ComponentModel;
using Microsoft.Extensions.Logging;
using GlacialCache.Logging;

namespace GlacialCache.PostgreSQL.Configuration;

/// <summary>
/// Observable property implementation that provides change notifications and logging.
/// </summary>
/// <typeparam name="T">The type of the property value.</typeparam>
public class ObservableProperty<T> : INotifyPropertyChanged
{
    private readonly ILogger? _logger;
    private readonly string _propertyName;
    private readonly object _syncLock = new();
    private T _value = default!;

    /// <summary>
    /// Initializes a new instance of the ObservableProperty class.
    /// </summary>
    /// <param name="propertyName">The name of the property for logging purposes.</param>
    /// <param name="logger">Optional logger for change notifications.</param>
    public ObservableProperty(string propertyName = "", ILogger? logger = null)
    {
        _propertyName = propertyName;
        _logger = logger;
    }

    /// <summary>
    /// Gets or sets the value of the property.
    /// </summary>
    public T Value
    {
        get
        {
            lock (_syncLock)
            {
                return _value;
            }
        }
        set
        {
            T oldValue;
            bool hasChanged;

            lock (_syncLock)
            {
                oldValue = _value;
                hasChanged = !EqualityComparer<T>.Default.Equals(_value, value);
                if (hasChanged)
                {
                    _value = value;
                }
            }

            if (hasChanged)
            {
                try
                {
                    // Log property change using established pattern
                    _logger?.LogConfigurationPropertyChanged(_propertyName, oldValue, value);

                    // Raise PropertyChanged event
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs<T>(oldValue, value, _propertyName));
                }
                catch (Exception ex)
                {
                    _logger?.LogObservablePropertyError(_propertyName, ex);
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Implicitly converts an ObservableProperty to its underlying value.
    /// </summary>
    /// <param name="property">The observable property to convert.</param>
    public static implicit operator T(ObservableProperty<T> property) => property.Value;

    /// <summary>
    /// Implicitly converts a value to an ObservableProperty.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static implicit operator ObservableProperty<T>(T value) => new() { Value = value };

    /// <summary>
    /// Returns the string representation of the current value.
    /// </summary>
    public override string ToString() => Value?.ToString() ?? string.Empty;

    /// <summary>
    /// Determines whether the specified object is equal to the current value.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is ObservableProperty<T> other)
            return EqualityComparer<T>.Default.Equals(Value, other.Value);

        if (obj is T directValue)
            return EqualityComparer<T>.Default.Equals(Value, directValue);

        return false;
    }

    /// <summary>
    /// Returns the hash code for the current value.
    /// </summary>
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;
}
