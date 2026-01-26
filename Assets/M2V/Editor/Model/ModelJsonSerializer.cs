using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace M2V.Editor.Model
{
    internal static class ModelJsonSerializer
    {
        internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new List<JsonConverter>
            {
                new VariantListConverter(),
                new ApplyListConverter(),
                new WhenConverter(),
                new ModelFacesConverter()
            }
        };

        internal static readonly JsonSerializer SafeSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        });

        internal static List<Variant> DeserializeVariantList(JToken token)
        {
            if (token == null)
            {
                return new List<Variant>();
            }

            if (token.Type == JTokenType.Array)
            {
                var list = new List<Variant>();
                foreach (var child in token.Children())
                {
                    var item = child.ToObject<Variant>(SafeSerializer);
                    if (item != null)
                    {
                        list.Add(item);
                    }
                }

                return list;
            }

            if (token.Type == JTokenType.Object)
            {
                var single = token.ToObject<Variant>(SafeSerializer);
                return single != null ? new List<Variant> { single } : new List<Variant>();
            }

            return new List<Variant>();
        }
    }

    internal sealed class VariantListConverter : JsonConverter<List<Variant>>
    {
        public override List<Variant> ReadJson(JsonReader reader, Type objectType, List<Variant> existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            return ModelJsonSerializer.DeserializeVariantList(token);
        }

        public override void WriteJson(JsonWriter writer, List<Variant> value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    internal sealed class ApplyListConverter : JsonConverter<List<Variant>>
    {
        public override List<Variant> ReadJson(JsonReader reader, Type objectType, List<Variant> existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);
            return ModelJsonSerializer.DeserializeVariantList(token);
        }

        public override void WriteJson(JsonWriter writer, List<Variant> value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    internal sealed class WhenConverter : JsonConverter<When>
    {
        public override When ReadJson(JsonReader reader, Type objectType, When existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject)
            {
                return null;
            }

            var obj = JObject.Load(reader);
            if (obj.TryGetValue("OR", out var orToken))
            {
                var list = new List<Dictionary<string, string>>();
                foreach (var child in orToken.Children<JObject>())
                {
                    list.Add(child.ToObject<Dictionary<string, string>>());
                }

                return new When(null, list);
            }

            return new When(obj.ToObject<Dictionary<string, string>>(), null);
        }

        public override void WriteJson(JsonWriter writer, When value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

    internal sealed class ModelFacesConverter : JsonConverter<Dictionary<string, Face>>
    {
        public override Dictionary<string, Face> ReadJson(JsonReader reader, Type objectType,
            Dictionary<string, Face> existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject)
            {
                return null;
            }

            var obj = JObject.Load(reader);
            var dict = new Dictionary<string, Face>(StringComparer.Ordinal);
            foreach (var prop in obj.Properties())
            {
                dict[prop.Name] = prop.Value.ToObject<Face>();
            }

            return dict;
        }

        public override void WriteJson(JsonWriter writer, Dictionary<string, Face> value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}