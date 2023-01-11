using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Query.Internal;
using System.CodeDom;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using TripPlannerAPI.DTOs;
using TripPlannerAPI.DTOs.TripDTOs;
using TripPlannerAPI.Models;
using TripPlannerAPI.Repositories;
using TripPlannerAPI.Services;
using static TripPlannerAPI.Controllers.TripController;

namespace TripPlannerAPI.Controllers
{
    public enum GetTripsQueryParams //"created-future", "created-past", "joined-future", "joined-past" 
    {
        created_future,
        created_past,
        joined_future,
        joined_past
    }

    [Route("api/[controller]")]
    [ApiController]
    public class TripController : ControllerBase
    {
        private readonly UserManager<User> _userManager;
        private readonly ITripRepository _tripRepository;
        public TripController(UserManager<User> userManager, ITripRepository tripRepository)
        {
            _userManager = userManager;
            this._tripRepository = tripRepository;
        }

        [Authorize]
        [HttpPost("new")]
        [ProducesResponseType(typeof(MsgOnlyResp), 201)]
        //[ProducesResponseType(typeof(string), 400)]
        public async Task<ActionResult<MsgOnlyResp>> CreateTrip(TripInputDto _trip)
        {
            var user = await _userManager.FindByNameAsync(User.Identity.Name);


            String[] splitStartTime = _trip.startTime.Split(":");
            DateTime dt = new DateTime(_trip.date.Year, _trip.date.Month, _trip.date.Day, int.Parse(splitStartTime[0]), int.Parse(splitStartTime[1]), 0, _trip.date.Kind);

            var trip = new Trip { type = _trip.type, date = dt, creator = user, members = new List<User>(), waypoints = _trip.waypoints, Pins = _trip.pins, description=_trip.description, distance = _trip.distance, totalTime = _trip.totalTime, creationDateTime=DateTime.Now };
            trip.preferences = new List<Preference>();
            for (int i = 0; i < _trip.preferences.Count(); i++)
            {
                trip.preferences.Add(new Preference(_trip.preferences[i]));
            }

            
            var result = await _tripRepository.CreateTripAsync(trip);

            var respBody = new MsgOnlyResp();
            respBody.message = "Success";
            return StatusCode((int)HttpStatusCode.Created, respBody);
        }
        [Authorize]
        [HttpGet("all")]
        [ProducesResponseType(typeof(TripListContainer), 200)]
        public async Task<ActionResult<TripListContainer>> GetAllTripsNotCreatorOrMemberOf()
        {
            var user = await _userManager.FindByNameAsync(User.Identity.Name);
            var result = await _tripRepository.GetAllTripsCurrentOrFutureUserNotMemberOrCreatorAsync(user);

            var respBody = new TripListContainer();
            respBody.trips = ((List<Trip>)result).Select(t => new TripDto(t, user)).ToList();
            return StatusCode((int)HttpStatusCode.OK, respBody);
        }

        [Authorize]
        [HttpGet("all/{tripId}")]
        [ProducesResponseType(typeof(TripDto), 200)]
        [ProducesResponseType(typeof(MsgOnlyResp), (int)HttpStatusCode.NotFound)]
        public async Task<ActionResult<Trip>> GetTrip(int tripId)
        {
            var user = await _userManager.FindByNameAsync(User.Identity.Name);
            var result = await _tripRepository.GetTripAsync(tripId);
            if (result == null)
            {
                var respBody = new MsgOnlyResp(); respBody.message = "Trip with given Id not found.";
                return StatusCode((int)HttpStatusCode.NotFound, respBody);
            }
            return StatusCode((int)HttpStatusCode.OK, new TripDto(result,user));
        }
        [Authorize]
        [HttpPost("all/{tripId}/join")]
        [ProducesResponseType(typeof(MsgOnlyResp), 200)]
        [ProducesResponseType(typeof(MsgOnlyResp), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(MsgOnlyResp), (int)HttpStatusCode.InternalServerError)]
        public async Task<ActionResult<Trip>> JoinTrip(int tripId)
        {
            var user = await _userManager.FindByNameAsync(User.Identity.Name);
            var trip = await _tripRepository.GetTripAsync(tripId);
            if (trip == null)
            {
                var respBody = new MsgOnlyResp(); respBody.message = "Failure: Trip with given Id not found.";
                return StatusCode((int)HttpStatusCode.NotFound, respBody);
            }
            trip.members.Add(user);
            var result = await _tripRepository.UpdateTripAsync(trip);
            var resBody = new MsgOnlyResp();
            resBody.message = "Success.";
            int statusCode = (int)HttpStatusCode.OK;
            if(result==null)
            {
                resBody.message = "Failure while trying to update trip.";
                statusCode = (int)HttpStatusCode.InternalServerError;
            }
            return StatusCode(statusCode, resBody);
        }

