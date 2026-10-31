/* Read-only tournament visualization. Shares the existing Matches API contract. */
new Vue({
  el: '#visualization',
  data: {
    tournaments: [], selectedTournamentId: '', rows: [], loading: false, error: '',
    routes: [], connections: [], routeWarning: '',
    editorStage: null, stageEditorUrl: '', editorPool: null, poolAdvanceUrl: '', poolAdvanceRevision: 0,
    editorMatches: [], editorLoading: false, editorSaving: false, editorError: '', editorMessage: '',
    playerSearch: '', playerSearchOpen: false, finderPlayers: [], finderLoading: false, finderError: '',
    selectedFinderPlayer: null, finderMatches: [], finderMatchesLoading: false,
    playerNamesOpen: false, renameablePlayers: [], playerNamesLoading: false,
    savingPlayerNameId: null, playerNameMessage: '', playerNameError: '',
    poolScheduleDetails: {}, poolAdvanceStates: {}, playerDestinations: {}, wildcardStages: {}, tournamentPodium: null, podiumLoading: false
  },
  computed: {
    stages() {
      const nodes = new Map();
      for (const r of this.rows) {
        const id = Number(r.MatchId);
        if (!Number.isFinite(id)) continue;
        if (!nodes.has(id)) {
          nodes.set(id, {
            matchId: id,
            parentMatchId: r.ParentMatchId == null ? null : Number(r.ParentMatchId),
            matchName: r.MatchName || ('Match ' + id),
            players: [],
            children: []
          });
        }
        const n = nodes.get(id);
        if (r.PlayerName && !n.players.includes(r.PlayerName)) n.players.push(r.PlayerName);
      }
      const roots = [];
      for (const node of nodes.values()) {
        if (node.parentMatchId != null && nodes.has(node.parentMatchId) && node.parentMatchId !== node.matchId) {
          nodes.get(node.parentMatchId).children.push(node);
        } else {
          roots.push(node);
        }
      }
      const sort = arr => {
        arr.sort((a, b) => a.matchId - b.matchId);
        arr.forEach(n => sort(n.children));
      };
      sort(roots);

      const isPool = n => n.children.length > 0 && n.children.every(c => !c.children.length);
      const stageNodes = [];
      const visit = n => {
        if (n.children.some(isPool)) stageNodes.push(n);
        n.children.filter(c => !isPool(c) && c.children.length).forEach(visit);
      };
      roots.forEach(visit);
      const unique = [...new Map(stageNodes.map(n => [n.matchId, n])).values()];
      return unique.map(stage => {
        const pools = stage.children.filter(isPool).map(node => {
          const matches = node.children.map(m => ({ matchId: m.matchId, matchName: m.matchName, players: m.players }));
          return {
            matchId: node.matchId,
            matchName: node.matchName,
            matches,
            players: [...new Set([...node.players, ...matches.flatMap(m => m.players)])]
          };
        });
        return { matchId: stage.matchId, matchName: stage.matchName, pools };
      });
    },
    matchingPlayers() {
      const q = this.playerSearch.trim().toLowerCase();
      if (!q) return [];
      return this.finderPlayers
        .filter(p => [p.playerName, p.playerId, p.mmr == null ? '' : p.mmr].join(' ').toLowerCase().includes(q))
        .slice(0, 60);
    },
    editorPlayers() {
      const names = new Map();
      for (const m of this.editorMatches) {
        for (const seat of m.seats) {
          const key = seat.playerId != null ? 'id:' + seat.playerId : 'name:' + seat.playerName;
          if (!names.has(key)) names.set(key, { key, playerName: seat.playerName, playerId: seat.playerId });
        }
      }
      return [...names.values()];
    },
    editorIsSeries() {
      const seen = new Set();
      for (const m of this.editorMatches) {
        if (m.seats.length !== 2) continue;
        const pair = m.seats.map(s => String(s.playerId)).sort().join(':');
        if (seen.has(pair)) return true;
        seen.add(pair);
      }
      return false;
    },
    editorStandings() {
      const stats = this.editorPlayers.map(p => ({ key: p.key, points: 0, score: 0, played: 0, place: '–' }));
      const byKey = new Map(stats.map(x => [x.key, x]));
      for (const m of this.editorMatches) {
        if (m.seats.length !== 2 || !Number.isFinite(m.playTo)) continue;
        const [a, b] = m.seats;
        if (a.score === '' || b.score === '') continue;
        const av = Number(a.score), bv = Number(b.score);
        if (!Number.isFinite(av) || !Number.isFinite(bv)) continue;
        const aw = av === m.playTo, bw = bv === m.playTo;
        if (aw === bw) continue;
        const x = byKey.get(this.seatKey(a)), y = byKey.get(this.seatKey(b));
        if (!x || !y) continue;
        x.score += av; y.score += bv;
        x.played++; y.played++;
        if (aw) x.points++; else y.points++;
      }
      const first = this.editorMatches[0];
      const descending = !first || first.playFrom === null || first.playTo === null || first.playFrom < first.playTo;
      const order = [...stats].sort((a, b) => b.points - a.points || (descending ? b.score - a.score : a.score - b.score));
      order.forEach((x, i) => { x.place = i + 1; });
      return Object.fromEntries(stats.map(x => [x.key, x]));
    }
  },
  methods: {
    normalizePlayerName(name) {
      return String(name || '').trim().toLocaleLowerCase();
    },
    playerNameChanged(player) {
      return !!player && !!String(player.editName || '').trim() && String(player.editName).trim() !== String(player.playerName || '').trim();
    },
    async loadRenameablePlayers() {
      this.renameablePlayers = [];
      this.playerNameError = '';
      this.playerNameMessage = '';
      if (!this.selectedTournamentId) return;
      this.playerNamesLoading = true;
      try {
        const r = await axios.get('http://localhost:5000/api/tournament/' + encodeURIComponent(this.selectedTournamentId) + '/players', { headers: this.authHeaders() });
        const list = Array.isArray(r.data && r.data.players) ? r.data.players : [];
        this.renameablePlayers = list.map(x => ({
          playerId: Number(x.playerId),
          playerName: x.playerName || ('Player #' + x.playerId),
          editName: x.playerName || ('Player #' + x.playerId)
        }));
      } catch (e) {
        this.playerNameError = (e.response && e.response.data && e.response.data.message) || 'Could not load player names. Administrator access may be required.';
      } finally {
        this.playerNamesLoading = false;
      }
    },
    async savePlayerName(player) {
      if (!this.playerNameChanged(player) || this.savingPlayerNameId !== null) return;
      const name = String(player.editName || '').trim();
      if (!name || name.length > 255) {
        this.playerNameError = 'Name must contain 1–255 characters.';
        return;
      }
      this.savingPlayerNameId = player.playerId;
      this.playerNameError = '';
      this.playerNameMessage = '';
      try {
        const r = await axios.put(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.selectedTournamentId) + '/players/' + encodeURIComponent(player.playerId) + '/name',
          { name },
          { headers: Object.assign({ 'Content-Type': 'application/json; charset=utf-8' }, this.authHeaders()) }
        );
        player.playerName = name;
        player.editName = name;
        await this.loadMatches();
        this.playerNameMessage = (r.data && r.data.message) || 'Player name saved.';
      } catch (e) {
        this.playerNameError = (e.response && e.response.data && e.response.data.message) || 'Could not save player name.';
      } finally {
        this.savingPlayerNameId = null;
      }
    },
    async loadFinderPlayers() {
      this.finderPlayers = [];
      this.finderMatches = [];
      this.selectedFinderPlayer = null;
      this.playerSearch = '';
      this.finderError = '';
      if (!this.selectedTournamentId) return;
      this.finderLoading = true;
      try {
        const res = await axios.get('http://localhost:5000/api/tournament/' + encodeURIComponent(this.selectedTournamentId) + '/player-match-search/players', { headers: this.authHeaders() });
        this.finderPlayers = Array.isArray(res.data && res.data.players) ? res.data.players : [];
      } catch (e) {
        this.finderError = (e.response && e.response.data && e.response.data.message) || 'Could not load players for match search.';
      } finally {
        this.finderLoading = false;
      }
    },
    onFinderInput() {
      if (this.selectedFinderPlayer && this.playerSearch.trim().toLowerCase() !== String(this.selectedFinderPlayer.playerName || '').trim().toLowerCase()) {
        this.selectedFinderPlayer = null;
        this.finderMatches = [];
      }
    },
    clearFinder() {
      this.playerSearch = '';
      this.selectedFinderPlayer = null;
      this.finderMatches = [];
      this.finderError = '';
    },
    async selectFinderPlayer(player) {
      this.selectedFinderPlayer = player;
      this.playerSearch = player.playerName;
      this.finderMatches = [];
      this.finderError = '';
      this.finderMatchesLoading = true;
      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.selectedTournamentId) + '/player-match-search/players/' + encodeURIComponent(player.playerId) + '/matches',
          { headers: this.authHeaders() }
        );
        this.finderMatches = Array.isArray(res.data && res.data.matches) ? res.data.matches : [];
      } catch (e) {
        this.finderError = (e.response && e.response.data && e.response.data.message) || 'Could not load player matches.';
      } finally {
        this.finderMatchesLoading = false;
      }
    },
    finderSchedule(match) {
      if (!match || !match.fromTime) return 'Time not set';
      const dt = new Date(match.fromTime);
      return Number.isNaN(dt.getTime())
        ? String(match.fromTime)
        : dt.toLocaleString('en-GB', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
    },
    openFinderMatch(match) {
      const id = Number(match && match.matchId);
      for (const stage of this.stages) {
        for (const pool of stage.pools) {
          if (pool.matches.some(m => m.matchId === id)) {
            this.openPool(pool);
            return;
          }
        }
      }
      this.finderError = 'Match not found in the current visualization.';
    },
    buildPoolAdvanceVisualState(state) {
      const done = !!(state && state.stageComplete);
      const playerStatuses = {};
      if (!state || !done) return { done, playerStatuses };

      const advancingPlaces = new Set(
        (Array.isArray(state.destinations) ? state.destinations : [])
          .map(destination => Number(destination.autoSelectPlace))
          .filter(place => Number.isInteger(place) && place > 0)
      );

      const wildcardAdvancedIds = new Set(
        (Array.isArray(state.wildcardSlots) ? state.wildcardSlots : [])
          .map(slot => slot && slot.playerId)
          .filter(id => id !== null && id !== undefined)
          .map(id => Number(id))
          .filter(id => Number.isFinite(id))
      );

      const addStatus = (name, status) => {
        const key = this.normalizePlayerName(name);
        if (!key) return;
        playerStatuses[key] = status;
      };

      const players = Array.isArray(state.players) ? state.players : [];
      for (const player of players) {
        const playerId = Number(player && player.playerId);
        const playerName = player && player.playerName ? player.playerName : (Number.isFinite(playerId) ? ('Player #' + playerId) : 'Player');
        const place = Number(player && player.place);
        if (wildcardAdvancedIds.has(playerId) || advancingPlaces.has(place)) addStatus(playerName, 'advanced');
        else if (player && player.place !== null && player.place !== undefined) addStatus(playerName, 'eliminated');
      }

      const wildcardCandidates = Array.isArray(state.wildcardCandidates) ? state.wildcardCandidates : [];
      for (const player of wildcardCandidates) {
        const playerId = Number(player && player.playerId);
        const playerName = player && player.playerName ? player.playerName : (Number.isFinite(playerId) ? ('Player #' + playerId) : 'Player');
        if (wildcardAdvancedIds.has(playerId)) addStatus(playerName, 'advanced');
        else addStatus(playerName, 'eliminated');
      }

      const reservedWildcardNames = [];
      for (const slot of Array.isArray(state.wildcardSlots) ? state.wildcardSlots : []) {
        if (slot.playerId === null || slot.playerId === undefined) continue;
        const player = [...players, ...wildcardCandidates].find(p => Number(p.playerId) === Number(slot.playerId));
        if (player && player.playerName) reservedWildcardNames.push(this.normalizePlayerName(player.playerName));
      }
      return { done, playerStatuses, reservedWildcardNames };
    },
    async loadPoolAdvanceStates() {
      const poolIds = this.stages.flatMap(stage => stage.pools.map(pool => Number(pool.matchId))).filter(id => Number.isFinite(id));
      if (!poolIds.length) {
        this.poolAdvanceStates = {};
        this.poolScheduleDetails = {};
        this.tournamentPodium = null;
        return;
      }
      const nextStates = {};
      await Promise.all(poolIds.map(async poolId => {
        try {
          const res = await axios.get('http://localhost:5000/api/match/' + encodeURIComponent(poolId) + '/advancement', { headers: this.authHeaders() });
          nextStates[String(poolId)] = this.buildPoolAdvanceVisualState(res.data);
        } catch (e) {
          nextStates[String(poolId)] = { done: false, playerStatuses: {} };
        }
      }));
      this.poolAdvanceStates = nextStates;
    },
    poolVisualState(pool) {
      return (pool && this.poolAdvanceStates[String(pool.matchId)]) || { done: false, playerStatuses: {} };
    },
    poolIsDone(pool) {
      return !!this.poolVisualState(pool).done;
    },
    poolLacksPlayers(pool) {
      // Wildcards may be in the pulje before their scheduled match seats update.
      // Assess available players at the pulje level instead of each match.
      if (!pool || !Array.isArray(pool.matches) || !pool.matches.length) return true;
      const assigned = new Set((pool.players || [])
        .map(name => this.normalizePlayerName(name)).filter(Boolean));
      if (assigned.size < 2) return true;

      const count = pool.matches.length;
      const roundRobinSize = (1 + Math.sqrt(1 + 8 * count)) / 2;
      if (Number.isInteger(roundRobinSize) && roundRobinSize >= 3) {
        const actualPairs = pool.matches.map(m => [...new Set((m.players || [])
          .map(name => this.normalizePlayerName(name)).filter(Boolean))])
          .filter(names => names.length === 2)
          .map(names => names.sort().join('|'));
        const isRepeatedSeries = actualPairs.length >= 2 && new Set(actualPairs).size === 1;
        if (!isRepeatedSeries) return assigned.size < roundRobinSize;
      }
      return false;
    },
    poolCardClass(pool) {
      if (this.poolIsDone(pool)) return 'vizPoolDone';
      return this.poolLacksPlayers(pool) ? 'vizPoolWaiting' : 'vizPoolPending';
    },
    poolStatusText(pool) {
      if (this.poolIsDone(pool)) return 'Done';
      return this.poolLacksPlayers(pool) ? 'Not enough players' : 'Not done';
    },
    playerVisualStatus(pool, playerName) {
      const state = this.poolVisualState(pool);
      return state && state.playerStatuses ? (state.playerStatuses[this.normalizePlayerName(playerName)] || '') : '';
    },
    playerClass(pool, playerName) {
      const status = this.playerVisualStatus(pool, playerName);
      return status === 'advanced' ? 'vizPlayerAdvanced' : (status === 'eliminated' ? 'vizPlayerEliminated' : '');
    },
    destinationLabel(poolId) {
      const target = this.stages.flatMap(stage => stage.pools.map(p => ({stage, pool:p})))
        .find(x => Number(x.pool.matchId) === Number(poolId));
      return target ? target.stage.matchName + ' · ' + target.pool.matchName : null;
    },
    playerStatusText(pool, playerName) {
      const status = this.playerVisualStatus(pool, playerName);
      if (status === 'eliminated') return 'eliminated';
      if (status !== 'advanced') return '';
      const source = this.playerDestinations[String(pool.matchId)] || {};
      const targetPoolId = source[this.normalizePlayerName(playerName)];
      const destination = targetPoolId ? this.destinationLabel(targetPoolId) : null;
      if (destination) return '→ ' + destination;
      const wildcardStage = (this.wildcardStages[String(pool.matchId)] || {})[this.normalizePlayerName(playerName)];
      return wildcardStage ? '→ ' + wildcardStage + ' (wildcard)' : '→ destination not assigned';
    },
    openStage(stage) {
      if (!stage || !this.selectedTournamentId) return;
      this.editorStage = stage;
      this.stageEditorUrl = 'matches.html?embeddedStage=1&tournamentId=' + encodeURIComponent(this.selectedTournamentId) + '&stageId=' + encodeURIComponent(stage.matchId);
    },
    async closeStage() {
      this.editorStage = null;
      this.stageEditorUrl = '';
      await this.loadMatches();
    },
    seatKey(seat) {
      return seat.playerId != null ? 'id:' + seat.playerId : 'name:' + seat.playerName;
    },
    seriesSeat(player, match) {
      return match.seats.find(s => this.seatKey(s) === player.key) || null;
    },
    matchCellClass(match, player) {
      if (!match || !player || match.seats.length !== 2) return '';
      const other = match.seats.find(s => s !== player);
      if (!other || player.score === '' || other.score === '' || !Number.isFinite(match.playTo)) return '';
      const mine = Number(player.score), theirs = Number(other.score);
      if (!Number.isFinite(mine) || !Number.isFinite(theirs)) return '';
      if (mine === match.playTo && theirs !== match.playTo) return 'vizMatrixWinner';
      if (theirs === match.playTo && mine !== match.playTo) return 'vizMatrixLoser';
      return '';
    },
    matrixCellClass(row, col) {
      const match = this.matrixMatch(row, col);
      return match ? this.matchCellClass(match, this.matrixSeat(row, col)) : '';
    },
    setMatchScore(match, seat, value) {
      if (!seat || !match) return;
      seat.score = value;
      const opponent = match.seats.find(s => s !== seat);
      if (!opponent || value === '' || !Number.isFinite(match.playTo)) return;
      const entered = Number(value);
      if (!Number.isInteger(entered) || entered === match.playTo) return;
      if (match.minScore !== null && (entered < match.minScore || entered > match.maxScore)) return;
      opponent.score = String(match.playTo);
    },
    seriesChange(player, match, value) {
      this.setMatchScore(match, this.seriesSeat(player, match), value);
    },
    matrixMatch(row, col) {
      if (row.key === col.key) return null;
      return this.editorMatches.find(m =>
        m.seats.length === 2 &&
        m.seats.some(s => (s.playerId != null ? 'id:' + s.playerId : 'name:' + s.playerName) === row.key) &&
        m.seats.some(s => (s.playerId != null ? 'id:' + s.playerId : 'name:' + s.playerName) === col.key)
      ) || null;
    },
    matrixSeat(row, col) {
      const match = this.matrixMatch(row, col);
      return match && match.seats.find(s => (s.playerId != null ? 'id:' + s.playerId : 'name:' + s.playerName) === row.key);
    },
    matrixChange(row, col, value) {
      const match = this.matrixMatch(row, col);
      this.setMatchScore(match, this.matrixSeat(row, col), value);
    },
    async openPool(pool) {
      this.editorPool = pool;
      this.poolAdvanceUrl = 'matches.html?embeddedPool=1&tournamentId=' + encodeURIComponent(this.selectedTournamentId) + '&poolId=' + encodeURIComponent(pool.matchId) + '&refresh=' + this.poolAdvanceRevision;
      this.editorMatches = [];
      this.editorError = '';
      this.editorMessage = '';
      this.editorLoading = true;
      try {
        const response = await Promise.all(pool.matches.map(m => axios.get('http://localhost:5000/api/match/' + encodeURIComponent(m.matchId), { headers: this.authHeaders() })));
        this.editorMatches = response.map((res, i) => {
          const details = res.data || {};
          const rules = details.matchRules || {};
          const playTo = rules.playTo === null || rules.playTo === undefined ? null : Number(rules.playTo);
          const playFrom = rules.playFrom === null || rules.playFrom === undefined ? null : Number(rules.playFrom);
          return {
            matchId: pool.matches[i].matchId,
            matchName: details.matchName || pool.matches[i].matchName,
            playTo: Number.isFinite(playTo) ? playTo : null,
            playFrom: Number.isFinite(playFrom) ? playFrom : null,
            minScore: Number.isFinite(playFrom) && Number.isFinite(playTo) ? Math.min(playFrom, playTo) : null,
            maxScore: Number.isFinite(playFrom) && Number.isFinite(playTo) ? Math.max(playFrom, playTo) : null,
            seats: (Array.isArray(details.seats) ? details.seats : [])
              .filter(x => x.playerId !== null && x.playerId !== undefined)
              .map(x => ({
                seatId: x.seatId,
                playerId: x.playerId,
                playerName: x.playerName || ('Player #' + x.playerId),
                score: x.resultPoints === null || x.resultPoints === undefined ? '' : String(x.resultPoints),
                original: x.resultPoints === null || x.resultPoints === undefined ? '' : String(x.resultPoints),
                originalPlace: x.resultMatchPlace
              }))
          };
        });
      } catch (e) {
        this.editorError = 'Could not load match details. Check login and server connection.';
      } finally {
        this.editorLoading = false;
      }
    },
    closePool() {
      if (this.editorSaving) return;
      this.editorPool = null;
      this.poolAdvanceUrl = '';
      this.editorMatches = [];
      this.editorError = '';
      this.editorMessage = '';
    },
    async savePoolResults(skipRefresh=false){
      if(this.editorSaving)return;
      this.editorError='';this.editorMessage='';
      const changes=[];
      for(const m of this.editorMatches){
        if(m.seats.length!==2)continue;
        const [a,b]=m.seats;
        if(a.score===a.original && b.score===b.original)continue;
        const bothEmpty=a.score==='' && b.score==='';
        if(bothEmpty){changes.push({id:m.matchId,clear:true});continue;}
        const scoreA=Number(a.score),scoreB=Number(b.score);
        if(a.score===''||b.score===''||!Number.isInteger(scoreA)||!Number.isInteger(scoreB)){
          this.editorError=m.matchName+': enter two whole-number scores, or clear both.';return;
        }
        if(m.minScore!==null&&(scoreA<m.minScore||scoreB<m.minScore||scoreA>m.maxScore||scoreB>m.maxScore)){
          this.editorError=m.matchName+': scores must be between '+m.minScore+' and '+m.maxScore+'.';return;
        }
        if(m.playTo===null || (scoreA===m.playTo)===(scoreB===m.playTo)){
          this.editorError=m.matchName+': exactly one player must finish on PlayTo ('+m.playTo+').';return;
        }
        changes.push({id:m.matchId,clear:false,payload:{matchId:m.matchId,results:[
          {seatId:a.seatId,resultMatchPlace:scoreA===m.playTo?1:2,resultPoints:scoreA},
          {seatId:b.seatId,resultMatchPlace:scoreB===m.playTo?1:2,resultPoints:scoreB}
        ]}});
      }
      if(!changes.length){if(!skipRefresh)this.editorMessage='No scores have changed.';return true;}
      this.editorSaving=true;
      try {
        for(const c of changes){
          if(c.clear)await axios.delete('http://localhost:5000/api/match/'+encodeURIComponent(c.id)+'/result',{headers:this.authHeaders()});
          else await axios.post('http://localhost:5000/api/match/result',c.payload,{headers:Object.assign({'Content-Type':'application/json; charset=utf-8'},this.authHeaders())});
        }
        const pool=this.editorPool;
        await this.loadMatches();
        if(pool && !skipRefresh){const fresh=this.stages.flatMap(s=>s.pools).find(p=>p.matchId===pool.matchId);this.poolAdvanceRevision++;if(fresh)await this.openPool(fresh);this.editorMessage='Match results saved. Advancement options refreshed.';}
        return true;
      }catch(e){this.editorError='Could not save all results. Earlier matches may have saved; refresh to check.';return false;}
      finally{this.editorSaving=false;}
    },
    async requestPoolEditorAction(action, poolId, savedSelections) {
      const frame = this.$refs.poolAdvancementFrame;
      if (!frame || !frame.contentWindow) throw new Error('The advancement panel has not loaded.');
      const target = frame.contentWindow;
      const requestId = 'pool-' + Date.now() + '-' + Math.random().toString(36).slice(2);
      return new Promise((resolve, reject) => {
        const cleanup = () => { window.removeEventListener('message', handler); clearTimeout(timer); };
        const handler = event => {
          if (event.source !== target || !event.data || event.data.type !== 'pool-editor-response' || event.data.requestId !== requestId) return;
          cleanup();
          if (event.data.error) reject(new Error(event.data.error));
          else resolve(event.data.result);
        };
        const timer = setTimeout(() => { cleanup(); reject(new Error('The advancement editor did not respond. Reopen the pulje and try again.')); }, 12000);
        window.addEventListener('message', handler);
        target.postMessage({ type:'pool-editor-request', requestId, action, poolId, savedSelections }, '*');
      });
    },
    async saveAllPoolChanges(){
      if (this.editorSaving || this.editorLoading || !this.editorPool) return;
      this.editorError=''; this.editorMessage='';
      const poolId = Number(this.editorPool.matchId);
      let selections;
      try {
        // Snapshot user choices before saving scores: a results refresh can replace the editor state.
        selections = await this.requestPoolEditorAction('snapshot', poolId);
      } catch (e) {
        this.editorError = e.message || 'Cannot access the advancement editor.';
        return;
      }
      if (!(await this.savePoolResults(true))) return;
      try {
        // Saving scores changes qualification status. Refresh that state
        // inside Matches before deciding whether advancement can be saved.
        const updated = await this.requestPoolEditorAction('refresh', poolId);
        if (!updated.stageComplete) {
          this.editorMessage = 'Match scores saved. Finish all matches in this pulje before assigning destination pools or wildcards.';
          return;
        }
        if (!updated.qualified && !updated.eliminated) {
          this.editorError = 'Match scores saved, but no qualifying or eliminated players were returned for this pulje. Advancement was not saved.';
          return;
        }
        const response = await this.requestPoolEditorAction('save', poolId, selections);
        if (!response || !response.savedAdvancement) throw new Error('Advancement was not confirmed saved.');
        await this.loadMatches();
        const fresh = this.stages.flatMap(stage=>stage.pools).find(p=>Number(p.matchId)===poolId);
        this.poolAdvanceRevision++;
        if (fresh) await this.openPool(fresh);
        this.editorMessage = 'All match results, wildcard selections and destination pools saved.';
      } catch (e) {
        this.editorError = 'Match scores were saved, but advancement changes could not all be saved: ' + (e.message || 'unknown error');
      }
    },
    scheduleLines() {
      this.$nextTick(() => {
        if (this._isDestroyed) return;
        requestAnimationFrame(() => this.drawLines());
      });
    },
    drawLines() {
      const board = this.$refs.vizBoard;
      if (!board) {
        this.connections = [];
        return;
      }
      const bounds = board.getBoundingClientRect();
      const nodes = new Map();
      board.querySelectorAll('[data-pool-id]').forEach(el => nodes.set(el.getAttribute('data-pool-id'), el));
      const edges = [];
      for (const route of this.routes || []) {
        const from = nodes.get(String(route.from)), to = nodes.get(String(route.to));
        if (!from || !to || from === to) continue;
        const a = from.getBoundingClientRect(), b = to.getBoundingClientRect();
        if (b.left <= a.left) continue;
        const x1 = a.right - bounds.left, y1 = a.top + a.height / 2 - bounds.top;
        const x2 = b.left - bounds.left, y2 = b.top + b.height / 2 - bounds.top;
        const bend = Math.max(35, (x2 - x1) * 0.48);
        edges.push({ key: route.from + '-' + route.to, d: `M ${x1} ${y1} C ${x1 + bend} ${y1}, ${x2 - bend} ${y2}, ${x2} ${y2}` });
      }
      this.connections = edges;
    },
    async loadRoutes() {
      this.routes = [];
      this.playerDestinations = {};
      this.wildcardStages = {};
      this.routeWarning = '';
      const ids = this.stages.map(s => s.matchId);
      if (ids.length < 2) {
        this.scheduleLines();
        return;
      }
      const requests = ids.slice(1).map(id => axios.get('http://localhost:5000/api/match/' + encodeURIComponent(id) + '/advancement/stage/incoming', { headers: this.authHeaders() }).then(r => r.data).catch(() => null));
      const results = await Promise.all(requests);
      const edges = new Map();
      const destinationMap = {};
      const wildcardMap = {};
      const assign = (from, name, to) => {
        if (!(from > 0 && to > 0 && name)) return;
        const key = String(from);
        if (!destinationMap[key]) destinationMap[key] = {};
        destinationMap[key][this.normalizePlayerName(name)] = to;
      };
      for (let stateIndex = 0; stateIndex < results.length; stateIndex++) {
        const state = results[stateIndex];
        if (!state) continue;
        const nextStageName = this.stages[stateIndex + 1] && this.stages[stateIndex + 1].matchName;
        for (const source of Array.isArray(state.sources) ? state.sources : []) {
          for (const player of Array.isArray(source.players) ? source.players : []) {
            const from = Number(player.sourcePoolMatchId), to = Number(player.currentDestinationPoolMatchId);
            if (from > 0 && to > 0) edges.set(from + '-' + to, { from, to });
            assign(from, player.playerName, to);
          }
          for (const slot of Array.isArray(source.wildcardSlots) ? source.wildcardSlots : []) {
            const selected = (source.wildcardCandidates || []).find(p => Number(p.playerId) === Number(slot.playerId));
            const from = Number(selected && selected.sourcePoolMatchId), to = Number(slot.destinationPoolMatchId);
            if (from > 0 && to > 0) edges.set(from + '-' + to, { from, to });
            assign(from, selected && selected.playerName, to);
            if (from > 0 && selected && selected.playerName && nextStageName) {
              const key = String(from);
              if (!wildcardMap[key]) wildcardMap[key] = {};
              wildcardMap[key][this.normalizePlayerName(selected.playerName)] = nextStageName;
            }
          }
        }
      }
      this.routes = [...edges.values()];
      this.playerDestinations = destinationMap;
      // Source-pulje wildcard seats are authoritative even when an incoming-stage
      // wildcard has not yet been assigned a specific destination pulje.
      // The incoming-stage endpoint can omit those reserved-but-unplaced seats.
      this.stages.forEach((stage, index) => {
        const nextStage = this.stages[index + 1];
        if (!nextStage) return;
        for (const pool of stage.pools) {
          const reserved = (this.poolAdvanceStates[String(pool.matchId)] || {}).reservedWildcardNames || [];
          if (!reserved.length) continue;
          const key = String(pool.matchId);
          if (!wildcardMap[key]) wildcardMap[key] = {};
          for (const name of reserved) wildcardMap[key][name] = nextStage.matchName;
        }
      });
      this.wildcardStages = wildcardMap;
      if (results.every(x => !x)) this.routeWarning = 'Advancement routes could not be loaded.';
      else if (!this.routes.length) this.routeWarning = 'No assigned advancement routes yet. Connections appear when source and destination pools are assigned.';
      this.scheduleLines();
    },
    authHeaders() {
      const token = localStorage.getItem('token');
      return token ? { Authorization: token } : {};
    },
    async loadOptions() {
      this.loading = true;
      this.error = '';
      try {
        const res = await axios.get('http://localhost:5000/api/matches/tournaments', { headers: this.authHeaders() });
        this.tournaments = Array.isArray(res.data && res.data.tournaments) ? res.data.tournaments : [];
        const requested = new URLSearchParams(location.search).get('tournamentId');
        const saved = requested || localStorage.getItem('lastTournamentId');
        if (this.tournaments.some(t => String(t.tournamentId) === String(saved))) this.selectedTournamentId = String(saved);
        else if (this.tournaments.length === 1) this.selectedTournamentId = String(this.tournaments[0].tournamentId);
      } catch (e) {
        this.error = 'Could not load tournaments. Please check that the server is running and you are logged in.';
      } finally {
        this.loading = false;
      }
      if (this.selectedTournamentId) await this.loadMatches();
    },
    async loadTournamentPodium() {
      this.tournamentPodium = null;
      const stages = this.stages;
      const lastStage = stages.length ? stages[stages.length - 1] : null;
      // Do not invent a champion if the last stage has multiple independent puljer.
      if (!lastStage || lastStage.pools.length !== 1) return;
      const finalPool = lastStage.pools[0];
      if (!finalPool.matches || !finalPool.matches.length) return;
      this.podiumLoading = true;
      try {
        const response = await Promise.all(finalPool.matches.map(match =>
          axios.get('http://localhost:5000/api/match/' + encodeURIComponent(match.matchId),
            { headers: this.authHeaders() })));
        const victories = new Map();
        let complete = true;
        for (const item of response) {
          const detail = item.data || {};
          const seats = (Array.isArray(detail.seats) ? detail.seats : [])
            .filter(seat => seat.playerId !== null && seat.playerId !== undefined);
          const rules = detail.matchRules || {};
          const playTo = rules.playTo == null ? null : Number(rules.playTo);
          if (seats.length !== 2 || !Number.isFinite(playTo) ||
            seats.some(seat => seat.resultPoints === null || seat.resultPoints === undefined)) {
            complete = false;
            break;
          }
          const winnerSeats = seats.filter(seat => Number(seat.resultPoints) === playTo);
          if (winnerSeats.length !== 1) { complete = false; break; }
          for (const seat of seats) {
            const key = String(seat.playerId);
            if (!victories.has(key)) victories.set(key, {
              playerId: seat.playerId, playerName: seat.playerName || ('Player #' + seat.playerId), wins: 0
            });
          }
          victories.get(String(winnerSeats[0].playerId)).wins++;
        }
        if (!complete || victories.size !== 2) return;
        const ranked = [...victories.values()].sort((a, b) => b.wins - a.wins);
        if (ranked[0].wins === ranked[1].wins) return;
        this.tournamentPodium = { winner: ranked[0], runnerUp: ranked[1], stageName: lastStage.matchName };
      } catch (e) {
        // Missing or unfinished final results should not display a guessed winner.
        this.tournamentPodium = null;
      } finally {
        this.podiumLoading = false;
      }
    },
    async loadPoolScheduleDetails() {
      const selected = String(this.selectedTournamentId);
      const ids = this.stages.flatMap(stage => stage.pools.map(pool => Number(pool.matchId)));
      const details = {};
      // Read the same pulje details endpoint used by Matches.
      // Use limited parallelism to avoid flooding the API on large tournaments.
      let next = 0;
      const workers = Array.from({length: Math.min(6, ids.length)}, async () => {
        while (next < ids.length) {
          const id = ids[next++];
          try {
            const response = await axios.get('http://localhost:5000/api/match/' + encodeURIComponent(id), {headers:this.authHeaders()});
            const value = response.data || {};
            const venue = value.venue || {};
            const schedule = value.schedule || {};
            details[String(id)] = {
              venueName: venue.facilityName || venue.venueName || venue.name || value.venueName || '',
              fromTime: schedule.fromTime || value.fromTime || ''
            };
          } catch (e) {
            details[String(id)] = {venueName:'',fromTime:''};
          }
        }
      });
      await Promise.all(workers);
      if (selected === String(this.selectedTournamentId)) this.poolScheduleDetails = details;
    },
    poolSchedule(pool) {
      return (pool && this.poolScheduleDetails[String(pool.matchId)]) || {};
    },
    poolStartTime(pool) {
      const value = this.poolSchedule(pool).fromTime;
      if (!value) return 'Start time not set';
      const text = String(value);
      const time = /T(\d{2}:\d{2})/.exec(text);
      const date = /^(\d{4})-(\d{2})-(\d{2})/.exec(text);
      const dateText = date && date[1] !== '2000' ? date[3]+'/'+date[2]+'/'+date[1] : '';
      return [dateText, time ? 'Starts '+time[1] : ''].filter(Boolean).join(' · ') || text;
    },
    async loadMatches() {
      if (!this.selectedTournamentId) {
        this.rows = [];
        this.renameablePlayers = [];
        this.finderPlayers = [];
        this.poolAdvanceStates = {};
        this.poolScheduleDetails = {};
        this.tournamentPodium = null;
        return;
      }
      this.loading = true;
      this.error = '';
      try {
        const res = await axios.post('http://localhost:5000/api/matches', Number(this.selectedTournamentId), {
          headers: Object.assign({ 'Content-Type': 'application/json; charset=utf-8' }, this.authHeaders())
        });
        let data = res.data && res.data.matches !== undefined ? res.data.matches : res.data;
        if (typeof data === 'string') data = JSON.parse(data);
        if (!Array.isArray(data)) throw new Error('Unexpected tournament response');
        this.rows = data;
        await this.loadRenameablePlayers();
        await this.loadFinderPlayers();
        await this.loadPoolAdvanceStates();
        await this.loadPoolScheduleDetails();
        await this.loadTournamentPodium();
        localStorage.setItem('lastTournamentId', this.selectedTournamentId);
        await this.loadRoutes();
      } catch (e) {
        this.rows = [];
        this.routes = [];
        this.connections = [];
        this.poolAdvanceStates = {};
        this.poolScheduleDetails = {};
        this.tournamentPodium = null;
        this.error = 'Could not load the tournament visualization: ' + ((e.response && e.response.status === 401) ? 'please log in again.' : e.message);
      } finally {
        this.loading = false;
      }
    },
    refresh() {
      return this.loadMatches();
    }
  },
  watch: {
    stages() {
      this.scheduleLines();
    }
  },
  mounted() {
    this.routes = [];
    this._onVizResize = () => this.scheduleLines();
    window.addEventListener('resize', this._onVizResize);
    this.loadOptions();
  },
  beforeDestroy() {
    window.removeEventListener('resize', this._onVizResize);
  }
});
