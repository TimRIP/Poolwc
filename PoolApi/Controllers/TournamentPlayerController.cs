using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;

namespace TournamentBackend.Controllers
{
    public class RenameTournamentPlayerRequest
    {
        public string Name { get; set; }
    }

    public class SaveTournamentPlayerHandicapRequest
    {
        public int? HandicapPlayTo { get; set; }
    }

    public class RandomizeTournamentNamesRequest
    {
        public List<TournamentNameShuffleAssignment> Assignments { get; set; }
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

        [HttpPut("{playerId:int}/handicap")]
        public IActionResult SaveHandicap(int tournamentId, int playerId, [FromBody] SaveTournamentPlayerHandicapRequest model)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();
            if (model == null) return BadRequest(new { message = "Handicap value is missing." });
            try
            {
                var player = new TournamentPlayerBackoffice().SavePlayerHandicap(
                    tournamentId, userId, playerId, model.HandicapPlayTo);
                return Ok(new { player, message = "Handicap saved." });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex) { Console.WriteLine(ex); return StatusCode(500, new { message = "Could not save handicap." }); }
        }

        [HttpPost("randomize-names")]
        public IActionResult RandomizeNames(int tournamentId, [FromBody] RandomizeTournamentNamesRequest model)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();
            try
            {
                new TournamentPlayerBackoffice().RandomizeNamesWithParents(
                    tournamentId, userId, model == null ? null : model.Assignments);
                return Ok(new { message = "Player names and child-parent links randomized." });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex) { Console.WriteLine(ex); return StatusCode(500, new { message = "Could not randomize player names." }); }
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
