namespace FashionApp.Api.Infrastructure.Feeds;

/// <summary>
/// Deterministic feed used in tests and local development: eight fashion notes
/// with stable titles and URLs, recent enough to pass the generator's age
/// filter. It never performs network I/O.
/// </summary>
public sealed class FakeFeedFetcher : IFeedFetcher
{
    private static readonly (string Title, string Source, string Summary)[] Notes =
    [
        ("Tendencias de otoño: capas y texturas", "Vogue", "Las claves de la temporada, de los abrigos largos a los tejidos cálidos."),
        ("Cómo combinar denim en clave minimalista", "Elle", "Tres fórmulas simples para usar jean todo el año sin recargar el look."),
        ("Los accesorios que ordenan un outfit", "Harper's Bazaar", "Cinturones, bolsos y joyería para dar intención a lo básico."),
        ("Calzado cómodo para la ciudad", "Glamour", "Zapatillas y botas que acompañan jornadas largas sin perder estilo."),
        ("Paleta neutra: beige, gris y blanco", "Vogue", "Una guía para construir un guardarropa monocromático que siempre funciona."),
        ("El regreso del traje oversized", "GQ", "Siluetas amplias y sastrería relajada para el trabajo y el fin de semana."),
        ("Texturas de invierno sin perder liviandad", "Elle", "Lana, seda y punto para sumar abrigo sin volumen innecesario."),
        ("Looks de fin de semana en tres pasos", "Harper's Bazaar", "Prendas versátiles para pasar del desayuno a una salida nocturna.")
    ];

    public Task<IReadOnlyList<FeedEntry>> FetchAsync(FeedSource source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ct.ThrowIfCancellationRequested();

        // Kept relative to the call so the demo always has a fresh digest. The
        // content and ordering are deterministic.
        var now = DateTimeOffset.UtcNow;
        var sourceName = string.IsNullOrWhiteSpace(source.Name) ? null : source.Name.Trim();

        IReadOnlyList<FeedEntry> entries = Notes
            .Select((note, index) => new FeedEntry(
                Title: note.Title,
                Url: $"https://example.com/nota-{index + 1}",
                Source: sourceName ?? note.Source,
                PublishedAt: now.AddHours(-(index + 1)),
                Summary: note.Summary,
                ImageUrl: null))
            .ToArray();

        return Task.FromResult(entries);
    }
}
