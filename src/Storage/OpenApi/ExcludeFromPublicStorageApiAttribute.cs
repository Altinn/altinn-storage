using System;

namespace Altinn.Platform.Storage.OpenApi;

/// <summary>
/// An attribute that can be applied to classes or methods to indicate that they should be excluded from the public storage API documentation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ExcludeFromPublicStorageApiAttribute : Attribute { }
