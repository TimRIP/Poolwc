POOL PLAYER APP - FIRST VERSION

1. Run this SQL migration ONCE against your existing pooldb database:
   PoolDB/2026-09-30_player_registration.sql

2. Restart PoolApi after copying the new API files.

3. Open PoolPlayer/index.html in a browser.

4. API address:
   - On the same computer: http://localhost:5000
   - On a phone/tablet: use the LAN address of the computer running PoolApi,
     for example http://192.168.1.20:5000.

5. A player can:
   - create a user account
   - sign in
   - see tournaments
   - register for a free player place
   - cancel registration until a match result has been recorded

When a player registers, the API claims one of the generated Player:n places in
that tournament and changes that Player row to the registered user's real name.
Because the existing Seat rows keep the same PlayerId, the player's real name
appears automatically throughout the existing tournament tree.

SECURITY CHANGE INCLUDED:
Saving or clearing match results is now restricted to the administrator of the
tournament. This is important now that ordinary player accounts can log in.
