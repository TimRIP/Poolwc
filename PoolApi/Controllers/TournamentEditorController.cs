using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;

namespace TournamentBackend.Controllers
{
    public class AddTournamentMatchEditorRequest
    {
        public string UserName { get; set; }
    }

    [Authorize]
    [ApiController]
    [Route("api/tournament/{tournamentId:int}/match-editors")]
    public class TournamentEditorController : ControllerBase
    {
        [HttpGet("access")]
        public IActionResult GetAccess(int tournamentId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentEditorBackoffice backoffice = new TournamentEditorBackoffice();
                TournamentEditorAccess access = backoffice.GetAccess(tournamentId, userId);
                if (access == null) return NotFound(new { message = "Tournament not found." });
                return Ok(new { access = access });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Tournament access could not be loaded." });
            }
        }

        [HttpGet]
        public IActionResult GetEditors(int tournamentId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentEditorBackoffice backoffice = new TournamentEditorBackoffice();
                return Ok(new { editors = backoffice.GetEditors(tournamentId, userId) });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Match editors could not be loaded." });
            }
        }

        [HttpPost]
        public IActionResult AddEditor(int tournamentId, [FromBody] AddTournamentMatchEditorRequest model)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            if (model == null || string.IsNullOrWhiteSpace(model.UserName))
            {
                return BadRequest(new { message = "Username is required." });
            }

            try
            {
                TournamentEditorBackoffice backoffice = new TournamentEditorBackoffice();
                TournamentEditorDetails editor = backoffice.AddEditor(tournamentId, userId, model.UserName);
                return Ok(new
                {
                    editor = editor,
                    message = editor.UserName + " can now edit match results for this tournament."
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The match editor could not be added." });
            }
        }

        [HttpDelete("{editorUserId:int}")]
        public IActionResult RemoveEditor(int tournamentId, int editorUserId)
        {
            if (!TryGetUserId(out int userId)) return Unauthorized();

            try
            {
                TournamentEditorBackoffice backoffice = new TournamentEditorBackoffice();
                backoffice.RemoveEditor(tournamentId, userId, editorUserId);
                return Ok(new { removed = true, userId = editorUserId });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(403, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The match editor could not be removed." });
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
