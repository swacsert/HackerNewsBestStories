# Hacker News Best Stories API — contexto del proyecto

## Qué estamos construyendo

Una API REST en **ASP.NET Core sobre .NET 10** que devuelve las mejores `n` historias de
Hacker News ordenadas por puntuación descendente, donde `n` lo indica quien llama a la API.

Es una **prueba técnica** para un proceso de selección. Se entrega como **repositorio público**
con un `README.md`. El código lo tengo que poder **defender línea a línea en una entrevista**,
así que no quiero nada que no entienda ni nada más complejo de lo necesario.

## Fuentes de datos (Hacker News)

- Lista de IDs: `https://hacker-news.firebaseio.com/v0/beststories.json` → devuelve ~200 IDs
- Detalle de una historia: `https://hacker-news.firebaseio.com/v0/item/{id}.json`

## Contrato de salida — EXACTO, no negociable

```json
[
  {
    "title": "A uBlock Origin update was rejected from the Chrome Web Store",
    "uri": "https://github.com/uBlockOrigin/uBlock-issues/issues/745",
    "postedBy": "ismaildonmez",
    "time": "2019-10-12T13:43:01+00:00",
    "score": 1716,
    "commentCount": 572
  }
]
```

**Los nombres de Hacker News NO son los de salida. Hay que mapear:**

| Hacker News | Salida |
|---|---|
| `by` | `postedBy` |
| `url` | `uri` |
| `time` (epoch Unix) | `time` en ISO 8601 con offset |
| `descendants` | `commentCount` |
| `title` | `title` |
| `score` | `score` |

## El requisito central

> "Your API should be able to efficiently service large numbers of requests
> without risking overloading of the Hacker News API."

Sin cuidado, **una petición a mi API = 201 peticiones a Hacker News**. Diez usuarios = 2.010.
Eso es lo que hay que evitar, y es el núcleo de la prueba.

---

# Decisiones de diseño ya tomadas — respétalas

## 1. Modelo proactivo, no reactivo

Un `BackgroundService` mantiene la caché fresca. **Las peticiones de usuario solo leen memoria
y nunca llaman a Hacker News.** Así la carga contra HN es constante y **no depende del número
de usuarios**.

## 2. Instantánea inmutable, sustituida de golpe

El estado cacheado es un `record` inmutable con el array ya ordenado y la hora de generación.
El refrescador **sustituye la referencia entera**; los lectores nunca bloquean.
Campo `volatile` para garantizar visibilidad entre hilos.

## 3. Frontera anticorrupción

Dos modelos separados y un mapeador en medio:
- `HackerNewsItem` → formato crudo de HN (`by`, `url`, `descendants`, epoch)
- `StoryDto` → el contrato de salida
Si HN cambia un nombre de campo, **solo cambia el mapeador**.

## 4. `IHttpClientFactory` con cliente tipado

Nunca `new HttpClient()`. Cliente tipado `AddHttpClient<IHackerNewsClient, HackerNewsClient>()`,
con URL base y timeout en configuración. Además `MaxConnectionsPerServer` en el handler
como segunda barrera contra el agotamiento de puertos efímeros.

## 5. Concurrencia limitada al pedir detalles

`Parallel.ForEachAsync` con `MaxDegreeOfParallelism` leído de `appsettings.json`
(valor inicial 10). Nunca 200 peticiones simultáneas.

## 6. Estampida: semáforo con doble comprobación

Para la ventana de arranque en frío: `SemaphoreSlim(1,1)` con comprobación **antes** de entrar
(camino rápido) y **otra vez dentro** (para que los que esperaban no recarguen).
`Release()` siempre en `finally`.

**NO uses la variante de `Lazy<Task<T>>` / tarea compartida.** La conozco y la descarté
a propósito: una tarea fallida queda fallada para siempre. Va al README como alternativa.

## 7. Resiliencia ante fallos de Hacker News

Si un refresco falla: **se registra en el log y NO se toca la instantánea anterior.**
Se sigue sirviendo el último dato bueno. El `try/catch` va **dentro del bucle**, por ciclo,
para que un fallo de red no tumbe el servicio entero.

## 8. `PeriodicTimer`, no `System.Threading.Timer` ni `while` + `Task.Delay`

`PeriodicTimer` garantiza que no haya refrescos solapados (solo pido el siguiente tic cuando
he terminado) y no acumula tics perdidos. `WaitForNextTickAsync` devuelve `false` al cancelar,
así que el bucle sale limpio en el apagado.

