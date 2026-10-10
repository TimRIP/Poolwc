using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
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

            int userId = ubo.SP_GetUserFromToken(values.ToString());
            if (userId == -1)
            {
                return Unauthorized();
            }

            bool isPrivate;
            bool manualPlayerDraw;
            bool autoSchedule;
            int gameLengthMinutes;
            string tournamentName = "Tournament";
            DateTime? scheduleStart = null;
            TimeSpan venueOpenTime = new TimeSpan(9, 0, 0);
            TimeSpan venueCloseTime = new TimeSpan(22, 0, 0);
            var venueSeeds = new List<TournamentVenueSeed>();

            try
            {
                JObject tournamentJson = JObject.Parse(model);
                isPrivate = tournamentJson.Value<bool?>("privateTournament") ?? false;
                manualPlayerDraw = tournamentJson.Value<bool?>("manualPlayerDraw") ?? false;
                autoSchedule = tournamentJson.Value<bool?>("autoSchedule") ?? false;
                gameLengthMinutes = tournamentJson.Value<int?>("gameLengthMinutes") ?? 30;

                string suppliedName = tournamentJson.Value<string>("tournamentname");
                if (!string.IsNullOrWhiteSpace(suppliedName))
                {
                    tournamentName = suppliedName.Trim();
                }

                JArray venues = tournamentJson["venues"] as JArray;
                if (venues != null)
                {
                    foreach (JToken token in venues)
                    {
                        JObject venueObject = token as JObject;
                        if (venueObject == null) continue;

                        string venueName = (venueObject.Value<string>("name") ?? string.Empty).Trim();
                        string description = venueObject.Value<string>("description");
                        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

                        if (venueName.Length == 0) continue;
                        if (venueName.Length > 255)
                        {
                            return BadRequest(new { message = "Venue names can be at most 255 characters." });
                        }
                        if (description != null && description.Length > 255)
                        {
                            return BadRequest(new { message = "Venue descriptions can be at most 255 characters." });
                        }

                        venueSeeds.Add(new TournamentVenueSeed
                        {
                            Name = venueName,
                            Description = description
                        });
                    }
                }

                if (venueSeeds.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                {
                    return BadRequest(new { message = "Venue names must be unique inside a tournament." });
                }

                string scheduleStartText = tournamentJson.Value<string>("scheduleStart");
                if (!string.IsNullOrWhiteSpace(scheduleStartText))
                {
                    DateTime parsedStart;
                    if (!DateTime.TryParse(scheduleStartText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedStart))
                    {
                        return BadRequest(new { message = "The automatic schedule start date/time is invalid." });
                    }
                    scheduleStart = DateTime.SpecifyKind(parsedStart, DateTimeKind.Unspecified);
                }

                string venueOpenTimeText = tournamentJson.Value<string>("venueOpenTime");
                string venueCloseTimeText = tournamentJson.Value<string>("venueCloseTime");
                TimeSpan parsedVenueTime;
                if (!string.IsNullOrWhiteSpace(venueOpenTimeText))
                {
                    if (!TimeSpan.TryParseExact(venueOpenTimeText, @"hh\:mm", CultureInfo.InvariantCulture, out parsedVenueTime))
                    {
                        return BadRequest(new { message = "The daily venue opening time is invalid." });
                    }
                    venueOpenTime = parsedVenueTime;
                }
                if (!string.IsNullOrWhiteSpace(venueCloseTimeText))
                {
                    if (!TimeSpan.TryParseExact(venueCloseTimeText, @"hh\:mm", CultureInfo.InvariantCulture, out parsedVenueTime))
                    {
                        return BadRequest(new { message = "The daily venue closing time is invalid." });
                    }
                    venueCloseTime = parsedVenueTime;
                }

                if (autoSchedule)
                {
                    if (venueSeeds.Count == 0)
                    {
                        return BadRequest(new { message = "Add at least one venue before using automatic scheduling." });
                    }
                    if (!scheduleStart.HasValue)
                    {
                        return BadRequest(new { message = "Set the tournament start date and time before using automatic scheduling." });
                    }
                    if (gameLengthMinutes < 1 || gameLengthMinutes > 1440)
                    {
                        return BadRequest(new { message = "Game length must be between 1 and 1440 minutes." });
                    }
                    if (venueCloseTime <= venueOpenTime)
                    {
                        return BadRequest(new { message = "Venue closing time must be later than opening time." });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return BadRequest(new { message = "The tournament definition is not valid JSON." });
            }

            Helper hel = new Helper();
            int tournamentMatchId;
            try
            {
                tournamentMatchId = hel.CreateTournamentFromString(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return BadRequest(new { message = ex.Message });
            }

            int tournamentId = ubo.SP_CreateTournament(tournamentMatchId, userId, true, "Tournament", tournamentName, model);
            if (tournamentId <= 0)
            {
                return StatusCode(500, new { message = "The tournament could not be created." });
            }

            string joinCode = ubo.ConfigureTournamentPrivacy(tournamentId, userId, isPrivate);

            TournamentDrawBackoffice drawBackoffice = new TournamentDrawBackoffice();
            drawBackoffice.ConfigureManualDraw(tournamentId, userId, manualPlayerDraw);

            AutomaticScheduleResult scheduleResult;
            try
            {
                VenueBackoffice venueBackoffice = new VenueBackoffice();
                scheduleResult = venueBackoffice.ConfigureTournamentVenuesAndAutoSchedule(
                    tournamentId,
                    userId,
                    venueSeeds,
                    autoSchedule,
                    scheduleStart,
                    gameLengthMinutes,
                    venueOpenTime,
                    venueCloseTime);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new
                {
                    message = "The tournament was created, but its venues/automatic schedule could not be created. Make sure the tournament-venue migration has been run. " + ex.Message,
                    Tournament = tournamentId
                });
            }

            return Ok(new
            {
                Tournament = tournamentId,
                isPrivate = isPrivate,
                joinCode = joinCode,
                manualPlayerDraw = manualPlayerDraw,
                venueCount = scheduleResult.VenueCount,
                scheduledPools = scheduleResult.ScheduledPools,
                scheduleStartsAt = scheduleResult.StartsAt,
                venueOpenTime = venueOpenTime.ToString(@"hh\:mm"),
                venueCloseTime = venueCloseTime.ToString(@"hh\:mm"),
                estimatedFinish = scheduleResult.EstimatedFinish
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
