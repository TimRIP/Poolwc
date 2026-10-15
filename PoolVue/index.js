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
function defaultTournamentStart() {
  var d = new Date();
  d.setSeconds(0, 0);
  var roundedMinutes = Math.ceil(d.getMinutes() / 15) * 15;
  d.setMinutes(roundedMinutes);

  function pad(value) {
    return String(value).padStart(2, '0');
  }

  return d.getFullYear() + '-' +
    pad(d.getMonth() + 1) + '-' +
    pad(d.getDate()) + 'T' +
    pad(d.getHours()) + ':' +
    pad(d.getMinutes());
}

function timeValueToMinutes(value) {
  if (typeof value !== 'string' || !/^\d{2}:\d{2}$/.test(value)) return null;
  var parts = value.split(':');
  var hours = Number(parts[0]);
  var minutes = Number(parts[1]);
  if (!Number.isInteger(hours) || !Number.isInteger(minutes) || hours < 0 || hours > 23 || minutes < 0 || minutes > 59) return null;
  return hours * 60 + minutes;
}

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
      manualPlayerDraw: false,
      autoSchedule: true,
      scheduleStart: defaultTournamentStart(),
      gameLengthMinutes: 30,
      venueOpenTime: '09:00',
      venueCloseTime: '22:00',
      venues: [
        { name: 'Table 1', description: '' },
        { name: 'Table 2', description: '' }
      ],
      rundearray: [
        {
          _key: 1,
          navn: 'Stage 1',
          NbPlayers: 8,
          selected: 'pool',
          BestOf: 1,
          PlayFrom: 70,
          PlayTo: 0,
          puljesize: 4,
          distributeEvenly: true,
          poolSizes: [4, 4],
          valsArray: [-1, -1, -1, -1],
          poolAdvancementRoutes: [],
          wildcardCount: 0,
          wildcardRoutes: [],
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

    addVenue: function () {
      this.formdata.venues.push({ name: 'Table ' + (this.formdata.venues.length + 1), description: '' });
      this.recalculate();
    },

    removeVenue: function (index) {
      if (!Array.isArray(this.formdata.venues) || this.formdata.venues.length <= 1) return;
      this.formdata.venues.splice(index, 1);
      this.recalculate();
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


    stagePoolSizes: function (round) {
      if (!round) return [];

      if (round.selected === 'pool') {
        return this.getPoolSizes(round);
      }

      if (round.selected === 'knockout') {
        var players = Number(round.NbPlayers);
        if (!Number.isInteger(players) || players < 2 || players % 2 !== 0) return [];
        var sizes = [];
        for (var i = 0; i < players / 2; i++) sizes.push(2);
        return sizes;
      }

      return [];
    },

    destinationPoolCount: function (stageIndex) {
      stageIndex = Number(stageIndex);
      if (!Number.isInteger(stageIndex) || stageIndex < 0 || stageIndex >= this.formdata.rundearray.length) return 0;
      return this.stagePoolSizes(this.formdata.rundearray[stageIndex]).length;
    },

    destinationPoolCapacity: function (stageIndex, poolIndex) {
      stageIndex = Number(stageIndex);
      poolIndex = Number(poolIndex);
      if (!Number.isInteger(stageIndex) || !Number.isInteger(poolIndex)) return 0;
      if (stageIndex < 0 || stageIndex >= this.formdata.rundearray.length) return 0;
      var sizes = this.stagePoolSizes(this.formdata.rundearray[stageIndex]);
      return poolIndex >= 0 && poolIndex < sizes.length ? Number(sizes[poolIndex]) || 0 : 0;
    },

    destinationPoolAssignedCount: function (stageIndex, poolIndex) {
      stageIndex = Number(stageIndex);
      poolIndex = Number(poolIndex);
      var count = 0;
      var rounds = this.formdata.rundearray;

      for (var i = 0; i < rounds.length; i++) {
        var routes = Array.isArray(rounds[i].poolAdvancementRoutes) ? rounds[i].poolAdvancementRoutes : [];
        for (var r = 0; r < routes.length; r++) {
          if (Number(routes[r].destinationStage) === stageIndex && Number(routes[r].destinationPool) === poolIndex) count++;
        }

        var wildcardRoutes = Array.isArray(rounds[i].wildcardRoutes) ? rounds[i].wildcardRoutes : [];
        for (var w = 0; w < wildcardRoutes.length; w++) {
          if (Number(wildcardRoutes[w].destinationStage) === stageIndex && Number(wildcardRoutes[w].destinationPool) === poolIndex) count++;
        }
      }

      return count;
    },

    wildcardEligibleCount: function (round) {
      if (!round || !Array.isArray(round.valsArray)) return 0;
      var count = 0;

      if (round.selected === 'pool') {
        for (var place = 0; place < round.valsArray.length; place++) {
          if (Number(round.valsArray[place]) === -1) {
            count += this.playersAtPlace(round, place + 1);
          }
        }
      } else if (round.selected === 'knockout') {
        var players = Number(round.NbPlayers) || 0;
        var half = Math.floor(players / 2);
        if (Number(round.valsArray[0]) === -1) count += half;
        if (Number(round.valsArray[1]) === -1) count += half;
      }

      return count;
    },

    sourceRouteLabel: function (round, place) {
      place = Number(place);
      if (round && round.selected === 'knockout') return place === 1 ? 'Winner' : 'Loser';
      return this.placeLabel(place);
    },

    hasPoolAdvancementRoutes: function () {
      return this.formdata.rundearray.some(function (round) {
        return (Array.isArray(round.poolAdvancementRoutes) && round.poolAdvancementRoutes.length > 0) ||
          (Array.isArray(round.wildcardRoutes) && round.wildcardRoutes.length > 0);
      });
    },

    rebuildPoolAdvancementRoutes: function (resetAssignments) {
      var rounds = this.formdata.rundearray;
      var existing = {};

      if (!resetAssignments) {
        for (var oldStage = 0; oldStage < rounds.length; oldStage++) {
          var oldRoutes = Array.isArray(rounds[oldStage].poolAdvancementRoutes) ? rounds[oldStage].poolAdvancementRoutes : [];
          for (var oldIndex = 0; oldIndex < oldRoutes.length; oldIndex++) {
            var oldRoute = oldRoutes[oldIndex];
            var oldKey = oldStage + '|' + Number(oldRoute.sourcePool) + '|' + Number(oldRoute.place) + '|' + Number(oldRoute.destinationStage);
            existing[oldKey] = Number(oldRoute.destinationPool);
          }
        }
      }

      var specs = [];
      var usage = {};

      for (var sourceStage = 0; sourceStage < rounds.length; sourceStage++) {
        var sourceRound = rounds[sourceStage];
        var sourcePoolSizes = this.stagePoolSizes(sourceRound);

        for (var sourcePool = 0; sourcePool < sourcePoolSizes.length; sourcePool++) {
          for (var place = 1; place <= sourcePoolSizes[sourcePool]; place++) {
            var destinationStage = Array.isArray(sourceRound.valsArray)
              ? Number(sourceRound.valsArray[place - 1])
              : -1;

            if (!Number.isInteger(destinationStage) || destinationStage <= sourceStage || destinationStage >= rounds.length) continue;

            var targetSizes = this.stagePoolSizes(rounds[destinationStage]);
            if (!targetSizes.length) continue;

            if (!usage[destinationStage]) usage[destinationStage] = new Array(targetSizes.length).fill(0);

            var key = sourceStage + '|' + sourcePool + '|' + place + '|' + destinationStage;
            var preserved = existing.hasOwnProperty(key) ? Number(existing[key]) : -1;
            var validPreserved = Number.isInteger(preserved) && preserved >= 0 && preserved < targetSizes.length;

            specs.push({
              sourceStage: sourceStage,
              sourcePool: sourcePool,
              place: place,
              destinationStage: destinationStage,
              destinationPool: validPreserved ? preserved : -1,
              targetSizes: targetSizes
            });

            if (validPreserved) usage[destinationStage][preserved]++;
          }
        }
      }

      // Fill only routes that do not already have a valid manual selection.
      // Auto-fill is done in adjacent placement pairs: 1st with 2nd, 3rd with
      // 4th, 5th with 6th, and so on. We assign the lower placement of each
      // pair first, then deliberately place the matching higher placement into
      // a pool that already contains the opposite placement from a DIFFERENT
      // source pool whenever possible. This makes 3rd-vs-4th just as strict as
      // 1st-vs-2nd instead of merely "balancing" counts.
      var pendingGroups = {};

      for (var pendingIndex = 0; pendingIndex < specs.length; pendingIndex++) {
        var pendingSpec = specs[pendingIndex];
        if (pendingSpec.destinationPool >= 0) continue;

        var pendingPlace = Number(pendingSpec.place);
        var pendingPairLow = pendingPlace % 2 === 1 ? pendingPlace : pendingPlace - 1;
        var pendingKey = pendingSpec.destinationStage + '|' + pendingPairLow;

        if (!pendingGroups[pendingKey]) {
          pendingGroups[pendingKey] = {
            destinationStage: pendingSpec.destinationStage,
            pairLow: pendingPairLow,
            pairHigh: pendingPairLow + 1,
            specs: []
          };
        }
        pendingGroups[pendingKey].specs.push(pendingSpec);
      }

      function assignedSpecsForPool(destinationStage, poolIndex) {
        var assigned = [];
        for (var ai = 0; ai < specs.length; ai++) {
          var assignedSpec = specs[ai];
          if (assignedSpec.destinationStage === destinationStage && assignedSpec.destinationPool === poolIndex) {
            assigned.push(assignedSpec);
          }
        }
        return assigned;
      }

      function assignSpecToBestPool(spec, group, preferOppositePlacement) {
        var targetUsage = usage[spec.destinationStage];
        var bestPool = -1;
        var bestOppositePenalty = Number.POSITIVE_INFINITY;
        var bestSourceConflict = Number.POSITIVE_INFINITY;
        var bestSamePlaceCount = Number.POSITIVE_INFINITY;
        var bestPairImbalance = Number.POSITIVE_INFINITY;
        var bestFill = Number.POSITIVE_INFINITY;

        for (var p = 0; p < spec.targetSizes.length; p++) {
          var capacity = Number(spec.targetSizes[p]) || 0;
          if (capacity <= 0 || targetUsage[p] >= capacity) continue;

          var assignedHere = assignedSpecsForPool(spec.destinationStage, p);
          var sourceConflict = 0;
          var samePlaceCount = 0;
          var pairLowCount = 0;
          var pairHighCount = 0;
          var oppositeFromDifferentSource = 0;
          var placeNumber = Number(spec.place);
          var oppositePlace = placeNumber === group.pairLow ? group.pairHigh : group.pairLow;

          for (var a = 0; a < assignedHere.length; a++) {
            var other = assignedHere[a];
            var otherPlace = Number(other.place);

            if (other.sourceStage === spec.sourceStage && other.sourcePool === spec.sourcePool) {
              sourceConflict = 1;
            }
            if (otherPlace === placeNumber) samePlaceCount++;
            if (otherPlace === group.pairLow) pairLowCount++;
            if (otherPlace === group.pairHigh) pairHighCount++;
            if (otherPlace === oppositePlace &&
                !(other.sourceStage === spec.sourceStage && other.sourcePool === spec.sourcePool)) {
              oppositeFromDifferentSource++;
            }
          }

          // For the second half of a placement pair, a pool that already has
          // the opposite placement from another source pool is always preferred.
          // This is the key that guarantees 3rd-vs-4th (and 1st-vs-2nd) when
          // the destination structure has room for it.
          var oppositePenalty = preferOppositePlacement
            ? (oppositeFromDifferentSource > 0 ? 0 : 1)
            : 0;

          var lowAfter = pairLowCount + (placeNumber === group.pairLow ? 1 : 0);
          var highAfter = pairHighCount + (placeNumber === group.pairHigh ? 1 : 0);
          var pairImbalance = Math.abs(lowAfter - highAfter);
          var fill = targetUsage[p] / capacity;

          if (oppositePenalty < bestOppositePenalty ||
              (oppositePenalty === bestOppositePenalty && sourceConflict < bestSourceConflict) ||
              (oppositePenalty === bestOppositePenalty && sourceConflict === bestSourceConflict && samePlaceCount < bestSamePlaceCount) ||
              (oppositePenalty === bestOppositePenalty && sourceConflict === bestSourceConflict && samePlaceCount === bestSamePlaceCount && pairImbalance < bestPairImbalance) ||
              (oppositePenalty === bestOppositePenalty && sourceConflict === bestSourceConflict && samePlaceCount === bestSamePlaceCount && pairImbalance === bestPairImbalance && fill < bestFill)) {
            bestPool = p;
            bestOppositePenalty = oppositePenalty;
            bestSourceConflict = sourceConflict;
            bestSamePlaceCount = samePlaceCount;
            bestPairImbalance = pairImbalance;
            bestFill = fill;
          }
        }

        // Validation will report an impossible/overfilled setup. This fallback
        // keeps the UI editable instead of dropping the route.
        if (bestPool < 0) {
          bestPool = 0;
          for (var fallback = 1; fallback < targetUsage.length; fallback++) {
            if (targetUsage[fallback] < targetUsage[bestPool]) bestPool = fallback;
          }
        }

        spec.destinationPool = bestPool;
        targetUsage[bestPool]++;
      }

      Object.keys(pendingGroups).sort().forEach(function (groupKey) {
        var group = pendingGroups[groupKey];
        var lower = group.specs.filter(function (item) { return Number(item.place) === group.pairLow; });
        var higher = group.specs.filter(function (item) { return Number(item.place) === group.pairHigh; });
        var unusual = group.specs.filter(function (item) {
          var placeNumber = Number(item.place);
          return placeNumber !== group.pairLow && placeNumber !== group.pairHigh;
        });

        lower.sort(function (a, b) {
          return a.sourceStage - b.sourceStage || a.sourcePool - b.sourcePool;
        });
        higher.sort(function (a, b) {
          return a.sourceStage - b.sourceStage || a.sourcePool - b.sourcePool;
        });

        // Seed one side of the pair across the destination pools first.
        lower.forEach(function (item) {
          assignSpecToBestPool(item, group, false);
        });

        // Then cross-seed the partner placement into pools containing the
        // opposite placement from another source pool.
        higher.forEach(function (item) {
          assignSpecToBestPool(item, group, true);
        });

        unusual.forEach(function (item) {
          assignSpecToBestPool(item, group, false);
        });
      });

      var routesPerSource = new Array(rounds.length);
      for (var ri = 0; ri < routesPerSource.length; ri++) routesPerSource[ri] = [];

      for (var x = 0; x < specs.length; x++) {
        var item = specs[x];
        routesPerSource[item.sourceStage].push({
          sourcePool: item.sourcePool,
          place: item.place,
          destinationStage: item.destinationStage,
          destinationPool: item.destinationPool
        });
      }

      for (var setStage = 0; setStage < rounds.length; setStage++) {
        this.$set(rounds[setStage], 'poolAdvancementRoutes', routesPerSource[setStage]);
      }
    },

    rebuildWildcardRoutes: function (resetAssignments) {
      var rounds = this.formdata.rundearray;

      for (var sourceStage = 0; sourceStage < rounds.length; sourceStage++) {
        var round = rounds[sourceStage];
        var count = sourceStage < rounds.length - 1
          ? Math.max(0, Math.floor(Number(round.wildcardCount) || 0))
          : 0;
        var destinationStage = sourceStage + 1;
        var targetSizes = destinationStage < rounds.length ? this.stagePoolSizes(rounds[destinationStage]) : [];
        var existing = Array.isArray(round.wildcardRoutes) ? round.wildcardRoutes : [];
        var normalRoutes = [];

        for (var rs = 0; rs < rounds.length; rs++) {
          var sourceRoutes = Array.isArray(rounds[rs].poolAdvancementRoutes) ? rounds[rs].poolAdvancementRoutes : [];
          for (var nr = 0; nr < sourceRoutes.length; nr++) {
            if (Number(sourceRoutes[nr].destinationStage) === destinationStage) normalRoutes.push(sourceRoutes[nr]);
          }
        }

        var usage = new Array(targetSizes.length).fill(0);
        for (var n = 0; n < normalRoutes.length; n++) {
          var normalPool = Number(normalRoutes[n].destinationPool);
          if (Number.isInteger(normalPool) && normalPool >= 0 && normalPool < usage.length) usage[normalPool]++;
        }

        var routes = [];
        for (var wildcardIndex = 0; wildcardIndex < count; wildcardIndex++) {
          var preserved = -1;
          if (!resetAssignments) {
            var oldRoute = existing.find(function (item) {
              return Number(item.wildcardIndex) === wildcardIndex && Number(item.destinationStage) === destinationStage;
            });
            if (oldRoute) preserved = Number(oldRoute.destinationPool);
          }

          var chosen = -1;
          if (Number.isInteger(preserved) && preserved >= 0 && preserved < targetSizes.length && usage[preserved] < Number(targetSizes[preserved] || 0)) {
            chosen = preserved;
          } else {
            var bestFill = Number.POSITIVE_INFINITY;
            for (var poolIndex = 0; poolIndex < targetSizes.length; poolIndex++) {
              var capacity = Number(targetSizes[poolIndex]) || 0;
              if (capacity <= 0 || usage[poolIndex] >= capacity) continue;
              var fill = usage[poolIndex] / capacity;
              if (fill < bestFill) {
                bestFill = fill;
                chosen = poolIndex;
              }
            }
          }

          if (chosen < 0 && targetSizes.length) chosen = 0;
          if (chosen >= 0 && chosen < usage.length) usage[chosen]++;

          routes.push({
            wildcardIndex: wildcardIndex,
            destinationStage: destinationStage,
            destinationPool: chosen
          });
        }

        this.$set(round, 'wildcardRoutes', routes);
      }
    },

    autoAssignPoolAdvancementRoutes: function () {
      this.rebuildPoolAdvancementRoutes(true);
      this.rebuildWildcardRoutes(true);
      this.validate();
      this.uiMessage = 'Pool advancement routes and wildcard slots were auto-filled. Normal placements are paired where possible, and wildcard places fill the remaining destination-pool capacity.';
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
        PlayFrom: 70,
        PlayTo: 0,
        puljesize: poolSize,
        distributeEvenly: true,
        poolSizes: this.calculateEvenPoolSizes(players, poolSize),
        valsArray: routes,
        poolAdvancementRoutes: [],
        wildcardCount: 0,
        wildcardRoutes: [],
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
        PlayFrom: 70,
        PlayTo: 0,
        puljesize: 2,
        distributeEvenly: false,
        poolSizes: [],
        valsArray: [hasNextStage ? stageIndex + 1 : 0, -1],
        poolAdvancementRoutes: [],
        wildcardCount: 0,
        wildcardRoutes: [],
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
            PlayFrom: 70,
            PlayTo: 0,
            puljesize: Math.min(4, players),
            distributeEvenly: true,
            poolSizes: [],
            valsArray: [],
            poolAdvancementRoutes: [],
            wildcardCount: 0,
            wildcardRoutes: [],
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
        PlayFrom: previous && Number.isInteger(Number(previous.PlayFrom)) ? Number(previous.PlayFrom) : 70,
        PlayTo: previous && Number.isInteger(Number(previous.PlayTo)) ? Number(previous.PlayTo) : 0,
        puljesize: 2,
        distributeEvenly: false,
        poolSizes: [],
        valsArray: [0, -1],
        poolAdvancementRoutes: [],
        wildcardCount: 0,
        wildcardRoutes: [],
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

      if (this.formdata.rundearray.length) {
        var lastRound = this.formdata.rundearray[this.formdata.rundearray.length - 1];
        this.$set(lastRound, 'wildcardCount', 0);
        this.$set(lastRound, 'wildcardRoutes', []);
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

    onWildcardCountChange: function (index) {
      this.activePreset = 'custom';
      var round = this.formdata.rundearray[index];
      if (!round) return;
      var count = Math.max(0, Math.floor(Number(round.wildcardCount) || 0));
      this.$set(round, 'wildcardCount', count);
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

        // Wildcards are reserved places from the immediately preceding stage.
        // The actual eliminated player is chosen only after that stage finishes.
        var wildcardSource = rounds[target - 1];
        if (wildcardSource) {
          count += Math.max(0, Math.floor(Number(wildcardSource.wildcardCount) || 0));
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

      var venues = Array.isArray(this.formdata.venues) ? this.formdata.venues : [];
      var venueNames = venues
        .map(function (venue) { return ((venue && venue.name) || '').trim(); })
        .filter(function (venueName) { return venueName.length > 0; });

      var seenVenueNames = {};
      venueNames.forEach(function (venueName) {
        var key = venueName.toLowerCase();
        if (seenVenueNames[key]) {
          errors.push({ type: 'venue-duplicate-' + key, description: 'Venue names must be unique inside the tournament: ' + venueName + '.' });
        }
        seenVenueNames[key] = true;
      });

      venues.forEach(function (venue, venueIndex) {
        var venueName = ((venue && venue.name) || '').trim();
        var venueDescription = ((venue && venue.description) || '').trim();
        if (venueName.length > 255) {
          errors.push({ type: 'venue-name-' + venueIndex, description: 'Venue ' + (venueIndex + 1) + ': name can be at most 255 characters.' });
        }
        if (venueDescription.length > 255) {
          errors.push({ type: 'venue-description-' + venueIndex, description: 'Venue ' + (venueIndex + 1) + ': description can be at most 255 characters.' });
        }
      });

      if (this.formdata.autoSchedule) {
        if (!venueNames.length) {
          errors.push({ type: 'auto-schedule-venues', description: 'Automatic scheduling needs at least one tournament venue.' });
        }
        if (!this.formdata.scheduleStart) {
          errors.push({ type: 'auto-schedule-start', description: 'Automatic scheduling needs a tournament start date and time.' });
        }
        var gameLength = Number(this.formdata.gameLengthMinutes);
        if (!Number.isInteger(gameLength) || gameLength < 1 || gameLength > 1440) {
          errors.push({ type: 'auto-schedule-length', description: 'Game length must be a whole number from 1 to 1440 minutes.' });
        }

        var venueOpenMinutes = timeValueToMinutes(this.formdata.venueOpenTime);
        var venueCloseMinutes = timeValueToMinutes(this.formdata.venueCloseTime);
        if (venueOpenMinutes === null) {
          errors.push({ type: 'venue-open-time', description: 'Set the daily venue opening time.' });
        }
        if (venueCloseMinutes === null) {
          errors.push({ type: 'venue-close-time', description: 'Set the daily venue closing time.' });
        }
        if (venueOpenMinutes !== null && venueCloseMinutes !== null && venueCloseMinutes <= venueOpenMinutes) {
          errors.push({ type: 'venue-hours', description: 'Venue closing time must be later than opening time.' });
        }
      }

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

        var playFrom = Number(round.PlayFrom);
        var playTo = Number(round.PlayTo);
        if (!Number.isInteger(playFrom) || playFrom < 0) {
          errors.push({ type: 'play-from-' + i, description: label + ': Play from must be a non-negative whole number.' });
        }
        if (!Number.isInteger(playTo) || playTo < 0) {
          errors.push({ type: 'play-to-' + i, description: label + ': Play to must be a non-negative whole number.' });
        }
        if (Number.isInteger(playFrom) && Number.isInteger(playTo) && playFrom === playTo) {
          errors.push({ type: 'play-range-' + i, description: label + ': Play from and Play to must be different.' });
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

        var wildcardCount = Math.max(0, Math.floor(Number(round.wildcardCount) || 0));
        if (Number(round.wildcardCount || 0) !== wildcardCount) {
          errors.push({ type: 'wildcard-count-' + i, description: label + ': wildcard places must be a non-negative whole number.' });
        }
        if (wildcardCount > 0 && i >= rounds.length - 1) {
          errors.push({ type: 'wildcard-last-' + i, description: label + ': wildcards need a following stage.' });
        }
        if (wildcardCount > this.wildcardEligibleCount(round)) {
          errors.push({
            type: 'wildcard-eligible-' + i,
            description: label + ': ' + wildcardCount + ' wildcard place' + (wildcardCount === 1 ? '' : 's') +
              ' requested, but only ' + this.wildcardEligibleCount(round) + ' eliminated player' +
              (this.wildcardEligibleCount(round) === 1 ? ' is' : 's are') + ' available.'
          });
        }
      }


      // Exact pool-to-pool routing. Stage-level routes decide who advances;
      // these assignments decide which destination pool receives each player.
      var targetAssignments = {};
      for (var sourceStageIndex = 0; sourceStageIndex < rounds.length; sourceStageIndex++) {
        var sourceStageRound = rounds[sourceStageIndex];
        var explicitRoutes = Array.isArray(sourceStageRound.poolAdvancementRoutes) ? sourceStageRound.poolAdvancementRoutes : [];

        for (var er = 0; er < explicitRoutes.length; er++) {
          var route = explicitRoutes[er];
          var destinationStageIndex = Number(route.destinationStage);
          var destinationPoolIndex = Number(route.destinationPool);
          var routePlace = Number(route.place);
          var routeSourcePool = Number(route.sourcePool);
          var sourcePoolSizes = this.stagePoolSizes(sourceStageRound);

          if (!Number.isInteger(routeSourcePool) || routeSourcePool < 0 || routeSourcePool >= sourcePoolSizes.length || routePlace < 1 || routePlace > sourcePoolSizes[routeSourcePool]) {
            errors.push({ type: 'pool-route-source-' + sourceStageIndex + '-' + er, description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': invalid source pool advancement route.' });
            continue;
          }

          if (!Number.isInteger(destinationStageIndex) || destinationStageIndex <= sourceStageIndex || destinationStageIndex >= rounds.length) {
            errors.push({ type: 'pool-route-stage-' + sourceStageIndex + '-' + er, description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': invalid destination stage in pool advancement routing.' });
            continue;
          }

          if (!Array.isArray(sourceStageRound.valsArray) || Number(sourceStageRound.valsArray[routePlace - 1]) !== destinationStageIndex) {
            errors.push({ type: 'pool-route-mismatch-' + sourceStageIndex + '-' + er, description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': a pool route does not match its finishing-position stage route.' });
            continue;
          }

          var targetSizes = this.stagePoolSizes(rounds[destinationStageIndex]);
          if (!Number.isInteger(destinationPoolIndex) || destinationPoolIndex < 0 || destinationPoolIndex >= targetSizes.length) {
            errors.push({ type: 'pool-route-destination-' + sourceStageIndex + '-' + er, description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': choose a valid destination pool.' });
            continue;
          }

          if (!targetAssignments[destinationStageIndex]) targetAssignments[destinationStageIndex] = new Array(targetSizes.length).fill(0);
          targetAssignments[destinationStageIndex][destinationPoolIndex]++;
        }

        var wildcardRoutes = Array.isArray(sourceStageRound.wildcardRoutes) ? sourceStageRound.wildcardRoutes : [];
        var expectedWildcards = Math.max(0, Math.floor(Number(sourceStageRound.wildcardCount) || 0));
        if (wildcardRoutes.length !== expectedWildcards) {
          errors.push({
            type: 'wildcard-route-count-' + sourceStageIndex,
            description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': wildcard pool assignments are incomplete.'
          });
        }

        for (var wr = 0; wr < wildcardRoutes.length; wr++) {
          var wildcardRoute = wildcardRoutes[wr];
          var wildcardDestinationStage = Number(wildcardRoute.destinationStage);
          var wildcardDestinationPool = Number(wildcardRoute.destinationPool);
          var wildcardIndex = Number(wildcardRoute.wildcardIndex);

          if (!Number.isInteger(wildcardIndex) || wildcardIndex < 0 || wildcardIndex >= expectedWildcards ||
              wildcardDestinationStage !== sourceStageIndex + 1 || wildcardDestinationStage >= rounds.length) {
            errors.push({ type: 'wildcard-route-' + sourceStageIndex + '-' + wr, description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': invalid wildcard route.' });
            continue;
          }

          var wildcardTargetSizes = this.stagePoolSizes(rounds[wildcardDestinationStage]);
          if (!Number.isInteger(wildcardDestinationPool) || wildcardDestinationPool < 0 || wildcardDestinationPool >= wildcardTargetSizes.length) {
            errors.push({ type: 'wildcard-pool-' + sourceStageIndex + '-' + wr, description: (sourceStageRound.navn || ('Stage ' + (sourceStageIndex + 1))) + ': choose a destination pool for every wildcard.' });
            continue;
          }

          if (!targetAssignments[wildcardDestinationStage]) targetAssignments[wildcardDestinationStage] = new Array(wildcardTargetSizes.length).fill(0);
          targetAssignments[wildcardDestinationStage][wildcardDestinationPool]++;
        }
      }

      for (var targetStageIndex = 1; targetStageIndex < rounds.length; targetStageIndex++) {
        var targetStageSizes = this.stagePoolSizes(rounds[targetStageIndex]);
        if (!targetStageSizes.length) continue;
        var assigned = targetAssignments[targetStageIndex] || new Array(targetStageSizes.length).fill(0);
        var mismatches = [];
        for (var tp = 0; tp < targetStageSizes.length; tp++) {
          if (Number(assigned[tp] || 0) !== Number(targetStageSizes[tp] || 0)) {
            mismatches.push('pulje ' + (tp + 1) + ': ' + Number(assigned[tp] || 0) + '/' + Number(targetStageSizes[tp] || 0));
          }
        }
        if (mismatches.length) {
          errors.push({
            type: 'pool-route-capacity-' + targetStageIndex,
            description: (rounds[targetStageIndex].navn || ('Stage ' + (targetStageIndex + 1))) + ': assign advancing players and wildcard places so every destination pool is filled exactly (' + mismatches.join(', ') + ').'
          });
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
      this.rebuildPoolAdvancementRoutes(false);
      this.rebuildWildcardRoutes(false);
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
        return players + ' players • ' + this.groupSummary(round) + ' • ' + style + ' • ' + (Number(round.PlayFrom) || 0) + ' → ' + (Number(round.PlayTo) || 0);
      }
      if (round.selected === 'knockout') {
        return players + ' players • Knockout • Best of ' + (round.BestOf || 1) + ' • ' + (Number(round.PlayFrom) || 0) + ' → ' + (Number(round.PlayTo) || 0);
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
      if (this.formdata.manualPlayerDraw === undefined) {
        this.$set(this.formdata, 'manualPlayerDraw', false);
      }
      if (this.formdata.autoSchedule === undefined) {
        this.$set(this.formdata, 'autoSchedule', true);
      }
      if (!this.formdata.scheduleStart) {
        this.$set(this.formdata, 'scheduleStart', defaultTournamentStart());
      }
      if (!Number.isInteger(Number(this.formdata.gameLengthMinutes)) || Number(this.formdata.gameLengthMinutes) < 1) {
        this.$set(this.formdata, 'gameLengthMinutes', 30);
      }
      if (timeValueToMinutes(this.formdata.venueOpenTime) === null) {
        this.$set(this.formdata, 'venueOpenTime', '09:00');
      }
      if (timeValueToMinutes(this.formdata.venueCloseTime) === null) {
        this.$set(this.formdata, 'venueCloseTime', '22:00');
      }
      if (!Array.isArray(this.formdata.venues) || !this.formdata.venues.length) {
        this.$set(this.formdata, 'venues', [
          { name: 'Table 1', description: '' },
          { name: 'Table 2', description: '' }
        ]);
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
        if (!Number.isInteger(Number(this.formdata.rundearray[i].PlayFrom))) {
          this.$set(this.formdata.rundearray[i], 'PlayFrom', 70);
        }
        if (!Number.isInteger(Number(this.formdata.rundearray[i].PlayTo))) {
          this.$set(this.formdata.rundearray[i], 'PlayTo', 0);
        }
        if (!Number.isInteger(Number(this.formdata.rundearray[i].wildcardCount)) || Number(this.formdata.rundearray[i].wildcardCount) < 0) {
          this.$set(this.formdata.rundearray[i], 'wildcardCount', 0);
        }
        if (!Array.isArray(this.formdata.rundearray[i].wildcardRoutes)) {
          this.$set(this.formdata.rundearray[i], 'wildcardRoutes', []);
        }
        if (!Array.isArray(this.formdata.rundearray[i].poolAdvancementRoutes)) {
          this.$set(this.formdata.rundearray[i], 'poolAdvancementRoutes', []);
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

        var scheduledPools = Number(response.data.scheduledPools || 0);
        var venueCount = Number(response.data.venueCount || 0);
        if (scheduledPools > 0) {
          this.uiMessage = 'Tournament created. ' + scheduledPools + ' pools were automatically scheduled across ' + venueCount + ' venue' + (venueCount === 1 ? '' : 's') + '.';
        } else if (venueCount > 0) {
          this.uiMessage = 'Tournament created with ' + venueCount + ' tournament venue' + (venueCount === 1 ? '' : 's') + '.';
        } else {
          this.uiMessage = 'Tournament created.';
        }
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
