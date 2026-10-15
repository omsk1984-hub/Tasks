# План рефакторинга Tasks.Api по SOLID и DDD

> **СТАТУС: ВЫПОЛНЕНО.** Рефакторинг реализован по шагам 1–9. Сборка решения —
> без ошибок, все тесты (23) проходят, включая интеграционный сценарий с реальным
> RabbitMQ. Внешние контракты API не изменены.

> Целевой проект: `src/Tasks/Tasks.Api`
> Цель: устранить нарушения SOLID и «анемичную модель», привести код к слоистой
> архитектуре в духе DDD (Domain / Application / Infrastructure / Presentation),
> не меняя внешнее поведение API (контракты роутов, JSON-формат, коды ответов).

---

## 1. Текущее состояние (as-is)

### 1.1 Структура

```
Tasks.Api/
├── Program.cs                         # композиция, health-checks, миграции при старте
├── Contracts/                         # DTO запроса/ответа API
│   ├── CreateTaskRequest.cs
│   └── TaskResponse.cs
├── Data/
│   ├── TasksDbContext.cs              # EF Core + маппинг сущности (все правила в OnModelCreating)
│   └── Migrations/
├── Domain/
│   ├── Priority.cs                    # enum
│   └── TaskItem.cs                    # анемичная сущность (только get/set)
├── Endpoints/
│   └── TasksEndpoints.cs              # ВСЯ бизнес-логика + доступ к данным + валидация
└── Messaging/
    ├── ITaskEventPublisher.cs
    ├── RabbitMqOptions.cs
    ├── RabbitMqTaskEventPublisher.cs
    ├── RecordingTaskEventPublisher.cs
    └── TaskCompletedEvent.cs
```

### 1.2 Ключевые проблемы

| # | Файл / место | Проблема | Нарушаемый принцип |
|---|--------------|----------|--------------------|
| P1 | [`TasksEndpoints.cs`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:33) | Эндпоинты напрямую работают с `DbContext`, инкапсулируют бизнес-правила (создание, завершение, валидация) | SRP, DIP |
| P2 | [`TaskItem.cs`](src/Tasks/Tasks.Api/Domain/TaskItem.cs:6) | Анемичная модель: только свойства `get; set;`, ни одного инварианта | DDD: домен без поведения |
| P3 | [`TaskItem.cs`](src/Tasks/Tasks.Api/Domain/TaskItem.cs:15) | Публичные сеттеры `IsCompleted`, `CompletedAt` — можно выставить `CompletedAt` без `IsCompleted`, нарушить связку | Инкапсуляция инвариантов |
| P4 | [`ValidateTitle`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:151) | Валидация дублируется: DataAnnotations в [`CreateTaskRequest`](src/Tasks/Tasks.Api/Contracts/CreateTaskRequest.cs:12) + ручной метод (trim/whitespace) | DRY, SRP |
| P5 | [`CompleteTaskAsync`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:83) | Смешаны: поиск, бизнес-правило «уже завершена», конкурентность, публикация события | SRP, ISP |
| P6 | [`ITaskEventPublisher`](src/Tasks/Tasks.Api/Messaging/ITaskEventPublisher.cs:7) | Один интерфейс содержит публикацию + `PingAsync` (health-check) — разные потребители | ISP |
| P7 | [`Program.cs`](src/Tasks/Tasks.Api/Program.cs:91) | Миграции при старте в `Program.cs` (в проде — анти-паттерн), смешение инфраструктуры и старта | SRP |
| P8 | [`Program.cs`](src/Tasks/Tasks.Api/Program.cs:63) | Health-check RabbitMQ создаёт соединение вручную в лямбде, минуя `ITaskEventPublisher` | DIP, DRY |
| P9 | [`TasksDbContext.cs`](src/Tasks/Tasks.Api/Data/TasksDbContext.cs:13) | Маппинг всей модели в одном методе; при росте числа сущностей раздуется | SRP (Organization) |
| P10 | [`TasksEndpoints.cs`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:91) | `loggerFactory.CreateLogger("...")` вручную вместо DI | DIP (мелкое, но показательное) |
| P11 | Везде | Нет доменных событий; `TaskCompletedEvent` — это сообщение брокера, а не доменное событие (смешение слоёв) | DDD: разделение Domain и Integration событий |
| P12 | [`TasksEndpoints.cs`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:83) | Возвращаемые union-типы `Results<...>` зашивают представление в бизнес-поток | SRP |

