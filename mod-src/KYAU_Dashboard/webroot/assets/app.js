/* AfterHours — live status, mods list, and the fog-of-war map. */
(function () {
  'use strict';

  var $ = function (id) { return document.getElementById(id); };
  var SERVER_ADDR = 'helios.kyaulabs.com:26900';
  var STATUS_MS = 15000;
  var MARKERS_MS = 60000;

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }
  function pad2(n) { return (n < 10 ? '0' : '') + n; }

  /* ================= status feed ================= */

  function fetchStatus() {
    return fetch('/api/afterhours').then(function (r) {
      if (!r.ok) throw new Error('afterhours api ' + r.status);
      return r.json();
    }).catch(function () {
      // Fallback: stock public APIs (used until the AfterHoursApi mod loads)
      return fetch('/api/serverstats').then(function (r) { return r.json(); }).then(function (stats) {
        return {
          fallback: true,
          data: {
            server: null,
            gameTime: stats.data.gameTime,
            bloodmoon: null,
            counts: {
              players: stats.data.players,
              hostiles: stats.data.hostiles,
              animals: stats.data.animals
            },
            players: null
          }
        };
      });
    });
  }

  var failCount = 0;
  var latestPlayers = [];
  var latestZombies = [];

  function bmCountdown(gt, bm) {
    if (!bm) return { big: '–', sub: 'no data', state: 'calm' };
    if (bm.active) return { big: 'TONIGHT', sub: 'the horde is here — good luck', state: 'active' };
    var hoursLeft = (bm.day - gt.days) * 24 + (bm.start.hours - gt.hours) - gt.minutes / 60;
    if (hoursLeft <= 0) return { big: 'TONIGHT', sub: 'starts at ' + pad2(bm.start.hours) + ':00', state: 'active' };
    if (hoursLeft < 24) return { big: 'TONIGHT', sub: 'horde night begins ' + pad2(bm.start.hours) + ':00', state: 'imminent' };
    var d = Math.floor(hoursLeft / 24);
    var h = Math.floor(hoursLeft % 24);
    return { big: d + 'D ' + h + 'H', sub: 'until day ' + bm.day + ' horde night', state: 'calm' };
  }

  function renderStatus(json) {
    var d = json.data;
    var gt = d.gameTime;
    var counts = d.counts || { players: 0, hostiles: 0, animals: 0 };
    var maxP = (d.server && d.server.maxPlayers) || 8;

    $('statPlayers').textContent = counts.players + '/' + maxP;
    $('statDay').textContent = gt ? gt.days : '–';
    $('statClock').textContent = gt ? pad2(gt.hours) + ':' + pad2(gt.minutes) : '–';
    $('statHostiles').textContent = counts.hostiles;
    $('statAnimals').textContent = counts.animals;
    $('playersCount').textContent = counts.players + '/' + maxP;

    if (d.server) {
      $('worldName').textContent = d.server.world || 'AH42';
      $('worldSub').textContent = (d.server.name || 'AfterHours') + ' · ' + (d.server.version || '') + ' · PVE';
    }

    var bm = bmCountdown(gt, d.bloodmoon);
    $('statBloodmoon').textContent = bm.big;
    $('statBloodmoon').classList.toggle('calm', bm.state === 'calm');
    $('bmBig').textContent = bm.big;
    $('bmSub').textContent = bm.sub;
    var bmPanel = $('bmPanel');
    bmPanel.classList.toggle('calm', bm.state === 'calm');
    bmPanel.classList.toggle('imminent', bm.state !== 'calm');

    latestPlayers = d.players || [];
    renderPlayers(latestPlayers);
    updatePlayerMarkers(latestPlayers);
    updateZombieMarkers(d.zombies || []);
  }

  function renderPlayers(players) {
    var list = $('playersList');
    if (!players || !players.length) {
      list.innerHTML = '<p class="players-empty">The wasteland is quiet… nobody is online right now.</p>';
      return;
    }
    list.innerHTML = players.map(function (p) {
      return '<div class="player-row' + (p.dead ? ' dead' : '') + '">' +
        '<span class="lvl-badge">' + esc(p.level) + '</span>' +
        '<span><span class="player-name">' + esc(p.name) +
          (p.dead ? '<span class="dead-tag">DEAD</span>' : '') + '</span><br>' +
          '<span class="player-meta">' + esc(p.zombieKills) + ' zombie kills · ' + esc(p.deaths) + ' deaths</span>' +
        '</span>' +
        '<span class="player-stats">' +
          '<span>HP <b>' + esc(p.health) + '</b></span>' +
          '<span class="psk">SCORE <b>' + esc(p.score) + '</b></span>' +
          '<span>PING <b>' + esc(p.ping) + '</b></span>' +
        '</span>' +
      '</div>';
    }).join('');
  }

  function pollStatus() {
    fetchStatus().then(function (json) {
      failCount = 0;
      setNavStatus(true);
      renderStatus(json);
    }).catch(function () {
      failCount++;
      if (failCount > 1) setNavStatus(false);
    });
  }

  function setNavStatus(online) {
    $('navDot').className = 'pulse-dot' + (online ? '' : ' off');
    $('navStatusText').textContent = online ? 'ONLINE' : 'OFFLINE';
  }

  /* ================= map ================= */

  var map = null;
  var playerLayer = null;
  var traderLayer = null;
  var zombieLayer = null;
  var tileLayer = null;
  var zoomMeter = null;
  var activeFollow = null;
  var playerMarkers = Object.create(null);
  var traderMarkers = Object.create(null);
  var zombieMarkers = Object.create(null);
  var tileEpoch = Date.now(); // cache-buster, bumped on each tile refresh
  var layerVisibility = { players: true, traders: true, zombies: true };
  var DEFAULT_MAP_CENTER = { x: 1152, z: -2328 }; // centroid of AH42's five spawn points
  var DEFAULT_MAP_ZOOM = 2;

  // World axes (V3.1.0, BlockFaces.cs): north = +z, east = +x.
  // The game renders each tile PNG with north (high z) at the TOP and the
  // tile file y = floor(worldZ / tileSpan). With the CRS below (pixel y
  // grows with -lat), lat = +z puts north up; the tile layer must then flip
  // Leaflet's y (which grows downward) via getTileUrl — same trick as the
  // stock web UI (coords.y = -coords.y - 1).
  function w2ll(x, z) { return [z, x]; } // world -> latlng (lat = +z = north)

  function getMapLayer(name) {
    return { players: playerLayer, traders: traderLayer, zombies: zombieLayer }[name] || null;
  }

  function centerFollow(latlng) {
    if (!map || !latlng) return;
    var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    map.panTo(latlng, { animate: !reduced, duration: 0.45, easeLinearity: 0.25 });
  }

  function beginFollowing(type, key, marker) {
    activeFollow = { type: type, key: String(key), marker: marker };
    centerFollow(marker.getLatLng());
  }

  function stopFollowing(type, key) {
    if (!activeFollow) return;
    if (type != null && (activeFollow.type !== type || activeFollow.key !== String(key))) return;
    activeFollow = null;
  }

  function bindFollowPopup(marker, type, key, html) {
    marker._ahFollowType = type;
    marker._ahFollowKey = String(key);
    if (marker.getPopup()) {
      marker.setPopupContent(html);
      return;
    }
    marker.bindPopup(html, { autoPan: false });
    marker.on('popupopen', function () {
      beginFollowing(marker._ahFollowType, marker._ahFollowKey, marker);
    });
    marker.on('popupclose', function () {
      stopFollowing(marker._ahFollowType, marker._ahFollowKey);
    });
  }

  function removeFollowMarker(type, key, marker, layer) {
    if (activeFollow && activeFollow.type === type && activeFollow.key === String(key)) {
      marker.closePopup();
      stopFollowing(type, key);
    }
    layer.removeLayer(marker);
  }

  function recenterActiveMarker(type, markers) {
    if (!activeFollow || activeFollow.type !== type) return;
    var marker = markers[activeFollow.key];
    if (!marker) {
      stopFollowing(type, activeFollow.key);
      return;
    }
    activeFollow.marker = marker;
    centerFollow(marker.getLatLng());
  }

  function setMapLayerVisibility(name, visible) {
    layerVisibility[name] = visible;
    var layer = getMapLayer(name);
    if (!visible && activeFollow && activeFollow.type === name) {
      if (activeFollow.marker) activeFollow.marker.closePopup();
      stopFollowing(name, activeFollow.key);
    }
    if (map && layer) {
      if (visible && !map.hasLayer(layer)) layer.addTo(map);
      if (!visible && map.hasLayer(layer)) map.removeLayer(layer);
    }

    var button = document.querySelector('[data-map-layer="' + name + '"]');
    if (!button) return;
    button.classList.toggle('is-off', !visible);
    button.setAttribute('aria-pressed', visible ? 'true' : 'false');
    button.setAttribute('title', (visible ? 'Hide ' : 'Show ') + name);
  }

  function initMapLegend() {
    var buttons = document.querySelectorAll('.map-legend-toggle[data-map-layer]');
    Array.prototype.forEach.call(buttons, function (button) {
      var name = button.getAttribute('data-map-layer');
      button.addEventListener('click', function () {
        setMapLayerVisibility(name, !layerVisibility[name]);
      });
      setMapLayerVisibility(name, layerVisibility[name] !== false);
    });
  }

  function updateZoomMeter() {
    if (!map || !zoomMeter) return;
    var min = map.getMinZoom();
    var max = map.getMaxZoom();
    var zoom = map.getZoom();
    var percent = max > min ? Math.round((zoom - min) * 100 / (max - min)) : 100;
    percent = Math.max(0, Math.min(100, percent));

    zoomMeter.querySelector('.map-zoom-meter-fill').style.height = percent + '%';
    zoomMeter.querySelector('.map-zoom-meter-label').textContent = percent + '%';
    zoomMeter.setAttribute('aria-valuenow', percent);
    zoomMeter.setAttribute('aria-valuetext', percent + '% zoom');
    zoomMeter.setAttribute('title', 'Map zoom: ' + percent + '%');
  }

  function initZoomMeter() {
    if (!map || !map.zoomControl) return;
    var control = map.zoomControl.getContainer();
    var zoomOut = control && control.querySelector('.leaflet-control-zoom-out');
    if (!control || !zoomOut) return;

    zoomMeter = document.createElement('div');
    zoomMeter.className = 'map-zoom-meter';
    zoomMeter.tabIndex = 0;
    zoomMeter.setAttribute('role', 'progressbar');
    zoomMeter.setAttribute('aria-label', 'Current map zoom');
    zoomMeter.setAttribute('aria-valuemin', '0');
    zoomMeter.setAttribute('aria-valuemax', '100');
    zoomMeter.innerHTML = '<span class="map-zoom-meter-track"><span class="map-zoom-meter-fill"></span></span>' +
      '<span class="map-zoom-meter-label">0%</span>';
    control.insertBefore(zoomMeter, zoomOut);
    L.DomEvent.disableClickPropagation(zoomMeter);
    L.DomEvent.disableScrollPropagation(zoomMeter);
    map.on('zoom zoomend', updateZoomMeter);
    updateZoomMeter();
  }

  function initMap() {
    return fetch('/api/map/config').then(function (r) { return r.json(); }).then(function (json) {
      var cfg = json.data;
      var maxNative = cfg.maxZoom;
      var s = 1 / Math.pow(2, maxNative);
      var crs = L.extend({}, L.CRS.Simple, {
        transformation: new L.Transformation(s, 0, -s, 0)
      });

      map = L.map('mapEl', {
        crs: crs,
        minZoom: -1,
        maxZoom: maxNative + 2,
        zoomSnap: 0.5,
        zoomDelta: 1,
        zoomControl: true,
        attributionControl: true
      });
      map.attributionControl.setPrefix(false);
      map.attributionControl.addAttribution('AfterHours · fog of war is real');

      var halfX = cfg.mapSize.x / 2;
      var halfZ = cfg.mapSize.z / 2;
      var worldBounds = [[-halfZ, -halfX], [halfZ, halfX]];

      tileLayer = L.tileLayer('/map/{z}/{x}/{y}.png', {
        tileSize: cfg.mapBlockSize,
        minNativeZoom: 0,
        maxNativeZoom: maxNative,
        minZoom: -1,
        maxZoom: maxNative + 2,
        noWrap: true,
        bounds: worldBounds,
        updateWhenIdle: true,
        keepBuffer: 4
      });
      // Leaflet tile y grows downward; the game's tile files grow with +z
      // (north). Flip so north renders up (stock web UI does the same).
      tileLayer.getTileUrl = function (coords) {
        coords.y = -coords.y - 1;
        return L.TileLayer.prototype.getTileUrl.call(tileLayer, coords) + '?t=' + tileEpoch;
      };
      tileLayer.addTo(map);

      traderLayer = L.layerGroup();
      playerLayer = L.layerGroup();
      zombieLayer = L.layerGroup();
      Object.keys(layerVisibility).forEach(function (name) {
        setMapLayerVisibility(name, layerVisibility[name]);
      });
      map.on('zoomend moveend', function () { renderZombieMarkers(false); });
      map.setView(w2ll(DEFAULT_MAP_CENTER.x, DEFAULT_MAP_CENTER.z), DEFAULT_MAP_ZOOM);
      initZoomMeter();
      window.__ahMap = map; // debug handle
      map.setMaxBounds([[-halfZ - 1024, -halfX - 1024], [halfZ + 1024, halfX + 1024]]);
    }).catch(function () {
      $('mapEl').innerHTML = '<p style="padding:2rem;color:var(--fog-dim)">Map unavailable right now.</p>';
    });
  }

  var TRADER_SVG = 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="28" height="28">' +
    '<circle cx="12" cy="12" r="9.6" fill="#e8b23a" stroke="#5d4310" stroke-width="1.6"/>' +
    '<circle cx="12" cy="12" r="6.6" fill="none" stroke="#8a6518" stroke-width="0.9"/>' +
    '<text x="12" y="16.4" font-size="13" font-family="Arial, sans-serif" font-weight="bold" text-anchor="middle" fill="#3a2a05">$</text></svg>');

  function loadTraders() {
    if (!map) return;
    fetch('/api/afterhours/traders').then(function (r) {
      if (!r.ok) throw new Error('traders ' + r.status);
      return r.json();
    }).then(function (json) {
      var traders = json.data ? json.data.traders : [];
      var seen = Object.create(null);
      traders.forEach(function (t) {
        if (!t.position) return;
        var ll = w2ll(t.position.x, t.position.z);
        var key = t.name + '|' + t.position.x + '|' + t.position.z;
        var marker = traderMarkers[key];
        seen[key] = true;
        if (!marker) {
          var icon = L.icon({
            iconUrl: TRADER_SVG,
            iconSize: [28, 28],
            iconAnchor: [14, 14],
            popupAnchor: [0, -14],
            className: 'trader-icon'
          });
          marker = L.marker(ll, { icon: icon, title: t.name }).addTo(traderLayer);
          traderMarkers[key] = marker;
        } else {
          marker.setLatLng(ll);
        }
        bindFollowPopup(marker, 'traders', key,
          '<b>' + esc(t.name) + '</b><br><span class="popup-sub">Trader · following while open</span>');
      });

      Object.keys(traderMarkers).forEach(function (key) {
        if (seen[key]) return;
        removeFollowMarker('traders', key, traderMarkers[key], traderLayer);
        delete traderMarkers[key];
      });
      recenterActiveMarker('traders', traderMarkers);
    }).catch(function () { /* traders optional */ });
  }

  // Fog of war is re-rendered server-side as players explore; refresh the
  // visible tiles periodically so the map is actually live (and so no
  // browser-cached stale tiles linger).
  function refreshTiles() {
    if (!tileLayer) return;
    tileEpoch = Date.now();
    tileLayer.redraw();
  }

  function updatePlayerMarkers(players) {
    if (!map || !playerLayer) return;
    var seen = Object.create(null);
    (players || []).forEach(function (p) {
      if (!p.position) return;
      var key = String(p.name);
      var ll = w2ll(p.position.x, p.position.z);
      var marker = playerMarkers[key];
      seen[key] = true;
      if (!marker) {
        marker = L.marker(ll, { interactive: true, keyboard: false }).addTo(playerLayer);
        playerMarkers[key] = marker;
      } else {
        marker.setLatLng(ll);
      }
      if (marker._ahDead !== !!p.dead || marker._ahName !== p.name) {
        marker.setIcon(L.divIcon({
          className: '',
          html: '<div class="ping' + (p.dead ? ' dead' : '') + '"></div>' +
                '<div class="ping-label">' + esc(p.name) + '</div>',
          iconSize: [14, 14],
          iconAnchor: [7, 7]
        }));
        marker._ahDead = !!p.dead;
        marker._ahName = p.name;
      }
      bindFollowPopup(marker, 'players', key,
        '<b>' + esc(p.name) + '</b><br>Level ' + esc(p.level) +
        ' · HP ' + esc(p.health) + '<br>' + esc(p.zombieKills) + ' zombie kills · ' +
        esc(p.deaths) + ' deaths' + (p.dead ? '<br>Currently dead' : '') +
        '<br><span class="popup-sub">Following while open</span>');
    });

    Object.keys(playerMarkers).forEach(function (key) {
      if (seen[key]) return;
      removeFollowMarker('players', key, playerMarkers[key], playerLayer);
      delete playerMarkers[key];
    });
    recenterActiveMarker('players', playerMarkers);
  }

  function updateZombieMarkers(zombies) {
    latestZombies = zombies || [];
    renderZombieMarkers(true);
  }

  // Active zombies are a small set (the server only keeps nearby entities
  // loaded), so a lightweight pixel-distance cluster avoids another plugin.
  // Rebuilding on zoom/move makes nearby dots merge as the map zooms out.
  // The API's transient entity id keeps a selected zombie stable between polls.
  function renderZombieMarkers(recenterFollow) {
    if (!map || !zombieLayer) return;

    var clusterRadius = 34;
    var clusterRadiusSq = clusterRadius * clusterRadius;
    var groups = [];

    latestZombies.forEach(function (zombie, index) {
      if (!zombie.position) return;
      var id = zombie.id == null ? 'legacy-' + index : String(zombie.id);
      var ll = w2ll(zombie.position.x, zombie.position.z);
      var point = map.latLngToLayerPoint(ll);
      var nearest = null;
      var nearestDistance = clusterRadiusSq + 1;

      groups.forEach(function (group) {
        var dx = point.x - group.point.x;
        var dy = point.y - group.point.y;
        var distance = dx * dx + dy * dy;
        if (distance <= clusterRadiusSq && distance < nearestDistance) {
          nearest = group;
          nearestDistance = distance;
        }
      });

      if (!nearest) {
        groups.push({ count: 1, lat: ll[0], lng: ll[1], point: point, members: [id] });
        return;
      }

      nearest.count++;
      nearest.members.push(id);
      nearest.lat += (ll[0] - nearest.lat) / nearest.count;
      nearest.lng += (ll[1] - nearest.lng) / nearest.count;
      nearest.point.x += (point.x - nearest.point.x) / nearest.count;
      nearest.point.y += (point.y - nearest.point.y) / nearest.count;
    });

    var seen = Object.create(null);
    groups.forEach(function (group) {
      var key = group.members[0];
      if (activeFollow && activeFollow.type === 'zombies' && group.members.indexOf(activeFollow.key) !== -1) {
        key = activeFollow.key;
      }

      var clustered = group.count > 1;
      var label = clustered ? group.count + ' zombies' : 'Zombie';
      var marker = zombieMarkers[key];
      var ll = [group.lat, group.lng];
      seen[key] = true;
      if (!marker) {
        marker = L.marker(ll, {
          interactive: true,
          keyboard: false,
          title: label
        }).addTo(zombieLayer);
        zombieMarkers[key] = marker;
      } else {
        marker.setLatLng(ll);
      }
      if (marker._ahZombieCount !== group.count) {
        marker.setIcon(L.divIcon({
          className: 'zombie-marker',
          html: clustered
            ? '<div class="zombie-cluster">' + group.count + '</div>'
            : '<div class="zombie-dot"></div>',
          iconSize: [34, 34],
          iconAnchor: [17, 17]
        }));
        marker._ahZombieCount = group.count;
      }
      bindFollowPopup(marker, 'zombies', key,
        '<b>' + esc(label) + '</b><br><span class="popup-sub">Active infected · following while open</span>');
    });

    Object.keys(zombieMarkers).forEach(function (key) {
      if (seen[key]) return;
      removeFollowMarker('zombies', key, zombieMarkers[key], zombieLayer);
      delete zombieMarkers[key];
    });
    if (recenterFollow) recenterActiveMarker('zombies', zombieMarkers);
  }

  /* ================= mods ================= */

  /* ================= modpack version & update notice ================= */

  function fmtDate(iso) {
    var d = new Date(iso);
    if (isNaN(d.getTime())) return iso || '';
    return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }

  function rememberPack(v) {
    try { localStorage.setItem('ahPackVersion', v); } catch (e) {}
  }

  function hidePackBanner() {
    var b = $('packBanner');
    if (b) b.hidden = true;
  }

  function initPack(pack, mods) {
    if (!pack || !pack.version || !$('packVersion')) return;
    var v = pack.version;
    $('packVersion').textContent = 'v' + v;
    $('packUpdated').textContent = fmtDate(pack.built);

    // What's inside the pack (client mods + their versions)
    $('packContents').innerHTML = (mods || []).filter(function (m) { return m.client; }).map(function (m) {
      return '<span class="dl-mod">' + esc(m.name) + (m.version ? ' <b>v' + esc(m.version) + '</b>' : '') + '</span>';
    }).join('');

    // Cache-bust the download URL so an updated pack is never a stale cached zip.
    var dl = document.querySelector('.dl-panel a.btn');
    if (dl) {
      dl.setAttribute('href', '/files/downloads/AfterHours_ClientMods.zip?v=' + v);
      dl.addEventListener('click', function () { rememberPack(v); hidePackBanner(); });
    }

    // Update notice: only for returning visitors whose last-seen pack differs.
    var seen = null;
    try { seen = localStorage.getItem('ahPackVersion'); } catch (e) {}
    if (seen === v) { hidePackBanner(); return; }
    if (seen === null) { rememberPack(v); return; } // first visit — nothing to compare
    $('packBannerVersion').textContent = 'v' + v;
    $('packBannerDate').textContent = '· updated ' + fmtDate(pack.built);
    $('packBanner').hidden = false;
    var ack = function () { rememberPack(v); hidePackBanner(); };
    $('packBannerBtn').addEventListener('click', ack);
    $('packBannerClose').addEventListener('click', ack);
  }

  function loadMods() {
    fetch('/files/assets/mods.json').then(function (r) { return r.json(); }).then(function (json) {
      initPack(json.pack, json.mods);
      var mods = (json.mods || []).slice().sort(function (a, b) {
        return (b.highlight - a.highlight) || a.name.localeCompare(b.name);
      });
      $('modsGrid').innerHTML = mods.map(function (m) {
        return '<div class="panel mod-card' + (m.highlight ? ' hl' : '') + '">' +
          '<h4 class="mod-name">' + esc(m.name) + '</h4>' +
          '<p class="mod-desc">' + esc(m.description) + '</p>' +
          '<div class="mod-foot">' +
            '<span class="mod-badge ' + (m.client ? 'client' : 'server') + '">' +
              (m.client ? 'IN MODPACK' : 'SERVER SIDE') + '</span>' +
            (m.version ? '<span>v' + esc(m.version) + '</span>' : '') +
            (m.author ? '<span>by ' + esc(m.author) + '</span>' : '') +
            (m.website ? '<a href="' + esc(m.website) + '" target="_blank" rel="noopener">source</a>' : '') +
          '</div>' +
        '</div>';
      }).join('');
    }).catch(function () {
      $('modsGrid').innerHTML = '<p class="players-empty">Mod list failed to load.</p>';
    });
  }

  /* ================= misc UI ================= */

  function initCopy() {
    var chip = $('addrChip');
    chip.addEventListener('click', function () {
      var done = function () {
        chip.classList.add('copied');
        $('addrValue').textContent = 'COPIED TO CLIPBOARD';
        setTimeout(function () {
          chip.classList.remove('copied');
          $('addrValue').textContent = SERVER_ADDR;
        }, 1600);
      };
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(SERVER_ADDR).then(done, done);
      } else {
        var ta = document.createElement('textarea');
        ta.value = SERVER_ADDR;
        document.body.appendChild(ta);
        ta.select();
        try { document.execCommand('copy'); } catch (e) {}
        document.body.removeChild(ta);
        done();
      }
    });
  }

  function initZipSize() {
    fetch('/files/downloads/AfterHours_ClientMods.zip', { method: 'HEAD' }).then(function (r) {
      var len = r.headers.get('content-length');
      if (!len) return;
      var kb = Math.round(len / 1024);
      $('zipSize').textContent = '· ' + (kb > 1024 ? (kb / 1024).toFixed(1) + ' MB' : kb + ' KB');
    }).catch(function () {});
  }

  function initVideo() {
    var v = $('bgVideo');
    if (!v) return;
    if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      v.pause();
      v.removeAttribute('autoplay');
    }
    document.addEventListener('visibilitychange', function () {
      if (document.hidden) { v.pause(); }
      else if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) { v.play().catch(function () {}); }
    });
  }

  /* ================= boot ================= */

  $('navDot').className = 'pulse-dot wait';
  initCopy();
  initZipSize();
  initVideo();
  initMapLegend();
  loadMods();
  initMap().then(function () {
    loadTraders();
    setInterval(loadTraders, MARKERS_MS);
    setInterval(refreshTiles, MARKERS_MS);
    updatePlayerMarkers(latestPlayers);
    renderZombieMarkers();
  });
  pollStatus();
  setInterval(pollStatus, STATUS_MS);
})();
