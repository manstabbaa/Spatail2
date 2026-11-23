/* Spatail Developer Guide. Mounts the Space Lab in the hero and wires every other section to it.
   Content that is plain data lives in content.js; the API text and planner instructions come from planner-data.js. */
(function () {
  'use strict';

  var DATA = window.SPATAIL_DATA || { programs: [] };
  var PLANNER = window.SPATAIL_PLANNER || { api: '', instructions: '', examples: [] };
  var EXAMPLES = window.SPATAIL_LAB_EXAMPLES || [];
  var FILM = window.SPATAIL_FILM || [];
  var APPS = window.SPATAIL_APPS || [];
  var STATUS = window.SPATAIL_STATUS || [];
  var MEDIA = window.SPATAIL_MEDIA || null;

  var $ = function (id) { return document.getElementById(id); };
  var esc = function (s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); };
  var store = {
    get: function (k) { try { return window.localStorage.getItem(k); } catch (e) { return null; } },
    set: function (k, v) { try { window.localStorage.setItem(k, v); } catch (e) { /* private window */ } }
  };

  // ---- the lab ----------------------------------------------------------------------------------------

  var ui = null, lab = null;
  var lastId = null;          // the experience most recently started
  var surfaceCount = 0;

  function bootLab() {
    var root = $('labroot');
    if (!window.THREE || !window.SpatailLab || !window.SpatailLabUI) {
      root.innerHTML = '<p class="loading">The 3D library did not load, so the lab cannot start. Check the connection and reload. Everything below still reads without it.</p>';
      return;
    }
    window.SpatailLabUI.loadLibrary('lab/data/spatail.js').then(function (library) {
      root.innerHTML = '';
      ui = window.SpatailLabUI.mount(root, { library: library, space: 'Workshop' });
      if (!ui) return;
      lab = ui.lab;
      lab.onEvent(onLabEvent);
      renderSpaces();
      renderStage();
    }, function (error) {
      root.innerHTML = '<p class="loading">' + esc(error.message) + ' Open this page from a web server, or from the planner service.</p>';
    });
  }

  function onLabEvent(ev) {
    if (ev.type === 'started') { lastId = ev.id; renderStage(); }
    else if (ev.type === 'stopped' || ev.type === 'failed') renderStage();
    else if (ev.type === 'space') { surfaceCount = 0; lastId = null; renderSpaces(); renderStage(); }
    else if (ev.type === 'surface') {
      surfaceCount = ev.count;
      spaceState();
      var live = $('live-surfaces');
      if (live) live.textContent = surfaceLine();
    }
  }

  function toLab() { $('lab').scrollIntoView({ behavior: 'smooth', block: 'start' }); }

  function setSpace(name, seed) {
    if (!ui) return;
    if (ui.setSpace) ui.setSpace(name, seed); else { lab.setSpace(name, seed); ui.refresh(); }
    surfaceCount = 0; lastId = null;
    renderSpaces(); renderStage();
  }

  function run(spec) {
    if (!ui) return;
    if (ui.run) ui.run(spec);
    else { lab.run({ id: spec.id, title: spec.title, prompt: spec.prompt, code: spec.code, view: lab.capture().view }); ui.refresh(); }
    toLab();
  }

  function filmProgram(id) { return DATA.programs.filter(function (p) { return p.id === id; })[0]; }
  function example(id) { return EXAMPLES.filter(function (p) { return p.id === id; })[0]; }

  function runFilm(id) {
    var program = filmProgram(id);
    if (!program || !lab) return;
    if (lab.space().name !== 'Workshop') setSpace('Workshop');
    run(program);
  }

  function lastExperience() {
    if (!lab) return null;
    var all = lab.experiences();
    var match = all.filter(function (e) { return e.id === lastId; })[0];
    return match || all[all.length - 1] || null;
  }

  // ---- the idea: request tables ---------------------------------------------------------------------------

  var SENSES = {
    'measure': 'A ray along your gaze. The first point is what you were looking at when you asked; the second follows your eyes and locks when you hold still.',
    'surfaces': 'The surface list, every frame. Each floor, wall or table top is drawn the moment the device finds it.',
    'keep-clear': 'The image position you were looking at, cast into the space, then the floor under it. The zone reacts when you step inside.',
    'route': 'Rays at shin and waist height along the way. The path bends around what they hit and is re-planned as you walk.',
    'height': 'The point you were looking at and the floor directly below it.'
  };

  function renderRequests() {
    $('filmrows').innerHTML = FILM.map(function (f) {
      var program = filmProgram(f.id);
      var lines = program ? program.code.split('\n').length : '';
      return '<tr><td class="q">&ldquo;' + esc(f.prompt) + '&rdquo;</td><td>' + esc(f.does) + '</td><td class="num">' + lines + '</td>' +
        '<td><button type="button" class="btn" data-film="' + esc(f.id) + '">Run in the lab</button></td></tr>';
    }).join('');
    $('senserows').innerHTML = EXAMPLES.map(function (e) {
      return '<tr><td class="q">&ldquo;' + esc(e.prompt) + '&rdquo;</td><td>' + esc(SENSES[e.id] || '') + '</td>' +
        '<td><button type="button" class="btn" data-example="' + esc(e.id) + '">Run where you are</button></td></tr>';
    }).join('');
  }

  // ---- how it works ---------------------------------------------------------------------------------------

  var STAGES = [
    {
      t: 'Sense', d: 'What the device knows right now',
      body: 'When you speak, the device captures a camera frame with its pose and lens data, samples depth along a coarse grid of rays through that frame, and lists the flat surfaces it has noticed so far. That is all the model gets about the place. There is no stored scan to fall back on.',
      points: ['<code>ISpaceSensor</code> is the whole contract: ray cast, floor height, surfaces so far, capture a frame.', 'The headset grows surfaces from depth-ray hits as you look around. The phone reads ARKit planes and, with LiDAR, the mesh.', 'A frame maps any image position to a ray in the room, so "that thing at (0.62, 0.41)" becomes a point in space.'],
      files: ['packages/com.spatail.core/Runtime/Core/Script/SpaceSensor.cs', 'packages/com.spatail.core/Runtime/Core/Room/SurfaceTracker.cs', 'apps/spatail-quest/.../QuestSensor.cs', 'apps/spatail-devtool-ios/.../ArSensor.cs']
    },
    {
      t: 'Write', d: 'Claude writes the program',
      body: 'The device posts the request to the planner service. Claude sees the frame as an image, reads the depth grid and surfaces, and writes a JavaScript program against the Spatail API. For things it can see, it anchors with <code>space.view.point(u, v)</code>, which casts through that pixel into the sensed geometry.',
      points: ['The answer is constrained to a schema: a reply to speak, a title, an id, the code, and what to remove.', 'Reusing an id replaces a running program with a new version of itself. That is how follow-ups edit.', 'With no planner reachable, a small built-in set of recorded programs answers the film requests.'],
      files: ['services/planner/src/prompt.ts', 'services/planner/src/api.ts', 'services/planner/src/planner.ts', 'packages/com.spatail.core/Runtime/Core/Building/Director.cs']
    },
    {
      t: 'Run', d: 'A sandbox on the device',
      body: 'The device runs the program in its own JavaScript engine with nothing in scope but the Spatail API: no network, no files, no engine objects. Budgets stop runaway code. If the program fails to start, its error goes back to the planner once for repair.',
      points: ['The engine is Jint, running inside Unity on both devices. The runtime library is one file, shared byte for byte with this lab.', 'A program only describes nodes. <code>SceneHost</code> draws them, so a program cannot reach past the API.'],
      files: ['packages/com.spatail.core/Runtime/Core/Script/ExperienceRuntime.cs', 'packages/com.spatail.core/Runtime/Resources/Spatail/Script/spatail.js.txt', 'packages/com.spatail.core/Runtime/Unity/Scene/SceneHost.cs']
    },
    {
      t: 'Live', d: 'It keeps sensing and listening',
      body: 'A program is not a picture. It runs every frame: it animates, reacts to taps, registers short spoken commands that the device handles at once, and can look at the place again with <code>space.raycast</code> or <code>await space.find(...)</code>.',
      points: ['<code>voice.on([&quot;next&quot;], ...)</code> is answered on the device with no planner call.', '<code>state</code> survives when a follow-up replaces the program, so ticks and step numbers are kept.', 'A program that keeps throwing is stopped on its own, with the error in its log. Other programs carry on.'],
      files: ['packages/com.spatail.core/Runtime/Unity/App/SpatailApp.cs', 'packages/com.spatail.core/Runtime/Unity/Interaction/Interaction.cs']
    }
  ];
  var BUDGETS = [['Statements at start', '3,000,000'], ['Statements per frame or event', '400,000'], ['Call depth', '200'], ['Nodes', '4,000'], ['Errors before it is stopped', '30']];
  var stageIndex = 0;

  function renderPipe() {
    $('pipe').innerHTML = STAGES.map(function (s, i) {
      return '<button type="button" class="stagebtn" role="tab" aria-selected="' + (i === stageIndex) + '" data-stage="' + i + '">' +
        '<span class="n">' + (i + 1) + '</span><span class="t">' + s.t + '</span><span class="d">' + s.d + '</span></button>';
    }).join('');
  }

  function surfaceLine() {
    if (!lab) return '';
    var list = lab.surfaces(), kinds = {};
    list.forEach(function (s) { kinds[s.kind] = (kinds[s.kind] || 0) + 1; });
    var parts = Object.keys(kinds).map(function (k) { return kinds[k] + ' ' + k; });
    return list.length ? list.length + ' found so far: ' + parts.join(', ') : 'none found yet. Look around in the lab.';
  }

  // JSON for reading: long strings and long number lists are summarised, short arrays stay on one line.
  function abridge(value, depth) {
    if (typeof value === 'string') return value.length > 160 ? '<' + Math.round(value.length / 1024) + ' kB of text>' : value;
    if (Array.isArray(value)) {
      var primitive = value.every(function (v) { return typeof v !== 'object' || v === null; });
      if (primitive && value.length > 12) return value.slice(0, 8).concat(['... ' + (value.length - 8) + ' more']);
      if (!primitive && value.length > 2 && depth > 0) return value.slice(0, 2).map(function (v) { return abridge(v, depth + 1); }).concat(['... ' + (value.length - 2) + ' more']);
      return value.map(function (v) { return abridge(v, depth + 1); });
    }
    if (value && typeof value === 'object') {
      var out = {};
      Object.keys(value).forEach(function (k) { out[k] = abridge(value[k], depth + 1); });
      return out;
    }
    return value;
  }
  function pretty(value, indent) {
    indent = indent || '';
    if (Array.isArray(value)) {
      if (value.every(function (v) { return typeof v !== 'object' || v === null; })) return '[' + value.map(function (v) { return JSON.stringify(v); }).join(', ') + ']';
      return '[\n' + value.map(function (v) { return indent + '  ' + pretty(v, indent + '  '); }).join(',\n') + '\n' + indent + ']';
    }
    if (value && typeof value === 'object') {
      var keys = Object.keys(value);
      if (!keys.length) return '{}';
      return '{\n' + keys.map(function (k) { return indent + '  ' + JSON.stringify(k) + ': ' + pretty(value[k], indent + '  '); }).join(',\n') + '\n' + indent + '}';
    }
    return JSON.stringify(value);
  }

  function liveSense() {
    if (!lab) return '<p class="muted small">The lab is not running, so there is nothing live to show.</p>';
    var shot = lab.lastCapture() || lab.capture();
    var d = shot.depth, cells = '';
    var far = 0;
    d.metres.forEach(function (m) { if (m > far) far = m; });
    for (var i = 0; i < d.metres.length; i++) {
      var m = d.metres[i];
      var alpha = m < 0 ? 0.06 : Math.max(0.15, 1 - m / Math.max(far, 1) * 0.85);
      cells += '<span style="opacity:' + alpha.toFixed(2) + '" title="' + (m < 0 ? 'nothing sensed' : m.toFixed(2) + ' m') + '"></span>';
    }
    return '<span class="cap">From the lab, just now</span>' +
      '<div class="frame"><img src="' + shot.imageDataUrl + '" alt="The camera frame the lab would send with a request">' +
      '<div class="stack"><div class="depth" style="grid-template-columns:repeat(' + d.columns + ',1fr)" aria-label="Depth grid">' + cells + '</div>' +
      '<p class="muted small">Depth grid, ' + d.columns + ' &times; ' + d.rows + ' rays. Stronger is nearer; faint means nothing was sensed.</p></div></div>' +
      '<dl class="kv"><dt>Surfaces</dt><dd id="live-surfaces">' + esc(surfaceLine()) + '</dd>' +
      '<dt>Named objects</dt><dd>' + (lab.space().props > 0 ? lab.space().props + ' (the stand-in pump and its parts)' : 'none') + '</dd></dl>' +
      '<div class="btnrow"><button type="button" class="btn" data-act="recapture">Capture what the lab sees now</button></div>';
  }

  function liveWrite() {
    if (!lab) return '';
    var exp = lastExperience();
    var prompt = exp ? exp.prompt : 'How high is that?';
    var body = abridge(lab.buildRequest(prompt), 0);
    return '<span class="cap">The request the lab would post for &ldquo;' + esc(prompt) + '&rdquo;</span>' +
      '<pre class="tall"><code>POST /v1/build\n' + esc(pretty(body)) + '</code></pre>';
  }

  function liveRun() {
    var rows = BUDGETS.map(function (b) { return '<dt>' + b[0] + '</dt><dd>' + b[1] + '</dd>'; }).join('');
    var exp = lastExperience();
    var code = exp
      ? '<span class="cap">Running now: ' + esc(exp.title) + ' (' + exp.code.split('\n').length + ' lines)</span><pre class="tall"><code>' + tint(exp.code) + '</code></pre>'
      : '<p class="muted small">Run a request in the lab and its program appears here.</p>';
    return '<span class="cap">Budgets on the device</span><dl class="kv">' + rows + '</dl>' + code;
  }

  function liveLive() {
    if (!lab) return '';
    var all = lab.experiences();
    if (!all.length) return '<p class="muted small">Nothing is running in the lab. Run a request above, then come back.</p>';
    return '<span class="cap">Running in the lab</span><div class="tablewrap"><table style="min-width:380px"><thead><tr><th>Experience</th><th class="num">Nodes</th><th>Latest log line</th></tr></thead><tbody>' +
      all.map(function (e) {
        var log = e.log && e.log.length ? e.log[e.log.length - 1] : '';
        if (log && typeof log === 'object') log = log.message || '';
        return '<tr><td class="q">' + esc(e.title) + (e.error ? ' <span class="tag warn">stopped</span>' : '') + '</td><td class="num">' + (e.nodeCount || 0) + '</td><td>' + esc(e.error || log || '') + '</td></tr>';
      }).join('') + '</tbody></table></div>' +
      '<p class="muted small">Type a short command such as <kbd>next</kbd> into the lab\'s prompt while the walkthrough runs: the program handles it itself.</p>';
  }

  function renderStage() {
    var s = STAGES[stageIndex];
    var live = [liveSense, liveWrite, liveRun, liveLive][stageIndex]();
    $('stagedetail').innerHTML =
      '<div class="stack"><h3>' + s.t + '</h3><p>' + s.body + '</p><ul class="plain">' + s.points.map(function (p) { return '<li>' + p + '</li>'; }).join('') + '</ul>' +
      '<div class="files">' + s.files.map(function (f) { return '<code>' + esc(f) + '</code>'; }).join('') + '</div></div>' +
      '<div class="live">' + live + '</div>';
  }

  // ---- the API ----------------------------------------------------------------------------------------------

  function tint(code) {
    return esc(code).split('\n').map(function (line) {
      var at = line.search(/(^|\s)\/\//);
      if (at < 0) return line;
      if (line.charAt(at) !== '/') at += 1;
      return line.slice(0, at) + '<span class="c">' + line.slice(at) + '</span>';
    }).join('\n');
  }

  var apiParts = (function () {
    var parts = [{ name: 'conventions', lines: [] }];
    PLANNER.api.split('\n').forEach(function (line) {
      var m = line.match(/^\/\/ ---- ([^:(-]+)/);
      if (m) parts.push({ name: m[1].trim(), lines: [] });
      else parts[parts.length - 1].lines.push(line);
    });
    return parts.map(function (p) { return { name: p.name, text: p.lines.join('\n').trim() }; });
  })();
  var apiIndex = Math.min(1, apiParts.length - 1);

  function renderApi() {
    $('apipicker').innerHTML = apiParts.map(function (p, i) {
      return '<button type="button" aria-pressed="' + (i === apiIndex) + '" data-api="' + i + '">' + esc(p.name) + '</button>';
    }).join('');
    $('apitext').innerHTML = tint(apiParts[apiIndex].text);
  }

  var programs = DATA.programs.map(function (p) { return { id: p.id, title: p.title, prompt: p.prompt, code: p.code, film: true }; })
    .concat(EXAMPLES.map(function (p) { return { id: p.id, title: p.title, prompt: p.prompt, code: p.code, film: false }; }));
  var progIndex = 0;

  function renderPrograms() {
    $('progpicker').innerHTML = programs.map(function (p, i) {
      return '<button type="button" aria-pressed="' + (i === progIndex) + '" data-prog="' + i + '">' + esc(p.title) + '</button>';
    }).join('');
    var p = programs[progIndex];
    $('progtext').innerHTML = p ? tint(p.code) : '';
    $('progrun').textContent = p && p.film ? 'Run it in the Workshop' : 'Run it where you are in the lab';
  }

  function renderInstructions() {
    var text = PLANNER.instructions || '';
    var at = PLANNER.api ? text.indexOf(PLANNER.api.slice(0, 120)) : -1;
    if (at >= 0) {
      var rest = text.slice(at + PLANNER.api.length).replace(/^\s*```\s*/, '\n');
      var examplesAt = rest.indexOf('\n## Examples');
      var tail = examplesAt >= 0 ? rest.slice(0, examplesAt) : rest;
      text = text.slice(0, at).replace(/```\w*\s*$/, '') + '[ the API reference shown above ]\n' + tail.trimEnd() +
        '\n\n## Examples\n\n[ ' + (PLANNER.examples ? PLANNER.examples.length : 'several') + ' worked example programs follow ]';
    }
    $('instructions').textContent = text.trim();
  }

  // ---- unprepared places ------------------------------------------------------------------------------------

  function spaceState() {
    var el = $('spacestate');
    if (!el || !lab) return;
    var s = lab.space();
    el.textContent = s.name + (s.seed !== undefined && s.seed !== null && s.name !== 'Workshop' ? ', seed ' + s.seed : '') + '. ' +
      (surfaceCount ? surfaceCount + ' surface' + (surfaceCount === 1 ? '' : 's') + ' found so far.' : 'No surfaces found yet. Look around in the lab.');
  }

  function renderSpaces() {
    var box = $('spacebuttons');
    if (!lab) { box.innerHTML = ''; return; }
    box.innerHTML = lab.spaces().filter(function (s) { return s.seeded; }).map(function (s) {
      return '<button type="button" class="btn" data-space="' + esc(s.name) + '" title="' + esc(s.about) + '">' + esc(s.name) + ', new layout</button>';
    }).join('');
    spaceState();
  }

  // ---- commands, apps, recording, map, status ---------------------------------------------------------------

  function cmdBlock(cmd) {
    return '<div class="cmd"><pre><code>' + esc(cmd) + '</code></pre><button type="button" class="copy" data-copy="' + esc(cmd) + '">Copy</button></div>';
  }

  function checklist(prefix, steps) {
    return steps.map(function (s, i) {
      var key = 'spatail.guide.' + prefix + '.' + i, id = 'step-' + prefix + '-' + i;
      var text = typeof s === 'string' ? s : s.text;
      return '<li><input type="checkbox" id="' + id + '" data-key="' + key + '"' + (store.get(key) === '1' ? ' checked' : '') + '>' +
        '<label for="' + id + '">' + text + (s.cmd ? cmdBlock(s.cmd) : '') + '</label></li>';
    }).join('');
  }

  function renderApps() {
    $('appcards').innerHTML = APPS.map(function (app) {
      return '<div class="card"><span class="eyebrow accent">' + esc(app.platform) + '</span><h3>' + esc(app.name) + '</h3><p>' + app.summary + '</p>' +
        '<ul class="plain">' + app.features.map(function (f) { return '<li>' + f + '</li>'; }).join('') + '</ul>' +
        '<div class="tablewrap"><table style="min-width:360px"><thead><tr><th>Input</th><th>Does</th></tr></thead><tbody>' +
        app.controls.map(function (c) { return '<tr><td class="q">' + esc(c[0]) + '</td><td>' + esc(c[1]) + '</td></tr>'; }).join('') + '</tbody></table></div>' +
        '<h3>From clone to device</h3><ol class="steps">' + checklist(app.id, app.steps) + '</ol>' +
        '<p class="note warn">' + app.note + '</p></div>';
    }).join('');
  }

  function renderPlanner() {
    $('plannercmds').innerHTML = ['cd services/planner', 'npm install', 'npm run build', 'ANTHROPIC_API_KEY=... npm start'].map(cmdBlock).join('');
    $('replaycmd').innerHTML = cmdBlock('ANTHROPIC_API_KEY=... npm run replay');
  }

  var SIM_STEPS = [
    'Install the Meta XR Simulator (Windows, or macOS on Apple Silicon). Version 85 or newer is needed for the camera path.',
    'In <code>apps/spatail-quest</code>, run <b>Spatail &gt; Use XR Runtime In Play Mode (Link or Meta XR Simulator)</b>.',
    'Switch the simulator on with its toolbar button in Unity, choose one of its synthetic rooms, and press Play.',
    'Check the Console line starting <code>[Spatail] This device:</code>, then ask for something.',
    'Run <b>Spatail &gt; Record Demo (Play Mode)</b>, and screen-record the simulator window alongside it.'
  ];

  function renderRecording() {
    $('simsteps').innerHTML = checklist('sim', SIM_STEPS);
    var slot = $('videoslot');
    if (MEDIA && MEDIA.video) {
      slot.innerHTML = '<video controls preload="metadata" playsinline' + (MEDIA.poster ? ' poster="' + esc(MEDIA.poster) + '"' : '') + ' src="' + esc(MEDIA.video) + '"></video>' +
        '<p class="muted small">' + (MEDIA.caption || '') + '</p>';
    } else {
      slot.innerHTML = '<div class="card"><h3>No clip is bundled with this copy</h3><p>Record one from the lab above with any screen recorder, or use the Unity command on the right.</p></div>';
    }
  }

  function renderTree() {
    var rows = [
      ['packages/<b>com.spatail.core</b>/', 'the shared Unity package'],
      ['  Runtime/Core/Script/', 'the script runtime, the sensor contract, text metrics (no engine references)'],
      ['  Runtime/Core/Room/', 'live surface tracking and ray casting'],
      ['  Runtime/Core/Building/', 'request, planner client, recorded programs, repair'],
      ['  Runtime/Core/Session/', 'the link between a headset and the dev tool'],
      ['  Runtime/Unity/', 'scene host, text, interaction, prompt bar, desktop simulator'],
      ['  Runtime/Resources/Spatail/Script/<b>spatail.js.txt</b>', 'the API library every program runs against'],
      ['  Runtime/Resources/Spatail/Script/Examples/', 'the six film requests as programs'],
      ['  Runtime/Plugins/', 'Jint, the JavaScript engine'],
      ['apps/<b>spatail-quest</b>/', 'Meta Quest 3 / 3S app: live depth, passthrough camera, demo recorder'],
      ['apps/<b>spatail-devtool-ios</b>/', 'iPhone and iPad dev tool: ARKit sensing, inspector tabs, code editor'],
      ['services/<b>planner</b>/', 'Claude sees the frame and writes the program; episode replay'],
      ['  src/prompt.ts, src/api.ts', 'what the model is told, and the API reference shown in this guide'],
      ['  knowledge/', 'stand-in for what your organisation knows about its equipment'],
      ['docs/<b>lab</b>/', 'the browser lab: generated spaces, the same library, a virtual camera'],
      ['docs/', 'this guide'],
      ['tests/Spatail.Core.Tests/', 'core test suite, also exports the lab\'s data'],
      ['tools/UnityCompileCheck/', 'type-checks the Unity runtime with no Unity install']
    ];
    var width = 0;
    rows.forEach(function (r) { var n = r[0].replace(/<[^>]+>/g, '').length; if (n > width) width = n; });
    $('tree').innerHTML = rows.map(function (r) {
      var plain = r[0].replace(/<[^>]+>/g, '');
      return r[0] + new Array(width - plain.length + 4).join(' ') + '<span class="c">' + esc(r[1]) + '</span>';
    }).join('\n');
  }

  function renderStatus() {
    $('statusrows').innerHTML = STATUS.map(function (r) {
      var cls = r[1] === 'Tested' ? 'ok' : (r[1] === 'Compile-checked' || r[1] === 'Checked against SDK source' ? '' : 'warn');
      return '<tr><td class="q">' + esc(r[0]) + '</td><td><span class="tag ' + cls + '">' + esc(r[1]) + '</span></td><td>' + r[2] + '</td></tr>';
    }).join('');
  }

  // ---- events ---------------------------------------------------------------------------------------------------

  function copyText(text, button) {
    var done = function () { var old = button.textContent; button.textContent = 'Copied'; setTimeout(function () { button.textContent = old; }, 1400); };
    var fallback = function () {
      var area = document.createElement('textarea');
      area.value = text; area.setAttribute('readonly', ''); area.style.position = 'fixed'; area.style.opacity = '0';
      document.body.appendChild(area); area.select();
      try { document.execCommand('copy'); done(); } catch (e) { /* nothing else to try */ }
      document.body.removeChild(area);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, fallback); else fallback();
  }

  document.addEventListener('click', function (ev) {
    var t = ev.target.closest('button');
    if (!t || t.closest('#labroot')) return;
    var d = t.dataset;
    if (d.film) runFilm(d.film);
    else if (d.example) { var e = example(d.example); if (e) run(e); }
    else if (d.stage) { stageIndex = parseInt(d.stage, 10); renderPipe(); renderStage(); }
    else if (d.act === 'recapture') { if (lab) { lab.capture(); renderStage(); } }
    else if (d.api) { apiIndex = parseInt(d.api, 10); renderApi(); }
    else if (d.prog) { progIndex = parseInt(d.prog, 10); renderPrograms(); }
    else if (t.id === 'progrun') { var p = programs[progIndex]; if (p) { if (p.film) runFilm(p.id); else run(p); } }
    else if (t.id === 'progcopy') { if (programs[progIndex]) copyText(programs[progIndex].code, t); }
    else if (d.space) { setSpace(d.space); toLab(); }
    else if (d.copy) copyText(d.copy, t);
  });

  document.addEventListener('change', function (ev) {
    var t = ev.target;
    if (t.matches && t.matches('.steps input[type="checkbox"]')) store.set(t.dataset.key, t.checked ? '1' : '0');
  });

  var links = Array.prototype.slice.call(document.querySelectorAll('.nav a'));
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        links.forEach(function (a) { a.classList.toggle('on', a.getAttribute('href') === '#' + entry.target.id); });
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    document.querySelectorAll('main section[id]').forEach(function (s) { io.observe(s); });
  }

  renderRequests();
  renderPipe();
  renderStage();
  renderApi();
  renderPrograms();
  renderInstructions();
  renderSpaces();
  renderApps();
  renderPlanner();
  renderRecording();
  renderTree();
  renderStatus();
  bootLab();
})();