## 9. Asíncrono de punta a punta

**Cero `.Result` y cero `.Wait()` en todo el proyecto.** `CancellationToken` propagado
por toda la cadena, incluido el `stoppingToken` del `BackgroundService`.

## 10. Validación de `n`

| Caso | Respuesta |
|---|---|
| `n` ausente | valor por defecto 10 |
| `n <= 0` | **400** con `ProblemDetails` |
| `n` mayor que las disponibles | **200** con las que haya (no es un error) |
| `n` por encima del máximo configurado | se recorta al máximo |

## 11. Arranque en frío

Si todavía no hay instantánea, **503 con cabecera `Retry-After`**. Honesto y simple.
El `BackgroundService` hace un primer refresco inmediato al arrancar.

## 12. Ciclos de vida

El proveedor de la instantánea es **`Singleton`** (si fuera `Scoped` no cachearía nada).
Un `BackgroundService` también es singleton: **si alguna vez necesitara algo `Scoped`,
hay que crear un ámbito con `IServiceScopeFactory`** — nunca inyectarlo por constructor.

## 13. Lo que NO vamos a hacer

Nada de base de datos, Redis, MediatR, AutoMapper, ni cinco capas con DDD.
**Para este alcance sería sobreingeniería.** En el README se explica dónde entraría
una caché distribuida si hubiera que escalar a varias instancias.

---

# Estructura de ficheros

```
HackerNews.Api/
├─ Program.cs
├─ appsettings.json                 (URL base, intervalo de refresco, concurrencia, n máximo)
├─ Configuration/HackerNewsOptions.cs
├─ Endpoints/StoriesEndpoints.cs
├─ Contracts/StoryDto.cs
├─ HackerNews/
│   ├─ IHackerNewsClient.cs
│   ├─ HackerNewsClient.cs
│   └─ HackerNewsItem.cs
├─ Stories/
│   ├─ IBestStoriesProvider.cs
│   ├─ BestStoriesProvider.cs
│   ├─ BestStoriesRefresher.cs
│   ├─ StoriesSnapshot.cs
│   └─ StoryMapper.cs
└─ Dockerfile

HackerNews.Api.Tests/
├─ StoryMapperTests.cs              (mapeo de campos, epoch → ISO 8601, url nula)
├─ BestStoriesProviderTests.cs      (orden descendente, top-n, n mayor que lo disponible)
└─ FakeHackerNewsClient.cs
```

---

# Cómo quiero el código

## Idioma
**Código, nombres y comentarios en inglés.** Es un repositorio público.
Los mensajes de commit también en inglés.

## Comentarios — esto es importante

Quiero comentarios **de desarrollador, no de documentación generada**:

- ✅ **Explican el PORQUÉ**, no el qué. Si el código ya dice qué hace, no lo repitas.
- ✅ Van **solo donde una decisión no es obvia**: por qué el doble chequeo, por qué `volatile`,
  por qué no se toca la instantánea cuando falla el refresco, por qué `PeriodicTimer`.
- ✅ **Cortos y concretos**, como una nota a un compañero. Una o dos líneas.
- ❌ **NO** pongas `/// <summary>` en todo. Solo en las interfaces públicas, si acaso.
- ❌ **NO** comentes líneas evidentes (`// create the client`, `// return the result`).
- ❌ **NO** uses encabezados decorativos ni separadores de bloque.
- ❌ **NO** escribas comentarios que narren el plan ("Step 1: ...", "First we...").

Ejemplo del tono que quiero:
```csharp
// Second check inside the gate: if 50 requests arrived at once, only the first one
// actually loads. The rest find the snapshot already here and return.
```

Y no este:
```csharp
/// <summary>
/// Gets the snapshot asynchronously.
/// </summary>
```

## Commits
Incrementales y con sentido, no un único volcado. Un commit por pieza:
contrato, cliente, mapeador, proveedor, refrescador, endpoint, tests, docker.

## README
**No lo escribas todavía.** Lo redacto yo al final. Cuando lleguemos, pídeme el contenido.

---

# Cómo trabajamos

- **Una pieza cada vez.** Escribe un fichero o un par relacionados, y para.
- Después de cada pieza, **explícame en dos o tres frases las decisiones** que has tomado
  y qué me podrían preguntar de ahí en una entrevista.
- **Si algo es ambiguo, pregúntame antes de decidir.** No des nada por supuesto.
- Si crees que alguna decisión de arriba es mala, **dímelo y discutámoslo** — pero no la cambies
  por tu cuenta.
