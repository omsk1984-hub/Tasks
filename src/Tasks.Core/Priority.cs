namespace Tasks.Core;

/// <summary>
/// Приоритет задачи. Значения из ТЗ: Low, Medium, High.
/// Общий тип для домена Tasks.Api и проекции Tasks.Consumer:
/// вынесен в Tasks.Core, чтобы контракт обмена не расходился между продюсером и потребителем.
/// </summary>
public enum Priority
{
    Low = 0,
    Medium = 1,
    High = 2
}