---

## 2. Целевая архитектура (to-be)

Разделение на 4 логических слоя **внутри одного проекта** (без обязательного
разбиения на отдельные сборки — это опциональный шаг, см. §9).

```
Tasks.Api/
├── Domain/                            # ядро, без зависимостей на EF/ASP.NET
│   ├── TaskItem.cs                    # богатая модель: фабрика + методы-поведение
│   ├── Priority.cs
│   ├── TaskTitle.cs                   # Value Object (валидный заголовок)
│   ├── Events/
│   │   └── TaskCompletedDomainEvent.cs
│   └── Exceptions/
│       └── DomainException.cs
├── Application/                       # оркестрация use-case'ов
│   ├── Abstractions/
│   │   ├── ITaskRepository.cs
│   │   ├── IUnitOfWork.cs
│   │   └── IClock.cs                  # обёртка над TimeProvider (или напрямую TimeProvider)
│   ├── Tasks/
│   │   ├── CreateTask/
│   │   │   ├── CreateTaskCommand.cs
│   │   │   └── CreateTaskHandler.cs
│   │   ├── CompleteTask/
│   │   │   ├── CompleteTaskCommand.cs
│   │   │   └── CompleteTaskHandler.cs
│   │   ├── DeleteTask/
│   │   │   ├── DeleteTaskCommand.cs
│   │   │   └── DeleteTaskHandler.cs
│   │   └── GetTasks/
│   │       ├── GetTasksQuery.cs
│   │       └── GetTasksHandler.cs
│   └── Result.cs                      # явный результат use-case (без HttpResults в ядре)
├── Infrastructure/                    # адаптеры к внешним технологиям
│   ├── Persistence/
│   │   ├── TasksDbContext.cs
│   │   ├── Configurations/
│   │   │   └── TaskItemConfiguration.cs   # IEntityTypeConfiguration<TaskItem>
│   │   ├── TaskRepository.cs
│   │   └── Migrations/
│   └── Messaging/
│       ├── ITaskEventPublisher.cs
│       ├── RabbitMqOptions.cs
│       ├── RabbitMqTaskEventPublisher.cs
│       ├── RecordingTaskEventPublisher.cs
│       ├── TaskCompletedEvent.cs          # integration-сообщение брокера
│       └── TaskDomainEventToIntegrationMapper.cs
├── Contracts/                         # DTO HTTP (запросы/ответы)
│   ├── CreateTaskRequest.cs
│   └── TaskResponse.cs
├── Endpoints/                         # Presentation: тонкие минимальные API
│   └── TasksEndpoints.cs              # только маппинг HTTP ↔ use-case
└── Program.cs                         # композиционный корень
```

**Правило зависимостей:**
`Endpoints → Application → Domain`; `Infrastructure → Application/Domain`;
`Domain` не знает ни о чём снаружи.

---

## 3. Рефакторинг домена (DDD)

### 3.1 Богатая модель [`TaskItem`](src/Tasks/Tasks.Api/Domain/TaskItem.cs:6)

- Сделать сеттеры `private` (или `private set`) — менять состояние только через методы.
- Добавить фабричный метод `TaskItem.Create(TaskTitle title, Priority priority, DateTimeOffset now, Guid? id = null)`.
- Добавить поведение:
  - `void Complete(DateTimeOffset now)` — выставляет `IsCompleted=true` и `CompletedAt=now`;
    бросает `DomainException`/возвращает признак, если уже завершена.
  - (на будущее) `void ChangeTitle(TaskTitle title)`, `void ChangePriority(Priority p)`.
