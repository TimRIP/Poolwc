using DrukDatabaseLayer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System;
using System.ComponentModel.DataAnnotations;

namespace TournamentBackend.Controllers
{
    public class RegisterPlayerUserRequest
    {
        [Required]
        [StringLength(30, MinimumLength = 3)]
        public string Username { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 8)]
        public string Password { get; set; }

        [Required]
        [StringLength(50, MinimumLength = 1)]
        public string Name { get; set; }

        [EmailAddress]
        [StringLength(100)]
        public string Email { get; set; }
    }

    [ApiController]
    [Route("api/users")]
    public class UserRegistrationController : ControllerBase
    {
        [AllowAnonymous]
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterPlayerUserRequest model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                PlayerRegistrationBackoffice backoffice = new PlayerRegistrationBackoffice();
                int userId = backoffice.CreateUser(model.Username, model.Password, model.Name, model.Email);

                return Ok(new
                {
                    created = true,
                    userId = userId,
                    username = model.Username,
                    name = model.Name,
                    message = "User created. You can now sign in."
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The user could not be created." });
            }
        }
    }

    [Authorize]
    [ApiController]
    [Route("api/player")]
    public class PlayerController : ControllerBase
    {
        [HttpGet("me")]
        public IActionResult Me()
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            try
            {
                PlayerRegistrationBackoffice backoffice = new PlayerRegistrationBackoffice();
                PlayerUserProfile profile = backoffice.GetUserProfile(userId);
                if (profile == null)
                {
                    return NotFound();
                }

                return Ok(profile);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The player profile could not be loaded." });
            }
        }

        [HttpGet("tournaments")]
        public IActionResult Tournaments()
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            try
            {
                PlayerRegistrationBackoffice backoffice = new PlayerRegistrationBackoffice();
                return Ok(new { tournaments = backoffice.GetTournamentsForUser(userId) });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Tournaments could not be loaded. Make sure the player-registration database migration has been run." });
            }
        }

        [HttpGet("tournaments/find")]
        public IActionResult FindPrivateTournament([FromQuery] string code)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                return BadRequest(new { message = "Enter a private tournament join code." });
            }

            try
            {
                PlayerRegistrationBackoffice backoffice = new PlayerRegistrationBackoffice();
                PlayerTournamentInfo tournament = backoffice.FindTournamentByJoinCode(userId, code);
                if (tournament == null)
                {
                    return NotFound(new { message = "No private tournament was found with that join code." });
                }

                return Ok(new { tournament = tournament });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The private tournament could not be found." });
            }
        }

        [HttpPost("tournaments/{tournamentId:int}/register")]
        public IActionResult RegisterForTournament(int tournamentId, [FromQuery] string code = null)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            try
            {
                PlayerRegistrationBackoffice backoffice = new PlayerRegistrationBackoffice();
                TournamentRegistrationResult registration = backoffice.RegisterForTournament(tournamentId, userId, code);

                return Ok(new
                {
                    registered = true,
                    registration = registration,
                    message = registration.AlreadyRegistered
                        ? "You are already registered for this tournament."
                        : "You are now registered for the tournament."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Tournament registration could not be completed." });
            }
        }

        [HttpDelete("tournaments/{tournamentId:int}/register")]
        public IActionResult CancelTournamentRegistration(int tournamentId)
        {
            if (!TryGetUserId(out int userId))
            {
                return Unauthorized();
            }

            try
            {
                PlayerRegistrationBackoffice backoffice = new PlayerRegistrationBackoffice();
                backoffice.CancelTournamentRegistration(tournamentId, userId);
                return Ok(new
                {
                    cancelled = true,
                    tournamentId = tournamentId,
                    message = "Your tournament registration has been cancelled."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Tournament registration could not be cancelled." });
            }
        }

        private bool TryGetUserId(out int userId)
        {
            userId = -1;

            StringValues values;
            Request.Headers.TryGetValue("Authorization", out values);
            string authorization = values.ToString();

            if (string.IsNullOrWhiteSpace(authorization))
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
