#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;

using Altinn.AccessManagement.Core.Models;
using Altinn.Platform.Storage.Helpers;

using Xunit;

namespace Altinn.Platform.Storage.UnitTest.Extensions;

public class ClaimsPrincipalExtensionsTests
{
    [Fact]
    public void GetSystemUser_ReturnsSystemUser()
    {
        // Arrange
        ClaimsPrincipal user = CreateSystemUser();

        // Act
        SystemUserClaim? result = user.GetSystemUser();

        // Assert
        Assert.Equal("996a686f-d24d-4d92-a92e-5b3cec4a8cf7", result?.Systemuser_id[0]);
    }

    [Fact]
    public void GetSystemUserId_ReturnsSystemUserId()
    {
        // Arrange
        ClaimsPrincipal user = CreateSystemUser();

        // Act
        Guid? result = user.GetSystemUserId();

        // Assert
        Assert.Equal("996a686f-d24d-4d92-a92e-5b3cec4a8cf7", result.ToString());
    }

    [Fact]
    public void GetUserOrOrgNo_ReturnsOrgNo()
    {
        // Arrange
        ClaimsPrincipal user = CreateSystemUser();

        // Act
        string? result = user.GetUserOrOrgNo();

        // Assert
        Assert.Equal("myOrg", result);
    }

    private static ClaimsPrincipal CreateSystemUser()
    {
        SystemUserClaim systemUserClaim = new SystemUserClaim
        {
            Systemuser_id = ["996a686f-d24d-4d92-a92e-5b3cec4a8cf7"],
            Systemuser_org = new OrgClaim() { ID = "34567:myOrg" },
            System_id = "the_matrix"
        };

        List<Claim> claims = [new Claim("authorization_details", JsonSerializer.Serialize(systemUserClaim), "string", "org")];
        ClaimsPrincipal user = new(new ClaimsIdentity(claims));

        return user;
    }
}
