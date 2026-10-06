using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using DrukDatabaseLayer;
using Newtonsoft.Json.Linq;

namespace TournamentBackend.Controllers
{

    [ApiController]
    [Route("api/token")]
    public class TokenController : ControllerBase
    {
        public class UserModel
        {
            [Required(ErrorMessage = "Username is required")]
            public string Username { get; set; }

            [Required(ErrorMessage = "Password is required")]
            public string Password { get; set; }
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult Post([FromBody]UserModel loginViewModel)
        {

            if (ModelState.IsValid)
            {     
                UserBackoffice ubo = new UserBackoffice();
                
                //This method returns user id from username and password.
                int userId = ubo.GetUserIdFromCredentials(loginViewModel.Username, loginViewModel.Password);
                if (userId == -1)
                {
                    return Unauthorized();
                }

                var claims = new[]
                {
            new Claim(JwtRegisteredClaimNames.Sub, "TimRIP"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

                var token = new JwtSecurityToken
                (
                    issuer: "http://localhost",
                    audience: "http://localhost",
                    claims: claims,
                    expires: DateTime.UtcNow.AddDays(60),
                    notBefore: DateTime.UtcNow,
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("KeyForSignInSecret@1234")),
                            SecurityAlgorithms.HmacSha256)
                );

                string tokenstr = new JwtSecurityTokenHandler().WriteToken(token);
                
                ubo.SaveToken(userId, tokenstr);

                return Ok(new { token = tokenstr });
            }

            return BadRequest();
        }

    }


    [Authorize]
    [ApiController]
    [Route("api/tournament")]
    public class TournamentController : ControllerBase
    {
        [HttpPost]
        public IActionResult Post([FromBody] String model)
        {
            UserBackoffice ubo = new UserBackoffice();
            StringValues values;
            this.Request.Headers.TryGetValue("Authorization", out values);

            
            int UserId = ubo.SP_GetUserFromToken(values.ToString());

            if (UserId == -1)
            {
                return Unauthorized();
            }

            bool isPrivate = false;
            string tournamentName = "Tournament";
            try
            {
                JObject tournamentJson = JObject.Parse(model);
                isPrivate = tournamentJson.Value<bool?>("privateTournament") ?? false;
                string suppliedName = tournamentJson.Value<string>("tournamentname");
                if (!string.IsNullOrWhiteSpace(suppliedName))
                {
                    tournamentName = suppliedName.Trim();
                }
            }
            catch
            {
                return BadRequest(new { message = "The tournament definition is not valid JSON." });
            }

            Helper hel = new Helper();
            int tournamentMatchId = hel.CreateTournamentFromString(model);

            int tournamentId = ubo.SP_CreateTournament(tournamentMatchId, UserId, true, "This is my first tournament", tournamentName, model);
            if (tournamentId <= 0)
            {
                return StatusCode(500, new { message = "The tournament could not be created." });
            }

            string? joinCode = ubo.ConfigureTournamentPrivacy(tournamentId, UserId, isPrivate);

            return Ok(new
            {
                Tournament = tournamentId,
                isPrivate = isPrivate,
                joinCode = joinCode
            });
        }

    }

    [Authorize]
    [ApiController]
    [Route("api/matches")]
    public class MatchesController : ControllerBase
    {
        [HttpGet("tournaments")]
        public IActionResult GetTournaments()
        {
            UserBackoffice ubo = new UserBackoffice();
            StringValues values;
            this.Request.Headers.TryGetValue("Authorization", out values);

            int userId = ubo.SP_GetUserFromToken(values.ToString());
            if (userId == -1)
            {
                return Unauthorized();
            }

            try
            {
                TournamentEditorBackoffice backoffice = new TournamentEditorBackoffice();
                return Ok(new { tournaments = backoffice.GetTournamentsForMatchPicker(userId) });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "The tournament list could not be loaded." });
            }
        }

        [HttpPost]
        public IActionResult Post([FromBody] int tournamentId)
        {
            UserBackoffice ubo = new UserBackoffice();
            StringValues values;
            this.Request.Headers.TryGetValue("Authorization", out values);

            int UserId = ubo.SP_GetUserFromToken(values.ToString());

            if (UserId == -1)
            {
                return Unauthorized();
            }

            string matches = ubo.SP_GetMatchesFromTournament(tournamentId);

            return Ok(new { matches = matches });
        }

    }

}
