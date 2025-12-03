using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GlacialCache.PostgreSQL.Configuration;

/// <summary>
/// Enhanced PropertyChangedEventArgs that includes old and new values.
/// </summary>
/// <typeparam name="T">The type of the property value.</typeparam>
public class PropertyChangedEventArgs<T> : PropertyChangedEventArgs
{
    /// <summary>
    /// Gets the old value of the property before the change.
    /// </summary>
    public T OldValue { get; }

    /// <summary>
    /// Gets the new value of the property after the change.
    /// </summary>
    public T NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the PropertyChangedEventArgs class.
    /// </summary>
    /// <param name="oldValue">The old value of the property.</param>
    /// <param name="newValue">The new value of the property.</param>
    /// <param name="propertyName">The name of the property that changed.</param>
    public PropertyChangedEventArgs(T oldValue, T newValue, [CallerMemberName] string? propertyName = null)
        : base(propertyName)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }
}
