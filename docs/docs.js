/* Spatail Developer Guide: page logic. Plain script, no build step. */
(function () {
  'use strict';

  var DATA = window.SPATAIL_DATA || { room: { entities: [] }, plans: [], library: [], fallbackReply: '' };
  var $ = function (id) { return document.getElementById(id); };
  var esc = function (s) { return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); };
  var store = {
    get: function (k) { try { return window.localStorage.getItem(k); } catch (e) { return null; } },
    set: function (k, v) { try { window.localStorage.setItem(k, v); } catch (e) { /* private mode */ } }
  };

  // ---- theme -------------------------------------------------------------------------------

  function currentTheme() {
    var explicit = document.documentElement.getAttribute('data-theme');
    if (explicit === 'dark' || explicit === 'light') return explicit;
    return window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  // ---- simulator ---------------------------------------------------------------------------

  var sim = null;
  var last = { prompt: '(scan)', plan: null, placements: {}, routed: null };
  var usedPrompts = {};

  function bootSim() {
    var el = $('sim');
    if (!window.SpatailSim || !window.THREE) {
      el.innerHTML = '<p class="muted" style="padding:24px">The 3D simulator could not load. The rest of this guide works without it.</p>';
      return;
    }
    try {
      sim = window.SpatailSim.create(el, { room: DATA.room, plans: DATA.plans, theme: currentTheme() });
    } catch (e) {
      el.innerHTML = '<p class="muted" style="padding:24px">This browser could not start WebGL, so the simulator is unavailable. The rest of this guide works without it.</p>';
      sim = null;
      return;
    }
    sim.onEvent(function (ev) {
      if (ev.type === 'step') { say('Step ' + ev.step + ' of ' + ev.stepCount + '.', 'Step ' + ev.step); renderPane(); renderChips(); }
      if (ev.type === 'checklist') renderPane();
    });
    new MutationObserver(function () { sim.setTheme(currentTheme()); }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    if (window.matchMedia) {
      var mq = window.matchMedia('(prefers-color-scheme: dark)');
      var onChange = function () { sim.setTheme(currentTheme()); };
      if (mq.addEventListener) mq.addEventListener('change', onChange); else if (mq.addListener) mq.addListener(onChange);
    }
    showScan();
  }

  function showScan() {
    var entry = DATA.plans[0];
    if (!sim || !entry) return;
    sim.applyPlan(entry.plan, entry.placements);
    last = { prompt: 'Room scan', plan: entry.plan, placements: entry.placements, routed: null };
  }

  function builds() { return sim ? sim.builds() : []; }
  function findBuild(kind) { return builds().filter(function (b) { return b.kind === kind || b.id === kind; })[0]; }
  function steppedBuild() { var b = builds().filter(function (x) { return x.stepCount > 0; }); return b[b.length - 1]; }

  // ---- routing a typed request to an exported plan -------------------------------------------
  // Mirrors the order of checks in OfflinePlanner.cs. Planning itself happened in C#; this only picks which
  // exported plan to replay.

  function norm(s) { return ' ' + String(s).toLowerCase().replace(/[^a-z0-9]+/g, ' ').trim() + ' '; }
  function has(p) { for (var i = 1; i < arguments.length; i++) if (p.indexOf(' ' + arguments[i]) >= 0) return true; return false; }
  function lib(prompt) { return DATA.library.filter(function (e) { return e.prompt === prompt; })[0]; }

  var KIND_WORDS = {
    walkthrough: ['walkthrough', 'guide', 'steps', 'procedure', 'instructions'],
    flow: ['flow', 'arrows', 'water'],
    faults: ['fault', 'failures', 'alarms', 'warnings'],
    dashboard: ['dashboard', 'chart', 'graph', 'output', 'bars'],
    checklist: ['checklist', 'parts', 'list'],
    session: ['session', 'avatar', 'sara', 'marco'],
    labels: ['labels', 'names', 'tags'],
    note: ['note', 'panel', 'explanation']
  };
  var PARTS = [
    ['gate valve', 'What is the gate valve?'], ['valve', 'What is the gate valve?'], ['handwheel', 'What is the gate valve?'],
    ['motor', 'What is the motor?'], ['coupling', 'What is the coupling?'], ['shaft', 'What is the coupling?'],
    ['control box', 'What is the control box?'], ['control panel', 'What is the control box?'], ['panel', 'What is the control box?'],
    ['outlet', 'What is the outlet?'], ['discharge', 'What is the outlet?'], ['inlet', 'What is the inlet pipe?'], ['pipe', 'What is the inlet pipe?'],
    ['workbench', 'What is the workbench?'], ['bench', 'What is the workbench?'], ['pump', 'What is the pump?']
  ];

  function route(prompt) {
    var p = norm(prompt);
    var words = p.trim().split(' ').length;
    var stepped = steppedBuild();

    if (builds().length && (p === ' clear ' || p === ' reset ' || has(p, 'clear everything', 'clear all', 'remove everything', 'start over')))
      return { local: { reply: 'Cleared.', chip: '', ops: [{ op: 'clear' }] } };

    if (stepped && (words <= 4 || has(p, 'step'))) {
      var target = -1, m = p.match(/ step (\d+) /);
      if (m) target = parseInt(m[1], 10);
      else if (has(p, 'next', 'continue', 'done', 'go on')) target = stepped.currentStep + 1;
      else if (has(p, 'back', 'previous')) target = stepped.currentStep - 1;
      if (target > stepped.stepCount) return { local: { reply: 'That was the last step.', chip: '', ops: [] } };
      if (target === 0) return { local: { reply: 'You are on the first step.', chip: '', ops: [] } };
      if (target > 0) return { local: { reply: 'Step ' + target + ' of ' + stepped.stepCount + '.', chip: 'Step ' + target, ops: [{ op: 'step', buildId: stepped.id, step: target }] } };
    }

    if (has(p, 'remove', 'hide', 'delete', 'dismiss', 'get rid of')) {
      var all = builds();
      for (var i = all.length - 1; i >= 0; i--) {
        var kw = KIND_WORDS[all[i].kind] || [];
        for (var k = 0; k < kw.length; k++)
          if (has(p, kw[k])) return { local: { reply: 'Removed the ' + all[i].title.toLowerCase() + '.', chip: '', ops: [{ op: 'remove', buildId: all[i].id }] } };
      }
    }

    if (has(p, 'bring', 'invite', 'call', 'join', 'share') && has(p, 'marco')) return { entry: lib('Bring Marco in') };
    if (has(p, 'bring', 'invite', 'call', 'join', 'share', 'session') && has(p, 'sara', 'session', 'someone', 'expert')) return { entry: lib('Bring Sara in') };
    if (has(p, 'flow', 'water', 'fluid', 'liquid', 'direction')) return { entry: lib('Show me how the water flows') };
    if (has(p, 'fail', 'fault', 'broke', 'alarm', 'issue', 'problem', 'wrong', 'trip')) return { entry: lib('Has it failed before?') };
    if (has(p, 'part', 'spare', 'checklist', 'tool', 'inventory', 'kit'))
      return { entry: has(p, 'wall') ? lib('Put the parts checklist on the wall') : lib('What parts do I need?') };
    if (has(p, 'energy', 'power', 'consumption', 'electricity')) return { entry: lib('Show energy this month on the bench') };
    if (has(p, 'output', 'production', 'throughput', 'chart', 'dashboard', 'graph', 'metric', 'kpi', 'trend', 'number'))
      return { entry: has(p, 'wall') ? lib("Put this month's output on the wall") : lib("Put this month's output by the bench") };
    if (has(p, 'start up', 'startup', 'start-up', 'restart', 'commission')) return { entry: lib('Walk me through the start-up check') };
    if (has(p, 'walkthrough', 'walk me', 'guide', 'procedure', 'step', 'how do i', 'how to', 'service', 'isolate', 'instruction', 'train', 'teach'))
      return { entry: lib('Build me a walkthrough for this pump') };
    if (has(p, 'label', 'identify', 'scan', 'name everything')) return { entry: lib('Label everything') };
    if (has(p, 'what is', 'what s', 'whats', 'what does', 'tell me about', 'explain', 'describe', 'about')) {
      for (var j = 0; j < PARTS.length; j++) if (has(p, PARTS[j][0])) return { entry: lib(PARTS[j][1]) };
    }
    return { local: { reply: DATA.fallbackReply, chip: '', ops: [] }, unknown: true };
  }

  function submit(prompt) {
    prompt = String(prompt || '').trim();
    if (!prompt) return;
    usedPrompts[prompt] = true;
    $('prompt').value = '';
    if (!sim) { say('The simulator is not available in this browser.', ''); return; }

    var r = route(prompt);
    var plan, placements = {};
    if (r.entry) {
      plan = JSON.parse(JSON.stringify(r.entry.plan));
      placements = r.entry.placements;
      // Same house rule as the app: the scan labels make way for the first real content.
      var addsContent = plan.ops.some(function (o) { return o.op === 'add' && o.build && o.build.kind !== 'labels'; });
      if (addsContent && findBuild('labels')) plan.ops.unshift({ op: 'remove', buildId: 'labels' });
    } else {
      plan = r.local;
    }
    sim.applyPlan(plan, placements);
    last = { prompt: prompt, plan: plan, placements: placements, routed: r.entry ? r.entry.prompt : null };

    var note = '';
    if (r.entry && norm(r.entry.prompt) !== norm(prompt)) note = ' <span class="muted">Replayed the exported plan for "' + esc(r.entry.prompt) + '".</span>';
    if (r.unknown) note = ' <span class="muted">That is the on-device planner\'s real answer. With the AI planner connected, the app sends requests like this to Claude.</span>';
    say(esc(plan.reply) + note, plan.chip, true);
    renderPane();
    renderChips();
    renderStage();
  }

  function say(html, chip, isHtml) {
    $('reply').innerHTML = isHtml ? html : esc(html);
    var built = $('built');
    if (chip) { built.textContent = (/^Step /.test(chip) ? '' : 'Built ') + chip; built.hidden = false; }
    else built.hidden = true;
  }

  // ---- chips -------------------------------------------------------------------------------

  var DEMO = [
    'Build me a walkthrough for this pump', 'Show me how the water flows', 'Has it failed before?',
    "Put this month's output by the bench", 'What parts do I need?', 'Bring Sara in'
  ];
  var MORE = ['Walk me through the start-up check', 'What is the gate valve?', 'Show energy this month on the bench', 'Label everything'];

  function renderChips() {
    var list = [];
    var stepped = steppedBuild();
    if (stepped && stepped.currentStep < stepped.stepCount) list.push('Next');
    if (stepped && stepped.currentStep > 1) list.push('Back');
    DEMO.concat(MORE).forEach(function (p) { list.push(p); });
    if (builds().length) list.push('Clear everything');
    $('chips').innerHTML = list.map(function (p) {
      return '<button type="button" class="chip' + (usedPrompts[p] ? ' used' : '') + '" data-prompt="' + esc(p) + '">' + esc(p) + '</button>';
    }).join('');
  }

  // ---- inspector ---------------------------------------------------------------------------

  var tab = 'plan';

  function highlightJson(value) {
    var text = JSON.stringify(value, null, 2);
    return esc(text).replace(/(&quot;(?:[^&]|&(?!quot;))*?&quot;)(\s*:)?|\b(-?\d+(?:\.\d+)?)\b|\b(true|false|null)\b/g, function (m, str, colon, num, lit) {
      if (str) return colon ? '<span class="k">' + str + '</span>' + colon : '<span class="s">' + str + '</span>';
      if (num !== undefined) return '<span class="n">' + num + '</span>';
      return '<span class="n">' + lit + '</span>';
    });
  }

  function fmt(v) { return v.map(function (n) { return (Math.round(n * 100) / 100).toFixed(2); }).join(', '); }

  function renderPane() {
    var pane = $('pane');
    var html = '';
    if (tab === 'plan') {
      var plan = last.plan;
      html += '<h3>' + esc(last.prompt) + '</h3>';
      if (!plan) html += '<p class="muted">Run a request to see its plan.</p>';
      else {
        html += '<dl class="kv"><dt>reply</dt><dd>' + esc(plan.reply || '') + '</dd><dt>chip</dt><dd>' + esc(plan.chip || '(none)') + '</dd><dt>planner</dt><dd>' + esc(plan.planner || 'docs router') + '</dd><dt>ops</dt><dd>' + plan.ops.map(function (o) { return o.op + (o.build ? ' ' + o.build.id : (o.buildId ? ' ' + o.buildId : '')); }).join(', ') + '</dd></dl>';
        var bs = builds();
        if (bs.length) {
          html += '<p class="eyebrow" style="margin-top:12px">In the room now</p><div class="builds">' + bs.map(function (b) {
            var step = b.stepCount ? ' ' + b.currentStep + '/' + b.stepCount : '';
            return '<span class="bchip">' + esc(b.title) + step +
              (b.stepCount ? '<button type="button" title="Previous step" data-step="' + esc(b.id) + '" data-to="' + (b.currentStep - 1) + '">&lsaquo;</button><button type="button" title="Next step" data-step="' + esc(b.id) + '" data-to="' + (b.currentStep + 1) + '">&rsaquo;</button>' : '') +
              '<button type="button" title="Remove" data-remove="' + esc(b.id) + '">&times;</button></span>';
          }).join('') + '</div>';
        }
        html += '<div style="display:flex;justify-content:space-between;align-items:center;margin-top:12px"><p class="eyebrow">PlanResult</p><button type="button" class="copy" data-copy="plan">Copy JSON</button></div>';
        html += '<div class="json" id="planjson">' + highlightJson(plan) + '</div>';
      }
    } else if (tab === 'placed') {
      html += '<h3>Where the layout solver put each element</h3><p class="muted">The plan says <em>what</em> and <em>near what</em>. The solver decides <em>where</em>. Select a row to look at its anchor.</p><div class="rows">';
      var any = false;
      (last.plan ? last.plan.ops : []).forEach(function (op) {
        if (op.op !== 'add' || !op.build) return;
        op.build.elements.forEach(function (e) {
          var pl = last.placements[op.build.id + '/' + e.id];
          any = true;
          html += '<button type="button" class="row" data-focus="' + esc(e.anchor.entity || '') + '"><span class="t">' + esc(e.id) + '</span><span class="tag' + (e.style === 'warning' ? ' warn' : '') + '">' + esc(e.type) + '</span>' +
            '<span class="s">' + esc(e.anchor.entity || 'user') + ' &middot; ' + esc(e.anchor.at) + (e.step ? ' &middot; step ' + e.step : '') + (pl ? '<br>&rarr; ' + fmt(pl.position) + ' m' : '') + '</span></button>';
        });
      });
      if (!any) html += '<p class="muted">This plan added nothing, so there is nothing to place.</p>';
      html += '</div>';
    } else {
      html += '<h3>What the device knows about the room</h3><p class="muted">Surfaces come from the device. Equipment parts and fixtures come from the registered site, bound to real furniture where the room has it. Select one to find it.</p><div class="rows">';
      DATA.room.entities.forEach(function (e) {
        var size = e.size ? e.size.map(function (n) { return (Math.round(n * 100) / 100); }).join(' &times; ') : '';
        html += '<button type="button" class="row" data-focus="' + esc(e.id) + '"><span class="t">' + esc(e.label || e.id) + '</span><span class="tag ' + (e.virtual ? '' : 'plain') + '">' + esc(e.kind) + '</span>' +
          '<span class="s">' + esc(e.id) + ' &middot; ' + size + ' m' + (e.virtual ? ' &middot; stand-in hologram' : ' &middot; from the device') + '</span></button>';
      });
      html += '</div>';
    }
    pane.innerHTML = html;
  }

  function copyText(text, button) {
    var done = function () { var old = button.textContent; button.textContent = 'Copied'; setTimeout(function () { button.textContent = old; }, 1400); };
    var fallback = function () {
      var area = document.createElement('textarea');
      area.value = text; area.setAttribute('readonly', ''); area.style.position = 'fixed'; area.style.opacity = '0';
      document.body.appendChild(area); area.select();
      try { document.execCommand('copy'); done(); } catch (e) { button.textContent = 'Select and copy'; }
      document.body.removeChild(area);
    };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, fallback);
    else fallback();
  }

  // ---- film table --------------------------------------------------------------------------

  var FILM = [
    ['Build me a walkthrough for this pump', 'Walkthrough', 'pin, card, gizmo, highlight', 'The isolation procedure: three steps, each tied to a part.'],
    ['Show me how the water flows', 'Live flow', 'flow, label', 'The flow path and wetted parts recorded for the pump.'],
    ['Has it failed before?', 'Fault history', 'highlight, pin, label', 'Two fault records from last week.'],
    ["Put this month's output by the bench", 'Dashboard', 'chart', 'The output metric series; "by the bench" becomes a floor-near anchor.'],
    ['What parts do I need?', 'Checklist', 'checklist', 'The spares list and who is on the job.'],
    ['Bring Sara in', 'Shared session', 'avatar', 'The people directory. With a second device linked, the marker follows that person.']
  ];

  function renderFilm() {
    $('filmrows').innerHTML = FILM.map(function (r) {
      return '<tr><td class="q">"' + esc(r[0]) + '"</td><td>' + esc(r[1]) + '</td><td>' + r[2].split(', ').map(function (t) { return '<code>' + t + '</code>'; }).join(' ') + '</td><td>' + esc(r[3]) + '</td><td><button type="button" class="run" data-prompt="' + esc(r[0]) + '" data-scroll="1">Run</button></td></tr>';
    }).join('');
  }

  // ---- pipeline ----------------------------------------------------------------------------

  var STAGES = [
    { t: 'Hear', d: 'Voice or text', body: 'A request arrives as text: spoken, typed on the system keyboard, tapped from a suggestion, or sent from a linked device. Whatever the user is pointing at travels with it as the <em>focus</em>, so "what is this?" has a subject.', files: ['Runtime/Unity/App/PromptBar.cs', 'Runtime/Unity/Platform/PlatformServices.cs (ISpeechInput, ITextEntry)', 'Runtime/Unity/Interaction/Interaction.cs (focus entity)'], live: function () { return { prompt: last.prompt, focus: null }; } },
    { t: 'Know the room', d: 'Room model', body: 'The device contributes surfaces and furniture (floor, walls, tables). The registered site contributes the equipment, its parts and its fixtures. The binder merges them into one room model, binding fixtures to real furniture when it exists and to stand-in holograms when it does not.', files: ['Runtime/Core/Room/RoomModel.cs', 'Runtime/Core/Site/SiteBinder.cs', 'Runtime/Resources/Spatail/pump_station.json'], live: function () { var k = {}; DATA.room.entities.forEach(function (e) { k[e.kind] = (k[e.kind] || 0) + 1; }); return { entities: DATA.room.entities.length, byKind: k, user: DATA.room.user }; } },
    { t: 'Plan', d: 'AI or on-device', body: 'The planner turns the request, the room and the site\'s knowledge into a plan. The router sends it to the AI planner service when one is configured, answers step navigation on-device so it stays instant, and falls back to the on-device planner if the service fails.', files: ['Runtime/Core/Planning/RemotePlanner.cs (PlannerRouter)', 'Runtime/Core/Planning/OfflinePlanner.cs', 'services/planner/src/planner.ts'], live: function () { return last.plan ? { planner: last.plan.planner || 'docs router', latencyMs: last.plan.latencyMs, reply: last.plan.reply } : {}; } },
    { t: 'Validate', d: 'Plan to builds', body: 'Every plan, whatever produced it, is sanitised against the live room: unknown element types and anchors to things that are not there are dropped, ids are filled in, scales are clamped. Then it is applied to the build store, the single source of truth for what is in the room.', files: ['Runtime/Core/Spec/SpecJson.cs (Sanitize)', 'Runtime/Core/Spec/BuildStore.cs'], live: function () { return last.plan ? { ops: last.plan.ops.map(function (o) { return o.op + (o.build ? ':' + o.build.id + ' (' + o.build.elements.length + ' elements)' : (o.buildId ? ':' + o.buildId : '')); }) } : {}; } },
    { t: 'Place', d: 'Layout solver', body: 'Anchors become poses. "floor-near" looks for free floor beside the entity on the user\'s side and fans out until nothing is in the way. "callout" sits beside a part without covering it. "wall" projects onto the nearest wall at eye height. Everything turns to face where the user stands.', files: ['Runtime/Core/Layout/LayoutSolver.cs'], live: function () { var out = {}; Object.keys(last.placements || {}).slice(0, 4).forEach(function (k) { out[k] = last.placements[k].position; }); return out; } },
    { t: 'Render and share', d: 'Views and session', body: 'A view is created per element and animated in; step changes show and hide views. The same plan is sent to anyone in the session, with poses expressed relative to the equipment so two devices that have each aligned the equipment agree on where things are.', files: ['Runtime/Unity/ContentRenderer.cs', 'Runtime/Unity/Elements/*.cs', 'Runtime/Core/Session/SessionLink.cs'], live: function () { return { builds: builds().map(function (b) { return b.id + (b.stepCount ? ' ' + b.currentStep + '/' + b.stepCount : ''); }) }; } }
  ];
  var stageIndex = 0;

  function renderPipe() {
    $('pipe').innerHTML = STAGES.map(function (s, i) {
      return '<button type="button" class="stagebtn" role="tab" aria-selected="' + (i === stageIndex) + '" data-stage="' + i + '"><span class="n">' + (i + 1) + '</span><span class="t">' + s.t + '</span><span class="d">' + s.d + '</span></button>';
    }).join('');
    renderStage();
  }

  function renderStage() {
    var s = STAGES[stageIndex];
    if (!$('stagedetail')) return;
    $('stagedetail').innerHTML = '<div class="stack"><h3>' + s.t + '</h3><p>' + s.body + '</p><p class="eyebrow" style="margin-top:6px">In the code <span class="muted" style="text-transform:none;letter-spacing:0">(under packages/com.spatail.core unless noted)</span></p><div class="files">' + s.files.map(function (f) { return '<code>' + esc(f) + '</code>'; }).join('') + '</div></div>' +
      '<div class="stack"><p class="eyebrow">For the last request</p><div class="json" style="margin-top:0">' + highlightJson(s.live()) + '</div></div>';
  }

  // ---- element reference -------------------------------------------------------------------

  var ELEMENTS = [
    { type: 'label', what: 'A pill of text tethered to an entity by a short leader.', fields: 'text, style', anchor: 'top', demo: 'Label everything' },
    { type: 'pin', what: 'A numbered marker standing above an entity. Selecting it jumps a walkthrough to that step.', fields: 'number, text, style', anchor: 'top', demo: 'Build me a walkthrough for this pump' },
    { type: 'card', what: 'An instruction card for one step, with Back and Next.', fields: 'number, title, body, step', anchor: 'callout', demo: 'Build me a walkthrough for this pump' },
    { type: 'gizmo', what: 'A motion cue on a part: a circular arrow that turns the way the part should be turned. Centres on the part\'s action point.', fields: 'action (turn-cw, turn-ccw), step', anchor: 'top', demo: 'Walk me through the start-up check' },
    { type: 'flow', what: 'Chevrons streaming along a path through the equipment, with the wetted parts lit.', fields: 'points, radius, entities, text', anchor: 'center (on the equipment)', demo: 'Show me how the water flows' },
    { type: 'highlight', what: 'A pulsing tinted volume hugging an entity: a cylinder for round parts, a box otherwise.', fields: 'style, title, body', anchor: 'center', demo: 'Has it failed before?' },
    { type: 'chart', what: 'A 3D bar chart with a trend line on a base slab. Bars rise in sequence.', fields: 'title, unit, values, labels', anchor: 'floor-near or surface', demo: "Put this month's output by the bench" },
    { type: 'checklist', what: 'A tickable list with the initials of the people on the job.', fields: 'title, items[{text, done}], people, more', anchor: 'above or wall', demo: 'What parts do I need?' },
    { type: 'avatar', what: 'A presence marker for a collaborator. Follows the real person when their device is in the session.', fields: 'name, text (initial)', anchor: 'floor-near', demo: 'Bring Sara in' },
    { type: 'panel', what: 'A title and a short paragraph, for answers that are explanations.', fields: 'title, body', anchor: 'callout, above, wall or user', demo: 'What is the gate valve?' }
  ];
  var ANCHORS = [
    ['center', 'The centre of the entity\'s bounds.'], ['top', 'On top of the entity\'s bounds.'], ['above', 'Floating clear above the entity.'],
    ['callout', 'Beside the entity on the user\'s side, below its centre so it does not cover it.'], ['floor-near', 'On free floor next to the entity, clear of everything else that stands on the floor.'],
    ['surface', 'Standing on the entity\'s top surface.'], ['wall', 'On the nearest wall to the entity, at eye height.'], ['user', 'In front of the user, for content that is about nothing in particular.']
  ];
  var elIndex = 0;

  function renderElements() {
    $('elpicker').innerHTML = ELEMENTS.map(function (e, i) { return '<button type="button" aria-pressed="' + (i === elIndex) + '" data-el="' + i + '">' + e.type + '</button>'; }).join('');
    var e = ELEMENTS[elIndex];
    var sample = null;
    DATA.library.some(function (entry) {
      return entry.plan.ops.some(function (op) {
        if (!op.build) return false;
        return op.build.elements.some(function (x) { if (x.type === e.type) { sample = x; return true; } return false; });
      });
    });
    $('eldetail').innerHTML = '<h3><code>' + e.type + '</code></h3><p>' + esc(e.what) + '</p>' +
      '<dl class="kv"><dt>fields</dt><dd>' + esc(e.fields) + '</dd><dt>usual anchor</dt><dd>' + esc(e.anchor) + '</dd></dl>' +
      (sample ? '<div class="json" style="margin-top:0;max-height:240px">' + highlightJson(sample) + '</div>' : '') +
      '<div><button type="button" class="send" data-prompt="' + esc(e.demo) + '" data-scroll="1">Show one in the room</button></div>';
    $('anchorrows').innerHTML = ANCHORS.map(function (a) { return '<tr><td><code>' + a[0] + '</code></td><td>' + esc(a[1]) + '</td></tr>'; }).join('');
  }

  // ---- apps --------------------------------------------------------------------------------

  var APPS = window.SPATAIL_APPS || [];

  function renderApps() {
    $('appcards').innerHTML = APPS.map(function (app) {
      var steps = app.steps.map(function (s, i) {
        var key = 'spatail.docs.' + app.id + '.' + i;
        var id = 'step-' + app.id + '-' + i;
        return '<li><input type="checkbox" id="' + id + '" data-key="' + key + '"' + (store.get(key) === '1' ? ' checked' : '') + '><label for="' + id + '">' + s.text +
          (s.cmd ? '<span class="cmd"><pre><code>' + esc(s.cmd) + '</code></pre><button type="button" class="copy" data-copytext="' + esc(s.cmd) + '">Copy</button></span>' : '') + '</label></li>';
      }).join('');
      return '<div class="card"><span class="eyebrow accent">' + esc(app.platform) + '</span><h3 style="font-size:1.25rem">' + esc(app.name) + '</h3><p>' + app.summary + '</p>' +
        '<p class="eyebrow" style="margin-top:6px">In the app</p><ul class="plain">' + app.features.map(function (f) { return '<li>' + f + '</li>'; }).join('') + '</ul>' +
        '<p class="eyebrow" style="margin-top:6px">Controls</p><div class="tablewrap"><table style="min-width:0"><tbody>' + app.controls.map(function (c) { return '<tr><td style="white-space:nowrap">' + c[0] + '</td><td>' + c[1] + '</td></tr>'; }).join('') + '</tbody></table></div>' +
        '<p class="eyebrow" style="margin-top:6px">From clone to device</p><ol class="steps">' + steps + '</ol>' +
        (app.note ? '<p class="note warn">' + app.note + '</p>' : '') + '</div>';
    }).join('');
  }

  // ---- planner section ---------------------------------------------------------------------

  function renderPlanner() {
    var cmds = ['cd services/planner', 'npm install', 'npm run build', 'ANTHROPIC_API_KEY=... npm start'];
    $('plannercmds').innerHTML = cmds.map(function (c) { return '<div class="cmd"><pre><code>' + esc(c) + '</code></pre><button type="button" class="copy" data-copytext="' + esc(c) + '">Copy</button></div>'; }).join('') +
      '<p class="muted" style="font-size:13.5px;margin-top:10px">Try it with no device, against the simulated room:</p><div class="cmd"><pre><code>ANTHROPIC_API_KEY=... npm run plan -- "label every part and explain the valve"</code></pre><button type="button" class="copy" data-copytext="ANTHROPIC_API_KEY=... npm run plan -- &quot;label every part and explain the valve&quot;">Copy</button></div>';
    var sample = {
      'request: POST /v1/plan': {
        prompt: 'Has it failed before?',
        focus: 'motor',
        site: { name: 'Pump P-101', equipment: 'pump-p101' },
        room: { floorY: 0, user: '{position, forward}', entities: '[' + DATA.room.entities.length + ' entities]' },
        knowledge: '{procedures, flows, faults, metrics, spares, people}',
        builds: '[what is already in the room]'
      },
      'response: 200': { reply: 'Two faults last week, on the motor and the coupling.', chip: 'Fault history', ops: '[...]', planner: 'claude-opus-5-5', latencyMs: 2100 }
    };
    $('requestsample').textContent = JSON.stringify(sample, null, 2);
  }

  // ---- site anatomy ------------------------------------------------------------------------

  var partIndex = 0;
  function parts() { return DATA.room.entities.filter(function (e) { return e.kind === 'part' || e.kind === 'fixture' || e.kind === 'table'; }); }

  function renderParts(focus) {
    var list = parts();
    $('partpicker').innerHTML = list.map(function (e, i) { return '<button type="button" aria-pressed="' + (i === partIndex) + '" data-part="' + i + '">' + esc(e.id) + '</button>'; }).join('');
    var e = list[partIndex];
    if (!e) return;
    var view = { id: e.id, label: e.label, kind: e.kind, aliases: e.aliases, about: e.about, size: e.size };
    if (e.actionAxis) view.actionAxis = e.actionAxis;
    if (e.shapes) view.shapes = e.shapes.slice(0, 3).concat(e.shapes.length > 3 ? ['... ' + (e.shapes.length - 3) + ' more primitives'] : []);
    $('partjson').textContent = JSON.stringify(view, null, 2);
    if (focus && sim) sim.focus(e.id);
  }

  // ---- project map + status ----------------------------------------------------------------

  function renderTree() {
    var rows = [
      ['<b>packages/com.spatail.core/</b>', 'the shared Unity package both apps use'],
      ['  Runtime/Core/', 'engine-independent C#: room model, spec, layout solver, planners, session link'],
      ['  Runtime/Unity/', 'rendering, interaction, prompt bar, SpatailApp, desktop simulator'],
      ['  Runtime/Resources/Spatail/', 'pump_station.json, shaders, baked Inter font atlases'],
      ['<b>apps/spatail-quest/</b>', 'Unity project: Meta Quest 3 / 3S passthrough app (Meta XR SDK, MR Utility Kit)'],
      ['<b>apps/spatail-devtool-ios/</b>', 'Unity project: iPhone / iPad dev tool (AR Foundation, ARKit)'],
      ['<b>services/planner/</b>', 'Node service that plans with Claude (official Anthropic SDK)'],
      ['<b>tests/Spatail.Core.Tests/</b>', 'dotnet tests for the core, and the exporter that feeds this page'],
      ['<b>tools/UnityCompileCheck/</b>', 'type-checks the Unity runtime without a Unity install'],
      ['<b>tools/fontgen/</b>', 'bakes the signed-distance-field font atlases'],
      ['<b>docs/</b>', 'this guide and the browser simulator']
    ];
    var width = 0;
    rows.forEach(function (r) { width = Math.max(width, r[0].replace(/<[^>]+>/g, '').length); });
    $('tree').innerHTML = rows.map(function (r) {
      var plain = r[0].replace(/<[^>]+>/g, '');
      return r[0] + new Array(width - plain.length + 4).join(' ') + '<span class="c">' + esc(r[1]) + '</span>';
    }).join('\n');
  }

  var STATUS = window.SPATAIL_STATUS || [];
  function renderStatus() {
    $('statusrows').innerHTML = STATUS.map(function (r) {
      var cls = r[1] === 'Tested' ? 'ok' : (r[1] === 'Compile-checked' || r[1] === 'Checked against SDK source' ? '' : 'warn');
      return '<tr><td class="q">' + esc(r[0]) + '</td><td><span class="tag ' + cls + '">' + esc(r[1]) + '</span></td><td>' + r[2] + '</td></tr>';
    }).join('');
  }

  // ---- events ------------------------------------------------------------------------------

  document.addEventListener('click', function (ev) {
    var t = ev.target.closest('button, a');
    if (!t) return;
    if (t.dataset.prompt) {
      submit(t.dataset.prompt);
      if (t.dataset.scroll) $('try').scrollIntoView({ block: 'start' });
    } else if (t.dataset.tab) {
      tab = t.dataset.tab;
      Array.prototype.forEach.call(t.parentNode.children, function (b) { b.setAttribute('aria-selected', String(b === t)); });
      renderPane();
    } else if (t.dataset.twin) {
      if (sim) sim.setTwinMode(t.dataset.twin);
      Array.prototype.forEach.call(t.parentNode.children, function (b) { b.setAttribute('aria-pressed', String(b === t)); });
    } else if (t.id === 'frame') {
      if (sim) sim.focus(null);
    } else if (t.id === 'resetroom') {
      if (sim) { sim.reset(); showScan(); }
      usedPrompts = {};
      say('<span class="muted">The room has been scanned: five things recognised. Pick a request below or type your own.</span>', '', true);
      renderPane(); renderChips(); renderStage();
    } else if (t.dataset.focus !== undefined) {
      if (sim && t.dataset.focus) sim.focus(t.dataset.focus);
    } else if (t.dataset.step) {
      if (sim) sim.setStep(t.dataset.step, parseInt(t.dataset.to, 10));
      renderPane(); renderChips();
    } else if (t.dataset.remove) {
      if (sim) sim.applyPlan({ ops: [{ op: 'remove', buildId: t.dataset.remove }] });
      renderPane(); renderChips();
    } else if (t.dataset.copy === 'plan') {
      copyText(JSON.stringify(last.plan, null, 2), t);
    } else if (t.dataset.copytext) {
      copyText(t.dataset.copytext, t);
    } else if (t.dataset.stage) {
      stageIndex = parseInt(t.dataset.stage, 10);
      renderPipe();
    } else if (t.dataset.el) {
      elIndex = parseInt(t.dataset.el, 10);
      renderElements();
    } else if (t.dataset.part) {
      partIndex = parseInt(t.dataset.part, 10);
      renderParts(true);
    }
  });

  document.addEventListener('change', function (ev) {
    var t = ev.target;
    if (t.dataset && t.dataset.key) store.set(t.dataset.key, t.checked ? '1' : '0');
  });

  $('promptform').addEventListener('submit', function (ev) {
    ev.preventDefault();
    submit($('prompt').value);
  });

  // Mark the section in view in the top navigation.
  var links = Array.prototype.slice.call(document.querySelectorAll('.nav a'));
  if ('IntersectionObserver' in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (!en.isIntersecting) return;
        links.forEach(function (a) { a.classList.toggle('on', a.getAttribute('href') === '#' + en.target.id); });
      });
    }, { rootMargin: '-45% 0px -50% 0px' });
    Array.prototype.forEach.call(document.querySelectorAll('main > section'), function (s) { io.observe(s); });
  }

  // ---- boot --------------------------------------------------------------------------------

  bootSim();
  renderChips();
  renderPane();
  renderFilm();
  renderPipe();
  renderElements();
  renderApps();
  renderPlanner();
  renderParts(false);
  renderTree();
  renderStatus();
})();
