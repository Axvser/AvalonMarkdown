using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AvalonMarkdown.Views;

/// <summary>
/// Payload the preview posts for a save. See renderer.js's ctxRun().
/// </summary>
internal sealed class ExportPayload
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "png";
    [JsonPropertyName("encoding")] public string Encoding { get; set; } = "utf8";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("data")] public string? Data { get; set; }
}

/// <summary>
/// Source-generated metadata for every JSON shape the library deserializes.
///
/// The Browser/WASM head runs with reflection-based serialization disabled
/// (<c>JsonSerializerIsReflectionDisabled</c>), so
/// <c>JsonSerializer.Deserialize&lt;T&gt;(...)</c> is not safe there. A
/// <see cref="JsonSerializerContext"/> gives the serializer the same metadata
/// without reflection.
/// </summary>
[JsonSerializable(typeof(ExportPayload))]
[JsonSerializable(typeof(MarkdownHeading))]
[JsonSerializable(typeof(List<MarkdownHeading>))]
internal sealed partial class MarkdownJsonContext : JsonSerializerContext
{
}
