using System;
using System.Linq;
using System.Runtime.CompilerServices;
using GlacialCache.PostgreSQL.Models.CommandParameters;
using Npgsql;
using NpgsqlTypes;

namespace GlacialCache.PostgreSQL.Extensions;

/// <summary>
/// Extension methods for NpgsqlCommand to add type-safe parameter groups.
/// </summary>
internal static class NpgsqlCommandExtensions
{
    /// <summary>
    /// Converts a property reference to a SQL parameter name by prepending '@'.
    /// </summary>
    /// <typeparam name="T">The type of the property value</typeparam>
    /// <param name="value">The property value</param>
    /// <param name="expression">Automatically captured expression by the compiler</param>
    /// <returns>The parameter name with '@' prefix (e.g., "@Key")</returns>
    private static string AsName<T>(T value,
        [CallerArgumentExpression(nameof(value))] string expression = "") =>
            expression.Split('.').Select(s => $"@{s}").LastOrDefault() ?? "@NoName";

    /// <summary>
    /// Adds parameters for SET operations.
    /// </summary>
    public static void AddParameters(this NpgsqlCommand command, SetEntryParameters item)
    {
        command.Parameters.AddWithValue(AsName(item.Key), item.Key);
        command.Parameters.AddWithValue(AsName(item.Value), item.Value);
        command.Parameters.AddWithValue(AsName(item.Now), item.Now);

        if (item.RelativeInterval.HasValue)
            command.Parameters.AddWithValue(AsName(item.RelativeInterval), item.RelativeInterval.Value);
        else
        {
            var p = command.Parameters.Add(AsName(item.RelativeInterval), NpgsqlDbType.Interval);
            p.Value = DBNull.Value;
        }

        if (item.SlidingInterval.HasValue)
            command.Parameters.AddWithValue(AsName(item.SlidingInterval), item.SlidingInterval.Value);
        else
        {
            var p = command.Parameters.Add(AsName(item.SlidingInterval), NpgsqlDbType.Interval);
            p.Value = DBNull.Value;
        }

        command.Parameters.AddWithValue(AsName(item.ValueType), item.ValueType ?? (object)DBNull.Value);
    }

    /// <summary>
    /// Adds parameters for GET operations.
    /// </summary>
    public static void AddParameters(this NpgsqlCommand command, GetEntryParameters item)
    {
        command.Parameters.AddWithValue(AsName(item.Key), item.Key);
        command.Parameters.AddWithValue(AsName(item.Now), item.Now);
    }

    /// <summary>
    /// Adds parameters for REMOVE operations.
    /// </summary>
    public static void AddParameters(this NpgsqlCommand command, RemoveEntryParameters item)
    {
        command.Parameters.AddWithValue(AsName(item.Key), item.Key);
    }

    /// <summary>
    /// Adds parameters for REFRESH operations.
    /// </summary>
    public static void AddParameters(this NpgsqlCommand command, RefreshEntryParameters item)
    {
        command.Parameters.AddWithValue(AsName(item.Key), item.Key);
        command.Parameters.AddWithValue(AsName(item.Now), item.Now);
    }
}

