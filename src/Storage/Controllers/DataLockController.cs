using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Altinn.Platform.Storage.Controllers;

/// <summary>
/// API for handling locking and unlocking of data elements
/// </summary>
[Route("storage/api/v1/instances/{instanceOwnerPartyId:int}/{instanceGuid:guid}/data/{dataGuid:guid}/lock")]
[ApiController]
public class DataLockController: ControllerBase
{
    private readonly IInstanceRepository _instanceRepository;
    private readonly IDataRepository _dataRepository;
    private readonly IAuthorization _authorizationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataLockController"/> class
    /// </summary>
    /// <param name="instanceRepository">the instance repository</param>
    /// <param name="dataRepository">the data repository handler</param>
    /// <param name="authorizationService">the authorization service.</param>
    public DataLockController(
        IInstanceRepository instanceRepository, 
        IDataRepository dataRepository, 
        IAuthorization authorizationService)
    {
        _instanceRepository = instanceRepository;
        _dataRepository = dataRepository;
        _authorizationService = authorizationService;
    }

    /// <summary>
    /// Locks a data element
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to delete.</param>
    /// <returns></returns>
    [Authorize(Policy = AuthzConstants.POLICY_INSTANCE_WRITE)]
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> Lock(int instanceOwnerPartyId, Guid instanceGuid, Guid dataGuid)
    {
        (Instance instance, ActionResult instanceError) = await GetInstanceAsync(instanceGuid, instanceOwnerPartyId);
        if (instance == null)
        {
            return instanceError;
        }

        (DataElement dataElement, ActionResult dataElementError) = await GetDataElementAsync(instanceGuid, dataGuid);
        if (dataElement == null)
        {
            return dataElementError;
        }
        
        if (dataElement.Locked)
        {
            return Ok(dataElement);
        }
        
        Dictionary<string, object> propertyList = new()
        {
            { "/locked", true }
        };
        
        DataElement updatedDataElement = await _dataRepository.Update(instanceGuid, dataGuid, propertyList);
        return Ok(updatedDataElement);
    }
    
    /// <summary>
    /// Unlocks a data element
    /// </summary>
    /// <param name="instanceOwnerPartyId">The party id of the instance owner.</param>
    /// <param name="instanceGuid">The id of the instance that the data element is associated with.</param>
    /// <param name="dataGuid">The id of the data element to delete.</param>
    /// <returns></returns>
    [Authorize]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [Produces("application/json")]
    public async Task<ActionResult<DataElement>> Unlock(int instanceOwnerPartyId, Guid instanceGuid, Guid dataGuid)
    {
        (Instance instance, ActionResult instanceError) = await GetInstanceAsync(instanceGuid, instanceOwnerPartyId);
        if (instance == null)
        {
            return Forbid();
        }

        bool authorized = await _authorizationService.AuthorizeAnyOfInstanceActions(instance, new List<string>() { "write", "unlock" }, instance.Process.CurrentTask.ElementId);
        if (!authorized)
        {
            return Forbid();
        }

        (DataElement dataElement, ActionResult dataElementError) = await GetDataElementAsync(instanceGuid, dataGuid);
        if (dataElement == null)
        {
            return dataElementError;
        }

        if (!dataElement.Locked)
        {
            return Ok(dataElement);
        }
        
        Dictionary<string, object> propertyList = new()
        {
            { "/locked", false }
        };
        
        DataElement updatedDataElement = await _dataRepository.Update(instanceGuid, dataGuid, propertyList);
        return Ok(updatedDataElement);
    }
    
    private async Task<(Instance Instance, ActionResult ErrorMessage)> GetInstanceAsync(Guid instanceGuid, int instanceOwnerPartyId)
    {
        Instance instance = await _instanceRepository.GetOne(instanceOwnerPartyId, instanceGuid);

        if (instance == null)
        {
            return (null, NotFound($"Unable to find any instance with id: {instanceOwnerPartyId}/{instanceGuid}."));
        }

        return (instance, null);
    }

    private async Task<(DataElement DataElement, ActionResult ErrorMessage)> GetDataElementAsync(Guid instanceGuid, Guid dataGuid)
    {
        DataElement dataElement = await _dataRepository.Read(instanceGuid, dataGuid);

        if (dataElement == null)
        {
            return (null, NotFound($"Unable to find any data element with id: {dataGuid}."));
        }

        return (dataElement, null);
    }
}
