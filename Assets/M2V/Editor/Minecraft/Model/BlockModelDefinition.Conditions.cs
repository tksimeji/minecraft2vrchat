#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using fNbt;
using M2V.Editor.Minecraft.World;

namespace M2V.Editor.Minecraft.Model
{
    /// <seealso href="https://mcsrc.dev/#1/26.1-snapshot-5/net/minecraft/client/renderer/block/model/multipart/Condition"/>
    public interface ICondition
    {
        bool Test(IReadOnlyDictionary<string, NbtTag> properties);
    }

    /// <seealso href="https://mcsrc.dev/#1/26.1-snapshot-5/net/minecraft/client/renderer/block/model/multipart/CombinedCondition"/>
    public sealed record CombinedCondition(CombinedCondition.Operation Op, List<ICondition> Terms) : ICondition
    {
        public enum Operation
        {
            And,
            Or
        }

        public bool Test(IReadOnlyDictionary<string, NbtTag> properties)
        {
            if (Terms.Count == 0) return true;
            return Op != Operation.And
                ? Terms.Any(term => term.Test(properties))
                : Terms.All(term => term.Test(properties));
        }
    }

    /// <seealso href="https://mcsrc.dev/#1/26.1-snapshot-5/net/minecraft/client/renderer/block/model/multipart/KeyValueCondition"/>
    public sealed record KeyValueCondition(Dictionary<string, string> Tests) : ICondition
    {
        public bool Test(IReadOnlyDictionary<string, NbtTag> properties)
        {
            if (Tests.Count == 0) return true;

            foreach (var test in Tests)
            {
                if (!properties.TryGetValue(test.Key, out var value)
                    || !MatchesSelector(BlockState.FormatNbtTag(value), test.Value))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesSelector(string value, string selector)
        {
            var options = selector.Split('|');
            return options.Any(option => string.Equals(value, option, StringComparison.Ordinal));
        }
    }

    internal sealed class ConditionJsonConverter : JsonConverter<ICondition>
    {
        private const string AndKey = "AND";
        private const string OrKey = "OR";

        public override ICondition? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return null;
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var rootElement = document.RootElement;

            if (rootElement.TryGetProperty(OrKey, out var orToken))
            {
                var list = ReadConditionList(orToken, options);
                return new CombinedCondition(CombinedCondition.Operation.Or, list);
            }

            if (rootElement.TryGetProperty(AndKey, out var andToken))
            {
                var list = ReadConditionList(andToken, options);
                return new CombinedCondition(CombinedCondition.Operation.And, list);
            }

            var singleDictionary =
                JsonSerializer.Deserialize<Dictionary<string, string>>(rootElement.GetRawText(), options);
            return singleDictionary != null ? new KeyValueCondition(singleDictionary) : null;
        }

        public override void Write(Utf8JsonWriter writer, ICondition value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }

        private static List<ICondition> ReadConditionList(JsonElement token, JsonSerializerOptions options)
        {
            if (token.ValueKind != JsonValueKind.Array)
            {
                return new List<ICondition>();
            }

            return token.EnumerateArray()
                .Select(child =>
                    JsonSerializer.Deserialize<Dictionary<string, string>>(child.GetRawText(), options))
                .Where(dictionary => dictionary is not null)
                .Select(dictionary => (ICondition)new KeyValueCondition(dictionary!))
                .ToList();
        }
    }
}