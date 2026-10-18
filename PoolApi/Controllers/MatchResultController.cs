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

    public class ManualAdvancementRequest
    {
        public List<ManualAdvancementAssignment> Assignments { get; set; }
    }

    public class StageManualAdvancementRequest
    {
        public List<StageManualAdvancementAssignment> Assignments { get; set; }
        public List<StageManualWildcardAssignment> WildcardAssignments { get; set; }
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
            if (!TryGetUserId(out int userId))
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
            if (!backoffice.CanEditMatchResults(model.MatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to edit match results for this tournament." });
            }

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
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(matchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to edit match results for this tournament." });
            }

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

        [HttpGet("{stageMatchId:int}/advancement")]
        public IActionResult GetManualAdvancement(int stageMatchId)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(stageMatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to manage advancement for this tournament." });
            }

            try
            {
                ManualAdvancementState state = backoffice.GetManualAdvancement(stageMatchId);
                if (state == null)
                {
                    return NotFound(new { message = "The pool was not found." });
                }

                return Ok(state);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not load manual advancement." });
            }
        }

        [HttpPost("{stageMatchId:int}/advancement/manual")]
        public IActionResult SaveManualAdvancement(
            int stageMatchId,
            [FromBody] ManualAdvancementRequest model)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            if (model == null || model.Assignments == null)
            {
                return BadRequest(new { message = "Advancement assignments are required." });
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(stageMatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to manage advancement for this tournament." });
            }

            try
            {
                ManualAdvancementSaveResult result =
                    backoffice.SaveManualAdvancement(stageMatchId, model.Assignments);
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not save manual advancement." });
            }
        }

        [HttpGet("{stageMatchId:int}/advancement/stage")]
        public IActionResult GetStageManualAdvancement(int stageMatchId)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(stageMatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to manage advancement for this tournament." });
            }

            try
            {
                StageManualAdvancementState state = backoffice.GetStageManualAdvancement(stageMatchId);
                if (state == null)
                {
                    return NotFound(new { message = "The stage was not found." });
                }

                return Ok(state);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not load stage advancement." });
            }
        }

        [HttpGet("{stageMatchId:int}/advancement/stage/incoming")]
        public IActionResult GetIncomingStageManualAdvancement(int stageMatchId)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(stageMatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to manage advancement for this tournament." });
            }

            try
            {
                IncomingStageManualAdvancementState state =
                    backoffice.GetIncomingStageManualAdvancement(stageMatchId);
                if (state == null)
                {
                    return NotFound(new { message = "The stage was not found." });
                }

                return Ok(state);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not load incoming stage advancement." });
            }
        }

        [HttpPost("{stageMatchId:int}/advancement/stage/manual")]
        public IActionResult SaveStageManualAdvancement(
            int stageMatchId,
            [FromBody] StageManualAdvancementRequest model)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            if (model == null)
            {
                return BadRequest(new { message = "Stage advancement assignments are required." });
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(stageMatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to manage advancement for this tournament." });
            }

            try
            {
                StageManualAdvancementSaveResult result =
                    backoffice.SaveStageManualAdvancement(
                        stageMatchId,
                        model.Assignments ?? new List<StageManualAdvancementAssignment>(),
                        model.WildcardAssignments ?? new List<StageManualWildcardAssignment>());
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not save stage advancement." });
            }
        }

        [HttpPost("{stageMatchId:int}/advancement/stage/incoming/{sourceStageMatchId:int}/manual")]
        public IActionResult SaveIncomingStageManualAdvancement(
            int stageMatchId,
            int sourceStageMatchId,
            [FromBody] StageManualAdvancementRequest model)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            if (model == null)
            {
                return BadRequest(new { message = "Stage advancement assignments are required." });
            }

            MatchResultBackoffice backoffice = new MatchResultBackoffice();
            if (!backoffice.CanEditMatchResults(stageMatchId, userId) ||
                !backoffice.CanEditMatchResults(sourceStageMatchId, userId))
            {
                return StatusCode(403, new { message = "You do not have permission to manage advancement for this tournament." });
            }

            try
            {
                StageManualAdvancementSaveResult result =
                    backoffice.SaveIncomingStageManualAdvancement(
                        sourceStageMatchId,
                        stageMatchId,
                        model.Assignments ?? new List<StageManualAdvancementAssignment>(),
                        model.WildcardAssignments ?? new List<StageManualWildcardAssignment>());
                return Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not save incoming stage advancement." });
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
