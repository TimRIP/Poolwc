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

            Helper hel = new Helper();
            int tournamentMatchId = hel.CreateTournamentFromString(model);

            int tournamentId = ubo.SP_CreateTournament(tournamentMatchId, UserId, true, "This is my first tournament", "test", model);

            return Ok(new { Tournament = tournamentId });
        }

    }

    [Authorize]
    [ApiController]
    [Route("api/matches")]
    public class MatchesController : ControllerBase
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

            string matches = ubo.SP_GetMatchesFromTournament(Int32.Parse(model));

            return Ok(new { matches = matches });
        }

    }

}
