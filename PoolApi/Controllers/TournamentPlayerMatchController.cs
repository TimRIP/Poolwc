using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;

namespace TournamentBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/tournament/{tournamentId:int}/player-match-search")]
    public class TournamentPlayerMatchController : ControllerBase
    {
        [HttpGet("players")]
        public IActionResult GetPlayers(int tournamentId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentPlayerMatchBackoffice backoffice = new TournamentPlayerMatchBackoffice();
                return Ok(new { players = backoffice.GetPlayers(tournamentId, userId) });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The tournament players could not be loaded." });
            }
        }

        [HttpGet("players/{playerId:int}/matches")]
        public IActionResult GetPlayerMatches(int tournamentId, int playerId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentPlayerMatchBackoffice backoffice = new TournamentPlayerMatchBackoffice();
                return Ok(new { matches = backoffice.GetPlayerMatches(tournamentId, userId, playerId) });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The player's matches could not be loaded." });
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
