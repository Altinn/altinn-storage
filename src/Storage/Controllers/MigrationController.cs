using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Altinn.Platform.Storage.Authorization;
using Altinn.Platform.Storage.Helpers;
using Altinn.Platform.Storage.Interface.Enums;
using Altinn.Platform.Storage.Interface.Models;
using Altinn.Platform.Storage.Repository;

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Altinn.Platform.Storage.Controllers
{
    /// <summary>
    /// Implements endpoints for the Altinn II migration
    /// </summary>
    [Route("storage/api/v1/migration")]
    [ApiController]
    public class MigrationController : ControllerBase
    {
        private readonly IInstanceRepository _instanceRepository;
        private readonly IInstanceEventRepository _instanceEventRepository;
        private readonly ITextRepository _textRepository;
        private readonly IApplicationRepository _applicationRepository;
        private readonly IAuthorization _authorizationService;

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageBoxInstancesController"/> class
        /// </summary>
        /// <param name="instanceRepository">the instance repository handler</param>
        /// <param name="instanceEventRepository">the instance event repository handler</param>
        /// <param name="textRepository">the text repository handler</param>
        /// <param name="applicationRepository">the application repository handler</param>
        /// <param name="authorizationService">the authorization service</param>
        public MigrationController(
            IInstanceRepository instanceRepository,
            IInstanceEventRepository instanceEventRepository,
            ITextRepository textRepository,
            IApplicationRepository applicationRepository,
            IAuthorization authorizationService)
        {
            _instanceRepository = instanceRepository;
            _instanceEventRepository = instanceEventRepository;
            _textRepository = textRepository;
            _applicationRepository = applicationRepository;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// Gets all instances in a given state for a given instance owner.
        /// </summary>
        /// <param name="p1">the instance owner id</param>
        /// <param name="p2">the instance guid</param>
        /// <param name="language"> language id en, nb, nn-NO"</param>
        /// <returns>list of instances</returns>
        [HttpGet("{p1:int}/{p2:int}")]
        public async Task<ActionResult> GetMessageBoxInstance(
            int p1,
            int p2,
            [FromQuery] string language)
        {
            return Ok($"Hello world {p1}{p2}");
        }
    }
}
