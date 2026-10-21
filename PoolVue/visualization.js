/* Read-only tournament visualization. Shares the existing Matches API contract. */
new Vue({
  el: '#visualization',
  data: { tournaments: [], selectedTournamentId: '', rows: [], loading: false, error: '', routes: [], connections: [], routeWarning: '' },
  computed: {
    stages() {
      const nodes = new Map();
      for (const r of this.rows) {
        const id = Number(r.MatchId);
        if (!Number.isFinite(id)) continue;
        if (!nodes.has(id)) nodes.set(id, {
          matchId: id, parentMatchId: r.ParentMatchId == null ? null : Number(r.ParentMatchId),
          matchName: r.MatchName || ('Match ' + id), players: [], children: []
        });
        const n = nodes.get(id);
        if (r.PlayerName && !n.players.includes(r.PlayerName)) n.players.push(r.PlayerName);
      }
      const roots = [];
      for (const node of nodes.values()) {
        if (node.parentMatchId != null && nodes.has(node.parentMatchId) && node.parentMatchId !== node.matchId)
          nodes.get(node.parentMatchId).children.push(node);
        else roots.push(node);
      }
      const sort = arr => { arr.sort((a,b) => a.matchId-b.matchId); arr.forEach(n => sort(n.children)); };
      sort(roots);
      // A stage can be nested beneath the previous stage in the match tree.
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
          const matches = node.children.map(m => ({matchId:m.matchId,matchName:m.matchName,players:m.players}));
          return {matchId:node.matchId, matchName:node.matchName, matches,
            players:[...new Set([...node.players,...matches.flatMap(m=>m.players)])]};
        });
        return {matchId:stage.matchId, matchName:stage.matchName, pools};
      });
    }
  },
  methods: {
    scheduleLines() {
      this.$nextTick(() => { if (this._isDestroyed) return; requestAnimationFrame(() => this.drawLines()); });
    },
    drawLines() {
      const board = this.$refs.vizBoard;
      if (!board) { this.connections = []; return; }
      const bounds = board.getBoundingClientRect();
      const nodes = new Map();
      board.querySelectorAll('[data-pool-id]').forEach(el => nodes.set(el.getAttribute('data-pool-id'), el));
      const edges = [];
      for (const route of this.routes || []) {
        const from = nodes.get(String(route.from)), to = nodes.get(String(route.to));
        if (!from || !to || from === to) continue;
        const a = from.getBoundingClientRect(), b = to.getBoundingClientRect();
        if (b.left <= a.left) continue;
        const x1 = a.right-bounds.left, y1 = a.top+a.height/2-bounds.top;
        const x2 = b.left-bounds.left, y2 = b.top+b.height/2-bounds.top;
        const bend = Math.max(35,(x2-x1)*0.48);
        edges.push({key:route.from+'-'+route.to, d:`M ${x1} ${y1} C ${x1+bend} ${y1}, ${x2-bend} ${y2}, ${x2} ${y2}`});
      }
      this.connections = edges;
    },
    async loadRoutes() {
      this.routes = []; this.routeWarning = '';
      const ids = this.stages.map(s => s.matchId);
      if (ids.length < 2) { this.scheduleLines(); return; }
      const requests = ids.slice(1).map(id => axios.get('http://localhost:5000/api/match/' + encodeURIComponent(id) + '/advancement/stage/incoming', {headers:this.authHeaders()}).then(r=>r.data).catch(()=>null));
      const results = await Promise.all(requests);
      const edges = new Map();
      for (const state of results) {
        if (!state) continue;
        for (const source of Array.isArray(state.sources) ? state.sources : []) {
          for (const player of Array.isArray(source.players) ? source.players : []) {
            const from = Number(player.sourcePoolMatchId), to = Number(player.currentDestinationPoolMatchId);
            if (from > 0 && to > 0) edges.set(from+'-'+to,{from,to});
          }
          for (const slot of Array.isArray(source.wildcardSlots) ? source.wildcardSlots : []) {
            const selected = (source.wildcardCandidates || []).find(p => Number(p.playerId) === Number(slot.playerId));
            const from = Number(selected && selected.sourcePoolMatchId), to = Number(slot.destinationPoolMatchId);
            if (from > 0 && to > 0) edges.set(from+'-'+to,{from,to});
          }
        }
      }
      this.routes = [...edges.values()];
      if (results.every(x=>!x)) this.routeWarning = 'Advancement routes could not be loaded.';
      else if (!this.routes.length) this.routeWarning = 'No assigned advancement routes yet. Connections appear when source and destination pools are assigned.';
      this.scheduleLines();
    },
    authHeaders() { const token=localStorage.getItem('token'); return token ? {Authorization:token} : {}; },
    async loadOptions() {
      this.loading=true; this.error='';
      try {
        const res=await axios.get('http://localhost:5000/api/matches/tournaments',{headers:this.authHeaders()});
        this.tournaments=Array.isArray(res.data && res.data.tournaments) ? res.data.tournaments : [];
        const requested=new URLSearchParams(location.search).get('tournamentId');
        const saved=requested || localStorage.getItem('lastTournamentId');
        if (this.tournaments.some(t=>String(t.tournamentId)===String(saved))) this.selectedTournamentId=String(saved);
        else if(this.tournaments.length===1) this.selectedTournamentId=String(this.tournaments[0].tournamentId);
      } catch(e) { this.error='Could not load tournaments. Please check that the server is running and you are logged in.'; }
      finally {this.loading=false;}
      if(this.selectedTournamentId) await this.loadMatches();
    },
    async loadMatches() {
      if(!this.selectedTournamentId) {this.rows=[];return;}
      this.loading=true;this.error='';
      try {
        const res=await axios.post('http://localhost:5000/api/matches',Number(this.selectedTournamentId),
          {headers:Object.assign({'Content-Type':'application/json; charset=utf-8'},this.authHeaders())});
        let data=res.data && res.data.matches !== undefined ? res.data.matches : res.data;
        if(typeof data==='string') data=JSON.parse(data);
        if(!Array.isArray(data)) throw new Error('Unexpected tournament response');
        this.rows=data; localStorage.setItem('lastTournamentId',this.selectedTournamentId); await this.loadRoutes();
      } catch(e) {this.rows=[];this.routes=[];this.connections=[];this.error='Could not load the tournament visualization: '+(e.response && e.response.status===401?'please log in again.':e.message);}
      finally {this.loading=false;}
    },
    refresh() {return this.loadMatches();}
  },
  watch: { stages() { this.scheduleLines(); } },
  mounted() {
    this.routes=[];
    this._onVizResize=()=>this.scheduleLines();
    window.addEventListener('resize',this._onVizResize);
    this.loadOptions();
  },
  beforeDestroy() { window.removeEventListener('resize',this._onVizResize); }
});
