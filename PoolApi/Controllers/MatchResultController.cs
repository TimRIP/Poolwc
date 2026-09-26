using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TournamentBackend.Controllers
{
    public class MatchResultRequest
    {
        public int MatchId { get; set; }
        public List<MatchResultUpdate> Results { get; set; }
    }

    [Authorize]
    [ApiController]
    [Route("api/match")]
    public class MatchResultController : ControllerBase
    {
        [HttpGet("{matchId:int}")]
        public IActionResult Get(int matchId)
        {
            if (!TryGetUserId(out _))
            {
                return Unauthorized();
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            MatchDetails details = backoffice.GetMatchDetails(matchId);

            if (details == null)
            {
                return NotFound();
            }

            return Ok(details);
        }

        [HttpPost("result")]
        public IActionResult SaveResult([FromBody] MatchResultRequest model)
        {
            if (!TryGetUserId(out _))
            {
                return Unauthorized();
            }

            if (model == null || model.MatchId <= 0 || model.Results == null || model.Results.Count == 0)
            {
                return BadRequest(new { message = "A match and at least one result are required." });
            }

            if (model.Results.Any(r => r.SeatId <= 0 || r.ResultMatchPlace <= 0))
            {
                return BadRequest(new { message = "Every result needs a valid seat and finishing place." });
            }

            if (model.Results.Select(r => r.SeatId).Distinct().Count() != model.Results.Count)
            {
                return BadRequest(new { message = "The same seat was submitted more than once." });
            }

            if (model.Results.Select(r => r.ResultMatchPlace).Distinct().Count() != model.Results.Count)
            {
                return BadRequest(new { message = "Each player must have a different finishing place." });
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            MatchDetails details = backoffice.GetMatchDetails(model.MatchId);

            if (details == null)
            {
                return NotFound(new { message = "The match was not found." });
            }

            if (!details.IndividualMatch)
            {
                return BadRequest(new { message = "Results can only be entered for playable matches." });
            }

            HashSet<int> validSeatIds = new HashSet<int>(details.Seats.Select(s => s.SeatId));
            if (model.Results.Any(r => !validSeatIds.Contains(r.SeatId)))
            {
                return BadRequest(new { message = "One or more seats do not belong to this match." });
            }

            int assignedPlayerCount = details.Seats.Count(s => s.PlayerId.HasValue);
            if (assignedPlayerCount < 2)
            {
                return BadRequest(new { message = "The match needs at least two assigned players." });
            }

            if (model.Results.Count != assignedPlayerCount)
            {
                return BadRequest(new { message = "Submit a finishing place for every assigned player." });
            }

            if (!model.Results.Any(r => r.ResultMatchPlace == 1))
            {
                return BadRequest(new { message = "A winner must be selected." });
            }

            try
            {
                StageAdvanceResult advancement = backoffice.SaveMatchResultsAndAdvance(model.MatchId, model.Results);
                return Ok(new
                {
                    saved = true,
                    matchId = model.MatchId,
                    message = advancement.Message,
                    advancement = advancement
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The match result could not be saved or advanced." });
            }
        }

        [HttpDelete("{matchId:int}/result")]
        public IActionResult ClearResult(int matchId)
        {
            if (!TryGetUserId(out _))
            {
                return Unauthorized();
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            MatchDetails details = backoffice.GetMatchDetails(matchId);

            if (details == null)
            {
                return NotFound();
            }

            try
            {
                StageAdvanceResult advancement = backoffice.ClearMatchResultsAndDownstream(matchId);
                return Ok(new
                {
                    cleared = true,
                    matchId = matchId,
                    message = advancement.Message,
                    advancement = advancement
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The match result could not be cleared." });
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
