using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;

namespace TournamentBackend.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/match")]
    public class AdvancementReplacementUndoController : ControllerBase
    {
        [HttpDelete("{stageMatchId:int}/advancement/replace/{replacementPlayerId:int}")]
        public IActionResult UndoReplacement(int stageMatchId, int replacementPlayerId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();
            var backoffice = new MatchResultBackoffice();
            if (!backoffice.CanManageAdvancement(stageMatchId, userId))
                return StatusCode(403, new { message = "Full tournament editing access is required to undo a replacement." });
            try
            {
                var result = backoffice.UndoQualifiedPlayerReplacement(stageMatchId, replacementPlayerId);
                return Ok(new { saved = true, state = result });
            }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Could not undo the saved replacement." });
            }
        }

        private bool TryGetUserId(out int userId)
        {
            userId = -1;
            StringValues values;
            Request.Headers.TryGetValue("Authorization", out values);
            string authorization = values.ToString();
            if (string.IsNullOrWhiteSpace(authorization) || authorization.Length <= 7) return false;
            try
            {
                userId = new UserBackoffice().SP_GetUserFromToken(authorization);
                return userId != -1;
            }
            catch { userId = -1; return false; }
        }
    }
}
