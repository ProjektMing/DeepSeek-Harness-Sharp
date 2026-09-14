using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

#pragma warning disable CA2255 // 库内 JSON 上下文自注册:模块初始化是最早且无依赖的注册时机

namespace Dsh.Llm;

/** Dsh.Llm 中进入会话/工具 JSON 的类型根;多态根由自定义转换器接管,这里只提供元数据。 */
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Message))]
[JsonSerializable(typeof(UserMessage))]
[JsonSerializable(typeof(AssistantMessage))]
[JsonSerializable(typeof(ToolResultMessage))]
[JsonSerializable(typeof(MessageSource))]
[JsonSerializable(typeof(UserMessageSource))]
[JsonSerializable(typeof(PluginMessageSource))]
[JsonSerializable(typeof(ModelMessageSource))]
[JsonSerializable(typeof(ToolMessageSource))]
[JsonSerializable(typeof(GoalMessageSource))]
[JsonSerializable(typeof(UnknownMessageSource))]
[JsonSerializable(typeof(IReadOnlyList<Message>))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(TextBlock))]
[JsonSerializable(typeof(ReasoningBlock))]
[JsonSerializable(typeof(ImageBlock))]
[JsonSerializable(typeof(ToolCallBlock))]
[JsonSerializable(typeof(ToolResultBlock))]
[JsonSerializable(typeof(UnknownContentBlock))]
[JsonSerializable(typeof(IReadOnlyList<ContentBlock>))]
[JsonSerializable(typeof(ImageAttachmentRef))]
[JsonSerializable(typeof(ToolSchema))]
[JsonSerializable(typeof(IReadOnlyList<ToolSchema>))]
[JsonSerializable(typeof(StreamChunk))]
[JsonSerializable(typeof(StreamChunk.BlockStart))]
[JsonSerializable(typeof(StreamChunk.TextDelta))]
[JsonSerializable(typeof(StreamChunk.ReasoningDelta))]
[JsonSerializable(typeof(StreamChunk.ToolCallDelta))]
[JsonSerializable(typeof(StreamChunk.BlockEnd))]
[JsonSerializable(typeof(StreamChunk.Usage))]
[JsonSerializable(typeof(StreamChunk.Finish))]
[JsonSerializable(typeof(FinishReason))]
[JsonSerializable(typeof(FinishReason.Stop))]
[JsonSerializable(typeof(FinishReason.ToolCalls))]
[JsonSerializable(typeof(FinishReason.MaxTokens))]
[JsonSerializable(typeof(FinishReason.Aborted))]
[JsonSerializable(typeof(FinishReason.Error))]
[JsonSerializable(typeof(FinishReason.Unknown))]
[JsonSerializable(typeof(TokenUsage))]
[JsonSerializable(typeof(ReplayEnvelope))]
[JsonSerializable(typeof(LlmFailure))]
[JsonSerializable(typeof(MessageRole))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class DshLlmJsonContext : JsonSerializerContext
{
}

internal static class DshLlmJsonRegistration
{
    [ModuleInitializer]
    internal static void Register() => DshJson.RegisterResolver(DshLlmJsonContext.Default);
}
