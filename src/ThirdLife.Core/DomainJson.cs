using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThirdLife.Core;

public static class DomainJson
{
    public static JsonSerializerOptions CreateStrictOptions() =>
        new()
        {
            AllowDuplicateProperties = false,
            PropertyNameCaseInsensitive = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
}
