// Контракт события task.completed перенесён в общий проект Tasks.Core,
// чтобы потребитель (Tasks.Consumer) и продюсер (Tasks.Api) использовали одну модель.
// Здесь оставлен тип-алиас, чтобы существующие using Tasks.Consumer.Messaging
// продолжали работать без массовых правок.
global using TaskCompletedEvent = Tasks.Core.Messaging.TaskCompletedEvent;
