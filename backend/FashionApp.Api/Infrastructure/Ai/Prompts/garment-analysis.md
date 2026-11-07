# Análisis de una prenda de ropa

Analiza la imagen adjunta y describe **una sola prenda**. Responde únicamente con un objeto JSON válido, sin markdown, sin comentarios y sin texto adicional.

## Esquema de respuesta

```json
{
  "name": "Camisa de lino beige",
  "category": "top",
  "subcategory": "camisa",
  "colors": [{ "name": "beige", "hex": "#D8C3A5", "share": 0.85 }],
  "pattern": "liso",
  "seasons": ["spring", "summer"],
  "formality": 3,
  "material": "lino",
  "styleTags": ["minimalista", "clasico"],
  "confidence": 0.86
}
```

## Reglas

- `name`: nombre corto y descriptivo en español (máximo 120 caracteres). Si no puedes reconocer la prenda, usa `"Prenda"`.
- `category`: exactamente uno de estos valores: `top`, `bottom`, `dress`, `outerwear`, `shoes`, `bag`, `accessory`.
- `subcategory`: tipo concreto de prenda en español (por ejemplo `camisa`, `jean recto`, `zapatillas`), máximo 60 caracteres. Puede ser `null`.
- `colors`: uno a cuatro colores dominantes, de mayor a menor proporción. `name` en español, máximo 40 caracteres; `hex` en formato `#RRGGBB`; `share` entre 0 y 1.
- `pattern`: estampado o textura en español, máximo 40 caracteres (por ejemplo `liso`, `rayas`, `floral`). Puede ser `null`.
- `seasons`: subconjunto de `spring`, `summer`, `autumn`, `winter`. Puede ser una lista vacía si la prenda es de todo el año.
- `formality`: entero de 1 (deportivo) a 5 (formal).
- `material`: material principal en español, máximo 60 caracteres (por ejemplo `lino`, `algodón`, `cuero`). Puede ser `null`.
- `styleTags`: entre 0 y 4 etiquetas de la lista cerrada de abajo. Usa los valores **exactamente** como aparecen, sin acentos y en minúsculas. No inventes etiquetas.
- `confidence`: número entre 0 y 1 con tu confianza global en el análisis.

## Vocabulario controlado de estilo (lista cerrada)

```
minimalista · clasico · casual · streetwear · romantico · bohemio
deportivo · formal · vintage · noventoso · y2k · utilitario
monocromatico · colorido · oversized · entallado · capas · texturado
```

## Casos especiales

- Si la imagen no contiene ninguna prenda, responde `{"error":"no_garment"}`.
- Si hay varias prendas, analiza la dominante y añade `"containsMultiple": true` al objeto.
- No describas personas, cuerpos ni marcas de forma identificable; describe solo la prenda.
- No incluyas campos adicionales fuera del esquema.
