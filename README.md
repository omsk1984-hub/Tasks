# Tasks — тестовое задание (C# / .NET Backend Developer, Middle)

Мини-сервис управления задачами: **ASP.NET Core Minimal API** + **PostgreSQL (EF Core, миграции)** +
**RabbitMQ (publish-only)**. Реализовано всё из ТЗ; ниже — что сделано, как запустить и на что обратить внимание.

## Стек

| Компонент | Технология |
|-----------|------------|
| Платформа | .NET 10 (ASP.NET Core Minimal API) |
| БД | PostgreSQL 17 + EF Core 10 (миграции) |
| Брокер | RabbitMQ 4 (RabbitMQ.Client 7, только publish) |
| Тесты | xUnit + Testcontainers + `WebApplicationFactory` |
| Инфраструктура | Docker Compose (запуск в WSL, префикс `Tasks_`) |

## Структура

```
.
├── db/                          # всё, что связано с Postgres/RabbitMQ
│   ├── docker-compose.yml       # Tasks_postgres, Tasks_rabbitmq, сеть Tasks_net
│   ├── init/01-init.sql         # инициализация БД (расширения)
│   ├── apply-migrations.ps1     # обёртка над `dotnet ef database update`
│   └── README.md
├── src/Tasks/
│   ├── Tasks.slnx               # решение (новый XML-формат решения .NET 10)
│   ├── Tasks.Api/
│   │   ├── Program.cs           # composition root: DI, health-checks, Swagger, миграции
│   │   ├── Domain/              # TaskItem, Priority
│   │   ├── Data/                # TasksDbContext + Data/Migrations
│   │   ├── Contracts/           # CreateTaskRequest, TaskResponse
│   │   ├── Messaging/           # ITaskEventPublisher + RabbitMQ-реализация
│   │   └── Endpoints/           # TasksEndpoints (POST/GET/PUT/DELETE)
│   └── Tasks.Tests/
│       └── CompleteTaskIntegrationTest.cs
├── .env.example                 # пример переменных окружения (DB__*, RABBITMQ__*)
├── .env                         # фактические значения для локального запуска (в .gitignore)
└── NuGet.Config                 # офлайн-источник пакетов (см. раздел «Про NuGet»)
```

## API

| Метод | Эндпоинт | Действие | Коды ответов |
|-------|----------|----------|--------------|
| POST | `/tasks` | Создать задачу (`IsCompleted=false`, `Priority=Medium` по умолчанию) | `201`, `400` |
| GET | `/tasks` | Все задачи (без пагинации) | `200` |
| PUT | `/tasks/{id}/complete` | Завершить задачу: ставит `CompletedAt`, публикует событие в RabbitMQ | `200`, `404`, `409` |
| DELETE | `/tasks/{id}` | Жёстко удалить задачу | `204`, `404` |
| GET | `/health/live` | Liveness-проба | `200` |
| GET | `/health/ready` | Readiness (Postgres + RabbitMQ) | `200`, `503` |

Swagger UI доступен в Development: `http://localhost:5080/swagger`.

## Быстрый старт

### 1. Поднять инфраструктуру (Docker в WSL)

```powershell
wsl docker compose -f db/docker-compose.yml up -d
wsl docker ps --filter "name=Tasks_"
```

Поднимаются контейнеры `Tasks_postgres` (порт **5442**) и `Tasks_rabbitmq` (порты **5673**,
management UI **15673**). Порты смещены, чтобы не конфликтовать с другими локальными сервисами.

### 2. Запустить API

Рекомендуемый способ — скрипт [`src/Tasks/run_Tasks.ps1`](src/Tasks/run_Tasks.ps1:1): он читает
корневой [`\.env`](.env:1), экспортирует переменные в процесс и запускает `Tasks.Api`.

```powershell
# Обычный интерактивный запуск (с пересборкой)
pwsh -NoProfile -ExecutionPolicy Bypass -File "src/Tasks/run_Tasks.ps1"

# Неинтерактивно, без пересборки, с проверкой доступности PostgreSQL
pwsh -NoProfile -ExecutionPolicy Bypass -File "src/Tasks/run_Tasks.ps1" -NoBuild -NoPause -CheckDb
```

Параметры скрипта: `-Url`, `-EnvFile`, `-NoBuild`, `-NoPause`, `-CheckDb`.

Альтернатива — запуск через `dotnet` (тогда переменные из `.env` нужно задать вручную
или они будут взяты из `appsettings.json`):

```powershell
dotnet run --project src/Tasks/Tasks.Api --urls http://localhost:5080
```

Миграции применяются автоматически при старте. Строка подключения и параметры RabbitMQ
берутся из `appsettings.json` и переопределяются переменными окружения (см. `.env.example`).

### 3. Проверить сценарий

```powershell
# Создать (201), ожидаем completedAt=null, priority=High
curl -X POST http://localhost:5080/tasks -H "Content-Type: application/json" `
     -d "{\"title\":\"Buy milk\",\"priority\":\"High\"}"

