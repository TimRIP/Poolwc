/* Matches page (Vue 2) - Tree view + result editor */

Vue.component('tree-node', {
  props: ['node', 'open', 'query', 'selectedMatchId'],
  methods: {
    isOpen() {
      return this.open.has(this.node.matchId);
    },
    toggle() {
      const next = new Set(this.open);
      const id = this.node.matchId;
      if (next.has(id)) next.delete(id);
      else next.add(id);
      this.$emit('update:open', next);
    },
    selectNode() {
      this.$emit('select-match', this.node);
      if (this.hasChildren) this.toggle();
    },
    matchesQuery() {
      const q = (this.query || '').trim().toLowerCase();
      if (!q) return true;

      const hay = ((this.node.matchName || '') + ' ' + this.node.players.join(' ')).toLowerCase();
      if (hay.includes(q)) return true;

      return this.node.children.some(ch => this.childMatchesQuery(ch, q));
    },
    childMatchesQuery(n, q) {
      const hay = ((n.matchName || '') + ' ' + n.players.join(' ')).toLowerCase();
      if (hay.includes(q)) return true;
      return n.children.some(c => this.childMatchesQuery(c, q));
    }
  },
  computed: {
    visible() { return this.matchesQuery(); },
    shouldHighlight() {
      const q = (this.query || '').trim().toLowerCase();
      if (!q) return false;
      const hay = ((this.node.matchName || '') + ' ' + this.node.players.join(' ')).toLowerCase();
      return hay.includes(q);
    },
    hasChildren() { return this.node.children && this.node.children.length > 0; },
    isSelected() { return Number(this.selectedMatchId) === Number(this.node.matchId); }
  },
  template: `
    <div v-if="visible" class="node" :class="{highlight: shouldHighlight, selectedMatchNode: isSelected}">
      <div class="nodeHead" @click="selectNode">
        <div class="leftRow">
          <button v-if="hasChildren"
                  type="button"
                  class="treeToggleButton"
                  :aria-label="isOpen() ? 'Collapse match' : 'Expand match'"
                  @click.stop="toggle">
            <span class="chev" :class="{open: isOpen()}">▶</span>
          </button>
          <span v-else class="chev leafDot">•</span>

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
              <span class="badge editBadge">click to edit result</span>
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
            :open="open"
            :query="query"
            :selected-match-id="selectedMatchId"
            @update:open="$emit('update:open', $event)"
            @select-match="$emit('select-match', $event)"
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
    rows: [],
    selectedMatchId: null,
    matchDetails: null,
    loadingDetails: false,
    savingResult: false,
    resultError: '',
    resultMessage: ''
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
        if (r.PlayerName && !n.players.includes(r.PlayerName)) n.players.push(r.PlayerName);
      }
      return [...map.values()].sort((a, b) => a.matchId - b.matchId);
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
        arr.sort((a, b) => (a.level - b.level) || (a.matchId - b.matchId));
        for (const x of arr) sortRec(x.children);
      };
      sortRec(roots);
      return roots;
    },

    visibleCount() {
      const q = (this.searchText || '').trim().toLowerCase();
      if (!q) return this.nodes.length;

      const matchNode = (n) => {
        const hay = ((n.matchName || '') + ' ' + n.players.join(' ')).toLowerCase();
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
    },

    activeSeats() {
      if (!this.matchDetails || !Array.isArray(this.matchDetails.seats)) return [];
      return this.matchDetails.seats.filter(s => s.playerId !== null && s.playerId !== undefined);
    }
  },

  methods: {
    expandAll() {
      this.open = new Set(this.nodes.map(n => n.matchId));
    },

    collapseAll() {
      this.open = new Set();
    },

    authHeaders() {
      const token = localStorage.getItem('token');
      return token ? { Authorization: token } : {};
    },

    displayValue(value) {
      return value === null || value === undefined ? 'Not set' : value;
    },

    hookSearchBox() {
      const el = document.getElementById('f-search');
      if (!el) return;
      el.addEventListener('input', (e) => {
        this.searchText = e.target.value || '';
        if (this.searchText.trim()) this.expandAll();
      });
    },

    async selectMatch(node) {
      if (!node || !node.matchId) return;
      this.selectedMatchId = node.matchId;
      await this.loadMatchDetails(node.matchId);
    },

    normalizeMatchDetails(data) {
      const details = data || {};
      const seats = Array.isArray(details.seats) ? details.seats : [];

      return {
        matchId: details.matchId,
        matchName: details.matchName || '(no name)',
        individualMatch: !!details.individualMatch,
        matchRules: details.matchRules || null,
        seats: seats.map(s => ({
          seatId: s.seatId,
          playerId: s.playerId === undefined ? null : s.playerId,
          playerName: s.playerName || null,
          resultMatchPlace: s.resultMatchPlace === undefined ? null : s.resultMatchPlace,
          resultPoints: s.resultPoints === undefined ? null : s.resultPoints
        }))
      };
    },

    async loadMatchDetails(matchId) {
      this.loadingDetails = true;
      this.resultError = '';
      this.resultMessage = '';
      this.matchDetails = null;

      try {
        const res = await axios.get(
          'http://localhost:5000/api/match/' + encodeURIComponent(matchId),
          { headers: this.authHeaders() }
        );
        this.matchDetails = this.normalizeMatchDetails(res.data);
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.resultError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.status === 404) {
          this.resultError = 'The match was not found.';
        } else {
          this.resultError = 'Could not load match details.';
        }
      } finally {
        this.loadingDetails = false;
      }
    },

    setWinner(seat) {
      if (!seat || this.activeSeats.length !== 2) return;
      for (const current of this.activeSeats) {
        current.resultMatchPlace = current.seatId === seat.seatId ? 1 : 2;
      }
      this.resultError = '';
      this.resultMessage = '';
    },

    setLoser(seat) {
      if (!seat || this.activeSeats.length !== 2) return;
      for (const current of this.activeSeats) {
        current.resultMatchPlace = current.seatId === seat.seatId ? 2 : 1;
      }
      this.resultError = '';
      this.resultMessage = '';
    },

    async clearResult() {
      if (!this.matchDetails || !this.matchDetails.matchId) return;

      this.savingResult = true;
      this.resultError = '';
      this.resultMessage = '';

      try {
        await axios.delete(
          'http://localhost:5000/api/match/' + encodeURIComponent(this.matchDetails.matchId) + '/result',
          { headers: this.authHeaders() }
        );

        for (const seat of this.matchDetails.seats) {
          seat.resultMatchPlace = null;
          seat.resultPoints = null;
        }
        this.resultMessage = 'Result cleared.';
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.resultError = 'You are not logged in, or your login has expired.';
        } else {
          this.resultError = 'Could not clear the result.';
        }
      } finally {
        this.savingResult = false;
      }
    },

    resultRowClass(seat) {
      return {
        winnerRow: Number(seat.resultMatchPlace) === 1,
        loserRow: this.activeSeats.length === 2 && Number(seat.resultMatchPlace) === 2
      };
    },

    validateResult() {
      if (!this.matchDetails || !this.matchDetails.individualMatch) {
        return 'Select a playable match first.';
      }

      if (this.activeSeats.length < 2) {
        return 'The match needs at least two assigned players before a result can be saved.';
      }

      const places = this.activeSeats.map(s => Number(s.resultMatchPlace));
      if (places.some(p => !Number.isInteger(p) || p < 1 || p > this.activeSeats.length)) {
        return 'Choose a finishing place for every player.';
      }

      if (new Set(places).size !== places.length) {
        return 'Each player must have a different finishing place.';
      }

      if (!places.includes(1)) {
        return 'A winner must be selected.';
      }

      return '';
    },

    async saveResult() {
      const validation = this.validateResult();
      if (validation) {
        this.resultError = validation;
        this.resultMessage = '';
        return;
      }

      this.savingResult = true;
      this.resultError = '';
      this.resultMessage = '';

      const payload = {
        matchId: this.matchDetails.matchId,
        results: this.activeSeats.map(seat => ({
          seatId: seat.seatId,
          resultMatchPlace: Number(seat.resultMatchPlace),
          resultPoints: seat.resultPoints === '' || seat.resultPoints === null || seat.resultPoints === undefined
            ? null
            : Number(seat.resultPoints)
        }))
      };

      try {
        await axios.post(
          'http://localhost:5000/api/match/result',
          payload,
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        this.resultMessage = 'Result saved.';
        await this.loadMatchDetails(this.matchDetails.matchId);
        this.resultMessage = 'Result saved.';
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.resultError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.resultError = e.response.data.message;
        } else {
          this.resultError = 'Could not save the result.';
        }
      } finally {
        this.savingResult = false;
      }
    },

    async loadMatches(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid)) {
        alert('Please enter a valid TournamentId');
        return;
      }

      try {
        const res = await axios.post(
          'http://localhost:5000/api/matches',
          tid,
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        let matches = res.data && res.data.matches !== undefined ? res.data.matches : res.data;
        if (typeof matches === 'string') matches = JSON.parse(matches);

        if (!Array.isArray(matches)) {
          console.log('Unexpected response:', res.data);
          alert('Unexpected response (matches is not an array). Check console.');
          return;
        }

        this.rows = matches;
        this.selectedMatchId = null;
        this.matchDetails = null;
        this.resultError = '';
        this.resultMessage = '';
        this.expandAll();
        localStorage.setItem('lastTournamentId', String(tid));
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          alert('Unauthorized. Please log in again.');
        } else {
          alert(e);
        }
      }
    }
  },

  mounted() {
    this.hookSearchBox();

    const tidEl = document.getElementById('tournamentId');
    const btn = document.getElementById('loadBtn');
    const last = localStorage.getItem('lastTournamentId');

    if (tidEl && last) tidEl.value = last;

    const load = () => this.loadMatches(tidEl ? tidEl.value : 0);

    if (btn) btn.addEventListener('click', load);
    if (tidEl) tidEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') load();
    });

    if (last) this.loadMatches(last);
  }
});
