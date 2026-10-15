# Tasks — тестовое задание (C# / .NET Backend Developer, Middle)

Мини-сервис управления задачами: **ASP.NET Core Minimal API** + **PostgreSQL (EF Core, миграции)** +
**RabbitMQ**. Доставка события завершения задачи в брокер реализована надёжно через
**transactional outbox + CDC** (логическая репликация PostgreSQL → отдельный воркер → RabbitMQ).
Реализовано всё из ТЗ; ниже — что сделано, как запустить и на что обратить внимание.

## Стек

| Компонент | Технология |
|-----------|------------|
| Платформа | .NET 10 (ASP.NET Core Minimal API) |
| БД | PostgreSQL 17 + EF Core 10 (миграции) |
| Брокер | RabbitMQ 4 (RabbitMQ.Client 7) |
| CDC | Логическая репликация PostgreSQL (pgoutput) через Npgsql |
| Тесты | xUnit + Testcontainers + `WebApplicationFactory` |
| Инфраструктура | Docker Compose (запуск в WSL, префикс `Tasks_`) |

## Архитектура доставки событий: Outbox + CDC

Вместо прямой публикации из API при завершении задачи событие пишется в таблицу
**`outbox_messages`** в одной транзакции с изменением задачи (transactional outbox),
а публикует его в RabbitMQ отдельный воркер **`Tasks.CDC`**, читающий WAL через
**логическую репликацию PostgreSQL** (pgoutput).

```
API (complete) ──одна транзакция──▶ tasks + outbox_messages
                                          │  WAL (logical replication, pgoutput)
                                          ▼
                                    Tasks.CDC ──publish──▶ RabbitMQ (task.events / task.completed)
                                                                  │
                                                                  ▼
                                                        Tasks.Consumer ──▶ tasks_from_rmq
```

Гарантии:
- **Атомарность** — задача и событие фиксируются вместе; событие не теряется при сбое.
- **At-least-once** — CDC подтверждает LSN только после успешной публикации в RabbitMQ.
- **Идемпотентность** — consumer защищён `ON CONFLICT (task_id) DO NOTHING`; CDC дедуплицирует
  по ключу `(lsn, id)` (таблица `cdc_processed`).

## Структура

```
.
├── db/                          # всё, что связано с Postgres/RabbitMQ
│   ├── docker-compose.yml       # Tasks_postgres (wal_level=logical), Tasks_rabbitmq, сеть Tasks_net
│   ├── init/01-init.sql         # инициализация БД (расширения + REPLICATION для tasks_user)
│   ├── apply-migrations.ps1     # обёртка над `dotnet ef database update`
│   └── README.md
├── src/
│   ├── Tasks/
│   │   ├── Tasks.slnx           # решение (новый XML-формат решения .NET 10)
│   │   ├── Tasks.Api/
│   │   │   ├── Program.cs       # composition root: DI, health-checks, Swagger, миграции
│   │   │   ├── Domain/          # TaskItem, Priority
│   │   │   ├── Data/            # TasksDbContext, OutboxMessage, Data/Migrations
│   │   │   ├── Application/     # CQRS-lite обработчики, ITaskRepository/IUnitOfWork/IOutboxWriter
│   │   │   ├── Contracts/       # CreateTaskRequest, TaskResponse
│   │   │   └── Endpoints/       # TasksEndpoints (POST/GET/PUT/DELETE)
│   │   ├── Tasks.CDC/           # CDC-воркер: logical replication → RabbitMQ
│   │   └── Tasks.Tests/
│   ├── Consumer/Tasks.Consumer/ # подписчик task.completed → tasks_from_rmq
│   ├── Tasks.Core/              # общие контракты (TaskCompletedEvent, JsonSerializationDefaults)
│   ├── run_Tasks.ps1            # запуск API
│   ├── run_CDC.ps1              # запуск CDC-воркера
│   └── run_Consumer.ps1         # запуск consumer
├── .env.example                 # пример переменных окружения (DB__*, RABBITMQ__*, CDC__*)
├── .env                         # фактические значения для локального запуска (в .gitignore)
└── NuGet.Config                 # офлайн-источник пакетов (см. раздел «Про NuGet»)
```

## API

| Метод | Эндпоинт | Действие | Коды ответов |
|-------|----------|----------|--------------|
| POST | `/tasks` | Создать задачу (`IsCompleted=false`, `Priority=Medium` по умолчанию) | `201`, `400` |
| GET | `/tasks` | Все задачи (без пагинации) | `200` |
| PUT | `/tasks/{id}/complete` | Завершить задачу: ставит `CompletedAt`, пишет событие в outbox (в той же транзакции) | `200`, `404`, `409` |
| DELETE | `/tasks/{id}` | Жёстко удалить задачу | `204`, `404` |
| GET | `/health/live` | Liveness-проба | `200` |
| GET | `/health/ready` | Readiness (Postgres) | `200`, `503` |

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

Рекомендуемый способ — скрипт [`src/run_Tasks.ps1`](src/run_Tasks.ps1:1): он читает
корневой [`\.env`](.env:1), экспортирует переменные в процесс и запускает `Tasks.Api`.

```powershell
# Обычный интерактивный запуск (с пересборкой)
pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_Tasks.ps1"

# Неинтерактивно, без пересборки, с проверкой доступности PostgreSQL
pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_Tasks.ps1" -NoBuild -NoPause -CheckDb
```

Параметры скрипта: `-Url`, `-EnvFile`, `-NoBuild`, `-NoPause`, `-CheckDb`.