        //internal List<String> possibleQueryParams = new List<string>() { "created_future", "created_past", "joined_future", "joined_past" };
        /// <summary>
        /// Gets trips based on criteria specified in queryParam parameter.
        /// </summary>
        /// <param name="queryParam">Specifies which filters to use when getting the trips.</param>
        /// <returns>HTTP response.</returns>
        [Authorize]
        [HttpGet("my-trips/{queryParam}")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(TripListContainer), (int)HttpStatusCode.OK)]
        //[ProducesResponseType(typeof(MsgOnlyResp),(int)HttpStatusCode.BadRequest)]
        public async Task<ActionResult<TripListContainer>> GetTripsByQueryParam([FromRoute]GetTripsQueryParams queryParam)
        {
            string _queryParam = queryParam.ToString();
            //if (!possibleQueryParams.Contains(_queryParam))
            //{
            //    MsgOnlyResp badreqRespBody = new MsgOnlyResp();
            //    badreqRespBody.message = "Failure. The recieved queryParam doesn't match allowed types.";
            //    return StatusCode((int)HttpStatusCode.BadRequest, badreqRespBody);
            //}
            var user = await _userManager.FindByNameAsync(User.Identity.Name);
            List<string> args = _queryParam.Split('_').ToList();
            var result = await _tripRepository.GetTripsQueryParamFilteredAsync(args[0], args[1],user);

            var respBody = new TripListContainer();
            respBody.trips = ((List<Trip>)result).Select(t => new TripDto(t, user)).ToList();
            return StatusCode((int)HttpStatusCode.OK, respBody);
        }



        [Authorize]
        [HttpPut("/my-favorites/add/{tripId}")]
        [ProducesResponseType(typeof(MsgOnlyResp), 200)]
        [ProducesResponseType(typeof(MsgOnlyResp), (int)HttpStatusCode.NotFound)]
        [ProducesResponseType(typeof(MsgOnlyResp), (int)HttpStatusCode.InternalServerError)]
        public async Task<ActionResult<MsgOnlyResp>> AddFavoriteTrip(int tripId)
        {
            var user = await _userManager.FindByNameAsync(User.Identity.Name);
            user.FavoriteTrips = (List<Trip>)await _tripRepository.GetFavoriteTrips(user);
            if(user.FavoriteTrips ==null)
            {
                user.FavoriteTrips = new List<Trip>();
            }
            var trip = await _tripRepository.GetTripAsync(tripId);
            if (trip == null)
            {
                var respBody = new MsgOnlyResp(); respBody.message = "Failure: Trip with given Id not found.";
                return StatusCode((int)HttpStatusCode.NotFound, respBody);
            }
            user.FavoriteTrips.Add(trip);
            var result = await _userManager.UpdateAsync(user);
            var resBody = new MsgOnlyResp();
            int statusCode;
            if (result == null)
            {
                resBody.message = "Failure while trying to update trip.";
                statusCode = (int)HttpStatusCode.InternalServerError;
            }
            else
            {
                resBody.message = "Success.";
                statusCode = (int)HttpStatusCode.OK;
            }
            return StatusCode(statusCode, resBody);
        }


        [Authorize]
        [HttpGet("/my-favorites/all")]
        [ProducesResponseType(typeof(TripListContainer), 200)]
        public async Task<ActionResult<MsgOnlyResp>> GetFavoriteTrips()
        {
            var user = await _userManager.FindByNameAsync(User.Identity.Name);
            TripListContainer respBody = new TripListContainer();
            var favTrips = (List<Trip>) await _tripRepository.GetFavoriteTrips(user);
            respBody.trips = ((List<Trip>)favTrips).Select(t => new TripDto(t, user, true)).ToList();
            return StatusCode((int) HttpStatusCode.OK, respBody);
        }

        [Authorize]
        [HttpPost("/pins")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<ActionResult<PinDto>> AddPins(PinDto pins)
        {
            var pinDto = await _tripRepository.AddPins(pins);

            if(pinDto == null)
                return BadRequest("Trip with such Id does not exist!");

            return Ok(pinDto);
        }
    }
}
