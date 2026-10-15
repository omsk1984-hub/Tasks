using System.ComponentModel.DataAnnotations;
using Tasks.Api.Domain;

namespace Tasks.Api.Contracts;

/// <summary>
/// Тело запроса POST /tasks.
/// </summary>
public sealed class CreateTaskRequest
{
    /// <summary>Заголовок задачи: не пустой, максимум 200 символов.</summary>
    [Required(ErrorMessage = "Title обязателен.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "Title должен быть от 1 до 200 символов.")]
    public string? Title { get; init; }

    /// <summary>Приоритет. Необязательный, по умолчанию Medium.</summary>
    public Priority? Priority { get; init; }
}
