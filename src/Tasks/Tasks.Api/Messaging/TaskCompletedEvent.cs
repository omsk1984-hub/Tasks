// Контракт события task.completed перенесён в общий проект Tasks.Core,
// чтобы продюсер (Tasks.Api) и потребитель (Tasks.Consumer) использовали одну модель.
// Здесь оставлен алиас-using для обратной совместимости using-ов в этом неймспейсе.
global using TaskCompletedEvent = Tasks.Core.Messaging.TaskCompletedEvent;
