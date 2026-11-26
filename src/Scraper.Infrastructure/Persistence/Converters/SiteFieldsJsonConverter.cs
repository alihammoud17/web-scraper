using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Scraper.Domain.Content;

namespace Scraper.Infrastructure.Persistence.Converters;

/// <summary>
/// JSON options for the polymorphic <see cref="SiteFields"/> jsonb column. A <c>"site"</c>
/// discriminator ("gtacars", "newswire", "gta5mods") picks the record type. It is configured here,
/// not with attributes, so the Domain stays free of serialization concerns.
/// </summary>
internal static class SiteFieldsJsonConverter
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ConfigurePolymorphism } },
    };

    private static void ConfigurePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(SiteFields))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "site",
                UnknownDerivedTypeHandling = System.Text.Json.Serialization.JsonUnknownDerivedTypeHandling.FailSerialization,
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(GtaCarsFields), GtaCarsFields.SiteKey),
                    new JsonDerivedType(typeof(NewswireFields), NewswireFields.SiteKey),
                    new JsonDerivedType(typeof(Gta5ModsFields), Gta5ModsFields.SiteKey),
                },
            };
        }
        else if (typeInfo.Type.IsAssignableTo(typeof(SiteFields)) && typeInfo.Kind == JsonTypeInfoKind.Object)
        {
            // The computed Site property would clash with the "site" discriminator; the discriminator carries it.
            var site = typeInfo.Properties.FirstOrDefault(p => p.Name == "site");
            if (site is not null)
            {
                typeInfo.Properties.Remove(site);
            }
        }
    }
}