- Инвариант: `CompletedAt != null` тогда и только тогда, когда `IsCompleted == true`.
- Конструктор для EF (`private TaskItem() { }`) — см. §8 про materialization.

### 3.2 Value Object `TaskTitle`

- Обёртка над строкой с правилами: не пусто, после `Trim()` длина 1..200.
- Конструктор приватный, создание через `TaskTitle.Create(string?)` → бросает `DomainException` (или `TryCreate` → `bool`).
- Убирает дублирование валидации (P4) и «протаскивание» сырых строк.

### 3.3 Доменные события

- Ввести [`TaskCompletedDomainEvent`](src/Tasks/Tasks.Api/Domain/Events/TaskCompletedDomainEvent.cs:1) в `Domain/Events`.
- `TaskItem` накапливает доменные события (`IReadOnlyList<IDomainEvent> DomainEvents`),
  очистка — методом `ClearDomainEvents()`.
- **Разделить** доменное событие и integration-сообщение:
  `TaskCompletedDomainEvent` (внутри) → маппинг → [`TaskCompletedEvent`](src/Tasks/Tasks.Api/Messaging/TaskCompletedEvent.cs:10) (формат брокера). Закрывает P11.

### 3.4 Исключения домена

- [`DomainException`](src/Tasks/Tasks.Api/Domain/Exceptions/DomainException.cs:1) с кодами
  (`task_already_completed`, `invalid_title`), чтобы Application/Presentation могли
  маппить их в HTTP-ответы без «магии строк».

---

## 4. Рефакторинг Application-слоя (SRP, DIP)

### 4.1 Абстракции

- `ITaskRepository`:
  - `Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct);`
  - `Task<IReadOnlyList<TaskItem>> GetAllAsync(CancellationToken ct);`
  - `void Add(TaskItem item);`
  - `void Remove(TaskItem item);`
- `IUnitOfWork`: `Task<int> SaveChangesAsync(CancellationToken ct);`
  (реализация — через `TasksDbContext`; это позволит позже вынести выход/события).
- `IClock` (или оставить `TimeProvider` — предпочтительнее, он уже есть в BCL).

### 4.2 CQRS-lite: команды/запросы и обработчики

Каждый use-case — маленький обработчик с одной ответственностью (закрывает P1, P5):

| Use-case | Вход | Выход | Бизнес-шаги |
|----------|------|-------|-------------|
| CreateTask | `CreateTaskCommand(Title, Priority?)` | `Result<TaskDto>` | `TaskTitle.Create` → `TaskItem.Create` → repo.Add → UoW.Save |
| GetAllTasks | `GetTasksQuery` | `Result<IReadOnlyList<TaskDto>>` | repo.GetAll |
| CompleteTask | `CompleteTaskCommand(Id)` | `Result<TaskDto>` | repo.GetById → `item.Complete(now)` → UoW.Save (catch concurrency) → publish integration event |
| DeleteTask | `DeleteTaskCommand(Id)` | `Result` | repo.GetById → repo.Remove → UoW.Save |

### 4.3 `Result` / `Result<T>`

- Вместо HTTP-типов (`TypedResults.*`, `Results<...>`) в ядре — собственный `Result`
  с `IsSuccess`, `Error` (код + сообщение), опционально `Value`.
- Ошибки: `NotFound`, `Conflict`, `Validation`. Presentation преобразует их в HTTP. Закрывает P12.

### 4.4 Публикация интеграционного события

- Обработчик `CompleteTaskHandler` **после** успешного `SaveChangesAsync` маппит
  доменное событие в [`TaskCompletedEvent`](src/Tasks/Tasks.Api/Messaging/TaskCompletedEvent.cs:10)
  и вызывает [`ITaskEventPublisher`](src/Tasks/Tasks.Api/Messaging/ITaskEventPublisher.cs:7).
- Порядок «сначала БД, потом брокер» сохраняем; fail-silently сохраняем.
- Дополнительно (опционально): обработка доменных событий через `IDomainEventDispatcher`
  после `SaveChanges` — если хотим единый механизм.

