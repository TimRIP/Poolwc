using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;

namespace TournamentBackend.Controllers
{
    public class ManualDrawRequest
    {
        public List<TournamentPlayerDrawAssignmentRequest> Assignments { get; set; } = new List<TournamentPlayerDrawAssignmentRequest>();
    }

    [Authorize]
    [ApiController]
    [Route("api/tournament/{tournamentId:int}/player-draw")]
    public class TournamentDrawController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetDraw(int tournamentId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentDrawBackoffice backoffice = new TournamentDrawBackoffice();
                TournamentPlayerDrawResult result = backoffice.GetDraw(tournamentId, userId);
                if (result == null) return NotFound(new { message = "Tournament not found." });
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The manual draw could not be loaded." });
            }
        }

        [HttpPost]
        public IActionResult SaveDraw(int tournamentId, [FromBody] ManualDrawRequest model)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentDrawBackoffice backoffice = new TournamentDrawBackoffice();
                TournamentPlayerDrawResult result = backoffice.SaveManualDraw(
                    tournamentId,
                    userId,
                    model == null ? null : model.Assignments);

                return Ok(new
                {
                    status = result.Status,
                    registrations = result.Registrations,
                    slots = result.Slots,
                    message = result.Status.DrawCompleted
                        ? "Manual draw saved. All registered players have been assigned to player places."
                        : "Manual draw saved. Some registered players are still waiting for a player place."
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
                return StatusCode(500, new { message = "The manual draw could not be saved." });
            }
        }

        private bool TryGetUserId(out int userId)
        {
            userId = -1;
            Request.Headers.TryGetValue("Authorization", out StringValues values);
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
