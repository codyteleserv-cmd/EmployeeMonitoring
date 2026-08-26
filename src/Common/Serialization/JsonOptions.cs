using System.Text.Json;
using System.Text.Json.Serialization;
using EmployeeMonitoring.Contracts;

namespace EmployeeMonitoring.Common.Serialization;

/// <summary>
/// JSON serialization options optimized for the monitoring platform.
/// </summary>
public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeOffsetConverter(),
            new TimeSpanConverter()
        },
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static readonly JsonSerializerOptions WithIndentation = new(Default)
    {
        WriteIndented = true
    };
}

/// <summary>
/// Converts DateTimeOffset to/from Unix milliseconds (int64).
/// </summary>
public class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var milliseconds = reader.GetInt64();
            return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            return DateTimeOffset.Parse(reader.GetString()!);
        }
        throw new JsonException("Expected number or string for DateTimeOffset");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.ToUnixTimeMilliseconds());
    }
}

/// <summary>
/// Converts TimeSpan to/from total milliseconds (int64).
/// </summary>
public class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return TimeSpan.FromMilliseconds(reader.GetInt64());
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            return TimeSpan.Parse(reader.GetString()!);
        }
        throw new JsonException("Expected number or string for TimeSpan");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((long)value.TotalMilliseconds);
    }
}

/// <summary>
/// Protobuf serialization helpers.
/// </summary>
public static class ProtobufExtensions
{
    public static byte[] ToByteArray(this Google.Protobuf.IMessage message)
    {
        return message.ToByteArray();
    }

    public static T ParseFrom<T>(this byte[] data) where T : Google.Protobuf.IMessage<T>, new()
    {
        return new T().MergeFrom(data);
    }
}