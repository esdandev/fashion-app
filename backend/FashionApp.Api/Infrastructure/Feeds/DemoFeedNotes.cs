namespace FashionApp.Api.Infrastructure.Feeds;

/// <summary>
/// Deterministic fashion notes shared by the fake feed and the public demo.
/// Every <see cref="Note.Url"/> points to a real, stable fashion section, so a
/// click from the demo never lands on a missing page.
/// </summary>
public static class DemoFeedNotes
{
    public sealed record Note(
        string Title,
        string Url,
        string Source,
        string Summary,
        string[] StyleTags);

    public static readonly IReadOnlyList<Note> All =
    [
        new(
            "Tendencias de otoño: capas y texturas",
            "https://www.vogue.com/fashion",
            "Vogue",
            "Las claves de la temporada, de los abrigos largos a los tejidos cálidos.",
            ["capas", "texturado"]),
        new(
            "Cómo combinar denim en clave minimalista",
            "https://www.elle.com/fashion/",
            "Elle",
            "Tres fórmulas simples para usar jean todo el año sin recargar el look.",
            ["minimalista", "casual"]),
        new(
            "Los accesorios que ordenan un outfit",
            "https://www.harpersbazaar.com/fashion/",
            "Harper's Bazaar",
            "Cinturones, bolsos y joyería para dar intención a lo básico.",
            ["clasico", "entallado"]),
        new(
            "Calzado cómodo para la ciudad",
            "https://www.glamour.com/fashion",
            "Glamour",
            "Zapatillas y botas que acompañan jornadas largas sin perder estilo.",
            ["casual", "deportivo"]),
        new(
            "Paleta neutra: beige, gris y blanco",
            "https://www.vogue.com/fashion/trends",
            "Vogue",
            "Una guía para construir un guardarropa monocromático que siempre funciona.",
            ["monocromatico", "minimalista"]),
        new(
            "El regreso del traje oversized",
            "https://www.gq.com/style",
            "GQ",
            "Siluetas amplias y sastrería relajada para el trabajo y el fin de semana.",
            ["formal", "oversized"]),
        new(
            "Texturas de invierno sin perder liviandad",
            "https://www.elle.com/",
            "Elle",
            "Lana, seda y punto para sumar abrigo sin volumen innecesario.",
            ["texturado", "clasico"]),
        new(
            "Looks de fin de semana en tres pasos",
            "https://www.harpersbazaar.com/",
            "Harper's Bazaar",
            "Prendas versátiles para pasar del desayuno a una salida nocturna.",
            ["casual", "romantico"])
    ];
}
