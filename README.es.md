# Hacker News — API de las mejores historias

*[English version](README.md)*

Una API en ASP.NET Core que devuelve las mejores `n` historias de Hacker News, ordenadas por
puntuación descendente.

Lo interesante de este problema no es llamar a la API de Hacker News, sino llamarla *lo menos
posible*. `beststories.json` devuelve unos 200 identificadores de historia, y el detalle de cada una
es una petición aparte. Una implementación ingenua convierte **una llamada a esta API en unas 201
llamadas a Hacker News**; diez usuarios concurrentes la convierten en unas 2.000.

---

## Cómo ejecutarlo

**Requisitos previos:** SDK de .NET 10.

```
git clone ...
cd HackerNewsBestStories
dotnet run --project HackerNews.Api
```

La consola escribe la URL en la que está escuchando (también está en
`HackerNews.Api/Properties/launchSettings.json`).

El perfil por defecto escucha en http://localhost:5283.

**Tests:**
```
dotnet test
```

---

## Cómo funciona

La caché se refresca **de forma proactiva**, no bajo demanda.

Un `BackgroundService` va a buscar las mejores historias cada cierto intervalo fijo y sustituye una
instantánea en memoria. Las peticiones que entran solo leen esa instantánea: nunca llaman a Hacker
News.

La consecuencia es el sentido de todo el diseño: **la carga sobre Hacker News es constante y no
crece con el tráfico.** Tanto si la API atiende diez peticiones como diez mil, Hacker News ve el
mismo refresco programado. La latencia de una petición es una lectura de memoria.

Algunas decisiones que se derivan de eso:

- **La instantánea es inmutable y se sustituye entera.** El refrescador construye una instantánea
  nueva, ya ordenada, y reemplaza la referencia; los lectores nunca bloquean ni ven un estado a
  medio escribir. El campo es `volatile` para que todos los hilos vean la referencia nueva y no una
  cacheada.
- **La concurrencia contra Hacker News está limitada** (`Parallel.ForEachAsync` con un grado de
  paralelismo configurable). Los 200 detalles se piden en tandas controladas, nunca todos de golpe.
- **Un refresco fallido conserva la instantánea anterior.** Si Hacker News no responde, la API sigue
  sirviendo el último dato bueno y registra el fallo, en vez de quedarse en blanco. El `try/catch`
  es por ciclo, así que un refresco malo no mata el servicio.
- **`PeriodicTimer` gobierna el bucle**, de modo que un refresco no puede empezar mientras el
  anterior sigue corriendo, y el bucle sale limpiamente al apagar.
- **Un `HttpClient` tipado a través de `IHttpClientFactory`**, con un tope de conexiones por
  servidor: los manejadores se reutilizan y rotan en vez de crear sockets por llamada.
- **El arranque en frío está tratado explícitamente.** Antes de que termine el primer refresco, la
  API devuelve `503` con cabecera `Retry-After` en lugar de bloquearse o lanzar una carga desde la
  petición. La carga inicial está protegida por un semáforo con doble comprobación para que no
  pueda duplicarse — es defensivo, porque hoy el refrescador es su único llamante.

### Estructura del proyecto

`HackerNewsItem` (lo que devuelve Hacker News) y `StoryDto` (lo que devuelve esta API) son
deliberadamente dos tipos distintos con un mapeador en medio. Hacker News usa `by`, `url`,
`descendants` y una fecha en epoch de Unix; el contrato de aquí usa `postedBy`, `uri`,
`commentCount` e ISO 8601. Mantener el formato del proveedor fuera del resto del código hace que un
cambio por su parte toque un solo fichero.

### Configuración

Todo vive bajo `HackerNews` en `appsettings.json`:

| Ajuste | Significado |
|---|---|
| `BaseUrl`, `RequestTimeout` | endpoint del proveedor y tiempo máximo por petición |
| `RefreshInterval` | cada cuánto corre el refresco en segundo plano |
| `MaxDegreeOfParallelism` | cuántos detalles de historia se piden a la vez |
| `MaxConnectionsPerServer` | tope de conexiones salientes |
| `MaxStoriesPerRequest` | tope de `n` |

