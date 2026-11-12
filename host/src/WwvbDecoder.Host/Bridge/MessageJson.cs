using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using WwvbDecoder.Core;

namespace WwvbDecoder.Host.Bridge;

/// <summary>
/// Provides methods for serializing messages to JSON format.
/// </summary>
public static class MessageJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new UtcZuluConverter(),
            new MillisecondsConverter(),
        },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { FrameSyncAsKindUnion, TimeSpanPropertiesEndInMs },
        },
    };

    /// <summary>
    /// Serializes <see cref="FrameSyncEvent"/> as a union discriminated by <c>kind</c>, with
    /// <see cref="FrameSyncEvent.SlotFilled.Kind"/> written as <c>symbol</c> to stay out of its way.
    /// </summary>
    private static void FrameSyncAsKindUnion(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(FrameSyncEvent))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(FrameSyncEvent.Searching), "searching"),
                    new JsonDerivedType(typeof(FrameSyncEvent.SlotFilled), "slotFilled"),
                    new JsonDerivedType(typeof(FrameSyncEvent.FrameComplete), "frameComplete"),
                    new JsonDerivedType(typeof(FrameSyncEvent.SyncLost), "syncLost"),
                },
            };
        }
        else if (typeInfo.Type == typeof(FrameSyncEvent.SlotFilled))
        {
            typeInfo.Properties.Single(p => p.Name == "kind").Name = "symbol";
        }
    }

    /// <summary>
    /// Appends <c>Ms</c> to every <see cref="TimeSpan"/> property name, so <c>OffsetFromPc</c>
    /// becomes <c>offsetFromPcMs</c> to match the number <see cref="MillisecondsConverter"/> writes.
    /// </summary>
    private static void TimeSpanPropertiesEndInMs(JsonTypeInfo typeInfo)
    {
        foreach (JsonPropertyInfo property in typeInfo.Properties)
        {
            Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type == typeof(TimeSpan))
            {
                property.Name += "Ms";
            }
        }
    }

    /// <summary>Writes <see cref="DateTimeOffset"/> as ISO-8601 UTC with a <c>Z</c> suffix.</summary>
    private sealed class UtcZuluConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => reader.GetDateTimeOffset();

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset value,
            JsonSerializerOptions options
        ) =>
            writer.WriteStringValue(
                value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            );
    }

    /// <summary>Writes <see cref="TimeSpan"/> as a number of milliseconds, fractional and signed.</summary>
    private sealed class MillisecondsConverter : JsonConverter<TimeSpan>
    {
        public override TimeSpan Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => TimeSpan.FromMilliseconds(reader.GetDouble());

        public override void Write(
            Utf8JsonWriter writer,
            TimeSpan value,
            JsonSerializerOptions options
        ) => writer.WriteNumberValue(value.TotalMilliseconds);
    }

    /// <summary>
    /// Serializes a message envelope with the given type, sequence number, and payload to JSON.
    /// </summary>
    /// <param name="type">The type of the message.</param>
    /// <param name="seq">The sequence number of the message.</param>
    /// <param name="payload">The payload of the message.</param>
    /// <typeparam name="TPayload">The type of the payload.</typeparam>
    /// <returns>The JSON representation of the message envelope.</returns>
    public static string Serialize<TPayload>(string type, int seq, TPayload payload) =>
        JsonSerializer.Serialize(new Envelope<TPayload>(type, seq, payload), Options);
}
