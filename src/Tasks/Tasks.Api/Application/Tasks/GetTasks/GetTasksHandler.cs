using System;
using Tasks.Api.Application.Abstractions;

namespace Tasks.Api.Application.Tasks.GetTasks;

/// <summary>Use-case «получить все задачи».</summary>
public sealed class GetTasksHandler(ITaskRepository repository)
{
    public async Task<Result<PaginatedResult<TaskDto>>> HandleAsync(
        GetTasksQuery query,
        CancellationToken cancellationToken)
    {
        PaginatedResult<TaskDto> paginatedResult;

        if (query.Page.HasValue && query.PageSize.HasValue)
        {
            // Пагинированный запрос
            var page = query.Page.Value;
            var pageSize = query.PageSize.Value;

            if (page < 1)
                return Result<PaginatedResult<TaskDto>>.Failure(
                    Error.Validation("Page must be greater than 0"));

            if (pageSize < 1 || pageSize > 100)
                return Result<PaginatedResult<TaskDto>>.Failure(
                    Error.Validation("PageSize must be between 1 and 100"));

            var (totalCount, pagedItems) = await repository.GetAllPagedAsync(page, pageSize, cancellationToken);
            var pagedDtos = pagedItems.Select(i => i.ToDto()).ToList();
            var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0;

            paginatedResult = new PaginatedResult<TaskDto>(
                pagedDtos,
                totalCount,
                page,
                pageSize,
                totalPages);
        }
        else
        {
            // Без пагинации — все задачи как одна страница
            var allItems = await repository.GetAllAsync(cancellationToken);
            var allDtos = allItems.Select(i => i.ToDto()).ToList();
            var totalPages = allDtos.Count > 0 ? 1 : 0;

            paginatedResult = new PaginatedResult<TaskDto>(
                allDtos,
                allDtos.Count,
                1,
                allDtos.Count,
                totalPages);
        }

        return Result<PaginatedResult<TaskDto>>.Success(paginatedResult);
    }
}
