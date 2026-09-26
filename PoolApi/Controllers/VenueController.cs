using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;

namespace TournamentBackend.Controllers
{
    public class CreateVenueRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    public class AssignVenueRequest
    {
        public int? FacilityId { get; set; }
    }

    [Authorize]
    [ApiController]
    [Route("api/tournament/{tournamentId:int}/venues")]
    public class VenueController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetVenues(int tournamentId)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            try
            {
                VenueBackoffice backoffice = new VenueBackoffice();
                return Ok(new { venues = backoffice.GetVenuesForTournament(tournamentId, userId) });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Venues could not be loaded." });
            }
        }

        [HttpPost]
        public IActionResult CreateVenue(int tournamentId, [FromBody] CreateVenueRequest model)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            if (model == null || string.IsNullOrWhiteSpace(model.Name))
            {
                return BadRequest(new { message = "Venue name is required." });
            }

            try
            {
                VenueBackoffice backoffice = new VenueBackoffice();
                VenueDetails venue = backoffice.CreateVenue(tournamentId, userId, model.Name, model.Description);
                return Ok(new { venue = venue });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The venue could not be created." });
            }
        }

        [HttpDelete("{facilityId:int}")]
        public IActionResult DeleteVenue(int tournamentId, int facilityId)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            try
            {
                VenueBackoffice backoffice = new VenueBackoffice();
                backoffice.DeleteVenue(tournamentId, userId, facilityId);
                return Ok(new { deleted = true, facilityId = facilityId });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The venue could not be deleted." });
            }
        }

        [HttpPut("~/api/tournament/{tournamentId:int}/pool/{poolMatchId:int}/venue")]
        public IActionResult AssignVenue(int tournamentId, int poolMatchId, [FromBody] AssignVenueRequest model)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            if (model == null)
            {
                return BadRequest(new { message = "A venue assignment is required." });
            }

            try
            {
                VenueBackoffice backoffice = new VenueBackoffice();
                PoolVenueAssignment assignment = backoffice.AssignVenueToPool(
                    tournamentId,
                    userId,
                    poolMatchId,
                    model.FacilityId);

                return Ok(new
                {
                    saved = true,
                    assignment = assignment,
                    message = assignment.FacilityId.HasValue
                        ? "Venue assigned to pool."
                        : "Venue assignment removed from pool."
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The venue assignment could not be saved." });
            }
        }

        private bool TryGetUserId(out int userId)
        {
            userId = -1;

            StringValues values;
            Request.Headers.TryGetValue("Authorization", out values);
            string authorization = values.ToString();

            if (string.IsNullOrWhiteSpace(authorization) || authorization.Length <= 7)
            {
                return false;
            }

            try
            {
                UserBackoffice ubo = new UserBackoffice();
                userId = ubo.SP_GetUserFromToken(authorization);
                return userId != -1;
            }
            catch
            {
                userId = -1;
                return false;
            }
        }
    }
}
