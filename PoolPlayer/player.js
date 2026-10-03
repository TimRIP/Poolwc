(function () {
  'use strict';

  const API_KEY = 'poolPlayerApiBase';
  const TOKEN_KEY = 'poolPlayerToken';

  const els = {
    apiBase: document.getElementById('apiBase'),
    saveApiButton: document.getElementById('saveApiButton'),
    message: document.getElementById('message'),
    authPanel: document.getElementById('authPanel'),
    dashboard: document.getElementById('dashboard'),
    logoutButton: document.getElementById('logoutButton'),
    loginForm: document.getElementById('loginForm'),
    loginUsername: document.getElementById('loginUsername'),
    loginPassword: document.getElementById('loginPassword'),
    registerForm: document.getElementById('registerForm'),
    registerName: document.getElementById('registerName'),
    registerEmail: document.getElementById('registerEmail'),
    registerUsername: document.getElementById('registerUsername'),
    registerPassword: document.getElementById('registerPassword'),
    profileName: document.getElementById('profileName'),
    profileUsername: document.getElementById('profileUsername'),
    refreshButton: document.getElementById('refreshButton'),
    privateTournamentForm: document.getElementById('privateTournamentForm'),
    privateTournamentCode: document.getElementById('privateTournamentCode'),
    privateTournamentResult: document.getElementById('privateTournamentResult'),
    tournamentList: document.getElementById('tournamentList'),
    emptyTournaments: document.getElementById('emptyTournaments')
  };

  let token = localStorage.getItem(TOKEN_KEY) || '';
  let profile = null;
  let tournaments = [];
  let foundPrivateTournament = null;
  let foundPrivateCode = '';
  let poolAssignmentsByTournament = {};
  let poolAssignmentErrorsByTournament = {};

  function normalizedBase() {
    return (els.apiBase.value || 'http://localhost:5000').trim().replace(/\/+$/, '');
  }

  function showMessage(text, kind) {
    els.message.textContent = text || '';
    els.message.className = 'message' + (kind ? ' ' + kind : '');
    if (!text) els.message.classList.add('hidden');
  }

  async function api(path, options) {
    const opts = Object.assign({ method: 'GET' }, options || {});
    opts.headers = Object.assign({ 'Content-Type': 'application/json' }, opts.headers || {});
    if (token) opts.headers.Authorization = token;

    const response = await fetch(normalizedBase() + path, opts);
    let body = null;
    const text = await response.text();
    if (text) {
      try { body = JSON.parse(text); }
      catch (_) { body = { message: text }; }
    }

    if (!response.ok) {
      const error = new Error(body && body.message ? body.message : 'Request failed (' + response.status + ').');
      error.status = response.status;
      error.body = body;
      throw error;
    }

    return body;
  }

  function setSignedInUi(signedIn) {
    els.authPanel.classList.toggle('hidden', signedIn);
    els.dashboard.classList.toggle('hidden', !signedIn);
    els.logoutButton.classList.toggle('hidden', !signedIn);
  }

  async function signIn(username, password) {
    const data = await api('/api/token', {
      method: 'POST',
      body: JSON.stringify({ username: username, password: password })
    });

    token = 'Bearer ' + data.token;
    localStorage.setItem(TOKEN_KEY, token);
    await loadDashboard();
  }

  async function loadDashboard() {
    try {
      const results = await Promise.all([
        api('/api/player/me'),
        api('/api/player/tournaments')
      ]);

      profile = results[0];
      tournaments = (results[1] && results[1].tournaments) || [];
      await loadPoolAssignments();
      setSignedInUi(true);
      renderProfile();
      renderTournaments();
    } catch (error) {
      if (error.status === 401) {
        signOut(false);
        showMessage('Your login has expired. Please sign in again.', 'bad');
        return;
      }
      showMessage(error.message, 'bad');
      throw error;
    }
  }

  function renderProfile() {
    els.profileName.textContent = profile && profile.name ? profile.name : 'Player';
    els.profileUsername.textContent = profile && profile.username ? '@' + profile.username : '';
  }

  async function loadPoolAssignments() {
    poolAssignmentsByTournament = {};
    poolAssignmentErrorsByTournament = {};
    const joined = tournaments.filter(function (tournament) { return tournament.isRegistered; });

    await Promise.all(joined.map(async function (tournament) {
      try {
        const result = await api('/api/player/tournaments/' + tournament.tournamentId + '/pools');
        poolAssignmentsByTournament[tournament.tournamentId] = (result && result.pools) || [];
      } catch (error) {
        poolAssignmentsByTournament[tournament.tournamentId] = [];
        poolAssignmentErrorsByTournament[tournament.tournamentId] = error && error.message ? error.message : 'Could not load pool assignments.';
        console.warn('Could not load pool assignments for tournament ' + tournament.tournamentId, error);
      }
    }));
  }

  function parseScheduleDate(value) {
    if (!value) return null;
    const match = String(value).match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/);
    if (!match) return null;
    return {
      year: match[1],
      month: match[2],
      day: match[3],
      hour: match[4],
      minute: match[5]
    };
  }

  function formatSchedule(value) {
    const parts = parseScheduleDate(value);
    if (!parts) return 'Time not set yet';
    return parts.day + '/' + parts.month + '/' + parts.year + ' · ' + parts.hour + ':' + parts.minute;
  }

  function buildPlayerPools(tournamentId) {
    const wrap = document.createElement('section');
    wrap.className = 'playerPools';

    const heading = document.createElement('div');
    heading.className = 'playerPoolsHeading';
    heading.textContent = 'Your pools';
    wrap.append(heading);

    const pools = poolAssignmentsByTournament[tournamentId] || [];
    const loadError = poolAssignmentErrorsByTournament[tournamentId];
    if (loadError) {
      const empty = document.createElement('div');
      empty.className = 'poolScheduleEmpty';
      empty.textContent = 'Could not load your pool: ' + loadError;
      wrap.append(empty);
      return wrap;
    }

    if (!pools.length) {
      const empty = document.createElement('div');
      empty.className = 'poolScheduleEmpty';
      empty.textContent = 'Your pool has not been assigned yet. Refresh after the tournament administrator updates the draw.';
      wrap.append(empty);
      return wrap;
    }

    const list = document.createElement('div');
    list.className = 'playerPoolList';

    pools.forEach(function (pool) {
      const item = document.createElement('div');
      item.className = 'playerPoolItem';

      const names = document.createElement('div');
      names.className = 'playerPoolNames';

      const stage = document.createElement('div');
      stage.className = 'playerPoolStage';
      stage.textContent = pool.stageName || 'Tournament stage';

      const poolName = document.createElement('div');
      poolName.className = 'playerPoolName';
      poolName.textContent = pool.poolName || ('Pool #' + pool.poolMatchId);
      names.append(stage, poolName);

      const details = document.createElement('div');
      details.className = 'playerPoolDetails';

      const venue = document.createElement('div');
      venue.className = 'poolDetailLine';
      venue.textContent = 'Venue: ' + (pool.venueName || 'Not set yet');

      const schedule = document.createElement('div');
      schedule.className = 'poolDetailLine';
      schedule.textContent = 'Starts: ' + formatSchedule(pool.fromTime);

      details.append(venue, schedule);
      item.append(names, details);
      list.append(item);
    });

    wrap.append(list);
    return wrap;
  }

  function buildTournamentCard(tournament, joinCode) {
    const card = document.createElement('article');
    card.className = 'tournamentCard' + (tournament.isRegistered ? ' joined' : '');

    const top = document.createElement('div');
    top.className = 'tournamentTop';

    const titleWrap = document.createElement('div');
    const title = document.createElement('div');
    title.className = 'tournamentName';
    title.textContent = tournament.name || ('Tournament #' + tournament.tournamentId);
    const meta = document.createElement('div');
    meta.className = 'tournamentMeta';
    meta.textContent = tournament.registeredPlayers + ' / ' + tournament.capacity + ' player places filled';
    titleWrap.append(title, meta);
    top.append(titleWrap);

    const badgeRow = document.createElement('div');
    badgeRow.className = 'badgeRow';

    if (tournament.isPrivate) {
      const privateBadge = document.createElement('div');
      privateBadge.className = 'privateBadge';
      privateBadge.textContent = 'Private';
      badgeRow.append(privateBadge);
    }

    if (tournament.isRegistered) {
      const badge = document.createElement('div');
      badge.className = 'registrationBadge';
      badge.textContent = 'Registered';
      badgeRow.append(badge);
    }

    if (badgeRow.childNodes.length) top.append(badgeRow);

    const bottom = document.createElement('div');
    bottom.className = 'tournamentBottom';

    const info = document.createElement('div');
    info.className = 'slotInfo';
    if (tournament.isRegistered) {
      info.textContent = 'You are entered as ' + (tournament.playerName || 'player') + ' (player #' + tournament.playerId + ').';
    } else if (tournament.availableSlots > 0) {
      info.textContent = tournament.availableSlots + ' place' + (tournament.availableSlots === 1 ? '' : 's') + ' available.';
    } else {
      info.textContent = 'Tournament is full.';
    }

    const button = document.createElement('button');
    button.type = 'button';
    button.className = tournament.isRegistered ? 'button danger' : 'button primary';
    button.textContent = tournament.isRegistered ? 'Cancel registration' : 'Register';
    button.disabled = !tournament.isRegistered && tournament.availableSlots <= 0;
    button.addEventListener('click', function () {
      if (tournament.isRegistered) cancelRegistration(tournament.tournamentId);
      else registerForTournament(tournament.tournamentId, joinCode || '');
    });

    bottom.append(info, button);
    card.append(top, bottom);

    if (tournament.isRegistered) {
      card.append(buildPlayerPools(tournament.tournamentId));
    }

    return card;
  }

  function renderTournaments() {
    els.tournamentList.replaceChildren();
    els.emptyTournaments.classList.toggle('hidden', tournaments.length !== 0);

    tournaments.forEach(function (tournament) {
      els.tournamentList.append(buildTournamentCard(tournament, ''));
    });
  }

  function clearPrivateSearchResult() {
    foundPrivateTournament = null;
    foundPrivateCode = '';
    els.privateTournamentResult.replaceChildren();
    els.privateTournamentResult.classList.add('hidden');
  }

  function renderPrivateTournament() {
    els.privateTournamentResult.replaceChildren();
    if (!foundPrivateTournament) {
      els.privateTournamentResult.classList.add('hidden');
      return;
    }

    els.privateTournamentResult.append(buildTournamentCard(foundPrivateTournament, foundPrivateCode));
    els.privateTournamentResult.classList.remove('hidden');
  }

  async function findPrivateTournament(code) {
    const normalizedCode = (code || '').trim().toUpperCase().replace(/[\s-]+/g, '');
    if (!normalizedCode) {
      clearPrivateSearchResult();
      showMessage('Enter the private tournament join code.', 'bad');
      return;
    }

    showMessage('Looking for private tournament...', '');
    try {
      const result = await api('/api/player/tournaments/find?code=' + encodeURIComponent(normalizedCode));
      foundPrivateTournament = result && result.tournament ? result.tournament : null;
      foundPrivateCode = normalizedCode;
      renderPrivateTournament();
      showMessage(foundPrivateTournament ? 'Private tournament found.' : 'Tournament not found.', foundPrivateTournament ? 'good' : 'bad');
    } catch (error) {
      clearPrivateSearchResult();
      showMessage(error.message, 'bad');
    }
  }

  async function registerForTournament(tournamentId, joinCode) {
    showMessage('Registering...', '');
    try {
      const suffix = joinCode ? '?code=' + encodeURIComponent(joinCode) : '';
      const result = await api('/api/player/tournaments/' + tournamentId + '/register' + suffix, { method: 'POST' });
      showMessage(result.message || 'You are registered.', 'good');
      clearPrivateSearchResult();
      els.privateTournamentCode.value = '';
      await loadDashboard();
    } catch (error) {
      showMessage(error.message, 'bad');
    }
  }

  async function cancelRegistration(tournamentId) {
    if (!window.confirm('Cancel your registration for this tournament?')) return;

    showMessage('Cancelling registration...', '');
    try {
      const result = await api('/api/player/tournaments/' + tournamentId + '/register', { method: 'DELETE' });
      showMessage(result.message || 'Registration cancelled.', 'good');
      clearPrivateSearchResult();
      els.privateTournamentCode.value = '';
      await loadDashboard();
    } catch (error) {
      showMessage(error.message, 'bad');
    }
  }

  function signOut(showNotice) {
    token = '';
    profile = null;
    tournaments = [];
    poolAssignmentsByTournament = {};
    clearPrivateSearchResult();
    if (els.privateTournamentCode) els.privateTournamentCode.value = '';
    localStorage.removeItem(TOKEN_KEY);
    setSignedInUi(false);
    els.tournamentList.replaceChildren();
    if (showNotice !== false) showMessage('Signed out.', 'good');
  }

  els.saveApiButton.addEventListener('click', function () {
    localStorage.setItem(API_KEY, normalizedBase());
    showMessage('API address saved.', 'good');
    if (token) loadDashboard().catch(function () {});
  });

  els.loginForm.addEventListener('submit', async function (event) {
    event.preventDefault();
    showMessage('Signing in...', '');
    try {
      await signIn(els.loginUsername.value.trim(), els.loginPassword.value);
      els.loginPassword.value = '';
      showMessage('Signed in.', 'good');
    } catch (error) {
      signOut(false);
      showMessage(error.status === 401 ? 'Wrong username or password.' : error.message, 'bad');
    }
  });

  els.registerForm.addEventListener('submit', async function (event) {
    event.preventDefault();
    const username = els.registerUsername.value.trim();
    const password = els.registerPassword.value;

    showMessage('Creating account...', '');
    try {
      await api('/api/users/register', {
        method: 'POST',
        body: JSON.stringify({
          username: username,
          password: password,
          name: els.registerName.value.trim(),
          email: els.registerEmail.value.trim() || null
        })
      });

      await signIn(username, password);
      els.registerPassword.value = '';
      showMessage('Account created and signed in.', 'good');
    } catch (error) {
      showMessage(error.message, 'bad');
    }
  });

  els.privateTournamentForm.addEventListener('submit', function (event) {
    event.preventDefault();
    findPrivateTournament(els.privateTournamentCode.value);
  });

  els.privateTournamentCode.addEventListener('input', function () {
    const start = this.selectionStart;
    this.value = this.value.toUpperCase();
    if (typeof start === 'number') this.setSelectionRange(start, start);
  });

  els.logoutButton.addEventListener('click', function () { signOut(true); });
  els.refreshButton.addEventListener('click', function () {
    showMessage('Refreshing...', '');
    loadDashboard().then(function () { showMessage('Updated.', 'good'); }).catch(function () {});
  });

  els.apiBase.value = localStorage.getItem(API_KEY) || 'http://localhost:5000';

  if (token) {
    loadDashboard().catch(function () {});
  } else {
    setSignedInUi(false);
  }
})();
