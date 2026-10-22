/* Read-only tournament visualization. Shares the existing Matches API contract. */
new Vue({
  el: '#visualization',
  data: { tournaments: [], selectedTournamentId: '', rows: [], loading: false, error: '', routes: [], connections: [], routeWarning: '', editorStage: null, stageEditorUrl: '', editorPool: null, poolAdvanceUrl: '', poolAdvanceRevision: 0, editorMatches: [], editorLoading: false, editorSaving: false, editorError: '', editorMessage: '', playerSearch: '', playerSearchOpen: false },
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
    },
    searchablePlayers() {
      const map = new Map();
      for (const stage of this.stages) for (const pool of stage.pools) {
        for (const name of pool.players) {
          const normalized = String(name || '').trim();
          if (!normalized) continue;
          const key = normalized.toLocaleLowerCase();
          if (!map.has(key)) map.set(key, {name:normalized, pools:[]});
          const item = map.get(key);
          item.pools.push({stageName:stage.matchName, poolName:pool.matchName,
            poolId:pool.matchId, pool, matches:pool.matches.filter(m => m.players.includes(normalized))});
        }
      }
      return [...map.values()].sort((a,b)=>a.name.localeCompare(b.name, undefined, {numeric:true}));
    },
    matchingPlayers() {
      const query = this.playerSearch.trim().toLocaleLowerCase();
      if (!query) return [];
      return this.searchablePlayers.filter(p=>p.name.toLocaleLowerCase().includes(query)).slice(0,40);
    },
    editorPlayers() {
      const names = new Map();
      for (const m of this.editorMatches) for (const seat of m.seats) {
        const key = seat.playerId != null ? 'id:'+seat.playerId : 'name:'+seat.playerName;
        if (!names.has(key)) names.set(key, {key, playerName:seat.playerName, playerId:seat.playerId});
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
      const stats = this.editorPlayers.map(p => ({key:p.key, points:0, score:0, played:0, place:'–'}));
      const byKey = new Map(stats.map(x => [x.key,x]));
      for (const m of this.editorMatches) {
        if (m.seats.length !== 2 || !Number.isFinite(m.playTo)) continue;
        const [a,b] = m.seats;
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
      const order = [...stats].sort((a,b) => b.points - a.points || (descending ? b.score-a.score : a.score-b.score));
      order.forEach((x,i) => {x.place = i+1;});
      return Object.fromEntries(stats.map(x => [x.key,x]));
    },
  },
  methods: {
    selectPlayerPool(pool) {
      this.playerSearchOpen = false;
      this.openPool(pool);
    },
    onSearchEscape() { this.playerSearch=''; this.playerSearchOpen=false; },
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
    seatKey(seat) { return seat.playerId != null ? 'id:'+seat.playerId : 'name:'+seat.playerName; },
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
    matrixCellClass(row,col) {
      const match = this.matrixMatch(row,col);
      return match ? this.matchCellClass(match,this.matrixSeat(row,col)) : '';
    },
    setMatchScore(match,seat,value) {
      if (!seat || !match) return;
      seat.score = value;
      const opponent = match.seats.find(s => s !== seat);
      if (!opponent || value === '' || !Number.isFinite(match.playTo)) return;
      const entered = Number(value);
      if (!Number.isInteger(entered) || entered === match.playTo) return;
      if (match.minScore !== null && (entered < match.minScore || entered > match.maxScore)) return;
      // Typing the losing score supplies the winning PlayTo score automatically.
      // The opponent's input remains editable afterward.
      opponent.score = String(match.playTo);
    },
    seriesChange(player,match,value) {
      this.setMatchScore(match, this.seriesSeat(player,match), value);
    },
    matrixMatch(row,col) {
      if(row.key===col.key)return null;
      return this.editorMatches.find(m=>m.seats.length===2 &&
        m.seats.some(s=>(s.playerId!=null?'id:'+s.playerId:'name:'+s.playerName)===row.key)&&
        m.seats.some(s=>(s.playerId!=null?'id:'+s.playerId:'name:'+s.playerName)===col.key)) || null;
    },
    matrixSeat(row,col) {
      const match=this.matrixMatch(row,col);
      return match && match.seats.find(s=>(s.playerId!=null?'id:'+s.playerId:'name:'+s.playerName)===row.key);
    },
    matrixChange(row,col,value){const match=this.matrixMatch(row,col);this.setMatchScore(match,this.matrixSeat(row,col),value);},

    async openPool(pool) {
      this.editorPool=pool;
      this.poolAdvanceUrl='matches.html?embeddedPool=1&tournamentId='+encodeURIComponent(this.selectedTournamentId)+'&poolId='+encodeURIComponent(pool.matchId)+'&refresh='+this.poolAdvanceRevision;
      this.editorMatches=[];
      this.editorError='';this.editorMessage='';this.editorLoading=true;
      try {
        const response=await Promise.all(pool.matches.map(m=>axios.get('http://localhost:5000/api/match/'+encodeURIComponent(m.matchId),{headers:this.authHeaders()})));
        this.editorMatches=response.map((res,i)=>{
          const details=res.data || {};
          const rules=details.matchRules || {};
          const playTo=rules.playTo===null || rules.playTo===undefined ? null : Number(rules.playTo);
          const playFrom=rules.playFrom===null || rules.playFrom===undefined ? null : Number(rules.playFrom);
          return {matchId:pool.matches[i].matchId,matchName:details.matchName||pool.matches[i].matchName,
            playTo:Number.isFinite(playTo)?playTo:null, playFrom:Number.isFinite(playFrom)?playFrom:null,
            minScore:Number.isFinite(playFrom)&&Number.isFinite(playTo)?Math.min(playFrom,playTo):null,
            maxScore:Number.isFinite(playFrom)&&Number.isFinite(playTo)?Math.max(playFrom,playTo):null,
            seats:(Array.isArray(details.seats)?details.seats:[]).filter(x=>x.playerId!==null&&x.playerId!==undefined).map(x=>({
              seatId:x.seatId,playerId:x.playerId,playerName:x.playerName||('Player #'+x.playerId),score:x.resultPoints===null||x.resultPoints===undefined?'':String(x.resultPoints),
              original:x.resultPoints===null||x.resultPoints===undefined?'':String(x.resultPoints),originalPlace:x.resultMatchPlace
            }))};
        });
      } catch(e){this.editorError='Could not load match details. Check login and server connection.';}
      finally {this.editorLoading=false;}
    },
    closePool(){if(this.editorSaving)return;this.editorPool=null;this.poolAdvanceUrl='';this.editorMatches=[];this.editorError='';this.editorMessage='';},
    async savePoolResults(){
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
      if(!changes.length){this.editorMessage='No scores have changed.';return;}
      this.editorSaving=true;
      try {
        for(const c of changes){
          if(c.clear)await axios.delete('http://localhost:5000/api/match/'+encodeURIComponent(c.id)+'/result',{headers:this.authHeaders()});
          else await axios.post('http://localhost:5000/api/match/result',c.payload,{headers:Object.assign({'Content-Type':'application/json; charset=utf-8'},this.authHeaders())});
        }
        const pool=this.editorPool;
        await this.loadMatches();
        if(pool){const fresh=this.stages.flatMap(s=>s.pools).find(p=>p.matchId===pool.matchId);this.poolAdvanceRevision++;if(fresh)await this.openPool(fresh);this.editorMessage='Match results saved. Advancement options refreshed.';}
      }catch(e){this.editorError='Could not save all results. Earlier matches may have saved; refresh to check.';}
      finally{this.editorSaving=false;}
    },
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
