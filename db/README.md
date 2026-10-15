# db — инфраструктура Tasks

Каталог содержит всё, что связано с PostgreSQL и RabbitMQ: запуск контейнеров,
инициализацию БД и применение EF Core-миграций.

## Состав

| Файл | Назначение |
|------|------------|
| `docker-compose.yml` | Поднимает `Tasks_postgres` (PostgreSQL 17) и `Tasks_rabbitmq` (RabbitMQ 4 + management) |
| `init/01-init.sql` | Скрипт первичной инициализации БД (расширения `pgcrypto`, `pg_trgm`) |
| `apply-migrations.ps1` | Обёртка над `dotnet ef database update` |

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

RabbitMQ management UI: <http://localhost:15672> (guest / guest).

## Остановка

```powershell
# Остановить, сохранив данные
wsl docker compose -f db/docker-compose.yml stop

# Остановить и удалить контейнеры (том с данными сохранится)
wsl docker compose -f db/docker-compose.yml down

# Полная очистка, включая данные
wsl docker compose -f db/docker-compose.yml down -v
```

## Миграции

Миграции применяются автоматически при старте API (`db.Database.MigrateAsync()`).
При необходимости применить их вручную:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File "db/apply-migrations.ps1" -NoPause
```

## Подключение

По умолчанию:

| Параметр | Значение |
|----------|----------|
| Host | `localhost` |
| Port | `5432` |
| Database | `tasks_db` |
| User | `tasks_user` |
| Password | `tasks_password` |

Значения переопределяются переменными окружения `DB__*` / `ConnectionStrings__Postgres`
(см. `.env.example`).
