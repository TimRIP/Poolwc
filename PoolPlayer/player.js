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
    tournamentList: document.getElementById('tournamentList'),
    emptyTournaments: document.getElementById('emptyTournaments')
  };

  let token = localStorage.getItem(TOKEN_KEY) || '';
  let profile = null;
  let tournaments = [];

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

  function renderTournaments() {
    els.tournamentList.replaceChildren();
    els.emptyTournaments.classList.toggle('hidden', tournaments.length !== 0);

    tournaments.forEach(function (tournament) {
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

      if (tournament.isRegistered) {
        const badge = document.createElement('div');
        badge.className = 'registrationBadge';
        badge.textContent = 'Registered';
        top.append(titleWrap, badge);
      } else {
        top.append(titleWrap);
      }

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
        else registerForTournament(tournament.tournamentId);
      });

      bottom.append(info, button);
      card.append(top, bottom);
      els.tournamentList.append(card);
    });
  }

  async function registerForTournament(tournamentId) {
    showMessage('Registering...', '');
    try {
      const result = await api('/api/player/tournaments/' + tournamentId + '/register', { method: 'POST' });
      showMessage(result.message || 'You are registered.', 'good');
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
      await loadDashboard();
    } catch (error) {
      showMessage(error.message, 'bad');
    }
  }

  function signOut(showNotice) {
    token = '';
    profile = null;
    tournaments = [];
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
