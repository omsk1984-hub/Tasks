using Tasks.Api.Domain;
using Tasks.Api.Domain.Exceptions;

namespace Tasks.Tests.Domain;

/// <summary>Юнит-тесты Value Object TaskTitle (единый источник правила валидации).</summary>
public sealed class TaskTitleTests
{
    [Theory]
    [InlineData("Buy milk", "Buy milk")]
    [InlineData("  padded  ", "padded")]
    public void Create_Normalizes_Valid_Title(string input, string expected)
    {
        var title = TaskTitle.Create(input);
        Assert.Equal(expected, title.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Rejects_Empty(string? input)
    {
        var ex = Assert.Throws<DomainException>(() => TaskTitle.Create(input));
        Assert.Equal("invalid_title", ex.Code);
    }

    [Fact]
    public void Create_Rejects_Too_Long()
    {
        var tooLong = new string('a', TaskTitle.MaxLength + 1);
        var ex = Assert.Throws<DomainException>(() => TaskTitle.Create(tooLong));
        Assert.Equal("invalid_title", ex.Code);
    }

    [Fact]
    public void TryCreate_Returns_False_For_Invalid()
    {
        Assert.False(TaskTitle.TryCreate("  ", out var result));
        Assert.Null(result);
    }
}
