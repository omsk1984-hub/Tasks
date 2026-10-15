# db — инфраструктура Tasks

Каталог содержит всё, что связано с PostgreSQL и RabbitMQ: запуск контейнеров,
инициализацию БД, применение EF Core-миграций и подготовку БД для CDC
(logical replication).

## Состав

| Файл | Назначение |
|------|------------|
| `docker-compose.yml` | Поднимает `Tasks_postgres` (PostgreSQL 17, `wal_level=logical`) и `Tasks_rabbitmq` (RabbitMQ 4 + management) |
| `init/01-init.sql` | Первичная инициализация БД: расширения `pgcrypto`/`pg_trgm` + право `REPLICATION` у `tasks_user` |
| `migrate.ps1` | Миграции + подготовка CDC: (опц.) пересоздание Postgres с `wal_level=logical`, применение миграций, диагностика |
| `apply-migrations.ps1` | Обёртка над `dotnet ef database update` (только миграции, без CDC-подготовки) |

Все контейнеры, тома и сеть именуются с префиксом `Tasks_`:
`Tasks_postgres`, `Tasks_rabbitmq`, `Tasks_pgdata`, `Tasks_rabbitmq_data`, `Tasks_net`.

## Запуск

Docker развёрнут в WSL, поэтому команды вызываются так:

```powershell
# Поднять инфраструктуру
wsl docker compose -f db/docker-compose.yml up -d

# Проверить, что контейнеры живы
wsl docker ps --filter "name=Tasks_"

# Логи
wsl docker logs Tasks_postgres
wsl docker logs Tasks_rabbitmq
```

RabbitMQ management UI: <http://localhost:15673> (guest / guest).

> **Порты смещены** относительно стандартных, чтобы не конфликтовать с другими
> локальными сервисами: PostgreSQL — **5442** (внутри 5432), RabbitMQ — **5673**
> (внутри 5672), management UI — **15673** (внутри 15672).

## Остановка

```powershell
# Остановить, сохранив данные
wsl docker compose -f db/docker-compose.yml stop

# Остановить и удалить контейнеры (том с данными сохранится)
wsl docker compose -f db/docker-compose.yml down

# Полная очистка, включая данные
wsl docker compose -f db/docker-compose.yml down -v
```

## Миграции и подготовка CDC

Рекомендуемый способ — скрипт [`migrate.ps1`](migrate.ps1:1):

```powershell
# Полная подготовка: пересоздать Postgres с wal_level=logical, применить миграции, диагностика
pwsh -NoProfile -ExecutionPolicy Bypass -File "db/migrate.ps1" -RecreatePostgres -NoPause

# Только применить миграции (контейнер уже с wal_level=logical)
pwsh -NoProfile -ExecutionPolicy Bypass -File "db/migrate.ps1" -NoPause
```

Скрипт `migrate.ps1`:

1. (с `-RecreatePostgres`) пересоздаёт `Tasks_postgres` (`docker compose up -d --force-recreate`),
   чтобы применить `command` с `wal_level=logical` — это **обязательное** условие
   логической репликации (CDC читает WAL таблицы `outbox_messages`). Том данных сохраняется.
2. применяет EF Core-миграции (`AddOutboxMessages` создаёт таблицу `outbox_messages`).
3. печатает диагностику: `wal_level`, `rolreplication` у `tasks_user`, наличие
   `outbox_messages`, публикации и репликационные слоты.

Если нужно применить **только** миграции (без CDC-логики), используйте
[`apply-migrations.ps1`](apply-migrations.ps1:1). Миграции также применяются
автоматически при старте API (`db.Database.MigrateAsync()`).

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File "db/apply-migrations.ps1" -NoPause
```

## CDC и логическая репликация

CDC-воркер [`Tasks.CDC`](../src/Tasks/Tasks.CDC/Tasks.CDC.csproj) при старте:

- создаёт публикацию `tasks_outbox_pub` на таблицу `outbox_messages`;
- создаёт репликационный слот `tasks_outbox_slot` (плагин `pgoutput`);
- создаёт служебную таблицу `cdc_processed` (дедупликация по ключу `(lsn, outbox_id)`).

Требования к PostgreSQL:

| Требование | Где задаётся |
|---|---|
| `wal_level=logical` | `command` в `docker-compose.yml` (применяется через `migrate.ps1 -RecreatePostgres`) |
| `max_replication_slots >= 1`, `max_wal_senders >= 1` | значения по умолчанию PostgreSQL достаточны |
| Право `REPLICATION` у `tasks_user` | `init/01-init.sql` (`ALTER ROLE tasks_user WITH REPLICATION`) |

Проверить состояние вручную:

```powershell
wsl docker exec -e PGPASSWORD=tasks_password Tasks_postgres psql -U tasks_user -d tasks_db `
  -c "SHOW wal_level;" `
  -c "SELECT slot_name, plugin FROM pg_replication_slots;" `
  -c "SELECT pubname FROM pg_publication;"
```

## Подключение

По умолчанию:

| Параметр | Значение |
|----------|----------|
| Host | `localhost` |
| Port | `5442` (внутри контейнера `5432`) |
| Database | `tasks_db` |
| User | `tasks_user` |
| Password | `tasks_password` |

Значения переопределяются переменными окружения `DB__*` / `ConnectionStrings__Postgres`
(см. `.env.example`).
