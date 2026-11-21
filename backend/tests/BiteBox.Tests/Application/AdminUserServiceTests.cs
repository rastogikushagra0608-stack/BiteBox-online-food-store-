using BiteBox.Application.Abstractions;
using BiteBox.Application.Common;
using BiteBox.Application.Features.Admin;
using BiteBox.Domain.Enums;
using Moq;

namespace BiteBox.Tests.Application;

/// <summary>Moq isolates the use case from Identity and the database so only the business rule is under test.</summary>
public class AdminUserServiceTests
{
    private readonly Mock<IIdentityService> _identity = new();
    private readonly Mock<ICurrentUser> _currentUser = new();
    private readonly AdminUserService _sut;

    public AdminUserServiceTests()
    {
        _currentUser.Setup(u => u.UserId).Returns(1);
        _sut = new AdminUserService(Mock.Of<IApplicationDbContext>(), _identity.Object, _currentUser.Object);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetActiveAsync(1, false, default));
        _identity.Verify(i => i.SetActiveAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Admin_can_deactivate_someone_else()
    {
        await _sut.SetActiveAsync(7, false, default);
        _identity.Verify(i => i.SetActiveAsync(7, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unknown_roles_are_rejected_before_reaching_identity()
    {
        await Assert.ThrowsAsync<BadRequestException>(() => _sut.SetRoleAsync(7, "SuperHacker", default));
        _identity.Verify(i => i.SetRoleAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(new[] { "Admin" }, OrderActor.Admin)]
    [InlineData(new[] { "Kitchen" }, OrderActor.Kitchen)]
    [InlineData(new[] { "Delivery" }, OrderActor.Delivery)]
    [InlineData(new[] { "Customer" }, OrderActor.Customer)]
    public void Roles_map_to_order_actors(string[] roles, OrderActor expected)
    {
        var user = new Mock<ICurrentUser>();
        user.Setup(u => u.IsInRole(It.IsAny<string>())).Returns<string>(roles.Contains);
        Assert.Equal(expected, Roles.ToOrderActor(user.Object));
    }
}
