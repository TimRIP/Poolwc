using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;

namespace TournamentBackend.Controllers
{
    public class RenameTournamentPlayerRequest
    {
        public string Name { get; set; }
    }

    [Authorize]
    [ApiController]
    [Route("api/tournament/{tournamentId:int}/players")]
    public class TournamentPlayerController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetPlayers(int tournamentId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentPlayerBackoffice backoffice = new TournamentPlayerBackoffice();
                return Ok(new { players = backoffice.GetUnregisteredPlayers(tournamentId, userId) });
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

        [HttpPut("{playerId:int}/name")]
        public IActionResult RenamePlayer(int tournamentId, int playerId, [FromBody] RenameTournamentPlayerRequest model)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentPlayerBackoffice backoffice = new TournamentPlayerBackoffice();
                TournamentUnregisteredPlayer player = backoffice.RenamePlayer(
                    tournamentId,
                    userId,
                    playerId,
                    model == null ? null : model.Name);

                return Ok(new
                {
                    player = player,
                    message = "Player name saved."
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
                return StatusCode(500, new { message = "The player name could not be saved." });
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