---

## 5. Рефакторинг Infrastructure (P6, P8, P9)

### 5.1 Разделить интерфейс publisher (ISP)

- `ITaskEventPublisher` — только `PublishTaskCompletedAsync` (ISP, закрывает P6).
- `IMessagingHealthProbe` (или `IBrokerHealthProbe`) — только `PingAsync`.
- [`RabbitMqTaskEventPublisher`](src/Tasks/Tasks.Api/Messaging/RabbitMqTaskEventPublisher.cs:19)
  реализует оба; [`RecordingTaskEventPublisher`](src/Tasks/Tasks.Api/Messaging/RecordingTaskEventPublisher.cs:12) — тоже.

### 5.2 Health-check RabbitMQ через абстракцию (P8)

- Зарегистрировать `IBrokerHealthProbe` в DI и использовать его в `AddCheck<...>()`
  или в лямбде `AddRabbitMQ(...)` — без ручного `new ConnectionFactory` в [`Program.cs`](src/Tasks/Tasks.Api/Program.cs:63).
- Health-check Postgres оставить как есть (`AddNpgSql`), но connection string получать через опцию/`IOptions`.

### 5.3 Разбить маппинг EF (P9)

- Вынести конфигурацию в `IEntityTypeConfiguration<TaskItem>` →
  `Infrastructure/Persistence/Configurations/TaskItemConfiguration.cs`.
- В [`TasksDbContext.OnModelCreating`](src/Tasks/Tasks.Api/Data/TasksDbContext.cs:13) —
  `modelBuilder.ApplyConfigurationsFromAssembly(...)`.

### 5.4 Репозиторий

- `TaskRepository : ITaskRepository`, инкапсулирует `DbContext` (закрывает доступ
  к EF из слоя Endpoints, P1).

---

## 6. Рефакторинг Presentation (thin endpoints)

- [`TasksEndpoints.cs`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:15):
  - инжектит **только** обработчики команд/запросов (или `ISender`-подобный фасад).
  - тело эндпоинта: `MapToCommand` → `handler.HandleAsync` → `Result` → HTTP-ответ.
  - маппинг ошибок `Result` → `TypedResults.ValidationProblem/NotFound/Conflict` в одном хелпере.
  - инжектить `ILogger<T>` вместо `ILoggerFactory.CreateLogger("...")` (P10).
- [`Program.cs`](src/Tasks/Tasks.Api/Program.cs:1):
  - оставить только композицию (DI) и пайплайн;
  - миграции при старте вынести в отдельный extension/хостед-сервис
    под флагом конфигурации (P7), напр. `if (config["Db:MigrateOnStartup"]==true)`.

---

## 7. Порядок выполнения (инкрементально, по шагам)

Каждый шаг должен оставлять решение собираемым и проходимым для тестов.

1. **Шаг 0. Безопасность.** Убедиться, что интеграционные тесты зелёные (baseline).
   Запустить [`Tasks.Tests`](src/Tasks/Tasks.Tests/Tasks.Tests.csproj:1).
2. **Шаг 1. Domain VOs и инварианты.** Добавить `TaskTitle`, `DomainException`,
   доменное событие; сделать `TaskItem` богатым (фабрика + `Complete`), сеттеры private.
   _Пока не подключать_ к эндпоинтам — добавить юнит-тесты на инварианты.
3. **Шаг 2. Persistence.** Вынести конфигурацию EF в `IEntityTypeConfiguration`,
   включить `ApplyConfigurationsFromAssembly`. Прогнать миграции/тесты.
4. **Шаг 3. Application-абстракции.** Ввести `ITaskRepository`, `IUnitOfWork`,
   `Result`, DTO уровня Application (переиспользовать [`TaskResponse`](src/Tasks/Tasks.Api/Contracts/TaskResponse.cs:8) или вынести `TaskDto`).
