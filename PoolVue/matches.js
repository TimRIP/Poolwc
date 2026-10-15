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
    formatScheduleDate(value) {
      if (!value) return '';
      const raw = String(value);
      const match = raw.match(/^(\d{4})-(\d{2})-(\d{2})/);
      if (!match) return '';
      // Old schedule entries used 2000-01-01 as a time-only placeholder.
      // Do not show that legacy placeholder as a real tournament date.
      if (match[1] === '2000' && match[2] === '01' && match[3] === '01') return '';
      return match[3] + '/' + match[2] + '/' + match[1];
    },
    scheduleRange(schedule) {
      if (!schedule) return '';
      const date = this.formatScheduleDate(schedule.fromTime);
      const from = this.formatScheduleTime(schedule.fromTime);
      if (date && from) return date + ' · Starts ' + from;
      if (from) return 'Starts ' + from;
      return '';
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
    tournaments: [],
    tournamentsLoading: false,
    tournamentListError: '',
    selectedTournamentId: '',
    venues: [],
    poolSchedules: {},
    venueAdminAllowed: false,
    venueLoading: false,
    venueError: '',
    venueMessage: '',
    editAccessLoading: false,
    matchEditAllowed: false,
    isTournamentAdmin: false,
    isTournamentMatchEditor: false,
    editors: [],
    editorsLoading: false,
    editorError: '',
    editorMessage: '',
    newEditorUsername: '',
    savingEditor: false,
    drawLoading: false,
    drawStatus: null,
    drawRegistrations: [],
    drawSlots: [],
    drawAssignments: {},
    drawError: '',
    drawMessage: '',
    savingDraw: false,
    playerNamesLoading: false,
    tournamentPlayers: [],
    playerNameError: '',
    playerNameMessage: '',
    savingPlayerNameId: null,
    playerFinderQuery: '',
    playerFinderPlayers: [],
    playerFinderLoading: false,
    playerFinderError: '',
    selectedFinderPlayer: null,
    playerFinderMatches: [],
    playerFinderMatchesLoading: false,
    newVenueName: '',
    newVenueDescription: '',
    selectedVenueId: null,
    selectedScheduleDate: '',
    selectedScheduleFrom: '',
    savingVenue: false,
    matchDetails: null,
    loadingDetails: false,
    savingResult: false,
    resultError: '',
    resultMessage: '',
    lastAdvancement: null,
    poolMatrixLoading: false,
    poolMatrixError: '',
    poolMatrixMessage: '',
    poolMatrixPlayers: [],
    poolMatrixMatches: [],
    poolMatrixCells: {},
    savingPoolMatrix: false,
    manualAdvanceLoading: false,
    manualAdvanceState: null,
    manualAdvanceAssignments: {},
    manualAdvanceError: '',
    manualAdvanceMessage: '',
    savingManualAdvance: false,
    stageAdvanceLoading: false,
    stageAdvanceState: null,
    stageAdvanceAssignments: {},
    stageWildcardAssignments: {},
    stageAdvanceError: '',
    stageAdvanceMessage: '',
    savingStageAdvance: false
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

    selectedIsStageWithPools() {
      if (!this.selectedNode || !this.matchDetails || this.matchDetails.individualMatch) return false;
      if (this.selectedIsPool) return false;

      const children = Array.isArray(this.selectedNode.children) ? this.selectedNode.children : [];
      if (!children.length) return false;

      // A stage can contain both its own pools AND the next stage as another
      // grouping child in the generated match tree.  The old check required
      // every child to be a pool, so e.g. Group stage -> [pulje 1, pulje 2,
      // Semi-finals] was not recognised as a stage.  It is enough that at
      // least one direct child is a real pool (a grouping node whose direct
      // children are playable leaf matches).
      return children.some(child => {
        const grandchildren = Array.isArray(child.children) ? child.children : [];
        return grandchildren.length > 0 && grandchildren.every(grandchild =>
          !grandchild.children || grandchild.children.length === 0
        );
      });
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
      const direction = to < from ? 'lower accumulated stage score is better' : 'higher accumulated stage score is better';
      return 'Winner finishes on ' + to + '; ' + direction;
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
    },

    poolMatrixCompletedMatches() {
      let complete = 0;
      for (const match of this.poolMatrixMatches) {
        if (!match || !Array.isArray(match.seats) || match.seats.length !== 2) continue;
        const a = match.seats[0];
        const b = match.seats[1];
        const ac = this.poolMatrixCells[this.poolMatrixPairKey(a.playerId, b.playerId, a.playerId)];
        const bc = this.poolMatrixCells[this.poolMatrixPairKey(a.playerId, b.playerId, b.playerId)];
        if (!ac || !bc) continue;
        const av = ac.value;
        const bv = bc.value;
        const playTo = Number(match.matchRules && match.matchRules.playTo);
        const both = av !== null && av !== '' && bv !== null && bv !== '' &&
          Number.isFinite(Number(av)) && Number.isFinite(Number(bv));
        if (!both || !Number.isFinite(playTo)) continue;
        const winners = [Number(av), Number(bv)].filter(v => v === playTo).length;
        if (winners === 1) complete++;
      }
      return complete;
    },

    filteredPlayerFinderPlayers() {
      const q = (this.playerFinderQuery || '').trim().toLowerCase();
      if (!q) return [];
      return this.playerFinderPlayers
        .filter(player => {
          const hay = [
            player.playerName || '',
            player.playerId || '',
            player.mmr === null || player.mmr === undefined ? '' : player.mmr
          ].join(' ').toLowerCase();
          return hay.includes(q);
        })
        .slice(0, 20);
    },

    poolMatrixDirtyCount() {
      const dirty = new Set();
      for (const key of Object.keys(this.poolMatrixCells)) {
        const cell = this.poolMatrixCells[key];
        const now = cell && cell.value !== '' && cell.value !== null && cell.value !== undefined
          ? Number(cell.value)
          : null;
        const before = cell && cell.originalValue !== '' && cell.originalValue !== null && cell.originalValue !== undefined
          ? Number(cell.originalValue)
          : null;
        if (now !== before) dirty.add(cell.matchId);
      }
      return dirty.size;
    },

    poolMatrixStandings() {
      const stats = new Map();
      for (const player of this.poolMatrixPlayers) {
        stats.set(Number(player.playerId), {
          playerId: Number(player.playerId),
          playerName: player.playerName,
          points: 0,
          score: 0,
          played: 0,
          headToHead: 0,
          place: null,
          placeLabel: '–'
        });
      }

      const completed = [];
      for (const match of this.poolMatrixMatches) {
        if (!match || !Array.isArray(match.seats) || match.seats.length !== 2) continue;
        const a = match.seats[0];
        const b = match.seats[1];
        const ac = this.poolMatrixCells[this.poolMatrixPairKey(a.playerId, b.playerId, a.playerId)];
        const bc = this.poolMatrixCells[this.poolMatrixPairKey(a.playerId, b.playerId, b.playerId)];
        if (!ac || !bc) continue;

        const av = ac.value;
        const bv = bc.value;
        const playTo = Number(match.matchRules && match.matchRules.playTo);
        if (av === null || av === '' || bv === null || bv === '' ||
            !Number.isFinite(Number(av)) || !Number.isFinite(Number(bv)) ||
            !Number.isFinite(playTo)) {
          continue;
        }

        const aScore = Number(av);
        const bScore = Number(bv);
        const aWins = aScore === playTo;
        const bWins = bScore === playTo;
        if (aWins === bWins) continue;

        const winnerId = Number(aWins ? a.playerId : b.playerId);
        const loserId = Number(aWins ? b.playerId : a.playerId);

        const aStanding = stats.get(Number(a.playerId));
        const bStanding = stats.get(Number(b.playerId));
        const winner = stats.get(winnerId);
        const loser = stats.get(loserId);
        if (!winner || !loser || !aStanding || !bStanding) continue;

        winner.points += 1;
        // Score is the sum of the actual final scores the player has recorded
        // in all completed matches. Example with 70 -> 0: 60 + 20 + 0 = 80.
        aStanding.score += aScore;
        bStanding.score += bScore;
        winner.played += 1;
        loser.played += 1;
        completed.push({ winnerId, loserId });
      }

      const firstRules = this.poolMatrixMatches.find(x => x && x.matchRules)?.matchRules || {};
      const playFrom = Number(firstRules.playFrom);
      const playTo = Number(firstRules.playTo);
      // If the game counts down (70 -> 0), lower accumulated score is better.
      // If it counts up (0 -> 70), higher accumulated score is better.
      const higherScoreIsBetter = Number.isFinite(playFrom) && Number.isFinite(playTo)
        ? playTo > playFrom
        : true;

      const orderedBase = [...stats.values()].sort((a, b) =>
        (b.points - a.points) ||
        (higherScoreIsBetter ? (b.score - a.score) : (a.score - b.score)) ||
        a.playerName.localeCompare(b.playerName)
      );

      // Match the backend ordering: points/wins, then accumulated score in the
      // PlayFrom -> PlayTo direction, then head-to-head amongst remaining ties.
      let i = 0;
      while (i < orderedBase.length) {
        const first = orderedBase[i];
        let j = i + 1;
        while (j < orderedBase.length &&
               orderedBase[j].points === first.points &&
               orderedBase[j].score === first.score) {
          j++;
        }

        const tied = orderedBase.slice(i, j);
        if (tied.length > 1) {
          const tiedIds = new Set(tied.map(x => x.playerId));
          for (const row of tied) {
            row.headToHead = completed.filter(x =>
              x.winnerId === row.playerId && tiedIds.has(x.loserId)
            ).length;
          }
          tied.sort((a, b) =>
            (b.headToHead - a.headToHead) ||
            a.playerName.localeCompare(b.playerName)
          );
          orderedBase.splice(i, tied.length, ...tied);
        }
        i = j;
      }

      let place = 1;
      for (let idx = 0; idx < orderedBase.length; idx++) {
        const row = orderedBase[idx];
        if (idx > 0) {
          const prev = orderedBase[idx - 1];
          const same = row.points === prev.points &&
            row.score === prev.score &&
            row.headToHead === prev.headToHead;
          if (!same) place = idx + 1;
        }
        row.place = place;
        const tiedWithPrev = idx > 0 &&
          row.points === orderedBase[idx - 1].points &&
          row.score === orderedBase[idx - 1].score &&
          row.headToHead === orderedBase[idx - 1].headToHead;
        const tiedWithNext = idx < orderedBase.length - 1 &&
          row.points === orderedBase[idx + 1].points &&
          row.score === orderedBase[idx + 1].score &&
          row.headToHead === orderedBase[idx + 1].headToHead;
        row.placeLabel = (tiedWithPrev || tiedWithNext ? '=' : '') + String(place);
      }

      return orderedBase;
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

    resetPoolMatrix() {
      this.poolMatrixLoading = false;
      this.poolMatrixError = '';
      this.poolMatrixMessage = '';
      this.poolMatrixPlayers = [];
      this.poolMatrixMatches = [];
      this.poolMatrixCells = {};
      this.savingPoolMatrix = false;
    },

    resetManualAdvancement() {
      this.manualAdvanceLoading = false;
      this.manualAdvanceState = null;
      this.manualAdvanceAssignments = {};
      this.manualAdvanceError = '';
      this.manualAdvanceMessage = '';
      this.savingManualAdvance = false;
    },

    resetStageAdvancement() {
      this.stageAdvanceLoading = false;
      this.stageAdvanceState = null;
      this.stageAdvanceAssignments = {};
      this.stageWildcardAssignments = {};
      this.stageAdvanceError = '';
      this.stageAdvanceMessage = '';
      this.savingStageAdvance = false;
    },

    stageAdvancePlaceLabel(player) {
      if (!player || player.place === null || player.place === undefined) return '';
      const place = Number(player.place);
      const total = Number(player.sourcePoolPlayerCount || 0);
      if (Number.isFinite(total) && total > 0) {
        return 'placement ' + place + ' / ' + total;
      }
      return 'placement ' + place;
    },

    stageAdvancePoolLabel(pool) {
      if (!pool) return 'Pool';
      const stage = pool.targetStageName || '';
      const name = pool.matchName || ('Pool #' + pool.matchId);
      return stage && stage !== name ? stage + ' · ' + name : name;
    },

    stageAdvancePoolOptions(player) {
      const state = this.stageAdvanceState;
      if (!state || !player || !Array.isArray(state.pools)) return [];
      return state.pools.filter(pool =>
        Number(pool.targetStageMatchId) === Number(player.targetStageMatchId)
      );
    },

    stageAdvanceSelectedCount(poolId) {
      const wanted = Number(poolId);
      let count = Object.keys(this.stageAdvanceAssignments).reduce((total, playerId) => {
        const value = this.stageAdvanceAssignments[playerId];
        return total + (Number(value) === wanted ? 1 : 0);
      }, 0);

      const state = this.stageAdvanceState;
      if (state && Array.isArray(state.wildcardSlots)) {
        for (const slot of state.wildcardSlots) {
          const selectedPlayerId = this.stageWildcardAssignments[String(slot.seatId)];
          if (Number(slot.destinationPoolMatchId) === wanted &&
              selectedPlayerId !== null && selectedPlayerId !== undefined && selectedPlayerId !== '') {
            count++;
          }
        }
      }
      return count;
    },

    stageAdvancePoolCapacityText(pool) {
      if (!pool) return '';
      const selected = this.stageAdvanceSelectedCount(pool.matchId);
      const totalCapacity = Number(pool.capacity || 0) + Number(pool.wildcardCapacity || 0);
      const wildcardCapacity = Number(pool.wildcardCapacity || 0);
      return selected + ' / ' + totalCapacity + ' assigned' +
        (wildcardCapacity ? ' (' + wildcardCapacity + ' wildcard)' : '');
    },

    stageWildcardCandidateLabel(player) {
      if (!player) return 'Player';
      const name = player.playerName || ('Player #' + player.playerId);
      const pool = player.sourcePoolName || ('Pool #' + player.sourcePoolMatchId);
      return name + ' · ' + pool + ' · placement ' + player.place + ' / ' + Number(player.sourcePoolPlayerCount || 0);
    },

    stageWildcardPoolLabel(slot) {
      if (!slot) return 'Wildcard';
      const stage = slot.targetStageName || '';
      const pool = slot.destinationPoolName || ('Pool #' + slot.destinationPoolMatchId);
      return stage && stage !== pool ? stage + ' · ' + pool : pool;
    },

    stageWildcardPlayerUsedElsewhere(playerId, seatId) {
      const wanted = Number(playerId);
      return Object.keys(this.stageWildcardAssignments).some(key =>
        Number(key) !== Number(seatId) && Number(this.stageWildcardAssignments[key]) === wanted
      );
    },

    setStageWildcardPlayer(seatId, playerId) {
      this.$set(
        this.stageWildcardAssignments,
        String(seatId),
        playerId === null || playerId === undefined || playerId === '' ? null : Number(playerId)
      );
      this.stageAdvanceError = '';
      this.stageAdvanceMessage = '';
    },

    applyStageAdvancementState(state) {
      this.stageAdvanceState = state || null;
      this.stageAdvanceAssignments = {};
      this.stageWildcardAssignments = {};
      if (!state) return;

      if (Array.isArray(state.players)) {
        for (const player of state.players) {
          this.$set(
            this.stageAdvanceAssignments,
            String(player.playerId),
            player.currentDestinationPoolMatchId === null || player.currentDestinationPoolMatchId === undefined
              ? null
              : Number(player.currentDestinationPoolMatchId)
          );
        }
      }

      if (Array.isArray(state.wildcardSlots)) {
        for (const slot of state.wildcardSlots) {
          this.$set(
            this.stageWildcardAssignments,
            String(slot.seatId),
            slot.playerId === null || slot.playerId === undefined ? null : Number(slot.playerId)
          );
        }
      }
    },

    async loadStageAdvancement(stageMatchId) {
      this.stageAdvanceLoading = true;
      this.stageAdvanceError = '';
      this.stageAdvanceMessage = '';

      try {
        const res = await axios.get(
          'http://localhost:5000/api/match/' + encodeURIComponent(stageMatchId) + '/advancement/stage',
          { headers: this.authHeaders() }
        );
        this.applyStageAdvancementState(res.data);
      } catch (e) {
        console.error(e);
        this.stageAdvanceState = null;
        this.stageAdvanceAssignments = {};
        this.stageWildcardAssignments = {};
        if (e.response && e.response.status === 401) {
          this.stageAdvanceError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.status === 403) {
          this.stageAdvanceError = 'You do not have permission to manage advancement for this tournament.';
        } else if (e.response && e.response.status === 404) {
          this.stageAdvanceError = 'The stage advancement API endpoint was not found. Restart PoolApi after updating the backend.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.stageAdvanceError = e.response.data.message;
        } else {
          this.stageAdvanceError = 'Could not load the advancing players for this stage.';
        }
      } finally {
        this.stageAdvanceLoading = false;
      }
    },

    setStageAdvanceDestination(playerId, poolId) {
      this.$set(
        this.stageAdvanceAssignments,
        String(playerId),
        poolId === null || poolId === undefined || poolId === '' ? null : Number(poolId)
      );
      this.stageAdvanceError = '';
      this.stageAdvanceMessage = '';
    },

    async saveStageAdvancement() {
      if (!this.matchEditAllowed || !this.selectedIsStageWithPools || !this.stageAdvanceState || this.savingStageAdvance) return;
      if (!this.stageAdvanceState.stageComplete) {
        this.stageAdvanceError = 'Finish every pool in this stage before assigning the advancing and wildcard players.';
        return;
      }

      const players = Array.isArray(this.stageAdvanceState.players) ? this.stageAdvanceState.players : [];
      const pools = Array.isArray(this.stageAdvanceState.pools) ? this.stageAdvanceState.pools : [];
      const wildcardSlots = Array.isArray(this.stageAdvanceState.wildcardSlots) ? this.stageAdvanceState.wildcardSlots : [];
      const wildcardCandidates = Array.isArray(this.stageAdvanceState.wildcardCandidates) ? this.stageAdvanceState.wildcardCandidates : [];

      if (!players.length && !wildcardSlots.length) {
        this.stageAdvanceError = 'There are no advancement or wildcard places in this stage.';
        return;
      }

      for (const player of players) {
        const selectedPoolId = this.stageAdvanceAssignments[String(player.playerId)];
        if (selectedPoolId === null || selectedPoolId === undefined || selectedPoolId === '') {
          this.stageAdvanceError = 'Choose a destination pool for every player who qualified normally.';
          return;
        }

        const allowed = this.stageAdvancePoolOptions(player).some(pool =>
          Number(pool.matchId) === Number(selectedPoolId)
        );
        if (!allowed) {
          this.stageAdvanceError = 'One of the selected pools is not in the stage that player qualified for.';
          return;
        }
      }

      const wildcardPlayerIds = [];
      for (const slot of wildcardSlots) {
        const selectedPlayerId = this.stageWildcardAssignments[String(slot.seatId)];
        if (selectedPlayerId === null || selectedPlayerId === undefined || selectedPlayerId === '') {
          this.stageAdvanceError = 'Choose an eliminated player for every wildcard place.';
          return;
        }

        const isEligible = wildcardCandidates.some(player => Number(player.playerId) === Number(selectedPlayerId));
        if (!isEligible) {
          this.stageAdvanceError = 'One of the selected wildcard players is no longer eligible.';
          return;
        }
        wildcardPlayerIds.push(Number(selectedPlayerId));
      }

      if (new Set(wildcardPlayerIds).size !== wildcardPlayerIds.length) {
        this.stageAdvanceError = 'The same player cannot use more than one wildcard place.';
        return;
      }

      for (const pool of pools) {
        const normalSelected = Object.keys(this.stageAdvanceAssignments).reduce((count, playerId) => {
          return count + (Number(this.stageAdvanceAssignments[playerId]) === Number(pool.matchId) ? 1 : 0);
        }, 0);
        if (normalSelected > Number(pool.capacity || 0)) {
          this.stageAdvanceError = (pool.matchName || 'A destination pool') +
            ' has ' + normalSelected + ' normal qualifiers selected but only ' + Number(pool.capacity || 0) + ' normal qualification places.';
          return;
        }
      }

      const assignments = players.map(player => ({
        playerId: Number(player.playerId),
        destinationPoolMatchId: Number(this.stageAdvanceAssignments[String(player.playerId)])
      }));

      const wildcardAssignments = wildcardSlots.map(slot => ({
        seatId: Number(slot.seatId),
        playerId: Number(this.stageWildcardAssignments[String(slot.seatId)])
      }));

      this.savingStageAdvance = true;
      this.stageAdvanceError = '';
      this.stageAdvanceMessage = '';
      const sourceStageId = Number(this.selectedMatchId);

      try {
        const res = await axios.post(
          'http://localhost:5000/api/match/' + encodeURIComponent(sourceStageId) + '/advancement/stage/manual',
          { assignments: assignments, wildcardAssignments: wildcardAssignments },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        if (res.data && res.data.state) {
          this.applyStageAdvancementState(res.data.state);
        } else {
          await this.loadStageAdvancement(sourceStageId);
        }

        if (this.currentTournamentId) {
          await this.loadMatches(this.currentTournamentId, true);
          const freshNode = this.findTreeNodeById(sourceStageId);
          if (freshNode) {
            this.selectedNode = freshNode;
            this.selectedMatchId = sourceStageId;
            await this.loadMatchDetails(sourceStageId);
          }
        }

        this.stageAdvanceMessage = res.data && res.data.message
          ? res.data.message
          : 'Stage advancement and wildcards saved.';
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.stageAdvanceError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.status === 403) {
          this.stageAdvanceError = 'You do not have permission to manage advancement for this tournament.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.stageAdvanceError = e.response.data.message;
        } else {
          this.stageAdvanceError = 'Could not save the stage advancement and wildcards.';
        }
      } finally {
        this.savingStageAdvance = false;
      }
    },

    manualAdvancePlayerPlaceLabel(player) {
      if (!player) return '';
      const standing = this.poolMatrixStanding(player.playerId);
      if (standing && standing.placeLabel && standing.placeLabel !== '–') {
        return 'place ' + standing.placeLabel;
      }
      if (player.place !== null && player.place !== undefined) {
        return 'place ' + player.place;
      }
      return '';
    },

    manualAdvanceOptionLabel(player) {
      if (!player) return 'Player';
      const name = player.playerName || ('Player #' + player.playerId);
      const place = this.manualAdvancePlayerPlaceLabel(player);
      return place ? name + ' · ' + place : name;
    },

    manualAdvanceEligiblePlayers() {
      const state = this.manualAdvanceState;
      if (!state || !Array.isArray(state.players) || !Array.isArray(state.destinations)) return [];

      const advancingPlaces = new Set(
        state.destinations
          .map(destination => Number(destination.autoSelectPlace))
          .filter(place => Number.isInteger(place) && place > 0)
      );

      return state.players
        .filter(player => player && player.place !== null && player.place !== undefined && advancingPlaces.has(Number(player.place)))
        .slice()
        .sort((a, b) => Number(a.place) - Number(b.place) || Number(a.playerId) - Number(b.playerId));
    },

    manualAdvanceDestinationLabel(destination) {
      if (!destination) return 'Next pool';
      const parent = destination.parentMatchName || '';
      const match = destination.matchName || ('Pool #' + destination.matchId);
      const base = parent && parent !== match ? parent + ' · ' + match : match;
      if (destination.requiresPlayerSwap && destination.playerName) {
        return base + ' (swap with ' + destination.playerName + ')';
      }
      return base;
    },

    manualAdvanceTargetStageKey(destination) {
      if (!destination) return null;
      if (destination.parentMatchId !== null && destination.parentMatchId !== undefined) {
        return Number(destination.parentMatchId);
      }
      return Number(destination.matchId);
    },

    manualAdvancePoolOptions(player) {
      const state = this.manualAdvanceState;
      if (!state || !player || !Array.isArray(state.destinations)) return [];

      const options = state.destinations.filter(destination =>
        Number(destination.autoSelectPlace) === Number(player.place)
      );

      const seenPools = new Set();
      return options.filter(destination => {
        const poolKey = Number(destination.matchId);
        if (seenPools.has(poolKey)) return false;
        seenPools.add(poolKey);
        return true;
      });
    },

    setManualAdvanceDestination(playerId, destinationSeatId) {
      const playerKey = String(playerId);
      const newSeatId = Number(destinationSeatId);
      if (!Number.isFinite(newSeatId)) return;

      const oldValue = this.manualAdvanceAssignments[playerKey];
      const oldSeatId = oldValue === null || oldValue === undefined || oldValue === ''
        ? null
        : Number(oldValue);

      // If another qualified player currently occupies the selected destination,
      // swap the two destinations. This makes a two-pool setup editable in one
      // action instead of disabling both alternatives because each is occupied.
      const otherPlayerKey = Object.keys(this.manualAdvanceAssignments).find(otherKey => {
        if (String(otherKey) === playerKey) return false;
        const selected = this.manualAdvanceAssignments[otherKey];
        return selected !== null && selected !== undefined && selected !== '' && Number(selected) === newSeatId;
      });

      if (otherPlayerKey !== undefined) {
        this.$set(this.manualAdvanceAssignments, otherPlayerKey, oldSeatId);
      }

      this.$set(this.manualAdvanceAssignments, playerKey, newSeatId);
      this.manualAdvanceError = '';
      this.manualAdvanceMessage = '';
    },

    applyManualAdvancementState(state) {
      this.manualAdvanceState = state || null;
      this.manualAdvanceAssignments = {};
      if (!state || !Array.isArray(state.destinations) || !Array.isArray(state.players)) return;

      const eligiblePlayers = this.manualAdvanceEligiblePlayers();
      for (const player of eligiblePlayers) {
        let destination = state.destinations.find(item =>
          item.playerId !== null && item.playerId !== undefined && Number(item.playerId) === Number(player.playerId)
        );

        // If automatic advancement has not populated the target yet, default to
        // the seat originally configured for this finishing place.
        if (!destination) {
          destination = state.destinations.find(item =>
            Number(item.autoSelectPlace) === Number(player.place)
          );
        }

        this.$set(
          this.manualAdvanceAssignments,
          String(player.playerId),
          destination ? Number(destination.seatId) : null
        );
      }
    },

    async loadManualAdvancement(stageMatchId) {
      this.manualAdvanceLoading = true;
      this.manualAdvanceError = '';
      this.manualAdvanceMessage = '';

      try {
        const res = await axios.get(
          'http://localhost:5000/api/match/' + encodeURIComponent(stageMatchId) + '/advancement',
          { headers: this.authHeaders() }
        );
        this.applyManualAdvancementState(res.data);
      } catch (e) {
        console.error(e);
        this.manualAdvanceState = null;
        this.manualAdvanceAssignments = {};
        if (e.response && e.response.status === 401) {
          this.manualAdvanceError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.status === 403) {
          this.manualAdvanceError = 'You do not have permission to manage advancement for this tournament.';
        } else if (e.response && e.response.status === 404) {
          this.manualAdvanceError = 'The advancement API endpoint was not found. Update MatchResultController.cs and restart PoolApi.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.manualAdvanceError = e.response.data.message;
        } else {
          this.manualAdvanceError = 'Could not load destination pools.';
        }
      } finally {
        this.manualAdvanceLoading = false;
      }
    },

    async saveManualAdvancement() {
      if (!this.matchEditAllowed || !this.selectedIsPool || !this.manualAdvanceState || this.savingManualAdvance) return;
      if (!this.manualAdvanceState.stageComplete) {
        this.manualAdvanceError = 'Finish all matches in this pool before choosing the destination pools.';
        return;
      }

      const destinations = Array.isArray(this.manualAdvanceState.destinations)
        ? this.manualAdvanceState.destinations
        : [];
      const advancingPlayers = this.manualAdvanceEligiblePlayers();
      if (!destinations.length) {
        this.manualAdvanceError = 'No next-stage pools are configured for this pool.';
        return;
      }
      if (!advancingPlayers.length) {
        this.manualAdvanceError = 'The advancing players could not be determined yet.';
        return;
      }

      const destinationToPlayer = {};
      for (const player of advancingPlayers) {
        const selectedSeatId = this.manualAdvanceAssignments[String(player.playerId)];
        if (selectedSeatId === null || selectedSeatId === undefined || selectedSeatId === '') {
          this.manualAdvanceError = 'Choose a destination pool for every advancing player.';
          return;
        }

        const seatId = Number(selectedSeatId);
        const allowed = this.manualAdvancePoolOptions(player).some(destination => Number(destination.seatId) === seatId);
        if (!allowed) {
          this.manualAdvanceError = 'One of the selected pools is not valid for that player’s next stage.';
          return;
        }
        if (destinationToPlayer[String(seatId)] !== undefined) {
          this.manualAdvanceError = 'Two players cannot use the same destination seat.';
          return;
        }
        destinationToPlayer[String(seatId)] = Number(player.playerId);
      }

      const assignments = advancingPlayers.map(player => ({
        destinationSeatId: Number(this.manualAdvanceAssignments[String(player.playerId)]),
        playerId: Number(player.playerId)
      }));

      this.savingManualAdvance = true;
      this.manualAdvanceError = '';
      this.manualAdvanceMessage = '';
      const sourcePoolId = Number(this.selectedMatchId);

      try {
        const res = await axios.post(
          'http://localhost:5000/api/match/' + encodeURIComponent(sourcePoolId) + '/advancement/manual',
          { assignments: assignments },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        if (res.data && res.data.state) {
          this.applyManualAdvancementState(res.data.state);
        } else {
          await this.loadManualAdvancement(sourcePoolId);
        }

        if (this.currentTournamentId) {
          await this.loadMatches(this.currentTournamentId, true);
          const freshNode = this.findTreeNodeById(sourcePoolId);
          if (freshNode) {
            this.selectedNode = freshNode;
            this.selectedMatchId = sourcePoolId;
            await this.loadMatchDetails(sourcePoolId);
            await this.loadPoolMatrix(freshNode);
          }
        }

        this.manualAdvanceMessage = res.data && res.data.message
          ? res.data.message
          : 'Destination pools saved.';
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.manualAdvanceError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.status === 403) {
          this.manualAdvanceError = 'You do not have permission to manage advancement for this tournament.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.manualAdvanceError = e.response.data.message;
        } else {
          this.manualAdvanceError = 'Could not save destination pools.';
        }
      } finally {
        this.savingManualAdvance = false;
      }
    },

    poolMatrixPairKey(playerA, playerB, ownerPlayerId) {
      const a = Number(playerA);
      const b = Number(playerB);
      const low = Math.min(a, b);
      const high = Math.max(a, b);
      return low + ':' + high + ':' + Number(ownerPlayerId);
    },

    poolMatrixCell(playerId, opponentId) {
      if (Number(playerId) === Number(opponentId)) return null;
      return this.poolMatrixCells[this.poolMatrixPairKey(playerId, opponentId, playerId)] || null;
    },

    poolMatrixStanding(playerId) {
      return this.poolMatrixStandings.find(x => Number(x.playerId) === Number(playerId)) || {
        points: 0,
        score: 0,
        placeLabel: '–'
      };
    },

    poolMatrixCellClass(playerId, opponentId) {
      const cell = this.poolMatrixCell(playerId, opponentId);
      const other = this.poolMatrixCell(opponentId, playerId);
      if (!cell || !other) return {};
      const playTo = Number(cell.playTo);
      const mine = cell.value === '' || cell.value === null || cell.value === undefined ? null : Number(cell.value);
      const theirs = other.value === '' || other.value === null || other.value === undefined ? null : Number(other.value);
      return {
        matrixWinner: Number.isFinite(playTo) && mine === playTo && theirs !== null && theirs !== playTo,
        matrixLoser: Number.isFinite(playTo) && theirs === playTo && mine !== null && mine !== playTo,
        matrixDirty: this.poolMatrixMatchDirty(cell.matchId)
      };
    },

    poolMatrixMatchDirty(matchId) {
      return Object.keys(this.poolMatrixCells).some(key => {
        const cell = this.poolMatrixCells[key];
        if (!cell || Number(cell.matchId) !== Number(matchId)) return false;
        const now = cell.value !== '' && cell.value !== null && cell.value !== undefined ? Number(cell.value) : null;
        const before = cell.originalValue !== '' && cell.originalValue !== null && cell.originalValue !== undefined ? Number(cell.originalValue) : null;
        return now !== before;
      });
    },

    findTreeNodeById(matchId) {
      const wanted = Number(matchId);
      const walk = nodes => {
        for (const node of nodes || []) {
          if (Number(node.matchId) === wanted) return node;
          const found = walk(node.children);
          if (found) return found;
        }
        return null;
      };
      return walk(this.roots);
    },

    async loadPoolMatrix(node) {
      this.poolMatrixLoading = true;
      this.poolMatrixError = '';
      this.poolMatrixMessage = '';
      this.poolMatrixPlayers = [];
      this.poolMatrixMatches = [];
      this.poolMatrixCells = {};

      try {
        const children = node && Array.isArray(node.children) ? node.children : [];
        const childIds = children
          .filter(child => child && child.matchId)
          .map(child => Number(child.matchId));

        if (!childIds.length) {
          this.poolMatrixError = 'This pool has no playable matches.';
          return;
        }

        const responses = await Promise.all(childIds.map(matchId =>
          axios.get(
            'http://localhost:5000/api/match/' + encodeURIComponent(matchId),
            { headers: this.authHeaders() }
          )
        ));

        const matches = [];
        const playerMap = new Map();
        const cells = {};

        for (const response of responses) {
          const details = this.normalizeMatchDetails(response.data);
          const assigned = (details.seats || []).filter(seat =>
            seat.playerId !== null && seat.playerId !== undefined
          );

          if (!details.individualMatch || assigned.length !== 2) continue;

          const matrixMatch = Object.assign({}, details, { seats: assigned });
          matches.push(matrixMatch);

          for (const seat of assigned) {
            const id = Number(seat.playerId);
            if (!playerMap.has(id)) {
              playerMap.set(id, {
                playerId: id,
                playerName: seat.playerName || ('Player #' + id)
              });
            }
          }

          const first = assigned[0];
          const second = assigned[1];
          for (const seat of assigned) {
            const opponent = Number(seat.playerId) === Number(first.playerId) ? second : first;
            const key = this.poolMatrixPairKey(first.playerId, second.playerId, seat.playerId);
            cells[key] = {
              matchId: details.matchId,
              matchName: details.matchName,
              seatId: seat.seatId,
              playerId: Number(seat.playerId),
              opponentId: Number(opponent.playerId),
              value: seat.resultPoints === undefined ? null : seat.resultPoints,
              originalValue: seat.resultPoints === undefined ? null : seat.resultPoints,
              originalPlace: seat.resultMatchPlace === undefined ? null : seat.resultMatchPlace,
              playFrom: details.matchRules ? details.matchRules.playFrom : null,
              playTo: details.matchRules ? details.matchRules.playTo : null
            };
          }
        }

        this.poolMatrixMatches = matches.sort((a, b) => Number(a.matchId) - Number(b.matchId));
        this.poolMatrixPlayers = [...playerMap.values()].sort((a, b) =>
          a.playerName.localeCompare(b.playerName, undefined, { numeric: true })
        );
        this.poolMatrixCells = cells;

        if (!this.poolMatrixMatches.length) {
          this.poolMatrixError = 'This pool does not have any two-player matches with assigned players yet.';
        }
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.poolMatrixError = 'You are not logged in, or your login has expired.';
        } else {
          this.poolMatrixError = 'Could not load the pool result table.';
        }
      } finally {
        this.poolMatrixLoading = false;
      }
    },

    onPoolMatrixInput(playerId, opponentId, rawValue) {
      if (!this.matchEditAllowed) return;
      const cell = this.poolMatrixCell(playerId, opponentId);
      const other = this.poolMatrixCell(opponentId, playerId);
      if (!cell || !other) return;

      this.poolMatrixError = '';
      this.poolMatrixMessage = '';

      if (rawValue === '' || rawValue === null || rawValue === undefined) {
        this.$set(cell, 'value', null);
        this.$set(other, 'value', null);
        return;
      }

      const value = Number(rawValue);
      if (!Number.isFinite(value)) return;

      this.$set(cell, 'value', value);

      const playTo = Number(cell.playTo);
      if (Number.isFinite(playTo)) {
        if (value !== playTo) {
          this.$set(other, 'value', playTo);
        } else if (Number(other.value) === playTo) {
          this.$set(other, 'value', null);
        }
      }
    },

    resetPoolMatrixChanges() {
      for (const key of Object.keys(this.poolMatrixCells)) {
        const cell = this.poolMatrixCells[key];
        this.$set(cell, 'value', cell.originalValue);
      }
      this.poolMatrixError = '';
      this.poolMatrixMessage = '';
    },

    validatePoolMatrixMatch(match) {
      if (!match || !Array.isArray(match.seats) || match.seats.length !== 2) {
        return { action: 'skip' };
      }

      const a = match.seats[0];
      const b = match.seats[1];
      const ac = this.poolMatrixCell(a.playerId, b.playerId);
      const bc = this.poolMatrixCell(b.playerId, a.playerId);
      if (!ac || !bc) return { action: 'skip' };

      const dirty = this.poolMatrixMatchDirty(match.matchId);
      if (!dirty) return { action: 'skip' };

      const emptyA = ac.value === '' || ac.value === null || ac.value === undefined;
      const emptyB = bc.value === '' || bc.value === null || bc.value === undefined;
      const hadOriginal = ac.originalValue !== null || bc.originalValue !== null ||
        ac.originalPlace !== null || bc.originalPlace !== null;

      if (emptyA && emptyB) {
        return hadOriginal
          ? { action: 'clear', matchId: match.matchId, matchName: match.matchName }
          : { action: 'skip' };
      }

      if (emptyA || emptyB) {
        return { error: (match.matchName || ('Match #' + match.matchId)) + ': enter both final scores, or clear both cells.' };
      }

      const aScore = Number(ac.value);
      const bScore = Number(bc.value);
      if (!Number.isInteger(aScore) || !Number.isInteger(bScore)) {
        return { error: (match.matchName || ('Match #' + match.matchId)) + ': scores must be whole numbers.' };
      }

      const playFrom = Number(match.matchRules && match.matchRules.playFrom);
      const playTo = Number(match.matchRules && match.matchRules.playTo);
      if (!Number.isFinite(playTo)) {
        return { error: (match.matchName || ('Match #' + match.matchId)) + ': PlayTo is not configured.' };
      }

      if (Number.isFinite(playFrom)) {
        const min = Math.min(playFrom, playTo);
        const max = Math.max(playFrom, playTo);
        if (aScore < min || aScore > max || bScore < min || bScore > max) {
          return { error: (match.matchName || ('Match #' + match.matchId)) + ': scores must be between PlayFrom (' + playFrom + ') and PlayTo (' + playTo + ').' };
        }
      }

      const aWinner = aScore === playTo;
      const bWinner = bScore === playTo;
      if (aWinner === bWinner) {
        return { error: (match.matchName || ('Match #' + match.matchId)) + ': exactly one player must finish on PlayTo (' + playTo + ').' };
      }

      return {
        action: 'save',
        matchId: match.matchId,
        matchName: match.matchName,
        payload: {
          matchId: match.matchId,
          results: [
            {
              seatId: a.seatId,
              resultMatchPlace: aWinner ? 1 : 2,
              resultPoints: aScore
            },
            {
              seatId: b.seatId,
              resultMatchPlace: bWinner ? 1 : 2,
              resultPoints: bScore
            }
          ]
        }
      };
    },

    async savePoolMatrix() {
      if (!this.matchEditAllowed || !this.selectedIsPool || this.savingPoolMatrix) return;

      const changes = [];
      for (const match of this.poolMatrixMatches) {
        const check = this.validatePoolMatrixMatch(match);
        if (check.error) {
          this.poolMatrixError = check.error;
          this.poolMatrixMessage = '';
          return;
        }
        if (check.action !== 'skip') changes.push(check);
      }

      if (!changes.length) {
        this.poolMatrixMessage = 'No pool results have changed.';
        this.poolMatrixError = '';
        return;
      }

      this.savingPoolMatrix = true;
      this.poolMatrixError = '';
      this.poolMatrixMessage = '';

      const poolMatchId = this.selectedMatchId;
      let saved = 0;
      let cleared = 0;
      let latestAdvancement = null;

      try {
        for (const change of changes) {
          let res;
          if (change.action === 'clear') {
            res = await axios.delete(
              'http://localhost:5000/api/match/' + encodeURIComponent(change.matchId) + '/result',
              { headers: this.authHeaders() }
            );
            cleared++;
          } else {
            res = await axios.post(
              'http://localhost:5000/api/match/result',
              change.payload,
              {
                headers: Object.assign(
                  { 'Content-Type': 'application/json; charset=utf-8' },
                  this.authHeaders()
                )
              }
            );
            saved++;
          }

          if (res && res.data && res.data.advancement) {
            latestAdvancement = res.data.advancement;
          }
        }

        if (this.currentTournamentId) {
          await this.loadMatches(this.currentTournamentId, true);
        }

        const freshNode = this.findTreeNodeById(poolMatchId);
        if (freshNode) {
          this.selectedNode = freshNode;
          this.selectedMatchId = poolMatchId;
          await this.loadMatchDetails(poolMatchId);
          await this.loadPoolMatrix(freshNode);
          await this.loadManualAdvancement(poolMatchId);
        }

        this.lastAdvancement = latestAdvancement;
        const pieces = [];
        if (saved) pieces.push(saved + ' match' + (saved === 1 ? '' : 'es') + ' saved');
        if (cleared) pieces.push(cleared + ' match' + (cleared === 1 ? '' : 'es') + ' cleared');
        this.poolMatrixMessage = pieces.join(', ') + '.';
      } catch (e) {
        console.error(e);
        if (e.response && e.response.status === 401) {
          this.poolMatrixError = 'You are not logged in, or your login has expired.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.poolMatrixError = e.response.data.message;
        } else {
          this.poolMatrixError = 'Could not save all pool results. Any matches saved before the error remain saved.';
        }
      } finally {
        this.savingPoolMatrix = false;
      }
    },

    onSearchInput() {
      if ((this.searchText || '').trim()) this.expandAll();
    },

    formatTournamentDateTime(value) {
      if (!value) return 'date/time unknown';
      const raw = String(value);
      const match = raw.match(/^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})/);
      if (match) {
        return match[3] + '/' + match[2] + '/' + match[1] + ' ' + match[4] + ':' + match[5];
      }
      return raw;
    },

    async loadTournamentOptions(reloadCurrent) {
      this.tournamentsLoading = true;
      this.tournamentListError = '';

      try {
        const res = await axios.get(
          'http://localhost:5000/api/matches/tournaments',
          { headers: this.authHeaders() }
        );

        this.tournaments = res.data && Array.isArray(res.data.tournaments)
          ? res.data.tournaments
          : [];

        const last = localStorage.getItem('lastTournamentId');
        const current = this.currentTournamentId ? String(this.currentTournamentId) : '';
        const hasId = (id) => !!id && this.tournaments.some(t => String(t.tournamentId) === String(id));

        if (hasId(current)) {
          this.selectedTournamentId = current;
        } else if (hasId(last)) {
          this.selectedTournamentId = String(last);
        } else if (this.tournaments.length === 1) {
          this.selectedTournamentId = String(this.tournaments[0].tournamentId);
        } else {
          this.selectedTournamentId = '';
        }

        if (this.selectedTournamentId && (!this.currentTournamentId || reloadCurrent)) {
          await this.loadMatches(this.selectedTournamentId, !!this.currentTournamentId);
        }
      } catch (e) {
        console.error(e);
        this.tournaments = [];
        this.selectedTournamentId = '';
        if (e.response && e.response.status === 401) {
          this.tournamentListError = 'Unauthorized. Please log in again.';
        } else if (e.response && e.response.data && e.response.data.message) {
          this.tournamentListError = e.response.data.message;
        } else {
          this.tournamentListError = 'Could not load tournaments.';
        }
      } finally {
        this.tournamentsLoading = false;
      }
    },

    async loadSelectedTournament() {
      if (!this.selectedTournamentId) return;
      await this.loadMatches(this.selectedTournamentId, false);
    },

    async selectMatch(node) {
      if (!node || !node.matchId) return;
      this.selectedNode = node;
      this.selectedMatchId = node.matchId;
      this.lastAdvancement = null;
      this.venueError = '';
      this.venueMessage = '';
      this.resetPoolMatrix();
      this.resetManualAdvancement();
      this.resetStageAdvancement();
      await this.loadMatchDetails(node.matchId);
      if (this.selectedIsPool) {
        await this.loadPoolMatrix(node);
        await this.loadManualAdvancement(node.matchId);
      } else if (this.selectedIsStageWithPools) {
        await this.loadStageAdvancement(node.matchId);
      }
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
          resultPoints: s.resultPoints === undefined ? null : s.resultPoints,
          mmr: s.mmr === undefined ? null : s.mmr
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
        const scheduledFrom = this.matchDetails.schedule ? this.matchDetails.schedule.fromTime : null;
        this.selectedScheduleDate = this.toDateInput(scheduledFrom) || this.todayDateInput();
        this.selectedScheduleFrom = this.toTimeInput(scheduledFrom);
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

    todayDateInput() {
      const now = new Date();
      const year = now.getFullYear();
      const month = String(now.getMonth() + 1).padStart(2, '0');
      const day = String(now.getDate()).padStart(2, '0');
      return year + '-' + month + '-' + day;
    },

    toDateInput(value) {
      if (!value) return '';
      const raw = String(value);
      const match = raw.match(/^(\d{4})-(\d{2})-(\d{2})/);
      if (!match) return '';
      // Legacy time-only schedules used this fixed placeholder date.
      if (match[1] === '2000' && match[2] === '01' && match[3] === '01') return '';
      return match[1] + '-' + match[2] + '-' + match[3];
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

    formatDateDisplay(value) {
      const date = this.toDateInput(value);
      if (!date) return '';
      const parts = date.split('-');
      return parts[2] + '/' + parts[1] + '/' + parts[0];
    },

    formatTimeDisplay(value) {
      const time = this.toTimeInput(value);
      return time || 'Not set';
    },

    scheduleDisplay(schedule) {
      if (!schedule || !schedule.fromTime) return 'Date/time not set';
      const date = this.formatDateDisplay(schedule.fromTime);
      const from = this.formatTimeDisplay(schedule.fromTime);
      if (date && from) return date + ' · Starts ' + from;
      return from ? 'Starts ' + from : 'Date/time not set';
    },

    scheduleDateTimeForApi(dateValue, timeValue) {
      const date = (dateValue || '').trim();
      const time = (timeValue || '').trim();
      if (!date || !time) return null;
      if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return null;
      if (!/^\d{2}:\d{2}$/.test(time)) return null;
      return date + 'T' + time + ':00';
    },

    timeToMinutes(value) {
      const match = String(value || '').match(/^(\d{2}):(\d{2})$/);
      if (!match) return null;
      return Number(match[1]) * 60 + Number(match[2]);
    },

    async loadMatchEditAccess(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0) return;

      this.editAccessLoading = true;
      this.matchEditAllowed = false;
      this.isTournamentAdmin = false;
      this.isTournamentMatchEditor = false;
      this.editorError = '';
      this.editorMessage = '';

      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/match-editors/access',
          { headers: this.authHeaders() }
        );
        const access = res.data && res.data.access ? res.data.access : {};
        this.isTournamentAdmin = !!access.isAdmin;
        this.isTournamentMatchEditor = !!access.isMatchEditor;
        this.matchEditAllowed = !!access.canEditMatches;

        if (this.isTournamentAdmin) {
          await this.loadMatchEditors(tid);
        } else {
          this.editors = [];
        }
      } catch (e) {
        console.error(e);
        this.matchEditAllowed = false;
        this.isTournamentAdmin = false;
        this.isTournamentMatchEditor = false;
        this.editors = [];
        this.editorError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not load match-editor permissions.';
      } finally {
        this.editAccessLoading = false;
      }
    },

    async loadMatchEditors(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0 || !this.isTournamentAdmin) return;

      this.editorsLoading = true;
      this.editorError = '';
      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/match-editors',
          { headers: this.authHeaders() }
        );
        this.editors = res.data && Array.isArray(res.data.editors) ? res.data.editors : [];
      } catch (e) {
        console.error(e);
        this.editors = [];
        this.editorError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not load match editors.';
      } finally {
        this.editorsLoading = false;
      }
    },

    async addMatchEditor() {
      const username = (this.newEditorUsername || '').trim();
      if (!username || !this.currentTournamentId || !this.isTournamentAdmin) return;

      this.savingEditor = true;
      this.editorError = '';
      this.editorMessage = '';
      try {
        const res = await axios.post(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) + '/match-editors',
          { userName: username },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );
        this.newEditorUsername = '';
        await this.loadMatchEditors(this.currentTournamentId);
        this.editorMessage = (res.data && res.data.message) ? res.data.message : 'Match editor added.';
      } catch (e) {
        console.error(e);
        this.editorError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not add the match editor.';
      } finally {
        this.savingEditor = false;
      }
    },

    async removeMatchEditor(editor) {
      if (!editor || !editor.userId || !this.currentTournamentId || !this.isTournamentAdmin) return;
      if (!confirm('Remove match-edit access for "' + (editor.userName || editor.userId) + '"?')) return;

      this.savingEditor = true;
      this.editorError = '';
      this.editorMessage = '';
      try {
        await axios.delete(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) + '/match-editors/' + encodeURIComponent(editor.userId),
          { headers: this.authHeaders() }
        );
        await this.loadMatchEditors(this.currentTournamentId);
        this.editorMessage = 'Match editor removed.';
      } catch (e) {
        console.error(e);
        this.editorError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not remove the match editor.';
      } finally {
        this.savingEditor = false;
      }
    },

    async loadPlayerFinderPlayers(tournamentId, preserveSelection) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0) {
        this.playerFinderPlayers = [];
        this.selectedFinderPlayer = null;
        this.playerFinderMatches = [];
        this.playerFinderError = '';
        return;
      }

      this.playerFinderLoading = true;
      this.playerFinderError = '';
      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/player-match-search/players',
          { headers: this.authHeaders() }
        );
        this.playerFinderPlayers = res.data && Array.isArray(res.data.players) ? res.data.players : [];

        if (!preserveSelection) {
          this.playerFinderQuery = '';
          this.selectedFinderPlayer = null;
          this.playerFinderMatches = [];
        } else if (this.selectedFinderPlayer) {
          const selectedId = Number(this.selectedFinderPlayer.playerId);
          const refreshed = this.playerFinderPlayers.find(x => Number(x.playerId) === selectedId);
          if (refreshed) {
            this.selectedFinderPlayer = refreshed;
            await this.loadSelectedPlayerMatches(refreshed);
          } else {
            this.selectedFinderPlayer = null;
            this.playerFinderMatches = [];
          }
        }
      } catch (e) {
        console.error(e);
        this.playerFinderPlayers = [];
        this.playerFinderError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not load players for match search.';
      } finally {
        this.playerFinderLoading = false;
      }
    },

    onPlayerFinderQueryInput() {
      if (!this.selectedFinderPlayer) return;
      const query = (this.playerFinderQuery || '').trim().toLowerCase();
      const selectedName = (this.selectedFinderPlayer.playerName || '').trim().toLowerCase();
      if (query !== selectedName) {
        this.selectedFinderPlayer = null;
        this.playerFinderMatches = [];
      }
    },

    async selectPlayerForMatches(player) {
      if (!player || !player.playerId) return;
      this.selectedFinderPlayer = player;
      this.playerFinderQuery = player.playerName || ('Player #' + player.playerId);
      await this.loadSelectedPlayerMatches(player);
    },

    async loadSelectedPlayerMatches(player) {
      const tid = Number(this.currentTournamentId);
      const playerId = Number(player && player.playerId);
      if (!Number.isFinite(tid) || !Number.isFinite(playerId)) return;

      this.playerFinderMatchesLoading = true;
      this.playerFinderError = '';
      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) +
            '/player-match-search/players/' + encodeURIComponent(playerId) + '/matches',
          { headers: this.authHeaders() }
        );
        this.playerFinderMatches = res.data && Array.isArray(res.data.matches) ? res.data.matches : [];
      } catch (e) {
        console.error(e);
        this.playerFinderMatches = [];
        this.playerFinderError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : "Could not load the player's matches.";
      } finally {
        this.playerFinderMatchesLoading = false;
      }
    },

    clearPlayerFinder() {
      this.playerFinderQuery = '';
      this.selectedFinderPlayer = null;
      this.playerFinderMatches = [];
      this.playerFinderError = '';
    },

    playerMatchScheduleText(match) {
      if (!match || !match.fromTime) return 'Time not set';
      const date = this.formatDateDisplay(match.fromTime);
      const time = this.formatTimeDisplay(match.fromTime);
      if (date && time) return date + ' · ' + time;
      return date || time || 'Time not set';
    },

    async openPlayerMatch(match) {
      if (!match || !match.matchId) return;
      const node = this.findTreeNodeById(Number(match.matchId));
      if (!node) {
        this.playerFinderError = 'This match is not currently available in the tournament tree.';
        return;
      }
      await this.selectMatch(node);
      const panel = document.querySelector('.matchResultPanel');
      if (panel && typeof panel.scrollIntoView === 'function') {
        panel.scrollIntoView({ behavior: 'smooth', block: 'start' });
      }
    },

    async loadTournamentPlayers(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0 || !this.isTournamentAdmin) {
        this.tournamentPlayers = [];
        this.playerNameError = '';
        this.playerNameMessage = '';
        return;
      }

      this.playerNamesLoading = true;
      this.playerNameError = '';
      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/players',
          { headers: this.authHeaders() }
        );

        const players = res.data && Array.isArray(res.data.players) ? res.data.players : [];
        this.tournamentPlayers = players.map(player => ({
          playerId: Number(player.playerId),
          playerName: player.playerName || ('Player #' + player.playerId),
          editName: player.playerName || ('Player #' + player.playerId)
        }));
      } catch (e) {
        console.error(e);
        this.tournamentPlayers = [];
        this.playerNameError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not load unregistered players.';
      } finally {
        this.playerNamesLoading = false;
      }
    },

    playerNameChanged(player) {
      if (!player) return false;
      const next = (player.editName || '').trim();
      const current = (player.playerName || '').trim();
      return next.length > 0 && next !== current;
    },

    async renameTournamentPlayer(player) {
      if (!player || !this.currentTournamentId || !this.isTournamentAdmin) return;

      const name = (player.editName || '').trim();
      if (!name) {
        this.playerNameError = 'Player name cannot be empty.';
        this.playerNameMessage = '';
        return;
      }
      if (name.length > 255) {
        this.playerNameError = 'Player name cannot be longer than 255 characters.';
        this.playerNameMessage = '';
        return;
      }
      if (!this.playerNameChanged(player)) return;

      const playerId = Number(player.playerId);
      const selected = this.selectedMatchId;
      this.savingPlayerNameId = playerId;
      this.playerNameError = '';
      this.playerNameMessage = '';

      try {
        const res = await axios.put(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) +
            '/players/' + encodeURIComponent(playerId) + '/name',
          { name: name },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        const message = res.data && res.data.message ? res.data.message : 'Player name saved.';

        // Refresh every view that displays Player.Name: tree, pool matrix, match details and draw list.
        await this.loadMatches(this.currentTournamentId, true);

        if (selected) {
          const freshNode = this.findTreeNodeById(selected);
          if (freshNode) {
            this.selectedNode = freshNode;
            this.selectedMatchId = selected;
            await this.loadMatchDetails(selected);
            if (this.selectedIsPool) {
              await this.loadPoolMatrix(freshNode);
            }
          }
        }

        this.playerNameMessage = message;
      } catch (e) {
        console.error(e);
        this.playerNameError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not save the player name.';
      } finally {
        this.savingPlayerNameId = null;
      }
    },

    async loadPlayerDraw(tournamentId) {
      const tid = Number(tournamentId);
      if (!Number.isFinite(tid) || tid <= 0 || !this.isTournamentAdmin) {
        this.drawStatus = null;
        this.drawRegistrations = [];
        this.drawSlots = [];
        this.drawAssignments = {};
        return;
      }

      this.drawLoading = true;
      this.drawError = '';
      this.drawMessage = '';
      try {
        const res = await axios.get(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(tid) + '/player-draw',
          { headers: this.authHeaders() }
        );

        this.drawStatus = res.data && res.data.status ? res.data.status : null;
        this.drawRegistrations = res.data && Array.isArray(res.data.registrations) ? res.data.registrations : [];
        this.drawSlots = res.data && Array.isArray(res.data.slots) ? res.data.slots : [];

        const next = {};
        for (const registration of this.drawRegistrations) {
          this.$set(next, String(registration.userId), registration.playerId ? Number(registration.playerId) : '');
        }
        this.drawAssignments = next;
      } catch (e) {
        console.error(e);
        this.drawStatus = null;
        this.drawRegistrations = [];
        this.drawSlots = [];
        this.drawAssignments = {};
        this.drawError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not load the manual player draw.';
      } finally {
        this.drawLoading = false;
      }
    },

    drawSlotTakenByOther(playerId, userId) {
      const target = Number(playerId);
      const owner = Number(userId);
      return Object.keys(this.drawAssignments).some(key => {
        const selected = Number(this.drawAssignments[key]);
        return Number(key) !== owner && selected > 0 && selected === target;
      });
    },

    async savePlayerDraw() {
      if (!this.currentTournamentId || !this.isTournamentAdmin || !this.drawStatus || !this.drawStatus.usePlayerDraw) return;

      const assignments = [];
      const seenPlayers = new Set();
      for (const registration of this.drawRegistrations) {
        const playerId = Number(this.drawAssignments[String(registration.userId)]);
        if (!Number.isFinite(playerId) || playerId <= 0) continue;
        if (seenPlayers.has(playerId)) {
          this.drawError = 'The same Player:n place cannot be assigned to more than one registered player.';
          return;
        }
        seenPlayers.add(playerId);
        assignments.push({ userId: Number(registration.userId), playerId: playerId });
      }

      this.savingDraw = true;
      this.drawError = '';
      this.drawMessage = '';
      try {
        const res = await axios.post(
          'http://localhost:5000/api/tournament/' + encodeURIComponent(this.currentTournamentId) + '/player-draw',
          { assignments: assignments },
          {
            headers: Object.assign(
              { 'Content-Type': 'application/json; charset=utf-8' },
              this.authHeaders()
            )
          }
        );

        const savedMessage = res.data && res.data.message ? res.data.message : 'Manual draw saved.';

        // Refresh the tree because Player:n names may now have changed to registered player names.
        const selected = this.selectedMatchId;
        await this.loadMatches(this.currentTournamentId, true);
        if (selected) this.selectedMatchId = selected;
        this.drawMessage = savedMessage;
      } catch (e) {
        console.error(e);
        this.drawError = (e.response && e.response.data && e.response.data.message)
          ? e.response.data.message
          : 'Could not save the manual player draw.';
      } finally {
        this.savingDraw = false;
      }
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
      const dateInput = (this.selectedScheduleDate || '').trim() || null;
      const fromInput = (this.selectedScheduleFrom || '').trim() || null;
      const fromMinutes = fromInput ? this.timeToMinutes(fromInput) : null;

      if (fromInput && fromMinutes === null) {
        this.venueError = 'Enter a valid start time.';
        this.savingVenue = false;
        return;
      }

      if (fromInput && !dateInput) {
        this.venueError = 'Choose a date for the pool.';
        this.savingVenue = false;
        return;
      }

      const fromTime = fromInput ? this.scheduleDateTimeForApi(dateInput, fromInput) : null;
      if (fromInput && !fromTime) {
        this.venueError = 'Enter a valid date and start time.';
        this.savingVenue = false;
        return;
      }

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
      if (!this.matchEditAllowed || !seat || this.activeSeats.length !== 2) return;
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
      if (!this.matchEditAllowed || !seat || this.activeSeats.length !== 2) return;
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
      if (!this.matchEditAllowed || !this.matchDetails || !this.matchDetails.matchId) return;

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
      if (!this.matchEditAllowed) {
        this.resultError = 'You do not have permission to edit match results for this tournament.';
        this.resultMessage = '';
        return;
      }

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
        const mmrChanges = res.data && res.data.advancement && Array.isArray(res.data.advancement.mmrChanges)
          ? res.data.advancement.mmrChanges
          : [];
        const mmrMessage = res.data && res.data.advancement && res.data.advancement.mmrMessage
          ? res.data.advancement.mmrMessage
          : '';

        const parts = [message];
        if (advanced.length) {
          const moved = advanced.map(x =>
            (x.playerName || ('Player #' + x.playerId)) +
            ' (place ' + x.place + ') → ' +
            (x.destinationMatchName || ('match #' + x.destinationMatchId))
          );
          parts.push(moved.join('; '));
        }
        if (mmrChanges.length) {
          const rating = mmrChanges.map(x => {
            const delta = Number(x.delta || 0);
            return (x.playerName || ('Player #' + x.playerId)) +
              ' ' + (delta >= 0 ? '+' : '') + delta + ' MMR (' + x.before + ' → ' + x.after + ')';
          });
          parts.push('MMR: ' + rating.join('; '));
        } else if (mmrMessage) {
          parts.push(mmrMessage);
        }
        this.resultMessage = parts.join(' ');
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
        this.selectedTournamentId = String(tid);
        await this.loadMatchEditAccess(tid);
        await this.loadTournamentPlayers(tid);
        await this.loadPlayerFinderPlayers(tid, !!preserveSelection);
        await this.loadPlayerDraw(tid);
        await this.loadPoolSchedules(tid);
        await this.loadVenues(tid);
        if (!preserveSelection) {
          this.selectedMatchId = null;
          this.selectedNode = null;
          this.selectedVenueId = null;
          this.selectedScheduleDate = this.todayDateInput();
          this.selectedScheduleFrom = '';
          this.matchDetails = null;
          this.resultError = '';
          this.resultMessage = '';
          this.lastAdvancement = null;
          this.resetPoolMatrix();
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
    this.loadTournamentOptions(false);
  }
});