# Завершить (200) — completedAt заполнится, в RabbitMQ уйдёт task.completed
curl -X PUT http://localhost:5080/tasks/<id>/complete

# Повторно завершить (409 Conflict)
curl -X PUT http://localhost:5080/tasks/<id>/complete

# Удалить (204), повторно — 404
curl -X DELETE http://localhost:5080/tasks/<id>
curl -X DELETE http://localhost:5080/tasks/<id>
```

Сообщение в RabbitMQ (exchange `task.events`, routing key `task.completed`):

```json
{
  "taskId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Buy milk",
  "completedAt": "2026-05-07T12:00:00+00:00",
  "priority": "High"
}
```

Management UI: <http://localhost:15673> (guest / guest).

### 4. Тесты

```powershell
dotnet test src/Tasks/Tasks.slnx
```

Тест `CompleteTaskIntegrationTest` покрывает сценарий из ТЗ: создать → завершить → проверить,
что событие ушло в RabbitMQ. Он умеет работать в двух режимах (см. ниже «Про тесты»).

## Реализованные требования ТЗ и принятые решения

### Валидация
- `Title`: не пустой, не из пробелов, не длиннее 200 символов → иначе **400** (`ValidationProblem`).
- Enum/`Guid` валидируются моделью (`{id:guid}` в маршруте, enum — строкой в теле).

### `CompletedAt`
- Заполняется **только** в `/complete`. При создании — `null`. Это явно проверяется тестом.

### Порядок: БД → RabbitMQ
- Сначала `SaveChangesAsync` (задача сохраняется в БД), **потом** публикация события.
  Если БД упадёт — сообщение зря не уйдёт.

### Конкурентность (RowVersion)
- Используется системная колонка PostgreSQL **`xmin`** как concurrency token
  (`TasksDbContext.OnModelCreating`). Два параллельных `/complete`:
  первый завершает задачу, второй получает **409 Conflict**
  (быстрая проверка `IsCompleted` + перехват `DbUpdateConcurrencyException`).

### RabbitMQ (publish-only)
- Один долгоживущий канал/соединение (singleton), ленивый реконнект, идемпотентная
  декларация обменника `task.events` (topic, durable).
- Публикация **без ожидания publisher confirms** (не блокирует поток запроса).
- **Fail silently + лог**: если брокер недоступен, задача всё равно завершается (200),
  а ошибка публикации пишется в лог.

### Graceful shutdown
- Реализован в `RabbitMqTaskEventPublisher` (`IAsyncDisposable`): при остановке
  (Ctrl+C / SIGTERM) сначала прекращается приём новых публикаций, затем ожидается
  завершение уже начатых (drain с таймаутом `RabbitMq:ShutdownDrainTimeoutSeconds`, по умолчанию 4 с),
  после чего канал и соединение корректно закрываются.

## Про тесты (важно)

Интеграционный тест запускается в двух режимах и выбирает их автоматически:

1. **DOCKER-режим (как в ТЗ)** — если процессу теста доступен Docker-эндпоинт, Testcontainers
   поднимает одноразовые контейнеры PostgreSQL и RabbitMQ и проверяет **реальную доставку**
   сообщения в брокер (через consumer, читающий тестовую очередь).
2. **FALLBACK-режим** — если Docker-эндпоинт процессу недоступен (в этой среде Docker живёт
   только внутри WSL, а `dotnet` запускается из Windows и не видит `docker_engine`),
   тест использует внешнюю БД из переменной `TASKS_TEST_POSTGRES` и подменяет publisher
   на `RecordingTaskEventPublisher`, проверяя сам факт публикации события после сохранения.

Пример запуска fallback-режима (БД `tasks_test` в том же контейнере):

```powershell
$env:TASKS_TEST_POSTGRES="Host=localhost;Port=5442;Database=tasks_test;Username=tasks_user;Password=tasks_password"
dotnet test src/Tasks/Tasks.slnx
```

Чтобы получить полноценный DOCKER-режим, нужно пробросить docker-сокет из WSL в Windows
или запускать `dotnet test` внутри WSL (там установлен .NET 10 и доступен `unix:///var/run/docker.sock`).

## Про NuGet (обход TLS-инспекции)

В этой среде HTTPS-соединение к `api.nuget.org` из `dotnet` не устанавливается
(прокси с TLS-инспекцией → `NU1301 / Authentication failed`), при этом `curl` работает.
Поэтому:

- нужные `.nupkg` скачаны `curl`-ом в папку `local-packages/` (в `.gitignore`);
- `NuGet.Config` подключает её как локальный источник.

Для обычной среды этот конфиг можно не использовать — достаточно восстановить
пакеты из `nuget.org` (`dotnet restore`).

## Что осталось за рамками (осознанно)

- Пагинация/фильтрация/сортировка `/tasks` — по ТЗ не требуется.
- Consumer событий — по ТЗ не нужен (только publish).
- Аутентификация, FluentValidation, расширенное логирование — не требуются.