5. **Шаг 4. Обработчики.** Реализовать 4 обработчика; покрыть юнит-тестами с fake-репозиторием.
6. **Шаг 5. Publisher/ISP.** Разделить `ITaskEventPublisher` и health-probe,
   ввести маппер доменного события в integration-сообщение.
7. **Шаг 6. Presentation.** Переписать [`TasksEndpoints`](src/Tasks/Tasks.Api/Endpoints/TasksEndpoints.cs:17)
   на тонкие обработчики; хелпер маппинга `Result` → HTTP.
8. **Шаг 7. Program/Infrastructure.** DI-регистрации, health-checks через абстракции,
   миграции под флагом.
9. **Шаг 8. Тесты.** Обновить [`CompleteTaskIntegrationTest`](src/Tasks/Tasks.Tests/CompleteTaskIntegrationTest.cs:1),
   добавить юнит-тесты домена и обработчиков; убрать `UnitTest1.cs`.
10. **Шаг 9 (опционально).** Разнести на отдельные проекты
    (`Tasks.Domain`, `Tasks.Application`, `Tasks.Infrastructure`, `Tasks.Api`).

---

## 8. Особые риски и как их закрыть

- **Материализация EF и private-сеттеры / private-конструктор.**
  EF Core умеет работать с `private set` и приватным конструктором, но нужно:
  - конструктор без параметров с `private` (EF найдёт через reflection), либо
  - `HasConstructorBinding`/binding по свойствам.
  Проверить после Шага 1 на реальной БД.
- **`xmin` как токен конкурентности** — сохранить текущий подход (см.
  [`TasksDbContext`](src/Tasks/Tasks.Api/Data/TasksDbContext.cs:52)); при выносе в
  `TaskItemConfiguration` не потерять `ValueGeneratedOnAddOrUpdate` + `IsConcurrencyToken`.
- **`HasConversion<string>()` для `Priority`** — перенести без изменений, чтобы
  не пересоздавать колонку/миграции.
- **Контракт API неизменен.** JSON, коды 200/201/204/400/404/409, маршруты —
  сохраняются. Рефакторинг не должен менять [`TaskResponse`](src/Tasks/Tasks.Api/Contracts/TaskResponse.cs:8) и формат [`TaskCompletedEvent`](src/Tasks/Tasks.Api/Messaging/TaskCompletedEvent.cs:10).
- **Кириллица в логах.** Не удалять UTF-8-блок в начале [`Program.cs`](src/Tasks/Tasks.Api/Program.cs:10)
  (см. правило проекта про кодировки .NET-процессов).

---

## 9. Критерии готовности (Definition of Done)

- [ ] `TaskItem` — богатая модель, инвариант «CompletedAt ⟺ IsCompleted» соблюдён.
- [ ] Валидация заголовка — в одном месте (`TaskTitle`), дублирования нет.
- [ ] Эндпоинты не ссылаются на `DbContext`/EF напрямую.
- [ ] `ITaskEventPublisher` и health-probe разделены (ISP).
- [ ] Маппинг EF разбит на `IEntityTypeConfiguration`.
- [ ] Health-check RabbitMQ использует абстракцию, а не ручное соединение.
- [ ] Доменное событие отделено от integration-сообщения.
- [ ] Внешнее поведение API (контракты, коды, формат) не изменилось.
- [ ] Юнит-тесты домена + обработчиков и обновлённые интеграционные тесты — зелёные.
- [ ] (опц.) Проекты разделены по слоям.

---

## 10. Что НЕ делаем (scope guard)

- Не меняем публичные контракты API и формат сообщений брокера.
- Не добавляем MediatR/FluentValidation и прочие внешние библиотеки на этом этапе —
  обработчики и валидация делаются «руками», чтобы минимизировать зависимости.
- Не меняем модель БД и не генерируем новые миграции (кроме случая, если вынос
  конфигурации потребует формально перегенерировать snapshot — изменения должны быть пустыми).
- Не переписываем логику RabbitMQ publisher (она корректна) — только разделяем интерфейс.
