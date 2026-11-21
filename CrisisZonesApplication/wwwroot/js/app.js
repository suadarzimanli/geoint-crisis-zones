/* Crisis Zones – map, filters, forecast and charts.
   All data text is inserted with textContent or esc(); nothing from the data is parsed as HTML. */
(() => {
  'use strict';

  const $ = (id) => document.getElementById(id);
  const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const nf = new Intl.NumberFormat('en-GB');
  const nf1 = new Intl.NumberFormat('en-GB', { maximumFractionDigits: 1 });
  const monthName = (ym, style = 'long') => {
    const [y, m] = ym.split('-').map(Number);
    return new Date(Date.UTC(y, m - 1, 1)).toLocaleDateString('en-GB', { month: style, year: 'numeric', timeZone: 'UTC' });
  };
  const shortMonth = (ym) => monthName(ym, 'short').split(' ')[0];
  // 'yyyy-MM-dd' is a calendar date: format in UTC so the viewer's timezone cannot shift it by a day
  const dayName = (iso, opts = { day: 'numeric', month: 'short', year: 'numeric' }) =>
    new Date(`${iso.slice(0, 10)}T00:00:00Z`).toLocaleDateString('en-GB', { ...opts, timeZone: 'UTC' });
  const css = (name) => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

  // Map colours: three slots (identity), see css --series-1..3
  const TYPE_SLOT = (type) => (type === 'Protests' ? 1 : type === 'Riots' ? 2 : 3);

  const state = {
    events: [],        // compact events from /api/events
    visible: [],       // events that pass the filters
    meta: null,
    forecast: null,
    map: null,
    markers: [],       // google.maps.Marker per event (same index as events)
    clusterer: null,
    infoTarget: null,  // selected event id
    charts: {},
    chartsDirty: true,
  };

  const F = {
    type: $('f-type'), sub: $('f-sub'), region: $('f-region'), city: $('f-city'),
    from: $('f-from'), to: $('f-to'), interaction: $('f-interaction'), cluster: $('f-cluster'),
  };

  /* ---------------- Panel: tabs and phone sheet ---------------- */
  const panel = $('panel');
  const tabs = [...document.querySelectorAll('[role="tab"]')];
  const isPhone = () => window.matchMedia('(max-width: 767px)').matches;

  function setSheet(open) {
    panel.dataset.state = open ? 'open' : 'peek';
    $('sheet-handle').setAttribute('aria-expanded', String(open));
    if (open) { $('event-card').hidden = true; document.querySelector('.map-wrap').classList.remove('card-open'); }
  }
  $('sheet-handle').addEventListener('click', () => setSheet(panel.dataset.state !== 'open'));

  function selectTab(tab, focus = false) {
    tabs.forEach((t) => {
      const on = t === tab;
      t.setAttribute('aria-selected', String(on));
      t.tabIndex = on ? 0 : -1;
      $(t.getAttribute('aria-controls')).hidden = !on;
    });
    if (focus) tab.focus();
    if (tab.id === 'tab-insights') renderCharts();
    if (tab.id === 'tab-forecast') loadForecast();
  }
  tabs.forEach((t, i) => {
    t.addEventListener('click', () => {
      const already = t.getAttribute('aria-selected') === 'true';
      selectTab(t);
      if (isPhone()) setSheet(already ? panel.dataset.state !== 'open' : true);
    });
    t.addEventListener('keydown', (e) => {
      const d = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : 0;
      if (d) { e.preventDefault(); selectTab(tabs[(i + d + tabs.length) % tabs.length], true); }
    });
  });

  /* ---------------- Data loading ---------------- */
  async function getJson(url) {
    const r = await fetch(url, { headers: { Accept: 'application/json' } });
    const body = await r.json().catch(() => ({}));
    if (!r.ok) throw new Error(body.error || `Request failed (${r.status})`);
    return body;
  }

  async function init() {
    try {
      state.meta = await getJson('/api/meta');
      if (!state.meta.available) throw new Error(state.meta.error);
      state.events = await getJson('/api/events');
    } catch (err) {
      $('summary').textContent = err.message || 'Event data could not be loaded.';
      $('summary').classList.add('error');
      showMapMessage(err.message);
      return;
    }
    buildFilterOptions();
    $('filters').disabled = false;
    applyFilters();
    loadMaps();
  }

  /* ---------------- Filters ---------------- */
  function countBy(list, key) {
    const m = new Map();
    for (const e of list) m.set(e[key], (m.get(e[key]) || 0) + 1);
    return [...m.entries()].sort((a, b) => b[1] - a[1] || String(a[0]).localeCompare(String(b[0])));
  }

  function fillSelect(sel, entries, allLabel, keep = true) {
    const current = sel.value;
    sel.replaceChildren(new Option(allLabel, ''));
    for (const [value, n] of entries) sel.add(new Option(n == null ? value : `${value} (${nf.format(n)})`, value));
    sel.value = keep && entries.some(([v]) => v === current) ? current : '';
  }

  function buildFilterOptions() {
    const ev = state.events;
    fillSelect(F.type, countBy(ev, 'type'), 'All event types');
    fillSelect(F.region, countBy(ev, 'region').sort((a, b) => a[0].localeCompare(b[0], 'pl')), 'All voivodeships');
    fillSelect(F.interaction, countBy(ev, 'interaction'), 'All interactions');
    const months = [...new Set(ev.map((e) => e.date.slice(0, 7)))].sort();
    for (const sel of [F.from, F.to]) sel.replaceChildren(...months.map((m) => new Option(monthName(m, 'short'), m)));
    F.from.value = months[0];
    F.to.value = months[months.length - 1];
    F.from.dataset.default = months[0];
    F.to.dataset.default = months[months.length - 1];
    updateDependentOptions();
  }

  function updateDependentOptions() {
    const ev = state.events;
    fillSelect(F.sub, countBy(F.type.value ? ev.filter((e) => e.type === F.type.value) : ev, 'sub'), 'All sub-event types');
    const inRegion = F.region.value ? ev.filter((e) => e.region === F.region.value) : ev;
    fillSelect(F.city, countBy(inRegion, 'city').sort((a, b) => a[0].localeCompare(b[0], 'pl')), 'All counties');
  }

  function applyFilters() {
    let from = F.from.value, to = F.to.value;
    if (from && to && from > to) { [from, to] = [to, from]; F.from.value = from; F.to.value = to; }
    const f = { type: F.type.value, sub: F.sub.value, region: F.region.value, city: F.city.value, interaction: F.interaction.value };
    const keep = state.events.map((e) => {
      const m = e.date.slice(0, 7);
      return (!f.type || e.type === f.type) && (!f.sub || e.sub === f.sub) && (!f.region || e.region === f.region) &&
        (!f.city || e.city === f.city) && (!f.interaction || e.interaction === f.interaction) &&
        (!from || m >= from) && (!to || m <= to);
    });
    state.visible = state.events.filter((_, i) => keep[i]);
    const total = state.events.length, n = state.visible.length;
    $('summary').innerHTML = n === total
      ? `<strong>${nf.format(n)}</strong> events · ${esc(monthName(state.meta.firstDate.slice(0, 7), 'short'))} – ${esc(dayName(state.meta.lastDate))}`
      : `<strong>${nf.format(n)}</strong> of ${nf.format(total)} events match the filters`;
    const active = Object.values(f).some(Boolean) || from !== F.from.dataset.default || to !== F.to.dataset.default;
    $('f-reset').disabled = !active;
    const browse = $('f-browse');
    browse.disabled = n === 0 || n > BROWSE_MAX;
    browse.textContent = n === 0 ? 'No events to read' : n > BROWSE_MAX ? `Narrow the filters to read events (max ${nf.format(BROWSE_MAX)})` : `Read these ${nf.format(n)} events, newest first`;
    updateMarkers(keep);
    state.chartsDirty = true;
    if (!$('view-insights').hidden) renderCharts();
  }

  F.type.addEventListener('change', () => { updateDependentOptions(); applyFilters(); });
  F.region.addEventListener('change', () => { updateDependentOptions(); applyFilters(); fitToVisible(); });
  F.city.addEventListener('change', () => { applyFilters(); fitToVisible(); });
  for (const s of [F.sub, F.from, F.to, F.interaction]) s.addEventListener('change', applyFilters);
  F.cluster.addEventListener('change', () => updateMarkers());
  // Keyboard- and no-map access to the event records of the current selection
  const BROWSE_MAX = 2000;
  $('f-browse').addEventListener('click', () => {
    if (!state.visible.length || state.visible.length > BROWSE_MAX) return;
    openStack([...state.visible].sort(byDateDesc), 0, 'in this selection');
    $('ev-close').focus();
  });
  $('f-reset').addEventListener('click', () => {
    for (const s of [F.type, F.region, F.city, F.sub, F.interaction]) s.value = '';
    F.from.value = F.from.dataset.default; F.to.value = F.to.dataset.default;
    updateDependentOptions(); applyFilters(); fitToVisible(true);
  });

  /* ---------------- Map ---------------- */
  function showMapMessage(text) {
    const m = $('map-message');
    m.textContent = text;
    m.hidden = !text;
  }

  function loadMaps() {
    const key = $('data-config').dataset.mapsKey;
    if (!key) {
      showMapMessage('No Google Maps key configured, so the map is off. Add it to appsettings.Development.local.json (see README). Forecast and Insights still work.');
      return;
    }
    window.gm_authFailure = () => showMapMessage('Google Maps rejected the API key. Check the key and its allowed sites in Google Cloud.');
    window.__czInitMap = initMap;
    const s = document.createElement('script');
    s.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(key)}&callback=__czInitMap&loading=async&v=weekly`;
    s.async = true;
    s.onerror = () => showMapMessage('Google Maps could not be loaded. Check your connection.');
    document.head.appendChild(s);
  }

  const isDark = () => !!(window.CrisisZonesTheme && window.CrisisZonesTheme.isDark());
  // Neutral dark basemap: quiet greys so the three data colours stay the strongest marks
  const DARK_MAP = [
    { elementType: 'geometry', stylers: [{ color: '#1d2128' }] },
    { elementType: 'labels.text.fill', stylers: [{ color: '#9aa3b2' }] },
    { elementType: 'labels.text.stroke', stylers: [{ color: '#1d2128' }] },
    { featureType: 'administrative', elementType: 'geometry.stroke', stylers: [{ color: '#4b5563' }] },
    { featureType: 'administrative.country', elementType: 'geometry.stroke', stylers: [{ color: '#7b8494' }, { weight: 1.2 }] },
    { featureType: 'administrative.locality', elementType: 'labels.text.fill', stylers: [{ color: '#c3c9d4' }] },
    { featureType: 'poi', elementType: 'labels', stylers: [{ visibility: 'off' }] },
    { featureType: 'poi.park', elementType: 'geometry', stylers: [{ color: '#1f2a26' }] },
    { featureType: 'road', elementType: 'geometry', stylers: [{ color: '#2b313b' }] },
    { featureType: 'road', elementType: 'geometry.stroke', stylers: [{ color: '#1a1e25' }] },
    { featureType: 'road', elementType: 'labels.text.fill', stylers: [{ color: '#8b93a1' }] },
    { featureType: 'road.highway', elementType: 'geometry', stylers: [{ color: '#3a4150' }] },
    { featureType: 'transit', elementType: 'geometry', stylers: [{ color: '#262c35' }] },
    { featureType: 'water', elementType: 'geometry', stylers: [{ color: '#0f1a26' }] },
    { featureType: 'water', elementType: 'labels.text.fill', stylers: [{ color: '#4f5b6d' }] },
  ];
  const POLAND_CENTER = { lat: 52.05, lng: 19.3 };
  const POLAND = { north: 54.85, south: 49.0, west: 14.1, east: 24.15 };
  // Fractional zoom lets Poland fill the map at every screen size
  function showPoland() {
    state.map.fitBounds(POLAND, isPhone() ? 4 : 12);
  }

  function markerIcon(slot) {
    return {
      path: google.maps.SymbolPath.CIRCLE,
      scale: 5.5,
      fillColor: css(`--series-${slot}`),
      fillOpacity: 0.95,
      strokeColor: css('--ring'),
      strokeWeight: 1.5,
    };
  }

  function initMap() {
    state.map = new google.maps.Map($('map'), {
      center: POLAND_CENTER,
      zoom: 6,
      minZoom: 4,
      isFractionalZoomEnabled: true,
      mapTypeControl: true,
      mapTypeControlOptions: { position: google.maps.ControlPosition.TOP_RIGHT, style: google.maps.MapTypeControlStyle.DROPDOWN_MENU },
      streetViewControl: true,
      fullscreenControl: false,
      clickableIcons: false,
      gestureHandling: 'greedy',
      styles: isDark() ? DARK_MAP : null,
    });
    showPoland();
    state.map.addListener('click', () => { if (isPhone() && panel.dataset.state === 'open') setSheet(false); });
    let icons = { 1: markerIcon(1), 2: markerIcon(2), 3: markerIcon(3) };
    state.markers = state.events.map((e) => {
      const m = new google.maps.Marker({ position: { lat: e.lat, lng: e.lng }, icon: icons[TYPE_SLOT(e.type)], title: `${e.sub}, ${e.city}, ${e.date}`, optimized: true });
      m.__ev = e;
      m.addListener('click', () => openStackAt(e));
      return m;
    });
    const renderer = {
      render({ count, position }) {
        const size = count < 10 ? 30 : count < 100 ? 36 : count < 1000 ? 44 : 52;
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${size} ${size}"><circle cx="${size / 2}" cy="${size / 2}" r="${size / 2 - 2}" fill="${css('--cluster')}" fill-opacity="0.9" stroke="${css('--ring')}" stroke-width="2"/></svg>`;
        return new google.maps.Marker({
          position,
          icon: { url: 'data:image/svg+xml;charset=UTF-8,' + encodeURIComponent(svg), anchor: new google.maps.Point(size / 2, size / 2) },
          label: { text: count >= 1000 ? `${(count / 1000).toFixed(1)}k` : String(count), color: css('--cluster-text'), fontSize: '12px', fontWeight: '600' },
          title: `${nf.format(count)} events – zoom in`,
          zIndex: 1000 + count,
        });
      },
    };
    // A cluster whose events share one place (ACLED often geocodes to the town centre) cannot be
    // split by zooming, so clicking it opens the events as a list instead.
    const onClusterClick = (_, cluster, map) => {
      const evs = cluster.markers.map((m) => m.__ev);
      const samePlace = evs.every((e) => e.lat === evs[0].lat && e.lng === evs[0].lng);
      if (samePlace || map.getZoom() >= 16) openStack(evs.sort(byDateDesc), 0);
      else map.fitBounds(cluster.bounds);
    };
    // The clustering index works on whole zoom levels; the map itself may sit between them
    const base = new markerClusterer.SuperClusterAlgorithm({ radius: 80, maxZoom: 22 });
    const algorithm = {
      calculate(opts) {
        const m = opts.map;
        const rounded = new Proxy(m, { get: (t, k) => (k === 'getZoom' ? () => Math.round(t.getZoom()) : typeof t[k] === 'function' ? t[k].bind(t) : t[k]) });
        return base.calculate({ ...opts, map: rounded });
      },
    };
    state.clusterer = new markerClusterer.MarkerClusterer({ map: state.map, markers: [], renderer, algorithm, onClusterClick });
    updateMarkers();
    initPlaceSearch();

    // Re-colour basemap, points and groups when the theme changes
    document.addEventListener('cz-themechange', () => {
      state.map.setOptions({ styles: isDark() ? DARK_MAP : null });
      icons = { 1: markerIcon(1), 2: markerIcon(2), 3: markerIcon(3) };
      state.markers.forEach((m) => m.setIcon(icons[TYPE_SLOT(m.__ev.type)]));
      state.clusterer.render();
    });
  }

  function updateMarkers(keep) {
    if (!state.map) return;
    if (!keep) {
      const ids = new Set(state.visible.map((e) => e.id));
      keep = state.events.map((e) => ids.has(e.id));
    }
    const shown = state.markers.filter((_, i) => keep[i]);
    state.clusterer.clearMarkers(true);
    if (F.cluster.checked) {
      state.markers.forEach((m) => m.setMap(null));
      state.clusterer.addMarkers(shown);
    } else {
      state.markers.forEach((m, i) => m.setMap(keep[i] ? state.map : null));
    }
  }

  function fitToVisible(all = false) {
    if (!state.map) return;
    const list = all ? state.events : state.visible;
    if (!list.length) return;
    if (all || (!F.region.value && !F.city.value)) { showPoland(); return; }
    const b = new google.maps.LatLngBounds();
    list.forEach((e) => b.extend({ lat: e.lat, lng: e.lng }));
    state.map.fitBounds(b, isPhone() ? 40 : 60);
    google.maps.event.addListenerOnce(state.map, 'idle', () => { if (state.map.getZoom() > 13) state.map.setZoom(13); });
  }

  /* ---------------- Place search (Geocoding, restricted to Poland) ---------------- */
  function initPlaceSearch() {
    const geocoder = new google.maps.Geocoder();
    $('place-form').addEventListener('submit', (ev) => {
      ev.preventDefault();
      const q = $('place-input').value.trim();
      if (!q) return;
      geocoder.geocode({ address: q, region: 'pl', componentRestrictions: { country: 'PL' } })
        .then(({ results }) => {
          if (!results.length) throw new Error();
          const r = results[0];
          if (r.geometry.viewport) state.map.fitBounds(r.geometry.viewport);
          else { state.map.setCenter(r.geometry.location); state.map.setZoom(13); }
          if (isPhone()) setSheet(false);
        })
        .catch((err) => {
          const denied = err && (err.code === 'REQUEST_DENIED' || /denied|not authorized/i.test(err.message || ''));
          $('summary').textContent = denied
            ? 'Place search is not enabled for this Maps key (it needs the Geocoding API).'
            : `No place found for “${q}” in Poland.`;
        });
    });
  }

  /* ---------------- Event details ---------------- */
  function byDateDesc(a, b) { return b.date.localeCompare(a.date) || a.id.localeCompare(b.id); }

  // All visible events at the clicked event's exact position, newest first
  function openStackAt(ev) {
    const list = state.visible.filter((e) => e.lat === ev.lat && e.lng === ev.lng).sort(byDateDesc);
    openStack(list.length ? list : [ev], Math.max(0, list.findIndex((e) => e.id === ev.id)));
  }

  function openStack(list, index, label = 'at this place') {
    state.stackLabel = label;
    state.stack = list;
    state.stackIndex = index;
    openEvent(list[index].id);
  }

  async function openEvent(id) {
    state.infoTarget = id;
    const card = $('event-card');
    $('ev-body').innerHTML = '<p class="muted">Loading event…</p>';
    card.hidden = false;
    document.querySelector('.map-wrap').classList.add('card-open');
    if (isPhone()) setSheet(false);
    try {
      const e = await getJson(`/api/events/${encodeURIComponent(id)}`);
      if (state.infoTarget !== id) return;
      const date = dayName(e.date, { day: 'numeric', month: 'long', year: 'numeric' });
      const place = [e.location, e.admin3 && e.admin3 !== e.location ? e.admin3 : '', e.city, e.region].filter(Boolean);
      const actors = [e.actor1, e.assocActor1, e.actor2, e.assocActor2].filter(Boolean);
      const precise = { 1: 'exact place', 2: 'nearby area', 3: 'region only' }[e.geoPrecision] || '';
      const st = state.stack || [];
      const stackNav = st.length > 1
        ? `<div class="ev-stack"><button type="button" data-step="-1" aria-label="Newer event" ${state.stackIndex === 0 ? 'disabled' : ''}>‹</button>
           <span>${nf.format(state.stackIndex + 1)} of ${nf.format(st.length)} events ${esc(state.stackLabel || 'at this place')}</span>
           <button type="button" data-step="1" aria-label="Older event" ${state.stackIndex === st.length - 1 ? 'disabled' : ''}>›</button></div>`
        : '';
      $('ev-body').innerHTML = `${stackNav}
        <p class="ev-kicker"><span class="dot" style="--c:var(--series-${TYPE_SLOT(e.type)})"></span>${esc(e.type)}</p>
        <h2 id="ev-title">${esc(e.sub)}</h2>
        <p class="ev-meta">${esc(date)} · ${esc(place.join(', '))}</p>
        ${e.notes ? `<p class="ev-notes">${esc(e.notes)}</p>` : ''}
        <dl class="ev-facts">
          ${actors.length ? `<dt>Actors</dt><dd>${esc(actors.join('; '))}</dd>` : ''}
          <dt>Interaction</dt><dd>${esc(e.interaction || '—')}</dd>
          <dt>Fatalities</dt><dd>${esc(e.fatalities)}</dd>
          ${precise ? `<dt>Location precision</dt><dd>${esc(precise)}</dd>` : ''}
          ${e.source ? `<dt>Reported by</dt><dd>${esc(e.source)}${e.sourceScale ? ` <span class="muted">(${esc(e.sourceScale.toLowerCase())})</span>` : ''}</dd>` : ''}
        </dl>
        <p class="ev-foot muted small">ACLED event ${esc(e.id)}</p>`;
      const pos = { lat: e.lat, lng: e.lng };
      if (state.map && !state.map.getBounds()?.contains(pos)) state.map.panTo(pos);
    } catch (err) {
      $('ev-body').innerHTML = `<p class="error">${esc(err.message)}</p>`;
    }
  }
  $('ev-close').addEventListener('click', () => {
    $('event-card').hidden = true;
    document.querySelector('.map-wrap').classList.remove('card-open');
    const wasBrowsing = state.stackLabel === 'in this selection';
    state.infoTarget = null; state.stack = null; state.stackLabel = null;
    if (wasBrowsing) $('f-browse').focus();
  });
  $('ev-body').addEventListener('click', (e) => {
    const b = e.target.closest('[data-step]');
    if (!b || !state.stack) return;
    const i = state.stackIndex + Number(b.dataset.step);
    if (i < 0 || i >= state.stack.length) return;
    state.stackIndex = i;
    openEvent(state.stack[i].id).then(() => $('ev-body').querySelector(`[data-step="${b.dataset.step}"]:not(:disabled)`)?.focus());
  });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && !$('event-card').hidden) $('ev-close').click(); });

  /* ---------------- Forecast ---------------- */
  async function loadForecast() {
    if (state.forecast) return;
    $('forecast-head').innerHTML = '<p class="muted">Loading forecast…</p>';
    try {
      state.forecast = await getJson('/api/forecast');
    } catch (err) {
      $('forecast-head').innerHTML = `<p class="error">${esc(err.message)}</p>`;
      return;
    }
    const fc = state.forecast;
    const basis = fc.basisMonths;
    const ma = fc.backtest[0], naive = fc.backtest[1], zero = fc.backtest[2];
    const partial = fc.partialUntil
      ? `<p class="note">ACLED had recorded events up to ${esc(dayName(fc.partialUntil, { day: 'numeric', month: 'long' }))} when the data were downloaded. Those are shown as <em>so far</em> and are not used by the forecast.</p>`
      : '';
    $('forecast-head').innerHTML = `
      <p class="kicker">Forecast for</p>
      <h2>${esc(monthName(fc.targetMonth))}</h2>
      <p>Expected number of events per county: the average of the ${fc.window} latest complete months (${esc(shortMonth(basis[0]))}–${esc(monthName(basis[basis.length - 1], 'short'))}).</p>
      ${ma ? `<details class="accuracy"><summary>How accurate is this?</summary>
        <p>Tested on the ${fc.backtestMonths} months up to ${esc(monthName(fc.lastCompleteMonth, 'short'))}, predicting each month from the months before it, over every county:</p>
        <table><thead><tr><th scope="col">Method</th><th scope="col">Typical error<br><span class="muted">RMSE</span></th><th scope="col">Mean abs. error</th></tr></thead><tbody>
        ${[ma, naive, zero].map((s, i) => `<tr${i === 0 ? ' class="hl"' : ''}><th scope="row">${esc(s.method)}</th><td>${s.rmse.toFixed(2)}</td><td>${s.mae.toFixed(2)}</td></tr>`).join('')}
        </tbody></table>
        <p class="muted small">Errors are in events per county and month. Most county-months have no events, which is why “always zero” has a low mean error but misses every active month. Details: About &amp; data.</p>
      </details>` : ''}
      ${partial}`;
    renderForecastList();
  }

  function sparkline(values) {
    const w = 84, h = 24, max = Math.max(1, ...values);
    const step = w / Math.max(1, values.length - 1);
    const pts = values.map((v, i) => `${(i * step).toFixed(1)},${(h - 2 - (v / max) * (h - 4)).toFixed(1)}`).join(' ');
    return `<svg class="spark" viewBox="0 0 ${w} ${h}" width="${w}" height="${h}" aria-hidden="true"><polyline points="${pts}" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linejoin="round" stroke-linecap="round"/></svg>`;
  }

  function renderForecastList() {
    const fc = state.forecast;
    if (!fc) return;
    const q = $('fc-search').value.trim().toLocaleLowerCase('pl');
    const withZero = $('fc-zero').checked;
    const sort = $('fc-sort').value;
    let list = fc.cities.filter((c) => (withZero || c.predicted > 0) &&
      (!q || c.city.toLocaleLowerCase('pl').includes(q) || c.region.toLocaleLowerCase('pl').includes(q)));
    if (sort === 'name') list = [...list].sort((a, b) => a.city.localeCompare(b.city, 'pl'));
    if (sort === 'total') list = [...list].sort((a, b) => b.total - a.total);
    const LIMIT = 60;
    const shown = list.slice(0, LIMIT);
    const target = shortMonth(fc.targetMonth);
    $('forecast-list').innerHTML = shown.length ? shown.map((c) => {
      const subs = Object.entries(c.predictedSubTypes).slice(0, 2).map(([k, v]) => `${esc(k)} ${nf1.format(v)}`).join(' · ');
      return `<li>
        <button type="button" class="fc-item" data-city="${esc(c.city)}">
          <span class="fc-name"><strong>${esc(c.city)}</strong><span class="muted">${esc(c.region)}</span></span>
          <span class="fc-value"><strong>${nf1.format(c.predicted)}</strong><span class="muted small">events</span></span>
          <span class="fc-detail muted small">
            ${sparkline(c.last12)}
            <span>${c.basis.map((v, i) => `${esc(shortMonth(fc.basisMonths[i]))} ${v}`).join(' · ')}${fc.partialUntil ? ` · ${esc(target)} so far ${c.soFar}` : ''}</span>
          </span>
          ${subs ? `<span class="fc-subs small">${subs}</span>` : ''}
        </button></li>`;
    }).join('') : '<li class="muted">No county matches.</li>';
    const more = $('forecast-more');
    more.hidden = list.length <= LIMIT;
    more.textContent = `Showing ${LIMIT} of ${list.length}. Search to find others.`;
  }

  $('fc-search').addEventListener('input', renderForecastList);
  $('fc-sort').addEventListener('change', renderForecastList);
  $('fc-zero').addEventListener('change', renderForecastList);
  $('forecast-list').addEventListener('click', (e) => {
    const btn = e.target.closest('.fc-item');
    if (!btn) return;
    const city = btn.dataset.city;
    const ev = state.events.find((x) => x.city === city);
    F.region.value = ev ? ev.region : '';
    updateDependentOptions();
    F.city.value = city;
    applyFilters();
    fitToVisible();
    if (isPhone()) setSheet(false);
  });

  /* ---------------- Charts (Insights) ---------------- */
  function chartDefaults() {
    Chart.defaults.font.family = css('--font');
    Chart.defaults.font.size = 12;
    Chart.defaults.color = css('--text-2');
    Chart.defaults.borderColor = css('--grid');
    Chart.defaults.plugins.legend.display = false;
    Chart.defaults.plugins.tooltip.backgroundColor = css('--tooltip');
    Chart.defaults.plugins.tooltip.padding = 8;
    Chart.defaults.maintainAspectRatio = false;
    Chart.defaults.animation = false;
  }

  function barChart(id, boxId, entries) {
    const box = $(boxId);
    box.style.height = `${Math.max(1, entries.length) * 26 + 40}px`;
    const data = {
      labels: entries.map((e) => e[0]),
      datasets: [{ data: entries.map((e) => e[1]), backgroundColor: css('--series-1'), borderRadius: 4, borderSkipped: 'start', barThickness: 14 }],
    };
    if (state.charts[id]) { state.charts[id].data = data; state.charts[id].update(); return; }
    state.charts[id] = new Chart($(id), {
      type: 'bar', data,
      options: {
        indexAxis: 'y',
        scales: {
          x: { beginAtZero: true, grid: { color: css('--grid') }, ticks: { precision: 0, maxTicksLimit: 5, callback: (v) => nf.format(v) } },
          y: { grid: { display: false }, ticks: { autoSkip: false, color: css('--text-1') } },
        },
        plugins: { tooltip: { callbacks: { label: (c) => ` ${nf.format(c.parsed.x)} events` } } },
      },
    });
  }

  function renderCharts() {
    if (!state.chartsDirty || !window.Chart || !state.events.length) return;
    if (!state.charts.ready) { chartDefaults(); state.charts.ready = true; }
    state.chartsDirty = false;
    const v = state.visible;

    // Monthly trend over the selected period, zero months included
    const from = F.from.value, to = F.to.value;
    const months = [];
    for (let [y, m] = from.split('-').map(Number); `${y}-${String(m).padStart(2, '0')}` <= to; m === 12 ? (y++, m = 1) : m++) months.push(`${y}-${String(m).padStart(2, '0')}`);
    const per = new Map(months.map((m) => [m, 0]));
    v.forEach((e) => { const k = e.date.slice(0, 7); if (per.has(k)) per.set(k, per.get(k) + 1); });
    const last = state.meta.lastDate.slice(0, 7);
    const partial = months.includes(last) && state.meta.lastCompleteMonth < last;
    $('trend-note').textContent = partial ? `${monthName(last, 'short')} is incomplete (data to ${dayName(state.meta.lastDate, { day: 'numeric', month: 'short' })}).` : '';
    const trend = {
      labels: months.map((m) => monthName(m, 'short')),
      datasets: [{
        data: [...per.values()], borderColor: css('--series-1'), backgroundColor: css('--series-1'), borderWidth: 2,
        pointRadius: 0, pointHoverRadius: 4, tension: 0.2,
        segment: { borderDash: (c) => (partial && c.p1DataIndex === months.length - 1 ? [4, 3] : undefined) },
      }],
    };
    if (state.charts['c-trend']) { state.charts['c-trend'].data = trend; state.charts['c-trend'].update(); }
    else state.charts['c-trend'] = new Chart($('c-trend'), {
      type: 'line', data: trend,
      options: {
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: { grid: { display: false }, ticks: { maxRotation: 0, autoSkipPadding: 16 } },
          y: { beginAtZero: true, grid: { color: css('--grid') }, ticks: { precision: 0, maxTicksLimit: 5 } },
        },
        plugins: { tooltip: { callbacks: { label: (c) => ` ${nf.format(c.parsed.y)} events` } } },
      },
    });

    barChart('c-types', 'b-types', countBy(v, 'type'));
    const subs = countBy(v, 'sub');
    barChart('c-subs', 'b-subs', subs.length > 8 ? [...subs.slice(0, 7), ['Other', subs.slice(7).reduce((s, e) => s + e[1], 0)]] : subs);
    barChart('c-cities', 'b-cities', countBy(v, 'city').slice(0, 10));
  }

  document.addEventListener('cz-themechange', () => {
    for (const [k, c] of Object.entries(state.charts)) if (c && c.destroy) c.destroy();
    state.charts = {};
    state.chartsDirty = true;
    if (!$('view-insights').hidden) renderCharts();
  });

  init();
})();
