using System.Text.Json.Nodes;

namespace FeatureFlags.Services;

public static class ConfigValidation
{
    public static bool ValidateAgainstSchema(JsonNode value, JsonObject schema, out string? error)
    {
        return ValidateAgainstSchema(value, schema, "$", out error);
    }

    private static bool ValidateAgainstSchema(JsonNode? value, JsonObject schema, string path, out string? error)
    {
        error = null;

        if (schema.TryGetPropertyValue("const", out var constValue) && !JsonNode.DeepEquals(value, constValue))
        {
            error = $"{path} must match the schema const value.";
            return false;
        }

        if (schema.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray enumValues &&
            !enumValues.Any(enumValue => JsonNode.DeepEquals(value, enumValue)))
        {
            error = $"{path} must match one of the allowed enum values.";
            return false;
        }

        if (schema.TryGetPropertyValue("type", out var typeNode) && typeNode is not null && !MatchesType(value, typeNode))
        {
            error = $"{path} must be {DescribeType(typeNode)}.";
            return false;
        }

        if (value is JsonObject valueObject)
        {
            if (schema.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray requiredValues)
            {
                foreach (var requiredValue in requiredValues.OfType<JsonValue>())
                {
                    var propertyName = requiredValue.GetValue<string>();
                    if (!valueObject.ContainsKey(propertyName))
                    {
                        error = $"{path}.{propertyName} is required.";
                        return false;
                    }
                }
            }

            if (schema.TryGetPropertyValue("properties", out var propertiesNode) && propertiesNode is JsonObject properties)
            {
                foreach (var property in properties)
                {
                    if (valueObject.TryGetPropertyValue(property.Key, out var propertyValue) &&
                        property.Value is JsonObject propertySchema &&
                        !ValidateAgainstSchema(propertyValue, propertySchema, $"{path}.{property.Key}", out error))
                    {
                        return false;
                    }
                }

                if (schema.TryGetPropertyValue("additionalProperties", out var additionalPropertiesNode) &&
                    additionalPropertiesNode is JsonValue additionalPropertiesValue &&
                    additionalPropertiesValue.TryGetValue<bool>(out var additionalProperties) &&
                    !additionalProperties)
                {
                    var unknownProperty = valueObject.Select(p => p.Key).FirstOrDefault(p => !properties.ContainsKey(p));
                    if (unknownProperty is not null)
                    {
                        error = $"{path}.{unknownProperty} is not allowed by the schema.";
                        return false;
                    }
                }
            }
        }

        if (value is JsonArray valueArray &&
            schema.TryGetPropertyValue("items", out var itemsNode) &&
            itemsNode is JsonObject itemSchema)
        {
            for (var i = 0; i < valueArray.Count; i++)
            {
                if (!ValidateAgainstSchema(valueArray[i], itemSchema, $"{path}[{i}]", out error))
                    return false;
            }
        }

        if (value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            if (schema.TryGetPropertyValue("minLength", out var minLengthNode) &&
                minLengthNode is JsonValue minLengthValue &&
                stringValue.Length < minLengthValue.GetValue<int>())
            {
                error = $"{path} is shorter than the schema minLength.";
                return false;
            }

            if (schema.TryGetPropertyValue("maxLength", out var maxLengthNode) &&
                maxLengthNode is JsonValue maxLengthValue &&
                stringValue.Length > maxLengthValue.GetValue<int>())
            {
                error = $"{path} is longer than the schema maxLength.";
                return false;
            }
        }

        if (TryGetNumber(value, out var numberValue))
        {
            if (schema.TryGetPropertyValue("minimum", out var minimumNode) &&
                TryGetNumber(minimumNode, out var minimum) &&
                numberValue < minimum)
            {
                error = $"{path} is less than the schema minimum.";
                return false;
            }

            if (schema.TryGetPropertyValue("maximum", out var maximumNode) &&
                TryGetNumber(maximumNode, out var maximum) &&
                numberValue > maximum)
            {
                error = $"{path} is greater than the schema maximum.";
                return false;
            }
        }

        return true;
    }

    private static bool MatchesType(JsonNode? value, JsonNode typeNode)
    {
        if (typeNode is JsonArray types)
            return types.Any(type => type is JsonValue typeValue && MatchesType(value, typeValue.GetValue<string>()));

        return typeNode is JsonValue jsonValue && MatchesType(value, jsonValue.GetValue<string>());
    }

    private static bool MatchesType(JsonNode? value, string type)
    {
        return type switch
        {
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "string" => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out _),
            "boolean" => value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out _),
            "integer" => value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out _),
            "number" => TryGetNumber(value, out _),
            "null" => value is null,
            _ => true
        };
    }

    private static string DescribeType(JsonNode typeNode)
    {
        if (typeNode is JsonArray types)
            return string.Join(" or ", types.OfType<JsonValue>().Select(type => type.GetValue<string>()));

        return typeNode is JsonValue value ? value.GetValue<string>() : "the expected type";
    }

    private static bool TryGetNumber(JsonNode? node, out decimal value)
    {
        value = 0;

        return node is JsonValue jsonValue &&
            (jsonValue.TryGetValue<decimal>(out value) ||
             (jsonValue.TryGetValue<double>(out var doubleValue) && decimal.TryParse(doubleValue.ToString(), out value)));
    }

}
