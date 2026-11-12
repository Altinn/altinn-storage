#nullable disable

using System;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Models;
using Altinn.Platform.Storage.Repository;

namespace Moq;

internal static class AuthorizationMockExtensions
{
    /// <summary>
    /// Makes <see cref="IAuthorization.GetAuthorizedInstance"/> on the mock behave like the real service
    /// over <paramref name="instanceRepository"/>: the instance is loaded with
    /// <see cref="IInstanceRepository.GetOne"/> and the decision is taken by the mock's own
    /// <see cref="IAuthorization.AuthorizeInstanceRequest"/> setup, so existing setups and verifications
    /// of that method keep working for controllers that go through the combined call.
    /// </summary>
    public static Mock<IAuthorization> DelegateGetAuthorizedInstance(
        this Mock<IAuthorization> mock,
        IInstanceRepository instanceRepository
    )
    {
        mock.Setup(a =>
                a.GetAuthorizedInstance(
                    It.IsAny<int>(),
                    It.IsAny<Guid>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Returns(
                async (
                    int instanceOwnerPartyId,
                    Guid instanceGuid,
                    string action,
                    bool includeElements,
                    CancellationToken cancellationToken
                ) =>
                {
                    InstanceInternal instance = await instanceRepository.GetOne(
                        instanceGuid,
                        includeElements,
                        cancellationToken
                    );
                    bool authorized = await mock.Object.AuthorizeInstanceRequest(instance, action);
                    return authorized ? (instance, true) : (null, false);
                }
            );
        return mock;
    }

    /// <summary>
    /// Creates an <see cref="IAuthorization"/> that permits every instance request and loads instances
    /// from <paramref name="instanceRepository"/>.
    /// </summary>
    public static IAuthorization CreatePermissiveAuthorization(
        IInstanceRepository instanceRepository
    )
    {
        Mock<IAuthorization> mock = new();
        mock.Setup(a =>
                a.AuthorizeInstanceRequest(It.IsAny<InstanceInternal>(), It.IsAny<string>())
            )
            .ReturnsAsync(true);
        return mock.DelegateGetAuthorizedInstance(instanceRepository).Object;
    }
}
