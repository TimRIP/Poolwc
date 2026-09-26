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
    nodeSearchText(n) {
      const schedule = n && n.schedule ? n.schedule : {};
      return [
        n && n.matchName ? n.matchName : '',
        n && Array.isArray(n.players) ? n.players.join(' ') : '',
        schedule.venueName || '',
        schedule.fromTime || ''
      ].join(' ').toLowerCase();
    },
    matchesQuery() {
      const q = (this.query || '').trim().toLowerCase();
      if (!q) return true;

      if (this.nodeSearchText(this.node).includes(q)) return true;
      return this.node.children.some(ch => this.childMatchesQuery(ch, q));
    },
    childMatchesQuery(n, q) {
      if (this.nodeSearchText(n).includes(q)) return true;
      return n.children.some(c => this.childMatchesQuery(c, q));
    },
    formatScheduleTime(value) {
      if (!value) return '';
      const raw = String(value);
      const iso = raw.match(/T(\d{2}):(\d{2})/);
      if (iso) return iso[1] + ':' + iso[2];
      const plain = raw.match(/^(\d{1,2}):(\d{2})/);
      if (plain) return String(plain[1]).padStart(2, '0') + ':' + plain[2];
      return '';
    },
    scheduleRange(schedule) {
      if (!schedule) return '';
      const from = this.formatScheduleTime(schedule.fromTime);
      return from ? 'Starts ' + from : '';
    }
  },
  computed: {
    visible() { return this.matchesQuery(); },
    shouldHighlight() {
      const q = (this.query || '').trim().toLowerCase();
      if (!q) return false;
      return this.nodeSearchText(this.node).includes(q);
    },
    hasChildren() { return this.node.children && this.node.children.length > 0; },
    isPoolNode() {
      // A pool can occur at different depths (for example group-stage pools
      // at level 2 and semi-final pools at level 3). Identify it by
      // structure instead of by an absolute tree level: a pool is a
      // grouping node whose direct children are playable leaf matches.
      return this.hasChildren && this.node.children.every(child =>
        !child.children || child.children.length === 0
      );
    },
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

            <div class="poolListSchedule" v-if="isPoolNode">
              <span class="poolVenueBadge" :class="{ missing: !(node.schedule && node.schedule.venueName) }">
                <i class="fa fa-map-marker"></i>
                {{ node.schedule && node.schedule.venueName ? node.schedule.venueName : 'Venue not set' }}
              </span>
              <span class="poolTimeBadge" v-if="node.schedule && scheduleRange(node.schedule)">
                <i class="fa fa-clock-o"></i> {{ scheduleRange(node.schedule) }}
              </span>
              <span class="poolTimeBadge missing" v-else>
                <i class="fa fa-clock-o"></i> Time not set
              </span>
            </div>

            <div class="badges">
              <span class="badge soft">parent {{ node.parentMatchId === null ? 'null' : node.parentMatchId }}</span>
              <span class="badge soft">{{ node.players.length }} player(s)</span>
              <span class="badge soft">{{ node.children.length }} child(ren)</span>
              <span class="badge editBadge">click to edit</span>
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
    selectedNode: null,
    currentTournamentId: null,
    venues: [],
    poolSchedules: {},
    venueAdminAllowed: false,
    venueLoading: false,
    venueError: '',
    venueMessage: '',
    newVenueName: '',
    newVenueDescription: '',
    selectedVenueId: null,
    selectedScheduleFrom: '',
    savingVenue: false,
    matchDetails: null,
    loadingDetails: false,
    savingResult: false,
    resultError: '',
    resultMessage: '',
    lastAdvancement: null
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
            children: [],
            schedule: this.poolSchedules[String(id)] || null
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
        const schedule = n.schedule || {};
        const hay = [
          n.matchName || '',
          n.players.join(' '),
          schedule.venueName || '',
          schedule.fromTime || ''
        ].join(' ').toLowerCase();
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
    },

    selectedIsPool() {
      if (!this.selectedNode || !this.matchDetails || this.matchDetails.individualMatch) return false;
      const children = Array.isArray(this.selectedNode.children) ? this.selectedNode.children : [];
      return children.length > 0 && children.every(child =>
        !child.children || child.children.length === 0
      );
    },

    selectedVenueLabel() {
      if (!this.matchDetails || !this.matchDetails.venue) return 'Not assigned';
      return this.matchDetails.venue.name || ('Venue #' + this.matchDetails.venue.facilityId);
    },

    scoreDirectionText() {
      if (!this.matchDetails || !this.matchDetails.matchRules) return '';
      const from = Number(this.matchDetails.matchRules.playFrom);
      const to = Number(this.matchDetails.matchRules.playTo);
      if (!Number.isFinite(from) || !Number.isFinite(to)) return '';
      return 'Winner finishes on ' + to + '; stage tiebreak uses score difference';
    },

    hasPlayTo() {
      if (!this.matchDetails || !this.matchDetails.matchRules) return false;
      const to = Number(this.matchDetails.matchRules.playTo);
      return Number.isFinite(to);
    },

    scoreMarginText() {
      if (this.activeSeats.length !== 2 || !this.hasPlayTo) return '';
      const a = this.activeSeats[0];
      const b = this.activeSeats[1];
      const aScore = Number(a.resultPoints);
      const bScore = Number(b.resultPoints);
      const bothEntered =
        a.resultPoints !== '' && a.resultPoints !== null && a.resultPoints !== undefined &&
        b.resultPoints !== '' && b.resultPoints !== null && b.resultPoints !== undefined &&
        Number.isFinite(aScore) && Number.isFinite(bScore);
      if (!bothEntered) return '';
      return 'Score difference: ' + Math.abs(aScore - bScore);
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
      this.selectedNode = node;
      this.selectedMatchId = node.matchId;
      this.lastAdvancement = null;
      this.venueError = '';
      this.venueMessage = '';
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
        venue: details.venue || null,
        schedule: details.schedule || null,
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
        this.selectedVenueId = this.matchDetails.venue ? Number(this.matchDetails.venue.facilityId) : null;
        this.selectedScheduleFrom = this.toTimeInput(this.matchDetails.schedule ? this.matchDetails.schedule.fromTime : null);
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

    toTimeInput(value) {
      if (!value) return '';
      const raw = String(value);
      const iso = raw.match(/T(\d{2}):(\d{2})/);
      if (iso) return iso[1] + ':' + iso[2];
      const plain = raw.match(/^(\d{1,2}):(\d{2})/);
      if (plain) return String(plain[1]).padStart(2, '0') + ':' + plain[2];
      return '';
    },

    formatTimeDisplay(value) {
      const time = this.toTimeInput(value);
      return time || 'Not set';
    },

    scheduleDisplay(schedule) {
      if (!schedule) return 'Time not set';
      const from = schedule.fromTime ? this.formatTimeDisplay(schedule.fromTime) : '';
      return from ? 'Starts ' + from : 'Time not set';
    },

    scheduleTimeForApi(value) {
      const time = (value || '').trim();
      if (!time) return null;
      const match = time.match(/^(\d{2}):(\d{2})$/);
      if (!match) return null;
      // Schedule currently uses SQL DATETIME columns. The date is deliberately
      // fixed because the application schedules pools by time-of-day only.
      return '2000-01-01T' + match[1] + ':' + match[2] + ':00';
    },

    timeToMinutes(value) {
      const match = String(value || '').match(/^(\d{2}):(\d{2})$/);
      if (!match) return null;
      return Number(match[1]) * 60 + Number(match[2]);
    },

    async loadPoolSchedules(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0) return;

      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/pool-schedules',
          { headers: this.authHeaders() }
        );
        const schedules = res.data && Array.isArray(res.data.schedules) ? res.data.schedules : [];
        const next = {};
        for (const schedule of schedules) {
          next[String(schedule.poolMatchId)] = schedule;
        }
        this.poolSchedules = next;
      } catch (e) {
        console.error(e);
        this.poolSchedules = {};
      }
    },

    async loadVenues(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0) return;

      this.venueLoading = true;
      this.venueError = '';
      this.venueMessage = '';

      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/venues',
          { headers: this.authHeaders() }
        );
        this.venues = res.data && Array.isArray(res.data.venues) ? res.data.venues : [];
        this.venueAdminAllowed = true;
      } catch (e) {
        console.error(e);
        this.venues = [];
        if (e.response && e.response.status === 403) {
          this.venueAdminAllowed = false;
          this.venueError = 'Only the tournament administrator can create and assign venues.';
        } else if (e.response && e.response.status === 401) {
          this.venueAdminAllowed = false;
          this.venueError = 'You are not logged in, or your login has expired.';
        } else {
          this.venueAdminAllowed = false;
          this.venueError = (e.response && e.response.data && e.response.data.message)
            ? e.response.data.message
            : 'Could not load tournament venues.';
        }
      } finally {
        this.venueLoading = false;
      }
    },

    async createVenue() {
      const name = (this.newVenueName || '').trim();
      if (!name || !this.currentTournamentId) return;

      this.savingVenue = true;
      this.venueError = '';
      this.venueMessage = '';

      try {
        const res = await axios.post(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) + '/venues',
          {
            name: name,
            description: (this.newVenueDescription || '').trim() || null
          },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        this.newVenueName = '';
        this.newVenueDescription = '';
        await this.loadVenues(this.currentTournamentId);
        this.venueMessage = 'Venue created.';

        if (res.data && res.data.venue && this.selectedIsPool && this.selectedVenueId == null) {
          this.selectedVenueId = Number(res.data.venue.facilityId);
        }
      } catch (e) {
        console.error(e);
        this.venueError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not create venue.';
      } finally {
        this.savingVenue = false;
      }
    },

    async deleteVenue(venue) {
      if (!venue || !venue.facilityId || !this.currentTournamentId) return;
      if (!confirm('Delete venue "' + (venue.name || venue.facilityId) + '"?')) return;

      this.savingVenue = true;
      this.venueError = '';
      this.venueMessage = '';

      try {
        await axios.delete(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) + '/venues/' + encodeURIComponent(venue.facilityId),
          { headers: this.authHeaders() }
        );
        await this.loadVenues(this.currentTournamentId);
        if (Number(this.selectedVenueId) === Number(venue.facilityId)) this.selectedVenueId = null;
        this.venueMessage = 'Venue deleted.';
      } catch (e) {
        console.error(e);
        this.venueError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not delete venue.';
      } finally {
        this.savingVenue = false;
      }
    },

    async savePoolSchedule() {
      if (!this.currentTournamentId || !this.matchDetails || !this.selectedIsPool) return;

      this.savingVenue = true;
      this.venueError = '';
      this.venueMessage = '';

      const facilityId = this.selectedVenueId === '' || this.selectedVenueId === null || this.selectedVenueId === undefined
        ? null
        : Number(this.selectedVenueId);
      const fromInput = (this.selectedScheduleFrom || '').trim() || null;
      const fromMinutes = fromInput ? this.timeToMinutes(fromInput) : null;

      if (fromInput && fromMinutes === null) {
        this.venueError = 'Enter a valid start time.';
        this.savingVenue = false;
        return;
      }

      const fromTime = this.scheduleTimeForApi(fromInput);

      try {
        const poolMatchId = this.matchDetails.matchId;
        const res = await axios.put(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) + '/pool/' + encodeURIComponent(poolMatchId) + '/schedule',
          {
            facilityId: facilityId,
            fromTime: fromTime
          },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        await this.loadPoolSchedules(this.currentTournamentId);
        await this.loadMatchDetails(poolMatchId);
        this.venueMessage = res.data && res.data.message ? res.data.message : 'Pool schedule saved.';
      } catch (e) {
        console.error(e);
        this.venueError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not save the pool schedule.';
      } finally {
        this.savingVenue = false;
      }
    },

    setWinner(seat) {
      if (!seat || this.activeSeats.length !== 2) return;
      const playTo = this.matchDetails && this.matchDetails.matchRules
        ? Number(this.matchDetails.matchRules.playTo)
        : NaN;
      for (const current of this.activeSeats) {
        const isWinner = current.seatId === seat.seatId;
        current.resultMatchPlace = isWinner ? 1 : 2;
        if (Number.isFinite(playTo)) {
          if (isWinner) current.resultPoints = playTo;
          else if (Number(current.resultPoints) === playTo) current.resultPoints = null;
        }
      }
      this.resultError = '';
      this.resultMessage = '';
    },

    setLoser(seat) {
      if (!seat || this.activeSeats.length !== 2) return;
      const playTo = this.matchDetails && this.matchDetails.matchRules
        ? Number(this.matchDetails.matchRules.playTo)
        : NaN;
      for (const current of this.activeSeats) {
        const isWinner = current.seatId !== seat.seatId;
        current.resultMatchPlace = isWinner ? 1 : 2;
        if (Number.isFinite(playTo)) {
          if (isWinner) current.resultPoints = playTo;
          else if (Number(current.resultPoints) === playTo) current.resultPoints = null;
        }
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
        const currentMatchId = this.matchDetails.matchId;
        const res = await axios.delete(
          'http://localhost:5000/api/match/' + encodeURIComponent(currentMatchId) + '/result',
          { headers: this.authHeaders() }
        );

        for (const seat of this.matchDetails.seats) {
          seat.resultMatchPlace = null;
          seat.resultPoints = null;
        }

        const lastTournamentId = localStorage.getItem('lastTournamentId');
        if (lastTournamentId) {
          await this.loadMatches(lastTournamentId, true);
          await this.loadMatchDetails(currentMatchId);
        }

        this.lastAdvancement = res.data && res.data.advancement ? res.data.advancement : null;
        this.resultMessage = (res.data && res.data.message)
          ? res.data.message
          : 'Result cleared.';
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

      // In this scoring model every player starts at PlayFrom and the winner
      // always finishes exactly on PlayTo. The loser's remaining score determines
      // the winning margin / score difference.
      if (this.activeSeats.length === 2 && this.matchDetails.matchRules) {
        const rules = this.matchDetails.matchRules;
        const from = Number(rules.playFrom);
        const to = Number(rules.playTo);
        const winner = this.activeSeats.find(s => Number(s.resultMatchPlace) === 1);
        const loser = this.activeSeats.find(s => Number(s.resultMatchPlace) === 2);

        if (Number.isFinite(to)) {
          if (!winner || winner.resultPoints === '' || winner.resultPoints === null || winner.resultPoints === undefined) {
            return 'The winner must have a final score of ' + to + '.';
          }
          if (Number(winner.resultPoints) !== to) {
            return 'The winner must finish exactly on PlayTo (' + to + ').';
          }
          if (!loser || loser.resultPoints === '' || loser.resultPoints === null || loser.resultPoints === undefined || !Number.isFinite(Number(loser.resultPoints))) {
            return "Enter the loser's remaining score so the score difference can be calculated.";
          }
          if (Number(loser.resultPoints) === to) {
            return 'The loser cannot also finish on PlayTo (' + to + ').';
          }

          if (Number.isFinite(from)) {
            const min = Math.min(from, to);
            const max = Math.max(from, to);
            const loserScore = Number(loser.resultPoints);
            if (loserScore < min || loserScore > max) {
              return "The loser's score must be between PlayFrom (" + from + ") and PlayTo (" + to + ").";
            }
          }
        }
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
        const currentMatchId = this.matchDetails.matchId;
        const res = await axios.post(
          'http://localhost:5000/api/match/result',
          payload,
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        const lastTournamentId = localStorage.getItem('lastTournamentId');
        if (lastTournamentId) {
          await this.loadMatches(lastTournamentId, true);
        }
        await this.loadMatchDetails(currentMatchId);

        this.lastAdvancement = res.data && res.data.advancement ? res.data.advancement : null;

        const message = res.data && res.data.message
          ? res.data.message
          : 'Result saved.';

        const advanced = res.data && res.data.advancement && Array.isArray(res.data.advancement.advancedPlayers)
          ? res.data.advancement.advancedPlayers
          : [];

        if (advanced.length) {
          const moved = advanced.map(x =>
            (x.playerName || ('Player #' + x.playerId)) +
            ' (place ' + x.place + ') → ' +
            (x.destinationMatchName || ('match #' + x.destinationMatchId))
          );
          this.resultMessage = message + ' ' + moved.join('; ');
        } else {
          this.resultMessage = message;
        }
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

    async loadMatches(tournamentId, preserveSelection) {
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
        this.currentTournamentId = tid;
        await this.loadPoolSchedules(tid);
        await this.loadVenues(tid);
        if (!preserveSelection) {
          this.selectedMatchId = null;
          this.selectedNode = null;
          this.selectedVenueId = null;
          this.selectedScheduleFrom = '';
          this.matchDetails = null;
          this.resultError = '';
          this.resultMessage = '';
          this.lastAdvancement = null;
        }
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
