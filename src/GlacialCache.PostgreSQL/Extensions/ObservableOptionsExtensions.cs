using System.Reflection;
using Microsoft.Extensions.Logging;
using GlacialCache.PostgreSQL.Configuration;

namespace GlacialCache.PostgreSQL.Extensions;

/// <summary>
/// Extension methods for synchronizing observable properties across options classes.
/// </summary>
public static class ObservableOptionsExtensions
{
    /// <summary>
    /// Synchronizes all ObservableProperty&lt;T&gt; properties from newOptions to current.
    /// This method uses reflection to find and update all observable properties, providing
    /// a DRY approach for configuration synchronization across CacheOptions, ConnectionOptions, and ConnectionPoolOptions.
    /// </summary>
    /// <typeparam name="T">The type of the options class containing ObservableProperty&lt;T&gt; properties.</typeparam>
    /// <param name="current">The current options instance to update.</param>
    /// <param name="newOptions">The new options instance containing updated values.</param>
    /// <param name="logger">Logger for error reporting and change tracking.</param>
    public static void SyncFromExternalChanges<T>(this T current, T newOptions, ILogger logger)
        where T : class
    {
        try
        {
            // Use reflection to sync all ObservableProperty<T> properties
            var properties = typeof(T).GetProperties()
                .Where(p => p.PropertyType.IsGenericType &&
                           p.PropertyType.GetGenericTypeDefinition() == typeof(ObservableProperty<>));

            foreach (var prop in properties)
            {
                var currentObservable = prop.GetValue(current);
                var newObservable = prop.GetValue(newOptions);

                if (currentObservable != null && newObservable != null)
                {
                    var valueProperty = prop.PropertyType.GetProperty("Value");
                    if (valueProperty != null)
                    {
                        var newValue = valueProperty.GetValue(newObservable);
                        valueProperty.SetValue(currentObservable, newValue);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync observable properties for {Type}", typeof(T).Name);
        }
    }
}