Альтернатива — запуск через `dotnet` (тогда переменные из `.env` нужно задать вручную
или они будут взяты из `appsettings.json`):

```powershell
dotnet run --project src/Tasks/Tasks.Api --urls http://localhost:5080
```

Миграции применяются автоматически при старте. Строка подключения берётся из
`appsettings.json` и переопределяется переменными окружения (см. `.env.example`).

### 2.1. Запустить CDC-воркер (публикация в RabbitMQ)

События из outbox публикует отдельный воркер `Tasks.CDC`. Запускается скриптом
[`src/run_CDC.ps1`](src/run_CDC.ps1:1):

```powershell
# Неинтерактивно, без пересборки, с проверкой брокера
pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_CDC.ps1" -NoBuild -NoPause -CheckBroker
```

> Требуется `wal_level=logical` в PostgreSQL и право `REPLICATION` у пользователя
> `tasks_user` (см. [`db/docker-compose.yml`](db/docker-compose.yml:1) и
> [`db/init/01-init.sql`](db/init/01-init.sql:1)).

### 2.2. Запустить consumer (опционально, для сквозной проверки)

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File "src/run_Consumer.ps1" -NoBuild -NoPause -CheckBroker
```

Consumer идемпотентно пишет доставленные события в таблицу `tasks_from_rmq`.

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

### Порядок: БД → outbox → (CDC) → RabbitMQ
- Задача и событие в `outbox_messages` сохраняются **в одной транзакции** (`ExecuteInTransactionAsync`).
- Публикацию выполняет отдельный воркер `Tasks.CDC` (logical replication → RabbitMQ),
  подтверждая LSN только после успешной отправки (at-least-once).

### Конкурентность (RowVersion)
- Используется системная колонка PostgreSQL **`xmin`** как concurrency token
  (`TasksDbContext.OnModelCreating`). Два параллельных `/complete`:
  первый завершает задачу, второй получает **409 Conflict**
  (быстрая проверка `IsCompleted` + перехват `DbUpdateConcurrencyException`).

### RabbitMQ (доставка через outbox + CDC)
- API **не публикует напрямую** — только пишет в outbox (запрос `complete` не зависит от брокера).
- `Tasks.CDC` читает `outbox_messages` через логическую репликацию (pgoutput), декодирует INSERT
  и публикует в обменник `task.events` (topic, durable) с routing key `task.completed`.
- Публикация persistent, `MessageId = TaskId`; подтверждение LSN продвигается только после
  успешной публикации.
- Если брокер недоступен, воркер ретраит с задержкой (`Cdc:ReconnectDelaySeconds`), а задача
  остаётся завершённой в БД с неподтверждённым outbox-событием.

### Graceful shutdown
- `Tasks.CDC`: по `CancellationToken` прекращается чтение потока репликации, закрываются
  соединение и слот (drain текущего сообщения).
- `Tasks.Consumer`: корректно закрывает канал/соединение RabbitMQ.

## Про тесты (важно)

- **Юнит-тесты** (`Application/*`): `CompleteTaskHandler` пишет событие в outbox в рамках одной
  транзакции (фейковый `IOutboxWriter`/`FakeUnitOfWork`), обработка ошибок → 400/404/409.
- **Интеграционный тест** `CompleteTaskIntegrationTest`: API создаёт и завершает задачу,
  проверяется строка в `outbox_messages` (реальная БД). Если Docker-эндпоинт недоступен
  (в этой среде Docker живёт только внутри WSL, а `dotnet` запускается из Windows),
  используется внешняя БД из переменной `TASKS_TEST_POSTGRES`; при её отсутствии проверка outbox пропускается.
- **Сквозной E2E** `CompleteToConsumerE2ETest`: поднимает реальные хосты `Tasks.CDC` и `Tasks.Consumer`
  против локальной инфраструктуры (.env): API → outbox → CDC (logical replication) → RabbitMQ →
  consumer → `tasks_from_rmq`. Требует `wal_level=logical` и право `REPLICATION`.

Пример запуска интеграционных тестов (БД `tasks_test` в контейнере):

```powershell
$env:TASKS_TEST_POSTGRES="Host=localhost;Port=5442;Database=tasks_test;Username=tasks_user;Password=tasks_password"
dotnet test src/Tasks/Tasks.slnx
```

Чтобы получить полноценный DOCKER-режим с Testcontainers, нужно пробросить docker-сокет из WSL
в Windows или запускать `dotnet test` внутри WSL (там .NET 10 и доступен `unix:///var/run/docker.sock`).

## Про NuGet (обход TLS-инспекции)

В этой среде HTTPS-соединение к `api.nuget.org` из `dotnet` не устанавливается
(прокси с TLS-инспекцией → `NU1301 / Authentication failed`), при этом `curl` работает.
Поэтому:

- нужные `.nupkg` скачаны `curl`-ом в папку `local-packages/` (в `.gitignore`);
- `NuGet.Config` подключает её как локальный источник.

Для обычной среды этот конфиг можно не использовать — достаточно восстановить
пакеты из `nuget.org` (`dotnet restore`).

## Что осталось за рамками (осознанно)

- Ретенция `outbox_messages` (строки хранятся как журнал исходящих событий; автоудаление не делаем).
- DLQ для «ядовитых» сообщений consumer-а (nack без requeue + лог).

- Пагинация/фильтрация/сортировка `/tasks` — по ТЗ не требуется.
- Consumer событий — по ТЗ не нужен (только publish).
- Аутентификация, FluentValidation, расширенное логирование — не требуются.
