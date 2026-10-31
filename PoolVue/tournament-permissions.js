(function () {
  'use strict';
  const $ = id => document.getElementById(id);
  const tournament = $('perm-tournament'), user = $('perm-user'), venue = $('perm-venue');
  if (!tournament || !user || !venue) return;
  const message = $('perm-message'), editors = $('perm-editors');
  function token() { return localStorage.getItem('token'); }
  async function get(url, options) {
    const response = await fetch('http://localhost:5000' + url, {
      ...options, headers: { Authorization: token() || '', 'Content-Type': 'application/json', ...(options && options.headers || {}) }
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw Error(data.message || 'Request failed (' + response.status + ')');
    return data;
  }
  function tournamentListDate(value) {
    if (!value) return '';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    const pad = n => String(n).padStart(2, '0');
    return pad(date.getDate()) + '/' + pad(date.getMonth() + 1) + '/' + date.getFullYear() +
      ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes());
  }
  function fill(select, entries, placeholder) {
    select.replaceChildren(new Option(placeholder, ''));
    entries.forEach(x => select.add(new Option(x.label, x.id)));
  }
  function show(err) { message.textContent = err && err.message ? err.message : String(err); }
  async function loadTournaments() {
    if (!token()) { show('Log in first.'); return; }
    try {
      const data = await get('/api/matches/tournaments');
      const rows = data.tournaments || [];
      fill(tournament, rows.filter(x => x.isAdmin).map(x => ({ id:x.tournamentId, label:x.name + (tournamentListDate(x.createdAt) ? ' — ' + tournamentListDate(x.createdAt) : '') })), 'Select your tournament');
      show('Select a tournament to configure its editors.');
    } catch(e) { show(e); }
  }
  async function loadDetails() {
    editors.replaceChildren();
    if (!tournament.value) return;
    try {
      const base = '/api/tournament/' + encodeURIComponent(tournament.value);
      const [us, vs, ed] = await Promise.all([get(base + '/match-editors/users'), get(base + '/venues'), get(base + '/match-editors')]);
      fill(user, (us.users || []).map(x => ({ id:x.userName, label:x.userName })), 'Select user');
      fill(venue, (vs.venues || []).map(x => ({ id:x.facilityId, label:x.name })), 'Full tournament editing access');
      (ed.editors || []).forEach(x => {
        const line = document.createElement('div'); line.style.cssText = 'display:flex;gap:12px;align-items:center;margin:8px 0;';
        const label = document.createElement('span'); label.textContent = x.userName + ' — ' + (x.venueName || 'Full tournament access');
        const remove = document.createElement('button');remove.type = 'button';remove.textContent = 'Remove';
        remove.addEventListener('click', async () => {
          if (!confirm('Remove ' + x.userName + ' from this tournament?')) return;
          try { await get(base + '/match-editors/' + x.userId, {method:'DELETE'});await loadDetails();show('Permission removed.'); }
          catch(e) { show(e); }
        });
        line.append(label, remove);editors.append(line);
      });
    } catch(e) { show(e); }
  }
  $('perm-load').addEventListener('click', loadTournaments);
  tournament.addEventListener('change', loadDetails);
  $('perm-save').addEventListener('click', async () => {
    if (!tournament.value || !user.value) { show('Select a tournament and user.'); return; }
    try {
      await get('/api/tournament/' + encodeURIComponent(tournament.value) + '/match-editors', {
        method:'POST', body: JSON.stringify({ userName:user.value, venueId:venue.value ? Number(venue.value) : null })
      });
      await loadDetails();show('Permission saved.');
    } catch(e) { show(e); }
  });
  window.addEventListener('storage', e => { if (e.key === 'token') loadTournaments(); });
  window.addEventListener('pool-login-changed', loadTournaments);
  document.addEventListener('click', e => { if (e.target && e.target.textContent === 'Login') setTimeout(loadTournaments, 700); });
  loadTournaments();
})();
