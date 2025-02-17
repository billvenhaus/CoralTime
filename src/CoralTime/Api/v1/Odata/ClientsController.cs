using CoralTime.BL.Interfaces;
using CoralTime.ViewModels.Clients;
using Microsoft.AspNet.OData;
using Microsoft.AspNet.OData.Routing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using static CoralTime.Common.Constants.Constants;
using static CoralTime.Common.Constants.Constants.Routes;
using static CoralTime.Common.Constants.Constants.Routes.OData;

namespace CoralTime.Api.v1.Odata
{
    [Route(BaseODataControllerRoute)]
    [Authorize]
    public class ClientsController : BaseODataController<ClientsController, IClientService>
    {
        public ClientsController(IClientService service, ILogger<ClientsController> logger)
            : base(logger, service) { }

        // GET: api/v1/odata/Clients
        [HttpGet]
        public IActionResult Get()
        {
            try
            {
                return Ok(_service.GetAllClients());
            }
            catch (Exception e)
            {
                return SendErrorODataResponse(e);
            }
        }

        // POST: api/v1/odata/Clients
        [HttpPost]
        [Authorize(Policy = PolicyAddClient)]
        public IActionResult Create([FromBody] ClientView clientData)
        {
            if (!ModelState.IsValid)
            {
                return SendInvalidModelResponse();
            }

            try
            {
                var result = _service.Create(clientData);

                var locationUri = $"{Request.Host}/{BaseODataRoute}/Clients({result.Id})";
                return Created(locationUri, result);
            }
            catch (Exception e)
            {
                return SendErrorODataResponse(e);
            }
        }

        // GET api/v1/odata/Clients(2)
        [ODataRoute(ClientsWithIdRoute)]
        [HttpGet(IdRoute)]
        public IActionResult GetById([FromODataUri] int id)
        {
            try
            {
                var result = _service.GetById(id);
                return new ObjectResult(result);
            }
            catch (Exception e)
            {
                return SendErrorODataResponse(e);
            }
        }

        // PUT: api/v1/odata/Clients(2)
        [ODataRoute(ClientsWithIdRoute)]
        [HttpPut(IdRoute)]
        [Authorize(Policy = PolicyEditClient)]
        public IActionResult Update([FromODataUri]int id, [FromBody] JsonElement clientData)
        {
            if (!ModelState.IsValid)
            {
                return SendInvalidModelResponse();
            }

            try
            {
                var result = _service.Update(id, clientData);
                return new ObjectResult(result);
            }
            catch (Exception e)
            {
                return SendErrorODataResponse(e);
            }
        }

        // PATCH: api/v1/odata/Clients(30)
        [ODataRoute(ClientsWithIdRoute)]
        [HttpPatch(IdRoute)]
        [Authorize(Policy = PolicyEditClient)]
        public IActionResult Patch([FromODataUri]int id, [FromBody] JsonElement clientData)
        {
            if (!ModelState.IsValid)
            {
                return SendInvalidModelResponse();
            }

            try
            {
                var result = _service.Update(id, clientData);
                return new ObjectResult(result);
            }
            catch (Exception e)
            {
                return SendErrorODataResponse(e);
            }
        }

        //DELETE :api/v1/odata/Clients(1)
        [HttpDelete(IdRoute)]
        [ODataRoute(ClientsWithIdRoute)]
        [Authorize(Policy = PolicyEditClient)]
        public IActionResult Delete([FromODataUri]int id)
        {
            return BadRequest($"Can't delete the client with Id - {id}");
        }
    }
}