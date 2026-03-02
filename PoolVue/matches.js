/* Matches page (Vue 2) - Tree view */

Vue.component('tree-node', {
  props: ['node', 'open', 'query'],
  methods: {
    isOpen() {
      return this.open.has(this.node.matchId);
    },
    toggle() {
      const id = this.node.matchId;
      if (this.open.has(id)) this.open.delete(id);
      else this.open.add(id);

      // Vue2: Set isn't reactive => replace with a new Set
      this.$emit('update:open', new Set(this.open));
    },
    matchesQuery() {
      const q = (this.query || '').trim().toLowerCase();
      if (!q) return true;

      const hay = (this.node.matchName + ' ' + this.node.players.join(' ')).toLowerCase();
      if (hay.includes(q)) return true;

      // Keep ancestors visible when any descendant matches
      return this.node.children.some(ch => this.childMatchesQuery(ch, q));
    },
    childMatchesQuery(n, q){
      const hay = (n.matchName + ' ' + n.players.join(' ')).toLowerCase();
      if (hay.includes(q)) return true;
      return n.children.some(c => this.childMatchesQuery(c, q));
    }
  },
  computed: {
    visible() { return this.matchesQuery(); },
    shouldHighlight() {
      const q = (this.query || '').trim().toLowerCase();
      if (!q) return false;
      const hay = (this.node.matchName + ' ' + this.node.players.join(' ')).toLowerCase();
      return hay.includes(q);
    },
    hasChildren() { return this.node.children && this.node.children.length > 0; }
  },
  template: `
    <div v-if="visible" class="node" :class="{highlight: shouldHighlight}">
      <div class="nodeHead" @click="toggle">
        <div class="leftRow">
          <span class="chev" :class="{open: isOpen()}">
            {{ hasChildren ? '▶' : '•' }}
          </span>

          <div class="titleBlock">
            <div class="nameRow">
              <div class="nodeName">{{ node.matchName || '(no name)' }}</div>
              <div class="badges">
                <span class="badge">#{{ node.matchId }}</span>
                <span class="badge soft">level {{ node.level }}</span>
              </div>
            </div>

            <div class="badges">
              <span class="badge soft">parent {{ node.parentMatchId === null ? 'null' : node.parentMatchId }}</span>
              <span class="badge soft">{{ node.players.length }} player(s)</span>
              <span class="badge soft">{{ node.children.length }} child(ren)</span>
            </div>
          </div>
        </div>
      </div>

      <div class="nodeBody" v-show="isOpen()">
        <div class="chips">
          <span v-if="node.players.length === 0" class="chip empty">No players yet</span>
          <span v-for="(p, idx) in node.players" :key="node.matchId + '-' + idx" class="chip">{{ p }}</span>
        </div>

        <div class="children" v-if="hasChildren">
          <tree-node
            v-for="c in node.children"
            :key="c.matchId"
            :node="c"
            :open.sync="open"
            :query="query"
          ></tree-node>
        </div>
      </div>
    </div>
  `
});

const matchesApp = new Vue({
  el: '#vuematch',
  data: {
    searchText: '',
    open: new Set(),
    rows: [] // raw API rows
  },

  computed: {
    nodes() {
      const map = new Map();
      for (const r of this.rows) {
        const id = r.MatchId;
        if (!map.has(id)) {
          map.set(id, {
            matchId: id,
            parentMatchId: (r.ParentMatchId === undefined ? null : r.ParentMatchId),
            matchName: r.MatchName,
            level: (typeof r.level === 'number' ? r.level : 0),
            players: [],
            children: []
          });
        }
        const n = map.get(id);
        if (!n.matchName && r.MatchName) n.matchName = r.MatchName;
        if (typeof r.level === 'number') n.level = Math.min(n.level, r.level);
        if (n.parentMatchId == null && r.ParentMatchId != null) n.parentMatchId = r.ParentMatchId;
        if (r.PlayerName) n.players.push(r.PlayerName);
      }
      return [...map.values()].sort((a,b) => a.matchId - b.matchId);
    },

    roots() {
      const map = new Map(this.nodes.map(n => [n.matchId, n]));
      for (const n of map.values()) n.children = [];

      const roots = [];
      for (const n of map.values()) {
        const pid = n.parentMatchId;
        if (pid == null || !map.has(pid)) roots.push(n);
        else map.get(pid).children.push(n);
      }

      const sortRec = (arr) => {
        arr.sort((a,b) => (a.level - b.level) || (a.matchId - b.matchId));
        for (const x of arr) sortRec(x.children);
      };
      sortRec(roots);
      return roots;
    },

    visibleCount() {
      const q = (this.searchText || '').trim().toLowerCase();
      if (!q) return this.nodes.length;

      const matchNode = (n) => {
        const hay = (n.matchName + ' ' + n.players.join(' ')).toLowerCase();
        if (hay.includes(q)) return true;
        return n.children.some(matchNode);
      };

      let count = 0;
      const walk = (n) => {
        if (matchNode(n)) count++;
        for (const c of n.children) walk(c);
      };
      for (const r of this.roots) walk(r);
      return count;
    }
  },

  methods: {
    expandAll() { this.open = new Set(this.nodes.map(n => n.matchId)); },
    collapseAll() { this.open = new Set(); },

    hookSearchBox() {
      const el = document.getElementById('f-search');
      if (!el) return;
      el.addEventListener('input', (e) => {
        this.searchText = e.target.value || '';
        if (this.searchText.trim()) this.expandAll();
      });
    },

    async loadMatches(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid)) {
        alert('Please enter a valid TournamentId');
        return;
      }

      const mytoken = localStorage.getItem('token');
      if (mytoken) axios.defaults.headers.common['Authorization'] = mytoken;

      try {
        // Your existing API expects a POST to /api/matches with a JSON string
        // This mirrors your current index.js behavior.
        const json = JSON.stringify(String(tid));
        const res = await axios.post(
          "http://localhost:5000/api/matches",
          tid,
          { headers: { 'Content-type': 'application/json; charset=utf-8' } }
        );

        let matches = res.data?.matches ?? res.data;

		// Your backend returns matches as a STRING -> parse it
		if (typeof matches === "string") {
		  matches = JSON.parse(matches);
		}

		if (!Array.isArray(matches)) {
		  console.log("Unexpected response:", res.data);
		  alert("Unexpected response (matches is not an array). Check console.");
		  return;
		}

		// Use the parsed array
		this.rows = matches;
		
        this.expandAll();
        localStorage.setItem('lastTournamentId', String(tid));
      } catch (e) {
        console.error(e);
        alert(e);
      }
    }
  },

  mounted() {
    this.hookSearchBox();

    // Wire up topbar controls
    const tidEl = document.getElementById('tournamentId');
    const btn = document.getElementById('loadBtn');

    const last = localStorage.getItem('lastTournamentId');
    if (tidEl && last) tidEl.value = last;

    const load = () => this.loadMatches(tidEl ? tidEl.value : 0);

    if (btn) btn.addEventListener('click', load);
    if (tidEl) tidEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') load();
    });

    // Auto-load if we have last value
    if (last) this.loadMatches(last);
  }
});
