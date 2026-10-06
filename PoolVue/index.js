/* =========================
   MMR ruler board
   ========================= */
new Vue({
  el: '#app',
  data: function () {
    return {
      players: [],
      loading: false,
      error: ''
    };
  },
  methods: {
    loadLeaderboard: function () {
      this.loading = true;
      this.error = '';

      axios.get('http://localhost:5000/api/player/leaderboard', {
        headers: { 'Content-type': 'application/json; charset=utf-8' }
      })
      .then(function (response) {
        this.players = response.data && Array.isArray(response.data.players)
          ? response.data.players
          : [];
      }.bind(this))
      .catch(function (error) {
        this.players = [];
        this.error = error.response && error.response.data && error.response.data.message
          ? error.response.data.message
          : 'Could not load the MMR ruler board.';
      }.bind(this))
      .then(function () {
        this.loading = false;
      }.bind(this));
    },

    rankClass: function (rank) {
      if (rank === 1) return 'rank-first';
      if (rank === 2) return 'rank-second';
      if (rank === 3) return 'rank-third';
      return '';
    }
  },
  mounted: function () {
    this.loadLeaderboard();
  }
});

/* =========================
   Login panel
   ========================= */
new Vue({
  el: '#vueformapp',
  data: function () {
    return {
      username: '',
      password: '',
      message: ''
    };
  },
  methods: {
    apipost: function () {
      this.message = '';

      axios.post('http://localhost:5000/api/token', {
        username: this.username,
        password: this.password
      }, {
        headers: { 'Content-type': 'application/json; charset=utf-8' }
      })
      .then(function (response) {
        var auth = 'Bearer ' + response.data.token;
        localStorage.setItem('token', auth);
        axios.defaults.headers.common['Authorization'] = auth;

        if (typeof dash !== 'undefined') {
          dash.authToken = auth;
          dash.apiError = '';
        }

        this.message = 'Login successful. You can now create tournaments.';
      }.bind(this))
      .catch(function (error) {
        this.message = error.response && error.response.status === 401
          ? 'Login failed. Check username and password.'
          : 'Login failed: ' + (error.message || String(error));
      }.bind(this));
    }
  }
});

/* =========================
   Last tournament panel
   ========================= */
var match = new Vue({
  el: '#vuematch',
  data: {
    TurnamentId: localStorage.getItem('lastTournamentId') || 'None yet'
  }
});

/* =========================
   Tournament builder
   ========================= */