---

## Supuestos

- **Es aceptable que el dato no esté al segundo.** Las mejores historias cambian despacio, así que
  un dato de hasta un intervalo de refresco de antigüedad vale. El intervalo es configurable; el
  compromiso es frescura contra carga sobre Hacker News.
- **`n` se recorta, no se rechaza, cuando es demasiado grande.** Pedir más historias de las que hay
  no es un error: la API devuelve las que tiene. Pedir cero o un número negativo sí es un error del
  cliente y devuelve `400` con un cuerpo `ProblemDetails`. Sin `n`, devuelve 10.
- **Las historias sin enlace externo devuelven `uri: null`.** Los posts "Ask HN" y "Tell HN" no
  tienen campo `url` en el origen. La alternativa sería sustituirlo por el enlace permanente de
  Hacker News, pero preferí que la API informe de lo que el origen realmente proporciona en vez de
  inventarse un valor.
- **Una sola instancia.** La caché vive en la memoria del proceso, que es suficiente para un nodo.
- **Sin autenticación ni limitación de peticiones**, ya que ninguna de las dos formaba parte del
  enunciado.

---

## Con más tiempo

- **Resiliencia por historia en el refresco.** Ahora mismo, el fallo del detalle de una sola historia
  aborta el ciclo entero y se conserva la instantánea anterior. Saltarse la que ha fallado y
  refrescar el resto sería más tolerante, a costa de una instantánea que queda brevemente incompleta.
- **Una caché compartida fuera del proceso** (Redis o similar) si esto corriera en más de una
  instancia. Hoy cada instancia tendría su propia copia en memoria y refrescaría por su cuenta, así
  que tres nodos significan tres refrescos contra Hacker News en vez de uno.
- **Pruebas de carga.** He razonado sobre los límites de concurrencia pero no los he medido. Una
  prueba de carga me diría cuál es el grado de paralelismo correcto de verdad, en vez del que supuse.
- **Una respuesta mejor para el arranque en frío.** El `Retry-After` es ahora un valor fijo;
  derivarlo del intervalo de refresco y de la hora del último intento sería más honesto.
- **Un refresco incremental.** Cada ciclo vuelve a pedir ahora mismo las ~200 historias, hayan
  cambiado o no. Comparar la lista nueva de identificadores con la que ya se tiene y pedir solo las
  historias nuevas recortaría un ciclo de unas 201 peticiones a un puñado. La pega es que las
  puntuaciones se mueven constantemente: una historia conservada de un ciclo anterior arrastraría una
  puntuación congelada, y el orden —que es lo único que esta API tiene que acertar— se iría
  desviando. La respuesta real sería un híbrido: pedir siempre las historias nuevas, y volver a pedir
  las ya conocidas con menos frecuencia o solo las de la parte alta del ranking.
- **Persistir las historias en una base de datos**, tratándolas como un catálogo en vez de como una
  caché. El servicio sobreviviría a un reinicio sin tener que reconstruirlo todo desde Hacker News,
  podría seguir sirviendo si ellos no estuvieran disponibles, y abriría la puerta al histórico: cómo
  se movió la puntuación de una historia en el tiempo, qué estaba arriba la semana pasada. Es más
  infraestructura de la que este enunciado necesita, pero es hacia donde lo llevaría si los datos
  importaran más allá de la instantánea actual.

---

## Nota sobre el bloqueo del arranque en frío

Una alternativa al semáforo es compartir la propia tarea en curso, de forma que todos los que llaman
esperen a la misma carga. Es una solución elegante y la valoré, pero una tarea que falla queda
fallada para siempre y hay que descartarla explícitamente antes del siguiente intento. El semáforo
con doble comprobación es un poco más de código y no tiene ese caso límite, así que me quedé con él.
