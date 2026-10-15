// Приоритет задачи вынесен в общий проект Tasks.Core (единый контракт обмена).
// Здесь оставлен тип-алиас, чтобы существующие using Tasks.Api.Domain продолжали работать
// без массовых правок в домене, Application и тестах.
global using Priority = Tasks.Core.Priority;