var dash = new Vue({
  el: '#dashboard',
  data: {
    authToken: localStorage.getItem('token') || '',
    activePreset: 'custom',
    uiMessage: '',
    apiError: '',
    creating: false,
    createdTournamentId: localStorage.getItem('lastTournamentId') || '',
    createdJoinCode: localStorage.getItem('lastTournamentJoinCode') || '',
    nextKey: 2,
    formdata: {
      errors: [],
      tournamentname: 'New tournament',
      tournamentplayers: 8,
      privateTournament: false,
      rundearray: [
        {
          _key: 1,
          navn: 'Stage 1',
          NbPlayers: 8,
          selected: 'pool',
          BestOf: 1,
          puljesize: 4,
          distributeEvenly: true,
          poolSizes: [4, 4],
          valsArray: [-1, -1, -1, -1],
          playstyle: 'roundrobin',
          swissRounds: 2
        }
      ]
    }
  },

  computed: {
    tokenAvailable: function () {
      return !!this.authToken;
    },
    isValid: function () {
      return this.formdata.errors.length === 0;
    }
  },

  methods: {
    newKey: function () {
      return this.nextKey++;
    },

    isPowerOfTwo: function (value) {
      value = Number(value);
      return value >= 2 && Number.isInteger(value) && (value & (value - 1)) === 0;
    },

    knockoutName: function (players) {
      players = Number(players);
      if (players === 2) return 'Final';
      if (players === 4) return 'Semi-finals';
      if (players === 8) return 'Quarter-finals';
      if (players === 16) return 'Round of 16';
      if (players === 32) return 'Round of 32';
      return 'Knockout - ' + players + ' players';
    },

    recommendedSwissRounds: function (players) {
      players = Math.max(2, Number(players) || 2);
      return Math.max(1, Math.ceil(Math.log(players) / Math.log(2)));
    },

    calculateEvenPoolSizes: function (players, preferredSize) {
      players = Number(players);
      preferredSize = Number(preferredSize);

      if (!Number.isInteger(players) || players < 2 || !Number.isInteger(preferredSize) || preferredSize < 2) {
        return [];
      }

      // Treat "players per group" as the preferred/max size, then spread
      // players as evenly as possible. Avoid one-player groups.
      var groups = Math.ceil(players / preferredSize);
      while (groups > 1 && Math.floor(players / groups) < 2) groups--;

      var baseSize = Math.floor(players / groups);
      var remainder = players % groups;
      var sizes = [];

      for (var i = 0; i < groups; i++) {
        sizes.push(baseSize + (i < remainder ? 1 : 0));
      }

      return sizes;
    },

    getPoolSizes: function (round) {
      if (!round || round.selected !== 'pool') return [];

      var players = Number(round.NbPlayers);
      var preferredSize = Number(round.puljesize);
      if (!Number.isInteger(players) || players < 2 || !Number.isInteger(preferredSize) || preferredSize < 2) return [];

      if (round.distributeEvenly !== false) {
        return this.calculateEvenPoolSizes(players, preferredSize);
      }

      if (players % preferredSize !== 0) return [];
      var groups = players / preferredSize;
      var sizes = [];
      for (var i = 0; i < groups; i++) sizes.push(preferredSize);
      return sizes;
    },

    playersAtPlace: function (round, place) {
      var sizes = this.getPoolSizes(round);
      var count = 0;
      for (var i = 0; i < sizes.length; i++) {
        if (sizes[i] >= place) count++;
      }
      return count;
    },

    updatePoolSizes: function (round) {
      if (!round) return;
      if (round.selected === 'pool') {
        this.$set(round, 'poolSizes', this.getPoolSizes(round));
      } else {
        this.$set(round, 'poolSizes', []);
      }
    },

    createPoolStage: function (name, players, poolSize, destinationIndex, qualifiersPerPool) {
      var routes = [];
      for (var i = 0; i < poolSize; i++) {
        routes.push(i < qualifiersPerPool ? destinationIndex : -1);
      }

      return {
        _key: this.newKey(),
        navn: name,
        NbPlayers: players,
        selected: 'pool',
        BestOf: 1,
        puljesize: poolSize,
        distributeEvenly: true,
        poolSizes: this.calculateEvenPoolSizes(players, poolSize),
        valsArray: routes,
        playstyle: 'roundrobin',
        swissRounds: this.recommendedSwissRounds(poolSize)
      };
    },

    createKnockoutStage: function (players, stageIndex, hasNextStage) {
      return {
        _key: this.newKey(),
        navn: this.knockoutName(players),
        NbPlayers: players,
        selected: 'knockout',
        BestOf: 1,
        puljesize: 2,
        distributeEvenly: false,
        poolSizes: [],
        valsArray: [hasNextStage ? stageIndex + 1 : 0, -1],
        playstyle: 'roundrobin',
        swissRounds: 1
      };
    },

    buildKnockoutStages: function (startingPlayers, firstIndex) {
      var stages = [];
      var players = Number(startingPlayers);
      var index = Number(firstIndex);

      while (players >= 2) {
        var hasNext = players > 2;
        stages.push(this.createKnockoutStage(players, index, hasNext));
        players = Math.floor(players / 2);
        index++;
      }

      return stages;
    },

    findGroupPreset: function (totalPlayers) {
      var preferredSizes = [4, 3, 5, 6, 8, 2];
      var qualifierPreference = [2, 1, 3, 4, 5, 6, 7];

      for (var s = 0; s < preferredSizes.length; s++) {
        var preferredSize = preferredSizes[s];
        var poolSizes = this.calculateEvenPoolSizes(totalPlayers, preferredSize);
        var groups = poolSizes.length;
        if (groups < 2) continue;

        var minPoolSize = Math.min.apply(null, poolSizes);

        for (var q = 0; q < qualifierPreference.length; q++) {
          var qualifiersPerGroup = qualifierPreference[q];
          // Keep at least one player eliminated in every group for this preset.
          if (qualifiersPerGroup >= minPoolSize) continue;

          var qualifiers = groups * qualifiersPerGroup;
          if (this.isPowerOfTwo(qualifiers)) {
            return {
              poolSize: preferredSize,
              poolSizes: poolSizes,
              groups: groups,
              qualifiersPerGroup: qualifiersPerGroup,
              qualifiers: qualifiers
            };
          }
        }
      }

      return null;
    },

    applyPreset: function (preset, silent) {
      var players = Number(this.formdata.tournamentplayers);
      this.uiMessage = '';
      this.activePreset = preset;

      if (!Number.isInteger(players) || players < 2) {
        this.uiMessage = 'Choose at least 2 starting players first.';
        this.recalculate();
        return;
      }

      if (preset === 'knockout') {
        if (!this.isPowerOfTwo(players)) {
          this.uiMessage = 'Single elimination needs 2, 4, 8, 16, 32… players with the current backend (no byes yet).';
          this.activePreset = 'custom';
          this.recalculate();
          return;
        }

        this.formdata.rundearray = this.buildKnockoutStages(players, 0);
        if (!silent) this.uiMessage = 'Single-elimination structure created automatically.';
      }

      if (preset === 'groups') {
        var setup = this.findGroupPreset(players);
        if (!setup) {
          this.uiMessage = 'I could not make an even groups → knockout preset for this player count. Use Custom or change the number of players.';
          this.activePreset = 'custom';
          this.recalculate();
          return;
        }

        var groupStage = this.createPoolStage(
          'Group stage',
          players,
          setup.poolSize,
          1,
          setup.qualifiersPerGroup
        );
        var knockoutStages = this.buildKnockoutStages(setup.qualifiers, 1);
        this.formdata.rundearray = [groupStage].concat(knockoutStages);

        if (!silent) {
          this.uiMessage = 'Groups distributed as ' + setup.poolSizes.join(' / ') + ' players. Top ' + setup.qualifiersPerGroup + ' from each group advance (' + setup.qualifiers + ' players).';
        }
      }

      if (preset === 'custom') {
        this.formdata.rundearray = [
          {
            _key: this.newKey(),
            navn: 'Stage 1',
            NbPlayers: players,
            selected: 'pool',
            BestOf: 1,
            puljesize: Math.min(4, players),
            distributeEvenly: true,
            poolSizes: [],
            valsArray: [],
            playstyle: 'roundrobin',
            swissRounds: this.recommendedSwissRounds(Math.min(4, players))
          }
        ];
        this.ensureRoutes(0);
        if (!silent) this.uiMessage = 'Custom setup started. Add stages and choose the routes yourself.';
      }

      this.recalculate();
    },

    onTournamentPlayersChange: function () {
      if (this.activePreset === 'groups' || this.activePreset === 'knockout') {
        this.applyPreset(this.activePreset, true);
      } else {
        this.recalculate();
      }
    },

    addrunde: function () {
      this.activePreset = 'custom';
      this.uiMessage = '';

      var newIndex = this.formdata.rundearray.length;
      var previous = this.formdata.rundearray[newIndex - 1];

      var stage = {
        _key: this.newKey(),
        navn: 'Stage ' + (newIndex + 1),
        NbPlayers: 0,
        selected: 'knockout',
        BestOf: 1,
        puljesize: 2,
        distributeEvenly: false,
        poolSizes: [],
        valsArray: [0, -1],
        playstyle: 'roundrobin',
        swissRounds: 1
      };

      this.formdata.rundearray.push(stage);

      // Make the most common connection automatically: first place / winner -> new stage.
      if (previous && Array.isArray(previous.valsArray) && previous.valsArray.length > 0) {
        this.$set(previous.valsArray, 0, newIndex);
      }

      this.recalculate();
    },

    removerunde: function (index) {
      if (this.formdata.rundearray.length <= 1) return;

      this.activePreset = 'custom';
      this.uiMessage = '';
      this.formdata.rundearray.splice(index, 1);

      // Repair destinations because stage indexes changed.
      for (var i = 0; i < this.formdata.rundearray.length; i++) {
        var round = this.formdata.rundearray[i];
        if (!Array.isArray(round.valsArray)) continue;

        for (var p = 0; p < round.valsArray.length; p++) {
          var destination = Number(round.valsArray[p]);
          if (destination === index) {
            this.$set(round.valsArray, p, -1);
          } else if (destination > index) {
            this.$set(round.valsArray, p, destination - 1);
          }

          // A route may only point forward. 0 is reserved for tournament winner.
          if (Number(round.valsArray[p]) > 0 && Number(round.valsArray[p]) <= i) {
            this.$set(round.valsArray, p, -1);
          }
        }
      }

      this.recalculate();
    },

    ensureRoutes: function (index) {
      var round = this.formdata.rundearray[index];
      if (!round) return;

      var expected = 0;
      if (round.selected === 'knockout') {
        expected = 2;
      } else if (round.selected === 'pool') {
        if (round.distributeEvenly === undefined) this.$set(round, 'distributeEvenly', true);
        var sizes = this.getPoolSizes(round);
        expected = sizes.length ? Math.max.apply(null, sizes) : Math.max(0, Number(round.puljesize) || 0);
      }

      if (!Array.isArray(round.valsArray)) this.$set(round, 'valsArray', []);

      while (round.valsArray.length > expected) round.valsArray.pop();
      while (round.valsArray.length < expected) round.valsArray.push(-1);
    },

    onStageTypeChange: function (index) {
      this.activePreset = 'custom';
      var round = this.formdata.rundearray[index];

      if (round.selected === 'pool') {
        this.$set(round, 'puljesize', Number(round.puljesize) >= 2 ? Number(round.puljesize) : 4);
        if (round.distributeEvenly === undefined) this.$set(round, 'distributeEvenly', true);
        this.$set(round, 'playstyle', round.playstyle || 'roundrobin');
        this.$set(round, 'BestOf', Number(round.BestOf) >= 1 ? Number(round.BestOf) : 1);
        if (!Number.isInteger(Number(round.swissRounds)) || Number(round.swissRounds) < 1) {
          this.$set(round, 'swissRounds', this.recommendedSwissRounds(round.puljesize));
        }
      } else if (round.selected === 'knockout') {
        this.$set(round, 'puljesize', 2);
        this.$set(round, 'distributeEvenly', false);
        this.$set(round, 'poolSizes', []);
        this.$set(round, 'playstyle', 'roundrobin');
        this.$set(round, 'BestOf', Number(round.BestOf) >= 1 ? Number(round.BestOf) : 1);
      }

      this.ensureRoutes(index);
      this.recalculate();
    },

    onPoolSizeChange: function (index) {
      this.activePreset = 'custom';
      this.ensureRoutes(index);
      this.recalculate();
    },

    onEvenDistributionChange: function (index) {
      this.activePreset = 'custom';
      this.ensureRoutes(index);
      this.recalculate();
    },

    onPlayStyleChange: function (index) {
      this.activePreset = 'custom';
      var round = this.formdata.rundearray[index];

      if (round.playstyle === 'beerpot') {
        this.$delete(round, 'BestOf');
      } else if (round.playstyle === 'swiss') {
        // A Swiss round is one pairing per player. The next pairing round is
        // generated only after every match in the current round is complete.
        this.$set(round, 'BestOf', 1);
        if (!Number.isInteger(Number(round.swissRounds)) || Number(round.swissRounds) < 1) {
          this.$set(round, 'swissRounds', this.recommendedSwissRounds(round.puljesize));
        }
      } else if (!Number.isInteger(Number(round.BestOf)) || Number(round.BestOf) < 1) {
        this.$set(round, 'BestOf', 1);
      }

      this.recalculate();
    },

    calculatePlayerCounts: function () {
      var rounds = this.formdata.rundearray;
      if (!rounds.length) return;

      this.$set(rounds[0], 'NbPlayers', Number(this.formdata.tournamentplayers) || 0);
      this.ensureRoutes(0);
      this.updatePoolSizes(rounds[0]);

      for (var target = 1; target < rounds.length; target++) {
        var count = 0;

        for (var source = 0; source < target; source++) {
          var round = rounds[source];
          if (!Array.isArray(round.valsArray)) continue;

          if (round.selected === 'pool') {
            for (var place = 0; place < round.valsArray.length; place++) {
              if (Number(round.valsArray[place]) === target) {
                count += this.playersAtPlace(round, place + 1);
              }
            }
          }

          if (round.selected === 'knockout') {
            var koPlayers = Number(round.NbPlayers);
            if (!koPlayers) continue;
            var half = Math.floor(koPlayers / 2);
            if (Number(round.valsArray[0]) === target) count += half;
            if (Number(round.valsArray[1]) === target) count += half;
          }
        }

        this.$set(rounds[target], 'NbPlayers', count);
        this.ensureRoutes(target);
        this.updatePoolSizes(rounds[target]);
      }
    },

    validate: function () {
      var errors = [];
      var name = (this.formdata.tournamentname || '').trim();
      var totalPlayers = Number(this.formdata.tournamentplayers);
      var rounds = this.formdata.rundearray;

      if (!name) errors.push({ type: 'name', description: 'Tournament name is required.' });
      if (!Number.isInteger(totalPlayers) || totalPlayers < 2) {
        errors.push({ type: 'players', description: 'Starting players must be a whole number of at least 2.' });
      }
      if (!rounds.length) errors.push({ type: 'rounds', description: 'Add at least one stage.' });

      for (var i = 0; i < rounds.length; i++) {
        var round = rounds[i];
        var label = round.navn || ('Stage ' + (i + 1));
        var roundPlayers = Number(round.NbPlayers);

        if (!(round.navn || '').trim()) {
          errors.push({ type: 'stage-name-' + i, description: 'Stage ' + (i + 1) + ' needs a name.' });
        }

        if (round.selected !== 'pool' && round.selected !== 'knockout') {
          errors.push({ type: 'stage-type-' + i, description: label + ': choose Pool / group or Knockout.' });
          continue;
        }

        if (!Number.isInteger(roundPlayers) || roundPlayers < 2) {
          errors.push({ type: 'stage-players-' + i, description: label + ': fewer than 2 players are routed into this stage.' });
        }

        if (round.selected === 'pool') {
          var size = Number(round.puljesize);
          if (!Number.isInteger(size) || size < 2) {
            errors.push({ type: 'pool-size-' + i, description: label + ': players per group must be at least 2.' });
          } else if (round.distributeEvenly === false && roundPlayers > 0 && roundPlayers % size !== 0) {
            errors.push({ type: 'pool-div-' + i, description: label + ': ' + roundPlayers + ' players cannot be split into equal groups of exactly ' + size + '. Turn on “Distribute players evenly” or change the group size.' });
          } else if (round.distributeEvenly !== false && roundPlayers > 0) {
            var sizes = this.getPoolSizes(round);
            if (!sizes.length || sizes.some(function (poolSize) { return poolSize < 2; })) {
              errors.push({ type: 'pool-distribution-' + i, description: label + ': could not create valid groups with at least 2 players.' });
            }
          }

          if (!round.playstyle) {
            errors.push({ type: 'style-' + i, description: label + ': choose how the group is played.' });
          }

          if (round.playstyle === 'swiss') {
            var swissRounds = Number(round.swissRounds);
            if (!Number.isInteger(swissRounds) || swissRounds < 1 || swissRounds > 20) {
              errors.push({ type: 'swiss-rounds-' + i, description: label + ': Swiss rounds must be a whole number from 1 to 20.' });
            }
            if (Number(round.BestOf) !== 1) {
              this.$set(round, 'BestOf', 1);
            }
          } else if (round.playstyle !== 'beerpot' && (!Number.isInteger(Number(round.BestOf)) || Number(round.BestOf) < 1)) {
            errors.push({ type: 'bestof-' + i, description: label + ': Best of must be at least 1.' });
          }
        }

        if (round.selected === 'knockout') {
          if (roundPlayers > 0 && roundPlayers % 2 !== 0) {
            errors.push({ type: 'ko-even-' + i, description: label + ': knockout needs an even number of players. Currently ' + roundPlayers + '.' });
          }
          if (!Number.isInteger(Number(round.BestOf)) || Number(round.BestOf) < 1) {
            errors.push({ type: 'ko-bestof-' + i, description: label + ': Best of must be at least 1.' });
          }
        }

        if (Array.isArray(round.valsArray)) {
          for (var r = 0; r < round.valsArray.length; r++) {
            var destination = Number(round.valsArray[r]);
            if (destination > 0 && destination <= i) {
              errors.push({ type: 'route-' + i + '-' + r, description: label + ': a route can only point to a later stage.' });
            }
            if (destination >= rounds.length) {
              errors.push({ type: 'route-missing-' + i + '-' + r, description: label + ': a route points to a stage that no longer exists.' });
            }
          }
        }
      }

      // The routes should ultimately produce exactly one tournament winner.
      var winnerCount = 0;
      for (var w = 0; w < rounds.length; w++) {
        var winnerRound = rounds[w];
        if (!Array.isArray(winnerRound.valsArray)) continue;

        if (winnerRound.selected === 'pool') {
          for (var wp = 0; wp < winnerRound.valsArray.length; wp++) {
            if (Number(winnerRound.valsArray[wp]) === 0) {
              winnerCount += this.playersAtPlace(winnerRound, wp + 1);
            }
          }
        } else if (winnerRound.selected === 'knockout') {
          var winnerKoPlayers = Number(winnerRound.NbPlayers);
          if (winnerKoPlayers > 0) {
            var winnerHalf = Math.floor(winnerKoPlayers / 2);
            if (Number(winnerRound.valsArray[0]) === 0) winnerCount += winnerHalf;
            if (Number(winnerRound.valsArray[1]) === 0) winnerCount += winnerHalf;
          }
        }
      }

      if (winnerCount !== 1) {
        errors.push({
          type: 'winner-count',
          description: winnerCount === 0
            ? 'No tournament winner is defined. Route exactly one final result to “Tournament winner”.'
            : 'The current routes create ' + winnerCount + ' tournament winners. Route exactly one final result to “Tournament winner”.'
        });
      }

      this.formdata.errors = errors;
    },

    recalculate: function () {
      this.calculatePlayerCounts();
      for (var i = 0; i < this.formdata.rundearray.length; i++) {
        this.updatePoolSizes(this.formdata.rundearray[i]);
      }
      this.validate();
    },

    placeLabel: function (n) {
      var endings = ['th', 'st', 'nd', 'rd'];
      var v = n % 100;
      return n + (endings[(v - 20) % 10] || endings[v] || endings[0]) + ' place';
    },

    groupSummary: function (round) {
      var players = Number(round.NbPlayers) || 0;
      var size = Number(round.puljesize) || 0;
      if (!size || players < 1) return '';

      var sizes = this.getPoolSizes(round);
      if (!sizes.length) return players + ' players / groups of ' + size;

      var allSame = sizes.every(function (value) { return value === sizes[0]; });
      if (allSame) {
        return sizes.length + ' group' + (sizes.length === 1 ? '' : 's') + ' × ' + sizes[0] + ' players';
      }

      return sizes.length + ' groups: ' + sizes.join(' / ') + ' players';
    },

    stageSummary: function (round) {
      var players = Number(round.NbPlayers) || 0;
      if (round.selected === 'pool') {
        var styleNames = { roundrobin: 'Round robin', swiss: 'Swiss', beerpot: 'Beer pot' };
        var style = styleNames[round.playstyle] || 'Pool';
        if (round.playstyle === 'swiss') {
          style += ' • ' + (Number(round.swissRounds) || this.recommendedSwissRounds(round.puljesize)) + ' rounds';
        }
        return players + ' players • ' + this.groupSummary(round) + ' • ' + style;
      }
      if (round.selected === 'knockout') {
        return players + ' players • Knockout • Best of ' + (round.BestOf || 1);
      }
      return players + ' players • Format not selected';
    },

    saveStorage: function (form) {
      localStorage.setItem('form', JSON.stringify(form));
    },

    openStorage: function () {
      var raw = localStorage.getItem('form');
      return raw ? JSON.parse(raw) : null;
    },

    getdatapost: function () {
      this.recalculate();
      this.saveStorage(this.formdata);
      this.uiMessage = 'Draft saved in this browser.';
    },

    loaddatapost: function () {
      var saved = this.openStorage();
      if (!saved) {
        this.uiMessage = 'No saved draft was found in this browser.';
        return;
      }

      this.formdata = saved;
      if (this.formdata.privateTournament === undefined) {
        this.$set(this.formdata, 'privateTournament', false);
      }
      this.activePreset = 'custom';
      this.uiMessage = 'Saved draft loaded.';

      for (var i = 0; i < this.formdata.rundearray.length; i++) {
        if (!this.formdata.rundearray[i]._key) {
          this.$set(this.formdata.rundearray[i], '_key', this.newKey());
        }
        if (this.formdata.rundearray[i].selected === 'pool' && this.formdata.rundearray[i].distributeEvenly === undefined) {
          this.$set(this.formdata.rundearray[i], 'distributeEvenly', true);
        }
        if (this.formdata.rundearray[i].playstyle === 'swiss') {
          this.$set(this.formdata.rundearray[i], 'BestOf', 1);
          if (!Number.isInteger(Number(this.formdata.rundearray[i].swissRounds)) || Number(this.formdata.rundearray[i].swissRounds) < 1) {
            this.$set(this.formdata.rundearray[i], 'swissRounds', this.recommendedSwissRounds(this.formdata.rundearray[i].puljesize));
          }
        }
      }
      this.recalculate();
    },

    apiPayload: function () {
      // _key is only for Vue rendering. The backend does not need it.
      var payload = JSON.parse(JSON.stringify(this.formdata));
      payload.errors = [];
      payload.rundearray.forEach(function (round) { delete round._key; });
      return payload;
    },

    apipostturnament: function () {
      this.apiError = '';
      this.createdTournamentId = '';
      this.createdJoinCode = '';
      this.authToken = localStorage.getItem('token') || '';
      this.recalculate();

      if (!this.isValid) {
        this.apiError = 'Fix the tournament structure before creating it.';
        return;
      }

      if (!this.authToken) {
        this.apiError = 'Login first. Open “Login & token” in the top menu.';
        return;
      }

      this.creating = true;
      axios.defaults.headers.common['Authorization'] = this.authToken;

      var json = JSON.stringify(this.apiPayload());

      // TournamentController expects [FromBody] string, so the JSON document
      // must be sent as a JSON string (same contract as the original project).
      axios.post('http://localhost:5000/api/tournament', JSON.stringify(json), {
        headers: { 'Content-type': 'application/json; charset=utf-8' }
      })
      .then(function (response) {
        var id = response.data.tournament !== undefined
          ? response.data.tournament
          : response.data.Tournament;

        this.createdTournamentId = id;
        this.createdJoinCode = response.data.joinCode || response.data.JoinCode || '';
        localStorage.setItem('lastTournamentId', String(id));
        if (this.createdJoinCode) {
          localStorage.setItem('lastTournamentJoinCode', this.createdJoinCode);
        } else {
          localStorage.removeItem('lastTournamentJoinCode');
        }
        match.TurnamentId = id;
      }.bind(this))
      .catch(function (error) {
        if (error.response && error.response.status === 401) {
          this.apiError = '401 Unauthorized. Your login token is missing, expired, or rejected. Login again and retry.';
        } else if (error.response) {
          this.apiError = 'Server error ' + error.response.status + ': ' + JSON.stringify(error.response.data);
        } else {
          this.apiError = error.message || String(error);
        }
      }.bind(this))
      .finally(function () {
        this.creating = false;
      }.bind(this));
    }
  },

  mounted: function () {
    this.recalculate();
  }
});

/* =========================
   Navigation
   ========================= */
function showSection(sectionId) {
  var sections = $('#dashboard, #app, #vueformapp, #vuematch');
  sections.hide();
  $('#' + sectionId).show();

  $('.admin-panel .slidebar li').removeClass('selectedbutton');
  $('#' + sectionId + 'Item').parent().addClass('selectedbutton');
}

$(document).ready(function () {
  $('.mainContent').show();
  showSection('dashboard');

  $('.admin-panel .slidebar a[href="#"]').on('click', function (event) {
    event.preventDefault();
    showSection($(this).attr('class'));
  });
});
