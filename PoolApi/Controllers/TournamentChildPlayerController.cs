using System.Data.SqlClient;
using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;

namespace TournamentBackend.Controllers
{
    public class CreateTournamentChildPlayerRequest
    {
        public int ParentPlayerId { get; set; }
        public string Name { get; set; }
    }

    [Authorize]
    [ApiController]
    [Route("api/tournament/{tournamentId:int}/child-players")]
    public class TournamentChildPlayerController : ControllerBase
    {
        private bool TryUser(out int userId)
        {
            userId = -1;
            Request.Headers.TryGetValue("Authorization", out StringValues values);
            string authorization = values.ToString();
            if (string.IsNullOrWhiteSpace(authorization) || authorization.Length <= 7) return false;
            try { userId = new UserBackoffice().SP_GetUserFromToken(authorization); return userId > 0; }
            catch { return false; }
        }

        [HttpGet]
        public IActionResult Get(int tournamentId)
        {
            if (!TryUser(out int userId)) return Unauthorized();
            try { return Ok(new { parents = new TournamentChildPlayerBackoffice().GetParents(tournamentId, userId) }); }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (Exception ex) { Console.WriteLine(ex); return StatusCode(500, new { message = "Could not load parent and child players." }); }
        }

        [HttpPost]
        public IActionResult Create(int tournamentId, [FromBody] CreateTournamentChildPlayerRequest request)
        {
            if (!TryUser(out int userId)) return Unauthorized();
            if (request == null) return BadRequest(new { message = "Choose a parent and enter a name." });
            try
            {
                var player = new TournamentChildPlayerBackoffice().CreateChild(tournamentId, userId, request.ParentPlayerId, request.Name);
                return Ok(new { player, message = "Child player created." });
            }
            catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
            catch (SqlException ex) { Console.WriteLine(ex); return BadRequest(new { message = ex.Number == 50001 ? "This parent already has a child with that name." : "Could not create the child player." }); }
            catch (Exception ex) { Console.WriteLine(ex); return StatusCode(500, new { message = "Could not create the child player." }); }
        }
    }
}
