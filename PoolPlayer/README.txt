POOL PLAYER APP - PLAYER REGISTRATION + PRIVATE TOURNAMENTS

1. For an existing database, run these migrations ONCE, in this order:
   PoolDB/2026-09-30_player_registration.sql
   PoolDB/2026-09-30_private_tournaments.sql

2. Restart PoolApi after copying the new API files.

3. Open PoolPlayer/index.html in a browser.

4. API address:
   - On the same computer: http://localhost:5000
   - On a phone/tablet: use the LAN address of the computer running PoolApi,
     for example http://192.168.1.20:5000.

5. A tournament administrator can create either:
   - Public tournament: shown in the normal player-app tournament list.
   - Private tournament: hidden from the list. A unique join code is generated
     when the tournament is created. Share that code only with invited players.

6. A player can:
   - create a user account
   - sign in
   - see public tournaments and private tournaments they already joined
   - enter a private tournament join code to find the hidden tournament
   - register for a free player place
   - cancel registration until a match result has been recorded

Private registration is also enforced by the API. Knowing only a tournament ID
is not enough to register for a private tournament; the correct join code must
be supplied unless that user is already registered.

When a player registers, the API claims one of the generated Player:n places in
that tournament and changes that Player row to the registered user's real name.
Because the existing Seat rows keep the same PlayerId, the player's real name
appears automatically throughout the existing tournament tree.

SECURITY CHANGE INCLUDED:
Saving or clearing match results remains restricted to the administrator of the
tournament. This is important now that ordinary player accounts can log in.
